using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using EspionSpotify.Wpf.Analysis;
using WShapes = System.Windows.Shapes;

namespace EspionSpotify.Wpf
{
    // Analyze tab: decode any audio file via the bundled ffmpeg, draw its waveform + averaged
    // spectrum, and estimate the real quality tier (incl. lossy-in-a-lossless-container).
    public partial class MainWindow
    {
        private AudioSample _analyzeSample;
        private QualityResult _analyzeResult;

        private static readonly Brush WaveBrush = Frozen(0x1E, 0xD7, 0x60, 0xFF);
        private static readonly Brush WaveMidBrush = Frozen(0xFF, 0xFF, 0xFF, 0x22);
        private static readonly Brush SpecBrush = Frozen(0x1E, 0xD7, 0x60, 0xFF);
        private static readonly Brush SpecFillBrush = Frozen(0x1E, 0xD7, 0x60, 0x33);
        private static readonly Brush GridBrush = Frozen(0xFF, 0xFF, 0xFF, 0x18);
        private static readonly Brush GridTextBrush = Frozen(0x88, 0x88, 0x88, 0xFF);
        private static readonly Brush CutoffBrush = Frozen(0xFF, 0xD5, 0x4F, 0xFF);

        private enum AnalyzeState { Empty, Busy, Results, Error }

        private void Analyze_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = SingleFileFrom(e) != null ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void Analyze_Drop(object sender, DragEventArgs e)
        {
            var path = SingleFileFrom(e);
            if (path != null) _ = AnalyzeFileAsync(path);
        }

        private void Analyze_OpenFile_Click(object sender, RoutedEventArgs e)
        {
            using (var dlg = new System.Windows.Forms.OpenFileDialog
            {
                Title = "Choose an audio file to analyze",
                Filter = "Audio files|*.flac;*.wav;*.mp3;*.m4a;*.aac;*.opus;*.ogg;*.wma;*.aiff;*.aif|All files|*.*"
            })
            {
                if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    _ = AnalyzeFileAsync(dlg.FileName);
            }
        }

        private static string SingleFileFrom(DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return null;
            return e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length == 1 ? files[0] : null;
        }

        private async Task AnalyzeFileAsync(string path)
        {
            SetAnalyzeState(AnalyzeState.Busy);
            try
            {
                var sample = await FfmpegDecoder.DecodeAsync(path).ConfigureAwait(true);
                var result = await Task.Run(() => QualityAnalyzer.Analyze(sample)).ConfigureAwait(true);

                _analyzeSample = sample;
                _analyzeResult = result;

                PopulateAnalyzeResults(path, sample, result);
                RenderWaveform();
                RenderSpectrum();
                SetAnalyzeState(AnalyzeState.Results);
            }
            catch (Exception ex)
            {
                _analyzeSample = null;
                _analyzeResult = null;
                AnalyzeErrorText.Text = "Couldn't analyze this file.\n" + ex.Message;
                SetAnalyzeState(AnalyzeState.Error);
            }
        }

        private void SetAnalyzeState(AnalyzeState state)
        {
            AnalyzeDropZone.Visibility = state == AnalyzeState.Empty ? Visibility.Visible : Visibility.Collapsed;
            AnalyzeBusy.Visibility = state == AnalyzeState.Busy ? Visibility.Visible : Visibility.Collapsed;
            AnalyzeResults.Visibility = state == AnalyzeState.Results ? Visibility.Visible : Visibility.Collapsed;
            AnalyzeError.Visibility = state == AnalyzeState.Error ? Visibility.Visible : Visibility.Collapsed;
        }

        private void PopulateAnalyzeResults(string path, AudioSample sample, QualityResult result)
        {
            AnalyzeFileName.Text = Path.GetFileName(path);

            var meta = $"{(sample.Codec ?? "?").ToUpperInvariant()}  ·  {sample.SampleRate / 1000.0:0.#} kHz";
            if (sample.ContainerBitrateKbps.HasValue) meta += $"  ·  {sample.ContainerBitrateKbps} kbps";
            if (sample.Duration > TimeSpan.Zero) meta += $"  ·  {sample.Duration:m\\:ss}";
            AnalyzeMeta.Text = meta;

            var (bg, fg) = TierColors(result.Tier);
            if (result.IsTranscode) { bg = Frozen(0xFF, 0xB7, 0x4D, 0xFF); fg = Brushes.Black; }
            VerdictBadge.Background = bg;
            VerdictTierText.Foreground = fg;
            VerdictTierText.Text = (result.IsTranscode ? "⚠ " : "") + result.TierLabel;
            VerdictText.Text = result.Verdict;
            VerdictDetail.Text = result.Detail;

            SpectrumCutoffLabel.Text = result.Tier == QualityTier.Unknown
                ? ""
                : $"cut-off ~{result.CutoffHz / 1000.0:0.0} kHz";
            SpectrumAxis.Text = $"0 to {result.NyquistHz / 1000.0:0.#} kHz  ·  gridlines every 5 kHz  ·  amber line = detected cut-off";
        }

