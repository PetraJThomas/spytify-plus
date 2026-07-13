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
            _instanceMutex = new Mutex(true, InstanceMutexName, out var isFirstInstance);
            if (!isFirstInstance)
            {
                // Already running: ask the live instance to surface its window, then exit quietly so we
                // never spawn a duplicate that fights over the OAuth callback port.
                try { EventWaitHandle.OpenExisting(ShowWindowEventName).Set(); } catch { /* ignored */ }
                Shutdown();
                return;
            }

            // We are the one true instance: listen for later launches asking us to come to the front.
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
            var window = MainWindow;
            if (window == null) return;
            window.Show();
            if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
            window.Activate();
            window.Topmost = true;
            window.Topmost = false;
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
