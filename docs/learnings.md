# Spytify+ — Learnings & gotchas

Hard-won notes from the `spytify-plus` work. Most cost a wrong build or a crash to
discover; recorded here so they don't have to be rediscovered.

## ModernWpf (0.9.6)

- **`DefaultToggleSwitchStyle` does not exist.** `BasedOn="{StaticResource
  DefaultToggleSwitchStyle}"` crashes at startup. Use `BasedOn="{StaticResource
  {x:Type ui:ToggleSwitch}}"` instead. `DefaultButtonStyle` and
  `DefaultComboBoxStyle` *do* exist.
- **`{x:Type TextBox}` does not resolve to ModernWpf's textbox style** — basing a
  TextBox style on it drops you to plain white WPF default. Don't restyle the
  TextBox; only match the ComboBox `MinHeight`/`FontSize` to it.
- **ToggleSwitch toggling is wired to a named template part** (`SwitchAreaGrid`).
  A custom ControlTemplate breaks click-to-toggle. To resize a switch, scale
  ModernWpf's own template with a `LayoutTransform` (1.1×) rather than re-template.
  (CheckBox/RadioButton toggle at the control level, so custom templates are safe
  there.)
- **Control content/header colour often comes from a theme brush, not
  `Foreground`.** The ToggleSwitch header ignored `Foreground`; the fix was a
  `HeaderTemplate` with an explicit-coloured `TextBlock`.
- **The modern window does not show the title-bar icon by default.** Setting
  `Window.Icon` is not enough. You need `ui:TitleBar.IsIconVisible="True"` (a real
  attached property on `ModernWpf.Controls.TitleBar`, backed by the template's
  `IconTitlePanel` part).
- **`NavigationViewItem.Content` doesn't always re-evaluate its `{l:Tr}` binding on
  a language switch.** Force it: iterate `Nav.MenuItems` and call
  `BindingOperations.GetBindingExpression(item, ContentControl.ContentProperty)
  ?.UpdateTarget()`.
- **The `Symbol` enum is the limited UWP set** — no `Folder`, `Link`, or `Stop`.
  For those, use `ui:FontIcon Glyph="&#xE8B7;"` etc. with raw Segoe MDL2 codes.
