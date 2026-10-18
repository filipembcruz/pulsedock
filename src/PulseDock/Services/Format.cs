using System;
using System.Collections.Generic;
using System.Globalization;

namespace PulseDock.Services
{
    /// <summary>Number formatting in the current language.</summary>
    public static class Format
    {
        /// <summary>Culture of the current language (decimal comma in Portuguese, point in English).</summary>
        public static CultureInfo Br => Loc.Culture;

        public static string Percent(double v) => Math.Round(v).ToString("0", Br) + "%";

        public static string Num(double v, int maxDecimals = 1)
        {
            string fmt = maxDecimals <= 0 ? "0" : "0." + new string('#', maxDecimals);
            return v.ToString(fmt, Br);
        }

        public static string Fixed1(double v) => v.ToString("0.0", Br);

        public static string Temp(double? c, bool shortForm = false) =>
            c.HasValue ? Math.Round(c.Value).ToString("0", Br) + (shortForm ? "°" : "°C") : "";

        public static string Gb(double bytes) => Fixed1(bytes / (1024.0 * 1024 * 1024));

        /// <summary>"Rounded" total: 31,8 GB → 32.</summary>
        public static string GbTotal(double bytes)
        {
            double g = bytes / (1024.0 * 1024 * 1024);
            return Math.Abs(g - Math.Round(g)) < 0.35 ? Math.Round(g).ToString("0", Br) : Num(g, 1);
        }

        /// <summary>MB/s with 1 decimal place below 10.</summary>
        public static string MBps(double bytesPerSec)
        {
            double mb = bytesPerSec / (1024.0 * 1024);
            return mb < 10 ? Num(mb, 1) : Math.Round(mb).ToString("0", Br);
        }

        /// <summary>Network rate in bits: returns (value, unit).</summary>
        public static (string value, string unit) NetRate(double bytesPerSec)
        {
            double bits = bytesPerSec * 8;
            if (bits >= 1_000_000_000) return (Num(bits / 1e9, 2), "Gb/s");
            if (bits >= 1_000_000) return (Num(bits / 1e6, 1), "Mb/s");
            return (Math.Round(bits / 1e3).ToString("0", Br), "Kb/s");
        }

        public static string Memory(long bytes)
        {
            double mb = bytes / (1024.0 * 1024);
            return mb >= 1024 ? Num(mb / 1024, 1) + " GB" : Math.Round(mb).ToString("0", Br) + " MB";
        }

        public static string Join(params string[] parts)
        {
            var list = new List<string>();
            foreach (var p in parts) if (!string.IsNullOrWhiteSpace(p)) list.Add(p);
            return string.Join(" · ", list);
        }

        public static string JoinShort(params string[] parts)
        {
            var list = new List<string>();
            foreach (var p in parts) if (!string.IsNullOrWhiteSpace(p)) list.Add(p);
            return string.Join(" ", list);
        }
    }
}
