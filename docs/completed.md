# Spytify+ — Completed

Checklist of what's done on `spytify-plus`. All items are committed and the
solution builds; the **349-test** suite passes. Shipped as **v2.1.0** (2026-07-13).

## Engine
- [x] Retarget all projects to .NET 4.8
- [x] Background encode queue (no more dropped recordings on fast track changes)
- [x] Unified FFmpeg encode pipeline (MP3 via libmp3lame; removed NAudio.Lame +
      native DLLs + LAMEPreset + LAME resampler/validation)
- [x] Native `Bitrate` enum replacing `LAMEPreset`
- [x] Full-resolution cover art (≤640px)
- [x] Default output path `<Music>\Spytify` (created on first run)
- [x] `IFrmEspionSpotify` decoupled from the concrete WinForms form

## WPF front-end
- [x] ModernWpf (Fluent) shell with NavigationView; 4 sections (Record, Config,
      Advanced, Analyze)
- [x] Snap-proof section transitions (ItemInvoked-driven, `_activeTag` guard)
- [x] WPF is the startup `Spytify.exe`; WinForms UI fully retired/deleted
- [x] System tray + minimize-to-tray
- [x] Selectable console (RichTextBox) + Copy log
- [x] Device-volume slider; virtual-cable driver install/reinstall
- [x] Auto-stop timer + counter/numbering control (Ctrl±click pad width)
- [x] DPAPI-encrypted Spotify Client ID/Secret + show/hide eye toggles
- [x] "Connect to Spotify" upfront OAuth button; OAuth runtime deps redeployed
- [x] Media formats shown cased (MP3/WAV/Opus/FLAC)

## Analyze tab
- [x] Decode via bundled ffmpeg (no ffprobe); 15-min cap; mono float decode
- [x] Amplitude waveform
- [x] Averaged frequency response with detected cut-off line
- [x] Full 2D spectrogram + palette selector (Inferno/Magma/Viridis/Heat)
- [x] Verdict badge (true lossless / lossy-in-lossless-container / lossy + bitrate)
- [x] Codec authoritative for lossy/lossless (AAC false-positive fixed)
- [x] Exact audio-stream bitrate; "extends to" vs "cut-off" near Nyquist
- [x] Local brick-wall cut-off detection (steep, deep drop into a dead plateau);
      immune to the near-Nyquist noise floor that defeats a global measure. Fixed a
      soft-track false transcode and a 128k-MP3 missed cut; validated on ~20 MP3s
- [x] Cut-off line seeks the cliff edge via a sustained-drop scan (noise-bin proof)
- [x] Flag-gated detection diagnostic readout (`AnalyzeShowDiagnostics`, off)
- [x] Tier-reactive pulse, lossless glow-streak border, equalizer loader,
      drag-over overlay, "How to read" info modal
- [x] Busy loader subtitle cross-fades through real pipeline stages (decode →
      passes → spectrogram), darker-green/larger
- [x] "Analysis Verdict" header on the verdict card; consistent card spacing

## Localization (en + fr)
- [x] Entire UI localized: nav, settings, Connect statuses, Record/log/output,
      the full Analyze tab (incl. info-modal prose), verdict/detail format
      strings, error messages, MessageBoxes
- [x] en/fr in sync — 171 real keys
- [x] All keys mirrored in `TranslationKeys` enum; tab-record test updated
- [x] Live language switch (incl. forced refresh for nav items + imperative
      strings); French prose self-reviewed
- [x] Distinctly-French where identical words confused (Paramètres; Identifiant
      client / Secret client)

## UX polish
- [x] Visual hierarchy (header > label > control) across Configuration + Analyze
- [x] Uniform label colour `#D8D8D8`; colons removed from headers/labels
- [x] Row checkboxes (label left, check right); long-label clipping fixed
- [x] Consistent control heights (32) / font (14); button heights matched
- [x] Card whitespace pass; Connect row set apart; General card reordered
- [x] Rounded buttons, hover-darken fade; Start/Stop expand animation removed

## Rebrand → Spytify+
- [x] New brand assets in `psd/new_assets` (square eye + "Spytify +" wordmark)
- [x] Multi-resolution `.ico` generated from the square logo
- [x] Display name "Spytify+" (title, tray, MessageBox titles, Product/Title);
      exe stays `Spytify.exe`
- [x] Title-bar icon (`ui:TitleBar.IsIconVisible`)
- [x] Footer logos as vector `DrawingImage` (from SVG); supersampled PNGs removed
- [x] Left-aligned icons on all action buttons (`BtnIcon` style; state-driven
      Start/Stop record-dot/stop-square); footer icons sized 13

## Offline library & metadata (Phase 10, 2026-07-12)
- [x] Mini Spotify-style player card with live album art (`UpdatePlayingArt`; art
      pushed each 1s tick to catch the delayed API fill)
