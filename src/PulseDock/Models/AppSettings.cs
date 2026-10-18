using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using PulseDock.Services;

namespace PulseDock.Models
{
    public enum DisplayMode { Overlay, Desktop, Tray }

    public enum LayoutKind { CompactVertical, Expanded, CompactHorizontal }

    public enum Corner { TopLeft, TopRight, BottomLeft, BottomRight }

    /// <summary>Corner = pinned to a corner; Free = wherever the user dropped it after dragging.</summary>
    public enum PositionMode { Corner, Free }

    public sealed class AppSettings
    {
        public DisplayMode Mode { get; set; } = DisplayMode.Overlay;
        public LayoutKind Layout { get; set; } = LayoutKind.CompactVertical;
        public Corner Corner { get; set; } = Corner.TopRight;

        /// <summary>Distance from the screen edge, in logical pixels.</summary>
        public int EdgeMargin { get; set; } = 8;

        public PositionMode Position { get; set; } = PositionMode.Corner;
        /// <summary>Free position, in screen pixels (top-left corner of the widget).</summary>
        public int FreeX { get; set; }
        public int FreeY { get; set; }
        /// <summary>Prevents dragging the widget.</summary>
        public bool LockPosition { get; set; }
        /// <summary>The desktop shortcut is only created on first run.</summary>
        public bool DesktopShortcutCreated { get; set; }

        /// <summary>Monitor DeviceName (e.g. \\.\DISPLAY1). Null = primary monitor.</summary>
        public string? MonitorDevice { get; set; }

        /// <summary>Widget background opacity, 40–100.</summary>
        public int Opacity { get; set; } = 100;

        public int IntervalMs { get; set; } = 1000;

        public bool ShowCpu { get; set; } = true;
        public bool ShowMemory { get; set; } = true;
        public bool ShowDisk { get; set; } = true;
        public bool ShowGpu0 { get; set; } = true;
        public bool ShowGpu1 { get; set; } = true;
        public bool ShowNetwork { get; set; } = true;

        public bool AutoStart { get; set; }
        public bool ClickThrough { get; set; }
        public bool HideInFullscreen { get; set; } = true;

        /// <summary>CPU/disk temperature via the WinRing0 driver (antivirus may flag it). Off by default.</summary>
        public bool AdvancedSensors { get; set; }

        /// <summary>"en" (default), "pt" or "auto" (follows the Windows language).</summary>
        public string Language { get; set; } = "en";

        /// <summary>Bumped when a default changes in a way that should reset older saved values.</summary>
        public int SettingsVersion { get; set; }

        /// <summary>
        /// Switches to isolate display problems (edit settings.json, then restart PulseDock).
        /// Defaults are the safe values; see README → Troubleshooting.
        /// </summary>
        public DiagnosticsSettings Diagnostics { get; set; } = new();

        /// <summary>Drive letter of the monitored drive (without ':').</summary>
        public string DiskLetter { get; set; } = "C";

        public AppSettings Clone() => (AppSettings)MemberwiseClone();
    }

    public sealed class DiagnosticsSettings
    {
        /// <summary>GPU temperature: "auto" (only while the GPU is already busy), "always" or "off".</summary>
        public string GpuTemperature { get; set; } = "auto";

        /// <summary>Read GPU load / memory counters.</summary>
        public bool GpuCounters { get; set; } = true;

        /// <summary>Old behavior: re-assert "always on top" every 2 s. Off by default.</summary>
        public bool ReassertTopmost { get; set; }

        /// <summary>Old behavior: per-pixel transparent window (WPF AllowsTransparency). Off by default.</summary>
        public bool PerPixelTransparency { get; set; }
    }

    public static class SettingsStore
    {
        private static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented = true,
            // Users edit this file by hand (Diagnostics): tolerate comments, trailing commas and any casing.
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };

        public static string Folder =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PulseDock");

        private static string FilePath => Path.Combine(Folder, "settings.json");

        public static AppSettings Load()
        {
            try
            {
                // First run after the rename: reuse the old "MonitorSistema" settings if present.
                var legacy = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MonitorSistema", "settings.json");
                var path = File.Exists(FilePath) ? FilePath : File.Exists(legacy) ? legacy : null;
                if (path != null)
                {
                    var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Options);
                    if (s != null)
                    {
                        if (path == legacy) s.DesktopShortcutCreated = false; // new name needs a new shortcut
                        return Sanitize(s);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("Settings.Load", ex);
            }
            return new AppSettings();
        }

        public static void Save(AppSettings s)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(Sanitize(s), Options));
            }
            catch (Exception ex)
            {
                Log.Error("Settings.Save", ex);
            }
        }

        private static AppSettings Sanitize(AppSettings s)
        {
            if (s.SettingsVersion < 2)
            {
                // v2: background transparency is now whole-window alpha; start opaque (cheapest for DWM).
                s.Opacity = 100;
                s.SettingsVersion = 2;
            }
            s.Diagnostics ??= new DiagnosticsSettings();
            s.Opacity = Math.Clamp(s.Opacity, 40, 100);
            s.EdgeMargin = Math.Clamp(s.EdgeMargin, 0, 400);
            s.IntervalMs = Math.Clamp(s.IntervalMs, 250, 10000);
            if (s.Language != "en" && s.Language != "pt" && s.Language != "auto") s.Language = "en";
            if (string.IsNullOrWhiteSpace(s.DiskLetter)) s.DiskLetter = "C";
            s.DiskLetter = s.DiskLetter.Trim().TrimEnd(':').ToUpperInvariant();
            if (s.DiskLetter.Length > 1) s.DiskLetter = s.DiskLetter.Substring(0, 1);
            return s;
        }
    }
}
