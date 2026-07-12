using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EspionSpotify.API;
using EspionSpotify.Enums;
using EspionSpotify.Exceptions;
using EspionSpotify.Extensions;
using EspionSpotify.Models;
using EspionSpotify.Native;
using EspionSpotify.Translations;
using NAudio.Wave;

namespace EspionSpotify
{
    /// <summary>
    /// Single-consumer background encoder. Decouples the heavy WAV→media encode (ffmpeg for
    /// FLAC/OPUS, LAME for MP3) from the recording path so capturing the next track is never
    /// blocked by encoding the previous one. Failures keep the source WAV and surface the real
    /// error instead of silently dropping the song.
    /// </summary>
    public sealed class EncodeService : IEncodeService
    {
        private readonly IFrmEspionSpotify _form;
        private readonly IFileSystem _fileSystem;
        private readonly BlockingCollection<EncodeJob> _queue;
        private readonly Task _worker;
        private bool _completed;
        private bool _disposed;

        public EncodeService(IFrmEspionSpotify form, IFileSystem fileSystem)
        {
            _form = form;
            _fileSystem = fileSystem;
            _queue = new BlockingCollection<EncodeJob>();

            // Dedicated long-running thread: the worker blocks on the queue while idle and on
            // each encode, so we keep it off the shared thread pool entirely.
            _worker = Task.Factory.StartNew(WorkerLoop, CancellationToken.None,
                TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        public void Enqueue(EncodeJob job)
        {
            if (job == null || _queue.IsAddingCompleted) return;
            try
            {
                _queue.Add(job);
            }
            catch (InvalidOperationException)
            {
                // adding was completed in the meantime
            }
        }

        public async Task CompleteAndDrainAsync()
        {
            if (_completed) return;
            _completed = true;

            try { _queue.CompleteAdding(); } catch (ObjectDisposedException) { return; }

            try { await _worker.ConfigureAwait(false); }
            catch { /* worker swallows per-job errors; nothing actionable here */ }
        }

        private void WorkerLoop()
        {
            foreach (var job in _queue.GetConsumingEnumerable())
            {
                try
                {
                    // Block this dedicated thread on each job; the async work inside hops to
                    // the pool (ConfigureAwait(false)) and never marshals back here.
                    ProcessJobAsync(job).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    // Never let one job kill the worker.
                    Console.WriteLine(ex.Message);
                    Program.ReportException(ex);
                }
            }
        }

        #region Pipeline

        private async Task ProcessJobAsync(EncodeJob job)
        {
            var fileManager = new FileManager(job.UserSettings, job.Track, _fileSystem);

            var tempEncodeFile = fileManager.GetTempFile();
            if (job.UserSettings.MediaFormat == MediaFormat.Opus)
                tempEncodeFile = Path.ChangeExtension(tempEncodeFile, ".opus");
            else if (job.UserSettings.MediaFormat == MediaFormat.Flac)
                tempEncodeFile = Path.ChangeExtension(tempEncodeFile, ".flac");
            else if (job.UserSettings.MediaFormat == MediaFormat.Mp3)
                tempEncodeFile = Path.ChangeExtension(tempEncodeFile, ".mp3");

            try
            {
                await EncodeAsync(job, tempEncodeFile).ConfigureAwait(false);

                if (!_fileSystem.File.Exists(tempEncodeFile))
                    throw new Exception($"Encoder produced no output for \"{job.Track}\".");
            }
            catch (Exception ex)
            {
                // Keep the captured WAV so it can be recovered/re-encoded; surface the real error.
                _form.WriteIntoConsole(I18NKeys.LogUnknownException, ex.Message);
                Console.WriteLine(ex.Message);
                Program.ReportException(ex);
                TryDelete(tempEncodeFile);
                return; // intentionally NOT deleting job.TempOriginalFile
            }

            // Encode succeeded, the raw WAV is no longer needed.
            fileManager.DeleteFile(job.TempOriginalFile);

            var outputFile = fileManager.GetOutputFileAndInitDirectories();

            if (job.CountSeconds < job.UserSettings.MinimumRecordedLengthSeconds)
            {
                _form.WriteIntoConsole(I18NKeys.LogDeleting, outputFile.ToString(),
                    job.UserSettings.MinimumRecordedLengthSeconds);
                fileManager.DeleteFile(tempEncodeFile);
                return;
            }

            // Length verification: drop captures clearly cut short versus the known track length
            // (skipped mid-song, crossfade cut, late start) so the library isn't polluted with
            // partial files. Only runs when enabled and the expected length is known.
            if (IsTruncatedCapture(job.UserSettings.VerifyRecordingLength, job.CountSeconds, job.Track.Length))
            {
                _form.WriteIntoConsole(I18NKeys.LogTruncated, outputFile.ToString(),
                    job.CountSeconds, job.Track.Length);
                fileManager.DeleteFile(tempEncodeFile);
                return;
            }

            try
            {
                fileManager.RenameFile(tempEncodeFile, outputFile.ToMediaFilePath());
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                if (ex is SourceFileNotFoundException)
                {
                    _form.WriteIntoConsole(I18NKeys.LogRecordedFileNotFound);
                }
                else if (ex is DestinationPathNotFoundException)
                {
                    _form.WriteIntoConsole(I18NKeys.LogOutputPathNotFound);
                    Watcher.Running = false;
                }
                else
                {
                    _form.WriteIntoConsole(I18NKeys.LogException, ex.Message);
                    Program.ReportException(ex);
                }

                return;
            }

            var length = TimeSpan.FromSeconds(job.CountSeconds).ToString(@"mm\:ss");
            _form.WriteIntoConsole(I18NKeys.LogRecorded, outputFile.ToString(), length);

            await UpdateMediaTagsAsync(outputFile, job).ConfigureAwait(false);

            if (job.UserSettings.SaveCoverFile)
                await SaveCoverFileAsync(outputFile, job.Track).ConfigureAwait(false);

            // Optional post-record quality check (spectral cut-off / transcode detection). Runs in the
            // UI layer where the analyzer lives; fired after tagging so the file is final.
            if (job.UserSettings.AnalyzeRecordings)
                _form.QueueQualityAnalysis(outputFile.ToMediaFilePath());
        }

        // Saves the album art as "cover.jpg" in the track's folder, once per album folder. Only when
        // the track is grouped into a sub-folder (a shared cover at the output root would be
        // meaningless), and skipped if a cover already exists. Best-effort.
        private async Task SaveCoverFileAsync(OutputFile outputFile, Track track)
        {
            try
            {
                if (string.IsNullOrEmpty(outputFile.FoldersPath)) return;

                var dir = _fileSystem.Path.GetDirectoryName(outputFile.ToMediaFilePath());
                if (string.IsNullOrEmpty(dir)) return;

                var coverPath = _fileSystem.Path.Combine(dir, "cover.jpg");
                if (_fileSystem.File.Exists(coverPath)) return;
                if (string.IsNullOrWhiteSpace(track.AlbumArtUrl)) return;

                var bytes = track.AlbumArtImage ?? await MapperID3.GetAlbumCover(track.AlbumArtUrl).ConfigureAwait(false);
                if (bytes == null || bytes.Length == 0) return;

                _fileSystem.File.WriteAllBytes(coverPath, bytes);
            }
            catch { /* best-effort; a missing cover.jpg never fails a recording */ }
        }

        // A capture counts as truncated when it is shorter than this fraction of the track length.
        // 0.80 keeps near-complete captures (lost intro/crossfade) while dropping obvious mid-song cuts.
        public const double RECORDING_LENGTH_MIN_RATIO = 0.80;

        public static bool IsTruncatedCapture(bool enabled, int countSeconds, int? expectedLengthSeconds)
        {
            if (!enabled) return false;
            if (!expectedLengthSeconds.HasValue || expectedLengthSeconds.Value <= 0) return false;
            return countSeconds < expectedLengthSeconds.Value * RECORDING_LENGTH_MIN_RATIO;
        }

        private async Task EncodeAsync(EncodeJob job, string tempEncodeFile)
        {
            switch (job.UserSettings.MediaFormat)
            {
                case MediaFormat.Wav:
                    _fileSystem.File.Copy(job.TempOriginalFile, tempEncodeFile);
                    return;
                case MediaFormat.Flac:
                    await EncodeWithFFmpegAsync(job, tempEncodeFile, isFlac: true).ConfigureAwait(false);
                    return;
                case MediaFormat.Opus:
                    await EncodeWithFFmpegAsync(job, tempEncodeFile, isFlac: false).ConfigureAwait(false);
                    return;
                case MediaFormat.Mp3:
                    await EncodeWaveToMp3Async(job, tempEncodeFile).ConfigureAwait(false);
                    return;
                default:
                    throw new Exception("Unsupported media format.");
            }
        }

        private async Task UpdateMediaTagsAsync(OutputFile outputFile, EncodeJob job)
        {
            // FLAC/OPUS tags are written by ffmpeg during the encode (ffmetadata).
            if (!_fileSystem.File.Exists(outputFile.ToMediaFilePath())) return;

            switch (job.UserSettings.MediaFormat)
            {
                case MediaFormat.Mp3:
                case MediaFormat.Wav:
                    var mapper = new MapperID3(_fileSystem, outputFile.ToMediaFilePath(), job.Track, job.UserSettings);
                    await Task.Run(mapper.SaveMediaTags).ConfigureAwait(false);
                    return;
                default:
                    return;
            }
        }

        #endregion Pipeline

        #region FFmpeg (FLAC / OPUS)

        private async Task EncodeWithFFmpegAsync(EncodeJob job, string tempEncodeFile, bool isFlac)
        {
            var ffmpegPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "binaries", "ffmpeg.exe");
            var ffmetaPath = BuildFfmetadataFile(job.Track, job.UserSettings);

            try
            {
                // Cover art is embedded by ffmpeg (Rekordbox-compatible attached picture).
                byte[] cover = null;
                if (isFlac && !string.IsNullOrWhiteSpace(job.Track.AlbumArtUrl))
                    cover = await MapperID3.GetAlbumCover(job.Track.AlbumArtUrl).ConfigureAwait(false);
                var hasCover = cover != null && cover.Length > 0;

                // NOTE: only fixed flags and our own temp file paths go on the command line.
                // All free-text metadata travels via the ffmetadata file, so titles containing
                // quotes/backslashes can never corrupt the arguments.
                string args;
                if (isFlac && hasCover)
                {
                    args = $"-y -i \"{job.TempOriginalFile}\" -i pipe:0 -i \"{ffmetaPath}\" " +
                           "-map_metadata 2 -map 0:a -map 1:v " +
                           "-c:a flac -compression_level 8 -c:v copy -disposition:v attached_pic " +
                           "-metadata:s:v title=\"Album cover\" -metadata:s:v comment=\"Cover (front)\" " +
                           $"\"{tempEncodeFile}\"";
                }
                else if (isFlac)
                {
                    args = $"-y -i \"{job.TempOriginalFile}\" -i \"{ffmetaPath}\" -map_metadata 1 " +
                           $"-c:a flac -compression_level 8 \"{tempEncodeFile}\"";
                }
                else // OPUS
                {
                    var bitrate = GetOpusBitrate(job.UserSettings.Bitrate);
                    args = $"-y -i \"{job.TempOriginalFile}\" -i \"{ffmetaPath}\" -map_metadata 1 " +
                           $"-c:a libopus -b:a {bitrate} \"{tempEncodeFile}\"";
                }

                await RunFFmpegAsync(ffmpegPath, args, hasCover ? cover : null).ConfigureAwait(false);
            }
            finally
            {
                TryDelete(ffmetaPath);
            }
        }

        private static async Task RunFFmpegAsync(string ffmpegPath, string args, byte[] coverToPipe)
        {
            var psi = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardInput = coverToPipe != null
            };

            using (var process = new Process { StartInfo = psi })
            {
                if (!process.Start())
                    throw new Exception("Failed to start ffmpeg.");

                if (coverToPipe != null)
                {
                    try
                    {
                        using (var stdin = process.StandardInput.BaseStream)
                        {
                            await stdin.WriteAsync(coverToPipe, 0, coverToPipe.Length).ConfigureAwait(false);
                            await stdin.FlushAsync().ConfigureAwait(false);
                        }
                    }
                    catch
                    {
                        // ffmpeg may have already rejected the input; its stderr will explain.
                    }
                }

                // Reading stderr to completion both captures diagnostics and waits for the
                // pipe to close as ffmpeg ends. WaitForExit then yields the exit code.
                var stderr = await process.StandardError.ReadToEndAsync().ConfigureAwait(false);
                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    var detail = (stderr ?? string.Empty).Trim();
                    if (detail.Length > 600) detail = "..." + detail.Substring(detail.Length - 600);
                    throw new Exception($"ffmpeg exited with code {process.ExitCode}. {detail}");
                }
            }
        }

