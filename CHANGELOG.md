# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [1.6.0] - 2026-10-16

A safer and steadier release. Damaged settings files no longer take your presets with them, the
tool's own Windows library calls ignore a planted copy next to the exe, autostart behaves when it was
once disabled in Task Manager, and a long list of small fixes makes the window, the preset editor
and the translations more reliable.

### Added

- The setup warns when Aura Toggle is already installed in a different folder and offers to uninstall that copy first. Settings and presets are kept, and autostart moves over to the new folder

### Fixed

- Upgrading from an early 1.0 build left the old "Aura Toggle.exe" in the install folder, and an autostart entry pointing at it kept starting that old build at logon, which ignored the start lighting. The setup now removes it and points the entry at the current exe
- Autostart did nothing when it had once been disabled in Task Manager's startup tab, although the settings showed it as on. The settings now show it as off in that case, and turning it on there (or ticking it in the setup) clears the old mark so Windows starts the tool again
- With "Apply and close app" on, the start lighting could be gone right after logon and leave the board dark. The tool now sends it again a few times during the first minute and stops as soon as the lighting is changed by hand
- A damaged `presets.json`, `channel-state.json` or `channel-names.json` is kept as a `.bad-` copy beside it before anything is saved over it, instead of being overwritten and losing every preset in it
- Saving a preset while one of its controllers was unplugged dropped that controller's part of the preset. It is kept now
- Turning the board on with a custom preset active failed with "controller not found" when the controller had been detected again under a new path, and a controller the preset does not cover stayed dark. Both now get their last lighting
- Preview and apply could read a preset's entries differently when it held a whole-controller entry and a channel entry for the same controller. They agree now, and so does the editor
- The window could freeze for up to two seconds when a preset was deleted or a setting changed while the lighting was being switched
- "Reset everything" in one copy of the tool removed the autostart entry of another copy, and could run halfway while a switch was still writing. It now only touches its own entry and waits for the switch, without freezing the window
- Renaming a channel on a board with several controllers saved the controller's name into the channel name when nothing was changed
- Holding an arrow key in the closed effect list now ends on the effect the key stopped at, instead of jumping back
- Deleting a preset that could not be saved still changed the start lighting and the active preset
- Repeated failed switches opened a stack of identical error windows; one window is reused now. A tray message no longer shows a raw English system text
- The colour strip kept its old size after moving the window to a display with another scale
- The preset editor and the error window now switch language along with the main window
- The window no longer parses the preset and channel files again on every repaint unless they changed
- The file type in the preset import and export dialogs is translated
- Turkish shows brightness as %50, the Italian minimise setting is shorter, and the German setup speaks to the user the same way throughout

### Security

- The tool's own calls into Windows libraries such as `hid.dll` load them only from the Windows folder, never from a copy lying next to the exe, for example in Downloads
- The log replaces your Windows user name in paths outside your profile folder too, and no longer names your custom presets
- Removing an old copy through the new setup question keeps the autostart entry of a portable copy

### Upgrade note

- Close every running copy of an older version before installing 1.6.0. The two versions guard the controller with differently named locks, so an old and a new copy switching at the same moment are not kept apart

## [1.5.0] - 2026-10-10

Aura Toggle now works entirely offline. The update check is gone; new versions are announced
on the releases page.

### Removed

- The update check and in-app updates. The tool no longer connects to the internet at all; new versions are on the releases page. Releases no longer carry `.sig` files, only the exes and `SHA256SUMS.txt`
- Upgrading from 1.4.x: the old version still announces 1.5.0 but reports it as not verified, since there is no signature any more. That notice is expected; download the new version from the releases page once, after that no further checks take place

## [1.4.1] - 2026-10-10

A steadier window: no more flashing when other programs open or close, while a preset is edited
or while a channel is renamed. A dynamic effect now takes every channel of its controller, which
is what the hardware does anyway, the tray icon stays put while the window is open, and the tray
menu got the same rounded look as the rest.