- **NavigationView section transitions** snap under rapid clicking if driven off
  `SelectionChanged` (it waits on the pane's selection-indicator animation). Drive
  the content switch from `ItemInvoked` (immediate); keep `SelectionChanged` for
  keyboard/programmatic; guard both with an `_activeTag` so a click animates once.

## Localization

- **Every resx key must be mirrored in `EspionSpotify/Enums/TranslationKeys.cs`.**
  `TranslationTests` asserts the resx key set *exactly equals* the enum (membership
  **and** count). A new string with no enum member fails the suite. (The resx
  header comment contains example entries `Bitmap1/Color1/Icon1/Name1` inside
  `<!-- -->` — not real resources, don't count them. Real key count is 168.)
- **WPF uses string keys** (`{l:Tr key}` / `Loc.Instance["key"]`); the **engine
  uses the `I18NKeys` enum** (`WriteIntoConsole`). Two different mechanisms over
  the same resx.
- **`{l:Tr}` live-updates** via `Loc` raising `PropertyChanged("Item[]")` — but
  **imperatively-set strings don't** (e.g. the connect status, the Start/Stop
  label). Re-localize those in the `SelectedLanguage` setter (`RefreshSpotifyConn
  State`, `OnPropertyChanged(nameof(StartStopLabel))`).
- **"Identical" words read as untranslated.** "Configuration" and "Client ID" are
  the same in en/fr, so French users think they weren't localized. Settings tab →
  "Paramètres" (matches "Paramètres avancés"); Client ID/Secret → "Identifiant
  client"/"Secret client".
- **A field initializer runs before the ResourceManager is built**, so
  `Loc.Instance["key"]` in a field initializer returns the key itself. Set the
  initial localized value after `BuildResourceManager()` (e.g. in `LoadState`).
- **resx values need `&amp;` for `&`**; literal `≈`, real newlines, and French
  guillemets `« »` are fine in UTF-8. `sed` over UTF-8 resx preserved accents.

## Audio analysis

- **Only `ffmpeg.exe` is bundled — no ffprobe.** Probe by running `ffmpeg -i` and
  parsing **stderr**; decode via `-f f32le` to stdout. Don't resample on decode or
  you destroy the high-frequency cut-off you're trying to measure.
- **The codec is authoritative for lossy/lossless, not the spectrum.** A
  high-bitrate AAC/Opus at 48 kHz reaches Nyquist with no cliff — the spectrum
  looks "full-band" but it's still lossy. Classify by codec first; use the
  spectrum only for bandwidth/cut-off. (This fixed an AAC false-positive.)
- **Spectrogram looked near-black** until normalized to the file's *peak*
  magnitude rather than an absolute reference.
- **Cut-off detection must measure transition steepness *locally*, not globally.**
  The first version took the highest bin clearing a single −60 dB floor; on a soft,
  bass-heavy track the gentle high-frequency roll-off naturally dips below −60 dB
  (relative to the loud low end) in the mid-teens kHz, so genuine lossless got
  flagged as a transcode. The defining feature of a lossy low-pass is a **brick-wall
  step**: a steep drop into a dead plateau. Detect *that*, by sliding a narrow
  window down the spectrum — in-band mean over the ~1.5 kHz below an edge vs plateau
  mean over the ~2 kHz above — and taking the highest edge whose drop is ≥ 25 dB.
  Natural roll-off is < ~10 dB per 2 kHz everywhere, so the separation is huge.
- **Lossy files leave a rising quantisation-noise floor near Nyquist**, and it
  wrecks any *global* steepness measure. A "spread between the −40/−60/−80 dB
  crossings" looked elegant until a 128k MP3 with an obvious 14 kHz cliff reported
  full-band: its noise floor poked above −80 dB up at 22 kHz, so the −80 crossing
  jumped to Nyquist and the global spread went meaningless. Measuring the drop in a
  window *local* to the edge (never looking all the way to Nyquist) is immune to it.
- **More passes is not more robustness.** An "ensemble" of three equal votes was
  *worse* than one good measure: two non-discriminating passes outvoted the correct
  one. *Residual energy above the cut* is dominated by the bass, so it's ~0 above
  13 kHz for **any** music, lossy or not. *Spectral flatness* reads "flat" for real
  high-frequency air/sibilance just as much as for quantisation noise. Robustness
  came from a single physically-defining measure (the local step), combined as a
  necessary condition, not from quantity or majority vote.
- **Place the cut-off *line* by scanning up from in-band to the first *sustained*
  drop** below the cliff mid-point (a few-hundred-Hz "stays below" hold). Seeking
  the line from the top down against a fixed threshold lets a lone faint noise-floor
  bin inside the plateau drag the marker up above the real cliff.
- **The shallow-step zone is genuinely unsolvable from spectrum alone.** A bright,
  airy lossless master and a low-bitrate MP3 whose artefact noise sits only ~15-20 dB
  below the band (not a clean 50 dB dead floor) overlap. No analyzer (Spek included)
  separates them from the spectrum. The codec-authoritative rule is the backstop:
  the spectral cut-off only changes the *verdict* inside a lossless container
  (transcode hunting), where real source cuts are almost always clean deep steps;
  everywhere else it only colours the detail line.

## WPF / vector / icons

- **WPF has no native SVG.** But `Path.Data` / `Geometry` mini-language is
  compatible with SVG path `d` syntax (`M/L/C/S/Q/A/Z`, relative lowercase, and
  implicit number separation like `16.92.39`). Clean SVGs (solid fills, no
  gradients/clips) convert to a `DrawingImage` of `GeometryDrawing`s with zero
  redraw — crisp at any DPI, a few KB vs a supersampled PNG.
  - Verify before trusting runtime: `[System.Windows.Media.Geometry]::Parse($d)`
    in PowerShell parses each string (a bad path throws at *load*, not build).
- **`Window.Icon` with a `.ico` needs the file added as a `<Resource>`** (the
  `ApplicationIcon` property alone doesn't make it loadable by a relative URI).
- **`convert` on Windows is the filesystem tool, not ImageMagick.** Built the
  multi-res `.ico` by hand in PowerShell: resize the PNG to 16–256 with
  `System.Drawing`, then write the ICO container (ICONDIR + ICONDIRENTRY per size +
  PNG-compressed image data). PNG-compressed ICO entries are fine on Vista+.
- **Segoe MDL2 Assets is single-weight.** `FontWeight` on a `FontIcon` has no
  effect (WPF doesn't synthesize faux-bold). For a heavier look, use filled glyph
  variants, not weight.
- **State-driven button glyphs**: for Start/Stop, two shapes (record `Ellipse` /
  stop `Rectangle`) toggled by `Visibility` bound to `IsRecording` via
  `BoolToVis`/`InverseBoolToVis` are more reliable than guessing a "stop" glyph.

## Offline-library suite (Phase 10)

- **Track metadata fills ~1s *after* the track change**, on the same `Track`
  instance (a delayed task in `SpotifyStatus.GetTrack` calls `UpdateTrack`). So
  `AlbumArtUrl`, `Length`, ISRC etc. are null at the `OnTrackChange` moment. The
  player card pushes art on every 1s `OnTrackTimeChanged` tick (UI dedupes by URL)
  to catch the late fill; a one-shot push at track-change misses it.
- **The playback context carries the playlist.** `PlaybackContext.Context` (already
  fetched in `UpdateTrack`) has `Type`/`Uri` — when `Type == "playlist"` the URI is
  `spotify:playlist:{id}`. No extra scope for public playlists; **private** ones need
  `PlaylistReadPrivate` added to the auth (users re-consent).
- **ffmpeg passes arbitrary ffmetadata keys straight through to FLAC as UPPERCASE
  Vorbis comments** — verified by round-trip that `SPOTIFY_TRACK_ID=...` in the
  ffmetadata file reads back as a `SPOTIFY_TRACK_ID` comment. That's how the custom
  IDs get written on the FLAC/OPUS path.
- **`TagLib.Tag.ISRC` is writable cross-format** (nice), but there's **no generic
  custom-field API** on the base `Tag`. Spotify IDs need format-specific writes: an
  ID3 `UserTextInformationFrame` (TXXX) via `GetTag(TagTypes.Id3v2)`, and
  `XiphComment.SetField` via `GetTag(TagTypes.Xiph)`.
- **`Normalize.RemoveDiacritics` is misnamed** — it FormD-decomposes, strips invalid
  *filename* chars, then FormC-recomposes, so diacritics are **preserved** (combining
  marks aren't invalid). The whole app keeps accents; the templating path matches
  that on purpose.
- **A new engine `.cs` file must be added to the non-SDK csproj** `<Compile Include>`
  by hand (`PathTemplate.cs` failed to compile until added) — packages.config
  projects don't auto-glob like SDK-style ones.

## Process / engineering

- **Spotify OAuth needs runtime deps the engine doesn't flow through the project
  reference**: EmbedIO (`Unosquare.Labs.EmbedIO.dll`) + System.Memory/Buffers/
  Unsafe/Numerics.Vectors facades + binding redirects. When the entry exe changed,
  these dropped and auth failed *silently* (the auth code swallows the load
  exception). Added as PackageReferences in the WPF csproj.
- **Buttons darken on hover** (black overlay 30%, press 45%) — the user wanted a
  shade, not a lighten.
- **No em dashes** in UI text/prose (a project rule) — use colons/commas/middots.
- **Rapid-fire polish is the right loop** for "feels off" UX: change a few px,
  rebuild, eyeball, repeat. You can't spec "beautiful"; the screen tells you when
  it's done.
- **A new persisted setting touches three files**: `Properties/Settings.settings`,
  `Properties/Settings.Designer.cs` (hand-edit — MSBuild doesn't regenerate it), and
  `App.config`. `UserSettings` copies new properties automatically (`CopyAllTo` is
  reflection-based).
- **A ModernWpf `ToggleSwitch` can't be flipped via UIAutomation** — it exposes no
  Toggle pattern and `SetFocus` is blocked. For automated GUI verification, set the
  value in the persisted `user.config` and relaunch instead of clicking.
- **`docs/*` is git-ignored except `.md`** (inherited rule). Committing README
  screenshots needed a `!docs/screenshots/` exception.

## Library integrity, settings & release (Phase 11)

- **The .NET user-settings path embeds the entry assembly's `AssemblyVersion`**
  (`…\<Company>\<App>_<hash>\<AssemblyVersion>\user.config`). Every version bump strands
  settings in a new folder. Fix: **pin the WPF `AssemblyVersion`** (2.0.0.0) forever;
  bump the release version via the engine `AssemblyVersion` (the updater compares that)
  and the WPF `<Version>`. Gotcha: an unset `<FileVersion>` inherits `AssemblyVersion`
  (the pin), not `<Version>`, so set `<FileVersion>` explicitly or the displayed version
  is wrong. A company rename (`Spytify` → `Spytify+`) also moves the folder.
- **A setter that saves to `Settings.Default` but that `LoadState` never reads back**
  silently doesn't persist (`UpdateId3Tags` reverted every launch). Every toggle needs
  both halves.
- **Windows Media Player writes album art as a Hidden+System `Folder.jpg`**, and
  `File.WriteAllBytes`/`File.Create` throw `UnauthorizedAccessException` on a *hidden*
  target. Our overwrite was silently swallowed. Clear Hidden/System/ReadOnly before
  writing. (Windows' own half-finished FLAC-art feature created the file that blocked us.)
- **Windows FLAC support is broken/unfinished**: the shell doesn't read embedded FLAC art
  for thumbnails, and the new Media Player reads it in its Edit dialog but won't show it
  on the tile and can't write FLAC tags at all. Not a file problem, use VLC/MusicBee/
  foobar. `Folder.jpg` is the sidecar that some players/Explorer do honour.
- **Spotify's `isrc:` search doesn't index every track** (notably DIY-distributor
  ISRCs, `QZ…`). A whole album that *is* on Spotify can come back "no match". Fall back to
  the embedded **Spotify track ID** (`GetTrack(id)`, exact). We embed both, so it's still
  an exact-identifier match, never fuzzy.
- **429 is benign** (rolling ~30s window, resets itself; no account/app penalty). But the
  old code counted throttled calls as "no match" silently. `SpotifyWebAPI` has built-in
  retry: `UseAutoRetry=true` + `TooManyRequestsConsumesARetry=false` waits the
  `Retry-After` and keeps retrying without burning the budget.
- **.NET Framework `ZipFile.CreateFromDirectory` writes BACKSLASH separators** (non-
  standard; breaks extraction elsewhere). Build the release zip by walking files and
  `ZipArchive.CreateEntry(rel.Replace('\\','/'))`.
- **The release zip must include the `Updater/` subfolder** (the app launches
  `<app>/Updater/Updater.exe`), which the WPF build does *not* produce, copy it from the
  engine output. The updater ignores draft/prerelease releases; the asset must match
  `^Spytify(-|\.)…zip$`.
- **Adding an optional parameter breaks method-group→delegate conversion**
  (`Task.Run(mapper.SaveMediaTags)` stopped compiling). Use an overload, not a default arg.
- **Building the test project only rebuilds the engine's own dll, not the copy inside
  the running exe.** After engine changes, do a full solution build (and close the app,
  which minimises to the tray and locks the dll) so the launched exe is current.
- **Fixed 2-digit `{track2}` mis-sorts past 99**, and the `.m3u` sorted ordinally
  (`"100"` between `"10"` and `"11"`). Windows Explorer's natural sort hid it in the file
  view but players and the m3u were wrong. Dynamic-width padding + a natural comparer fix it.
