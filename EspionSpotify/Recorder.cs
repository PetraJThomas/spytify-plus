using System;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EspionSpotify.API;
using EspionSpotify.AudioSessions;
using EspionSpotify.Enums;
using EspionSpotify.Exceptions;
using EspionSpotify.Extensions;
using EspionSpotify.Models;
using EspionSpotify.Native;
using EspionSpotify.Spotify;
using EspionSpotify.Translations;
using NAudio.Wave;

namespace EspionSpotify
{
    public sealed class Recorder : IRecorder, IDisposable
    {
        public const int MP3_MAX_NUMBER_CHANNELS = 2;
        public const int MP3_MAX_SAMPLE_RATE = 48000;
        
        private readonly bool _initiated;
        private readonly FileManager _fileManager;
        private readonly IFileSystem _fileSystem;
        private readonly IFrmEspionSpotify _form;
        private readonly Track _track;
        private readonly IAudioThrottler _audioThrottler;
        private readonly UserSettings _userSettings;
        private readonly IEncodeService _encodeService;

        private bool _disposed;
        private string _tempOriginalFile;
        private bool _canBeSkippedValidated;
        private CancellationTokenSource _cancellationTokenSource;
        private OutputFile _currentOutputFile;
        private Stream _tempWaveWriter;
        private IProcessManager _processManager;

        public Recorder()
        {
            _track = new Track();
        }

        internal Recorder(
            IFrmEspionSpotify form,
            IAudioThrottler audioThrottler,
            UserSettings userSettings,
            ref Track track,
            IFileSystem fileSystem,
            IEncodeService encodeService) : this(form, audioThrottler, userSettings, ref track, fileSystem, encodeService, new ProcessManager(), init: true) { }

        public Recorder(
            IFrmEspionSpotify form,
            IAudioThrottler audioThrottler,
            UserSettings userSettings,
            ref Track track,
            IFileSystem fileSystem,
            IEncodeService encodeService,
            IProcessManager processManager,
            bool init)
        {
            _userSettings = new UserSettings();
            userSettings.CopyAllTo(_userSettings);

            _form = form;
            _audioThrottler = audioThrottler;
            _fileSystem = fileSystem;
            _encodeService = encodeService;
            _track = track;
            _fileManager = new FileManager(_userSettings, _track, fileSystem);
            _processManager = processManager;

            _initiated = init && Init();
        }

        public Track Track => _track;

        public bool IsSkipTrackActive =>
            _userSettings.RecordRecordingsStatus == RecordRecordingsStatus.Skip
            && _fileManager.IsPathFileNameExists(_track, _userSettings, _fileSystem);

        // A zero-byte take shorter than this is treated as a fast skip, not an endpoint misconfig.
        private const int MinSecondsForEmptyEndpointWarning = 2;

        public int CountSeconds { get; set; }
        public bool Running { get; set; }

        // True while the track's metadata (album, year, etc.) is still being fetched from the API.
        // The skip/re-tag path waits on this so the file path it computes matches an existing file:
        // the album comes from the API a beat after the track starts, so checking too early would
        // miss the existing file. Applies to every format (not just MP3): MetaDataUpdated is always
        // set to a non-null result once the fetch completes, so this can't wait forever.
        private bool TrackIsFetchingMetadata => _track.MetaDataUpdated == null && !_userSettings.RecordEverythingEnabled;

        private WaveFormat WaveFormat => _audioThrottler.WaveFormat;

        private bool Init()
        {
            _tempOriginalFile = _fileManager.GetTempFile();

            try
            {
                _tempWaveWriter = new WaveFileWriter(_tempOriginalFile, WaveFormat);
            }
            catch (Exception ex)
            {
                ForceStopRecording();
                _form.WriteIntoConsole(I18NKeys.LogUnknownException, ex.Message);
                Console.WriteLine(ex.Message);
                Program.ReportException(ex);
                return false;
            }

            return true;
        }

        #region RecorderStart

        public async Task Run(CancellationTokenSource cancellationTokenSource)
        {
            _cancellationTokenSource = cancellationTokenSource;

            if (!_initiated || _userSettings.InternalOrderNumber > _userSettings.OrderNumberMax) return;

            _form.WriteIntoConsole(I18NKeys.LogRecording, _track.ToString());
            Running = true;

            // await _audioThrottler.WaitBufferReady();
            await RecordAvailableData(SilenceAnalyzer.TrimStart);
            
            while (Running)
            {
                if (_cancellationTokenSource.IsCancellationRequested) return;
                if (await StopRecordingIfTrackCanBeSkipped()) return;
                await RecordAvailableData(SilenceAnalyzer.None);
            }

            await RecordAvailableData(SilenceAnalyzer.TrimEnd);

            await RecordingStopped();
        }

