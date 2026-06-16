using System.Windows.Media;

namespace EspionSpotify.Wpf
{
    /// <summary>One line in the recording console: timestamp + optional colored status type + message.</summary>
    public class LogLine
    {
        public string Time { get; set; }
        public string Type { get; set; }
        public string Message { get; set; }
        public Brush TypeBrush { get; set; }
        public bool HasType => !string.IsNullOrEmpty(Type);
    }
}
