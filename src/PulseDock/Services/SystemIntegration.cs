using System;
using System.Diagnostics;
using System.Security.Principal;
using Microsoft.Win32;

namespace PulseDock.Services
{
    /// <summary>Start with Windows, administrator status and elevated restart.</summary>
    public static class SystemIntegration
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "PulseDock";

        public static string ExePath => Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? "";

        public static void SetAutoStart(bool enabled)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
                                ?? Registry.CurrentUser.CreateSubKey(RunKey);
                if (enabled) key.SetValue(ValueName, $"\"{ExePath}\" --startup");
                else if (key.GetValue(ValueName) != null) key.DeleteValue(ValueName, false);
            }
            catch (Exception ex)
            {
                Log.Error("AutoStart", ex);
            }
        }

        /// <summary>Removes the startup entry from the old version ("MonitorSistema").</summary>
        public static void RemoveLegacyAutoStart()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
                if (key?.GetValue("MonitorSistema") != null) key.DeleteValue("MonitorSistema", false);
            }
            catch (Exception ex)
            {
                Log.Error("RemoveLegacyAutoStart", ex);
            }
        }

        public static bool IsAdministrator()
        {
            try
            {
                using var id = WindowsIdentity.GetCurrent();
                return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Starts a new elevated instance. Returns false if the user declined UAC.</summary>
        public static bool StartElevated()
        {
            try
            {
                Process.Start(new ProcessStartInfo(ExePath)
                {
                    UseShellExecute = true,
                    Verb = "runas",
                    Arguments = "--elevated"
                });
                return true;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return false; // UAC cancelled
            }
            catch (Exception ex)
            {
                Log.Error("StartElevated", ex);
                return false;
            }
        }
    }
}
