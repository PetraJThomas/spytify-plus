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
                result.TierLabel = Tr("anzTierUnknown");
                result.Verdict = Tr("anzNotEnough");
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
                result.TierLabel = Tr("anzTierUnknown");
                result.Verdict = Tr("anzSilent");
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
            AssignTier(result, result.CutoffHz, nyquist, sample.Codec, sample.EffectiveBitrateKbps);
            return result;
        }

        private static string Tr(string key) => Loc.Instance[key];
        private static string Trf(string key, params object[] args) => string.Format(Loc.Instance[key], args);

        private static void AssignTier(QualityResult r, double cutoff, double nyquist, string codec, int? bitrateKbps)
        {
            var cutoffKHz = (cutoff / 1000.0).ToString("0.0");
            var nyqKHz = (nyquist / 1000.0).ToString("0.0");
            var codecUp = string.IsNullOrEmpty(codec) ? "?" : codec.ToUpperInvariant();
            var fullBand = cutoff >= nyquist - 1000 && nyquist >= 21000;
            var (cTier, cLabel) = CutoffTier(cutoff, nyquist);
            r.IsTranscode = false;

            // A lossy codec is never lossless, however much bandwidth it kept (high-bitrate AAC/Opus
            // at 48 kHz often reach Nyquist with no cliff). The codec is authoritative; the spectrum
            // only describes the bandwidth.
            if (IsLossyCodec(codec))
            {
                r.Tier = bitrateKbps.HasValue ? BitrateColor(bitrateKbps.Value) : cTier;
                r.TierLabel = bitrateKbps.HasValue ? $"{codecUp} {bitrateKbps} kbps" : Trf("anzTierLossy", codecUp);
                r.Verdict = bitrateKbps.HasValue ? Trf("anzVLossyBitrate", codecUp, bitrateKbps.Value) : Trf("anzVLossy", codecUp);
                r.Detail = fullBand ? Trf("anzDLossyFull", codecUp, cutoffKHz) : Trf("anzDLossyCut", codecUp, cutoffKHz);
                return;
            }

            // A lossless codec: full-band is genuinely lossless; an early cut-off means a lossy source
            // was re-wrapped (transcode).
            if (IsLosslessCodec(codec))
            {
                if (fullBand)
                {
                    r.Tier = QualityTier.Lossless;
                    r.TierLabel = Tr("anzTierLossless");
                    r.Verdict = Tr("anzVTrueLossless");
                    var rate = bitrateKbps.HasValue ? Trf("anzDActualBitrate", bitrateKbps.Value) : "";
                    r.Detail = Trf("anzDLossless", cutoffKHz, nyqKHz) + rate;
                }
                else
                {
                    r.Tier = cTier;
                    r.TierLabel = cLabel;
                    r.IsTranscode = true;
                    r.Verdict = Trf("anzVTranscode", cLabel);
                    r.Detail = Trf("anzDTranscode", codecUp, cutoffKHz);
                }
                return;
            }

            // Unknown codec: fall back to the spectral estimate (and say so).
            r.Tier = cTier;
            r.TierLabel = cLabel;
            if (cTier == QualityTier.Lossless)
            {
                r.Verdict = Tr("anzVFullBand");
                var rate = bitrateKbps.HasValue ? Trf("anzDActualBitrate", bitrateKbps.Value) : "";
                r.Detail = Trf("anzDUnknown", cutoffKHz, rate, codecUp);
            }
            else
            {
                r.Verdict = Trf("anzVLossy", cLabel);
                r.Detail = Trf("anzDSpectralCut", cutoffKHz);
            }
        }

        private static (QualityTier tier, string label) CutoffTier(double cutoff, double nyquist)
        {
            if (cutoff >= nyquist - 1000 && nyquist >= 21000) return (QualityTier.Lossless, Tr("anzTierLossless"));
            if (cutoff >= 20000) return (QualityTier.Kbps320, "~320 kbps");
            if (cutoff >= 18500) return (QualityTier.Kbps256, "~256 kbps");
            if (cutoff >= 17000) return (QualityTier.Kbps192, "~192 kbps");
            if (cutoff >= 15500) return (QualityTier.Kbps128, "~128 kbps");
            return (QualityTier.Low, Tr("anzTierLow"));
        }

        // Badge colour for a lossy file, from its actual bitrate (codec-agnostic, rough).
        private static QualityTier BitrateColor(int kbps)
        {
            if (kbps >= 256) return QualityTier.Kbps320;
            if (kbps >= 192) return QualityTier.Kbps256;
            if (kbps >= 144) return QualityTier.Kbps192;
            if (kbps >= 96) return QualityTier.Kbps128;
            return QualityTier.Low;
        }

        private static bool IsLosslessCodec(string codec)
        {
            if (string.IsNullOrEmpty(codec)) return false;
            codec = codec.ToLowerInvariant();
            return codec.Contains("flac") || codec.Contains("alac") || codec.StartsWith("pcm") ||
                   codec.Contains("wav") || codec.Contains("wmalossless") || codec.Contains("ape") ||
                   codec.Contains("tak") || codec.Contains("truehd") || codec.Contains("wavpack");
        }

        private static bool IsLossyCodec(string codec)
        {
            if (string.IsNullOrEmpty(codec)) return false;
            codec = codec.ToLowerInvariant();
            if (codec.Contains("wmalossless")) return false; // lossless WMA is handled as lossless
            return codec.Contains("aac") || codec.Contains("mp3") || codec.Contains("mp2") ||
                   codec.Contains("mpeg") || codec.Contains("opus") || codec.Contains("vorbis") ||
                   codec.Contains("ogg") || codec.Contains("ac3") || codec.Contains("eac3") ||
                   codec.Contains("wma") || codec.Contains("dts") || codec.Contains("amr") ||
                   codec.Contains("atrac") || codec.Contains("musepack") || codec.Contains("speex") ||
                   codec.Contains("cook") || codec.Contains("sipr");
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
