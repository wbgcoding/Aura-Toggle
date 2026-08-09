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
- A hint row under the effect list, shown whenever a single channel is selected: the four
  effects the controller generates itself - spectrum cycle, rainbow, rainbow breathing and wave -
  still spread to every channel of that controller, one engine shared between all of them.
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
- A themed error dialog, replacing the plain message box, with the exception details in a
  collapsible area and buttons to copy them or open the log folder.
- A log at `%LOCALAPPDATA%\aura-toggle\log.txt`: start, version and errors, rotated to
  `log.old.txt` once past 200 KB.
- "Reset settings" in the settings panel: deletes the stored preferences and remembered state and
  brings the window back to first-run defaults without a restart.
- The channel rename popup blinks the channel being renamed and holds every other channel of
  that controller dark, so the right header is obvious - restored to its own recorded look the
  moment the popup closes.
- The tray icon dims while the lighting is off.
- A global hotkey, Ctrl+Alt+L by default and configurable in settings, switches the whole board
  regardless of what is selected in the window. A combination already claimed by another
  program is reported in the settings panel instead of silently doing nothing.
- Command line: `-list` numbers every controller and channel, `-status` prints the current
  effect, colour, brightness and on/off per channel, `--version` prints the version number,
  `-custom <name>` applies a saved custom preset, and `-device`/`-channel` target a single
  controller or channel for `-on`, `-off`, `-preset` and `-brightness`.
- `SHA256SUMS.txt` written next to the release artifacts, alongside the checksums the build
  already printed.
- `-review layout` opens the main window against stand-in controllers and reports every
  measurement its width depends on, so a "cut off at 150 %" report can be reproduced and proved
  fixed at any display scale without a controller attached.
- `-help` (also `-h`, `--help` and `/?`) prints every command and option with an explanation,
  the accepted effect and colour formats, the exit codes and a few examples. Deliberately English
  whatever the interface language is, like `-list` and `-status`, since it is what gets pasted
  into a script or a bug report.
- The log says considerably more about hardware trouble. Every controller search records what it
  found and, when it found nothing, why: how many ASUS interfaces were present at all, how many
  looked like a candidate, and how many were busy, silent, unreachable or unusable. Controllers
  that were found are listed with their channel count, a write that times out or is refused says
  so, and an effect that fails to apply names the controller and how many channels it was
  carrying. Errors now carry the exception type and any inner cause, not just a bare message -
  "the handle is invalid" on its own never identified anything.

### Changed

- Deleting a custom preset from its editor now arms on the first click, like "Reset settings"
  does - the button asks for confirmation before a second click actually deletes it.
- The custom preset editor shows what it is building on the real hardware: every change to an
  effect, colour or brightness is applied straight away, and the lighting is put back to what it
  was the moment the editor closes without saving.
- Naming a channel now lights it steady red at full brightness with every other channel of that
  controller at a faint white, rather than blinking it. Toggling a channel in a loop ran into a
  dynamic lighting limit on some boards, where the colour command was dropped and the header
  never lit at all. Full off was tried for the other channels first; on boards where several RGB
  headers share one bus, an "off" neighbour could still catch stray colour from whichever header
  was actually driving it, so it no longer proved anything - a faint white neighbour stays
  visibly distinct from the red target either way.
- The brightness row stays visible with "all channels" selected even while a firmware-coloured
  effect is running. The board-wide value is real and is what the next dimmable effect will use,
  so hiding the row made a stored setting look like it did not exist.
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
- Autostart always opens straight into the notification area now; starting the tool by hand
  always shows the window.
- The executable is `AuraToggle.exe` (was `Aura Toggle.exe`), and the installer's output is
  `AuraToggle-Setup-<version>.exe` (was `Setup Aura Toggle v<version>.exe`). Both the portable
  and the installed build still keep their settings in the same `%LOCALAPPDATA%` folder either
  way - anyone with the old portable exe keeps everything already configured, but needs to
  recreate the two shortcuts once, since the old ones point at a file that no longer exists.

### Removed

- "Start minimised" in the settings panel - autostart now always starts minimised on its own.
- ARM64 support. The portable executable and the installer are x64 only again.

### Fixed

- Picking an effect or a colour while a single channel was selected also overwrote the
  board-wide look every untouched channel falls back to - dimming one header could silently
  redefine what a never-touched header comes up as the next time the whole board switches on.
