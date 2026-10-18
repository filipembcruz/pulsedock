using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace PulseDock.Services
{
    /// <summary>
    /// Minimal PDH wrapper for wildcard counters such as "\GPU Engine(*)\Utilization Percentage".
    /// Much cheaper than PerformanceCounterCategory.ReadCategory(), which reads every counter of every
    /// instance (thousands for "GPU Engine") on each call. Wildcards are re-expanded on every collect,
    /// so new processes show up automatically. Use from a single thread.
    /// </summary>
    internal sealed class PdhQuery : IDisposable
    {
        private IntPtr _query;
        private readonly Dictionary<string, IntPtr> _counters = new(StringComparer.OrdinalIgnoreCase);

        private PdhQuery() { }

        public static PdhQuery? Create(params string[] paths)
        {
            var q = new PdhQuery();
            try
            {
                if (PdhOpenQuery(null, IntPtr.Zero, out q._query) != 0) return null;
                foreach (var p in paths)
                {
                    if (PdhAddEnglishCounter(q._query, p, IntPtr.Zero, out var c) == 0) q._counters[p] = c;
                    else Log.Info("PDH counter not available: " + p);
                }
                if (q._counters.Count == 0) { q.Dispose(); return null; }
                PdhCollectQueryData(q._query); // first sample: rate counters need two
                return q;
            }
            catch (Exception ex)
            {
                Log.Error("PdhQuery.Create", ex);
                q.Dispose();
                return null;
            }
        }

        public bool Has(string path) => _counters.ContainsKey(path);

        public bool Collect()
        {
            try { return _query != IntPtr.Zero && PdhCollectQueryData(_query) == 0; }
            catch { return false; }
        }

        /// <summary>Current (instance name, value) pairs of a wildcard counter.</summary>
        public List<(string instance, double value)> Read(string path)
        {
            var result = new List<(string, double)>();
            if (!_counters.TryGetValue(path, out var counter)) return result;

            uint size = 0;
            int status = PdhGetFormattedCounterArray(counter, PDH_FMT_DOUBLE | PDH_FMT_NOCAP100, ref size, out uint count, IntPtr.Zero);
            if (status != PDH_MORE_DATA || size == 0) return result;

            IntPtr buf = Marshal.AllocHGlobal((int)size);
            try
            {
                status = PdhGetFormattedCounterArray(counter, PDH_FMT_DOUBLE | PDH_FMT_NOCAP100, ref size, out count, buf);
                if (status != 0) return result;

                // PDH_FMT_COUNTERVALUE_ITEM_W: { LPWSTR szName; DWORD CStatus; (pad) double value }.
                // The double forces 8-byte alignment, so the layout is 0 / 8 / 16, 24 bytes on x64 AND x86.
                const int itemSize = 24, statusOffset = 8, valueOffset = 16;
                for (int i = 0; i < count; i++)
                {
                    IntPtr item = buf + i * itemSize;
                    string? name = Marshal.PtrToStringUni(Marshal.ReadIntPtr(item));
                    uint cstatus = unchecked((uint)Marshal.ReadInt32(item + statusOffset));
                    if (name == null || cstatus > 1) continue; // 0 = valid data, 1 = new data
                    double v = BitConverter.Int64BitsToDouble(Marshal.ReadInt64(item + valueOffset));
                    if (!double.IsNaN(v) && !double.IsInfinity(v)) result.Add((name, v));
                }
            }
            catch (Exception ex)
            {
                Log.Error("PdhQuery.Read " + path, ex);
            }
            finally
            {
                Marshal.FreeHGlobal(buf);
            }
            return result;
        }

        public void Dispose()
        {
            if (_query != IntPtr.Zero)
            {
                try { PdhCloseQuery(_query); } catch { }
                _query = IntPtr.Zero;
            }
        }

        private const uint PDH_FMT_DOUBLE = 0x00000200;
        private const uint PDH_FMT_NOCAP100 = 0x00008000;
        private const int PDH_MORE_DATA = unchecked((int)0x800007D2);

        [DllImport("pdh.dll", CharSet = CharSet.Unicode, EntryPoint = "PdhOpenQueryW")]
        private static extern int PdhOpenQuery(string? dataSource, IntPtr userData, out IntPtr query);

        [DllImport("pdh.dll", CharSet = CharSet.Unicode, EntryPoint = "PdhAddEnglishCounterW")]
        private static extern int PdhAddEnglishCounter(IntPtr query, string fullCounterPath, IntPtr userData, out IntPtr counter);

        [DllImport("pdh.dll")]
        private static extern int PdhCollectQueryData(IntPtr query);

        [DllImport("pdh.dll", CharSet = CharSet.Unicode, EntryPoint = "PdhGetFormattedCounterArrayW")]
        private static extern int PdhGetFormattedCounterArray(IntPtr counter, uint format, ref uint bufferSize, out uint itemCount, IntPtr itemBuffer);

        [DllImport("pdh.dll")]
        private static extern int PdhCloseQuery(IntPtr query);
    }
}
