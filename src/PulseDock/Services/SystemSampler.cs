using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading;
using PulseDock.Models;

namespace PulseDock.Services
{
    /// <summary>
    /// Periodic sampling on a dedicated thread. CPU, memory, disk and network use Windows APIs
    /// (no admin); GPUs come from <see cref="GpuSensors"/> (no driver); optional CPU/disk temperatures from <see cref="HardwareSensors"/>.
    /// </summary>
    public sealed class SystemSampler : IDisposable
    {
        private readonly HardwareSensors _hw = new();
        private readonly GpuSensors _gpu = new();
        private volatile bool _advanced;
        private bool _hwTried;
        private readonly AutoResetEvent _wake = new(false);
        private Thread? _thread;
        private volatile bool _stop;
        private volatile int _intervalMs = 1000;
        private volatile string _diskLetter = "C";
        private volatile bool _diskChanged = true;

        // CPU
        private PerformanceCounter? _cpuUtility;
        private PerformanceCounter? _cpuPerformance;
        private PerformanceCounter? _cpuFrequency;
        private long _lastIdle, _lastKernel, _lastUser;

        // Disk
        private PerformanceCounter? _diskIdle;
        private PerformanceCounter? _diskRead;
        private PerformanceCounter? _diskWrite;

        // Network
        private NetworkInterface[] _nics = Array.Empty<NetworkInterface>();
        private readonly Dictionary<string, (long rx, long tx)> _lastNic = new();
        private int _nicRefreshCountdown;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private double _lastNetSeconds;

        public event Action<Snapshot>? Sampled;

        public int IntervalMs
        {
            get => _intervalMs;
            set { _intervalMs = Math.Clamp(value, 250, 10000); _wake.Set(); }
        }

        public string DiskLetter
        {
            get => _diskLetter;
            set
            {
                var v = string.IsNullOrWhiteSpace(value) ? "C" : value.Trim().TrimEnd(':').ToUpperInvariant();
                if (v != _diskLetter) { _diskLetter = v; _diskChanged = true; }
            }
        }

        /// <summary>Turns on the advanced sensors (WinRing0 driver). Only takes effect as administrator.</summary>
        public bool AdvancedSensors
        {
            get => _advanced;
            set { _advanced = value; _wake.Set(); }
        }

        /// <summary>Diagnostics: read GPU load/memory counters (set before Start).</summary>
        public bool GpuCounters
        {
            get => _gpu.CountersEnabled;
            set => _gpu.CountersEnabled = value;
        }

        /// <summary>Diagnostics: GPU temperature "auto" / "always" / "off".</summary>
        public string GpuTemperatureMode
        {
            get => _gpu.TemperatureMode;
            set => _gpu.TemperatureMode = string.IsNullOrWhiteSpace(value) ? "auto" : value.Trim().ToLowerInvariant();
        }

        public void Start()
        {
            _thread = new Thread(Run) { IsBackground = true, Name = "SystemSampler", Priority = ThreadPriority.BelowNormal };
            _thread.Start();
        }

        private void Run()
        {
            InitCpuCounters();
            _gpu.Open();

            while (!_stop)
            {
                var started = _clock.ElapsedMilliseconds;
                try
                {
                    var snap = Sample();
                    Sampled?.Invoke(snap);
                }
                catch (Exception ex)
                {
                    Log.Error("Sampler", ex);
                }
                var wait = _intervalMs - (int)(_clock.ElapsedMilliseconds - started);
                try { _wake.WaitOne(Math.Max(100, wait)); }
                catch (ObjectDisposedException) { break; }
            }
        }

        private Snapshot Sample()
        {
            var s = new Snapshot();
            SampleCpu(s);
            SampleMemory(s);
            SampleDisk(s);
            SampleNetwork(s);
            s.Gpus = _gpu.Read();

            // Advanced sensors turn on/off without restarting the app.
            if (_advanced && !_hw.IsOpen && !_hwTried) { _hwTried = true; _hw.Open(); }
            else if (!_advanced) { _hwTried = false; if (_hw.IsOpen) _hw.Dispose(); }
            if (_hw.IsOpen) _hw.Read(s);
            return s;
        }

        // ---------------- CPU ----------------

        private void InitCpuCounters()
        {
            try
            {
                // "% Processor Utility" is the same number Task Manager shows.
                _cpuUtility = new PerformanceCounter("Processor Information", "% Processor Utility", "_Total", true);
                _cpuUtility.NextValue();
            }
            catch (Exception ex)
            {
                Log.Error("CPU utility counter", ex);
                _cpuUtility = null;
            }

            try
            {
                _cpuPerformance = new PerformanceCounter("Processor Information", "% Processor Performance", "_Total", true);
                _cpuFrequency = new PerformanceCounter("Processor Information", "Processor Frequency", "_Total", true);
                _cpuPerformance.NextValue();
                _cpuFrequency.NextValue();
            }
            catch (Exception ex)
            {
                Log.Error("CPU frequency counters", ex);
                _cpuPerformance = null;
                _cpuFrequency = null;
            }

            Native.GetSystemTimes(out _lastIdle, out _lastKernel, out _lastUser);
        }