### Fixed

- On a second monitor at a different display scale than the main one, the channel rename popup showed its text field half covered by its buttons and the start of the name scrolled out of view. The colour picker's hex field had the same fault. Every popup, the settings panel and the preset editor included, now lays itself out for that monitor when it opens there
- The window, the big button most of all, could flash when another program opened or closed. Many programs announce a theme change as they do, even when nothing changed, and Windows answered each one by redrawing the window; such an empty announcement is now ignored
- Editing a preset made the big button flash on every change, as each live preview briefly showed the window as busy. The preview now works in the background without that
- Renaming a channel dimmed the big button while that channel blinked, as if the lighting had changed. The window now stays as it is while the name box is open

### Changed

- The channel list opens only as wide as its longest name, instead of at a fixed minimum width
- The tray icon's right-click menu has rounded corners and a rounded highlight, like the window's own lists
- The tray icon stays in the notification area while the window is open too, not only when it is minimised
- Picking one of the four dynamic effects (spectrum cycle, rainbow, rainbow breathing, wave) on a single channel now switches every channel of that controller to it, in the window too, instead of only the channels already running one; a channel that is off stays dark and joins the effect when it comes back on. A colour effect still changes only its own channel, and that channel keeps its colour while the others keep the dynamic effect
- The preset editor does the same: choosing a dynamic effect on one row sets every row of that controller to it, and a saved or imported preset with different dynamic effects on one controller opens showing the first channel's effect on all of them

## [1.4.0] - 2026-10-10

Updates are back, this time signed: the tool checks GitHub at most once a day and installs a
new version on one click, only when it carries the project's signature. Your own presets can now
be the lighting at start, autostart can set it and quit again, and the window keeps up with
anything the command line switched behind its back.

### Added

- Signed automatic updates: the program checks GitHub for a newer release at most once a day and installs it on one click, only when the download carries the project's signature; a "Check for updates" switch in the gear turns the check off
- "Lighting at start" offers your own custom presets too, and follows a preset when you rename it and falls back to leaving the lighting alone when you delete it
- "Apply and close the app": autostart only sets the start lighting and quits again, with no
  window and no notification area icon; a controller still coming up at logon is waited for
- A note under "Apply and close the app" says the hotkey only works while the app is running, shown when both are on
- With no controller the window now says why (not found, in use by another program, or not answering) and offers a "Try again" button
- Screen readers: the big button reads as a switch that is on or off, the brightness slider and the drop downs report their value; drop downs open on Alt+Down and F4 and step with Up and Down
- The notification area menu has an "Effects" submenu with the built-in effects and your custom presets
- Uninstalling while the tool is still running asks you to close it first, so the settings folder you chose to delete does not come back

### Changed

- Interface texts tightened in all ten languages: effect state and reset labels, the close switch ("Apply and close the app"), the close-to-tray switch, usage line with every command, a separate keyboard hint for channel rows, and "Aura" instead of "AURA"
- The big button no longer repaints 30 times a second while a static colour is showing
- The big button and the effect icons no longer take turns rebuilding the same gradient brush
- Looking for the controller no longer opens every keyboard, mouse and headset on the machine
- Update notice and effect names reworded in several languages

### Fixed