- The channel rename popup's pencil could be clicked while a switch from elsewhere (the hotkey,
  the toggle button) was already in flight, racing two hardware sweeps against each other and
  then re-enabling every control mid-switch when the rename's own busy flag cleared.
- Deleting the active custom preset from its own editor updated the tray a step too early to
  see that it had just been cleared, leaving the deleted name showing until an unrelated action
  repainted it. Deleting the same preset from the effect drop down already did this correctly.
- A controller plugged in after the window found none at startup was never looked for again -
  the window stayed disabled until the app was restarted. Opening it back up from the
  notification area now checks again.
- The error dialog, and the colour picker's window size, used unscaled pixel positions for
  everything but their own painted geometry - both could crowd or clip their controls once the
  window itself had grown for a higher display scale.
- Selecting a single channel could make the main window visibly jump width and back: the hint
  row shown under the effect list only for a single channel is explanatory text, never a choice,
  but its length was still counted toward how wide the window needed to be.
- A right or middle click on the brightness slider dragged the value and could send a switch to
  the controller, the same as a left click.
- "Reset settings" left autostart on if it had been, since that switch lives in the registry and
  not in one of the files the reset otherwise clears.
- The command line silently ignored `-device`/`-channel` typed alongside `-custom`, `-list`,
  `-status` or `--version`, none of which use them - it now reports them as a usage error instead
  of doing nothing. Both flags also require their own leading dash again to be recognised, so a
  channel or preset genuinely named "device" or "channel" cannot be mistaken for the flag itself.
- The pencil on a custom preset in the effect list, clicked after the preset had just been
  deleted elsewhere, opened a blank "new preset" dialog instead of doing nothing.
- The explanatory hint row under the effect list (shown for a single channel, explaining that
  four of the effects still run across the whole controller) painted in the same colour as a real
  choice, reading as selectable when it never was.
- Switching the whole board on lit the onboard zone up to a dozen reports and roughly 200 ms
  before the last ARGB header caught up, which read as the board coming on in stages rather than
  at once. Every channel of a controller is now sent as one burst instead of one channel at a
  time, cutting the spread to a couple of reports.
- The channel-rename indicator never lit RGB headers past the first two: it turned every other
  channel off, then sent the target channel's red as its own, later operation - arriving well
  behind a run of other reports is the same pattern that silently drops a colour command on some
  boards (see the dynamic-lighting note above). Sent together in one burst now.
- The installer's two one-click Start menu shortcuts were always named in German, even on an
  English install. They follow the setup language now, like every other text in it.
- Closing the window with the preset editor still open left the editor's live preview on the
  board with nothing written down to describe it, so the next start showed a window that
  disagreed with the lighting. The board is handed back to its records before the window goes,
  the same way an interrupted channel rename already was.
- An effect list too long for the screen scrolls, but gave no sign that it did. It has a slim
  indicator on the right now, shown only when there is something to scroll.
- Saving a custom preset while its own live preview was still reaching the controller wrote the
  preset to disk and never applied it - the board kept the preview, the records kept the old
  look. The editor is a window of its own, so the Save button was never disabled while the main
  window was busy, which made this easy to hit: change a colour, click Save straight away.
- The lighting entry in the notification area described the whole board but switched whichever
  channel the window's selector pointed at. With a single, switched-off channel selected on a lit
  board it read "On", with a tick, and clicking it switched something on. It is board-wide now,
  the same blunt gesture as the hotkey, and its tooltip names the board's effect rather than the
  selected channel's.
- The custom preset editor came back to its 100 % width every time an effect was changed on a
  display scaled above 100 %, cutting off the colour chips. The panel width, the room reserved
  for the big switch, and the limits on the channel and effect selectors are all measured against
  the display the window is actually on now, rather than assuming 96 dpi.
- The hotkey in the settings panel is written the way the key is printed on the keyboard -
  `Ctrl+Alt+1`, `Ctrl+Alt+,` - instead of the internal names `D1` and `Oemcomma`, and Windows is
  asked for the name so it follows the keyboard layout and its language.
- A crash in the very first two lines of startup - reading `settings.json`, opening the log -
  showed the raw .NET crash box instead of this tool's own error dialog.
