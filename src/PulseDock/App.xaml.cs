using System;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using PulseDock.Services;

namespace PulseDock
{
    public partial class App : Application
    {
        private const string MutexName = "PulseDock.SingleInstance.v1";
        private static Mutex? _mutex;
        private static bool _ownsMutex;

        public static AppHost? Host { get; private set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            _mutex = new Mutex(true, MutexName, out _ownsMutex);

            // When restarting as administrator, the old instance is still closing: wait up to 6 s.
            if (!_ownsMutex && Array.IndexOf(e.Args, "--elevated") >= 0)
            {
                try { _ownsMutex = _mutex.WaitOne(TimeSpan.FromSeconds(6)); }
                catch (AbandonedMutexException) { _ownsMutex = true; }
            }

            if (!_ownsMutex)
            {
                // Another instance is already running.
                Shutdown();
                return;
            }

            base.OnStartup(e);

            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += (_, ex) => Log.Error("Unhandled", ex.ExceptionObject as Exception);

            Host = new AppHost();
            Host.Start();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            Host?.Dispose();
            ReleaseSingleInstance();
            base.OnExit(e);
        }

        /// <summary>Releases the single-instance mutex (used when restarting as administrator).</summary>
        public static void ReleaseSingleInstance()
        {
            try
            {
                if (_mutex != null && _ownsMutex)
                {
                    _mutex.ReleaseMutex();
                    _ownsMutex = false;
                }
                _mutex?.Dispose();
                _mutex = null;
            }
            catch (Exception ex)
            {
                Log.Error("ReleaseMutex", ex);
            }
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            Log.Error("Dispatcher", e.Exception);
            e.Handled = true;
        }
    }
}
