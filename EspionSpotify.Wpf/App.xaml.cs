using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace EspionSpotify.Wpf
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            // Spotify green as the system accent: drives toggles, slider, nav indicator,
            // selection, focus rings, etc.
            ModernWpf.ThemeManager.Current.AccentColor = Color.FromRgb(0x1E, 0xD7, 0x60);

            // Restore the engine's crash reporter that the old WinForms Program.Main wired up.
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;

            base.OnStartup(e);
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
