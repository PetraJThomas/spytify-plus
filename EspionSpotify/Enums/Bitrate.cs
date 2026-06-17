namespace EspionSpotify.Enums
{
    /// <summary>
    /// Output bitrate for lossy formats (MP3 via ffmpeg libmp3lame, Opus via libopus).
    /// Values are the kbps where meaningful; <see cref="Insane"/> is constant 320 CBR
    /// (distinct from the ABR 320 of <see cref="Kbps320"/>).
    /// </summary>
    public enum Bitrate
    {
        Kbps128 = 128,
        Kbps160 = 160,
        Kbps256 = 256,
        Kbps320 = 320,
        Insane = 321
    }
}
