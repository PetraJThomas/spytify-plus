using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Resources;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using EspionSpotify.API;
using EspionSpotify.AudioSessions;
using EspionSpotify.Enums;
using EspionSpotify.Extensions;
using EspionSpotify.Models;
using EspionSpotify.Native;
using EspionSpotify.Translations;
using NAudio.Lame;
using Settings = EspionSpotify.Properties.Settings;

namespace EspionSpotify.Wpf
{
    public partial class MainWindow : Window, IFrmEspionSpotify, INotifyPropertyChanged
    {
        private static readonly Brush GreenBrush = new SolidColorBrush(Color.FromRgb(0x1E, 0xD7, 0x60));
        private static readonly Brush GrayBrush = new SolidColorBrush(Color.FromRgb(0xB3, 0xB3, 0xB3));
        private static readonly Brush MsgBrush = new SolidColorBrush(Color.FromRgb(0xB3, 0xB3, 0xB3));
        private static readonly Brush TimeBrush = new SolidColorBrush(Color.FromRgb(0x77, 0x77, 0x77));

        private readonly IMainAudioSession _audioSession;
        private readonly UserSettings _userSettings = new UserSettings();
        private readonly DispatcherTimer _timer;
        private Watcher _watcher;
        private bool _watching;
        private bool _toggleStopRecordingDelayed;
        private bool _loading;
        private System.Windows.Forms.NotifyIcon _tray;

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;
            Spytify.Form = this;

            EnsureDefaults();

            _audioSession = new MainAudioSession(Settings.Default.app_selected_audio_device_id);

            BuildResourceManager();

            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += OnTimerTick;

            StartStopCommand = new RelayCommand(_ => ToggleRecording());
            ClearLogCommand = new RelayCommand(_ => LogBox.Document.Blocks.Clear());
            CopyLogCommand = new RelayCommand(_ => CopyLog());
            BrowseOutputCommand = new RelayCommand(_ => BrowseOutput());
            OpenOutputCommand = new RelayCommand(_ => OpenOutputFolder());

            LoadState();
            ReloadExternalApi();
            InitTray();
        }

        #region System tray

        private void InitTray()
        {
            _tray = new System.Windows.Forms.NotifyIcon { Text = "Spytify", Visible = false };
            try
            {
                _tray.Icon = System.Drawing.Icon.ExtractAssociatedIcon(
                    System.Reflection.Assembly.GetEntryAssembly().Location);
            }
            catch { /* fall back to no tray icon image */ }

            var menu = new System.Windows.Forms.ContextMenuStrip();
            menu.Items.Add("Open Spytify", null, (s, e) => RestoreFromTray());
            menu.Items.Add("Exit", null, (s, e) => Close());
            _tray.ContextMenuStrip = menu;
            _tray.DoubleClick += (s, e) => RestoreFromTray();

            StateChanged += (s, e) =>
            {
                if (MinimizeToTray && WindowState == WindowState.Minimized)
                {
                    Hide();
                    _tray.Visible = true;
                }
            };

            Closed += (s, e) => { _tray?.Dispose(); _tray = null; };
        }