        #endregion RecorderStart

        #region RecorderWriteUpcomingData

        private async Task RecordAvailableData(SilenceAnalyzer analyzer)
        {
            if (_tempWaveWriter == null) return;
            
            var audio = await _audioThrottler.Read(analyzer);
            if (audio == null) return;
            
            await Task.Run(async () =>
            {
                await _tempWaveWriter.WriteAsync(
                    audio.Buffer,
                    0,
                    audio.BytesRecordedCount);
            });
        }

        #endregion RecorderWriteUpcomingData

        #region RecorderStopRecording
        
        private async Task<bool> StopRecordingIfTrackCanBeSkipped()
        {
            if (_canBeSkippedValidated || TrackIsFetchingMetadata) return false;

            _canBeSkippedValidated = true;
            if (IsSkipTrackActive)
            {
                // Verify the EXISTING file too: if length-verification is on and the file already in
                // the library is truncated (a bad earlier rip, shorter than the track), don't skip.
                // Record fresh instead; the encode path only overwrites when the new take is valid,
                // so a truncated file is replaced when possible and kept if the re-record is also cut
                // short. This makes verify-length apply in skip mode, not just to new recordings.
                if (_userSettings.VerifyRecordingLength && ExistingRecordingIsTruncated())
                {
                    _form.WriteIntoConsole(I18NKeys.LogExistingTruncatedRerecording, _track.ToString());
                    return false; // fall through to a fresh recording
                }

                _form.WriteIntoConsole(I18NKeys.LogTrackExists, _track.ToString());
                await UpdateMediaTagsWhenSkippingTrack();
                ForceStopRecording();
                if (_userSettings.ForceSpotifyToSkipEnabled)
                {
                    var spotifyHandler = SpotifyProcess.GetMainSpotifyHandler(_processManager);
                    if (spotifyHandler.HasValue)
                    {
                        NativeMethods.SendKeyPessNextMedia(spotifyHandler.Value);
                    }
                }
                
                return true;
            }

            return false;
        }

        // True when the file already on disk for this track is truncated versus the track's known
        // length (same 80% rule as new captures, via EncodeService.IsTruncatedCapture). Best-effort:
        // an unreadable file or an unknown track length counts as "not truncated" so we never
        // re-record on a guess.
        private bool ExistingRecordingIsTruncated()
        {
            if (!_track.Length.HasValue || _track.Length.Value <= 0) return false;
            try
            {
                var path = _fileManager.GetOutputFileAndInitDirectories().ToMediaFilePath();
                int seconds;
                using (var f = TagLib.File.Create(path))
                    seconds = (int)Math.Round(f.Properties.Duration.TotalSeconds);
                return seconds > 0 && EncodeService.IsTruncatedCapture(true, seconds, _track.Length);
            }
            catch
            {
                return false;
            }
        }

        private async Task RecordingStopped()
        {
            while (TrackIsFetchingMetadata) await Task.Delay(100);
            var skipped = !_canBeSkippedValidated && await StopRecordingIfTrackCanBeSkipped();
            if (_tempWaveWriter == null || skipped)
            {
                ForceStopRecording();
                return;
            }

            await _tempWaveWriter.FlushAsync();
            var isTempWaveEmpty = _tempWaveWriter.Length == 0;

            TempWaveWriterDispose();

            if (isTempWaveEmpty)
            {
                // Zero captured bytes usually means Spotify's audio is going to a different endpoint.
                // But a fast skip (the track changed before any audio arrived) also yields zero bytes
                // and is not an endpoint problem, so only surface the warning when the take actually
                // ran for a moment. Otherwise this is just noise while spamming skip.
                if (CountSeconds >= MinSecondsForEmptyEndpointWarning)
                    _form.WriteIntoConsole(I18NKeys.LogSpotifyPlayingOutsideOfSelectedAudioEndPoint);
                ForceStopRecording();
                return;
            }

            // Hand the finished capture to the background encoder and return immediately.
            // From here the encode service owns the temp WAV: it encodes (off the recording
            // path), moves the result to its final destination, tags it and cleans up.
            _encodeService.Enqueue(new EncodeJob
            {
                TempOriginalFile = _tempOriginalFile,
                Track = new Track(_track),
                UserSettings = _userSettings,
                CountSeconds = CountSeconds
            });
            _tempOriginalFile = null; // ownership transferred, do not delete it here

            Running = false;
            _form.UpdateIconSpotify(true);
        }

