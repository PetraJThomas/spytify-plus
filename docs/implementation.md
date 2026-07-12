# Spytify+ — Implementation

## Solution layout

```
EspionSpotify/            engine (library, non-SDK csproj + packages.config, net48)
EspionSpotify.Wpf/        WPF front-end (SDK-style csproj, net48, AssemblyName=Spytify)
EspionSpotify.Tests/      xUnit test suite (325 tests)
psd/new_assets/           Spytify+ brand source (.ai, SVG, @4x PNG)
docs/                     this documentation
```

The WPF project references the engine project. `AssemblyName` is `Spytify`
(the exe stays `Spytify.exe`); `Product`/`AssemblyTitle` are `Spytify+`.

## Engine (`EspionSpotify/`)

### Background encode queue
- `IEncodeService` / `EncodeService.cs` / `Models/EncodeJob.cs`. The recorder
  captures to a temp WAV, then **hands the WAV to the encode service and returns
  immediately** (`Recorder.cs` `RecordingStopped` → `_encodeService.Enqueue`).
  The service owns the temp file from there: encode off the recording path, move
  to final destination, tag, clean up. This is what stops fast track changes from
  dropping recordings.
- Ownership transfer is explicit (`_tempOriginalFile = null` after enqueue) so the
  recorder never deletes a WAV the service now owns.

### Unified FFmpeg encode
- All formats record to a temp WAV first, then FFmpeg encodes. MP3 went from
  NAudio.Lame to FFmpeg `libmp3lame` (ABR 128/160/256/320; "Insane" = constant
  320 CBR). The native `libmp3lame.32/64.dll` and the `LAMEPreset` enum were
  removed in favour of a native `Bitrate` enum. FFmpeg handles arbitrary sample
  rate / channel count, so the old LAME resampler/channel-reducer and the
  LAME-based `TestFileWriter` validation (which wrongly rejected some multichannel
  devices) are gone.

### Other engine changes
- Retargeted all projects 4.6.1 → **4.8**.
- Cover art: fetch the largest ≤640px image (Spotify 640, Last.fm extralarge)
  instead of capping at 300px.
- Default output path: `<Music>\Spytify`, created on first run.
- `IFrmEspionSpotify` is the engine↔UI callback boundary; it was decoupled from
  the concrete WinForms form so WPF can implement it.
- `Translations/en.resx` + `fr.resx` are the shared string tables;
  `Enums/TranslationKeys.cs` is the canonical key list (see learnings).

## WPF front-end (`EspionSpotify.Wpf/`)

### Shell
- `MainWindow.xaml` — a ModernWpf `NavigationView` with four sections (Tag-based):
  **record**, **settings** (Configuration), **advanced**, **analyze**. Section
  panels are sibling Grids toggled with a snap-proof fade/slide transition driven
  from `ItemInvoked` (immediate) with an `_activeTag` guard.
- `MainWindow.xaml.cs` — the bulk of the view-model-ish code-behind: implements
  `IFrmEspionSpotify`, exposes bindable properties (recording state, settings
  toggles, device/volume, counter/timer, Spotify connection), commands
  (`RelayCommand.cs`), tray (`NotifyIcon`), language switching.
- `App.xaml` — global styles: rounded buttons with a hover-darken overlay,
  ComboBox/ToggleSwitch/CheckBox/RadioButton styling, `ControlCornerRadius=6`.

### Analyze feature (`Analysis/` + `MainWindow.Analyze.cs`)
- `FfmpegDecoder.cs` — probes with `ffmpeg -i` (parses stderr for codec / sample
  rate / bitrate / duration), decodes to mono `float[]` via `-f f32le` (no
  ffprobe; 15-min cap). `AudioSample.cs` is the DTO.
- `QualityAnalyzer.cs` — Hann-windowed FFT (NAudio), averaged magnitude spectrum,
  cut-off detection, and tiering. **Cut-off = a local brick-wall** (`DetectCutoff`):
  slide a narrow window down the spectrum and take the highest edge whose level
  drops ≥ `DeadDepthDb` (in-band mean below vs plateau mean over the ~2 kHz above)
  into a dead plateau; full-band (Nyquist) when none. Local on purpose — a global
  steepness measure is defeated by the rising quantisation-noise floor lossy files
  leave near Nyquist. The line is then placed at the cliff edge by a sustained-drop
  scan (noise-bin proof). **The codec is authoritative** for lossy vs lossless (an
  AAC/Opus that reaches Nyquist is still lossy); the spectrum only describes
  bandwidth. Emits localized tier/verdict/detail via `Loc.Instance` + format
  strings; `QualityResult.cs` is the DTO and carries a `Diagnostics` string surfaced
  by the `AnalyzeShowDiagnostics` calibration flag.
