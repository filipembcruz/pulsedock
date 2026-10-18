using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using PulseDock.Models;

namespace PulseDock.Services
{
    /// <summary>
    /// Measures per-process CPU, memory and disk by comparing two readings.
    /// Runs only while the details window is open.
    /// </summary>
    public sealed class ProcessSampler
    {
        private sealed class Prev
        {
            public long CreationTime;
            public long CpuTicks;   // kernel + user, in 100 ns units
            public ulong IoBytes;   // read + write
        }

        private readonly Dictionary<int, Prev> _prev = new();
        private readonly Dictionary<string, string> _descByPath = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<int, (long creation, string path)> _pathByPid = new();
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private double _lastSeconds = -1;
        private readonly int _cores = Math.Max(1, Environment.ProcessorCount);

        private static readonly HashSet<string> NoExeSuffix = new(StringComparer.OrdinalIgnoreCase)
        {
            "System", "Registry", "Idle", "Memory Compression", "Secure System", "Interrupts", "vmmem"
        };

        public int LastProcessCount { get; private set; }

        public List<ProcInfo> Sample()
        {
            double now = _clock.Elapsed.TotalSeconds;
            double dt = _lastSeconds < 0 ? 0 : now - _lastSeconds;
            _lastSeconds = now;

            var result = new List<ProcInfo>();
            var seen = new HashSet<int>();
            Process[] procs;
            try { procs = Process.GetProcesses(); }
            catch (Exception ex) { Log.Error("GetProcesses", ex); return result; }

            LastProcessCount = procs.Length;

            foreach (var p in procs)
            {
                try
                {
                    int pid = p.Id;
                    if (pid == 0) continue; // Idle
                    seen.Add(pid);

                    var info = new ProcInfo
                    {
                        Pid = pid,
                        Name = NoExeSuffix.Contains(p.ProcessName) ? p.ProcessName : p.ProcessName + ".exe",
                        MemoryBytes = SafeWorkingSet(p)
                    };

                    IntPtr h = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
                    if (h != IntPtr.Zero)
                    {
                        try
                        {
                            long creation = 0, cpu = 0;
                            ulong io = 0;
                            if (Native.GetProcessTimes(h, out creation, out _, out var kernel, out var user))
                                cpu = kernel + user;
                            if (Native.GetProcessIoCounters(h, out var ioc))
                                io = ioc.ReadTransferCount + ioc.WriteTransferCount;

                            if (_prev.TryGetValue(pid, out var prev) && prev.CreationTime == creation && dt > 0)
                            {
                                double cpuSec = Math.Max(0, cpu - prev.CpuTicks) / 1e7;
                                info.CpuPercent = Math.Clamp(cpuSec / dt / _cores * 100.0, 0, 100);
                                info.DiskBps = io >= prev.IoBytes ? (io - prev.IoBytes) / dt : 0;
                            }
                            _prev[pid] = new Prev { CreationTime = creation, CpuTicks = cpu, IoBytes = io };

                            info.Description = Describe(h, pid, creation, info.Name);
                        }
                        finally
                        {
                            Native.CloseHandle(h);
                        }
                    }
                    else
                    {
                        info.Description = SystemDescription(info.Name);
                    }

                    result.Add(info);
                }
                catch
                {
                    // Process exited during the reading.
                }
                finally
                {
                    p.Dispose();
                }
            }

            // Remove processes that are gone.
            var gone = new List<int>();
            foreach (var pid in _prev.Keys) if (!seen.Contains(pid)) gone.Add(pid);
            foreach (var pid in gone) { _prev.Remove(pid); _pathByPid.Remove(pid); }

            return result;
        }

        private static long SafeWorkingSet(Process p)
        {
            try { return p.WorkingSet64; } catch { return 0; }
        }

        private string Describe(IntPtr handle, int pid, long creation, string fallback)
        {
            string path;
            if (_pathByPid.TryGetValue(pid, out var cached) && cached.creation == creation)
            {
                path = cached.path;
            }
            else
            {
                var sb = new StringBuilder(1024);
                int size = sb.Capacity;
                path = Native.QueryFullProcessImageName(handle, 0, sb, ref size) ? sb.ToString(0, size) : "";
                _pathByPid[pid] = (creation, path);
            }

            if (string.IsNullOrEmpty(path)) return SystemDescription(fallback);

            if (!_descByPath.TryGetValue(path, out var desc))
            {
                try
                {
                    desc = FileVersionInfo.GetVersionInfo(path).FileDescription ?? "";
                }
                catch
                {
                    desc = "";
                }
                if (string.IsNullOrWhiteSpace(desc)) desc = Path.GetFileNameWithoutExtension(path);
                _descByPath[path] = desc;
            }
            return desc;
        }

        private static string SystemDescription(string name) => name switch
        {
            "System" => Loc.T("SysKernel"),
            "Registry" => Loc.T("SysRegistry"),
            "Memory Compression" => Loc.T("SysMemCompression"),
            _ => Loc.T("SysProcess")
        };

        // ---------- End task ----------

        private static readonly HashSet<string> Critical = new(StringComparer.OrdinalIgnoreCase)
        {
            "System", "Registry", "Secure System", "Memory Compression", "smss.exe", "csrss.exe",
            "wininit.exe", "winlogon.exe", "services.exe", "lsass.exe", "lsaiso.exe", "fontdrvhost.exe", "dwm.exe"
        };

        public static bool IsCritical(string name) => Critical.Contains(name);

        /// <summary>Ends the process. Returns a message for the user.</summary>
        public static Task<(bool ok, string message)> KillAsync(int pid, string name)
        {
            return Task.Run(() =>
            {
                if (pid == Environment.ProcessId)
                    return (false, Loc.T("KillSelf"));
                if (IsCritical(name))
                    return (false, Loc.F("KillCritical", name));
                try
                {
                    using var p = Process.GetProcessById(pid);
                    p.Kill(entireProcessTree: false);
                    p.WaitForExit(3000);
                    return (true, Loc.F("KillOk", name));
                }
                catch (ArgumentException)
                {
                    return (true, Loc.F("KillGone", name));
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    return (false, Loc.F("KillDenied", name));
                }
                catch (Exception ex)
                {
                    Log.Error("Kill " + name, ex);
                    return (false, Loc.F("KillFailed", name, ex.Message));
                }
            });
        }
    }
}
