using System;
using NAudio.Dsp;

namespace EspionSpotify.Wpf.Analysis
{
    internal enum SpectrogramPalette { Inferno, Magma, Viridis, Heat }

    /// <summary>A computed spectrogram: cached per-pixel dB (row 0 = highest freq) + BGRA pixels.</summary>
    internal sealed class SpectrogramImage
    {
        public int Width { get; set; }
        public int Height { get; set; }
        public float[] Db { get; set; }   // cached so re-colouring (palette switch) needs no re-FFT
        public byte[] Bgra { get; set; }
    }

    /// <summary>
    /// Short-time Fourier transform → Spek-style heat-map (X=time, Y=frequency, colour=dBFS).
    /// Compute() does the FFT once and caches dB; Colorize() maps dB→palette and can be re-run
    /// cheaply when the user changes palette.
    /// </summary>
    internal static class Spectrogram
    {
        private const int FftSize = 2048; // matches Spek's default window
        private const int FftM = 11;      // 2^11 == 2048

        public static SpectrogramImage Compute(AudioSample sample, int columns)
        {
            var samples = sample?.Mono;
            if (samples == null || samples.Length < FftSize || columns < 1) return null;

            var bins = FftSize / 2;
            var img = new SpectrogramImage
            {
                Width = columns,
                Height = bins,
                Db = new float[columns * bins],
                Bgra = new byte[columns * bins * 4]
            };

            var window = Hann(FftSize);
            var fft = new Complex[FftSize];
            var maxStart = samples.Length - FftSize;

            // Pass 1: store linear magnitudes and track the loudest bin in the whole file.
            // (NAudio's FFT applies its own scaling, so an absolute dB reference is unreliable —
            // normalising to the file's own peak is what gives a Spek-like image.)
            var peak = 1e-12;
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
                    var mag = Math.Sqrt(re * re + im * im);
                    img.Db[(bins - 1 - b) * columns + c] = (float)mag; // flip: low freq at the bottom
                    if (mag > peak) peak = mag;
                }
            }

            // Pass 2: convert to dB relative to that peak (loudest bin == 0 dB).
            for (var i = 0; i < img.Db.Length; i++)
                img.Db[i] = (float)(20.0 * Math.Log10((img.Db[i] + 1e-12) / peak));

            return img;
        }

        // Tighter floor (-105) + gamma < 1 lifts mid-level musical content into the bright part of
        // the palette, the way Spek stays vivid down to ~-90 dB instead of fading to dark.
        public static void Colorize(SpectrogramImage img, SpectrogramPalette palette,
            double minDb = -105, double maxDb = 0, double gamma = 0.78)
        {
            if (img?.Db == null) return;

            var anchors = GetAnchors(palette);
            var range = maxDb - minDb;

            for (var i = 0; i < img.Db.Length; i++)
            {
                var t = (img.Db[i] - minDb) / range;
                if (t < 0) t = 0; else if (t > 1) t = 1;
                t = Math.Pow(t, gamma);

                Map(anchors, t, out var r, out var g, out var b);
                var idx = i * 4;
                img.Bgra[idx] = b;
                img.Bgra[idx + 1] = g;
                img.Bgra[idx + 2] = r;
                img.Bgra[idx + 3] = 255;
            }
        }

        private static void Map(byte[][] c, double t, out byte r, out byte g, out byte b)
        {
            var n = c.Length - 1;
            var x = t * n;
            var lo = (int)x;
            if (lo >= n) { r = c[n][0]; g = c[n][1]; b = c[n][2]; return; }
            var f = x - lo;
            byte[] a = c[lo], d = c[lo + 1];
            r = (byte)(a[0] + (d[0] - a[0]) * f);
            g = (byte)(a[1] + (d[1] - a[1]) * f);
            b = (byte)(a[2] + (d[2] - a[2]) * f);
        }

        // Evenly-spaced anchor colours (low dB → high dB).
        public static byte[][] GetAnchors(SpectrogramPalette palette)
        {
            switch (palette)
            {
                case SpectrogramPalette.Viridis:
                    return new[]
                    {
                        B(68, 1, 84), B(71, 45, 123), B(59, 82, 139), B(44, 114, 142), B(33, 145, 140),
                        B(40, 174, 128), B(94, 201, 98), B(173, 220, 48), B(253, 231, 37)
                    };
                case SpectrogramPalette.Magma:
                    return new[]
                    {
                        B(0, 0, 4), B(24, 15, 62), B(69, 16, 119), B(114, 31, 129), B(159, 47, 127),
                        B(205, 64, 113), B(241, 96, 93), B(253, 149, 103), B(254, 202, 141), B(252, 253, 191)
                    };
                case SpectrogramPalette.Heat: // Spek-like: black→blue→purple→magenta→red→orange→yellow→white
                    return new[]
                    {
                        B(0, 0, 0), B(11, 11, 59), B(59, 15, 112), B(139, 26, 139), B(214, 32, 74),
                        B(243, 102, 27), B(255, 193, 0), B(255, 242, 122), B(255, 255, 255)
                    };
                default: // Inferno
                    return new[]
                    {
                        B(0, 0, 4), B(27, 12, 65), B(74, 12, 107), B(120, 28, 109), B(165, 44, 96),
                        B(207, 68, 70), B(237, 105, 37), B(251, 155, 6), B(247, 208, 60), B(252, 255, 164)
                    };
            }
        }

        private static byte[] B(byte r, byte g, byte b) => new[] { r, g, b };

        private static double[] Hann(int n)
        {
            var w = new double[n];
            for (var i = 0; i < n; i++)
                w[i] = 0.5 * (1 - Math.Cos(2 * Math.PI * i / (n - 1)));
            return w;
        }
    }
}
