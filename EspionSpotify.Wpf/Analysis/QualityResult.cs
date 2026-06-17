namespace EspionSpotify.Wpf.Analysis
{
    internal enum QualityTier
    {
        Lossless,
        Kbps320,
        Kbps256,
        Kbps192,
        Kbps128,
        Low,
        Unknown
    }

    /// <summary>One point of the averaged magnitude spectrum (dB relative to the peak bin).</summary>
    internal struct SpectrumPoint
    {
        public double FrequencyHz;
        public double Db;
    }

    internal sealed class QualityResult
    {
        public QualityTier Tier { get; set; }
        public string TierLabel { get; set; }
        public double CutoffHz { get; set; }
        public double NyquistHz { get; set; }
        public double Confidence { get; set; } // 0..1, how hard the roll-off is
        public bool IsTranscode { get; set; }
        public string Verdict { get; set; }    // headline line
        public string Detail { get; set; }     // secondary explanation
        public SpectrumPoint[] Spectrum { get; set; }
    }
}