- The brightness slider sent a full pass over the controller even when a click left the value
  exactly where it already was.
- A hand-edited `settings.json` carrying a hotkey with modifiers but no key at all was accepted,
  failed to register, and switched the setting back off without explanation.
- An abandoned file lock - the previous holder killed mid-write - could crash instead of being
  taken over, and a failure to acquire it leaked the handle.
- The log timestamp is written in a fixed format rather than the Windows display language's, so
  two logs from differently configured machines can be read side by side.
- The colour picker's hex field was the one input in the program still wearing the system border
  and the system font; in dark mode that showed as a pale box around a dark field. It also has a
  screen-reader name now.
- The downloaded .NET runtime installer's signature check matched the publisher name anywhere in
  the certificate subject; it is anchored on the organisation field itself now.
- Saving a custom preset in its editor did not actually apply it: the board kept showing the
  live preview while the window, `state.json` and the per-channel record still described the
  previous look, until the next unrelated action snapped everything back.
- Deleting the active custom preset from its own editor's delete button, rather than the effect
  drop down, left its name showing as though it were still running.
- An unhandled exception on a background thread exited the tool silently instead of showing the
  error dialog - a fatal error is now shown regardless of which thread it came from.
- The custom preset editor's live preview and a switch from the main window could reach the
  controller at the same time and interleave.
- Renaming a second channel before the first rename had finished restoring the previous one
  could send both to the same controller at once.
- Switching the interface language left the four settings switches (autostart, minimise on
  close, animate, hotkey) labelled in the old language until the panel was reopened.
- Hardware error messages were always in English regardless of the interface language.
- Popups and the custom preset editor sized themselves against the primary monitor's screen
  height rather than the one they actually opened on.
- The installer accepted any validly signed file, not specifically one from Microsoft, before
  installing the downloaded .NET runtime with elevated rights, and treated a same-named but
  empty or corrupted runtime folder as already installed.
- The uninstaller could delete the wrong user's settings folder when run elevated as a different
  administrator account than the one who had used the app - the autostart registry entry already
  avoided this, the data folder did not.
- Two sessions of the same Windows account (a physical logon and a Remote Desktop session, say)
  could each start their own copy of the tool and both address the same physical controller.
- The channel rename popup could leave a channel stuck lit if the window closed while it was
  blinking, and a cancellation race could dispose the wrong run's token source.
- Every light-switch action only caught two exception types; anything else vanished with no log
  or dialog. `state.json` had no read-modify-write lock, unlike the other stored files.
- The error dialog closed itself immediately (`Application.Exit` ran right before the user could
  read it), its detail text doubled every line break, and a fixed width overlapped its buttons
  in German.
- The global hotkey repeated while held down, and a hand-edited `settings.json` could register a
  bare key with no modifier, hijacking that key system-wide.
- A custom preset only reasserted the channels it named, not the rest of the controller it
  touched - a hand-edited or stale preset could leave an unrelated channel smeared with the
  wrong effect. The whole-board brightness slider now overrides every channel's own brightness,
  including ones a custom preset dims individually, instead of only the channels the preset names.
- The installer's autostart entry, and the uninstaller's cleanup of it, wrote into the wrong
  Windows profile when the setup was elevated with a different administrator account than the
  one actually using the app.
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
  exactly the executable, the two shortcuts and the setup, with no `.pdb`.
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
- The effect drop down marked the currently running effect as selected even while the pointer was
  somewhere else, so the highlight jumped between two rows depending on whether the mouse stayed
  exactly inside the list. It now marks only what the mouse is over, or - once an arrow key is
  used - the keyboard's own position.
- Deleting a custom preset from the effect drop down closed the whole list, so removing several in
  a row meant reopening it each time. The list now stays open and refills itself in place.
- The whole-board brightness slider, applied while a custom preset was active, dimmed the hardware
  correctly but the per-channel record kept the previous brightness - the change looked like it
  did nothing once the lighting was switched off and on again.
- A light/dark switch in Windows did not reach the controls inside the custom preset editor or the
  settings panel while either was open: their selects, colour strip and buttons kept the old
  palette's background until the window was reopened.
- Turning the global hotkey off in settings and then choosing "Reset" left the old combination
  still registered with Windows - only the setting itself had been cleared.
- The effect drop down did not follow the display's scale: at 150/200 % it opened at the right
  width but with rows, icons and buttons still sized for 96 dpi, crowding the text.