        private string BuildFfmetadataFile(Track track, UserSettings userSettings)
        {
            var path = _fileSystem.Path.GetTempFileName();
            _fileSystem.File.WriteAllText(path, BuildFfmetadataContent(track, userSettings), new UTF8Encoding(false));
            return path;
        }

        /// <summary>
        /// Builds the ffmetadata document fed to ffmpeg when encoding FLAC/OPUS. Mirrors the MP3/WAV
        /// tag logic in <see cref="MapperID3"/> so every container ends up with the same tags,
        /// honouring the "extra title as subtitle" and "counter number as track number" toggles.
        /// </summary>
        public static string BuildFfmetadataContent(Track track, UserSettings userSettings)
        {
            var sb = new StringBuilder();
            sb.Append(";FFMETADATA1\n");

            void Add(string key, string value)
            {
                if (string.IsNullOrEmpty(value)) return;
                sb.Append(key).Append('=').Append(FfmetaEscape(value)).Append('\n');
            }

            // "Extra title as subtitle": keep the bare title and split the "(feat.)"/"- Live" part
            // into a subtitle field; otherwise the title carries the full extended string.
            var movingExtraToSubtitle = userSettings.ExtraTitleToSubtitleEnabled
                                        && track.TitleExtendedSeparatorType != TitleSeparatorType.None;

            Add("title", movingExtraToSubtitle ? track.Title : track.ToTitleString());
            if (movingExtraToSubtitle) Add("subtitle", track.TitleExtended);

            Add("artist", track.Artist);
            Add("album", track.Album);

            if (track.AlbumArtists != null && track.AlbumArtists.Length > 0)
                Add("album_artist", string.Join(", ", track.AlbumArtists));
            else if (!string.IsNullOrEmpty(track.Artists))
                Add("album_artist", track.Artists);

            if (track.Performers != null && track.Performers.Length > 0)
                Add("performer", string.Join(", ", track.Performers));
            if (track.Genres != null && track.Genres.Length > 0)
                Add("genre", string.Join(", ", track.Genres));
            if (track.Year.HasValue) Add("date", track.Year.Value.ToString());

            // "Counter number as track number": use the app counter when enabled, else the album
            // position (same choice MapperID3.GetTrackNumber makes for MP3/WAV).
            var trackNumber = userSettings.OrderNumberInMediaTagEnabled && userSettings.OrderNumberAsTag.HasValue
                ? userSettings.OrderNumberAsTag
                : track.AlbumPosition;
            if (trackNumber.HasValue) Add("track", trackNumber.Value.ToString());

            if (track.Disc.HasValue) Add("disc", track.Disc.Value.ToString());

            // Extended identifiers as Vorbis comments (ISRC is standard; the Spotify IDs are custom).
            if (userSettings.WriteExtendedTags)
            {
                Add("ISRC", track.Isrc);
                Add("SPOTIFY_TRACK_ID", track.SpotifyTrackId);
                Add("SPOTIFY_ALBUM_ID", track.SpotifyAlbumId);
            }

            return sb.ToString();
        }

