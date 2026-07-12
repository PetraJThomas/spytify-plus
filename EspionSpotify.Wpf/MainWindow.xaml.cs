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
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EspionSpotify.API;
using EspionSpotify.AudioSessions;
using EspionSpotify.Drivers;
using EspionSpotify.Enums;
using EspionSpotify.Extensions;
using EspionSpotify.Models;
using EspionSpotify.Native;
using EspionSpotify.Translations;
using Settings = EspionSpotify.Properties.Settings;

namespace EspionSpotify.Wpf
{
    public partial class MainWindow : Window, IFrmEspionSpotify, INotifyPropertyChanged
    {
        private static readonly Brush GreenBrush = new SolidColorBrush(Color.FromRgb(0x1E, 0xD7, 0x60));
        private static readonly Brush GrayBrush = new SolidColorBrush(Color.FromRgb(0xB3, 0xB3, 0xB3));
        private static readonly Brush MsgBrush = new SolidColorBrush(Color.FromRgb(0xB3, 0xB3, 0xB3));
        private static readonly Brush TimeBrush = new SolidColorBrush(Color.FromRgb(0x77, 0x77, 0x77));
        private static readonly Brush AmberBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xB7, 0x4D));
        private static readonly Brush RedBrush = new SolidColorBrush(Color.FromRgb(0xEF, 0x53, 0x50));

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
            NumPlusCommand = new RelayCommand(_ => NumAdjust(+1));
            NumMinusCommand = new RelayCommand(_ => NumAdjust(-1));
            InstallDriverCommand = new RelayCommand(_ => InstallDriver());

            LoadState();
            RefreshSpotifyConnState(); // set the initial connect status in the current language
            ReloadExternalApi();
            InitTray();

            Loaded += (s, e) =>
            {
                var dpd = System.ComponentModel.DependencyPropertyDescriptor.FromProperty(
                    ModernWpf.Controls.NavigationView.IsPaneOpenProperty,
                    typeof(ModernWpf.Controls.NavigationView));
                dpd?.AddValueChanged(Nav, (s2, e2) => UpdateLogo(Nav.IsPaneOpen));
                UpdateLogo(Nav.IsPaneOpen);
            };
        }

        // Cross-fade the wordmark logo (expanded pane) and the icon logo (collapsed pane).
        private void UpdateLogo(bool paneOpen)
        {
            var dur = new Duration(TimeSpan.FromMilliseconds(180));
            LogoFull.BeginAnimation(OpacityProperty, new DoubleAnimation(paneOpen ? 1 : 0, dur) { EasingFunction = EaseOut() });
            LogoIcon.BeginAnimation(OpacityProperty, new DoubleAnimation(paneOpen ? 0 : 1, dur) { EasingFunction = EaseOut() });
        }

        #region System tray

        private void InitTray()
        {
            _tray = new System.Windows.Forms.NotifyIcon { Text = "Spytify+", Visible = false };
            try
            {
                _tray.Icon = System.Drawing.Icon.ExtractAssociatedIcon(
                    System.Reflection.Assembly.GetEntryAssembly().Location);
            }
            catch { /* fall back to no tray icon image */ }

            var menu = new System.Windows.Forms.ContextMenuStrip();
            menu.Items.Add("Open Spytify+", null, (s, e) => RestoreFromTray());
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
                // Default to <Music>\Spytify rather than the Music root, so recordings don't
                // scatter artist/album folders all over the user's library.
                var music = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
                var spytify = Path.Combine(music, "Spytify");
                try { Directory.CreateDirectory(spytify); } catch { /* fall back to the path string */ }
                Settings.Default.settings_output_path = spytify;
                Settings.Default.Save();
            }
        }

        #region Bindable state

        public ICommand StartStopCommand { get; }
        public ICommand ClearLogCommand { get; }
        public ICommand CopyLogCommand { get; }
        public ICommand BrowseOutputCommand { get; }
        public ICommand OpenOutputCommand { get; }
        public ICommand NumPlusCommand { get; }
        public ICommand NumMinusCommand { get; }
        public ICommand InstallDriverCommand { get; }

        private bool _isRecording;
        public bool IsRecording
        {
            get => _isRecording;
            set { if (Set(ref _isRecording, value)) { OnPropertyChanged(nameof(StartStopLabel)); OnPropertyChanged(nameof(SettingsEnabled)); OnPropertyChanged(nameof(StatusBrush)); } }
        }

        public bool SettingsEnabled => !IsRecording;
        public string StartStopLabel => IsRecording ? Loc.Instance["lblStop"] : Loc.Instance["lblStartRecording"];

        private string _statusGlyph = ""; // pause
        public string StatusGlyph { get => _statusGlyph; set => Set(ref _statusGlyph, value); }

        // Dot + now-playing title are green while a recording session is active and grey when
        // idle/stopped, driven by the same session flag as the pulse so they never disagree.
        public Brush StatusBrush => IsRecording ? GreenBrush : GrayBrush;

        private string _nowPlaying = "Spotify";
        public string NowPlaying { get => _nowPlaying; set => Set(ref _nowPlaying, value); }

        // Cover art of the track currently playing/recording, shown on the player card.
        // Null when idle, during ads, or before the API has filled the URL (placeholder shows through).
        private ImageSource _albumArt;
        public ImageSource AlbumArt { get => _albumArt; set { if (Set(ref _albumArt, value)) OnPropertyChanged(nameof(HasAlbumArt)); } }
        public bool HasAlbumArt => _albumArt != null;

        private string _recordedTime = "";
        public string RecordedTime { get => _recordedTime; set => Set(ref _recordedTime, value); }

        private string _recordingNumber = "001";
        public string RecordingNumber
        {
            get => _recordingNumber;
            set
            {
                if (_loading) { Set(ref _recordingNumber, value); return; }
                // user-edited (LostFocus): keep the digits, clamp to the mask range, reformat
                var digits = new string((value ?? "").Where(char.IsDigit).ToArray());
                if (int.TryParse(digits, out var n))
                    _userSettings.InternalOrderNumber = Math.Max(0, Math.Min(n, _userSettings.OrderNumberMax));
                Set(ref _recordingNumber, _userSettings.InternalOrderNumber.ToString(_userSettings.OrderNumberMask));
            }
        }

        // Auto-stop timer, shown/edited as HH:MM:SS, stored as the engine's "HHMMSS" string.
        private string _recordingTimerText = "";
        public string RecordingTimerText
        {
            get => _recordingTimerText;
            set
            {
                var digits = new string((value ?? "").Where(char.IsDigit).ToArray());
                if (digits.Length > 6) digits = digits.Substring(0, 6);
                _userSettings.RecordingTimer = digits.Length == 0 ? null : digits.PadLeft(6, '0');
                Set(ref _recordingTimerText, FormatTimer(_userSettings.RecordingTimer));
            }
        }

        private static string FormatTimer(string hhmmss) =>
            string.IsNullOrEmpty(hhmmss) || hhmmss.Length != 6
                ? ""
                : $"{hhmmss.Substring(0, 2)}:{hhmmss.Substring(2, 2)}:{hhmmss.Substring(4, 2)}";

        private void NumAdjust(int dir)
        {
            var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
            if (ctrl)
            {
                // Ctrl: grow/shrink the zero-pad width (e.g. "000" <-> "0000")
                var mask = _userSettings.OrderNumberMask ?? "000";
                if (dir > 0 && mask.Length < 6) mask += "0";
                else if (dir < 0 && mask.Length > 1) mask = mask.Substring(1);
                _userSettings.OrderNumberMask = mask;
                Settings.Default.app_counter_number_mask = mask;
                Settings.Default.Save();
            }
            else
            {
                var n = _userSettings.InternalOrderNumber + dir;
                if (n >= 0 && n <= _userSettings.OrderNumberMax) _userSettings.InternalOrderNumber = n;
            }

            _recordingNumber = _userSettings.InternalOrderNumber.ToString(_userSettings.OrderNumberMask);
            OnPropertyChanged(nameof(RecordingNumber));
        }

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
                RefreshAudioState();
            }
        }

        private string _deviceName;
        public string DeviceName { get => _deviceName; set => Set(ref _deviceName, value); }

        // --- Windows volume for the selected device ---
        private bool _suppressVolumeApply;
        private int _volume;
        public int Volume
        {
            get => _volume;
            set
            {
                if (!Set(ref _volume, value)) return;
                OnPropertyChanged(nameof(VolumePercent));
                if (_loading || _suppressVolumeApply) return;

                var mgr = _audioSession.AudioMMDevicesManager;
                if (mgr.AudioEndPointDeviceMute == true && mgr.AudioEndPointDevice?.AudioEndpointVolume != null)
                    mgr.AudioEndPointDevice.AudioEndpointVolume.Mute = false;
                _audioSession.SetAudioDeviceVolume(value);
            }
        }

        public string VolumePercent => $"{_volume}%";

        private bool _volumeEnabled;
        public bool VolumeEnabled { get => _volumeEnabled; set => Set(ref _volumeEnabled, value); }

        // --- VB-Audio virtual cable driver ---
        public bool DriverButtonVisible => AudioVirtualCableDriver.IsFound;

        private string _driverButtonText = "Install virtual cable driver";
        public string DriverButtonText { get => _driverButtonText; set => Set(ref _driverButtonText, value); }

        private void RefreshAudioState()
        {
            VolumeEnabled = _audioSession.AudioMMDevicesManager.AudioEndPointDeviceName != null;
            _suppressVolumeApply = true;
            Volume = _audioSession.AudioDeviceVolume;
            _suppressVolumeApply = false;

            OnPropertyChanged(nameof(DriverButtonVisible));
            if (DriverButtonVisible)
                DriverButtonText = AudioVirtualCableDriver.ExistsInAudioEndPointDevices(
                    _audioSession.AudioMMDevicesManager.AudioEndPointDeviceNames)
                    ? "Reinstall virtual cable driver"
                    : "Install virtual cable driver";
        }

        private async void InstallDriver()
        {
            if (!AudioVirtualCableDriver.IsFound) return;
            var ok = await Task.Run(() => AudioVirtualCableDriver.SetupDriver());
            if (!ok)
                MessageBox.Show(this, Loc.Instance["msgCableInstallFailed"],
                    "Spytify+", MessageBoxButton.OK, MessageBoxImage.Warning);
            RefreshAudioState();
        }

        // --- Format / bitrate ---
        public List<KeyValuePair<MediaFormat, string>> Formats { get; } = new List<KeyValuePair<MediaFormat, string>>
        {
            new KeyValuePair<MediaFormat, string>(MediaFormat.Mp3, "MP3"),
            new KeyValuePair<MediaFormat, string>(MediaFormat.Wav, "WAV"),
            new KeyValuePair<MediaFormat, string>(MediaFormat.Opus, "Opus"),
            new KeyValuePair<MediaFormat, string>(MediaFormat.Flac, "FLAC")
        };

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

        public List<KeyValuePair<Bitrate, string>> Bitrates { get; private set; }

        private Bitrate _selectedBitrate = Bitrate.Kbps320;
        public Bitrate SelectedBitrate
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
                Settings.Default.app_spotify_api_client_id = Crypto.Encrypt(value?.Trim());
                Settings.Default.Save();
                OnPropertyChanged(nameof(SpotifyApiConfigured));
                OnPropertyChanged(nameof(ClientIdMasked));
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
                Settings.Default.app_spotify_api_client_secret = Crypto.Encrypt(value?.Trim());
                Settings.Default.Save();
                OnPropertyChanged(nameof(SpotifyApiConfigured));
                OnPropertyChanged(nameof(SecretMasked));
            }
        }

        // Reveal/hide state (hidden by default on boot) and the masked dots shown when hidden.
        private bool _showClientId;
        public bool ShowClientId { get => _showClientId; set => Set(ref _showClientId, value); }

        private bool _showSecret;
        public bool ShowSecret { get => _showSecret; set => Set(ref _showSecret, value); }

        public string ClientIdMasked => Mask(_spotifyClientId);
        public string SecretMasked => Mask(_spotifySecretId);

        private static string Mask(string value) =>
            string.IsNullOrEmpty(value) ? "" : new string('●', Math.Min(value.Length, 32));

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

        // --- Spotify connection (the Connect button + status dot) ---
        private string _spotifyConnectionStatus = "Not connected";
        public string SpotifyConnectionStatus { get => _spotifyConnectionStatus; set => Set(ref _spotifyConnectionStatus, value); }

        private Brush _spotifyConnectionBrush = GrayBrush;
        public Brush SpotifyConnectionBrush { get => _spotifyConnectionBrush; set => Set(ref _spotifyConnectionBrush, value); }

        private bool _spotifyConnecting;
        public bool SpotifyConnecting
        {
            get => _spotifyConnecting;
            set { if (Set(ref _spotifyConnecting, value)) OnPropertyChanged(nameof(CanConnectSpotify)); }
        }
        public bool CanConnectSpotify => !_spotifyConnecting;

        // Trigger the OAuth flow up front from Configuration, so the token is ready before recording
        // (the engine otherwise only authenticates lazily once a spy session hooks Spotify).
        private async void ConnectSpotify_Click(object sender, RoutedEventArgs e)
        {
            if (_spotifyConnecting) return;
            if (!_userSettings.IsSpotifyAPISet) { SetConnState(Loc.Instance["connEnterCreds"], AmberBrush); return; }

            if (!IsSpotifySelected) IsSpotifySelected = true; // selects Spotify + builds the SpotifyAPI instance
            if (!(ExternalAPI.Instance is EspionSpotify.API.SpotifyAPI))
                SetExternalApi(ExternalAPIType.Spotify, true);
            if (!(ExternalAPI.Instance is EspionSpotify.API.SpotifyAPI))
            {
                SetConnState(Loc.Instance["connInitFailed"], RedBrush);
                return;
            }

            if (ExternalAPI.Instance.IsAuthenticated) { SetConnState(Loc.Instance["connConnected"], GreenBrush); return; }

            SpotifyConnecting = true;
            SetConnState(Loc.Instance["connConnecting"], AmberBrush);
            try { await ExternalAPI.Instance.Authenticate(); } catch { /* the API swallows auth errors too */ }

            for (var i = 0; i < 90 && !ExternalAPI.Instance.IsAuthenticated; i++)
                await Task.Delay(1000);

            SpotifyConnecting = false;
            SetConnState(ExternalAPI.Instance.IsAuthenticated ? Loc.Instance["connConnected"] : Loc.Instance["connFailed"],
                ExternalAPI.Instance.IsAuthenticated ? GreenBrush : RedBrush);
        }

        private void SetConnState(string text, Brush brush)
        {
            SpotifyConnectionStatus = text;
            SpotifyConnectionBrush = brush;
        }

        private void RefreshSpotifyConnState()
        {
            if (_spotifyConnecting) return;
            if (ExternalAPI.Instance is EspionSpotify.API.SpotifyAPI && ExternalAPI.Instance.IsAuthenticated)
                SetConnState(Loc.Instance["connConnected"], GreenBrush);
            else
                SetConnState(Loc.Instance["connNotConnected"], GrayBrush);
        }

        // NavigationView items bind their Content via {l:Tr}, but the pane doesn't always re-evaluate
        // those bindings on a language switch, so force each to re-pull from the resource manager.
        private void RefreshNavLabels()
        {
            if (Nav?.MenuItems == null) return;
            foreach (var obj in Nav.MenuItems)
                if (obj is ModernWpf.Controls.NavigationViewItem item)
                    System.Windows.Data.BindingOperations
                        .GetBindingExpression(item, System.Windows.Controls.ContentControl.ContentProperty)
                        ?.UpdateTarget();
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

        // --- Advanced: record the current Spotify playlist as one album (Spotify API only) ---
        public bool PlaylistAsAlbum
        {
            get => Settings.Default.advanced_playlist_as_album_enabled;
            set => SetToggle(value, v => { Settings.Default.advanced_playlist_as_album_enabled = v; });
        }

        // --- Advanced: custom path templates (opt-in override of the naming/folder toggles) ---
        public bool PathTemplateEnabled
        {
            get => _userSettings.PathTemplateEnabled;
            set => SetToggle(value, v =>
            {
                _userSettings.PathTemplateEnabled = v;
                Settings.Default.advanced_file_path_template_enabled = v;
                OnPropertyChanged(nameof(TemplateFieldsVisible));
                OnPropertyChanged(nameof(ClassicNamingEnabled));
                UpdateTemplatePreview();
            });
        }
        public bool TemplateFieldsVisible => PathTemplateEnabled;
        // The folder/prefix/underscore toggles are overridden by the template, so grey them out when it is on.
        public bool ClassicNamingEnabled => !PathTemplateEnabled;

        public string FolderTemplate
        {
            get => _userSettings.FolderTemplate;
            set
            {
                _userSettings.FolderTemplate = value ?? "";
                Settings.Default.advanced_file_folder_template = _userSettings.FolderTemplate;
                if (!_loading) Settings.Default.Save();
                OnPropertyChanged();
                UpdateTemplatePreview();
            }
        }

        public string FileTemplate
        {
            get => _userSettings.FileTemplate;
            set
            {
                _userSettings.FileTemplate = value ?? "";
                Settings.Default.advanced_file_name_template = _userSettings.FileTemplate;
                if (!_loading) Settings.Default.Save();
                OnPropertyChanged();
                UpdateTemplatePreview();
            }
        }

        private string _templatePreview = "";
        public string TemplatePreview { get => _templatePreview; private set => Set(ref _templatePreview, value); }

        // Renders both templates against a fixed sample track so the user sees the resulting path live.
        private void UpdateTemplatePreview()
        {
            var sample = new Track
            {
                Artist = "Radiohead",
                Title = "15 Step",
                Album = "In Rainbows",
                Year = 2007,
                AlbumPosition = 1,
                Disc = 1,
                AlbumArtists = new[] { "Radiohead" },
                Genres = new[] { "Alternative" }
            };
            var folders = PathTemplate.ResolveFolders(_userSettings.FolderTemplate, sample, _userSettings);
            var file = PathTemplate.ResolveFileName(_userSettings.FileTemplate, sample, _userSettings);
            var ext = SelectedFormat.ToString().ToLower();
            TemplatePreview = FileManager.ConcatPaths(folders, $"{file}.{ext}");
        }

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
            SelectedBitrate = Enum.IsDefined(typeof(Bitrate), Settings.Default.settings_media_bitrate_quality)
                ? (Bitrate)Settings.Default.settings_media_bitrate_quality
                : Bitrate.Kbps320;
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
            RefreshAudioState();

            // metadata api. Client ID / secret are stored encrypted (DPAPI); decrypt for use.
            var storedClientId = Settings.Default.app_spotify_api_client_id;
            var storedSecret = Settings.Default.app_spotify_api_client_secret;
            _userSettings.SpotifyAPIClientId = Crypto.Decrypt(storedClientId)?.Trim();
            _userSettings.SpotifyAPISecretId = Crypto.Decrypt(storedSecret)?.Trim();
            _userSettings.SpotifyAPIRedirectURL = Settings.Default.app_spotify_api_redirect_url?.Trim();
            SpotifyClientId = _userSettings.SpotifyAPIClientId;
            SpotifySecretId = _userSettings.SpotifyAPISecretId;
            SpotifyRedirectUrl = _userSettings.SpotifyAPIRedirectURL;

            // Migrate any legacy plaintext values to encrypted at rest.
            var migrated = false;
            if (!string.IsNullOrEmpty(storedClientId) && !Crypto.IsEncrypted(storedClientId))
            { Settings.Default.app_spotify_api_client_id = Crypto.Encrypt(_userSettings.SpotifyAPIClientId); migrated = true; }
            if (!string.IsNullOrEmpty(storedSecret) && !Crypto.IsEncrypted(storedSecret))
            { Settings.Default.app_spotify_api_client_secret = Crypto.Encrypt(_userSettings.SpotifyAPISecretId); migrated = true; }
            if (migrated) Settings.Default.Save();
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
            _userSettings.PathTemplateEnabled = Settings.Default.advanced_file_path_template_enabled;
            _userSettings.FolderTemplate = Settings.Default.advanced_file_folder_template ?? "";
            _userSettings.FileTemplate = Settings.Default.advanced_file_name_template ?? "";
            RecordingNumber = _userSettings.InternalOrderNumber.ToString(_userSettings.OrderNumberMask);

            _loading = false;
            UpdateTemplatePreview();

            // refresh toggle-backed properties (they read straight from _userSettings/Settings)
            foreach (var p in new[]
            {
                nameof(MuteAds), nameof(MinimizeToTray), nameof(ListenToPlayback), nameof(ForceSkip),
                nameof(RecordEverything), nameof(RecordAds), nameof(RecordAdsVisible), nameof(AddFolders),
                nameof(AddSeparators), nameof(CounterToFilePrefix), nameof(AlbumTrackNumberPrefix),
                nameof(RecordOverRecordings), nameof(DuplicateRecordings), nameof(DuplicateVisible),
                nameof(CounterToMediaTag), nameof(ExtraTitleToSubtitle), nameof(UpdateId3Tags),
                nameof(PlaylistAsAlbum),
                nameof(PathTemplateEnabled), nameof(TemplateFieldsVisible), nameof(ClassicNamingEnabled),
                nameof(FolderTemplate), nameof(FileTemplate), nameof(SpotifyApiConfigured), nameof(SpotifyOptionsVisible)
            }) OnPropertyChanged(p);
        }

        private List<KeyValuePair<Bitrate, string>> BuildBitrates() => new List<KeyValuePair<Bitrate, string>>
        {
            new KeyValuePair<Bitrate, string>(Bitrate.Kbps128, "128 kbps"),
            new KeyValuePair<Bitrate, string>(Bitrate.Kbps160, "160 kbps (Spotify Free)"),
            new KeyValuePair<Bitrate, string>(Bitrate.Kbps256, "256 kbps"),
            new KeyValuePair<Bitrate, string>(Bitrate.Kbps320, "320 kbps (Spotify Premium)"),
            new KeyValuePair<Bitrate, string>(Bitrate.Insane, "320 kbps (Insane, CBR)")
        };

        private void BuildResourceManager()
        {
            var lang = Settings.Default.settings_language.ToLanguageType() ?? LanguageType.en;
            _selectedLanguage = lang;
            Rm = new ResourceManager(Languages.GetResourcesManagerLanguageType(lang));
            Loc.Instance.SetLanguage(lang);
        }

        public List<KeyValuePair<LanguageType, string>> LanguageOptions { get; } =
            Languages.DropdownListValues.ToList();

        private LanguageType _selectedLanguage;
        public LanguageType SelectedLanguage
        {
            get => _selectedLanguage;
            set
            {
                if (!Set(ref _selectedLanguage, value) || _loading) return;
                Settings.Default.settings_language = value.ToString();
                Settings.Default.Save();
                Rm = new ResourceManager(Languages.GetResourcesManagerLanguageType(value));
                Loc.Instance.SetLanguage(value);
                // these are set imperatively (not {l:Tr} bindings), so re-localize them on language change
                RefreshSpotifyConnState();
                OnPropertyChanged(nameof(StartStopLabel));
                RefreshNavLabels();
            }
        }

        #endregion Load

        #region Recording control

        private void ToggleRecording()
        {
            if (!Watcher.Running)
            {
                if (!Directory.Exists(_userSettings.OutputPath))
                {
                    MessageBox.Show(this, Loc.Instance["msgOutputNotFound"] + "\n" + _userSettings.OutputPath,
                        "Spytify+", MessageBoxButton.OK, MessageBoxImage.Warning);
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

        // Engine pushes the current cover URL on every track tick; reload only when it actually
        // changes so per-second calls are a cheap no-op. The bitmap downloads asynchronously and
        // decodes small (card art is tiny). On failure we drop back to the placeholder and clear the
        // cached URL so a later tick can retry.
        private string _currentArtUrl;
        public void UpdatePlayingArt(string url) => RunOnUi(() =>
        {
            if (string.Equals(url, _currentArtUrl, StringComparison.Ordinal)) return;
            _currentArtUrl = url;

            if (string.IsNullOrWhiteSpace(url)) { AlbumArt = null; return; }

            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.DecodePixelWidth = 160;
                bmp.UriSource = new Uri(url, UriKind.Absolute);
                bmp.EndInit();
                bmp.DownloadFailed += (s, e) =>
                {
                    if (ReferenceEquals(AlbumArt, bmp)) AlbumArt = null;
                    if (string.Equals(_currentArtUrl, url, StringComparison.Ordinal)) _currentArtUrl = null;
                };
                AlbumArt = bmp;
            }
            catch { AlbumArt = null; _currentArtUrl = null; }
        });

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
            MessageBox.Show(this, Loc.Instance["msgSpotifyFallback"],
                "Spytify+", MessageBoxButton.OK, MessageBoxImage.Information));

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
            RefreshAudioState();
        });

        // Volume changed externally (Windows mixer / notification): reflect it on the slider
        // without re-applying it back to the device.
        public void SetSoundVolume(int volume) => RunOnUi(() =>
        {
            _suppressVolumeApply = true;
            Volume = volume;
            _suppressVolumeApply = false;
        });

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

        private static readonly Duration PanelFade = new Duration(TimeSpan.FromMilliseconds(90));
        private static readonly Duration PanelSlide = new Duration(TimeSpan.FromMilliseconds(110));
        private const double SlideOffset = 8.0;

        private static IEasingFunction EaseOut() => new CubicEase { EasingMode = EasingMode.EaseOut };

        private string _activeTag;

        // ItemInvoked fires immediately on click, decoupled from the NavigationView's
        // selection-indicator animation, so the content switches without waiting on the pane.
        private void Nav_ItemInvoked(ModernWpf.Controls.NavigationView sender,
            ModernWpf.Controls.NavigationViewItemInvokedEventArgs args)
        {
            var tag = (args.InvokedItemContainer as ModernWpf.Controls.NavigationViewItem)?.Tag as string;
            if (tag != null) ApplySection(tag);
        }

        // SelectionChanged covers keyboard and programmatic selection (e.g. switching to the
        // Record tab when recording starts). The _activeTag guard makes it idempotent, so a
        // click that raises both ItemInvoked and SelectionChanged only animates once.
        private void Nav_SelectionChanged(ModernWpf.Controls.NavigationView sender,
            ModernWpf.Controls.NavigationViewSelectionChangedEventArgs args)
        {
            var tag = (args.SelectedItem as ModernWpf.Controls.NavigationViewItem)?.Tag as string ?? "record";
            ApplySection(tag);
        }

        private void ApplySection(string tag)
        {
            if (tag == _activeTag) return;
            _activeTag = tag;
            SetPanelActive(RecordPanel, tag == "record");
            SetPanelActive(SettingsPanel, tag == "settings");
            SetPanelActive(AdvancedPanel, tag == "advanced");
            SetPanelActive(AnalyzePanel, tag == "analyze");
            if (tag == "settings") RefreshSpotifyConnState();
        }

        private static TranslateTransform SlideOf(FrameworkElement el)
        {
            if (!(el.RenderTransform is TranslateTransform tt))
            {
                tt = new TranslateTransform();
                el.RenderTransform = tt;
            }
            return tt;
        }

        // Fade + slide with no explicit From and a persistent transform. Inactive panels are
        // parked at opacity 0 / Y = SlideOffset (their animations released first), so the
        // incoming panel always animates from its real current opacity/Y up to 1 / 0.
        // Interrupting mid-transition just continues from where it is, never snaps, however
        // fast you click between sections.
        private void SetPanelActive(FrameworkElement el, bool active)
        {
            if (el == null) return;
            var slide = SlideOf(el);
            if (active)
            {
                el.Visibility = Visibility.Visible;
                el.BeginAnimation(OpacityProperty, new DoubleAnimation(1, PanelFade) { EasingFunction = EaseOut() });
                slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, PanelSlide) { EasingFunction = EaseOut() });
            }
            else
            {
                el.BeginAnimation(OpacityProperty, null); // release held animated values...
                slide.BeginAnimation(TranslateTransform.YProperty, null);
                el.Opacity = 0;                            // ...then park at the entrance start
                slide.Y = SlideOffset;
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
