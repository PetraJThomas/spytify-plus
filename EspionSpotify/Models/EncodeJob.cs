namespace EspionSpotify.Models
{
    /// <summary>
    /// An immutable snapshot of a finished capture, handed from the <see cref="Recorder"/>
    /// to the background <see cref="IEncodeService"/>. It carries everything the encoder
    /// needs so the recording path can return immediately and stay responsive.
    /// </summary>
    public class EncodeJob
    {
        /// <summary>Path to the flushed temp WAV holding the raw captured PCM.</summary>
        public string TempOriginalFile { get; set; }

        /// <summary>Snapshot of the track metadata (taken via the Track copy constructor).</summary>
        public Track Track { get; set; }

        /// <summary>The recorder's settings copy (media format, bitrate, output path, etc.).</summary>
        public UserSettings UserSettings { get; set; }

        /// <summary>Recorded length in seconds, used for the minimum-length check.</summary>
        public int CountSeconds { get; set; }
    }
}
