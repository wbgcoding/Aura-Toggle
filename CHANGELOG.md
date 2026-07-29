# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
