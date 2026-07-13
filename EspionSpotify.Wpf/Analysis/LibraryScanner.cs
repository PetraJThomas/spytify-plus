using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EspionSpotify.Wpf.Analysis
{
    /// <summary>
    /// Batch-scans a library folder for lossless-container files that are actually lossy inside
    /// (a lossy source re-wrapped as FLAC/WAV/etc.). Reuses the exact same decode + quality analysis
    /// as the single-file Analyze tab, so verdicts are identical. Only lossless containers are
    /// considered: an MP3 being lossy is expected, not a finding.
    /// </summary>
    internal static class LibraryScanner
    {
        // Containers that are meant to be lossless. A lossy file wearing one of these extensions is
        // the whole point of the scan. (.m4a is deliberately excluded: it is ambiguous ALAC-or-AAC.)
        private static readonly HashSet<string> LosslessExtensions =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".flac", ".wav", ".wave", ".aiff", ".aif", ".aifc",
                ".ape", ".wv", ".tak", ".tta", ".alac"
            };

        public static bool IsLosslessContainer(string path) =>
            !string.IsNullOrEmpty(path) && LosslessExtensions.Contains(Path.GetExtension(path) ?? "");

        public static IReadOnlyList<string> EnumerateLosslessFiles(string root)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                return Array.Empty<string>();
            return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Where(IsLosslessContainer)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// Decodes and analyzes every lossless-container file under <paramref name="root"/>, reporting
        /// progress. A file is a finding when the analyzer flags it as a transcode (a lossless codec
        /// whose spectrum shows a lossy cut-off). Files that fail to decode are counted as skipped.
        /// </summary>
        public static async Task<LibraryScanResult> ScanAsync(
            string root, IProgress<LibraryScanProgress> progress, CancellationToken ct)
        {
            var files = EnumerateLosslessFiles(root);
            var result = new LibraryScanResult { Root = root, Total = files.Count };
            if (files.Count == 0)
            {
                progress?.Report(new LibraryScanProgress { Done = 0, Total = 0, Flagged = 0 });
                return result;
            }

            // Files are independent (each is its own ffmpeg decode plus a pure FFT pass), so scan
            // several at once. Leave one core for the UI, and cap at 6 so we never spawn a swarm of
            // ffmpeg processes on high-core machines. A SemaphoreSlim bounds the concurrency
            // (Parallel.ForEachAsync isn't available on .NET Framework), and Task.WhenAll joins.
            var maxDop = Math.Max(1, Math.Min(6, Environment.ProcessorCount - 1));
            var findings = new ConcurrentBag<LibraryScanFinding>();
            var done = 0;
            var scanned = 0;
            var skipped = 0;

            using (var gate = new SemaphoreSlim(maxDop))
            {
                async Task ScanOne(string path)
                {
                    await gate.WaitAsync(ct).ConfigureAwait(false);
                    try
                    {
                        ct.ThrowIfCancellationRequested();
                        var sample = await FfmpegDecoder.DecodeAsync(path, ct, fast: true).ConfigureAwait(false);
                        var q = QualityAnalyzer.Analyze(sample);
                        Interlocked.Increment(ref scanned);

                        // IsTranscode is only ever set for a lossless codec with a lossy cut-off, so it
                        // is exactly the "lossless container, lossy content" signal we want here.
                        if (q.IsTranscode)
                        {
                            findings.Add(new LibraryScanFinding
                            {
                                Path = path,
                                FileName = Path.GetFileName(path),
                                RelativeFolder = RelativeFolder(root, path),
                                Codec = (sample.Codec ?? "?").ToUpperInvariant(),
                                TierLabel = q.TierLabel,
                                CutoffKHz = q.CutoffHz / 1000.0,
                                Detail = q.Detail
                            });
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw; // cancellation propagates to the caller; partial results are discarded
                    }
                    catch
                    {
                        Interlocked.Increment(ref skipped); // unreadable/undecodable; leave it out
                    }
                    finally
                    {
                        gate.Release();
                    }

                    var d = Interlocked.Increment(ref done);
                    progress?.Report(new LibraryScanProgress
                    {
                        Done = d,
                        Total = files.Count,
                        CurrentFile = Path.GetFileName(path),
                        Flagged = findings.Count
                    });
                }

                await Task.WhenAll(files.Select(ScanOne)).ConfigureAwait(false);
            }

            result.Scanned = scanned;
            result.Skipped = skipped;
            foreach (var f in findings.OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase))
                result.Findings.Add(f);
            return result;
        }

        private static string RelativeFolder(string root, string filePath)
        {
            var dir = Path.GetDirectoryName(filePath) ?? "";
            if (!string.IsNullOrEmpty(root) &&
                dir.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                var rel = dir.Substring(root.Length).TrimStart('\\', '/');
                return rel.Length == 0 ? "." : rel;
            }
            return dir;
        }

        // Self-contained dark-theme HTML report written to the output root, so results survive without
        // a rescan. No external assets (works offline, portable next to the library).
        public static string BuildHtmlReport(LibraryScanResult result, string generatedAtText)
        {
            var sb = new StringBuilder();
            sb.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\">")
              .Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">")
              .Append("<title>Spytify+ Library Check</title><style>")
              .Append("*{box-sizing:border-box}body{margin:0;background:#0f0f10;color:#e9e9ea;")
              .Append("font:14px/1.5 -apple-system,Segoe UI,Roboto,Helvetica,Arial,sans-serif;padding:32px}")
              .Append(".wrap{max-width:1000px;margin:0 auto}h1{font-size:22px;margin:0 0 4px}")
              .Append(".sub{color:#9a9a9c;margin:0 0 24px}.summary{display:flex;gap:16px;flex-wrap:wrap;margin:0 0 24px}")
              .Append(".card{background:#1a1a1c;border:1px solid #2a2a2d;border-radius:10px;padding:16px 20px;min-width:150px}")
              .Append(".card .n{font-size:26px;font-weight:600}.card .l{color:#9a9a9c;font-size:12px}")
              .Append(".ok{color:#1ED760}.warn{color:#FFB74D}")
              .Append("table{width:100%;border-collapse:collapse;background:#1a1a1c;border-radius:10px;overflow:hidden}")
              .Append("th,td{text-align:left;padding:10px 14px;border-bottom:1px solid #2a2a2d;font-size:13px}")
              .Append("th{color:#9a9a9c;font-weight:500;background:#161618}tr:last-child td{border-bottom:none}")
              .Append(".badge{display:inline-block;padding:2px 8px;border-radius:6px;background:#FFB74D;color:#000;")
              .Append("font-weight:600;font-size:12px;white-space:nowrap}.path{color:#9a9a9c;font-size:12px}")
              .Append(".clean{background:#1a1a1c;border:1px solid #2a2a2d;border-radius:10px;padding:28px;text-align:center}")
              .Append("</style></head><body><div class=\"wrap\">");

            sb.Append("<h1>Spytify+ &middot; Library Check</h1>")
              .Append("<p class=\"sub\">").Append(Esc(result.Root)).Append("<br>Generated ")
              .Append(Esc(generatedAtText)).Append("</p>");

            var flagged = result.Findings.Count;
            sb.Append("<div class=\"summary\">")
              .Append(Stat(result.Scanned.ToString(), "lossless files scanned", ""))
              .Append(Stat(flagged.ToString(), "not truly lossless", flagged > 0 ? "warn" : "ok"));
            if (result.Skipped > 0)
                sb.Append(Stat(result.Skipped.ToString(), "skipped (unreadable)", ""));
            sb.Append("</div>");

            if (flagged == 0)
            {
                sb.Append("<div class=\"clean\"><span class=\"ok\" style=\"font-size:18px\">&#10003; All clear</span>")
                  .Append("<p class=\"sub\" style=\"margin:8px 0 0\">Every lossless file reached full band. No transcodes found.</p></div>");
            }
            else
            {
                sb.Append("<table><thead><tr><th>File</th><th>Folder</th><th>Verdict</th><th>Cut-off</th></tr></thead><tbody>");
                foreach (var f in result.Findings)
                {
                    sb.Append("<tr><td>").Append(Esc(f.FileName)).Append("</td>")
                      .Append("<td class=\"path\">").Append(Esc(f.RelativeFolder)).Append("</td>")
                      .Append("<td><span class=\"badge\">").Append(Esc(f.TierLabel)).Append("</span></td>")
                      .Append("<td>").Append(f.CutoffKHz.ToString("0.0")).Append(" kHz</td></tr>");
                }
                sb.Append("</tbody></table>");
            }

            sb.Append("</div></body></html>");
            return sb.ToString();
        }

        private static string Stat(string n, string label, string cls) =>
            $"<div class=\"card\"><div class=\"n {cls}\">{Esc(n)}</div><div class=\"l\">{Esc(label)}</div></div>";

        private static string Esc(string s) => (s ?? "")
            .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
    }

    internal sealed class LibraryScanProgress
    {
        public int Done { get; set; }
        public int Total { get; set; }
        public string CurrentFile { get; set; }
        public int Flagged { get; set; }
    }

    internal sealed class LibraryScanFinding
    {
        public string Path { get; set; }
        public string FileName { get; set; }
        public string RelativeFolder { get; set; }
        public string Codec { get; set; }
        public string TierLabel { get; set; }
        public double CutoffKHz { get; set; }
        public string Detail { get; set; }
    }

    internal sealed class LibraryScanResult
    {
        public string Root { get; set; }
        public int Total { get; set; }
        public int Scanned { get; set; }
        public int Skipped { get; set; }
        public bool Canceled { get; set; }
        public List<LibraryScanFinding> Findings { get; } = new List<LibraryScanFinding>();
    }
}
