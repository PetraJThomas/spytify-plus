# Spytify+ — Completed

Checklist of what's done on `spytify-plus`. All items are committed and the
solution builds; the 290-test suite passes.

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

## Verification
- Build: VS MSBuild, Release. Engine = packages.config; WPF = SDK-style.
- Tests: **290/290** (xUnit). `TranslationTests` enforces resx ⇄ enum parity.
- Manual GUI checks (user-run): Analyze classification across FLAC/WAV/320/256/128
  MP3 + MP3-sourced FLAC; live language switch; title-bar icon + crisp vector
  footer; button icons render.

## Remaining / future
- [ ] Confirm in-app (GUI) that the title-bar icon renders and the vector footer
      reads crisp at all window scales (built clean; not visually verified here).
- [ ] Optional: filled-glyph variants if a heavier icon weight is wanted (Segoe
      MDL2 is single-weight; `FontWeight` is a no-op).
- [ ] Optional: move analysis into the engine for headless reuse.
- [ ] Optional: localize the spectrogram palette names if ever desired (currently
      literal because the selection code matches on them).
- [ ] PR/merge `spytify-plus` → `master`.
