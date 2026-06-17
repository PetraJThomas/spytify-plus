using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace EspionSpotify.Wpf.Analysis
{
    /// <summary>
    /// Decodes any audio file to mono float PCM via the bundled ffmpeg (no ffprobe needed).
    /// Decoding happens at the file's native sample rate so the high-frequency roll-off that
    /// reveals lossy encoding is preserved.
    /// </summary>
    internal static class FfmpegDecoder
    {
        // Cap decode length so a long DJ mix can't exhaust memory (~155 MB worst case at 44.1k mono f32).
        private const int MaxSeconds = 900;

        private static string FfmpegPath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Binaries", "ffmpeg.exe");

        public static async Task<AudioSample> DecodeAsync(string path, CancellationToken ct = default)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("File not found.", path);
            if (!File.Exists(FfmpegPath)) throw new FileNotFoundException("Bundled ffmpeg.exe is missing.", FfmpegPath);

            var info = await ProbeAsync(path, ct).ConfigureAwait(false);

            // ffmpeg only prints a per-stream bitrate for some codecs (e.g. MP3); for the rest
            // (notably FLAC) measure the real audio-stream size so the figure isn't the cover-art-
            // inflated container number.
            var audioBitrate = info.AudioBitrateKbps;
            if (audioBitrate == null && info.Duration.TotalSeconds >= 0.5)
                audioBitrate = await MeasureAudioBitrateAsync(path, info.Duration, ct).ConfigureAwait(false);

            var pcm = await DecodePcmAsync(path, ct).ConfigureAwait(false);

            var mono = new float[pcm.Length / 4];
            Buffer.BlockCopy(pcm, 0, mono, 0, mono.Length * 4);

            return new AudioSample
            {
                Mono = mono,
                SampleRate = info.SampleRate > 0 ? info.SampleRate : 44100,
                Codec = info.Codec,
                ContainerBitrateKbps = info.BitrateKbps,
                AudioBitrateKbps = audioBitrate,
                Duration = info.Duration
            };
        }

        // Exact audio bitrate from the real audio-stream byte size: stream-copy the audio to the null
        // muxer and read ffmpeg's "audio:NkB" summary. Excludes embedded cover art, so unlike the
        // container figure it isn't inflated, and it works for FLAC where no bitrate is stored.
        private static async Task<int?> MeasureAudioBitrateAsync(string path, TimeSpan duration, CancellationToken ct)
        {
            var psi = new ProcessStartInfo
            {
                FileName = FfmpegPath,
                Arguments = $"-hide_banner -i \"{path}\" -map 0:a:0 -c copy -f null -",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };

            using (var p = new Process { StartInfo = psi })
            {
                try { p.Start(); }
                catch { return null; }

                var stderrTask = p.StandardError.ReadToEndAsync();
                await p.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
                var stderr = await stderrTask.ConfigureAwait(false);
                await Task.Run(() => p.WaitForExit(), ct).ConfigureAwait(false);

                // ffmpeg reports the stream size as "audio:NkB" (older) or "audio:NKiB" (newer);
                // both count 1024-byte units.
                var m = Regex.Match(stderr ?? string.Empty, @"audio:\s*(\d+)\s*[kK]i?B");
                if (!m.Success || !long.TryParse(m.Groups[1].Value, out var kib)) return null;
                return (int)Math.Round(kib * 1024.0 * 8.0 / duration.TotalSeconds / 1000.0);
            }
        }

        private struct ProbeInfo
        {
            public int SampleRate;
            public string Codec;
            public int? BitrateKbps;       // container overall (audio + cover art + tags)
            public int? AudioBitrateKbps;  // the audio stream itself, when ffmpeg reports it
            public TimeSpan Duration;
        }

        // `ffmpeg -i file` with no output exits non-zero but prints the stream info to stderr.
        private static async Task<ProbeInfo> ProbeAsync(string path, CancellationToken ct)
        {
            var psi = new ProcessStartInfo
            {
                FileName = FfmpegPath,
                Arguments = $"-hide_banner -i \"{path}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };

            using (var p = new Process { StartInfo = psi })
            {
                p.Start();
                var stderrTask = p.StandardError.ReadToEndAsync();
                await p.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
                var stderr = await stderrTask.ConfigureAwait(false);
                await Task.Run(() => p.WaitForExit(), ct).ConfigureAwait(false);
                return ParseProbe(stderr);
            }
        }

        private static ProbeInfo ParseProbe(string stderr)
        {
            var info = new ProbeInfo { Codec = "unknown" };
            if (string.IsNullOrEmpty(stderr)) return info;

            var sr = Regex.Match(stderr, @"(\d+) Hz");
            if (sr.Success) int.TryParse(sr.Groups[1].Value, out info.SampleRate);

            var codec = Regex.Match(stderr, @"Audio:\s*([A-Za-z0-9_]+)");
            if (codec.Success) info.Codec = codec.Groups[1].Value;

            var br = Regex.Match(stderr, @"bitrate:\s*(\d+) kb/s");
            if (br.Success && int.TryParse(br.Groups[1].Value, out var b)) info.BitrateKbps = b;

            // Per-stream bitrate on the "Audio:" line (e.g. "Audio: mp3, 48000 Hz, stereo, 320 kb/s").
            // This is the real audio bitrate, free of cover-art/tag inflation; lossless usually omits it.
            var abr = Regex.Match(stderr, @"Audio:[^\r\n]*?(\d+) kb/s");
            if (abr.Success && int.TryParse(abr.Groups[1].Value, out var ab)) info.AudioBitrateKbps = ab;

            var dur = Regex.Match(stderr, @"Duration:\s*(\d+):(\d+):([\d.]+)");
            if (dur.Success)
            {
                var h = int.Parse(dur.Groups[1].Value, CultureInfo.InvariantCulture);
                var m = int.Parse(dur.Groups[2].Value, CultureInfo.InvariantCulture);
                var s = double.Parse(dur.Groups[3].Value, CultureInfo.InvariantCulture);
                info.Duration = TimeSpan.FromSeconds(h * 3600 + m * 60 + s);
            }

            return info;
        }

        private static async Task<byte[]> DecodePcmAsync(string path, CancellationToken ct)
        {
            var psi = new ProcessStartInfo
            {
                FileName = FfmpegPath,
                Arguments = $"-v error -i \"{path}\" -t {MaxSeconds} -ac 1 -map 0:a:0? -f f32le -",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };

            using (var p = new Process { StartInfo = psi })
            {
                p.Start();
                using (var outMs = new MemoryStream())
                {
                    // Read raw bytes from the BaseStream (not the text StreamReader) and drain
                    // stderr concurrently so the pipe never deadlocks.
                    var copy = p.StandardOutput.BaseStream.CopyToAsync(outMs, 1 << 16, ct);
                    var errTask = p.StandardError.ReadToEndAsync();
                    await copy.ConfigureAwait(false);
                    var stderr = await errTask.ConfigureAwait(false);
                    await Task.Run(() => p.WaitForExit(), ct).ConfigureAwait(false);

                    if (outMs.Length == 0)
                        throw new InvalidOperationException(
                            $"ffmpeg produced no audio for this file.{(string.IsNullOrWhiteSpace(stderr) ? "" : " " + stderr.Trim())}");

                    return outMs.ToArray();
                }
            }
        }
    }
}
