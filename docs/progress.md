# Spytify+ — Progress log

Branch: `spytify-plus` (off `master`). ~106 commits 2026-06-16 → 2026-06-19,
plus the offline-library suite (Phase 10) on 2026-07-12.
This is a phase-ordered narrative; see `git log master..HEAD` for the full list.

## Phase 1 — Engine modernization
- Retargeted all projects 4.6.1 → **.NET 4.8**.
- **Background encode queue** (`IEncodeService`/`EncodeService`/`EncodeJob`): the
  recorder hands off the temp WAV and returns, so fast track changes stop dropping
  recordings.
- **Unified FFmpeg encoding**: MP3 moved from NAudio.Lame to FFmpeg `libmp3lame`
  (ABR 128/160/256/320; Insane = 320 CBR). Removed the native LAME DLLs,
  `LAMEPreset`, the LAME resampler/channel-reducer, and the LAME `TestFileWriter`.
- Full-resolution cover art (≤640px). Default output `<Music>\Spytify`.

## Phase 2 — WinForms → WPF
- Scaffolded a ModernWpf (Fluent) shell on net48 alongside the engine; wired the
  3-section shell to the engine via `IFrmEspionSpotify` (decoupled from the
  concrete form).
- Made the WPF app the startup `Spytify.exe`; engine became a library.
- **Retired the WinForms UI entirely** (deleted `frmEspionSpotify.*`, dropped
  MetroFramework, removed orphaned WinForms-only code).
- Spotify-green accent, logo in the pane footer (cross-fades to icon when the rail
  collapses), snap-proof nav transitions, selectable console (RichTextBox) + Copy
  log, system tray + minimize-to-tray.
- DPAPI encryption for Spotify Client ID/Secret with show/hide eye toggles.
- Device-volume slider, virtual-cable driver install/reinstall, auto-stop timer,
  counter/numbering control.
