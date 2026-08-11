# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.2.0] - 2026-08-11

### Added

- `-toggle` on the command line: on if the lighting is off, off if it is on. A single targeted
  channel decides by its own remembered state rather than the board's.
- `-list` now also names every saved custom preset, numbered for use with `-custom`.
- `-status --json`: the same information as plain `-status`, as one line of JSON for scripts.
- A single left click on the tray icon now toggles the lighting; double click still opens the
  window, and right click still opens the menu.
- The window remembers where it was last placed and reopens there, as long as that spot is still
  on a display that exists - otherwise it centres itself instead of opening off-screen.
- "Always on top" in the settings panel.
- A tooltip on F2/Delete-capable rows in the effect list, and one on every built-in effect
  explaining what it does.
- A duplicate icon next to edit and delete on every custom preset row, naming the copy
  "<name> (2)" (counting up on a collision) without opening the editor for it.
- "Reset settings" now zips every file it is about to touch into `reset-backup.bak` first.
- Export and Import buttons in the custom preset editor, to move one preset as a single `.json`
  file to another machine, profile, or just a backup.
- The on/off button's label is now bold, wide-tracked upper case instead of the plain default
  weight and spacing.
- Optional update check (on by default, one click to turn off): at most once every 24 hours the
  tool asks GitHub for the latest release. An installed copy can install the update from a tray
  menu entry once its checksum matches the release's own `SHA256SUMS.txt`; a portable copy is
  offered a link to the release page instead, since it cannot replace itself.
- The uninstaller now names the actual files it is about to delete when asking to remove settings.

### Documentation

- README: release/license/download badges, a checksum-comparison command, a SmartScreen note, a
  "does this fit my board" pointer, silent install/uninstall switches, an honest OpenRGB/SignalRGB
  comparison, and why the lighting never survives a reboot on its own.
- New screenshots (English, both themes) showing the new bold button label, now animated with a
  moving rainbow wave over the button - `tools/make-preview-gif.py` builds them from the plain
  screenshots, with no button coordinates hardcoded.

### Changed

- The tray icon shown while the lighting is off is now noticeably darker and desaturated, instead
  of a shade too close to the "on" icon to tell apart at taskbar size.

- The window title is just "Aura Toggle". It used to carry the channel count as well, which said
  nothing a look at the channel selector does not.
- The documentation is English only. The German README is gone; the interface itself stays
  bilingual.

### Removed

- "Open log folder" from the settings panel. The README names the path under Troubleshooting, and
  the error dialog still offers the button where it actually helps - next to the details of a
  failure worth reporting.
- The diagnostic log lines about window layout and window placement, written while a display-scale
  bug was being tracked down. That bug is fixed; the lines were noise in every log since.

### Fixed

- The "Details" link in the error dialog did nothing when clicked with the mouse: the click was
  handled twice and folded the details shut again in the same motion. Keyboard use was unaffected.
- The screen-edge margin popups keep, and the separators between channels in the preset editor,
  now scale with the display instead of staying at their 100 % size.
- `-list` and `-status` reported "no controller found" (exit code 3) instead of "controller busy"
  (exit code 4) whenever another application - Armoury Crate above all - already had the
  controller open. `-device`/`-channel` made the same mistake, reporting a bad argument instead of
  a missing controller when no hardware was present at all.
- A custom preset that names two controllers no longer leaves the board and the stored state
  disagreeing if the second controller drops the write: the first controller's channels are
  recorded either way now, matching how switching the whole board already handled the same case.
- `-preset static transparent` (and any Windows system colour name) painted the board white
  instead of being rejected as an unusable colour.
- A hand-edited `channel-state.json` naming an effect this build does not know was shown as-is in
  the window while the board silently ran the default effect instead. It now falls back to the
  same default the window shows, so both agree.
- A failed save (full disk, a security tool holding the file) could leave a stray `.tmp` file
  behind in the data folder forever.
- Two lines written to the log file at nearly the same moment - the app and a command line call
  running together, which the test suite does constantly - could make one of them disappear.
- The "Reset settings" button kept its old-language label and confirmation text after switching
  languages in the settings panel.
- The short usage line named "aura" as the program to run; the actual file is "AuraToggle.exe".
- A rename popup opened again right after the previous one was rejected by the controller could
  silently do nothing, with neither its display nor its "busy" state ever clearing.
- The installer's checksum step could run arbitrary commands if the checkout path contained an
  apostrophe.
- The update check no longer trusts an installer or checksum URL from anywhere but GitHub's own
  asset hosts, closing a redirect that could otherwise hand back a matching pair for a hostile
  download.
- Declining the installer's UAC prompt during a self-update now shows the existing "update
  failed" balloon instead of crashing.
