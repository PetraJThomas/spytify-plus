using System;

namespace EspionSpotify.Wpf.Analysis
{
    /// <summary>Decoded mono PCM plus the container facts ffmpeg reported for a file.</summary>
    internal sealed class AudioSample
    {
        public float[] Mono { get; set; }
        public int SampleRate { get; set; }
        public string Codec { get; set; }
        public int? ContainerBitrateKbps { get; set; }
        public int? AudioBitrateKbps { get; set; }
        public TimeSpan Duration { get; set; }

        public double NyquistHz => SampleRate / 2.0;

        /// <summary>Real audio bitrate when ffmpeg reported it, else the container figure.</summary>
        public int? EffectiveBitrateKbps => AudioBitrateKbps ?? ContainerBitrateKbps;
    }
}