- Fixed: startup crash (App.config userSettings section), collapsed-rail icon not
  rendering (ico wasn't a Resource), masked-credential TwoWay binding crash.

## Phase 3 — Analyze tab (Spek-lite quality viewer)
- Drag-drop / file-picker audio quality viewer: decode via bundled ffmpeg (no
  ffprobe), Hann-windowed FFT (NAudio), cut-off detection, tiering.
- Amplitude **waveform**, averaged **frequency response** with cut-off line, full
  2D **spectrogram** (palette selector: Inferno/Magma/Viridis/Heat), and a
  plain-language **verdict** badge.
- **Codec is authoritative** for lossy/lossless (fixed an AAC false positive);
  exact audio-stream bitrate via ffmpeg; "extends to" near Nyquist vs "cut-off".
- Polish: tier-reactive pulse, lossless gets a circulating glow-streak border,
  custom equalizer loader, drag-over overlay, "How to read the analysis" info
  modal, cut-off marker with halo, entrance/cross-fade animations.

## Phase 4 — Connect to Spotify + nav rename
- Added an upfront "Connect to Spotify" (OAuth) button to Configuration.
- Fixed Spotify auth: redeployed the OAuth runtime deps (EmbedIO + System.*
  facades + binding redirects) the entry-exe change had dropped.
- Renamed nav tabs (Spy/Record, Configuration); verdict badge became a pill chip.

## Phase 5 — UX polish pass
- Configuration visual hierarchy (header anchors > label > value); consistent
  rounded buttons with theme-aware hover/press fade (darken on hover); modernized
  combos/switches/checkboxes; eye toggles match the button style.
- Uniform label colour `#D8D8D8` across fields/toggles/checks/radios; checkboxes
  as full-width rows (label left, check right); fixed long-label clipping (also
  helped French); text boxes & combos share height 32 / font 14; settings buttons
  match that height.
- Whitespace: more vertical air in cards; the Connect row set apart; removed
  trailing colons from all headers and field labels; General card reordered
  (toggles first, Language last); "Choose Language" label.

## Phase 6 — Full localization (en + fr)
- Localized in committed passes: nav tabs + settings literals + Connect statuses;
  the entire Analyze tab (incl. info-modal prose + cheat-sheet); verdict/detail
  format strings + errors; Record/log/output literals; two remaining MessageBoxes.
- Synced the `TranslationKeys` enum with the 67+ new keys; updated the tab-record
  test. **290/290 tests pass.** Self-reviewed and polished the French prose.
- Fixed missed all-caps headers + Start/Stop label; language-switch refresh for
  imperatively-set strings; "Configuration" → "Paramètres", Client ID/Secret →
  "Identifiant client"/"Secret client".

## Phase 7 — Analyze layout refinements
- Spectrogram header-row hierarchy; "Analysis Verdict" header on the verdict card;
  breathing room under the cut-off & spectrogram graph headers; removed the
  Start/Stop hover expand; tuned the verdict header-to-badge gap (→ 6px).

## Phase 8 — Spytify+ rebrand
- New brand assets (square eye logo + "Spytify +" wordmark, from `psd/new_assets`).
- Generated a multi-resolution `.ico`; renamed display name to **Spytify+** (title,
  tray, MessageBox titles, Product/AssemblyTitle); icon in the title bar.
- **Footer logos converted to vector** (`DrawingImage` from SVG); dropped the
  supersampled PNGs.
- **Left-aligned icons on all action buttons** (shared `BtnIcon` style; Start/Stop
  uses state-driven record-dot/stop-square); footer icons scaled to 13.

## Phase 9 — Analyze cut-off detection rework
- Replaced the single −60 dB floor-crossing cut-off with **local brick-wall
  detection**: slide a narrow window down the spectrum and take the highest edge
  whose level drops ≥ 25 dB (in-band over the ~1.5 kHz below vs plateau over the
  ~2 kHz above) into a dead plateau. Fixed two real misreads found by user testing:
  a soft RnB ballad (gentle roll-off) wrongly flagged as a transcode, and a 128k MP3
  with an obvious 14 kHz cliff wrongly called full-band (its near-Nyquist
  quantisation-noise floor had defeated a global steepness measure).
- Cut-off **line placement** now seeks the cliff edge by scanning up from in-band to
  the first *sustained* drop below the cliff mid-point, so faint noise-floor bins in
  the plateau can't drag the marker upward.
- Added a flag-gated **diagnostic readout** (`AnalyzeShowDiagnostics`) that surfaces
  the raw detection metrics under the verdict for calibration; off for normal use.
- Validated against ~20 assorted MP3s plus lossless/transcode cases; the residual
  ambiguous zone (airy lossless vs noisy-floor MP3) is accepted as unsolvable from
  spectrum and backstopped by the codec-authoritative verdict.
- **Busy-loader subtitle** now cross-fades through the genuine pipeline stages
  (Decoding → Running quality passes → Building spectrogram), darker readable green
  at a larger size; three new resx keys (`anzPhaseDecode/Passes/Spectro`, en + fr,
  mirrored in the enum).

## Phase 10 — Offline-library & metadata suite (2026-07-12)
Guiding philosophy from the user: **pure capture** — the recorded audio faithfully
mirrors Spotify's output (no upsampling, no forced CBR, no loudness processing).

- **Mini player card**: the Record screen's now-playing strip became a Spotify-style
  card with the current track's **live album art** (new `IFrmEspionSpotify.Update
  PlayingArt`; art pushed each 1s tick because the API fills the URL ~1s after the
  track change).
- **FLAC/OPUS tag parity**: the "extra title as subtitle", "counter as track number",
  and "re-tag on replay" toggles now apply to FLAC/OPUS (ffmetadata + a TagLib re-tag
  path), not just MP3/WAV. Extracted `EncodeService.BuildFfmetadataContent`.
- **Advanced tab reorg**: file/folder + tag settings consolidated into one **"Song
  Metadata & Organisation"** card (Metadata tags + Files & folders sub-groups); the
  Recorder card kept only the counter + timer.
- **Custom filename/folder templates** (opt-in override) with a click-to-insert **tag
  builder** legend. Engine: `Native/PathTemplate.cs`. Tokens: artist, albumartist,
  album, title, titlefull, year, track, track2, disc, genre, counter.
- **Record the current playlist as one album** (Spotify API): reads the playback
  context, fetches the playlist name + cover, tags the set as a "Various Artists"
  compilation. Added the `PlaylistReadPrivate` scope.
- **Recording verification**: discard captures cut clearly short of the track length
  (`EncodeService.IsTruncatedCapture`, 80% threshold). Populated `Track.Length` from
  the Spotify API too (Last.fm already did).
- **Auto quality-analysis**: after each recording, run the Analyze-tab analyzer and
  log the real quality (flag-only, non-destructive; via `QueueQualityAnalysis`).
- **cover.jpg per album folder** + **ISRC / Spotify track-album ID tags** (ISRC via
  TagLib/ffmetadata; Spotify IDs as ID3 TXXX + Vorbis custom fields).
- **.m3u playlist export** per folder (mirrors a recorded playlist; purity-safe).
- Shipping prep: LICENSE gained a Spytify+ copyright line; README rewritten for the
  fork with current screenshots.

## Phase 11 — Library integrity, hardening & the v2.1.0 release (2026-07-13)
- **Check Library** tab, two library-wide tools:
  - *Analyse Library*: parallel spectral scan flagging lossless files that are actually
    lossy inside (a lossy source re-wrapped as FLAC/WAV). Auto-saves a bilingual HTML
    report; a finding row click opens the file in Analyze.
  - *Update Library Metadata*: a direct sweep refreshing tags + cover art from Spotify +
    iTunes, keyed exactly by embedded ISRC with a **Spotify track-ID fallback** (Spotify's
    `isrc:` search doesn't index every track, e.g. DIY-distributor releases). Parallel,
    429-safe, lists the files it couldn't match.
- **Rate-limit resilience**: enabled `SpotifyWebAPI.UseAutoRetry` with
  `TooManyRequestsConsumesARetry=false`, so 429s wait the `Retry-After` and retry rather
  than being silently counted as "no match". Made the artist-genre + album caches
  concurrent so the sweep can parallelize; skip the 1s `SaveMediaTags` delay for sweeps.
- **Cover art on Windows**: write `Folder.jpg` next to `cover.jpg`; clear Hidden/System/
  ReadOnly before writing (Windows Media Player leaves a hidden `Folder.jpg` that
  `File.WriteAllBytes` can't overwrite). High-res via iTunes now validates artist **and**
  album so a fuzzy match can't key wrong-album art.
- **Metadata**: genre falls back to the artist's Spotify genres; the metadata-fetch gate
  is now format-agnostic (was MP3-only) so FLAC skip-retag matches; skip-scrub re-tags +
  refreshes covers in place and re-records truncated existing files (verify-length in
  skip mode); a "Updated metadata" console line confirms it.
- **Settings**: collapsed to one canonical `user.config`. The .NET settings path embeds
  the entry AssemblyVersion, so the WPF `AssemblyVersion` is **pinned at 2.0.0.0**; the
  `Upgrade()` migration was removed; fixed the `UpdateId3Tags` toggle never being read on
  load. `FileVersion` set explicitly (else it inherits the pin).
- **Large playlist-albums**: `{trackpad}` token zero-pads the number to the album's total
  width (100 → `001`..`100`), plus a natural-sort `.m3u` (`NaturalFileNameComparer`).
- **Released v2.1.0** to GitHub: engine `AssemblyVersion` 2.1.0.0 (updater compares it),
  WPF `FileVersion` 2.1.0.0. Release zip = `net48/` contents + the `Updater/` subfolder,
  built with forward-slash entries (.NET Framework's zipper defaults to backslashes). See
  the `spytify-release-process` memory.

## Current state
All of the above is committed and building; **349/349 tests pass**. **v2.1.0 shipped**
(auto-updater repointed to `PetraJThomas/spytify-plus`). Open items are in `completed.md`
under "Remaining / future".
