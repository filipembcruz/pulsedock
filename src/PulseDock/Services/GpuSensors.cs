using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using PulseDock.Models;
using LayoutKind = System.Runtime.InteropServices.LayoutKind;

namespace PulseDock.Services
{
    /// <summary>
    /// GPUs without an extra driver: the same sources as Task Manager.
    /// - List, name, total VRAM and temperature: D3DKMT (gdi32), per adapter.
    /// - Load: "\GPU Engine(*)\Utilization Percentage" via PDH (busiest engine, summed across processes).
    /// - Memory in use: "\GPU Adapter Memory(*)\..." via PDH.
    ///
    /// Hybrid laptops (NVIDIA Optimus / AMD switchable): the dedicated GPU sleeps when idle.
    /// Asking it for its temperature (KMTQAITYPE_ADAPTERPERFDATA) wakes it up, and every power-up
    /// stalls the graphics kernel for a moment. So the dedicated GPU's temperature is only read
    /// while that GPU is already busy (load above IdleThreshold), and at most every TempIntervalSec.
    /// Use only on the sampler thread.
    /// </summary>
    public sealed class GpuSensors : IDisposable
    {
        private sealed class Adapter
        {
            public uint Handle;
            public uint LuidLow;
            public int LuidHigh;
            public string Name = "GPU";
            public bool Integrated;
            public ulong DedicatedBytes;
            public bool TempSupported = true;
            public double? LastTemp;
            public DateTime LastTempRead = DateTime.MinValue;
            public int TempFailures;
        }

        private const string EnginePath = @"\GPU Engine(*)\Utilization Percentage";
        private const string DedicatedPath = @"\GPU Adapter Memory(*)\Dedicated Usage";
        private const string SharedPath = @"\GPU Adapter Memory(*)\Shared Usage";
        private const double IdleThreshold = 2.0;   // % load below which a dedicated GPU counts as idle
        private const double TempIntervalSec = 5.0;

        private readonly List<Adapter> _adapters = new();
        private PdhQuery? _pdh;

        /// <summary>Diagnostics: read GPU load/memory counters at all.</summary>
        public bool CountersEnabled { get; set; } = true;

        /// <summary>Diagnostics: "auto" (default, only when active), "always" or "off".</summary>
        public string TemperatureMode { get; set; } = "auto";