- The settings panel and the custom preset editor could grow past the bottom of the screen after
  they had already been positioned - switching the hotkey on, or picking an effect that adds
  colour chips - pushing their buttons under the taskbar.
- Closing the main window right after a switch or a preset save, before the write had reached the
  controller, could abandon it mid-flight; and closing it a moment after the preset editor itself
  had already closed skipped the hand-back to the last recorded look entirely.
- A hardware error partway through the channel-rename indicator left the affected channel dark
  with nothing restored - the record still described it as lit.
- Deleting the active custom preset while another process (a command-line switch, say) changed the
  lighting at the same instant could lose one of the two changes; the clearing write is locked now
  like every other one.
- A hardware error partway through switching several channels could leave the channels already
  sent unrecorded, even though they had already changed on the controller.
- The custom preset editor's channel headings looked the same as any other muted label, making it
  hard to tell at a glance which channel a block of controls belonged to on a board with several.
  They are bolder now, with a rule between one channel's block and the next.
- Colour chips, the colour picker's painted areas, the slider, the toggle switches and the
  effect list's own closed-state icon did not scale with the display either - a mix of the same
  defect as the drop down above, spread across every hand-painted control in the window.
- The main window stayed a fixed width regardless of language: with the channel selector shown,
  German effect names longer than roughly twenty characters were cut short where the English ones
  fit. The window now measures its top row and grows within limits.
- Quitting from the notification area while the very first hardware discovery was still running
  could still run the configured start action afterwards, touching a window already gone.
- The error dialog's "Details" section could not be expanded with the keyboard, only the mouse.
- The settings panel's two drop downs (start action, language) announced as unnamed to a screen
  reader; the switches next to them already had names.
- A language switch left the colour strip's screen-reader name, and the window title when no
  controller was found, in the previous language.
- Dragging any window - the main one, the settings panel or the custom preset editor - onto a
  second monitor with a different display scale left every measured size and position from the
  one it came from: undersized icons, controls sitting in the wrong place, buttons off the edge
  once it was dragged back. Every window now re-measures itself when the display scale changes.
- The lighting configured under "Lighting at start" is now only ever applied on a genuine
  Windows-startup launch, not every time the window is opened by hand - the setting itself is
  hidden while "Start with Windows" is off, since it can never fire without it.
- Dimming or recolouring a single channel could quietly redefine what a never-touched channel
  shows the next time the whole board is switched on; the effect and colour only move into the
  board-wide fallback when the whole board is the target.
- The channel-rename pencil could start while a switch was still in progress instead of waiting
  for it, like every other action that talks to the controller.
- Deleting the active custom preset from its own editor updated the tray name one action later
  than deleting the same preset from the drop down.
- A controller plugged in after the window had already started with none present was never
  looked for again, leaving the window's controls locked for the rest of the session. It looks
  again when the window is brought back from the notification area.
- The error dialog and the colour picker ignored the display scale for their window size, unlike
  the rest of the window.
- Selecting a single channel could make the main window visibly jump width: a hint line that is
  never itself selectable was still being counted while measuring how wide the window needed to
  be.
- Right- or middle-clicking the brightness slider dragged its value and switched the lighting;
  only the left button does that now.
- "Reset settings" left "Start with Windows" switched on, since that entry lives in the registry
  rather than one of the files it clears.
- `-device`/`-channel` next to `-custom`, `-list`, `-status` or `--version` on the command line
  were silently ignored, which read as if they had worked - they are now reported as a usage
  error. Both flags also require their leading dash again, so a channel or preset that happens to
  be named "device" or "channel" is no longer mistaken for the switch itself.
- The rename pencil on a custom preset that had just been deleted opened a new, empty preset
  instead of doing nothing.
- The hint row under the effect list was the same colour as a real, selectable entry.
- The window was cut off on the right, worst at higher display scales: it worked out its width
  while the channel selector was still hidden and never measured again once the controller was
  found, so it stayed at its minimum with the gear pushed past the edge.
- Nothing that makes up the window frame grew with the display scale - the gear stayed 30 px, the
  window padding 16 px and every gap between the rows unchanged - next to text that did grow. At
  150 % and 200 % that read as a cramped window with a shrunken gear in the corner.
