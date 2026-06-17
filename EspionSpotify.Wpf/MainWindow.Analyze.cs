using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using EspionSpotify.Wpf.Analysis;
using WShapes = System.Windows.Shapes;

namespace EspionSpotify.Wpf
{
    // Analyze tab: decode any audio file via the bundled ffmpeg, draw its waveform, a Spek-style
    // spectrogram and a vertical frequency profile, and estimate the real quality tier
    // (including lossy audio hidden inside a lossless container).
    public partial class MainWindow
    {
        private AudioSample _analyzeSample;
        private QualityResult _analyzeResult;
        private SpectrogramImage _spectrogram;
        private SpectrogramPalette _palette = SpectrogramPalette.Inferno;

        private static readonly Brush WaveBrush = Frozen(0x1E, 0xD7, 0x60, 0xFF);
        private static readonly Brush WaveMidBrush = Frozen(0xFF, 0xFF, 0xFF, 0x22);
        private static readonly Brush ProfileStroke = Frozen(0x1E, 0xD7, 0x60, 0xFF);
        private static readonly Brush ProfileFill = Frozen(0x1E, 0xD7, 0x60, 0x44);
        private static readonly Brush AxisTextBrush = Frozen(0x99, 0x99, 0x99, 0xFF);
        private static readonly Brush GridLineBrush = Frozen(0xFF, 0xFF, 0xFF, 0x14);
        private static readonly Brush CutoffBrush = Frozen(0xFF, 0xE4, 0x5C, 0xFF);
        private static readonly Brush CutoffHaloBrush = Frozen(0x00, 0x00, 0x00, 0xCC);

        private enum AnalyzeState { Empty, Busy, Results, Error }

        private bool _dragShown;

        private void Analyze_DragOver(object sender, DragEventArgs e)
        {
            var ok = SingleFileFrom(e) != null;
            e.Effects = ok ? DragDropEffects.Copy : DragDropEffects.None;
            if (ok && !_dragShown) ShowDragOverlay(true);
            e.Handled = true;
        }

        private void Analyze_DragLeave(object sender, DragEventArgs e)
        {
            // DragLeave also fires when crossing child elements; only hide once the pointer has
            // actually left the panel's bounds, otherwise the overlay flickers.
            var p = e.GetPosition(AnalyzePanel);
            if (p.X < 0 || p.Y < 0 || p.X > AnalyzePanel.ActualWidth || p.Y > AnalyzePanel.ActualHeight)
                ShowDragOverlay(false);
            e.Handled = true;
        }

        private void Analyze_Drop(object sender, DragEventArgs e)
        {
            ShowDragOverlay(false);
            var path = SingleFileFrom(e);
            if (path != null) _ = AnalyzeFileAsync(path);
        }

        private void InfoButton_Click(object sender, RoutedEventArgs e) => ShowInfoOverlay(true);
        private void InfoClose_Click(object sender, RoutedEventArgs e) => ShowInfoOverlay(false);
        private void InfoOverlay_BackdropClick(object sender, MouseButtonEventArgs e) => ShowInfoOverlay(false);
        private void InfoCard_Click(object sender, MouseButtonEventArgs e) => e.Handled = true; // clicks inside the card don't dismiss

