# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [1.2.1] - 2026-08-22

Ten interface languages instead of two, a setup that no longer starts behind the window you were
looking at, and a round of fixes to the window's sizing, its safety checks and its German wording.

### Added

- Eight more interface languages: Spanish, Brazilian Portuguese, Italian, Dutch, Polish, Turkish,
  Japanese and Simplified Chinese, alongside the English and German that were already there. The
  setup speaks the same ten and follows the Windows display language; `/LANG=<code>` overrides it.
  The settings list names every language in itself, so it can be found from any of them, and
  `-preset` and `-channel` accept a name in any of the ten, not just the language in use.

### Changed

- Naming a channel now lights that one white and takes every other channel on the board dark,
  across all controllers, instead of lighting the chosen one red and holding the rest at a faint
  white. One lit header on an unlit board leaves nothing to work out.
- The button animates a custom preset's own effect: the one most of the preset's channels run, in
  the colour of the first channel running it. It used to keep animating whatever board-wide effect
  was set before the preset took over, which could be a rainbow wash under a preset that runs no
  rainbow anywhere.
- "Start Aura Toggle when Windows starts" is unticked every time the setup runs. Setup used to
  remember which boxes were ticked last time and tick them again, so once it had been switched on
  it came back switched on with every later install.
- The setup no longer opens its own "Select Setup Language" dialog. That window is put on screen
  before the setup can run a line of its own code, which is why it could end up behind whatever
  the user was looking at, making the setup look like it never started. The language now comes
  from Windows, and the first window is the wizard - which the setup does raise to the front.
- The licence page no longer claims the application makes no network connections of its own. It
  says what actually happens instead: a daily update check against the project's release page that
  can be switched off, nothing downloaded without being asked for, and everything the tool
  remembers kept in the user's own profile.
- The executable and the setup now name `BGCoding` as their publisher in the Windows file
  properties.
- Importing a preset refuses a file larger than 256 KB, and shortens a name longer than the 40
  characters the editor itself allows. Names and labels read back from the stored files get the
  same treatment, so a hand-edited file cannot put a line break or a few thousand characters into
  a list row.
- The switch measures its ON/OFF label once per text and font instead of on every one of the 30
  frames it draws each second, and reuses its brushes from frame to frame instead of building new
  ones for each.
- Resetting a channel to its default name now asks first: the button arms on one click and
  only resets on the second, like every other action here that throws something away.
- `-custom` takes a preset name the way `-preset` already takes an effect name - casing,
  spaces, hyphens and underscores no longer have to match, while an exact name still wins.
- Four German texts now say what the rest of the interface says: `Presets` instead of
  `Voreinstellungen`, `An/Aus` beside the hotkey instead of `Ein/Aus`, a delete confirmation that
  admits the preset is gone for good, and an import error that reads as a sentence.

### Fixed

- The window no longer widens in front of the user a moment after it opens. It comes up at the
  width it was last closed at, which is what the reveal shows while the controller is still being
  looked for; before, the size worked out during startup replaced that remembered width with one
  measured for a top row that has no channel selector in it yet.
- A newer version is downloaded only when the host at the *end* of the redirect chain is still
  GitHub's. The published checksum cannot catch this on its own: a setup and a checksum file
  served by the same foreign host agree with each other perfectly.
- A damaged or hand-edited `settings.json` could put a hotkey nobody can press into the settings
  panel - a mouse button, or a code no keyboard has. Such an entry falls back to Ctrl+Alt+L, as a
  missing modifier already did.
- Looking for controllers no longer leaves the window greyed out for good when the search itself
  fails, and a preview that fails unexpectedly reports it instead of ending the process.
- The release page a new-version notice links to is opened only when the address really is
  GitHub's - the same check the download beside it already had.
- A stored file that damage or hand-editing has blown up to megabytes is ignored like an
  unreadable one instead of being read into memory whole, and the update check turns down an
  oversized answer for the same reason.
- The preset editor's Save and Delete buttons keep the width their own label needs after the
  window is moved to a screen with a different scale - the longer German labels could be cut off -
  and the room the editor reserves for its scrollbar now follows that screen as well.
- The preset editor keeps its heading, its close button and its Save/Delete row in place while
  only the channel rows scroll, so both stay reachable on a board with many channels.
- The error and the update dialog widen for a long heading instead of letting it run into their
  right edge.
- Focus rings, the drop-down chevron, the ring around a colour chip, the slider outline and the
  colour picker's markers take their thickness from the window's display scale rather than from a
  control's own, which lags behind while a window is moved between screens.

## [1.2.0] - 2026-08-14

First public release. Aura Toggle switches the Aura lighting of an ASUS mainboard off and on from
a single portable executable — no background service, no driver, no admin rights.

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
- Presets are created, edited, duplicated and deleted straight from the effect drop down. A new one
  starts from whatever is running right now, so saving the current look needs no changes at all;
  duplicating names the copy "<name> (2)" and opens straight into its editor.