        #endregion RecorderStopRecording

        #region TestFileWriter

        public static bool TestFileWriter(IFrmEspionSpotify form, IMainAudioSession audioSession, UserSettings settings)
        {
            if (audioSession.AudioMMDevicesManager.AudioEndPointDevice == null) return false;

            var waveIn = new WasapiLoopbackCapture(audioSession.AudioMMDevicesManager.AudioEndPointDevice);

            switch (settings.MediaFormat)
            {
                case MediaFormat.Mp3:
                case MediaFormat.Wav:
                case MediaFormat.Opus:
                case MediaFormat.Flac:
                    // Every format records to a temp WAV first (then ffmpeg encodes), so validating the
                    // WAV writer is enough; ffmpeg handles any sample rate / channel count downstream.
                    try
                    {
                        using (new WaveFileWriter(new MemoryStream(), waveIn.WaveFormat))
                        {
                            return true;
                        }
                    }
                    catch (Exception ex)
                    {
                        form.UpdateIconSpotify(true, false);
                        form.WriteIntoConsole(I18NKeys.LogUnknownException, ex.Message);
                        Console.WriteLine(ex.Message);
                        Program.ReportException(ex);
                        return false;
                    }

                default:
                    return false;
            }
        }

        #endregion TestFileWriter

        #region RecorderUpdateMp3MataData

        private async Task UpdateMediaTagsWhenSkippingTrack()
        {
            // Nothing to refresh unless we're re-tagging and/or writing the sidecar cover.
            if (!_userSettings.UpdateRecordingsID3TagsEnabled && !_userSettings.SaveCoverFile) return;

            _currentOutputFile = _fileManager.GetOutputFileAndInitDirectories();

            var updated = false;
            if (_userSettings.UpdateRecordingsID3TagsEnabled)
                updated = await UpdateMediaTagsFileBasedOnMediaFormat();

            // The embedded cover is refreshed by the re-tag above; also drop/refresh the sidecar
            // cover.jpg / Folder.jpg so a skip-scrub backfills them onto folders recorded before
            // the feature existed, not only on fresh recordings. Best-effort, once per folder.
            if (_userSettings.SaveCoverFile)
                await EncodeService.SaveCoverFilesAsync(_fileSystem, _currentOutputFile, _track);

            // Confirm in the console when we actually re-tagged an existing file, so a skip-scrub is
            // visible and it's obvious when the file wasn't found (nothing written).
            if (updated)
                _form.WriteIntoConsole(I18NKeys.LogMetadataUpdated, _track.ToString());
        }

        private async Task<bool> UpdateMediaTagsFileBasedOnMediaFormat()
        {
            if (!_fileSystem.File.Exists(_currentOutputFile.ToMediaFilePath())) return false;

            switch (_userSettings.MediaFormat)
            {
                // TagLib re-writes tags in place for every container (ID3 for MP3/WAV, Vorbis
                // comments for FLAC/OPUS), so re-tagging a skipped/replayed track works for all.
                case MediaFormat.Mp3:
                case MediaFormat.Wav:
                case MediaFormat.Flac:
                case MediaFormat.Opus:
                    var mapper = new MapperID3(
                        _currentOutputFile.ToMediaFilePath(),
                        _track,
                        _userSettings);
                    await Task.Run(mapper.SaveMediaTags);
                    return true;
                default:
                    return false;
            }
        }

        #endregion RecorderUpdateMp3MataData

        #region DisposeRecorder

        private void ForceStopRecording()
        {
            _form.UpdateIconSpotify(true);
            Running = false;

            EndRecording();
        }

        private void EndRecording()
        {
            TempWaveWriterDispose();

            // Once a capture has been handed to the encode service, _tempOriginalFile is null
            // and the service owns the temp WAV, so this only deletes WAVs we still own.
            if (!string.IsNullOrWhiteSpace(_tempOriginalFile)) _fileManager.DeleteFile(_tempOriginalFile);
        }

        private void TempWaveWriterDispose()
        {
            if (_tempWaveWriter == null) return;
            _tempWaveWriter.Close();
            _tempWaveWriter.Dispose();
            _tempWaveWriter = null;
        }

        public void Dispose()
        {
            Dispose(true);

            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing) ForceStopRecording();

            _disposed = true;
        }

        #endregion DisposeRecorder
    }
}