        private static (Brush bg, Brush fg) TierColors(QualityTier tier)
        {
            switch (tier)
            {
                case QualityTier.Lossless: return (Frozen(0x1E, 0xD7, 0x60, 0xFF), Brushes.Black);
                case QualityTier.Kbps320: return (Frozen(0x8B, 0xC3, 0x4A, 0xFF), Brushes.Black);
                case QualityTier.Kbps256: return (Frozen(0xCD, 0xDC, 0x39, 0xFF), Brushes.Black);
                case QualityTier.Kbps192: return (Frozen(0xFF, 0xB7, 0x4D, 0xFF), Brushes.Black);
                case QualityTier.Kbps128: return (Frozen(0xFF, 0x8A, 0x65, 0xFF), Brushes.Black);
                case QualityTier.Low: return (Frozen(0xEF, 0x53, 0x50, 0xFF), Brushes.White);
                default: return (Frozen(0x9E, 0x9E, 0x9E, 0xFF), Brushes.Black);
            }
        }

        private void WaveformCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => RenderWaveform();
        private void SpectrumCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => RenderSpectrum();

        private void RenderWaveform()
        {
            var canvas = WaveformCanvas;
            canvas.Children.Clear();
            if (_analyzeSample?.Mono == null) return;

            double width = canvas.ActualWidth, height = canvas.ActualHeight;
            if (width < 2 || height < 2) return;

            var mid = height / 2.0;
            var halfH = mid * 0.92;
            var peaks = WaveformPeaks.Build(_analyzeSample.Mono, (int)width);

            canvas.Children.Add(new WShapes.Line
            {
                X1 = 0, Y1 = mid, X2 = width, Y2 = mid, Stroke = WaveMidBrush, StrokeThickness = 0.5
            });

            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                for (var x = 0; x < peaks.Length; x++)
                {
                    var yMax = mid - peaks[x].Max * halfH;
                    var yMin = mid - peaks[x].Min * halfH;
                    if (Math.Abs(yMax - yMin) < 0.5) yMin = yMax + 0.5;
                    ctx.BeginFigure(new Point(x + 0.5, yMax), false, false);
                    ctx.LineTo(new Point(x + 0.5, yMin), true, false);
                }
            }
            geo.Freeze();
            canvas.Children.Add(new WShapes.Path { Data = geo, Stroke = WaveBrush, StrokeThickness = 1 });
        }

        private void RenderSpectrum()
        {
            var canvas = SpectrumCanvas;
            canvas.Children.Clear();
            var result = _analyzeResult;
            if (result?.Spectrum == null || result.Spectrum.Length == 0) return;

            double width = canvas.ActualWidth, height = canvas.ActualHeight;
            if (width < 2 || height < 2) return;

            var nyquist = result.NyquistHz <= 0 ? 22050 : result.NyquistHz;
            const double minDb = -90, maxDb = 0;

            double X(double f) => f / nyquist * width;
            double Y(double db)
            {
                var t = (db - minDb) / (maxDb - minDb);
                if (t < 0) t = 0; else if (t > 1) t = 1;
                return height - t * height;
            }

            // Frequency gridlines + labels every 5 kHz.
            for (var f = 5000; f < nyquist; f += 5000)
            {
                var gx = X(f);
                canvas.Children.Add(new WShapes.Line
                {
                    X1 = gx, Y1 = 0, X2 = gx, Y2 = height, Stroke = GridBrush, StrokeThickness = 0.5
                });
                var label = new System.Windows.Controls.TextBlock
                {
                    Text = $"{f / 1000}k", Foreground = GridTextBrush, FontSize = 10
                };
                System.Windows.Controls.Canvas.SetLeft(label, gx + 2);
                System.Windows.Controls.Canvas.SetTop(label, height - 14);
                canvas.Children.Add(label);
            }

            // Spectrum curve (filled to the baseline).
            var fill = new StreamGeometry();
            using (var ctx = fill.Open())
            {
                ctx.BeginFigure(new Point(0, height), true, false);
                for (var i = 1; i < result.Spectrum.Length; i++)
                    ctx.LineTo(new Point(X(result.Spectrum[i].FrequencyHz), Y(result.Spectrum[i].Db)), true, false);
                ctx.LineTo(new Point(width, height), true, false);
            }
            fill.Freeze();
            canvas.Children.Add(new WShapes.Path { Data = fill, Fill = SpecFillBrush, Stroke = SpecBrush, StrokeThickness = 1 });

            // Detected cut-off marker.
            if (result.CutoffHz > 0 && result.CutoffHz < nyquist)
            {
                var cx = X(result.CutoffHz);
                canvas.Children.Add(new WShapes.Line
                {
                    X1 = cx, Y1 = 0, X2 = cx, Y2 = height,
                    Stroke = CutoffBrush, StrokeThickness = 1.5,
                    StrokeDashArray = new DoubleCollection { 3, 3 }
                });
            }
        }

        private static SolidColorBrush Frozen(byte r, byte g, byte b, byte a)
        {
            var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
            brush.Freeze();
            return brush;
        }
    }
}