- The editor shows what it is building on the real hardware and puts the lighting back the way it
  was if you close it without saving. Deleting asks once before it goes through.
- Export and Import move a single preset as one `.json` file — to another machine, a second Windows
  profile, or just a backup. On different hardware the channels are matched by position, since a
  different machine cannot have the same controller.

**Living in the background**

- A notification-area icon that dims while the lighting is off. Left click switches the lighting,
  double click reopens the window, right click opens a menu. Closing the window can be set to
  minimise there instead.
- Autostart, with a choice of what the lighting should do when Windows starts.
- A global hotkey, Ctrl+Alt+L by default and configurable, switches the whole board from anywhere.
  A combination another program already claimed is reported instead of silently doing nothing.
- The window reopens where it was last placed, as long as that spot is still on a display that
  exists — otherwise it centres itself instead of opening off-screen — and at the width it last
  closed at, so it does not visibly grow a moment after opening.
- "Always on top", for keeping the window in view while something else has the focus.

**Command line**

- `-on`, `-off`, `-toggle`, `-preset <name> [colour]`, `-brightness <10-100>`, `-custom <preset>`,
  `-list`, `-status`, `--version` and `-help`, all with forgiving spelling (`-on`, `--on`, `/on`,
  `on`).
- `-toggle` switches to whichever state the lighting is not in. A single targeted channel decides
  by its own remembered state rather than the board's.
- `-list` numbers every controller, channel and saved custom preset; `-custom` accepts a preset's
  number as well as its name.
- `-status --json` prints the same information as plain `-status` as one line of JSON for scripts.
- `-device` and `-channel` target a single controller or channel instead of the whole board.
- Exit codes for scripting: `0` ok, `2` bad argument, `3` no controller, `4` controller busy,
  `5` communication error. `-list` and `-status` always print English so a script does not break
  when the window language changes.

**Interface and safety net**

- English and German, switchable in the settings panel.
- Follows the Windows light/dark setting while running, scales cleanly from 100 % to 200 % and
  survives being dragged to a second monitor.
- A tooltip on every built-in effect explaining what it does, and one on the rows that answer to
  F2 and Delete.
- Keyboard and screen-reader support throughout: arrow keys on the colour chips, F2 to rename and
  Delete to remove in the drop down, wheel and Page Up/Down on the brightness slider.
- An error dialog with the details in a collapsible area, a button to copy them for a bug report
  and one to open the log folder. Paths are stripped of your user name before they are written.
- A log at `%LOCALAPPDATA%\aura-toggle\log.txt`, rotated at 200 KB, that records start-up, version
  and what went wrong when a controller could not be reached.
- "Reset settings" puts everything back to first-run defaults without a restart, and zips every
  file it is about to delete — your own custom presets included — into `reset-backup.zip` first.
- Every stored file is written through a temporary file, so an interrupted save cannot leave a
  half-written one behind, and the window and a command line call cannot overwrite each other.

**Staying up to date**

- An update check, on by default: at most once every 24 hours the tool asks GitHub for the latest
  release. Finding a newer one adds a tray notice and, the first time the window is open to see it,
  a small popup with the same choice that closes itself after 30 seconds — either one offered once
  per version, not on every start.
- An installed copy can install the update once its checksum matches the release's own
  `SHA256SUMS.txt`; a portable copy is offered a link to the release page instead, since it cannot
  replace itself. Nothing is downloaded or run without a click.
- Nothing but the question for the latest release tag is ever sent — no machine identifier, no
  usage data. Turned off by setting `"checkUpdates": false` in `settings.json`; there is no switch
  for it in the window.

**Download**

- A portable executable of around 630 KB, and an installer of around 2.3 MB that can install for
  everyone or just for you — the latter without admin rights. When the .NET 10 Desktop Runtime is
  missing, the setup asks once and fetches it from Microsoft, verifying the download's Microsoft
  signature before it runs.
- The uninstaller names the actual files it is about to delete when it asks whether to remove your
  settings.
- `SHA256SUMS.txt` next to the release files.

### Known limitations

An effect and colour apply to a whole channel, never to a single LED. Spectrum cycle, rainbow,
rainbow breathing and wave come out of the controller's own firmware, which has one effect engine
per controller — set one of them on a single header and every header of that controller runs it,
and none of the four can be dimmed. Only mainboard lighting is covered: no GPU, RAM, fans or other
Aura Sync devices, and plain 12 V RGB headers get no channel of their own — only what the board
reports as its onboard zone is switched. The controller cannot report back which effect is
running, so the tool remembers what it last set.

[1.2.1]: https://github.com/wbgcoding/Aura-Toggle/releases/tag/v1.2.1
[1.2.0]: https://github.com/wbgcoding/Aura-Toggle/releases/tag/v1.2.0