        private static string FfmetaEscape(string value)
        {
            var sb = new StringBuilder(value.Length + 8);
            foreach (var ch in value)
            {
                switch (ch)
                {
                    case '\r':
                        break;
                    case '\n':
                        sb.Append(' ');
                        break;
                    case '=':
                    case ';':
                    case '#':
                    case '\\':
                        sb.Append('\\').Append(ch);
                        break;
                    default:
                        sb.Append(ch);
                        break;
                }
            }

            return sb.ToString();
        }

        private static string GetOpusBitrate(Bitrate bitrate)
        {
            switch (bitrate)
            {
                case Bitrate.Kbps128: return "128k";
                case Bitrate.Kbps160: return "160k";
                case Bitrate.Kbps256: return "256k";
                case Bitrate.Kbps320:
                case Bitrate.Insane: return "320k";
                default: return "256k";
            }
        }

        #endregion FFmpeg

        #region MP3 (FFmpeg libmp3lame)

        private async Task EncodeWaveToMp3Async(EncodeJob job, string tempEncodeFile)
        {
            // libmp3lame is LAME; ffmpeg natively handles any sample rate / channel count, so the old
            // resampler/channel "reducer" code is gone. ID3 tags + cover are applied afterwards via
            // TagLib (UpdateMediaTagsAsync), so the encode itself is audio-only.
            var ffmpegPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "binaries", "ffmpeg.exe");
            var args = $"-y -i \"{job.TempOriginalFile}\" -c:a libmp3lame {GetMp3QualityArgs(job.UserSettings.Bitrate)} " +
                       $"\"{tempEncodeFile}\"";
            await RunFFmpegAsync(ffmpegPath, args, null).ConfigureAwait(false);
        }

        private static string GetMp3QualityArgs(Bitrate bitrate)
        {
            switch (bitrate)
            {
                case Bitrate.Kbps128: return "-b:a 128k -abr 1";
                case Bitrate.Kbps160: return "-b:a 160k -abr 1";
                case Bitrate.Kbps256: return "-b:a 256k -abr 1";
                case Bitrate.Kbps320: return "-b:a 320k -abr 1";
                case Bitrate.Insane:  return "-b:a 320k"; // clean constant 320 CBR
                default:              return "-b:a 320k";
            }
        }

        #endregion MP3

        private void TryDelete(string path)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path) && _fileSystem.File.Exists(path))
                    _fileSystem.File.Delete(path);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try { _queue.CompleteAdding(); } catch { /* ignored */ }
            try { _worker?.Wait(TimeSpan.FromSeconds(60)); } catch { /* ignored */ }
            try { _queue.Dispose(); } catch { /* ignored */ }
        }
    }
}