- The window is now pulled back into the screen after it grows. It is centred at its starting
  size, so on a small screen at a high display scale it could widen straight off the right-hand
  edge.
- The signature check on the downloaded .NET runtime can no longer stall the setup: its online
  certificate-revocation lookup had no timeout of its own, so a network that drops those packets
  rather than refusing them would leave the installer waiting indefinitely. It is now bounded, and
  a check that cannot finish counts as a failed one rather than a passed one.
- `build.bat` used Unix line endings, which cmd.exe's label lookup handles unreliably - the build
  could report "Sprungziel maybepause wurde nicht gefunden" and skip its own last step.
- The published executable carried the absolute path it was built from, symbol file included.
- Nothing in the window scaled with the display except the text: the gear, the switches, the drop
  downs, the sliders, every rounded corner, the glyphs on the small buttons and the padding of
  the settings panel and the preset editor all stayed at their 96 dpi size. At 150 % and 200 %
  that showed as shrunken controls beside full-size labels, a text field shorter than its own
  caret, and a pencil glyph filling the button it sat in.
- Dismissing the channel-rename popup while a switch was still running could leave the tool
  locked up: the identify request that was waiting its turn could no longer be cancelled, so it
  started anyway, held every control disabled and left the header lit red until the window was
  closed.
- Bringing the window back from the notification area while the very first controller search was
  still running started a second search on top of it, and the two then talked over each other.
- A hardware failure on the only controller was reported as success and saved as though the
  lighting had changed.
- Pressing the global hotkey with no controller present re-enabled the effect list, colour chips
  and brightness slider behind a disabled button, so each of them raised another error.
- A failure while lighting a channel for renaming that was neither a missing controller nor an
  I/O error vanished without reaching the log or the user.
- `-preset` only accepted an effect's name in the language the interface happened to be set to,
  so a script written in German stopped working after switching to English. Both are accepted
  now, as `-channel` already did.
- The colour picker rebuilt its whole saturation/brightness square on every mouse movement while
  the hue strip was being dragged, which is what made the drag stutter.
- Records in `channel-state.json` are dropped once nothing has written to them for 30 days, so
  the file no longer keeps a row for every header of every controller ever plugged in. Records
  from an older version start their 30 days at the first run of this one.
- The channel-rename popup and its text field announced as unnamed to a screen reader.
- Upgrading over the 1.0.0 installer left its `aura.exe` and two Start-menu entries behind, and
  an autostart entry written by that version kept starting the old executable.
- Moving the window to a second monitor could still cut its top row off on the right, gear and
  all - at any display scale, not just a mismatched one. The row holding the effect list, channel
  selector and gear was an auto-sizing panel with the effect list filling whatever was left of
  it; auto-size turns a panel's own computed width into a floor its content is never laid out
  narrower than, and that floor kept the effect list's widest size seen so far even after a
  display change asked for something narrower - the window itself resized correctly, the row
  inside it didn't shrink to match. The row no longer auto-sizes its width, only ever taking
  exactly what the window gives it.
- Fixing the above the same way broke the row's height instead: without auto-sizing, the panel
  above the toggle button stopped reporting its true height to the layout holding it and settled
  on a guess nearly three times too tall, most visible right after switching a preset, which
  starved the toggle button down to a sliver. The row's true height was never actually wrong -
  querying it directly still gave the right number - only the automatic hand-off of that number
  to its parent was. The parent now reads the real number itself instead of asking the layout
  engine to infer it.
- The colour command is no longer sent for the four effects the firmware colours itself
  (spectrum cycle, rainbow, rainbow breathing, wave), where it did nothing. Two fewer reports per
  such channel, which is time the board would otherwise spend coming on in stages.
- The portable executable is built without ReadyToRun precompilation now, shrinking it from
  ~950 KB to ~580 KB, the installer to ~2.3 MB with it. ReadyToRun only trades size for a faster
  JIT warm-up, and that warm-up was already hidden behind the hardware scan the window runs on
  every launch - nothing about startup time or behaviour changed.
- "Reset settings" deleted custom presets along with the actual preferences, even though the
  confirmation only ever said "settings" - a preset is content the user built, not a default to
  fall back to, and it now survives a reset like the log already did.
