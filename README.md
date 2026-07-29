# Aura Toggle

**One button. Your ASUS mainboard LEDs go dark. No Armoury Crate, no background service, no install.**

*Deutsche Version: [README.de.md](README.de.md)*

---

## The problem this solves

It is half past one in the morning. A large download is running, a render is finishing, a
backup is crawling through the night — and your PC has to stay on. So you go to bed, and the
machine sits there glowing like a jukebox. The onboard RGB pulses across the ceiling, the
strip behind the case throws colour on the wall, and the room never gets properly dark.

The obvious fix is to turn the lighting off. The not-so-obvious part is what that costs you:
the official way is to install a full RGB suite, which brings a permanent background service,
an auto-start entry, a login account, an updater and a couple of hundred megabytes — all so
that you can occasionally set one value to zero. Plenty of people would rather keep the LEDs
than install all of that. So the lights stay on, every night, forever.

The BIOS can switch the lighting off too, but only if you reboot into it, and then it stays
off until you reboot again. That is not a light switch. That is a ritual.

**Aura Toggle is the missing light switch.** One portable 300 KB executable. Run it, the
lighting goes out. Run it again, the lighting comes back exactly as it was. Nothing is
installed, nothing runs in the background, nothing is written to your mainboard permanently.
When you are done, delete the file and no trace of it remains.

If you have ever thought *"I just want the LEDs off for tonight, not a whole software suite"* —
this was written for exactly that.

## What it does

- Switches **all** channels of the Aura controller: the onboard zone, the 12 V RGB headers
  and every addressable ARGB header.
- Restores the last effect when you switch back on.
- Lets you pick one of the controller's built-in effects, from the window or the command line.
- Works from the command line, so you can put it in a scheduled task, a shortcut or a script.
- Runs without administrator rights.
- Speaks German and English, following your Windows display language.

## What it deliberately does not do

No colour picker, no custom animations, no profiles, no tray icon, no auto-start, no updater,
no telemetry, no network access at all. It switches the lighting and selects one of the
effects the controller already has. That is the whole feature list, and it is meant to stay
that way.

## Getting started

1. Download `aura.exe` and put it anywhere you like — Desktop, a tools folder, a USB stick.
2. Double click it. A small window opens with a big button showing the current state, and
   below it a drop down with the available effects plus a **Set** button.
3. Click the big button to switch the lighting. Pick an effect and press **Set** to change it.

That is the entire setup.

### Command line

| Command | Effect |
|---|---|
| `aura` | Opens the toggle window |
| `aura -off` | Lighting off |
| `aura -on` | Lighting back to the last effect |
| `aura -preset <name>` | Switches to that effect and turns the lighting on |

`-on`, `--on`, `/on` and plain `on` are all accepted, in any capitalisation. Same for `off`
and `preset`.

### Effects

| Name | What it looks like | Uses a colour |
|---|---|---|
| `static` | One steady colour | yes |
| `breathing` | Fades in and out | yes |
| `flashing` | Blinks on and off | yes |
| `spectrum-cycle` | All LEDs cycle through the spectrum together | no |
| `rainbow` | Colour gradient travelling across the LEDs — the ASUS default | no |
| `rainbow-breathing` | Spectrum cycle that fades in and out | no |
| `chase-fade` | Running light with a fading tail | yes |
| `chase` | Running light | yes |
| `wave` | Wave travelling across the LEDs | no |

Names are matched forgivingly: capitalisation, spaces, hyphens and underscores are ignored, so
`spectrum-cycle`, `"Spectrum Cycle"` and `spectrumcycle` all work. The translated names shown
in the window are accepted too. An unknown name prints the full list.

The effects marked as using a colour run in white. There is no colour picker — this tool is
about switching lighting, not designing it.

Exit codes: `0` success, `2` unknown argument, `3` no controller found, `4` controller in use
by another program, `5` communication error. Errors are printed to standard error, so scripts
can react to them.

> **PowerShell note:** `aura.exe` is a windowed application, and PowerShell does not wait for
> those. If you need the exit code, use
> `Start-Process aura.exe -ArgumentList "-off" -Wait -NoNewWindow`.

### Turn the lights off automatically at night

Windows Task Scheduler, one task, no extra software:

```bat
schtasks /create /tn "LEDs off" /tr "C:\tools\aura.exe -off" /sc daily /st 23:30
schtasks /create /tn "LEDs on"  /tr "C:\tools\aura.exe -on"  /sc daily /st 08:00
```

## Requirements

- Windows 10 or Windows 11, 64 bit.
- An ASUS mainboard with an Aura USB lighting controller. Boards from roughly the X470 and
  Z390 generation onwards use one; recent AM5 and LGA1700 boards do as well.
- The .NET 10 Desktop Runtime for the small build. If you prefer zero prerequisites, use the
  standalone build instead — it is much larger but needs nothing installed.

Developed and verified on a ROG STRIX Z790-E GAMING WIFI. The tool identifies the controller
by talking to it rather than by a fixed model list, so unlisted ASUS boards with the same
controller family are expected to work.

## Is this safe for my mainboard?

Yes, and the reason is worth understanding.

The Aura controller keeps its lighting configuration in its own flash memory, and that flash
is what the mainboard uses at power-on. Aura Toggle **never** sends the command that writes to
that flash. It only sends volatile effect commands, which live in the controller's RAM.

The practical consequences:

- Your BIOS lighting settings stay exactly as you left them.
- After a reboot the lighting is back on, even if you shut down with it switched off.
- Uninstalling means deleting one file.

The tool also does not load a kernel driver and does not need administrator rights — the
controller is a standard USB HID device, so ordinary user permissions are enough.

## Troubleshooting

**"No AURA LED controller found"**
Your board may not have an Aura USB controller, or lighting is disabled in the BIOS. Check
Device Manager for a device with hardware id `USB\VID_0B05` present under Human Interface
Devices.

**"The AURA LED controller is in use by another program"**
Armoury Crate, OpenRGB, SignalRGB and similar tools hold the controller open. Close the other
program first — two programs cannot drive the same lighting controller at once.

**The lighting comes back looking different**
The controller cannot report which effect is currently running, so Aura Toggle remembers the
effect it last applied. The first time you ever switch on, it falls back to the ASUS default
rainbow effect. Reboot once and your BIOS setting is back, or pick the effect you want from
the drop down.

## Building from source

Requires the .NET 10 SDK.

```bat
build.bat
```

The result is `dist\aura.exe`. For a build that runs without the .NET runtime installed:

```bat
build.bat standalone
```

There is a regression suite in `tests\`. It switches the lighting while it runs and leaves it
turned on afterwards:

```bat
powershell -ExecutionPolicy Bypass -File tests\aura-tests.ps1
```

## How it works

The Aura controller is a USB HID device. Aura Toggle enumerates the HID interfaces, asks each
candidate for its firmware string and configuration table, and keeps the one that answers
correctly — which is why it does not depend on a hardcoded interface number. From the
configuration table it reads how many lighting channels the board has, then sends one effect
command per channel.

The only state kept on your machine is a small file at
`%LOCALAPPDATA%\aura-toggle\state.json`, holding the last effect so it can be restored.
