# Spytify+ — Specification

## What this is

Spytify+ is a modernized fork of Spytify (the open-source Spotify loopback
recorder). The `spytify-plus` branch takes the original WinForms app and turns it
into a polished WPF (Fluent / ModernWpf) application with a new in-app audio
**quality analyzer**, a hardened recording/encoding engine, full English +
French localization, and a "Spytify+" rebrand.

The original tool's recurring pain point: people record from Spotify's loopback,
get a `.flac`, and assume it's true lossless — when the loopback is quality-capped
(320 kbps, or bit-perfect CD only with Spotify Lossless + a 44.1 kHz virtual
cable). The headline new feature, the **Analyze** tab, exists to make that
audible/visible: drop any audio file and see its real quality.

A later pass (Phase 10) added an **offline-library suite** on top: a live album-art
player card, custom filename/folder templates with a tag builder, "record a Spotify
playlist as one album", recording-length verification, auto quality-analysis of each
capture, cover.jpg + ISRC/Spotify-ID tags, and .m3u playlist export. Guiding rule:
**pure capture** — never resample, transcode, or loudness-process the audio; mirror
Spotify's output faithfully.

## Goals

1. **Replace the WinForms UI** with a modern, themeable WPF shell — without
   rewriting the recording engine, which stays as a shared library.
2. **Stop losing recordings.** Decouple encoding from capture so a slow encode or
   a fast track change can't drop a recorded track.
3. **Unify the encode pipeline** on bundled FFmpeg (drop the NAudio.Lame native
   DLLs) so every format (MP3/Opus/FLAC/WAV) takes the same path and any sample
   rate / channel count is handled.
4. **Add a quality analyzer** ("Spek-lite"): waveform, averaged frequency
   response with detected cut-off, full spectrogram, and a plain-language verdict
   (true lossless vs lossy-in-a-lossless-container vs lossy + estimated bitrate).
5. **Secure the Spotify API credentials** at rest (DPAPI), with show/hide reveal.
6. **Localize everything** the user can see, in English and French, with the two
   resource files always in sync.
7. **Make the UX feel intentional** — consistent visual hierarchy, spacing,
   colour, iconography across every screen.
8. **Rebrand to Spytify+** — new logo/wordmark, multi-resolution app icon, icon
   in the title bar, product metadata.

## Non-goals / constraints

- **The recording engine is not rewritten.** WPF talks to the existing
  `EspionSpotify` engine through the `IFrmEspionSpotify` callback interface.
- **The executable stays `Spytify.exe`.** Only the *display* name becomes
  "Spytify+", so process references, kill scripts and anything launching it keep
  working.
- **No new heavy dependencies for the analyzer.** Decode reuses the bundled
  `ffmpeg.exe` (no ffprobe); the FFT reuses NAudio (already referenced). No
  ImageMagick, no SVG runtime library.
- **Win32 icons stay raster.** The app/taskbar/tray/title-bar icon must be an
  `.ico`; SVG only drives the in-app WPF logos.
- **Brand/technical terms stay literal**: Last.fm, Spotify API, the spectrogram
  palette names (Inferno/Magma/Viridis/Heat — matched by code), and unit labels
  (kHz, kbps, dB).

## Target / platform

- .NET Framework **4.8** (retargeted from 4.6.1), WPF + WinForms interop
  (`UseWPF` + `UseWindowsForms`), Windows-only.
- UI: **ModernWpfUI 0.9.6** (Fluent styling, NavigationView, modern window
  chrome).
- Build: Visual Studio MSBuild (engine project is non-SDK / packages.config; WPF
  project is SDK-style). The test suite (349 tests) must stay green.

## Success criteria

- WinForms fully retired; WPF is the sole front-end and the startup `Spytify.exe`.
- Recordings never dropped under rapid track changes (background encode queue).
- Analyze tab correctly classifies: true-lossless FLAC/WAV, a 320/256/128 MP3
  (visible cut-off cliff), a FLAC transcoded from MP3 (lossy-source flag), and
  high-bitrate AAC/Opus (full-band but still lossy — codec is authoritative).
- en/fr resx in sync, every key mirrored in the `TranslationKeys` enum,
  **349/349 tests pass**.
- Consistent, polished UX; Spytify+ branding throughout.
- Offline-library features (templates, playlist-as-album, verification, quality
  gating, cover.jpg + ISRC/Spotify-ID tags, .m3u export) work without ever altering
  the captured audio.