        private void RestoreFromTray()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
            if (_tray != null) _tray.Visible = false;
        }

        #endregion System tray

        private static void EnsureDefaults()
        {
            if (string.IsNullOrEmpty(Settings.Default.settings_output_path))
            {
                Settings.Default.settings_output_path =
                    Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
                Settings.Default.Save();
            }
        }

        #region Bindable state

        public ICommand StartStopCommand { get; }
        public ICommand ClearLogCommand { get; }
        public ICommand CopyLogCommand { get; }
        public ICommand BrowseOutputCommand { get; }
        public ICommand OpenOutputCommand { get; }

        private bool _isRecording;
        public bool IsRecording
        {
            get => _isRecording;
            set { if (Set(ref _isRecording, value)) { OnPropertyChanged(nameof(StartStopLabel)); OnPropertyChanged(nameof(SettingsEnabled)); OnPropertyChanged(nameof(StatusBrush)); } }
        }

        public bool SettingsEnabled => !IsRecording;
        public string StartStopLabel => IsRecording ? "Stop" : "Start recording";

        private string _statusGlyph = ""; // pause
        public string StatusGlyph { get => _statusGlyph; set => Set(ref _statusGlyph, value); }

        // Dot + now-playing title are green while a recording session is active and grey when
        // idle/stopped — driven by the same session flag as the pulse so they never disagree.
        public Brush StatusBrush => IsRecording ? GreenBrush : GrayBrush;

        private string _nowPlaying = "Spotify";
        public string NowPlaying { get => _nowPlaying; set => Set(ref _nowPlaying, value); }

        private string _recordedTime = "";
        public string RecordedTime { get => _recordedTime; set => Set(ref _recordedTime, value); }

        private string _recordingNumber = "001";
        public string RecordingNumber { get => _recordingNumber; set => Set(ref _recordingNumber, value); }

        // --- Output ---
        private string _outputPath;
        public string OutputPath
        {
            get => _outputPath;
            set
            {
                if (!Set(ref _outputPath, value) || _loading) return;
                _userSettings.OutputPath = FileManager.GetCleanPath(value ?? "");
                Settings.Default.settings_output_path = value;
                Settings.Default.Save();
            }
        }

        // --- Audio device ---
        public IDictionary<string, string> Devices { get; private set; }

        private string _selectedDeviceId;
        public string SelectedDeviceId
        {
            get => _selectedDeviceId;
            set
            {
                if (!Set(ref _selectedDeviceId, value) || _loading || string.IsNullOrEmpty(value)) return;
                if (Settings.Default.app_selected_audio_device_id == value) return;
                _userSettings.AudioEndPointDeviceID = value;
                _audioSession.AudioMMDevicesManager.RefreshSelectedDevice(value);
                Settings.Default.app_selected_audio_device_id = value;
                Settings.Default.Save();
                DeviceName = _audioSession.AudioMMDevicesManager.AudioEndPointDeviceName;
            }
        }

        private string _deviceName;
        public string DeviceName { get => _deviceName; set => Set(ref _deviceName, value); }

        // --- Format / bitrate ---
        public MediaFormat[] Formats { get; } = { MediaFormat.Mp3, MediaFormat.Wav, MediaFormat.Opus, MediaFormat.Flac };

        private MediaFormat _selectedFormat;
        public MediaFormat SelectedFormat
        {
            get => _selectedFormat;
            set
            {
                if (!Set(ref _selectedFormat, value)) return;
                BitrateVisible = value == MediaFormat.Mp3 || value == MediaFormat.Opus;
                if (_loading) return;
                _userSettings.MediaFormat = value;
                Settings.Default.settings_media_audio_format = (int)value;
                Settings.Default.Save();
                ReloadExternalApi();
                OnPropertyChanged(nameof(SpotifyOptionsVisible));
            }
        }

        private bool _bitrateVisible = true;
        public bool BitrateVisible { get => _bitrateVisible; set => Set(ref _bitrateVisible, value); }

        public List<KeyValuePair<LAMEPreset, string>> Bitrates { get; private set; }

        private LAMEPreset _selectedBitrate = LAMEPreset.ABR_320;
        public LAMEPreset SelectedBitrate
        {
            get => _selectedBitrate;
            set
            {
                if (!Set(ref _selectedBitrate, value) || _loading) return;
                _userSettings.Bitrate = value;
                Settings.Default.settings_media_bitrate_quality = (int)value;
                Settings.Default.Save();
            }
        }

        // --- Min length ---
        private int _minLengthSeconds;
        public int MinLengthSeconds
        {
            get => _minLengthSeconds;
            set
            {
                if (!Set(ref _minLengthSeconds, value)) return;
                OnPropertyChanged(nameof(MinLengthDisplay));
                if (_loading) return;
                _userSettings.MinimumRecordedLengthSeconds = value;
                Settings.Default.settings_media_minimum_recorded_length_in_seconds = value;
                Settings.Default.Save();
            }
        }

        public string MinLengthDisplay => TimeSpan.FromSeconds(MinLengthSeconds).ToString(@"m\:ss");

        // --- Metadata API ---
        private bool _isSpotifySelected;
        public bool IsSpotifySelected
        {
            get => _isSpotifySelected;
            set
            {
                if (!Set(ref _isSpotifySelected, value)) return;
                OnPropertyChanged(nameof(SpotifyCredentialsVisible));
                if (_loading || !value) return;
                Settings.Default.app_selected_external_api_id = (int)ExternalAPIType.Spotify;
                Settings.Default.Save();
                ReloadExternalApi();
            }
        }

        private bool _isLastFmSelected = true;
        public bool IsLastFmSelected
        {
            get => _isLastFmSelected;
            set
            {
                if (!Set(ref _isLastFmSelected, value) || _loading || !value) return;
                Settings.Default.app_selected_external_api_id = (int)ExternalAPIType.LastFM;
                Settings.Default.Save();
                ReloadExternalApi();
            }
        }

        public bool SpotifyApiConfigured => _userSettings.IsSpotifyAPISet;
        public bool SpotifyOptionsVisible => SelectedFormat != MediaFormat.Wav;
        public bool SpotifyCredentialsVisible => IsSpotifySelected;

        private string _spotifyClientId;
        public string SpotifyClientId
        {
            get => _spotifyClientId;
            set
            {
                if (!Set(ref _spotifyClientId, value) || _loading) return;
                _userSettings.SpotifyAPIClientId = value?.Trim();
                Settings.Default.app_spotify_api_client_id = value?.Trim();
                Settings.Default.Save();
                OnPropertyChanged(nameof(SpotifyApiConfigured));
            }
        }

        private string _spotifySecretId;
        public string SpotifySecretId
        {
            get => _spotifySecretId;
            set
            {
                if (!Set(ref _spotifySecretId, value) || _loading) return;
                _userSettings.SpotifyAPISecretId = value?.Trim();
                Settings.Default.app_spotify_api_client_secret = value?.Trim();
                Settings.Default.Save();
                OnPropertyChanged(nameof(SpotifyApiConfigured));
            }
        }

        private string _spotifyRedirectUrl;
        public string SpotifyRedirectUrl
        {
            get => _spotifyRedirectUrl;
            set
            {
                if (!Set(ref _spotifyRedirectUrl, value) || _loading) return;
                _userSettings.SpotifyAPIRedirectURL = value?.Trim();
                Settings.Default.app_spotify_api_redirect_url = value?.Trim();
                Settings.Default.Save();
            }
        }

        // --- General toggles ---
        public bool MuteAds { get => _userSettings.MuteAdsEnabled; set => SetToggle(value, v => { _userSettings.MuteAdsEnabled = v; Settings.Default.settings_mute_ads_enabled = v; }); }
        public bool MinimizeToTray { get => _userSettings.MinimizeToSystemTrayEnabled; set => SetToggle(value, v => { _userSettings.MinimizeToSystemTrayEnabled = v; Settings.Default.settings_minimize_to_system_tray_enabled = v; }); }

        // --- Advanced: Spy ---
        public bool ListenToPlayback { get => _userSettings.ListenToSpotifyPlaybackEnabled; set => SetToggle(value, v => { _userSettings.ListenToSpotifyPlaybackEnabled = v; Settings.Default.advanced_watcher_listen_to_spotify_playback_enabled = v; }); }
        public bool ForceSkip { get => _userSettings.ForceSpotifyToSkipEnabled; set => SetToggle(value, v => { _userSettings.ForceSpotifyToSkipEnabled = v; Settings.Default.advanced_watcher_force_spotify_to_skip = v; }); }
        public bool RecordEverything
        {
            get => _userSettings.RecordEverythingEnabled;
            set => SetToggle(value, v => { _userSettings.RecordEverythingEnabled = v; Settings.Default.advanced_record_everything = v; OnPropertyChanged(nameof(RecordAdsVisible)); });
        }
        public bool RecordAds { get => _userSettings.RecordAdsEnabled; set => SetToggle(value, v => { _userSettings.RecordAdsEnabled = v; Settings.Default.advanced_record_everything_and_ads_enabled = v; }); }
        public bool RecordAdsVisible => RecordEverything;

        // --- Advanced: Recorder / files ---
        public bool AddFolders { get => _userSettings.GroupByFoldersEnabled; set => SetToggle(value, v => { _userSettings.GroupByFoldersEnabled = v; Settings.Default.advanced_file_group_media_in_folders_enabled = v; }); }
        public bool AddSeparators
        {
            get => _userSettings.TrackTitleSeparator == "_";
            set => SetToggle(value, v => { _userSettings.TrackTitleSeparator = v ? "_" : " "; Settings.Default.advanced_file_replace_space_by_underscore_enabled = v; });
        }
        public bool CounterToFilePrefix { get => _userSettings.OrderNumberInfrontOfFileEnabled; set => SetToggle(value, v => { _userSettings.OrderNumberInfrontOfFileEnabled = v; Settings.Default.advanced_file_counter_number_prefix_enabled = v; }); }
        public bool AlbumTrackNumberPrefix { get => _userSettings.AlbumTrackNumberInfrontOfFileEnabled; set => SetToggle(value, v => { _userSettings.AlbumTrackNumberInfrontOfFileEnabled = v; Settings.Default.advanced_file_album_track_number_prefix_enabled = v; }); }
        public bool RecordOverRecordings
        {
            get => Settings.Default.advanced_record_over_recordings_enabled;
            set => SetToggle(value, v => { Settings.Default.advanced_record_over_recordings_enabled = v; _userSettings.RecordRecordingsStatus = Settings.Default.GetRecordRecordingsStatus(); OnPropertyChanged(nameof(DuplicateVisible)); });
        }
        public bool DuplicateRecordings
        {
            get => Settings.Default.advanced_record_over_recordings_and_duplicate_enabled;
            set => SetToggle(value, v => { Settings.Default.advanced_record_over_recordings_and_duplicate_enabled = v; _userSettings.RecordRecordingsStatus = Settings.Default.GetRecordRecordingsStatus(); });
        }
        public bool DuplicateVisible => RecordOverRecordings;

        // --- Advanced: ID3 ---
        public bool CounterToMediaTag { get => _userSettings.OrderNumberInMediaTagEnabled; set => SetToggle(value, v => { _userSettings.OrderNumberInMediaTagEnabled = v; Settings.Default.advanced_id3_counter_number_as_track_number_enabled = v; }); }
        public bool ExtraTitleToSubtitle { get => _userSettings.ExtraTitleToSubtitleEnabled; set => SetToggle(value, v => { _userSettings.ExtraTitleToSubtitleEnabled = v; Settings.Default.advanced_id3_extra_title_as_subtitle_enabled = v; }); }
        public bool UpdateId3Tags { get => _userSettings.UpdateRecordingsID3TagsEnabled; set => SetToggle(value, v => { _userSettings.UpdateRecordingsID3TagsEnabled = v; Settings.Default.advanced_id3_update_recordings_tags_enabled = v; }); }

        #endregion Bindable state

        #region Load

        private void LoadState()
        {
            _loading = true;

            OutputPath = Settings.Default.settings_output_path;
            _userSettings.OutputPath = FileManager.GetCleanPath(Settings.Default.settings_output_path);

            SelectedFormat = (MediaFormat)Settings.Default.settings_media_audio_format;
            _userSettings.MediaFormat = SelectedFormat;

            Bitrates = BuildBitrates();
            OnPropertyChanged(nameof(Bitrates));
            SelectedBitrate = Enum.IsDefined(typeof(LAMEPreset), Settings.Default.settings_media_bitrate_quality)
                ? (LAMEPreset)Settings.Default.settings_media_bitrate_quality
                : LAMEPreset.ABR_320;
            _userSettings.Bitrate = SelectedBitrate;

            MinLengthSeconds = Settings.Default.settings_media_minimum_recorded_length_in_seconds;
            _userSettings.MinimumRecordedLengthSeconds = MinLengthSeconds;

            // devices
            Devices = _audioSession.AudioMMDevicesManager.AudioEndPointDeviceNames;
            OnPropertyChanged(nameof(Devices));
            SelectedDeviceId = _audioSession.IsAudioEndPointDeviceIndexAvailable
                ? _audioSession.AudioMMDevicesManager.AudioEndPointDeviceID
                : _audioSession.AudioMMDevicesManager.DefaultAudioEndPointDeviceID;
            _userSettings.AudioEndPointDeviceID = _audioSession.AudioMMDevicesManager.AudioEndPointDeviceID;
            DeviceName = _audioSession.AudioMMDevicesManager.AudioEndPointDeviceName;

            // metadata api
            _userSettings.SpotifyAPIClientId = Settings.Default.app_spotify_api_client_id?.Trim();
            _userSettings.SpotifyAPISecretId = Settings.Default.app_spotify_api_client_secret?.Trim();
            _userSettings.SpotifyAPIRedirectURL = Settings.Default.app_spotify_api_redirect_url?.Trim();
            SpotifyClientId = _userSettings.SpotifyAPIClientId;
            SpotifySecretId = _userSettings.SpotifyAPISecretId;
            SpotifyRedirectUrl = _userSettings.SpotifyAPIRedirectURL;
            IsSpotifySelected = Settings.Default.app_selected_external_api_id == (int)ExternalAPIType.Spotify && _userSettings.IsSpotifyAPISet;
            IsLastFmSelected = !IsSpotifySelected;

            // remaining UserSettings mirror (so a recording can start straight away)
            _userSettings.RecordRecordingsStatus = Settings.Default.GetRecordRecordingsStatus();
            _userSettings.ListenToSpotifyPlaybackEnabled = Settings.Default.advanced_watcher_listen_to_spotify_playback_enabled;
            _userSettings.GroupByFoldersEnabled = Settings.Default.advanced_file_group_media_in_folders_enabled;
            _userSettings.OrderNumberInfrontOfFileEnabled = Settings.Default.advanced_file_counter_number_prefix_enabled;
            _userSettings.AlbumTrackNumberInfrontOfFileEnabled = Settings.Default.advanced_file_album_track_number_prefix_enabled;
            _userSettings.OrderNumberInMediaTagEnabled = Settings.Default.advanced_id3_counter_number_as_track_number_enabled;
            _userSettings.ForceSpotifyToSkipEnabled = Settings.Default.advanced_watcher_force_spotify_to_skip;
            _userSettings.RecordEverythingEnabled = Settings.Default.advanced_record_everything;
            _userSettings.RecordAdsEnabled = Settings.Default.advanced_record_everything_and_ads_enabled;
            _userSettings.MuteAdsEnabled = Settings.Default.settings_mute_ads_enabled;
            _userSettings.MinimizeToSystemTrayEnabled = Settings.Default.settings_minimize_to_system_tray_enabled;
            _userSettings.TrackTitleSeparator = Settings.Default.advanced_file_replace_space_by_underscore_enabled ? "_" : " ";
            _userSettings.OrderNumberMask = Settings.Default.app_counter_number_mask;
            _userSettings.ExtraTitleToSubtitleEnabled = Settings.Default.advanced_id3_extra_title_as_subtitle_enabled;
            RecordingNumber = _userSettings.InternalOrderNumber.ToString(_userSettings.OrderNumberMask);

            _loading = false;

            // refresh toggle-backed properties (they read straight from _userSettings/Settings)
            foreach (var p in new[]
            {
                nameof(MuteAds), nameof(MinimizeToTray), nameof(ListenToPlayback), nameof(ForceSkip),
                nameof(RecordEverything), nameof(RecordAds), nameof(RecordAdsVisible), nameof(AddFolders),
                nameof(AddSeparators), nameof(CounterToFilePrefix), nameof(AlbumTrackNumberPrefix),
                nameof(RecordOverRecordings), nameof(DuplicateRecordings), nameof(DuplicateVisible),
                nameof(CounterToMediaTag), nameof(ExtraTitleToSubtitle), nameof(UpdateId3Tags),
                nameof(SpotifyApiConfigured), nameof(SpotifyOptionsVisible)
            }) OnPropertyChanged(p);
        }

        private List<KeyValuePair<LAMEPreset, string>> BuildBitrates() => new List<KeyValuePair<LAMEPreset, string>>
        {
            new KeyValuePair<LAMEPreset, string>(LAMEPreset.ABR_128, "128 kbps"),
            new KeyValuePair<LAMEPreset, string>(LAMEPreset.ABR_160, "160 kbps (Spotify Free)"),
            new KeyValuePair<LAMEPreset, string>(LAMEPreset.ABR_256, "256 kbps"),
            new KeyValuePair<LAMEPreset, string>(LAMEPreset.ABR_320, "320 kbps (Spotify Premium)"),
            new KeyValuePair<LAMEPreset, string>(LAMEPreset.INSANE, "320 kbps (Insane, CBR)")
        };

        private void BuildResourceManager()
        {
            var languageType = Settings.Default.settings_language.ToLanguageType() ?? LanguageType.en;
            Rm = new ResourceManager(Languages.GetResourcesManagerLanguageType(languageType));
        }

        #endregion Load

        #region Recording control

        private void ToggleRecording()
        {
            if (!Watcher.Running)
            {
                if (!Directory.Exists(_userSettings.OutputPath))
                {
                    MessageBox.Show(this, "Output folder not found:\n" + _userSettings.OutputPath,
                        "Spytify", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                SelectNav("record");
                StartRecording();
            }
            else if (_watcher != null && _watcher.RecorderUpAndRunning && !_toggleStopRecordingDelayed)
            {
                _toggleStopRecordingDelayed = true;
                Watcher.ToggleStopRecordingDelayed = true;
            }
            else
            {
                StopRecording();
            }
        }

        private void StartRecording()
        {
            if (_watching) return;
            _watching = true;
            IsRecording = true;
            _watcher = new Watcher(this, _audioSession, _userSettings);
            Task.Run(_watcher.Run);
            _timer.Start();
        }

        private void OnTimerTick(object sender, EventArgs e)
        {
            if (_watcher == null) return;
            _watcher.CountSeconds++;
            if (!Watcher.Running && !Watcher.Ready) StopRecording();
        }

        private void ReloadExternalApi()
        {
            if (_userSettings.MediaFormat == MediaFormat.Wav) { SetExternalApi(ExternalAPIType.None); return; }
            if (_userSettings.IsSpotifyAPISet && Settings.Default.app_selected_external_api_id == (int)ExternalAPIType.Spotify)
            {
                SetExternalApi(ExternalAPIType.Spotify, true);
                return;
            }
            SetExternalApi(ExternalAPIType.LastFM);
        }

        private void SetExternalApi(ExternalAPIType api, bool isSpotifyApiSet = false)
        {
            switch (api)
            {
                case ExternalAPIType.Spotify:
                    if (isSpotifyApiSet)
                        ExternalAPI.Instance = new EspionSpotify.API.SpotifyAPI(
                            _userSettings.SpotifyAPIClientId,
                            _userSettings.SpotifyAPISecretId,
                            _userSettings.SpotifyAPIRedirectURL);
                    break;
                case ExternalAPIType.LastFM:
                    ExternalAPI.Instance = new LastFMAPI();
                    break;
                default:
                    ExternalAPI.Instance = new NoneAPI();
                    break;
            }
        }

        #endregion Recording control

        #region IFrmEspionSpotify

        public ResourceManager Rm { get; private set; }

        public void UpdateStartButton() => RunOnUi(() => IsRecording = false);

        // Status dot/title colour follows the recording session (see StatusBrush);
        // the engine's per-track playing/recording flags no longer toggle it.
        public void UpdateIconSpotify(bool isSpotifyPlaying, bool isRecording = false) { }

        public void UpdatePlayingTitle(string text) => RunOnUi(() => NowPlaying = text);

        public void UpdateRecordedTime(int? time) => RunOnUi(() =>
            RecordedTime = time.HasValue ? TimeSpan.FromSeconds(time.Value).ToString(@"mm\:ss") : "");

        public void UpdateNumUp() => RunOnUi(() =>
        {
            if (!_userSettings.HasOrderNumberEnabled) return;
            _userSettings.InternalOrderNumber++;
            RecordingNumber = _userSettings.InternalOrderNumber.ToString(_userSettings.OrderNumberMask);
        });

        public void UpdateNumDown() => RunOnUi(() =>
        {
            if (!_userSettings.HasOrderNumberEnabled) return;
            _userSettings.InternalOrderNumber--;
            RecordingNumber = _userSettings.InternalOrderNumber.ToString(_userSettings.OrderNumberMask);
        });

        public void StopRecording() => RunOnUi(() =>
        {
            Watcher.Running = false;
            ExternalAPI.Instance.Reset();
            _toggleStopRecordingDelayed = false;
            _timer.Stop();
            _watching = false;
            IsRecording = false;
        });

        public void WriteIntoConsole(TranslationKeys resource, params object[] args)
        {
            string text;
            try { text = string.Format(Rm.GetString(resource) ?? resource.ToString(), args); }
            catch { text = resource.ToString(); }

            var time = $"[{DateTime.Now:HH:mm:ss}] ";
            var isStatus = resource.Equals(I18NKeys.LogRecording) || resource.Equals(I18NKeys.LogRecorded)
                        || resource.Equals(I18NKeys.LogDeleting) || resource.Equals(I18NKeys.LogTrackExists);
            var colon = text.IndexOf(": ", StringComparison.Ordinal);
            var typeBrush = resource.Equals(I18NKeys.LogRecording) ? GreenBrush : GrayBrush;

            RunOnUi(() =>
            {
                var atBottom = IsScrolledToBottom();
                var p = new Paragraph { Margin = new Thickness(0) };
                p.Inlines.Add(new Run(time) { Foreground = TimeBrush });
                if (isStatus && colon > 0)
                {
                    p.Inlines.Add(new Run(text.Substring(0, colon)) { Foreground = typeBrush, FontWeight = FontWeights.Bold });
                    p.Inlines.Add(new Run(text.Substring(colon)) { Foreground = MsgBrush });
                }
                else
                {
                    p.Inlines.Add(new Run(text) { Foreground = MsgBrush });
                }

                LogBox.Document.Blocks.Add(p);
                if (atBottom) LogBox.ScrollToEnd(); // don't yank the view if the user scrolled up to select

                Settings.Default.app_console_logs += $";{time}{text}";
                Settings.Default.Save();
            });
        }

        private bool IsScrolledToBottom()
        {
            // VerticalOffset+ViewportHeight ~= ExtentHeight when the caret view is at the bottom.
            return LogBox.VerticalOffset + LogBox.ViewportHeight >= LogBox.ExtentHeight - 2.0;
        }

        private void CopyLog()
        {
            var text = new TextRange(LogBox.Document.ContentStart, LogBox.Document.ContentEnd).Text;
            if (string.IsNullOrWhiteSpace(text)) return;
            try { Clipboard.SetText(text); } catch { /* clipboard can be transiently locked */ }
        }

        public void UpdateExternalAPIToggle(ExternalAPIType value) => RunOnUi(() =>
        {
            _isLastFmSelected = value == ExternalAPIType.LastFM;
            _isSpotifySelected = value == ExternalAPIType.Spotify;
            OnPropertyChanged(nameof(IsLastFmSelected));
            OnPropertyChanged(nameof(IsSpotifySelected));
            OnPropertyChanged(nameof(SpotifyCredentialsVisible));
        });

        public void ShowFailedToUseSpotifyAPIMessage() => RunOnUi(() =>
            MessageBox.Show(this, "Couldn't use the Spotify API — falling back to Last.fm.",
                "Spytify", MessageBoxButton.OK, MessageBoxImage.Information));

        public void UpdateAudioDevicesDataSource() => RunOnUi(() =>
        {
            Devices = _audioSession.AudioMMDevicesManager.AudioEndPointDeviceNames;
            OnPropertyChanged(nameof(Devices));
            var id = _audioSession.IsAudioEndPointDeviceIndexAvailable
                ? _audioSession.AudioMMDevicesManager.AudioEndPointDeviceID
                : _audioSession.AudioMMDevicesManager.DefaultAudioEndPointDeviceID;
            _loading = true;
            SelectedDeviceId = id;
            _loading = false;
            DeviceName = _audioSession.AudioMMDevicesManager.AudioEndPointDeviceName;
        });

        // No Windows-volume slider surfaced in this UI yet.
        public void SetSoundVolume(int volume) { }

        public bool AskUpdate(string title, string message)
        {
            return Dispatcher.Invoke(() => MessageBox.Show(this, message, title,
                MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK);
        }

        #endregion IFrmEspionSpotify

        #region Helpers / UI events

        private void RunOnUi(Action action)
        {
            if (Dispatcher.CheckAccess()) action();
            else Dispatcher.BeginInvoke(action);
        }

        private void SelectNav(string tag)
        {
            if (Nav == null) return;
            var item = Nav.MenuItems.OfType<ModernWpf.Controls.NavigationViewItem>()
                .FirstOrDefault(i => (string)i.Tag == tag);
            if (item != null) Nav.SelectedItem = item;
        }

        private static readonly Duration PanelFade = new Duration(TimeSpan.FromMilliseconds(160));

        private void Nav_SelectionChanged(ModernWpf.Controls.NavigationView sender,
            ModernWpf.Controls.NavigationViewSelectionChangedEventArgs args)
        {
            var tag = (args.SelectedItem as ModernWpf.Controls.NavigationViewItem)?.Tag as string ?? "record";
            SetPanelActive(RecordPanel, tag == "record");
            SetPanelActive(SettingsPanel, tag == "settings");
            SetPanelActive(AdvancedPanel, tag == "advanced");
        }

        // Cross-fade with no explicit From and no position transform. Inactive panels are
        // pre-set to opacity 0, so the incoming panel always animates from its real current
        // value up to 1 — re-triggering mid-fade is seamless and never snaps, no matter how
        // fast you click between sections.
        private void SetPanelActive(FrameworkElement el, bool active)
        {
            if (el == null) return;
            if (active)
            {
                el.Visibility = Visibility.Visible;
                el.BeginAnimation(OpacityProperty,
                    new DoubleAnimation(1, PanelFade) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            }
            else
            {
                el.BeginAnimation(OpacityProperty, null); // release the held animated value
                el.Opacity = 0;
                el.Visibility = Visibility.Collapsed;
            }
        }

        private void BrowseOutput()
        {
            using (var dlg = new System.Windows.Forms.FolderBrowserDialog())
            {
                if (Directory.Exists(OutputPath)) dlg.SelectedPath = OutputPath;
                if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    OutputPath = dlg.SelectedPath;
            }
        }

        private void OpenOutputFolder()
        {
            if (Directory.Exists(OutputPath))
                System.Diagnostics.Process.Start("explorer.exe", OutputPath);
        }

        private void SetToggle(bool value, Action<bool> apply, [CallerMemberName] string name = null)
        {
            apply(value);
            if (!_loading) Settings.Default.Save();
            OnPropertyChanged(name);
        }

        #endregion Helpers / UI events

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler PropertyChanged;

        private bool Set<T>(ref T field, T value, [CallerMemberName] string name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(name);
            return true;
        }

        private void OnPropertyChanged([CallerMemberName] string name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        #endregion INotifyPropertyChanged
    }
}
