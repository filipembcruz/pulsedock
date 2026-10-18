using System;
using System.IO;
using PulseDock.Models;

namespace PulseDock.Services
{
    /// <summary>Simple log at %APPDATA%\PulseDock\log.txt (rotates at 1 MB).</summary>
    public static class Log
    {
        private static readonly object Gate = new();

        public static void Error(string where, Exception? ex) => Write("ERRO", where + ": " + ex);

        public static void Info(string message) => Write("INFO", message);

        private static void Write(string level, string message)
        {
            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(SettingsStore.Folder);
                    var path = Path.Combine(SettingsStore.Folder, "log.txt");
                    var fi = new FileInfo(path);
                    if (fi.Exists && fi.Length > 1_000_000)
                    {
                        File.Copy(path, path + ".old", true);
                        File.Delete(path);
                    }
                    File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}{Environment.NewLine}");
                }
            }
            catch
            {
                // Logging must never crash the app.
            }
        }
    }
}
