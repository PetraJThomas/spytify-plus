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
