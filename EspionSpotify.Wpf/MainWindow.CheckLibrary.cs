using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using EspionSpotify.API;
using EspionSpotify.Enums;
using EspionSpotify.Wpf.Analysis;

namespace EspionSpotify.Wpf
{
    // Check Library tab: batch-scan the output folder for lossless-container files that are actually
    // lossy inside (transcodes), reusing the Analyze engine, and optionally write a portable HTML
    // report to the output root so results survive without a rescan.
    public partial class MainWindow
    {
        private enum ClbState { Idle, Scanning, Results }

        private CancellationTokenSource _clbCts;
        private LibraryScanResult _clbResult;
        private ClbState _clbState = ClbState.Idle;
        private string _clbReportPath;
        private bool _clbLastOpWasScan; // gates the "Open report" button (only a scan makes a report)

        // Audio containers the metadata sweep considers (it only touches files that carry an ISRC).
        private static readonly HashSet<string> AudioExtensions =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".flac", ".mp3", ".wav", ".wave", ".m4a", ".opus", ".ogg", ".aiff", ".aif", ".ape", ".wv"
            };

        // Release version for display and reports. Reads FileVersion (which tracks releases), not
        // AssemblyVersion, which is pinned at 2.0.0.0 to keep the user-settings path stable.
        public string AppVersion
        {
            get
            {
                try
                {
                    var fv = System.Diagnostics.FileVersionInfo.GetVersionInfo(
                        System.Reflection.Assembly.GetExecutingAssembly().Location);
                    return new Version(fv.FileMajorPart, fv.FileMinorPart, fv.FileBuildPart).ToString();
                }
                catch { return ""; }
            }
        }

        public string AppVersionDisplay => "Spytify+ v" + AppVersion;

        // Shown whenever the tab is opened: refresh the folder label; keep any existing scan/results.
        private void OnCheckLibraryShown()
        {
            ClbFolderText.Text = string.IsNullOrWhiteSpace(OutputPath)
                ? Loc.Instance["clbNoFolder"]
                : OutputPath;
            ClbThreadHint.Text = string.Format(Loc.Instance["clbThreadHint"],
                LibraryScanner.MaxParallelism, Environment.ProcessorCount);
            if (_clbState == ClbState.Idle) SetClbState(ClbState.Idle);
        }

        private void SetClbState(ClbState state)
        {
            _clbState = state;
            ClbIdle.Visibility = state == ClbState.Idle ? Visibility.Visible : Visibility.Collapsed;
            ClbScanning.Visibility = state == ClbState.Scanning ? Visibility.Visible : Visibility.Collapsed;
            ClbResults.Visibility = state == ClbState.Results ? Visibility.Visible : Visibility.Collapsed;

            ClbScanButton.Visibility = state == ClbState.Idle ? Visibility.Visible : Visibility.Collapsed;
            ClbUpdateButton.Visibility = state == ClbState.Idle || state == ClbState.Results
                ? Visibility.Visible : Visibility.Collapsed;
            ClbCancelButton.Visibility = state == ClbState.Scanning ? Visibility.Visible : Visibility.Collapsed;
            ClbRescanButton.Visibility = state == ClbState.Results ? Visibility.Visible : Visibility.Collapsed;
            ClbSaveReportButton.Visibility = state == ClbState.Results && _clbLastOpWasScan
                ? Visibility.Visible : Visibility.Collapsed;
        }

