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

        // Cut-off thresholds (see DetectCutoff). A brick-wall low-pass is a STEEP, DEEP local drop:
        // the level falls past DeadDepthDb within a ~2 kHz window into a plateau that is genuinely
        // dead. Measured locally at the edge so the rising near-Nyquist noise of lossy files can't
        // hide the cut. Easy to calibrate here; the huge gap to natural roll-off (<10 dB/2 kHz) is the
        // safety margin.
        private const double DeadDepthDb = 25.0;    // in-band -> plateau must drop at least this much over ~2 kHz
        private const double AboveDeadDb = -55.0;   // ...and the plateau must itself sit at least this far below peak

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

            // Cut-off detection: an ensemble of three independent passes that must corroborate
            // before we call an early cut-off. A lossy low-pass leaves a brick-wall cliff with a
            // flat "dead band" above; natural (lossless) roll-off declines gradually and keeps real
            // content to Nyquist. No single threshold decides it: the passes vote.
            var (cutoffHz, confidence, diag) = DetectCutoff(db, accum, binHz, bins, nyquist);
            result.CutoffHz = cutoffHz;
            result.Confidence = confidence;
            result.Diagnostics = diag;
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

        // A lossy low-pass leaves a brick-wall cliff: the level falls steeply into a dead plateau. A
        // natural (lossless) roll-off declines gradually and keeps real content to Nyquist. We scan
        // for the highest edge where the level drops at least DeadDepthDb within a ~2 kHz window into
        // a plateau that is itself dead. The window is LOCAL to the edge, so the rising quantisation-
        // noise floor lossy codecs leave near Nyquist cannot mask the cut (the old global measure was
        // fooled exactly there). Returns the cut-off (Nyquist when full-band), confidence, and a
        // calibration line.
        private static (double cutoffHz, double confidence, string diag) DetectCutoff(
            double[] db, double[] mag, double binHz, int bins, double nyquist)
        {
            var sdb = Smooth(db, 3);
            var pre = HzBins(1500, binHz);     // in-band reference window, just below the edge
            var gap = HzBins(200, binHz);      // skip the transition band itself
            var win = HzBins(2000, binHz);     // dead-plateau window, just above the edge
            var minAbove = HzBins(700, binHz); // need at least this much band above to judge a plateau
            var loBin = HzBins(7000, binHz);   // do not hunt cut-offs below ~7 kHz

            // Scan downward: the first (highest) edge with a steep, deep local drop is the cut.
            var cutBin = -1;
            double cBelow = 0, cAbove = 0;
            for (var i = bins - 1 - minAbove; i >= loBin; i--)
            {
                var below = MeanDb(sdb, i - pre, i - gap, bins);
                var above = MeanDb(sdb, i + gap, Math.Min(bins - 1, i + win), bins);
                if (below - above >= DeadDepthDb && above <= AboveDeadDb)
                {
                    cutBin = i; cBelow = below; cAbove = above;
                    break;
                }
            }

            // Multi-level crossings: printed for calibration (and flatness, info-only).
            var f40 = CrossingHz(sdb, -40, binHz, bins);
            var f60 = CrossingHz(sdb, -60, binHz, bins);
            var f80 = CrossingHz(sdb, -80, binHz, bins);

            if (cutBin < 0)
                return (nyquist, 0.9, string.Format(
                    "cross -40/-60/-80={0:0}/{1:0}/{2:0}Hz | no local drop >= {3:0}dB into a dead plateau " +
                    "=> full-band", f40, f60, f80, DeadDepthDb));

            // Place the line at the cliff edge: scanning UP from in-band, the first frequency whose
            // level falls below the cliff mid-point and STAYS below for a few hundred Hz. The "stays
            // below" hold makes the line ignore faint noise-floor bins that poke up inside the dead
            // plateau, which would otherwise drag the line up into the noise.
            var mid = (cBelow + cAbove) / 2.0;
            var hold = HzBins(400, binHz);
            var hi = Math.Min(bins - 1, cutBin + win);
            var edge = cutBin;
            for (var k = Math.Max(0, cutBin - pre); k <= hi; k++)
            {
                if (sdb[k] >= mid) continue;
                var stays = true;
                for (var m = k; m < Math.Min(bins, k + hold); m++)
                    if (sdb[m] >= mid) { stays = false; break; }
                if (stays) { edge = k; break; }
            }
            var cut = edge * binHz;
            var deadDepth = cBelow - cAbove;
            var flatness = Flatness(mag, cutBin + gap, Math.Min(bins, cutBin + win));

            var diag = string.Format(
                "cut~{0:0}Hz | below/above={1:0.0}/{2:0.0}dB deadDepth={3:0.0}dB (>= {4:0}) " +
                "| cross -40/-60/-80={5:0}/{6:0}/{7:0}Hz flatness={8:0.00} => cut-off",
                cut, cBelow, cAbove, deadDepth, DeadDepthDb, f40, f60, f80, flatness);

            var conf = 0.7 + Math.Min(0.29, (deadDepth - DeadDepthDb) / 100.0);
            return (cut, conf, diag);
        }

        private static int HzBins(double hz, double binHz) => Math.Max(1, (int)Math.Round(hz / binHz));

        // Mean smoothed dB over the inclusive bin range [from, to], clamped to the spectrum.
        private static double MeanDb(double[] sdb, int from, int to, int bins)
        {
            if (from < 0) from = 0;
            if (to > bins - 1) to = bins - 1;
            if (to < from) return -120.0;
            double s = 0;
            var c = 0;
            for (var i = from; i <= to; i++) { s += sdb[i]; c++; }
            return c == 0 ? -120.0 : s / c;
        }

        private static double[] Smooth(double[] x, int radius)
        {
            var y = new double[x.Length];
            for (var i = 0; i < x.Length; i++)
            {
                double s = 0;
                var c = 0;
                for (var k = i - radius; k <= i + radius; k++)
                    if (k >= 0 && k < x.Length) { s += x[k]; c++; }
                y[i] = s / c;
            }
            return y;
        }

        // Highest frequency whose smoothed level still clears the given dB line (relative to peak).
        private static double CrossingHz(double[] sdb, double thresholdDb, double binHz, int bins)
        {
            for (var i = bins - 1; i >= 0; i--)
                if (sdb[i] >= thresholdDb) return i * binHz;
            return 0;
        }

        // Wiener entropy of magnitudes over [from, to): geometric mean / arithmetic mean. 1 == flat.
        private static double Flatness(double[] mag, int from, int to)
        {
            if (from < 1) from = 1;
            if (to > mag.Length) to = mag.Length;
            if (to - from < 8) return 0; // too few bins above the cut to judge
            double logSum = 0, sum = 0;
            var n = 0;
            for (var i = from; i < to; i++)
            {
                var m = mag[i] + 1e-12;
                logSum += Math.Log(m);
                sum += m;
                n++;
            }
            if (n == 0 || sum <= 0) return 0;
            var geo = Math.Exp(logSum / n);
            var arith = sum / n;
            return geo / arith;
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