- The installer's own "Launch Aura Toggle" step could open the window behind whatever else was on
  screen instead of in front. It runs the app as the original, non-elevated user so the app does
  not inherit the installer's admin rights, but that hand-off does not carry the
  foreground-activation right an elevated Setup.exe normally passes straight to what it launches.
  Granted explicitly now, right before the wizard's Finished page.
- The top row could still end up a few pixels short of what it measured for after a live
  display-scale change - moving the window to a second monitor with a different scale and back -
  even after the fix above for the same complaint. WinForms rescales fonts and control bounds
  through its own rounding on that event, which does not always agree with this window's own to
  the exact pixel. The window now re-measures the same way `-review layout` does and grows to
  match whenever anything still comes up short, instead of only logging that it did.
- A code-quality pass over the whole project found and fixed a run of smaller issues, none of them
  ever reported, most never observable without an unlucky timing window or a hand-edited file:
  - A HID read or write that timed out could dispose its own cancellation source twice if the
    controller answered at almost the exact same moment - both places now dispose it exactly once.
  - An out-of-memory moment during device discovery could leak an unmanaged handle instead of
    freeing it.
  - Opening the controller reported "busy" for any failure to open it, not only when another
    program genuinely already had it open - a device unplugged between being found and being
    opened now says so instead of pointing at a program that was never running.
  - The internal check that limits which commands are ever allowed to reach the controller only
    ever guarded the write path, not the two read commands sent during the handshake - harmless in
    practice (both are on the allowed list already) but now closed so a later change cannot widen
    it without the same check catching it.
  - Saving or deleting a custom preset skipped the file lock every other stored file's
    read-modify-write already takes - unlikely to matter today, since nothing but the window
    itself currently writes `presets.json`, but now consistent with the rest.
  - A hand-edited `channel-state.json` could carry an out-of-range per-channel brightness that
    reached `-status` unclamped, unlike the board-wide value.
  - The error dialog could not be dragged out of the way, unlike the other popup that also stays
    open rather than closing on an outside click.
  - Dragging the effect list's drop down or the channel rename popup to a display with a different
    scale left its size and hit-testing stuck at the scale it opened on - the colour picker already
    handled this, the other two now do too.
  - The rainbow and wave effect icons reuse one gradient brush instead of allocating a new one on
    every animation frame, the same fix already applied to their colour data a while back.
  - A missing translation now falls back to English consistently everywhere a lookup can miss, not
    just in most places.
- A UI pass over every window and popup found a run of places that stayed a fixed 96 dpi size while
  everything around them grew with the display - each individually easy to miss, since none of them
  broke the layout, they just drifted further from the rest of the window at higher scales:
  - The keyboard-focus ring drawn around a button, drop down or switch, the slider's own focus ring
    and hover outline, and the colour picker's saturation/hue markers all used a flat pen width
    regardless of display scale, thinning out relative to everything else on a high-dpi screen.
  - The small × on the discard button in the custom preset editor did the same.
  - Four of the five places that open a popup next to the control that triggered it - the preset
    editor, the channel rename popup, the settings gear, the effect drop down - measured the gap
    in fixed pixels; only the colour picker already scaled it. The gap between a control and its
    popup now grows with the display like the controls themselves do.
  - The colour picker's hex field crept a couple of pixels off centre from the swatch beside it at
    anything above 100%.
  - The custom preset editor's per-channel effect drop down was sized in fixed pixels instead of
    through the same scaled-height property every other row in the app uses, so it stayed a fixed
    height while its own text grew.
  - The margins between rows and controls throughout the settings panel and the custom preset
    editor were fixed pixel gaps - correct at 100%, proportionally tighter at every scale above it,
    while the fonts and controls next to them grew normally.
- The uninstaller's "keep or delete my settings" step could be pointed at an arbitrary folder
  named `aura-toggle` by whichever user's desktop session it runs from, since it read that user's
  own `LOCALAPPDATA` environment variable - which any standard account can freely repoint - without
  checking it actually looked like a real profile folder first. Only reachable when a different
  administrator elevates the uninstaller on someone else's session and confirms the deletion by
  hand, and only ever deletes a folder that already exists and is already named exactly
  `aura-toggle`, but the check costs nothing and closes it outright: the value is now accepted only
  when it has the shape `<system drive>\Users\<name>\AppData\Local`, and Setup's own safely-resolved
  folder is used otherwise.

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
