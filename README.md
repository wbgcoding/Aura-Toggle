<div align="center">

# 💡 Aura Toggle

**Turn off ASUS RGB — without Armoury Crate.**

One executable · no install · no background service · nothing written to your board

[Download](#-download) · [Command line](#-command-line) · [Effects](#-effects) · [Is it safe?](#-is-this-safe-for-my-mainboard) · [Deutsch](README.de.md)

<img src="docs/preview-dark.png" alt="The Aura Toggle window" width="360">

</div>

---

| | Aura Toggle | Armoury Crate |
|---|---|---|
| Size | ~580 KB | Hundreds of MB |
| Background service | None | Always running |
| Account | None | Sign-in required |

**Quickstart:**

1. Download `AuraToggle.exe` below (or the installer)
2. Run it — no setup, no admin rights
3. Click the button

---

## 🌙 The problem

RGB lighting on a mainboard is usually all-or-nothing: it either runs whatever effect the BIOS
left it in, or it runs whatever the vendor's control suite last set. Turning it off for a while
— a dark room at night, a long render, a stretch without needing the show — normally means one
of two routes:

| Option | What it costs |
|---|---|
| Armoury Crate | Background service, auto-start, account, updater, hundreds of MB |
| BIOS | A reboot to switch off, another reboot to switch back on |
| **Aura Toggle** | One click. Delete the file when you no longer need it |

Aura Toggle exists for the case where none of that is worth it for flipping one switch.

## ✨ What it does

- 🔌 Switches **every** channel: onboard zone, 12 V RGB headers, all addressable ARGB headers
- 🎯 Or **one channel at a time** — the onboard zone steady white while an ARGB header breathes red
- 🎨 Nine built-in effects with a colour picker, applied instantly
- 🔆 **Brightness** for the colour effects, 10 – 100 %, per channel or for the whole board
- 🧩 **Custom presets** — a name, one effect and colour per channel, saved and reusable
- 🖥️ One window: a button that **animates the running effect** and switches it
- 📌 Lives in the notification area, right-click for on/off
- ⌨️ Full command line with exit codes — scheduled tasks, scripts, shortcuts
- 🔥 A global hotkey, configurable, switches the whole board from anywhere
- 🔒 No admin rights, no driver, no network, no telemetry
- 🇩🇪 EN German and English, switchable independently of Windows

## 🚫 What it can't do

- No speed or direction for the running-light effects — the controller has no such setting
- No individual LED control — an effect and colour apply to a whole channel, not one LED
- Only one dynamic effect per controller — spectrum cycle, rainbow, rainbow breathing and wave
  run across every channel of a controller at once, not one at a time
- Only mainboard lighting — no GPU, RAM, fans or other Aura Sync devices

## 📥 Download

| | Size | Needs |
|---|---|---|
| **Portable** `AuraToggle.exe` | ~580 KB | [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) |
| **Installer** | ~2.3 MB | Nothing — it fetches the runtime if you lack it |

Portable: download, double click, done. Installer: for everyone or just for you, optional
autostart and desktop shortcut, clean uninstall. If the .NET 10 Desktop Runtime is missing it
asks once, downloads it from Microsoft and installs it — no 60 MB bundle in every download.

## 🚀 Using it

<img src="docs/preview-light.png" alt="Light mode" width="360" align="right">

1. **Big button** — shows the state and switches it. While the lighting is on it animates
   whatever effect is running, brightness included.
2. **Drop down** — pick a built-in effect or a saved custom preset, it applies immediately.
   The last row creates a preset; each preset of your own carries a ✏️ to edit it and an ✕ to
   delete it, which asks once more before it does.
3. **Channel selector** — all channels, or a single one: the onboard zone, one ARGB header, or
   one whole controller on boards that have several. Hover a channel for a ✏️ to give it a name
   of your own.
4. **Colour chips** — appear for effects that use a colour, including a custom colour picker.
5. **Brightness** — appears with the chips, 10 to 100 %, and follows the channel selector: dim
   one header on its own, or set the whole board at once, which hands every channel back to the
   board-wide value.
6. **⚙️ Gear** — autostart, minimise instead of close, lighting at start, a global hotkey,
   animation on/off, language, open the log folder, reset everything back to first-run defaults.

Minimising sends the window to the notification area. Right click the icon there to toggle the
lighting, reopen or quit.

### Custom presets

A custom preset bundles one effect and colour **per channel** under a name of your own — the
onboard zone steady white while an ARGB header breathes red, say. Create one from the last row
of the effect drop down: name it, then an effect and colour per channel, save. Every channel
starts out matching whatever is running right now, so a preset that should just save the
current look needs no changes at all. It then shows up in the effect list next to a small
person icon, told apart from the built-in effects at a glance. Each channel also carries its own
brightness there, so a preset can hold one header at 30 % and the next at full. The editor has no
title bar of its own but can be dragged by its heading, so it never sits in the way.

## ⌨️ Command line

```bat
AuraToggle.exe                            :: opens the window
AuraToggle.exe -off                       :: lighting off
AuraToggle.exe -on                        :: back to the last effect
AuraToggle.exe -preset rainbow            :: switch to an effect
AuraToggle.exe -preset static "#20C0FF"   :: effect with a colour
AuraToggle.exe -brightness 40             :: dim the colour effects, 10 to 100
AuraToggle.exe -custom "Movie Night"      :: apply a preset saved in the window
AuraToggle.exe -list                      :: number every controller and channel
AuraToggle.exe -status                    :: current effect, colour, brightness, on/off
AuraToggle.exe --version                  :: version number, nothing else
AuraToggle.exe -help                      :: every command, explained
```

`-on`, `--on`, `/on`, `on` — all accepted, any casing. Same for `off`, `preset`, `brightness`,
`custom`, `list`, `status`, `version` and `help` (also `-h` and `/?`). Creating a custom preset still only happens in the
window - applying an existing one does not.

**One channel or controller**, with `-on`, `-off`, `-preset` and `-brightness`:

```bat
AuraToggle.exe -preset static red -channel 2       :: by the number from -list
AuraToggle.exe -preset static red -channel 1.2     :: controller 1, channel 2
AuraToggle.exe -preset static red -channel "ARGB 1" :: by its default or renamed name
AuraToggle.exe -on -device 1                       :: every channel of controller 1
```

`-channel` accepts a flat number from `-list`, the `<controller>.<channel>` form, the default
name in either language, or a name given in the window - matched the same forgiving way as preset
names (casing, spaces and hyphens ignored). Unknown or ambiguous exits `2` and lists the possible
targets on stderr. `-list` and `-status` are always in English, regardless of the window's
language, so a script reading them does not break when someone switches it; error messages stay
translated.

**Exit codes:** `0` ok · `2` bad argument · `3` no controller · `4` controller busy ·
`5` communication error. Errors go to stderr.

> ⚠️ **PowerShell** does not wait for windowed apps. For the exit code use
> `Start-Process AuraToggle.exe -ArgumentList "-off" -Wait -NoNewWindow`.

**Lights out at night, automatically:**

```bat
schtasks /create /tn "LEDs off" /tr "C:\tools\AuraToggle.exe -off" /sc daily /st 23:30
schtasks /create /tn "LEDs on"  /tr "C:\tools\AuraToggle.exe -on"  /sc daily /st 08:00
```

Two ready-made shortcuts, **Aura On** and **Aura Off**, sit next to the executable in the portable
download. They carry a relative path, so the folder can be moved anywhere. The installer does not
add them — it puts the application in the Start menu and nothing else.

## 🎨 Effects

| Name | Looks like | Colour |
|---|---|---|
| `static` | One steady colour | ✅ |
| `breathing` | Fades in and out | ✅ |
| `flashing` | Blinks | ✅ |
| `spectrum-cycle` | All LEDs cycle the spectrum together | — |
| `rainbow` | Gradient travelling across the LEDs *(ASUS default)* | — |
| `rainbow-breathing` | Spectrum cycle that fades | — |
| `chase-fade` | Running light with a fading tail | ✅ |
| `chase` | Running light | ✅ |
| `wave` | Slow spectrum drifting across the strip | — |

Names are forgiving: casing, spaces, hyphens and underscores are ignored, and the translated
names work too. An unknown name prints the list.

> There is **no speed or direction** setting. The controller has none — its effect command
> carries a channel and a mode, nothing else.
>
> **Brightness** works by scaling the colour that is sent, so it applies to the five effects
> marked ✅ above. The other four are generated inside the controller's own firmware, which
> takes no colour and no brightness — nothing can dim those short of switching them off.
>
> Effects can be **mixed** across channels — one header steady red while the next one breathes —
> but only for the five colour effects. The other four are one effect engine inside the
> controller, shared by all of its channels: set the rainbow on a single header and every header
> of that controller runs it. The window still offers all nine with a single channel selected, but
> flags it with a hint rather than letting the choice quietly spread.

## 🔒 Is this safe for my mainboard?

**Yes**, and the reason matters.

The Aura controller keeps its configuration in its own flash, and that flash is what your board
applies at power-on. Aura Toggle **never sends the command that writes to it**. Only volatile
effect commands, which live in the controller's RAM.

- ✅ Your BIOS lighting settings stay untouched
- ✅ After a reboot the lighting is back, even if you shut down with it off
- ✅ Uninstalling means deleting one file
- ✅ No kernel driver, no admin rights — it is a standard USB HID device

## 💻 Requirements

- Windows 10 or 11, 64 bit
- An ASUS mainboard with an onboard Aura USB controller (most ASUS boards with Aura Sync or
  addressable RGB headers have one, going back several chipset generations)

Developed and verified on an **ASUS Z790 mainboard**. The controller is found by talking to it
directly, not by a model list, so it either works or reports no controller found — see
[Troubleshooting](#-troubleshooting).

## 🛠️ Troubleshooting

**"No AURA LED controller found"**
No Aura USB controller on the board, or lighting is off in the BIOS. Look for hardware id
`USB\VID_0B05` under Human Interface Devices in Device Manager.

**"The AURA LED controller is in use by another program"**
Armoury Crate, OpenRGB or SignalRGB hold it open. Close them — two programs cannot drive the
same controller.

**The lighting comes back looking different**
The controller cannot report which effect is running, so the tool remembers what it set last.
On the very first switch-on it falls back to the ASUS rainbow. Reboot, or just pick the effect
you want.

## 🔨 Building

Needs the .NET 10 SDK. The installer additionally needs
[Inno Setup 6](https://jrsoftware.org/isinfo.php).

`build.bat` at the root of the repository is the whole build. Run it with no argument, or double
click it, and it produces everything a release consists of:

```bat
build.bat                REM everything: portable exe, installer, checksums
build.bat portable       REM only the portable x64 exe and its two shortcuts
build.bat installer      REM only the setup
```

It empties `dist\` first, so what is left there afterwards is exactly the release: `AuraToggle.exe`,
the `Aura On` / `Aura Off` shortcuts, `AuraToggle-Setup-<version>.exe` and `SHA256SUMS.txt`. The
version comes from the project file, not from anything typed twice. Set `NOPAUSE=1` to run it
from a script without the closing keypress.

The single command behind the portable build, if you would rather not use the script:

```powershell
dotnet publish AuraToggle.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o dist
```

For the Inno Setup installer, pack `installer\aura.iss` with `ISCC.exe` (needs
[Inno Setup 6](https://jrsoftware.org/isinfo.php)).

Regression suite — it switches the lighting while it runs and leaves it on afterwards:

```bat
powershell -ExecutionPolicy Bypass -File tests\aura-tests.ps1
```

## ⚙️ How it works

The controller is a USB HID device. Aura Toggle enumerates the HID interfaces, asks each
candidate for its firmware string and configuration table, and keeps the ones that answer —
which is why no interface number is hardcoded, and why more than one controller is found on
boards that have them. The configuration table gives each controller's channel layout; one
effect command per channel does the rest.

Commands are paced and the sequence is sent twice: the controller silently drops commands that
arrive while it is still busy, which otherwise left the ARGB headers running after the onboard
zone had already switched.

State lives in `%LOCALAPPDATA%\aura-toggle` — `state.json` for the last effect and brightness,
`settings.json` for your preferences, `presets.json` for custom presets, `channel-state.json`
for what each channel was last set to including its own brightness, `channel-names.json` for
channels you renamed, and `log.txt` (rotated to `log.old.txt` past 200 KB) for start-up, version
and error entries. Portable and installed builds share them, every write goes through a
temporary file so an interrupted save cannot corrupt one, and uninstalling offers to delete the
folder.

## 📄 Licence and trademarks

[MIT](LICENSE): free to use privately or commercially, pass on and change, no strings attached
beyond keeping the copyright notice with it. The software comes **without any warranty**, and
nobody is liable for what it does on your machine.

This is an independent project. It is **not** made, endorsed or supported by ASUSTeK Computer
Inc. "ASUS", "ROG", "TUF" and "Aura" are trademarks of their respective owners, used here only
to describe which hardware this talks to. No ASUS software, driver or library is used, bundled
or required.
