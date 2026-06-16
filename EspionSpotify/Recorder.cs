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
using NAudio.Lame;
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

        public int CountSeconds { get; set; }
        public bool Running { get; set; }

        private bool TrackIsFetchingMetadata => _track.MetaDataUpdated == null && !_userSettings.RecordEverythingEnabled && _userSettings.MediaFormat == MediaFormat.Mp3;

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
            _tempOriginalFile = null; // ownership transferred — do not delete it here

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
                    try
                    {
                        using (new LameMP3FileWriter(new MemoryStream(), waveIn.WaveFormat, settings.Bitrate))
                        {
                            return true;
                        }
                    }
                    catch (ArgumentException ex)
                    {
                        return LogLameMP3FileWriterArgumentException(form, ex, waveIn.WaveFormat);
                    }
                    catch (Exception ex)
                    {
                        LogLameMP3FileWriterException(form, ex);
                        return false;
                    }

                case MediaFormat.Wav:
                case MediaFormat.Opus: // Correction : On autorise le format Ogg � passer le test de validation
                case MediaFormat.Flac:
                    try
                    {
                        // Pour l'Ogg, on teste la capacit� � �crire un flux WAV (notre �tape interm�diaire)
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
                    // Si le format n'est ni Mp3, ni Wav, ni Ogg, le test �choue
                    return false;
            }
        }

        #endregion TestFileWriter

        #region RecorderUpdateMp3MataData

        private async Task UpdateMediaTagsWhenSkippingTrack()
        {
            if (!_userSettings.UpdateRecordingsID3TagsEnabled) return;

            _currentOutputFile = _fileManager.GetOutputFileAndInitDirectories();
            await UpdateMediaTagsFileBasedOnMediaFormat();
        }

        private async Task UpdateMediaTagsFileBasedOnMediaFormat()
        {
            if (!_fileSystem.File.Exists(_currentOutputFile.ToMediaFilePath())) return;

            switch (_userSettings.MediaFormat)
            {
                case MediaFormat.Mp3:
                case MediaFormat.Wav:
                    var mapper = new MapperID3(
                        _currentOutputFile.ToMediaFilePath(),
                        _track,
                        _userSettings);
                    await Task.Run(mapper.SaveMediaTags);
                    return;
                default:
                    return;
            }
        }

        #endregion RecorderUpdateMp3MataData

        #region MP3ConverterReducer

        private static bool LogLameMP3FileWriterArgumentException(IFrmEspionSpotify form, ArgumentException ex,
            WaveFormat waveFormat)
        {
            var restrictions = waveFormat.GetMP3RestrictionCode().ToList();
            if (restrictions.Any())
            {
                if (restrictions.Contains(WaveFormatMP3Restriction.Channel))
                    form.WriteIntoConsole(I18NKeys.LogUnsupportedNumberChannels, waveFormat.Channels);
                if (restrictions.Contains(WaveFormatMP3Restriction.SampleRate))
                    form.WriteIntoConsole(I18NKeys.LogUnsupportedRate, waveFormat.SampleRate);
                return true;
            }

            form.UpdateIconSpotify(true);
            form.WriteIntoConsole(I18NKeys.LogUnknownException, ex.Message);
            return false;
        }

        private static void LogLameMP3FileWriterException(IFrmEspionSpotify form, Exception ex)
        {
            if (ex.Message.Contains("Unable to load DLL"))
            {
                form.WriteIntoConsole(I18NKeys.LogMissingDlls);
            }
            else
            {
                Program.ReportException(ex);
                form.WriteIntoConsole(I18NKeys.LogUnknownException, ex.Message);
            }

            form.UpdateIconSpotify(true);
            Console.WriteLine(ex.Message);
        }

        #endregion MP3ConverterReducer

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
            // and the service owns the temp WAV — so this only deletes WAVs we still own.
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