        private static readonly Regex EngineRx = new(
            @"luid_0x([0-9a-fA-F]+)_0x([0-9a-fA-F]+)_phys_(\d+)_eng_(\d+)_engtype_",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex MemoryRx = new(
            @"luid_0x([0-9a-fA-F]+)_0x([0-9a-fA-F]+)_phys_(\d+)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public void Open()
        {
            try
            {
                EnumerateAdapters();
                Log.Info("GPUs: " + string.Join(", ", _adapters.Select(a => $"{a.Name} ({(a.Integrated ? "integrated" : "dedicated")}, {a.DedicatedBytes / (1024 * 1024)} MB)")));
            }
            catch (Exception ex)
            {
                Log.Error("GpuSensors.Enumerate", ex);
            }

            if (!CountersEnabled) return;
            try
            {
                _pdh = PdhQuery.Create(EnginePath, DedicatedPath, SharedPath);
                if (_pdh != null) DropAdaptersWithoutEngines();
            }
            catch (Exception ex)
            {
                Log.Error("GpuSensors.Counters", ex);
                _pdh = null;
            }
        }

        /// <summary>Virtual monitors (e.g. remote access apps) show up as adapters with no engines: remove them.</summary>
        private void DropAdaptersWithoutEngines()
        {
            if (_pdh == null || !_pdh.Has(EnginePath)) return;
            _pdh.Collect();
            var names = _pdh.Read(EnginePath).Select(x => x.instance).ToList();
            if (names.Count == 0) return;
            bool HasEngines(Adapter a)
            {
                string tag = $"luid_0x{unchecked((uint)a.LuidHigh):X8}_0x{a.LuidLow:X8}";
                return names.Any(n => n.IndexOf(tag, StringComparison.OrdinalIgnoreCase) >= 0);
            }
            var keep = _adapters.Where(HasEngines).ToList();
            if (keep.Count == 0 || keep.Count == _adapters.Count) return;
            foreach (var a in _adapters.Except(keep)) Close(a.Handle);
            _adapters.Clear();
            _adapters.AddRange(keep);
        }

        public List<GpuReading> Read()
        {
            var result = new List<GpuReading>();
            if (_adapters.Count == 0) return result;

            bool counters = CountersEnabled && _pdh != null && _pdh.Collect();
            var load = counters ? ReadEngineLoad() : new Dictionary<(int, uint), double>();
            var mem = counters ? ReadMemory() : new Dictionary<(int, uint), (double dedicated, double shared)>();

            foreach (var a in _adapters)
            {
                var key = (a.LuidHigh, a.LuidLow);
                double? l = load.TryGetValue(key, out var lv) ? Math.Clamp(lv, 0, 100) : (counters ? 0d : (double?)null);
                var g = new GpuReading
                {
                    Name = a.Name,
                    Integrated = a.Integrated,
                    Load = l,
                    // Without counters we can't tell if it's awake, so never call it idle.
                    Idle = !a.Integrated && counters && (l ?? 0) < IdleThreshold
                };
                g.Temperature = ReadTemperature(a, g.Idle, counters);
                if (mem.TryGetValue(key, out var m))
                {
                    g.MemoryUsedMb = (a.Integrated ? m.shared : m.dedicated) / (1024.0 * 1024.0);
                }
                if (!a.Integrated && a.DedicatedBytes > 0) g.MemoryTotalMb = a.DedicatedBytes / (1024.0 * 1024.0);
                result.Add(g);
            }

            return result
                .OrderByDescending(g => g.Integrated) // integrated = GPU 0
                .ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        // ---------------- D3DKMT ----------------

        private void EnumerateAdapters()
        {
            var e = new D3DKMT_ENUMADAPTERS2();
            if (D3DKMTEnumAdapters2(ref e) != 0 || e.NumAdapters == 0) return;

            int size = Marshal.SizeOf<D3DKMT_ADAPTERINFO>();
            e.pAdapters = Marshal.AllocHGlobal(size * (int)e.NumAdapters);
            try
            {
                if (D3DKMTEnumAdapters2(ref e) != 0) return;
                var seen = new HashSet<(int, uint)>();
                for (int i = 0; i < e.NumAdapters; i++)
                {
                    var info = Marshal.PtrToStructure<D3DKMT_ADAPTERINFO>(e.pAdapters + i * size);
                    var key = (info.AdapterLuid.HighPart, info.AdapterLuid.LowPart);

                    var reg = new D3DKMT_ADAPTERREGISTRYINFO();
                    string name = Query(info.hAdapter, KMTQAITYPE_ADAPTERREGISTRYINFO, ref reg) && !string.IsNullOrWhiteSpace(reg.AdapterString)
                        ? reg.AdapterString.Trim()
                        : "GPU";

                    // Software renderers and remote/virtual displays aren't real GPUs.
                    bool ignore = name.IndexOf("Microsoft Basic Render", StringComparison.OrdinalIgnoreCase) >= 0
                                  || name.IndexOf("Microsoft Remote Display", StringComparison.OrdinalIgnoreCase) >= 0
                                  || name.IndexOf("Basic Display", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (ignore || !seen.Add(key))
                    {
                        Close(info.hAdapter);
                        continue;
                    }

                    // Adapter type: discard software and indirect/virtual displays.
                    uint flags = 0;
                    bool hasType = Query(info.hAdapter, KMTQAITYPE_ADAPTERTYPE, ref flags);
                    if (hasType && ((flags & 0x1) == 0 || (flags & 0x4) != 0 || (flags & 0x40) != 0))
                    {
                        Close(info.hAdapter);
                        continue;
                    }

                    var seg = new D3DKMT_SEGMENTSIZEINFO();
                    ulong dedicated = Query(info.hAdapter, KMTQAITYPE_GETSEGMENTSIZE, ref seg) ? seg.DedicatedVideoMemorySize : 0;

                    _adapters.Add(new Adapter
                    {
                        Handle = info.hAdapter,
                        LuidLow = info.AdapterLuid.LowPart,
                        LuidHigh = info.AdapterLuid.HighPart,
                        Name = name,
                        DedicatedBytes = dedicated,
                        // Integrated GPUs reserve little (or no) dedicated memory.
                        Integrated = (hasType && (flags & 0x20) != 0) || dedicated < 1024UL * 1024 * 1024
                    });
                }
            }
            finally
            {
                Marshal.FreeHGlobal(e.pAdapters);
            }
        }

        private double? ReadTemperature(Adapter a, bool idle, bool haveLoad)
        {
            if (!a.TempSupported || TemperatureMode == "off") return null;

            bool auto = TemperatureMode != "always";
            // Dedicated GPU: never wake it just to read a number. Without load counters we can't
            // know whether it's awake, so in auto mode we don't ask at all.
            if (auto && !a.Integrated && (idle || !haveLoad))
            {
                a.LastTemp = null;
                a.LastTempRead = DateTime.MinValue; // read right away when it wakes up
                return null;
            }

            // Temperature changes slowly: reuse the last value between reads.
            if ((DateTime.UtcNow - a.LastTempRead).TotalSeconds < TempIntervalSec) return a.LastTemp;
            a.LastTempRead = DateTime.UtcNow;

            var perf = new D3DKMT_ADAPTER_PERFDATA { PhysicalAdapterIndex = 0 };
            if (!Query(a.Handle, KMTQAITYPE_ADAPTERPERFDATA, ref perf))
            {
                // The GPU may have powered down between the load sample and this query: only give up
                // after several failures in a row (old Windows or a driver without support).
                if (++a.TempFailures >= 3) a.TempSupported = false;
                a.LastTemp = null;
                return null;
            }
            a.TempFailures = 0;
            // Value in tenths of a degree; 0 = not reported.
            a.LastTemp = perf.Temperature == 0 || perf.Temperature > 1500 ? null : perf.Temperature / 10.0;
            return a.LastTemp;
        }

        // ---------------- Counters (PDH) ----------------

        private Dictionary<(int, uint), double> ReadEngineLoad()
        {
            var perEngine = new Dictionary<(int, uint, string), double>();
            foreach (var (instance, v) in _pdh!.Read(EnginePath))
            {
                if (v <= 0) continue;
                var m = EngineRx.Match(instance);
                if (!m.Success) continue;
                int high = unchecked((int)uint.Parse(m.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                uint low = uint.Parse(m.Groups[2].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                var k = (high, low, m.Groups[3].Value + "_" + m.Groups[4].Value);
                perEngine[k] = (perEngine.TryGetValue(k, out var acc) ? acc : 0) + v;
            }

            // GPU usage = busiest engine (same criterion as Task Manager).
            var result = new Dictionary<(int, uint), double>();
            foreach (var kv in perEngine)
            {
                var key = (kv.Key.Item1, kv.Key.Item2);
                result[key] = Math.Max(result.TryGetValue(key, out var cur) ? cur : 0, kv.Value);
            }
            return result;
        }

        private Dictionary<(int, uint), (double dedicated, double shared)> ReadMemory()
        {
            var result = new Dictionary<(int, uint), (double dedicated, double shared)>();
            void Add(string path, bool dedicated)
            {
                foreach (var (instance, v) in _pdh!.Read(path))
                {
                    var m = MemoryRx.Match(instance);
                    if (!m.Success) continue;
                    int high = unchecked((int)uint.Parse(m.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                    uint low = uint.Parse(m.Groups[2].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                    var key = (high, low);
                    result.TryGetValue(key, out var cur);
                    double b = Math.Max(0, v);
                    result[key] = dedicated ? (cur.dedicated + b, cur.shared) : (cur.dedicated, cur.shared + b);
                }
            }
            Add(DedicatedPath, true);
            Add(SharedPath, false);
            return result;
        }

        public void Dispose()
        {
            _pdh?.Dispose();
            _pdh = null;
            foreach (var a in _adapters) Close(a.Handle);
            _adapters.Clear();
        }

        // ---------------- P/Invoke ----------------

        private const int KMTQAITYPE_GETSEGMENTSIZE = 3;
        private const int KMTQAITYPE_ADAPTERREGISTRYINFO = 8;
        private const int KMTQAITYPE_ADAPTERTYPE = 15;
        private const int KMTQAITYPE_ADAPTERPERFDATA = 62;

        [StructLayout(LayoutKind.Sequential)]
        private struct LUID
        {
            public uint LowPart;
            public int HighPart;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct D3DKMT_ADAPTERINFO
        {
            public uint hAdapter;
            public LUID AdapterLuid;
            public uint NumOfSources;
            public int bPrecisePresentRegionsPreferred;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct D3DKMT_ENUMADAPTERS2
        {
            public uint NumAdapters;
            public IntPtr pAdapters;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct D3DKMT_QUERYADAPTERINFO
        {
            public uint hAdapter;
            public int Type;
            public IntPtr pPrivateDriverData;
            public uint PrivateDriverDataSize;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct D3DKMT_ADAPTERREGISTRYINFO
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string AdapterString;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string BiosString;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string DacType;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string ChipType;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct D3DKMT_SEGMENTSIZEINFO
        {
            public ulong DedicatedVideoMemorySize;
            public ulong DedicatedSystemMemorySize;
            public ulong SharedSystemMemorySize;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct D3DKMT_ADAPTER_PERFDATA
        {
            public uint PhysicalAdapterIndex;
            public ulong MemoryFrequency;
            public ulong MaxMemoryFrequency;
            public ulong MaxMemoryFrequencyOC;
            public ulong MemoryBandwidth;
            public ulong PCIEBandwidth;
            public uint FanRPM;
            public uint Power;
            public uint Temperature;
            public byte PowerStateOverride;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct D3DKMT_CLOSEADAPTER
        {
            public uint hAdapter;
        }

        [DllImport("gdi32.dll")]
        private static extern int D3DKMTEnumAdapters2(ref D3DKMT_ENUMADAPTERS2 data);

        [DllImport("gdi32.dll")]
        private static extern int D3DKMTQueryAdapterInfo(ref D3DKMT_QUERYADAPTERINFO data);

        [DllImport("gdi32.dll")]
        private static extern int D3DKMTCloseAdapter(ref D3DKMT_CLOSEADAPTER data);

        private static bool Query<T>(uint adapter, int type, ref T value) where T : struct
        {
            int size = Marshal.SizeOf<T>();
            IntPtr buf = Marshal.AllocHGlobal(size);
            try
            {
                // Zero the buffer and copy the input fields.
                for (int i = 0; i < size; i++) Marshal.WriteByte(buf, i, 0);
                Marshal.StructureToPtr(value, buf, false);
                var q = new D3DKMT_QUERYADAPTERINFO
                {
                    hAdapter = adapter,
                    Type = type,
                    pPrivateDriverData = buf,
                    PrivateDriverDataSize = (uint)size
                };
                if (D3DKMTQueryAdapterInfo(ref q) != 0) return false;
                value = Marshal.PtrToStructure<T>(buf);
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                Marshal.FreeHGlobal(buf);
            }
        }

        private static void Close(uint handle)
        {
            try
            {
                var c = new D3DKMT_CLOSEADAPTER { hAdapter = handle };
                D3DKMTCloseAdapter(ref c);
            }
            catch
            {
            }
        }
    }
}
