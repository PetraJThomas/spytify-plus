using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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

        // Shown whenever the tab is opened: refresh the folder label; keep any existing scan/results.
        private void OnCheckLibraryShown()
        {
            ClbFolderText.Text = string.IsNullOrWhiteSpace(OutputPath)
                ? "No output folder set (choose one on the Recorder tab)."
                : OutputPath;
            if (_clbState == ClbState.Idle) SetClbState(ClbState.Idle);
        }

        private void SetClbState(ClbState state)
        {
            _clbState = state;
            ClbIdle.Visibility = state == ClbState.Idle ? Visibility.Visible : Visibility.Collapsed;
            ClbScanning.Visibility = state == ClbState.Scanning ? Visibility.Visible : Visibility.Collapsed;
            ClbResults.Visibility = state == ClbState.Results ? Visibility.Visible : Visibility.Collapsed;

            ClbScanButton.Visibility = state == ClbState.Idle ? Visibility.Visible : Visibility.Collapsed;
            ClbCancelButton.Visibility = state == ClbState.Scanning ? Visibility.Visible : Visibility.Collapsed;
            ClbRescanButton.Visibility = state == ClbState.Results ? Visibility.Visible : Visibility.Collapsed;
            ClbSaveReportButton.Visibility = state == ClbState.Results ? Visibility.Visible : Visibility.Collapsed;
        }

        private async void CheckLibrary_Scan_Click(object sender, RoutedEventArgs e)
        {
            var root = OutputPath;
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                MessageBox.Show(this, "Set a valid output folder on the Recorder tab first.",
                    "Spytify+", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _clbCts?.Cancel();
            _clbCts = new CancellationTokenSource();
            _clbResult = null;
            SetClbState(ClbState.Scanning);
            ClbProgress.Value = 0;
            ClbProgressText.Text = "Enumerating files…";
            ClbProgressFile.Text = "";

            var progress = new Progress<LibraryScanProgress>(p =>
            {
                ClbProgress.Maximum = Math.Max(1, p.Total);
                ClbProgress.Value = p.Done;
                ClbProgressText.Text = p.Total == 0
                    ? "No lossless files found."
                    : $"{p.Done} of {p.Total} scanned  ·  {p.Flagged} flagged";
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
                MessageBox.Show(this, "Scan failed: " + ex.Message,
                    "Spytify+", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            PopulateFindings(_clbResult);
            SetClbState(ClbState.Results);
        }

        private void CheckLibrary_Cancel_Click(object sender, RoutedEventArgs e) => _clbCts?.Cancel();

        private void PopulateFindings(LibraryScanResult r)
        {
            ClbFindings.Children.Clear();

            var flagged = r.Findings.Count;
            var skipped = r.Skipped > 0 ? $"  ({r.Skipped} skipped)" : "";
            ClbSummary.Text = flagged == 0
                ? $"All {r.Scanned} lossless files passed. No transcodes found.{skipped}"
                : $"Scanned {r.Scanned} lossless files  ·  {flagged} not truly lossless.{skipped}";

            if (flagged == 0)
            {
                ClbFindings.Children.Add(new TextBlock
                {
                    Text = "✓  Every lossless file reached full band.",
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
                    Text = "⚠ " + f.TierLabel,
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

        private void CheckLibrary_SaveReport_Click(object sender, RoutedEventArgs e)
        {
            if (_clbResult == null) return;
            try
            {
                var html = LibraryScanner.BuildHtmlReport(_clbResult, DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
                var path = Path.Combine(_clbResult.Root, "Spytify library check.html");
                File.WriteAllText(path, html, new UTF8Encoding(false));
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Couldn't save the report: " + ex.Message,
                    "Spytify+", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
