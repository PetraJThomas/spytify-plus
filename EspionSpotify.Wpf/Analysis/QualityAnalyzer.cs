using System;
using NAudio.Dsp;

namespace EspionSpotify.Wpf.Analysis
{
    /// <summary>
    /// Estimates the real audio quality of a decoded sample from its averaged frequency spectrum.
    /// Lossy encoders apply a low-pass whose cut-off ("cliff") betrays the bitrate; lossless
    /// content reaches close to Nyquist. The container codec is combined with the measured
    /// cut-off so a lossy source hidden inside a FLAC/ALAC wrapper is flagged as a transcode.
    /// </summary>
    internal static class QualityAnalyzer
    {
        private const int FftSize = 4096;
        private const int FftM = 12; // 2^12 == 4096
        private const double FloorDb = -60.0;

        public static QualityResult Analyze(AudioSample sample)
        {
            var samples = sample.Mono;
            var sr = sample.SampleRate;
            var bins = FftSize / 2;
            var nyquist = sr / 2.0;

            var result = new QualityResult { NyquistHz = nyquist, Spectrum = Array.Empty<SpectrumPoint>() };

            if (samples == null || samples.Length < FftSize)
            {
                result.Tier = QualityTier.Unknown;
                result.TierLabel = "Unknown";
                result.Verdict = "Not enough audio to analyze.";
                return result;
            }

            var window = Hann(FftSize);
            var accum = new double[bins];
            var fft = new Complex[FftSize];
            var hop = FftSize / 2;
            var frames = 0;

            for (var pos = 0; pos + FftSize <= samples.Length; pos += hop)
            {
                double energy = 0;
                for (var i = 0; i < FftSize; i++)
                {
                    double s = samples[pos + i];
                    energy += s * s;
                }

                if (Math.Sqrt(energy / FftSize) < 1e-4) continue; // skip near-silent frames

                for (var i = 0; i < FftSize; i++)
                {
                    fft[i].X = (float)(samples[pos + i] * window[i]);
                    fft[i].Y = 0f;
                }

                FastFourierTransform.FFT(true, FftM, fft);

                for (var i = 0; i < bins; i++)
                {
                    double re = fft[i].X, im = fft[i].Y;
                    accum[i] += Math.Sqrt(re * re + im * im);
                }

                frames++;
            }

            if (frames == 0)
            {
                result.Tier = QualityTier.Unknown;
                result.TierLabel = "Unknown";
                result.Verdict = "Track is silent or too short to analyze.";
                return result;
            }

            var peak = 1e-12;
            for (var i = 0; i < bins; i++)
            {
                accum[i] /= frames;
                if (accum[i] > peak) peak = accum[i];
            }

            var binHz = (double)sr / FftSize;
            var db = new double[bins];
            var spectrum = new SpectrumPoint[bins];
            for (var i = 0; i < bins; i++)
            {
                db[i] = 20.0 * Math.Log10((accum[i] + 1e-12) / peak);
                spectrum[i] = new SpectrumPoint { FrequencyHz = i * binHz, Db = db[i] };
            }

            result.Spectrum = spectrum;

            // Cut-off = highest frequency whose locally-smoothed level still clears the floor.
            const int smooth = 3;
            var cutoffBin = 0;
            for (var i = bins - 1; i >= 0; i--)
            {
                double s = 0;
                var c = 0;
                for (var k = i - smooth; k <= i + smooth; k++)
                    if (k >= 0 && k < bins) { s += db[k]; c++; }

                if (s / c > FloorDb) { cutoffBin = i; break; }
            }

            result.CutoffHz = cutoffBin * binHz;
            result.Confidence = Confidence(db, cutoffBin, binHz);
            AssignTier(result, result.CutoffHz, nyquist, sample.Codec);
            return result;
        }

        private static void AssignTier(QualityResult r, double cutoff, double nyquist, string codec)
        {
            QualityTier tier;
            string label;

            if (cutoff >= nyquist - 1000 && nyquist >= 21000) { tier = QualityTier.Lossless; label = "Lossless (full-band)"; }
            else if (cutoff >= 20000) { tier = QualityTier.Kbps320; label = "~320 kbps"; }
            else if (cutoff >= 18500) { tier = QualityTier.Kbps256; label = "~256 kbps"; }
            else if (cutoff >= 17000) { tier = QualityTier.Kbps192; label = "~192 kbps"; }
            else if (cutoff >= 15500) { tier = QualityTier.Kbps128; label = "~128 kbps"; }
            else { tier = QualityTier.Low; label = "≤96 kbps / heavily compressed"; }

            r.Tier = tier;
            r.TierLabel = label;

            var losslessContainer = IsLosslessCodec(codec);
            r.IsTranscode = losslessContainer && tier != QualityTier.Lossless;

            var cutoffKHz = cutoff / 1000.0;
            var nyqKHz = nyquist / 1000.0;
            var codecUp = string.IsNullOrEmpty(codec) ? "?" : codec.ToUpperInvariant();

            if (tier == QualityTier.Lossless)
            {
                r.Verdict = losslessContainer ? "True lossless" : "Full-band (lossless-grade)";
                r.Detail = $"Content reaches ~{cutoffKHz:0.0} kHz (Nyquist {nyqKHz:0.0} kHz) with no early roll-off.";
            }
            else if (r.IsTranscode)
            {
                r.Verdict = $"Lossy source in a lossless container ({label})";
                r.Detail = $"Container is {codecUp} but the spectrum cuts off at ~{cutoffKHz:0.0} kHz: " +
                           "re-encoded from a lossy file, not true lossless.";
            }
            else
            {
                r.Verdict = $"Lossy: {label}";
                r.Detail = $"Spectral cut-off at ~{cutoffKHz:0.0} kHz.";
            }
        }

        private static bool IsLosslessCodec(string codec)
        {
            if (string.IsNullOrEmpty(codec)) return false;
            codec = codec.ToLowerInvariant();
            return codec.Contains("flac") || codec.Contains("alac") || codec.StartsWith("pcm") ||
                   codec.Contains("wav") || codec.Contains("wmalossless") || codec.Contains("ape") ||
                   codec.Contains("tak") || codec.Contains("truehd") || codec.Contains("wavpack");
        }

        // Sharper drop just above the cut-off => more confident it's a deliberate low-pass.
        private static double Confidence(double[] db, int cutoffBin, double binHz)
        {
            if (cutoffBin <= 0 || cutoffBin >= db.Length - 1) return 0.5;
            var ahead = Math.Max(1, (int)(1000 / binHz));
            var j = Math.Min(db.Length - 1, cutoffBin + ahead);
            var drop = db[cutoffBin] - db[j];
            var c = drop / 40.0;
            return c < 0 ? 0 : (c > 1 ? 1 : c);
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
