# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.0] - 2026-07-29

### Added

- Switch ASUS Aura mainboard lighting off and on: onboard zone, 12 V RGB headers and every
  addressable ARGB header.
- Toggle window with a status card, a button showing the current state, and a drop down of
  the available effects with a Set button.
- The status card names the running effect, shows its colour and reports the controller
  firmware and channel count read from the device.
- Nine built-in effects selectable by name: static, breathing, flashing, spectrum-cycle,
  rainbow, rainbow-breathing, chase-fade, chase and wave.
- Command line interface: `-on`, `-off` and `-preset <name>`, also accepted as `--on`, `/on`
  and `on`, in any capitalisation.
- Exit codes for scripting: `0` success, `2` unknown argument, `3` controller not found,
  `4` controller in use, `5` communication error.
- Controller detection through the device handshake instead of a fixed interface number, so
  ASUS boards other than the reference board are covered.
- German and English user interface, following the Windows display language.
- Regression suite in `tests\`.
- Commands are paced and the switching sequence runs twice, because the controller silently
  drops commands that arrive while it is still busy — that showed up as the onboard zone
  switching while the ARGB headers kept running.
- `build.bat` producing either a small framework dependent build or a standalone build.

[1.0.0]: https://github.com/wbgcoding/aura-toggle/releases/tag/v1.0.0
