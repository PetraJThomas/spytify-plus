using System;
using NAudio.Dsp;

namespace EspionSpotify.Wpf.Analysis
{
    /// <summary>A computed spectrogram as raw BGRA pixels (row 0 = highest frequency).</summary>
    internal sealed class SpectrogramImage
    {
        public int Width { get; set; }
        public int Height { get; set; }
        public byte[] Bgra { get; set; }
    }

    /// <summary>
    /// Short-time Fourier transform rendered to a viridis heat-map (purple→teal→green→yellow),
    /// the Spek-style view: X = time, Y = frequency (0..Nyquist bottom→top), colour = dBFS.
    /// </summary>
    internal static class Spectrogram
    {
        private const int FftSize = 2048; // matches Spek's default window
        private const int FftM = 11;      // 2^11 == 2048

        public static SpectrogramImage Compute(AudioSample sample, int columns,
            double minDb = -120, double maxDb = 0)
        {
            var samples = sample?.Mono;
            if (samples == null || samples.Length < FftSize || columns < 1) return null;

            var bins = FftSize / 2;
            var img = new SpectrogramImage { Width = columns, Height = bins, Bgra = new byte[columns * bins * 4] };

            var window = Hann(FftSize);
            var fft = new Complex[FftSize];
            var maxStart = samples.Length - FftSize;
            var refMag = FftSize / 4.0; // ~0 dBFS for a full-scale tone through a Hann window
            var range = maxDb - minDb;

            for (var c = 0; c < columns; c++)
            {
                var start = columns == 1 ? 0 : (int)((long)c * maxStart / (columns - 1));

                for (var i = 0; i < FftSize; i++)
                {
                    fft[i].X = (float)(samples[start + i] * window[i]);
                    fft[i].Y = 0f;
                }

                FastFourierTransform.FFT(true, FftM, fft);

                for (var b = 0; b < bins; b++)
                {
                    double re = fft[b].X, im = fft[b].Y;
                    var db = 20.0 * Math.Log10((Math.Sqrt(re * re + im * im) + 1e-12) / refMag);
                    var t = (db - minDb) / range;
                    if (t < 0) t = 0; else if (t > 1) t = 1;

                    Viridis(t, out var r, out var g, out var bl);
                    var idx = ((bins - 1 - b) * columns + c) * 4; // flip so low freq sits at the bottom
                    img.Bgra[idx] = bl;
                    img.Bgra[idx + 1] = g;
                    img.Bgra[idx + 2] = r;
                    img.Bgra[idx + 3] = 255;
                }
            }

            return img;
        }

        // Viridis anchor stops (perceptually-uniform: dark purple → blue → teal → green → yellow).
        private static readonly double[] Stops = { 0.0, 0.13, 0.25, 0.38, 0.5, 0.63, 0.75, 0.88, 1.0 };
        private static readonly byte[][] Colors =
        {
            new byte[] { 68, 1, 84 }, new byte[] { 71, 44, 122 }, new byte[] { 59, 81, 139 },
            new byte[] { 44, 113, 142 }, new byte[] { 33, 144, 141 }, new byte[] { 39, 173, 129 },
            new byte[] { 92, 200, 99 }, new byte[] { 170, 220, 50 }, new byte[] { 253, 231, 37 }
        };

        private static void Viridis(double t, out byte r, out byte g, out byte b)
        {
            var i = 1;
            while (i < Stops.Length - 1 && t > Stops[i]) i++;
            var lo = i - 1;
            var span = Stops[i] - Stops[lo];
            var f = span <= 0 ? 0 : (t - Stops[lo]) / span;
            r = (byte)(Colors[lo][0] + (Colors[i][0] - Colors[lo][0]) * f);
            g = (byte)(Colors[lo][1] + (Colors[i][1] - Colors[lo][1]) * f);
            b = (byte)(Colors[lo][2] + (Colors[i][2] - Colors[lo][2]) * f);
        }

        private static double[] Hann(int n)
        {
            var w = new double[n];
            for (var i = 0; i < n; i++)
                w[i] = 0.5 * (1 - Math.Cos(2 * Math.PI * i / (n - 1)));
            return w;
        }
    }
}