- Closing the channel rename popup after the controller went away no longer closes the whole app
- Renaming a channel, or reopening the window from the notification area, no longer leaves the window out of step with the channel selector
- In the effect list, Enter on a preset marked for deletion now deletes it instead of applying it; the arrow keys reach "New preset", and Ctrl+D duplicates the highlighted preset
- Saving a custom preset that cannot be written (disk full, file locked) now says so and keeps the editor open instead of pretending it worked
- Resetting settings no longer deletes anything when the backup of them cannot be written, and no longer replaces an older backup with a half-written one
- Picking a rainbow, spectrum or wave effect for one channel is no longer overridden by a different one of those left on another channel of the same controller
- Uninstalling removes your settings folder with your own account's rights, leaves it alone if it is a link, and the setup no longer counts a preview .NET runtime as installed
- Autostart with a controller that is not up yet at logon keeps looking for it for a minute and then applies the start lighting
- A hotkey combination another program already holds is refused and the previous one stays in place; at startup a taken hotkey is no longer saved as switched off
- The "Start with Windows" switch shows what was really stored when the Run key refuses the change
- Resetting everything now says so when a file could not be deleted because another program holds it
- A hand-edited or imported preset with a brightness outside the slider range is brought back into it
- A channel of a controller that is attached keeps its remembered look however long ago it was last switched
- Resetting everything keeps the lighting the board is running, so the window and the board stay in step
- A hand-edited start lighting that names nothing known is read as "leave the lighting alone"
- The update setup is checked again right before it runs, so it cannot be swapped in the temp folder in between
- An installed copy for all users asks for administrator rights itself before the update setup starts; saying no keeps the tool open instead of closing it
- An update is only installed when the signed file carries the version of its release, so an older build cannot come back as an update
- The window, the tray icon and the hotkey follow what a shortcut or script switched in the meantime, instead of switching the old state again
- Moving the brightness slider no longer lights up a channel you switched off, and the slider is hidden while the selected channels are off
- `-toggle -device N` switches a dark controller back on even while another controller is lit
- A window and a command line talking to the controller at the same moment wait for each other longer, so neither reports a missing controller
- A custom preset whose controller has a new device path is reported in the log at logon instead of being retried for a minute
- Update setups left in the temp folder are cleaned up after a day

## [1.3.0] - 2026-08-22

A quieter window (the brightness slider previews itself and steps aside for the effects it
cannot reach), a new icon, a setup that no longer trips over a per-user uninstall, and no
network code left in the tool at all.

### Added

- The brightness slider live-previews its effect's colour on the big button while you drag it
- A new icon: the colour ring on a transparent centre, redrawn at every size the taskbar, the
  notification area and the shortcuts use

### Changed

- Settings are reordered and "Animate button" is renamed to "Animate switch"
- The big button no longer flashes on a fast round trip through the busy state
- The brightness slider is only there for effects brightness can reach. Spectrum cycle, rainbow,
  rainbow breathing and wave are generated by the controller itself and cannot be dimmed, so the
  slider is hidden for them rather than sitting there doing nothing
- The setup's licence page is shorter, its privacy line is stronger, and its paragraphs now wrap
  to the width of the page instead of carrying the text file's own line breaks
- The running-light effects redraw with less work per frame, so the animated button costs less
  while they run

### Fixed

- A portable copy that was moved, renamed or duplicated no longer reads "Start with Windows" as on
  because some other copy's entry happens to still be in the registry: the switch now checks that
  the entry actually points at the copy asking
- Uninstalling a per-user install no longer hits an internal setup error partway through and skips
  the prompt to delete your settings
- A custom preset name that was already at the length limit can be duplicated without silently
  losing its "(2)" and colliding with the original
- A hand-edited or imported preset with an unrecognised effect number no longer falls back to a
  whole-board effect on a single channel
- A long custom preset name is shortened by the tool for the notification-area tooltip instead of
  being cut off mid-word by Windows
- An error message stays on screen long enough to be read and copied instead of disappearing
  together with the run that raised it

### Removed

- The update check, including its setting and its tray notification: the tool no longer talks to
  the network at all

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
  from Windows, and the first window is the wizard, which the setup does raise to the front.
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
- `-custom` takes a preset name the way `-preset` already takes an effect name: casing,
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
  panel: a mouse button, or a code no keyboard has. Such an entry falls back to Ctrl+Alt+L, as a
  missing modifier already did.
- Looking for controllers no longer leaves the window greyed out for good when the search itself
  fails, and a preview that fails unexpectedly reports it instead of ending the process.
- The release page a new-version notice links to is opened only when the address really is
  GitHub's, the same check the download beside it already had.
