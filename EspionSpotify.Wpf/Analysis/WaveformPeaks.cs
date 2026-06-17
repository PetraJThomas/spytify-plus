using System;

namespace EspionSpotify.Wpf.Analysis
{
    internal static class WaveformPeaks
    {
        /// <summary>Min/max amplitude per horizontal bucket, for drawing an amplitude waveform.</summary>
        public static (float Min, float Max)[] Build(float[] samples, int buckets)
        {
            if (samples == null || samples.Length == 0 || buckets <= 0)
                return new (float, float)[0];

            buckets = Math.Min(buckets, samples.Length);
            var peaks = new (float Min, float Max)[buckets];
            var per = (double)samples.Length / buckets;

            for (var i = 0; i < buckets; i++)
            {
                var start = (int)(i * per);
                var end = (int)((i + 1) * per);
                if (end <= start) end = start + 1;
                if (end > samples.Length) end = samples.Length;

                float min = float.MaxValue, max = float.MinValue;
                for (var j = start; j < end; j++)
                {
                    var v = samples[j];
                    if (v < min) min = v;
                    if (v > max) max = v;
                }

                if (min > max) { min = 0f; max = 0f; }
                peaks[i] = (min, max);
            }

            return peaks;
        }
    }
}
