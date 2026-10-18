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
    /// - Load: "GPU Engine" counters (busiest engine, summed across processes).
    /// - Memory in use: "GPU Adapter Memory" counters.
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
        }

        private readonly List<Adapter> _adapters = new();
        private Dictionary<string, CounterSample> _prevEngine = new();
        private bool _countersAvailable;

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
                Log.Info("GPUs: " + string.Join(", ", _adapters.Select(a => $"{a.Name} ({(a.Integrated ? "integrada" : "dedicada")}, {a.DedicatedBytes / (1024 * 1024)} MB)")));
            }
            catch (Exception ex)
            {
                Log.Error("GpuSensors.Enumerate", ex);
            }

            try
            {
                _countersAvailable = PerformanceCounterCategory.Exists("GPU Engine");
                if (_countersAvailable) DropAdaptersWithoutEngines();
            }
            catch (Exception ex)
            {
                Log.Error("GpuSensors.Counters", ex);
                _countersAvailable = false;
            }
        }

        /// <summary>Virtual monitors (e.g. remote access apps) show up as adapters with no engines: remove them.</summary>
        private void DropAdaptersWithoutEngines()
        {
            var names = new PerformanceCounterCategory("GPU Engine").GetInstanceNames();
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

            Dictionary<(int, uint), double> load = _countersAvailable ? ReadEngineLoad() : new();
            Dictionary<(int, uint), (double dedicated, double shared)> mem = _countersAvailable ? ReadMemory() : new();

            foreach (var a in _adapters)
            {
                var key = (a.LuidHigh, a.LuidLow);
                var g = new GpuReading
                {
                    Name = a.Name,
                    Integrated = a.Integrated,
                    Load = load.TryGetValue(key, out var l) ? Math.Clamp(l, 0, 100) : (_countersAvailable ? 0d : (double?)null),
                    Temperature = ReadTemperature(a)
                };
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

        private static double? ReadTemperature(Adapter a)
        {
            if (!a.TempSupported) return null;
            var perf = new D3DKMT_ADAPTER_PERFDATA { PhysicalAdapterIndex = 0 };
            if (!Query(a.Handle, KMTQAITYPE_ADAPTERPERFDATA, ref perf))
            {
                a.TempSupported = false; // old Windows or unsupported driver: don't try again
                return null;
            }
            // Value in tenths of a degree; 0 = not reported.
            if (perf.Temperature == 0 || perf.Temperature > 1500) return null;
            return perf.Temperature / 10.0;
        }

        // ---------------- Counters ----------------

        private Dictionary<(int, uint), double> ReadEngineLoad()
        {
            var perEngine = new Dictionary<(int, uint, string), double>();
            var next = new Dictionary<string, CounterSample>();
            try
            {
                var data = new PerformanceCounterCategory("GPU Engine").ReadCategory();
                InstanceDataCollection? util = null;
                foreach (InstanceDataCollection c in data.Values)
                {
                    if (string.Equals(c.CounterName, "Utilization Percentage", StringComparison.OrdinalIgnoreCase)) { util = c; break; }
                }
                if (util == null) return new();

                foreach (InstanceData d in util.Values)
                {
                    var sample = d.Sample;
                    next[d.InstanceName] = sample;
                    if (!_prevEngine.TryGetValue(d.InstanceName, out var prev)) continue;

                    var m = EngineRx.Match(d.InstanceName);
                    if (!m.Success) continue;
                    int high = unchecked((int)uint.Parse(m.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                    uint low = uint.Parse(m.Groups[2].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                    string engine = m.Groups[3].Value + "_" + m.Groups[4].Value;

                    float v = CounterSample.Calculate(prev, sample);
                    if (float.IsNaN(v) || v <= 0) continue;
                    var k = (high, low, engine);
                    perEngine[k] = (perEngine.TryGetValue(k, out var acc) ? acc : 0) + v;
                }
            }
            catch (Exception ex)
            {
                Log.Error("GPU Engine", ex);
            }
            _prevEngine = next;

            // GPU usage = busiest engine (same criterion as Task Manager).
            var result = new Dictionary<(int, uint), double>();
            foreach (var kv in perEngine)
            {
                var key = (kv.Key.Item1, kv.Key.Item2);
                result[key] = Math.Max(result.TryGetValue(key, out var cur) ? cur : 0, kv.Value);
            }
            return result;
        }

        private static Dictionary<(int, uint), (double dedicated, double shared)> ReadMemory()
        {
            var result = new Dictionary<(int, uint), (double, double)>();
            try
            {
                if (!PerformanceCounterCategory.Exists("GPU Adapter Memory")) return result;
                var data = new PerformanceCounterCategory("GPU Adapter Memory").ReadCategory();
                foreach (InstanceDataCollection c in data.Values)
                {
                    bool dedicated = string.Equals(c.CounterName, "Dedicated Usage", StringComparison.OrdinalIgnoreCase);
                    bool shared = string.Equals(c.CounterName, "Shared Usage", StringComparison.OrdinalIgnoreCase);
                    if (!dedicated && !shared) continue;

                    foreach (InstanceData d in c.Values)
                    {
                        var m = MemoryRx.Match(d.InstanceName);
                        if (!m.Success) continue;
                        int high = unchecked((int)uint.Parse(m.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        uint low = uint.Parse(m.Groups[2].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                        var key = (high, low);
                        result.TryGetValue(key, out var cur);
                        double v = Math.Max(0, d.RawValue);
                        result[key] = dedicated ? (cur.Item1 + v, cur.Item2) : (cur.Item1, cur.Item2 + v);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("GPU Adapter Memory", ex);
            }
            return result;
        }

        public void Dispose()
        {
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