        private async void CheckLibrary_Scan_Click(object sender, RoutedEventArgs e)
        {
            var root = OutputPath;
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                MessageBox.Show(this, Loc.Instance["clbSetFolderFirst"],
                    "Spytify+", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _clbCts?.Cancel();
            _clbCts = new CancellationTokenSource();
            _clbResult = null;
            _clbLastOpWasScan = true;
            SetClbState(ClbState.Scanning);
            ClbProgress.Value = 0;
            ClbProgressText.Text = Loc.Instance["clbEnumerating"];
            ClbProgressFile.Text = "";

            var progress = new Progress<LibraryScanProgress>(p =>
            {
                ClbProgress.Maximum = Math.Max(1, p.Total);
                ClbProgress.Value = p.Done;
                ClbProgressText.Text = p.Total == 0
                    ? Loc.Instance["clbNoLosslessFiles"]
                    : string.Format(Loc.Instance["clbProgress"], p.Done, p.Total, p.Flagged);
                ClbProgressFile.Text = p.CurrentFile ?? "";
            });

            try
            {
                _clbResult = await LibraryScanner.ScanAsync(root, progress, _clbCts.Token);
            }
            catch (OperationCanceledException)
            {
                SetClbState(ClbState.Idle);
                return;
            }
            catch (Exception ex)
            {
                SetClbState(ClbState.Idle);
                MessageBox.Show(this, string.Format(Loc.Instance["clbScanFailed"], ex.Message),
                    "Spytify+", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            PopulateFindings(_clbResult);
            AutoSaveReport(_clbResult);
            SetClbState(ClbState.Results);
        }

        private void CheckLibrary_Cancel_Click(object sender, RoutedEventArgs e) => _clbCts?.Cancel();

        private static List<string> EnumerateAudioFiles(string root) =>
            Directory.Exists(root)
                ? Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                    .Where(p => AudioExtensions.Contains(Path.GetExtension(p) ?? ""))
                    .OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList()
                : new List<string>();

        // Direct metadata refresh: walk the library and, for every file that carries an ISRC, re-fetch
        // its tags + cover art from Spotify/iTunes and rewrite them. No playback, no recorder. Exact
        // (ISRC) match only, so files without one are simply skipped. Runs sequentially: it's API-bound
        // and SpotifyAPI's caches aren't built for concurrent access.
        private async void CheckLibrary_UpdateMetadata_Click(object sender, RoutedEventArgs e)
        {
            var root = OutputPath;
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                MessageBox.Show(this, Loc.Instance["clbSetFolderFirst"],
                    "Spytify+", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (!(ExternalAPI.Instance is ISpotifyAPI spotify) || !ExternalAPI.Instance.IsAuthenticated)
            {
                MessageBox.Show(this, Loc.Instance["clbNeedsSpotify"],
                    "Spytify+", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _clbCts?.Cancel();
            _clbCts = new CancellationTokenSource();
            var ct = _clbCts.Token;
            _clbResult = null;
            _clbLastOpWasScan = false;
            SetClbState(ClbState.Scanning);
            ClbProgress.Value = 0;
            ClbProgressText.Text = Loc.Instance["clbEnumerating"];
            ClbProgressFile.Text = "";

            var files = await Task.Run(() => EnumerateAudioFiles(root)).ConfigureAwait(true);
            ClbProgress.Maximum = Math.Max(1, files.Count);
            int updated = 0, noIsrc = 0, noMatch = 0, unreadable = 0, done = 0;

            // API-bound, so scan several at once but keep the cap modest to stay under Spotify's rate
            // limits (the album/artist caches are now concurrent-safe, so this is safe to parallelize).
            var maxDop = Math.Max(2, Math.Min(5, Environment.ProcessorCount - 2));
            IProgress<(int done, string file)> progress = new Progress<(int done, string file)>(p =>
            {
                ClbProgress.Value = p.done;
                ClbProgressText.Text = string.Format(Loc.Instance["clbUpdateProgress"], p.done, files.Count, updated);
                ClbProgressFile.Text = p.file ?? "";
            });

            try
            {
                using (var gate = new SemaphoreSlim(maxDop))
                {
                    async Task UpdateOne(string path)
                    {
                        await gate.WaitAsync(ct).ConfigureAwait(false);
                        try
                        {
                            ct.ThrowIfCancellationRequested();
                            MetadataUpdateOutcome outcome;
                            try
                            {
                                outcome = await LibraryMetadataUpdater
                                    .UpdateFileFromSpotifyAsync(path, spotify, _userSettings).ConfigureAwait(false);
                            }
                            catch (OperationCanceledException) { throw; }
                            catch { outcome = MetadataUpdateOutcome.Unreadable; }

                            switch (outcome)
                            {
                                case MetadataUpdateOutcome.Updated: Interlocked.Increment(ref updated); break;
                                case MetadataUpdateOutcome.NoIsrc: Interlocked.Increment(ref noIsrc); break;
                                case MetadataUpdateOutcome.NoMatch: Interlocked.Increment(ref noMatch); break;
                                default: Interlocked.Increment(ref unreadable); break;
                            }
                        }
                        finally
                        {
                            gate.Release();
                        }

                        var d = Interlocked.Increment(ref done);
                        progress.Report((d, Path.GetFileName(path)));
                    }

                    await Task.WhenAll(files.Select(UpdateOne)).ConfigureAwait(true);
                }
            }
            catch (OperationCanceledException)
            {
                SetClbState(ClbState.Idle);
                return;
            }

            ClbFindings.Children.Clear();
            ClbSummary.Text = string.Format(Loc.Instance["clbUpdateSummary"], updated, noIsrc, noMatch);
            ClbReportText.Visibility = Visibility.Collapsed;
            SetClbState(ClbState.Results);
        }

        // Every completed scan writes its own timestamped report to the output root, so results are
        // preserved automatically (no overwrite between scans) and findable without a rescan. The
        // English report is always written; when French is the selected language a French report is
        // written alongside it (…_fr.html), and "Open report" opens the user's-language one.
        private void AutoSaveReport(LibraryScanResult r)
        {
            _clbReportPath = null;
            if (r == null || string.IsNullOrWhiteSpace(r.Root))
            {
                ClbReportText.Visibility = Visibility.Collapsed;
                return;
            }

            try
            {
                var now = DateTime.Now;
                var stamp = now.ToString("yyyyMMddHHmmss");
                var generatedAt = now.ToString("yyyy-MM-dd HH:mm:ss");

                var enPath = WriteReport(r, generatedAt, stamp, LibraryReportStrings.English);
                _clbReportPath = enPath;
                var names = Path.GetFileName(enPath);

                if (SelectedLanguage == LanguageType.fr)
                {
                    var frPath = WriteReport(r, generatedAt, stamp, LibraryReportStrings.French);
                    _clbReportPath = frPath; // open the report in the user's language
                    names += ", " + Path.GetFileName(frPath);
                }

                ClbReportText.Text = string.Format(Loc.Instance["clbReportSaved"], names);
            }
            catch (Exception ex)
            {
                ClbReportText.Text = string.Format(Loc.Instance["clbReportSaveError"], ex.Message);
            }
            ClbReportText.Visibility = Visibility.Visible;
        }

        private string WriteReport(LibraryScanResult r, string generatedAt, string stamp, LibraryReportStrings s)
        {
            var html = LibraryScanner.BuildHtmlReport(r, generatedAt, AppVersion, s);
            var name = "Spytify_library_check_" + stamp + s.FileSuffix + ".html";
            var path = Path.Combine(r.Root, name);
            File.WriteAllText(path, html, new UTF8Encoding(false));
            return path;
        }

        private void PopulateFindings(LibraryScanResult r)
        {
            ClbFindings.Children.Clear();

            var flagged = r.Findings.Count;
            var skipped = r.Skipped > 0 ? string.Format(Loc.Instance["clbSkippedSuffix"], r.Skipped) : "";
            ClbSummary.Text = (flagged == 0
                ? string.Format(Loc.Instance["clbSummaryClean"], r.Scanned)
                : string.Format(Loc.Instance["clbSummaryFlagged"], r.Scanned, flagged)) + skipped;

            if (flagged == 0)
            {
                ClbFindings.Children.Add(new TextBlock
                {
                    Text = Loc.Instance["clbAllFullBand"],
                    Foreground = Frozen(0x1E, 0xD7, 0x60, 0xFF),
                    FontSize = 14,
                    Margin = new Thickness(0, 6, 0, 0)
                });
                return;
            }

            foreach (var f in r.Findings)
                ClbFindings.Children.Add(BuildFindingRow(f));
        }

        // A clickable row: filename + folder on the left, amber tier badge + cut-off on the right.
        // Clicking opens the file in the Analyze tab for the full spectrogram/verdict.
        private Border BuildFindingRow(LibraryScanFinding f)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // chevron

            var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            left.Children.Add(new TextBlock
            {
                Text = f.FileName,
                Foreground = Brushes.White,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            left.Children.Add(new TextBlock
            {
                Text = f.RelativeFolder,
                Foreground = Frozen(0x99, 0x99, 0x99, 0xFF),
                FontSize = 12,
                Margin = new Thickness(0, 1, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            Grid.SetColumn(left, 0);
            grid.Children.Add(left);

            var badge = new Border
            {
                Background = Frozen(0xFF, 0xB7, 0x4D, 0xFF),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(8, 2, 8, 2),
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = "\u26A0 " + f.TierLabel,
                    Foreground = Brushes.Black,
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold
                }
            };
            Grid.SetColumn(badge, 1);
            grid.Children.Add(badge);

            var cutoff = new TextBlock
            {
                Text = f.CutoffKHz.ToString("0.0") + " kHz",
                Foreground = Frozen(0xCC, 0xCC, 0xCC, 0xFF),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(14, 0, 2, 0),
                Width = 62,
                TextAlignment = TextAlignment.Right
            };
            Grid.SetColumn(cutoff, 2);
            grid.Children.Add(cutoff);

            // Right-anchored chevron to signal the row is clickable (opens the file in Analyze).
            var chevron = new TextBlock
            {
                Text = "\uE76C", // Segoe MDL2 ChevronRight
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 11,
                Foreground = Frozen(0x88, 0x88, 0x88, 0xFF),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 2, 0)
            };
            Grid.SetColumn(chevron, 3);
            grid.Children.Add(chevron);

            var row = new Border
            {
                Background = Frozen(0xFF, 0xFF, 0xFF, 0x12), // faint translucent white card (r,g,b,a)
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14, 10, 12, 10),
                Margin = new Thickness(0, 0, 0, 8),
                Cursor = Cursors.Hand,
                Child = grid,
                ToolTip = f.Path
            };
            row.MouseLeftButtonUp += (s, e) => OpenInAnalyze(f.Path);
            return row;
        }

        // Switch to the Analyze tab (via the nav, so the selection updates) and load the file.
        private void OpenInAnalyze(string path)
        {
            foreach (var item in Nav.MenuItems)
                if (item is ModernWpf.Controls.NavigationViewItem nvi && (nvi.Tag as string) == "analyze")
                {
                    Nav.SelectedItem = nvi;
                    break;
                }
            _ = AnalyzeFileAsync(path);
        }

        private void CheckLibrary_OpenReport_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_clbReportPath) || !File.Exists(_clbReportPath)) return;
            try
            {
                Process.Start(new ProcessStartInfo(_clbReportPath) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, string.Format(Loc.Instance["clbReportOpenError"], ex.Message),
                    "Spytify+", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
