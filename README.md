<div align="center">

# 💡 Aura Toggle

**Switch your ASUS mainboard lighting off. Without Armoury Crate.**

One 540 KB executable · no install · no background service · nothing written to your board

[Download](#-download) · [Command line](#-command-line) · [Effects](#-effects) · [Is it safe?](#-is-this-safe-for-my-mainboard) · [Deutsch](README.de.md)

<img src="docs/preview-light.png" alt="The Aura Toggle window" width="360">

</div>

---

## 🌙 The problem

It is 1:30 in the morning. A download is running, so the PC stays on — and the case glows like
a jukebox. You want the LEDs off for the night.

Your options today:

| Option | What it costs you |
|---|---|
| Armoury Crate | Background service, auto-start, account, updater, hundreds of MB |
| BIOS | A reboot to switch off. Another reboot to switch back on |
| **Aura Toggle** | **One click. Delete the file when you are done** |

If you have ever thought *"I just want the LEDs off tonight, not a whole software suite"* — this
is for you.

## ✨ What it does

- 🔌 Switches **every** channel: onboard zone, 12 V RGB headers, all addressable ARGB headers
- 🎨 Nine built-in effects with colour picker, applied instantly
- 🖥️ One window: a button that **animates the running effect** and switches it
- 📌 Lives in the notification area, right-click for on/off
- ⌨️ Full command line with exit codes — scheduled tasks, scripts, shortcuts
- 🔒 No admin rights, no driver, no network, no telemetry
- 🇩🇪 🇬🇧 German and English

## 📥 Download

| | Size | Needs |
|---|---|---|
| **Portable** `aura.exe` | 540 KB | [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) |
| **Installer** x64 / ARM64 | 34 MB / 31 MB | Nothing — the runtime is inside |

Portable: download, double click, done. Installer: Program Files, optional autostart and
desktop shortcut, clean uninstall.

## 🚀 Using it

<img src="docs/preview-dark.png" alt="Dark mode" width="360" align="right">

1. **Big button** — shows the state and switches it. While the lighting is on it animates
   whatever effect is running.
2. **Drop down** — pick an effect, it applies immediately.
3. **Colour chips** — appear for effects that use a colour, including a free picker.
4. **⚙️ Gear** — autostart, start minimised, minimise instead of close, lighting at start,
   animation on/off, language.

Minimising sends the window to the notification area. Right click the icon there to toggle the
lighting, reopen or quit.

## ⌨️ Command line

```bat
aura                            REM opens the window
aura -off                       REM lighting off
aura -on                        REM back to the last effect
aura -preset rainbow            REM switch to an effect
aura -preset static "#20C0FF"   REM effect with a colour
```

`-on`, `--on`, `/on`, `on` — all accepted, any casing. Same for `off` and `preset`.

**Exit codes:** `0` ok · `2` bad argument · `3` no controller · `4` controller busy ·
`5` communication error. Errors go to stderr.

> ⚠️ **PowerShell** does not wait for windowed apps. For the exit code use
> `Start-Process aura.exe -ArgumentList "-off" -Wait -NoNewWindow`.

**Lights out at night, automatically:**

```bat
schtasks /create /tn "LEDs off" /tr "C:\tools\aura.exe -off" /sc daily /st 23:30
schtasks /create /tn "LEDs on"  /tr "C:\tools\aura.exe -on"  /sc daily /st 08:00
```

Two ready-made shortcuts, **Aura An** and **Aura Aus**, sit next to the executable. They carry
a relative path, so the folder can be moved anywhere.

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

- Windows 10 or 11, 64 bit or ARM64
- An ASUS mainboard with an Aura USB controller — X470 / Z390 generation onwards, including
  current AM5 and LGA1700 boards

Developed and verified on a **ROG STRIX Z790-E GAMING WIFI**. The controller is found by
talking to it, not by a model list, so unlisted ASUS boards of the same family should work.

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

Needs the .NET 10 SDK. Installers additionally need [Inno Setup 6](https://jrsoftware.org/isinfo.php).

```bat
build.bat            REM dist\aura.exe, 540 KB
build.bat standalone REM self contained, ~120 MB, no runtime needed
build.bat installer  REM dist\installer\Setup-AuraToggle-<version>-x64.exe
build.bat all        REM portable plus both installers
```

Regression suite — it switches the lighting while it runs and leaves it on afterwards:

```bat
powershell -ExecutionPolicy Bypass -File tests\aura-tests.ps1
```

## ⚙️ How it works

The controller is a USB HID device. Aura Toggle enumerates the HID interfaces, asks each
candidate for its firmware string and configuration table, and keeps the one that answers —
which is why no interface number is hardcoded. The configuration table gives the channel
layout; one effect command per channel does the rest.

Commands are paced and the sequence is sent twice: the controller silently drops commands that
arrive while it is still busy, which otherwise left the ARGB headers running after the onboard
zone had already switched.

State lives in `%LOCALAPPDATA%\aura-toggle` — `state.json` for the last effect, `settings.json`
for your preferences. Portable and installed builds share them.

## 📄 Licence and trademarks

MIT, see [LICENSE](LICENSE). The software comes **without any warranty**, and nobody is liable
for what it does on your machine.

This is an independent project. It is **not** made, endorsed or supported by ASUSTeK Computer
Inc. "ASUS", "ROG", "TUF" and "Aura" are trademarks of their respective owners, used here only
to describe which hardware this talks to. No ASUS software, driver or library is used, bundled
or required.
