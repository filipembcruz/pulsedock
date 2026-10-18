using System;
using System.IO;
using System.Runtime.InteropServices;

namespace PulseDock.Services
{
    /// <summary>"PulseDock" shortcuts in the Start menu and on the desktop.</summary>
    public static class Shortcuts
    {
        private const string Name = "PulseDock.lnk";
        private const string LegacyName = "Monitor de Sistema.lnk";

        public static string StartMenuPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), Name);

        public static string DesktopPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Name);

        /// <summary>Always keeps the Start menu shortcut pointing to this .exe.</summary>
        public static void EnsureStartMenu() => Create(StartMenuPath);

        public static void CreateDesktop() => Create(DesktopPath);

        /// <summary>Removes shortcuts from the old version ("Monitor de Sistema").</summary>
        public static void RemoveLegacy()
        {
            foreach (var folder in new[] { Environment.SpecialFolder.Programs, Environment.SpecialFolder.DesktopDirectory })
            {
                try
                {
                    var path = Path.Combine(Environment.GetFolderPath(folder), LegacyName);
                    if (File.Exists(path)) File.Delete(path);
                }
                catch (Exception ex)
                {
                    Log.Error("RemoveLegacy", ex);
                }
            }
        }

        private static void Create(string lnkPath)
        {
            object? shell = null;
            object? link = null;
            try
            {
                var exe = SystemIntegration.ExePath;
                if (string.IsNullOrEmpty(exe)) return;

                var type = Type.GetTypeFromProgID("WScript.Shell");
                if (type == null) return;
                shell = Activator.CreateInstance(type);
                if (shell == null) return;

                Directory.CreateDirectory(Path.GetDirectoryName(lnkPath)!);
                dynamic sh = shell;
                link = sh.CreateShortcut(lnkPath);
                dynamic l = link!;
                l.TargetPath = exe;
                l.WorkingDirectory = Path.GetDirectoryName(exe) ?? "";
                l.IconLocation = exe + ",0";
                l.Description = Loc.T("ShortcutDesc");
                l.Save();
            }
            catch (Exception ex)
            {
                Log.Error("Shortcut " + lnkPath, ex);
            }
            finally
            {
                if (link != null && Marshal.IsComObject(link)) Marshal.FinalReleaseComObject(link);
                if (shell != null && Marshal.IsComObject(shell)) Marshal.FinalReleaseComObject(shell);
            }
        }
    }
}