- Saving the window position, the last update-check time, or a setting from the settings panel
  could silently discard whatever either of the other two had just written, if two of them landed
  close together - all three now read-modify-write under the same lock.
- The update tray entry could go unreachable the next time the window was opened and closed,
  because closing to tray always re-hid the icon regardless of a pending update.
- The system tray's right-click menu is now released together with the tray icon on exit, instead
  of leaking until the process ends.

## [1.1.0] - 2026-08-09

First public release. Aura Toggle switches the Aura lighting of an ASUS mainboard on and off
from a single portable executable — no background service, no driver, no admin rights.

### Added

**Switching the lighting**

- A window with one big button that shows whether the lighting is on and switches it. While it is
  on, the button animates the effect that is actually running, brightness included.
- Nine built-in effects: static, breathing, flashing, spectrum cycle, rainbow, rainbow breathing,
  chase with a fading tail, chase and wave. They apply the moment they are picked.
- Nine colour chips plus a free-choice chip that opens a full colour picker, for the five effects
  that take a colour.
- Brightness from 10 to 100 % for those five effects, either for the whole board or for a single
  channel, so one header can run dimmed next to one at full brightness.
- A channel selector: all channels at once, the onboard zone, a single ARGB header, or one whole
  controller on boards that have more than one.
- Every channel remembers its own effect, colour, brightness and on/off state. Switching the board
  back on returns each channel to what it was running instead of pushing one colour across all of
  them.
- Channels can be given a name of your own — hover one in the selector for a pencil. While you
  name it, that channel lights up red and the rest of its controller goes faint white, so there is
  no guessing which header is which.

**Custom presets**

- A custom preset holds one effect, colour and brightness *per channel* under a name of your own:
  the onboard zone steady white while an ARGB header breathes red, say.
- Presets are created, edited and deleted straight from the effect drop down. A new one starts
  from whatever is running right now, so saving the current look needs no changes at all.
- The editor shows what it is building on the real hardware and puts the lighting back the way it
  was if you close it without saving. Deleting asks once before it goes through.

**Living in the background**

- A notification-area icon that dims while the lighting is off; right-click it to switch, reopen
  or quit. Closing the window can be set to minimise there instead.
- Autostart, with a choice of what the lighting should do when Windows starts.
- A global hotkey, Ctrl+Alt+L by default and configurable, switches the whole board from anywhere.
  A combination another program already claimed is reported instead of silently doing nothing.

**Command line**

- `-on`, `-off`, `-preset <name> [colour]`, `-brightness <10-100>`, `-custom <preset>`, `-list`,
  `-status`, `--version` and `-help`, all with forgiving spelling (`-on`, `--on`, `/on`, `on`).
- `-device` and `-channel` target a single controller or channel instead of the whole board.
- Exit codes for scripting: `0` ok, `2` bad argument, `3` no controller, `4` controller busy,
  `5` communication error. `-list` and `-status` always print English so a script does not break
  when the window language changes.

**Interface and safety net**

- English and German, switchable in the settings panel.
- Follows the Windows light/dark setting while running, scales cleanly from 100 % to 200 % and
  survives being dragged to a second monitor.
- Keyboard and screen-reader support throughout: arrow keys on the colour chips, F2 to rename and
  Delete to remove in the drop down, wheel and Page Up/Down on the brightness slider.
- An error dialog with the details in a collapsible area, a button to copy them for a bug report
  and one to open the log folder. Paths are stripped of your user name before they are written.
- A log at `%LOCALAPPDATA%\aura-toggle\log.txt`, rotated at 200 KB, that records start-up, version
  and what went wrong when a controller could not be reached.
- "Reset settings" puts everything back to first-run defaults without a restart; saved presets
  survive it.
- Every stored file is written through a temporary file, so an interrupted save cannot leave a
  half-written one behind, and the window and a command line call cannot overwrite each other.

**Download**

- A portable executable of around 580 KB, and an installer of around 2.3 MB that can install for
  everyone or just for you — the latter without admin rights. When the .NET 10 Desktop Runtime is
  missing, the setup asks once and fetches it from Microsoft, verifying the download's Microsoft
  signature before it runs.
- `SHA256SUMS.txt` next to the release files.

### Known limitations

An effect and colour apply to a whole channel, never to a single LED. Spectrum cycle, rainbow,
rainbow breathing and wave come out of the controller's own firmware, which has one effect engine
per controller — set one of them on a single header and every header of that controller runs it,
and none of the four can be dimmed. Only mainboard lighting is covered: no GPU, RAM, fans or other
Aura Sync devices, and plain 12 V RGB headers get no channel of their own — only what the board
reports as its onboard zone is switched. The controller cannot report back which effect is
running, so the tool remembers what it last set.

[1.1.0]: https://github.com/wbgcoding/aura-toggle/releases/tag/v1.1.0
