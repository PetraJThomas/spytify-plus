using System;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace EspionSpotify.Wpf
{
    public partial class App : Application
    {
        // Single-instance plumbing. Two Spytify+ processes would each start a Spotify OAuth callback
        // listener on the same redirect port; whichever binds first wins and the other hangs on
        // "Connecting" forever, so we hard-enforce one instance.
        private const string InstanceMutexName = "SpytifyPlus.SingleInstance";
        private const string ShowWindowEventName = "SpytifyPlus.ShowWindow";
        private Mutex _instanceMutex;
        private EventWaitHandle _showWindowSignal;
        private RegisteredWaitHandle _showWindowRegistration;

        protected override void OnStartup(StartupEventArgs e)
        {
            // Single-instance slot: acquire ownership via WaitOne rather than the constructor, so we can
            // wait out a previous instance that is still shutting down. AbandonedMutexException means the
            // prior owner died holding it (Environment.Exit doesn't release gracefully) and it's now ours.
            _instanceMutex = new Mutex(false, InstanceMutexName);
            bool owned;
            try { owned = _instanceMutex.WaitOne(0); }
            catch (AbandonedMutexException) { owned = true; }

            if (!owned)
            {
                // The slot is held. Ask whoever has it to surface right away (no-op if they're already
                // dying), then wait briefly: if they were a previous instance mid-shutdown, the slot
                // frees up and we take over, so a quick relaunch right after "Exit" actually starts
                // instead of silently doing nothing. If a live instance keeps holding it, we bail.
                try { EventWaitHandle.OpenExisting(ShowWindowEventName).Set(); } catch { /* ignored */ }
                try { owned = _instanceMutex.WaitOne(TimeSpan.FromSeconds(3)); }
                catch (AbandonedMutexException) { owned = true; }

                if (!owned)
                {
                    Shutdown();
                    return;
                }
            }

            // We own the single-instance slot: listen for later launches asking us to come to the front.
            _showWindowSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowWindowEventName);
            _showWindowRegistration = ThreadPool.RegisterWaitForSingleObject(
                _showWindowSignal, (_, __) => Dispatcher.BeginInvoke(new Action(SurfaceMainWindow)),
                null, Timeout.Infinite, false);

            // Spotify green as the system accent: drives toggles, slider, nav indicator,
            // selection, focus rings, etc.
            ModernWpf.ThemeManager.Current.AccentColor = Color.FromRgb(0x1E, 0xD7, 0x60);

            // Restore the engine's crash reporter that the old WinForms Program.Main wired up.
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;

            base.OnStartup(e);
        }

        // Bring the running instance's window back into view when a second launch pings us (works
        // whether it was minimized, hidden to the tray, or just behind other windows).
        private void SurfaceMainWindow()
        {
            // Reuse the window's own tray-restore so a second launch surfaces us the same way the tray
            // "Open" does (window shown, tray icon cleared, brought to front) whether we were hidden in
            // the tray, minimized, or just behind other windows.
            if (MainWindow is MainWindow window) window.RestoreFromTray();
        }

        private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            EspionSpotify.Program.ReportException(e.Exception);
            e.Handled = true;
        }

        private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex) EspionSpotify.Program.ReportException(ex);
        }
    }
}
