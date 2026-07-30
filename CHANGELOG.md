# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- Brightness, 10 to 100 %, in the window and as `-brightness <n>` on the command line. It
  scales the colour that is sent, because the controller has no brightness of its own - so it
  covers the effects that carry a colour, and the slider is hidden for the ones the firmware
  colours itself.
- The channel selector lists every channel, not just every controller: all of them together,
  the onboard zone, a single ARGB header, or one whole controller on boards with several. It is
  shown as soon as there is more than one channel, which is every supported board.
- Custom presets hold one effect and colour per channel rather than per controller, and are
  created, edited and deleted from the effect drop down itself: the last row creates one, a
  pencil edits, an X deletes after confirming on the row.
- A new preset starts every channel from whatever effect and colour is running right now,
  instead of always defaulting to static white - the controller cannot report its state per
  channel, so this is the closest thing to "start from the current look" the hardware allows.
- Channels can be given a name of their own: hover one in the channel selector for a pencil,
  type a name, save. Shown everywhere that channel's name appears - the selector itself and the
  preset editor - and cleared back to the computed default (Onboard, ARGB 1, ...) with Reset.
- Selecting a whole controller, on a board that has more than one, shows and switches that
  controller rather than the board: it reads as on while any of its channels is.
- Every channel remembers the effect and colour it was last set to, and whether it is on. Picking
  a single channel shows its own look and its own state, the big button then switches just that
  channel, and switching the board back on returns each channel to what it was running instead of
  pushing one colour across all of them.
- A custom preset carries a brightness per channel too, set on that channel's own row in the
  editor and applied when the preset is switched to.
- With a single channel selected, the effects the controller generates itself - spectrum cycle,
  rainbow, rainbow breathing and wave - are left out of the list, and a row says why: the
  controller has one engine for them and runs them across all of its channels at once. Picking
  one for a single header used to change every header of that controller without saying so.
- Brightness is per channel as well: the slider follows the channel selector, so one header can
  run dimmed next to one at full brightness, and each channel remembers its own. Setting it with
  "all channels" selected hands every channel back to the board-wide value.
- The window follows a light or dark switch in Windows while it is running, instead of staying
  half themed until the next start.
- The custom preset editor can be dragged by its heading. It has no title bar of its own, so it
  used to sit wherever it opened.
- Keyboard and screen reader support for the parts that had none: the colour chips are reachable
  with the arrow keys, the drop down takes F2 to rename and Delete to remove, Tab is no longer
  swallowed, the settings switches report their label and their state, and the brightness slider
  takes the wheel and Page Up/Down.

### Changed

- Rounded shapes whose contents take several passes - the toggle button, every effect icon -
  render through an offscreen surface and are masked once. They were clipped by a region, which
  is not anti-aliased, and then washed over a second time; that was the remaining ragged,
  dark-rimmed edge.
- The running-light effects animate more slowly.
- The state on the big button is set much larger, so the window can be read at a glance, and the
  primary button of a panel carries a heavier label than the rest.
- The channel selector is only as wide as its own longest name, which leaves the effect list the
  rest of the row - long preset names were being cut off while the selector kept room it did not
  need. A tenth colour chip, a bright pink, joins the palette.
- Effect icons only draw the hairline along their edge when they would otherwise disappear into
  the panel behind them. On a coloured icon that line read as a rim around some icons but not
  others.
- The installer's licence page says in plain words what the licence allows, what the tool does to
  the board and whose the trademarks are, in German and English, instead of showing the bare MIT
  text.
- Creating a preset moved out of the settings panel to the effect drop down, where the presets
  are.
- Every stored file is written through a temporary file and moved into place, so an interrupted
  save can no longer leave an empty or half-written one behind, and a lock keeps the window and a
  command line invocation from overwriting each other's changes.
- Reading a stored file checks what it actually contains rather than assuming, so a hand-edited or
  truncated file is ignored instead of being taken at face value.
- The commands the tool is allowed to send are now enforced in code, not just documented, and an
  effect mode read back from a stored file is validated before it reaches the controller.
- Window, panel and button sizes are measured from their own contents, so a longer translation or
  a larger display scale no longer clips text or squeezes the switch.
- The drop down stops at the edge of the screen and scrolls, instead of running off it once enough
  custom presets exist.
- `build.bat` with no argument now builds everything - it only made the portable executable
  before, which is why the installer kept not appearing - and rejects an unknown option instead of
  quietly building something else.
- The setup no longer carries a copy of .NET: it checks for the .NET 10 Desktop Runtime, and when
  the machine has none it asks once, downloads it from Microsoft and installs it. That took the
  download from 63 MB to 2.5 MB, and it also means only one build of the application exists
  instead of a framework dependent and a self contained one.
- The setup can be installed just for the current user, without administrator rights, instead of
  always asking for elevation.
- A change to one channel now writes every channel of that controller: the ones that were not
  named are re-asserted exactly as they stand. The controller applies an effect across its
  channels unless the whole mix arrives at once, which is why a static header could not sit next
  to a header running a wave.
- The window and its panels share one set of fonts instead of each control creating its own and
  never releasing it.

### Fixed

- A plain `build.bat` deleted the whole `dist` folder, silently removing the installer and the
  standalone builds that `build.bat all` had produced. It now clears only the loose files in its
  own output folder.
- `build.bat` failed to copy its own exe, and so silently skipped the installer step, whenever a
  built copy of the app was still running - it locks its own apphost during publish. It now
  closes a running instance first.