- A stored file that damage or hand-editing has blown up to megabytes is ignored like an
  unreadable one instead of being read into memory whole, and the update check turns down an
  oversized answer for the same reason.
- The preset editor's Save and Delete buttons keep the width their own label needs after the
  window is moved to a screen with a different scale (the longer German labels could be cut off),
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
a single portable executable: no background service, no driver, no admin rights.

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
- Channels can be given a name of your own. Hover one in the selector for a pencil. While you
  name it, that channel lights up red and the rest of its controller goes faint white, so there is
  no guessing which header is which.

**Custom presets**

- A custom preset holds one effect, colour and brightness *per channel* under a name of your own:
  the onboard zone steady white while an ARGB header breathes red, say.
- Presets are created, edited, duplicated and deleted straight from the effect drop-down. A new one
  starts from whatever is running right now, so saving the current look needs no changes at all;
  duplicating names the copy "<name> (2)" and opens straight into its editor.
- The editor shows what it is building on the real hardware and puts the lighting back the way it
  was if you close it without saving. Deleting asks once before it goes through.
- Export and Import move a single preset as one `.json` file, to another machine, a second Windows
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
  exists (otherwise it centres itself instead of opening off-screen) and at the width it last
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
  file it is about to delete (your own custom presets included) into `reset-backup.zip` first.
- Every stored file is written through a temporary file, so an interrupted save cannot leave a
  half-written one behind, and the window and a command line call cannot overwrite each other.

**Staying up to date**

- An update check, on by default: at most once every 24 hours the tool asks GitHub for the latest
  release. Finding a newer one adds a tray notice and, the first time the window is open to see it,
  a small popup with the same choice that closes itself after 30 seconds, either one offered once
  per version, not on every start.
- An installed copy can install the update once its checksum matches the release's own
  `SHA256SUMS.txt`; a portable copy is offered a link to the release page instead, since it cannot
  replace itself. Nothing is downloaded or run without a click.
- Nothing but the question for the latest release tag is ever sent, no machine identifier, no
  usage data. Turned off by setting `"checkUpdates": false` in `settings.json`; there is no switch
  for it in the window.

**Download**

- A portable executable of around 630 KB, and an installer of around 2.3 MB that can install for
  everyone or just for you, the latter without admin rights. When the .NET 10 Desktop Runtime is
  missing, the setup asks once and fetches it from Microsoft, verifying the download's Microsoft
  signature before it runs.
- The uninstaller names the actual files it is about to delete when it asks whether to remove your
  settings.
- `SHA256SUMS.txt` next to the release files.

### Known limitations

An effect and colour apply to a whole channel, never to a single LED. Spectrum cycle, rainbow,
rainbow breathing and wave come out of the controller's own firmware, which has one effect engine
per controller: set one of them on a single header and every header of that controller runs it,
and none of the four can be dimmed. Only mainboard lighting is covered: no GPU, RAM, fans or other
Aura Sync devices, and plain 12 V RGB headers get no channel of their own, only what the board
reports as its onboard zone is switched. The controller cannot report back which effect is
running, so the tool remembers what it last set.

[Unreleased]: https://github.com/wbgcoding/Aura-Toggle/compare/v1.6.0...HEAD
[1.6.0]: https://github.com/wbgcoding/Aura-Toggle/compare/v1.5.0...v1.6.0
[1.5.0]: https://github.com/wbgcoding/Aura-Toggle/compare/v1.4.1...v1.5.0
[1.4.1]: https://github.com/wbgcoding/Aura-Toggle/compare/v1.4.0...v1.4.1
[1.4.0]: https://github.com/wbgcoding/Aura-Toggle/releases/tag/v1.4.0
[1.3.0]: https://github.com/wbgcoding/Aura-Toggle/releases/tag/v1.3.0
[1.2.1]: https://github.com/wbgcoding/Aura-Toggle/releases/tag/v1.2.1
[1.2.0]: https://github.com/wbgcoding/Aura-Toggle/releases/tag/v1.2.0
