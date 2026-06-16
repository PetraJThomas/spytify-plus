using System.Windows;
using System.Windows.Media;

namespace EspionSpotify.Wpf
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            // Spotify green as the system accent — drives toggles, slider, nav indicator,
            // selection, focus rings, etc.
            ModernWpf.ThemeManager.Current.AccentColor = Color.FromRgb(0x1E, 0xD7, 0x60);
            base.OnStartup(e);
        }
    }
}