- The custom preset editor found no controllers even while the title bar named four channels: it
  ran its own discovery, on the UI thread, while the first one was still settling. It uses the
  list the window already has.
- The custom preset editor's Create button stayed greyed out however much was typed into the name
  field: the field never passed its own text changes on, so nothing noticed the name.
- `build.bat` no longer leaves the intermediate build output in `dist` - it used an `OUTDIR`
  variable, which MSBuild read as its own `OutDir` and published the loose `.dll`, `.deps.json`
  and `.runtimeconfig.json` next to the executable. A full build now empties `dist` and leaves
  exactly the executable, the two shortcuts, the setup and `arm64\`, with no `.pdb`.
- A colour chip no longer draws a tick on top of its ring.
- Renaming a custom preset left the old name behind as a second entry.
- A damaged `settings.json` made the tool refuse to start at all: reading it happened before
  anything else and threw an exception that nothing caught. All five stored files are now read
  defensively.
- Buttons inside the popups drew a square patch of window colour behind their rounded corners,
  which showed up as an ugly border on the panel they sat on.
- A custom preset naming a controller that is no longer connected now reports that instead of
  claiming the lighting changed.
- Cancelling the colour picker with Escape applied the colour anyway.
- Pressing the gear while the settings panel was open closed and immediately reopened it.
- The notification area entry stayed clickable with no controller present and while a switch was
  already running.
- A wedged controller could park the switching thread indefinitely; writes now time out.
- Discovery enumerated every HID interface on the machine twice, and the stored files were
  re-read once per channel on every switch.
- The regression suite only restored `settings.json` while it also rewrote the state, presets,
  channel names and per-channel records - a test run destroyed them. It now backs up and restores
  all five whatever happens.

## [1.0.0] - 2026-07-29

### Added

- Switch ASUS Aura mainboard lighting off and on: onboard zone, 12 V RGB headers and every
  addressable ARGB header, on every controller the machine has.
- Toggle window built around one button that shows the state and, while the lighting is on,
  animates the effect that is running. Animation can be switched off in the settings.
- Themed drop down listing the effects, each with an icon drawn from the effect itself.
  Choosing one applies it straight away.
- Colour chips plus a small themed colour picker (hue strip, saturation/value square, hex
  field) for the effects that use a colour.
- Custom presets: a name plus one built-in effect and colour per controller, created and
  managed from the settings and selectable everywhere the built-in effects are. Meant for
  machines with more than one Aura controller that should look different at once.
- A per-controller selector next to the effect list, shown once more than one controller is
  found, to switch every controller together or one at a time.
- Settings behind the gear: start with Windows, start minimised (only when Windows itself
  started the tool - starting it by hand always shows the window), minimise instead of
  closing, the lighting state to apply at start, the animation switch, the interface language
  and creating custom presets. The panel and every drop down are non-modal, so a click
  anywhere else dismisses them without losing other open panels.
- Notification area icon with a themed menu that toggles the lighting, reopens the window or
  quits. Minimising and, optionally, closing send the window there.
- Starting the tool a second time brings the running one back instead of opening a window.
- One installer covering x64 and ARM64, alongside the portable executable, installing per
  machine into Program Files with optional autostart and desktop shortcut.
- The title bar names the total channel count read from the device(s).
- Nine built-in effects selectable by name: static, breathing, flashing, spectrum-cycle,
  rainbow, rainbow-breathing, chase-fade, chase and wave.
- Command line interface: `-on`, `-off` and `-preset <name> [colour]`, also accepted as
  `--on`, `/on` and `on`, in any capitalisation. Colours as `#RRGGBB` or by name.
- Exit codes for scripting: `0` success, `2` unknown argument, `3` controller not found,
  `4` controller in use, `5` communication error.
- Controller detection through the device handshake instead of a fixed interface number, so
  ASUS boards other than the reference board are covered, including boards with more than one
  Aura controller.
- German and English user interface, following the Windows display language by default, with
  a setting to force one language.
- Two ready-made shortcuts next to the executable, Aura An and Aura Aus, carrying a relative
  path so the folder can be moved without breaking them.
- Regression suite in `tests\`.
- `build.bat` producing a framework dependent build, a self contained build, the installer, or
  everything at once including a ready-to-upload release set with checksums.

### Fixed

- Report buffers follow the length the device reports instead of assuming 65 bytes, and short
  or truncated replies no longer throw while the configuration table is read.
- A device that fails mid-read, an unreadable state file and a locked down Run key are handled
  instead of ending the process.
- Commands are paced and the switching sequence runs twice, because the controller silently
  drops commands that arrive while it is still busy - that showed up as the onboard zone
  switching while the ARGB headers kept running.
- Switching runs off the UI thread, so the window keeps painting while the controller is being
  talked to.
- Drop downs and the settings panel close on a click anywhere else; as modal windows they used
  to swallow exactly that click.
- Rounded corners come from the desktop compositor instead of a clipping region, and popups no
  longer also draw their own competing border - together, what made them look stair-stepped
  with dark fringed edges.
- The animation timer runs at 30 fps with a cached gradient instead of 60 fps with one
  rebuilt every frame, and effects render as a single flat fill while animation is off -
  cheaper to paint and unambiguous about being paused.

[1.0.0]: https://github.com/wbgcoding/aura-toggle/releases/tag/v1.0.0