        private void ShowInfoOverlay(bool show)
        {
            if (show)
            {
                InfoOverlay.Visibility = Visibility.Visible;
                InfoOverlay.BeginAnimation(OpacityProperty,
                    new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)));
            }
            else
            {
                var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(140));
                fade.Completed += (s, e2) => InfoOverlay.Visibility = Visibility.Collapsed;
                InfoOverlay.BeginAnimation(OpacityProperty, fade);
            }
        }

        private void ShowDragOverlay(bool show)
        {
            _dragShown = show;
            if (show)
            {
                AnalyzeDragOverlay.Visibility = Visibility.Visible;
                AnalyzeDragOverlay.BeginAnimation(OpacityProperty,
                    new DoubleAnimation(1, TimeSpan.FromMilliseconds(120)));
            }
            else
            {
                var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(120));
                fade.Completed += (s, e2) => { if (!_dragShown) AnalyzeDragOverlay.Visibility = Visibility.Collapsed; };
                AnalyzeDragOverlay.BeginAnimation(OpacityProperty, fade);
            }
        }

        private void Analyze_OpenFile_Click(object sender, RoutedEventArgs e)
        {
            using (var dlg = new System.Windows.Forms.OpenFileDialog
            {
                Title = "Choose an audio file to analyze",
                Filter = "Audio files|*.flac;*.wav;*.mp3;*.m4a;*.aac;*.opus;*.ogg;*.wma;*.aiff;*.aif|All files|*.*",
                InitialDirectory = Directory.Exists(OutputPath) ? OutputPath : ""
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
                var spectrogram = await Task.Run(() =>
                {
                    var s = Spectrogram.Compute(sample, 1600);
                    Spectrogram.Colorize(s, _palette);
                    return s;
                }).ConfigureAwait(true);

                _analyzeSample = sample;
                _analyzeResult = result;
                _spectrogram = spectrogram;

                PopulateAnalyzeResults(path, sample, result);
                UpdateLegend();
                RenderWaveform();
                RenderSpectrogram();
                RenderSpectrogramAxes();
                RenderFrequencyResponse();
                SetAnalyzeState(AnalyzeState.Results);
            }
            catch (Exception ex)
            {
                _analyzeSample = null;
                _analyzeResult = null;
                _spectrogram = null;
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

            if (state == AnalyzeState.Results) AnimateResultsIn();
            else StopTierPulse();
        }

        // Fade + slide the results panel up as they appear.
        private void AnimateResultsIn()
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            AnalyzeResults.BeginAnimation(OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300)) { EasingFunction = ease });

            if (!(AnalyzeResults.RenderTransform is TranslateTransform tt))
            {
                tt = new TranslateTransform();
                AnalyzeResults.RenderTransform = tt;
            }
            tt.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(16, 0, TimeSpan.FromMilliseconds(340)) { EasingFunction = ease });
        }

        private bool _pulseLossless;

        // Tier indicator lives on/around the ring behind the badge, so the badge never scales or glows.
        // Lossless gets a glowing comet that orbits the border (a single light travelling the perimeter).
        // Lossy tiers get a static coloured ring whose glow pulses slower and dimmer the lower the bitrate.
        private void StartTierPulse(QualityTier tier, Color glow)
        {
            StopTierPulse();

            if (tier == QualityTier.Lossless)
            {
                _pulseLossless = true;
                BadgeRing.Background = new SolidColorBrush(Color.FromRgb(0x1E, 0xD7, 0x60));
                StartCometOrbit();
                return;
            }

            double seconds, maxOpacity, maxBlur;
            switch (tier)
            {
                case QualityTier.Kbps320: seconds = 1.4; maxOpacity = 0.80; maxBlur = 22; break;
                case QualityTier.Kbps256: seconds = 1.6; maxOpacity = 0.70; maxBlur = 20; break;
                case QualityTier.Kbps192: seconds = 1.9; maxOpacity = 0.60; maxBlur = 18; break;
                case QualityTier.Kbps128: seconds = 2.2; maxOpacity = 0.50; maxBlur = 16; break;
                default: seconds = 2.6; maxOpacity = 0.42; maxBlur = 14; break;
            }

            var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
            BadgeRing.Background = new SolidColorBrush(glow);
            var effect = new DropShadowEffect { Color = glow, ShadowDepth = 0, BlurRadius = 8, Opacity = 0.15, RenderingBias = RenderingBias.Performance };
            BadgeRing.Effect = effect;
            var dur = new Duration(TimeSpan.FromSeconds(seconds));
            effect.BeginAnimation(DropShadowEffect.BlurRadiusProperty, new DoubleAnimation(8, maxBlur, dur) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease });
            effect.BeginAnimation(DropShadowEffect.OpacityProperty, new DoubleAnimation(0.15, maxOpacity, dur) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease });
        }

        // A single continuous light streak that flows around the badge's rounded-rect border, drawn
        // as a stroked path with one visible dash whose offset is animated (no rotating gradient, so
        // no mirror/wobble). Two layers: a soft green glow body and a hard bright core inside it.
        private static readonly Color CometColor = Color.FromRgb(0x1E, 0xD7, 0x60);
        private static readonly Brush CometHeadBrush = Frozen(0xEA, 0xFF, 0xF2, 0xFF);
        private static readonly Brush CometTrailBrush = Frozen(0x1E, 0xD7, 0x60, 0xFF);

        private void StartCometOrbit()
        {
            BadgeOrbit.Children.Clear();
            double w = BadgeWrap.ActualWidth, h = BadgeWrap.ActualHeight;
            if (w < 4 || h < 4) return; // not measured yet; BadgeWrap_SizeChanged will retry

            const double r = 9;
            var geo = PathGeometry.CreateFromGeometry(new RectangleGeometry(new Rect(0, 0, w, h), r, r));
            geo.Freeze();
            var perim = 2 * (w + h) - 8 * r + 2 * Math.PI * r;
            var dur = new Duration(TimeSpan.FromSeconds(2.4));

            // soft glowing body, then a thinner hard-bright core on top, sharing the same flow
            AddStreakLayer(geo, perim, 3.4, CometTrailBrush, 0.42, 0.9,
                new DropShadowEffect { Color = CometColor, ShadowDepth = 0, BlurRadius = 11, Opacity = 0.9, RenderingBias = RenderingBias.Performance }, dur);
            AddStreakLayer(geo, perim, 1.5, CometHeadBrush, 0.36, 1.0, null, dur);
        }

        // segment = fraction of the border the streak covers; offset animated by the full pattern so
        // it loops seamlessly. Speed is perim/duration regardless of thickness (offset is in
        // thickness units), so all layers travel together.
        private void AddStreakLayer(PathGeometry geo, double perim, double thickness, Brush brush,
            double segment, double opacity, DropShadowEffect glow, Duration dur)
        {
            var units = perim / thickness;
            var dash = units * segment;
            var path = new WShapes.Path
            {
                Data = geo,
                Stroke = brush,
                StrokeThickness = thickness,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeDashCap = PenLineCap.Round,
                StrokeDashArray = new DoubleCollection { dash, units - dash },
                Opacity = opacity,
                IsHitTestVisible = false,
                Effect = glow
            };
            BadgeOrbit.Children.Add(path);
            path.BeginAnimation(WShapes.Shape.StrokeDashOffsetProperty,
                new DoubleAnimation(units, 0, dur) { RepeatBehavior = RepeatBehavior.Forever });
        }

        private void BadgeWrap_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_pulseLossless) StartCometOrbit();
        }

        private void StopTierPulse()
        {
            _pulseLossless = false;
            if (BadgeRing.Effect is DropShadowEffect e)
            {
                e.BeginAnimation(DropShadowEffect.BlurRadiusProperty, null);
                e.BeginAnimation(DropShadowEffect.OpacityProperty, null);
            }
            BadgeRing.Effect = null;
            BadgeRing.Background = null;
            BadgeOrbit.Children.Clear();
        }

        private static Color TierGlow(QualityTier tier, bool transcode)
        {
            if (transcode) return Color.FromRgb(0xFF, 0xB7, 0x4D);
            switch (tier)
            {
                case QualityTier.Lossless: return Color.FromRgb(0x1E, 0xD7, 0x60);
                case QualityTier.Kbps320: return Color.FromRgb(0x8B, 0xC3, 0x4A);
                case QualityTier.Kbps256: return Color.FromRgb(0xCD, 0xDC, 0x39);
                case QualityTier.Kbps192: return Color.FromRgb(0xFF, 0xB7, 0x4D);
                case QualityTier.Kbps128: return Color.FromRgb(0xFF, 0x8A, 0x65);
                case QualityTier.Low: return Color.FromRgb(0xEF, 0x53, 0x50);
                default: return Color.FromRgb(0x9E, 0x9E, 0x9E);
            }
        }

        private void PopulateAnalyzeResults(string path, AudioSample sample, QualityResult result)
        {
            AnalyzeFileName.Text = Path.GetFileName(path);

            var meta = $"{(sample.Codec ?? "?").ToUpperInvariant()}  ·  {sample.SampleRate / 1000.0:0.#} kHz";
            if (sample.EffectiveBitrateKbps.HasValue) meta += $"  ·  {sample.EffectiveBitrateKbps} kbps";
            if (sample.Duration > TimeSpan.Zero) meta += $"  ·  {sample.Duration:m\\:ss}";
            AnalyzeMeta.Text = meta;

            var (bg, fg) = TierColors(result.Tier);
            if (result.IsTranscode) { bg = Frozen(0xFF, 0xB7, 0x4D, 0xFF); fg = Brushes.Black; }
            VerdictBadge.Background = bg;
            VerdictTierText.Foreground = fg;
            VerdictTierText.Text = (result.IsTranscode ? "⚠ " : "") + result.TierLabel;
            VerdictText.Text = result.Verdict;
            VerdictDetail.Text = result.Detail;

            FreqResponseCutoffLabel.Text = result.Tier == QualityTier.Unknown
                ? ""
                : $"cut-off ~{result.CutoffHz / 1000.0:0.0} kHz";

            StartTierPulse(result.Tier, TierGlow(result.Tier, result.IsTranscode));
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
        private void SpecOverlay_SizeChanged(object sender, SizeChangedEventArgs e) => RenderSpectrogramAxes();
        private void FreqResponseCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => RenderFrequencyResponse();

        private async void PaletteCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _palette = (PaletteCombo?.SelectedItem as ComboBoxItem)?.Content as string == "Magma" ? SpectrogramPalette.Magma
                : (PaletteCombo?.SelectedItem as ComboBoxItem)?.Content as string == "Viridis" ? SpectrogramPalette.Viridis
                : (PaletteCombo?.SelectedItem as ComboBoxItem)?.Content as string == "Heat" ? SpectrogramPalette.Heat
                : SpectrogramPalette.Inferno;

            UpdateLegend();
            if (_spectrogram == null) return; // initial selection during InitializeComponent

            await Task.Run(() => Spectrogram.Colorize(_spectrogram, _palette)).ConfigureAwait(true);
            RenderSpectrogram();
        }

        // Vertical dB legend gradient for the current palette (top = 0 dB / brightest).
        private void UpdateLegend()
        {
            if (LegendRect == null) return;

            var anchors = Spectrogram.GetAnchors(_palette);
            var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            for (var i = 0; i < anchors.Length; i++)
            {
                var c = anchors[anchors.Length - 1 - i]; // reverse: brightest at the top
                brush.GradientStops.Add(new GradientStop(
                    Color.FromRgb(c[0], c[1], c[2]), (double)i / (anchors.Length - 1)));
            }
            brush.Freeze();
            LegendRect.Fill = brush;
        }

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

        private void RenderSpectrogram()
        {
            var spec = _spectrogram;
            if (spec == null) { SpectrogramImageControl.Source = null; return; }

            var wb = new WriteableBitmap(spec.Width, spec.Height, 96, 96, PixelFormats.Bgra32, null);
            wb.WritePixels(new Int32Rect(0, 0, spec.Width, spec.Height), spec.Bgra, spec.Width * 4, 0);
            wb.Freeze();
            SpectrogramImageControl.Source = wb;
            SpectrogramImageControl.BeginAnimation(OpacityProperty,
                new DoubleAnimation(0.35, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }

        // Frequency axis (left), time axis (bottom) and the dashed cut-off line over the heatmap.
        private void RenderSpectrogramAxes()
        {
            SpecFreqAxis.Children.Clear();
            SpecTimeAxis.Children.Clear();
            SpecOverlay.Children.Clear();
            if (_analyzeSample == null) return;

            double w = SpecOverlay.ActualWidth, h = SpecOverlay.ActualHeight;
            if (h < 2) return;

            var nyquist = _analyzeSample.NyquistHz <= 0 ? 22050 : _analyzeSample.NyquistHz;

            // frequency labels every 5 kHz + Nyquist
            for (var f = 0.0; f <= nyquist; f += 5000)
            {
                AddFreqLabel(f, nyquist, h);
                if (f + 5000 > nyquist) AddFreqLabel(nyquist, nyquist, h); // top tick
            }

            // dashed cut-off line across the heatmap (shown even at Nyquist for full-band lossless)
            var cutoff = _analyzeResult?.CutoffHz ?? 0;
            if (w >= 2 && cutoff > 0 && cutoff <= nyquist)
            {
                var y = Math.Max(0.75, h * (1 - cutoff / nyquist));
                AddCutoffLine(SpecOverlay, 0, y, w, y);
            }

            // time axis
            if (w >= 2)
            {
                var seconds = _analyzeSample.SampleRate > 0
                    ? _analyzeSample.Mono.Length / (double)_analyzeSample.SampleRate
                    : _analyzeSample.Duration.TotalSeconds;
                if (seconds > 0)
                {
                    const int ticks = 6;
                    for (var k = 0; k <= ticks; k++)
                    {
                        var x = w * k / ticks;
                        var label = new TextBlock
                        {
                            Text = TimeSpan.FromSeconds(seconds * k / ticks).ToString(@"m\:ss"),
                            Foreground = AxisTextBrush, FontSize = 10
                        };
                        var left = k == ticks ? x - 26 : (k == 0 ? x : x - 13);
                        Canvas.SetLeft(label, Math.Max(0, left));
                        Canvas.SetTop(label, 0);
                        SpecTimeAxis.Children.Add(label);
                    }
                }
            }
        }

        private void AddFreqLabel(double f, double nyquist, double h)
        {
            var y = h * (1 - f / nyquist);
            var label = new TextBlock
            {
                Text = f >= 1000 ? $"{f / 1000:0.#}k" : "0",
                Foreground = AxisTextBrush, FontSize = 10, Width = 34, TextAlignment = TextAlignment.Right
            };
            Canvas.SetLeft(label, 2);
            Canvas.SetTop(label, Math.Min(h - 12, Math.Max(0, y - 7)));
            SpecFreqAxis.Children.Add(label);
        }

        // Horizontal averaged-spectrum graph: X = frequency, Y = level. The cut-off cliff is
        // the highest frequency that still carries energy (see the card's explanation text).
        private void RenderFrequencyResponse()
        {
            var canvas = FreqResponseCanvas;
            canvas.Children.Clear();
            FreqDbAxis.Children.Clear();
            FreqHzAxis.Children.Clear();
            var result = _analyzeResult;
            if (result?.Spectrum == null || result.Spectrum.Length == 0) return;

            double w = canvas.ActualWidth, h = canvas.ActualHeight;
            if (w < 2 || h < 2) return;

            var nyquist = result.NyquistHz <= 0 ? 22050 : result.NyquistHz;
            const double minDb = -100, maxDb = 0;

            double X(double f) => f / nyquist * w;
            double Y(double db)
            {
                var t = (db - minDb) / (maxDb - minDb);
                if (t < 0) t = 0; else if (t > 1) t = 1;
                return h - t * h;
            }

            // dB gridlines + left-axis labels
            foreach (var dbv in new[] { 0.0, -50.0, -100.0 })
            {
                var y = Y(dbv);
                canvas.Children.Add(new WShapes.Line { X1 = 0, Y1 = y, X2 = w, Y2 = y, Stroke = GridLineBrush, StrokeThickness = 0.5 });
                var label = new TextBlock
                {
                    Text = dbv == 0 ? "0 dB" : dbv.ToString("0"),
                    Foreground = AxisTextBrush, FontSize = 10, Width = 34, TextAlignment = TextAlignment.Right
                };
                Canvas.SetLeft(label, 2);
                Canvas.SetTop(label, Math.Min(h - 12, Math.Max(0, y - 7)));
                FreqDbAxis.Children.Add(label);
            }

            // frequency gridlines + bottom-axis labels
            AddHzLabel(0, nyquist, w);
            for (var f = 5000.0; f < nyquist; f += 5000)
            {
                var gx = X(f);
                canvas.Children.Add(new WShapes.Line { X1 = gx, Y1 = 0, X2 = gx, Y2 = h, Stroke = GridLineBrush, StrokeThickness = 0.5 });
                AddHzLabel(f, nyquist, w);
            }

            // averaged-spectrum curve, filled to the baseline
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                ctx.BeginFigure(new Point(0, h), true, false);
                foreach (var p in result.Spectrum)
                    ctx.LineTo(new Point(X(p.FrequencyHz), Y(p.Db)), true, false);
                ctx.LineTo(new Point(w, h), true, false);
            }
            geo.Freeze();
            canvas.Children.Add(new WShapes.Path { Data = geo, Fill = ProfileFill, Stroke = ProfileStroke, StrokeThickness = 1.2 });

            // detected cut-off (drawn even when it sits at Nyquist, i.e. full-band lossless)
            if (result.CutoffHz > 0 && result.CutoffHz <= nyquist)
            {
                var cx = Math.Min(w - 1, X(result.CutoffHz));
                AddCutoffLine(canvas, cx, 0, cx, h);
            }
        }

        private void AddHzLabel(double f, double nyquist, double w)
        {
            var label = new TextBlock
            {
                Text = f >= 1000 ? $"{f / 1000:0}k" : "0",
                Foreground = AxisTextBrush, FontSize = 10
            };
            var x = f / nyquist * w;
            Canvas.SetLeft(label, f <= 0 ? 0 : x - 8);
            Canvas.SetTop(label, 0);
            FreqHzAxis.Children.Add(label);
        }

        // Cut-off marker drawn with a dark solid halo under the amber dashes, so it stays legible
        // over any heatmap palette (bright orange/yellow tops as well as dark regions).
        private static void AddCutoffLine(Canvas canvas, double x1, double y1, double x2, double y2)
        {
            canvas.Children.Add(new WShapes.Line
            {
                X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = CutoffHaloBrush, StrokeThickness = 7
            });
            canvas.Children.Add(new WShapes.Line
            {
                X1 = x1, Y1 = y1, X2 = x2, Y2 = y2,
                Stroke = CutoffBrush, StrokeThickness = 2.6,
                StrokeDashArray = new DoubleCollection { 4, 2.5 }
            });
        }

        private static SolidColorBrush Frozen(byte r, byte g, byte b, byte a)
        {
            var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
            brush.Freeze();
            return brush;
        }
    }
}
