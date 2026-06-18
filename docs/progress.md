# Spytify+ — Progress log

Branch: `spytify-plus` (off `master`). ~106 commits, 2026-06-16 → 2026-06-19.
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

## Current state
All of the above is committed and building; 290/290 tests pass. Open items are in
`completed.md` under "Remaining / future".