- [x] FLAC/OPUS honour the tag toggles (extra-title-as-subtitle, counter-as-track,
      re-tag on replay) — `BuildFfmetadataContent` + a TagLib re-tag path
- [x] Advanced tab consolidated into a "Song Metadata & Organisation" card
- [x] Custom filename/folder templates (opt-in override) + click-to-insert tag
      builder; engine `Native/PathTemplate.cs`
- [x] Record the current playlist as one album (Spotify API: playback context →
      playlist name + cover; Various Artists; `PlaylistReadPrivate` scope)
- [x] Discard truncated recordings (`IsTruncatedCapture`, 80% of track length);
      `Track.Length` populated from the Spotify API too
- [x] Auto quality-analysis of each recording (flag-only log via `QueueQualityAnalysis`)
- [x] Save cover.jpg per album folder
- [x] ISRC + Spotify track/album ID tags (ISRC standard; Spotify IDs as ID3 TXXX +
      Vorbis custom fields)
- [x] Export an .m3u playlist per folder (`BuildM3uEntry`)
- [x] LICENSE Spytify+ copyright line; README rewritten for the fork + screenshots

## Library integrity, hardening & release (Phase 11, 2026-07-13)
- [x] **Check Library** tab, `Analyse Library`: parallel spectral scan of the whole
      output folder for lossless containers that are actually lossy inside; auto-saved
      HTML report (bilingual en/fr); click a finding to open it in Analyze
- [x] **Update Library Metadata**: direct sweep refreshing tags + cover art from Spotify
      + iTunes, keyed exactly by embedded ISRC with a Spotify track-ID fallback (Spotify's
      `isrc:` search doesn't index every track); parallel, 429-safe, lists no-match files
- [x] Spotify API honours HTTP 429 (auto-retry, `TooManyRequestsConsumesARetry=false`);
      artist-genre + album caches made concurrent; the 1s tag-write delay is skipped for sweeps
- [x] `Folder.jpg` written next to `cover.jpg` (Windows shell/Media Player); overwrite
      hidden WMP art files (clear Hidden/System/ReadOnly before write)
- [x] High-res covers via iTunes with artist+album match validation (no wrong-album art)
- [x] Genre falls back to the artist's Spotify genres when the album has none
- [x] Skip-scrub maintenance: re-tag + refresh covers in place; re-record truncated
      existing files (verify-length now applies in skip mode); "Updated metadata" log
- [x] Metadata-fetch gate made format-agnostic (was MP3-only) so FLAC skip-retag resolves
- [x] Settings collapsed to one canonical file: WPF `AssemblyVersion` pinned at 2.0.0.0
      (that segment is the settings path); removed the `Upgrade()` migration; fixed the
      `UpdateId3Tags` toggle never being read back on load
- [x] Large playlist-albums: `{trackpad}` dynamic zero-padding (100→`001`..`100`) +
      natural-sort m3u (`NaturalFileNameComparer`)
- [x] Shipped **v2.1.0**: engine `AssemblyVersion` 2.1.0.0 (updater compares it), WPF
      `FileVersion` 2.1.0.0, WPF `AssemblyVersion` pinned. Release zip = `net48/` +
      `Updater/` subfolder, forward-slash entries

## Verification
- Build: VS MSBuild, Release. Engine = packages.config; WPF = SDK-style.
- Tests: **349/349** (xUnit). `TranslationTests` enforces resx ⇄ enum parity.
- Manual GUI checks (user-run): Analyze classification across FLAC/WAV/320/256/128
  MP3 + MP3-sourced FLAC; live language switch; title-bar icon + crisp vector
  footer; button icons render.

## Remaining / future

### Shipped (was pre-ship)
- [x] Auto-updater repointed to `PetraJThomas/spytify-plus` (`EspionSpotify/GitHub.cs`
      + `EspionSpotify.Updater/Utilities/GitHub.cs`).
- [x] GitHub repo created (public); version scheme settled (engine AsmVersion = release
      version; WPF AsmVersion pinned; tag `v2.1.0`). Release cut manually via `gh` (see
      `spytify-release-process` memory) rather than a workflow.
- [x] Working branch is `main` (canonical default).

### Optional / polish
- [x] Playlist-as-album uses the true playlist index (not playback order), and pads it
      dynamically for large playlists.
- [ ] Quality-analysis gating is flag-only — could move/reject suspect files or tag
      the detected quality.
- [ ] Verify-length threshold (80%) is a code constant — could be a slider.
- [ ] MP3/WAV Spotify-ID tag path (TXXX) is wired but only FLAC was round-trip
      verified live.
- [ ] Move the analyzer into the engine for headless reuse (currently the WPF layer
      runs it via the `QueueQualityAnalysis` callback).
- [ ] Custom `SettingsProvider` to key settings on a fixed name instead of pinning
      `AssemblyVersion` (fully identity-independent; not urgent).
- [ ] Localize the Check Library UI strings' non-`clb` bits + spectrogram palette names.