- `WaveformPeaks.cs` — min/max buckets for the amplitude waveform.
- `Spectrogram.cs` — 2D heatmap (`WriteableBitmap`) with palette selection
  (Inferno/Magma/Viridis/Heat) and a cut-off overlay line.
- `MainWindow.Analyze.cs` — drag/drop + file-picker handlers, async decode/analyze
  off the UI thread, and all the canvas/axis rendering (re-rendered on resize).

### Localization
- `Loc.cs` — `Loc.Instance["key"]` string indexer backed by the engine's
  `ResourceManager`; raises `PropertyChanged("Item[]")` on language change so
  bindings refresh live.
- `TrExtension.cs` — the `{l:Tr key}` XAML markup extension (a one-way binding to
  `Loc.Instance[key]`).
- Code-behind strings use `Loc.Instance["key"]` directly; format strings use
  `string.Format(Loc.Instance["key"], args)`.

### Security
- `Crypto.cs` — DPAPI (CurrentUser) encryption for the Spotify Client ID/Secret;
  stored encrypted in user.config, decrypted only in memory. Legacy plaintext is
  migrated on first load. Eye toggles reveal/mask each field.

### Branding
- `Assets/spytify.ico` — multi-resolution (16–256px, PNG-compressed) icon
  generated from the square logo; used for exe icon, `Window.Icon`, and the tray
  (which extracts the exe icon). `ui:TitleBar.IsIconVisible="True"` puts it in the
  title bar.
- The two in-app logos (footer wordmark + collapsed-pane icon) are **vector**
  `DrawingImage` resources in `MainWindow.xaml` (converted from the SVG: SVG path
  `d` data drops straight into WPF `Geometry`). The raster PNGs were removed.

## Offline-library suite (Phase 10)

Every feature here is opt-in (off by default), gated by a new `UserSettings` flag
persisted across three files (`Settings.settings`, `Settings.Designer.cs`,
`App.config`) plus a WPF toggle, and **never alters the captured audio**.

### Naming templates (`Native/PathTemplate.cs`)
- `ResolveFolders` / `ResolveFileName` expand `{token}` templates (artist,
  albumartist, album, title, titlefull, year, track, track2, disc, genre, counter),
  sanitising each path segment and dropping empty ones. `FileManager` uses them for
  the output path and the "already recorded" check when `PathTemplateEnabled` and the
  track is normal (ads/unknown keep the classic path). The WPF tag builder
  (`TemplateTags` in `MainWindow.xaml.cs`) is a click-to-insert legend that writes
  into the last-focused template box.

### Playlist-as-album (`API/SpotifyAPI.cs`)
- `UpdateTrack` reads `playback.Context`; when it's a playlist and the toggle is on,
  `GetPlaylistIdFromContext` + a cached `GetPlaylistAsync` fetch the name + cover, and
  `MapSpotifyPlaylistToTrack` overrides Album/AlbumArtists(=Various Artists)/cover/
  position (playback-order counter). Uses the `PlaylistReadPrivate` scope.

### Post-record steps (`EncodeService.cs`, after tagging)
- `IsTruncatedCapture` (pure, testable): drop captures < 80% of `Track.Length`.
- `SaveCoverFileAsync`: write `cover.jpg` once per album folder (grouped-only).
- `AppendToPlaylist` / `BuildM3uEntry`: append an Extended-M3U entry to a per-folder
  `.m3u8`.
- `QueueQualityAnalysis` (via `IFrmEspionSpotify`): the WPF layer runs the Analyze
  analyzer on the finished file and logs the verdict (flag-only).
- `BuildFfmetadataContent` (extracted, testable): FLAC/OPUS tags now honour the
  extra-title/counter toggles and write ISRC + `SPOTIFY_TRACK_ID`/`SPOTIFY_ALBUM_ID`.

### Extended tags (`API/MapperID3.cs`)
- MP3/WAV/re-tag path: `tags.ISRC` for ISRC; `WriteCustomIdTags` writes the Spotify
  IDs as ID3 `TXXX` frames and Vorbis (`XiphComment`) custom fields.

### Player card
- `MainWindow.xaml` Record card + `UpdatePlayingArt` on `IFrmEspionSpotify`: a
  bindable `AlbumArt` `ImageSource` (decoded small, deduped by URL). The engine
  (`Watcher`) pushes `track.AlbumArtUrl` each 1s tick so the delayed API fill appears.