        private void SampleCpu(Snapshot s)
        {
            double load;
            if (_cpuUtility != null)
            {
                load = SafeNext(_cpuUtility);
            }
            else
            {
                // Fallback: GetSystemTimes (kernel includes idle).
                Native.GetSystemTimes(out var idle, out var kernel, out var user);
                long dIdle = idle - _lastIdle, dKernel = kernel - _lastKernel, dUser = user - _lastUser;
                _lastIdle = idle; _lastKernel = kernel; _lastUser = user;
                long total = dKernel + dUser;
                load = total > 0 ? (total - dIdle) * 100.0 / total : 0;
            }
            s.CpuLoad = Math.Clamp(load, 0, 100);

            if (_cpuPerformance != null && _cpuFrequency != null)
            {
                double mhz = SafeNext(_cpuFrequency);
                double perf = SafeNext(_cpuPerformance);
                if (mhz > 0 && perf > 0) s.CpuGhz = mhz * perf / 100.0 / 1000.0;
            }
        }

        // ---------------- Memory ----------------

        private static void SampleMemory(Snapshot s)
        {
            var m = new Native.MEMORYSTATUSEX { dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Native.MEMORYSTATUSEX>() };
            if (Native.GlobalMemoryStatusEx(ref m))
            {
                s.MemoryTotalBytes = m.ullTotalPhys;
                s.MemoryUsedBytes = m.ullTotalPhys - m.ullAvailPhys;
                s.MemoryLoad = m.ullTotalPhys > 0 ? s.MemoryUsedBytes * 100.0 / m.ullTotalPhys : 0;
            }
        }

        // ---------------- Disk ----------------

        private void InitDiskCounters()
        {
            _diskChanged = false;
            DisposeDiskCounters();
            var letter = _diskLetter + ":";

            string? category = null, instance = null;
            try
            {
                var phys = new PerformanceCounterCategory("PhysicalDisk").GetInstanceNames();
                instance = phys.FirstOrDefault(n => n.Split(' ').Contains(letter, StringComparer.OrdinalIgnoreCase));
                if (instance != null) category = "PhysicalDisk";
            }
            catch (Exception ex)
            {
                Log.Error("PhysicalDisk instances", ex);
            }

            if (category == null)
            {
                category = "LogicalDisk";
                instance = letter;
            }

            try
            {
                _diskIdle = new PerformanceCounter(category, "% Idle Time", instance, true);
                _diskRead = new PerformanceCounter(category, "Disk Read Bytes/sec", instance, true);
                _diskWrite = new PerformanceCounter(category, "Disk Write Bytes/sec", instance, true);
                _diskIdle.NextValue(); _diskRead.NextValue(); _diskWrite.NextValue();
            }
            catch (Exception ex)
            {
                Log.Error("Disk counters " + category + "/" + instance, ex);
                DisposeDiskCounters();
            }
        }

        private void SampleDisk(Snapshot s)
        {
            if (_diskChanged) InitDiskCounters();
            s.DiskName = _diskLetter + ":";
            if (_diskIdle == null || _diskRead == null || _diskWrite == null) return;
            s.DiskActive = Math.Clamp(100 - SafeNext(_diskIdle), 0, 100);
            s.DiskReadBps = Math.Max(0, SafeNext(_diskRead));
            s.DiskWriteBps = Math.Max(0, SafeNext(_diskWrite));
        }

        private void DisposeDiskCounters()
        {
            _diskIdle?.Dispose(); _diskRead?.Dispose(); _diskWrite?.Dispose();
            _diskIdle = _diskRead = _diskWrite = null;
        }

        // ---------------- Network ----------------

        private void SampleNetwork(Snapshot s)
        {
            if (--_nicRefreshCountdown <= 0)
            {
                _nicRefreshCountdown = 10;
                try
                {
                    _nics = NetworkInterface.GetAllNetworkInterfaces()
                        .Where(n => n.OperationalStatus == OperationalStatus.Up
                                    && n.NetworkInterfaceType != NetworkInterfaceType.Loopback
                                    && n.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
                        .ToArray();
                }
                catch (Exception ex)
                {
                    Log.Error("NICs", ex);
                    _nics = Array.Empty<NetworkInterface>();
                }
            }

            double now = _clock.Elapsed.TotalSeconds;
            double dt = now - _lastNetSeconds;
            _lastNetSeconds = now;

            double down = 0, up = 0, best = -1;
            string bestName = "";
            foreach (var nic in _nics)
            {
                try
                {
                    var st = nic.GetIPStatistics();
                    long rx = st.BytesReceived, tx = st.BytesSent;
                    if (_lastNic.TryGetValue(nic.Id, out var prev) && dt > 0)
                    {
                        double dRx = Math.Max(0, rx - prev.rx) / dt;
                        double dTx = Math.Max(0, tx - prev.tx) / dt;
                        down += dRx; up += dTx;
                        if (dRx + dTx > best) { best = dRx + dTx; bestName = nic.Name; }
                    }
                    else if (best < 0)
                    {
                        bestName = nic.Name;
                    }
                    _lastNic[nic.Id] = (rx, tx);
                }
                catch
                {
                    // The interface may have gone down between listing and reading.
                }
            }

            s.NetDownBps = down;
            s.NetUpBps = up;
            s.NetworkName = bestName;
        }

        private static double SafeNext(PerformanceCounter c)
        {
            try
            {
                var v = c.NextValue();
                return float.IsNaN(v) || float.IsInfinity(v) ? 0 : v;
            }
            catch
            {
                return 0;
            }
        }

        public void Dispose()
        {
            _stop = true;
            _wake.Set();
            _thread?.Join(2000);
            // Still opening sensors (can take a few seconds): let process exit clean up.
            if (_thread != null && _thread.IsAlive) return;
            _cpuUtility?.Dispose(); _cpuPerformance?.Dispose(); _cpuFrequency?.Dispose();
            DisposeDiskCounters();
            _hw.Dispose();
            _gpu.Dispose();
            _wake.Dispose();
        }
    }
}
