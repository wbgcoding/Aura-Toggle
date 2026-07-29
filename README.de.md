<div align="center">

# 💡 Aura Toggle

**Mainboard-Beleuchtung aus. Ohne Armoury Crate.**

Eine Datei mit 540 KB · keine Installation · kein Hintergrunddienst · nichts wird ins Board geschrieben

[Download](#-download) · [Kommandozeile](#-kommandozeile) · [Effekte](#-effekte) · [Ist das sicher?](#-ist-das-sicher-für-mein-mainboard) · [English](README.md)

<img src="docs/preview-light.png" alt="Das Aura-Toggle-Fenster" width="360">

</div>

---

## 🌙 Das Problem

Halb zwei nachts. Ein Download läuft, der PC muss anbleiben — und das Gehäuse leuchtet wie eine
Jukebox. Du willst die LEDs für die Nacht aus.

Die Möglichkeiten bisher:

| Weg | Was er kostet |
|---|---|
| Armoury Crate | Hintergrunddienst, Autostart, Konto, Updater, hunderte MB |
| BIOS | Neustart zum Ausschalten. Noch ein Neustart zum Einschalten |
| **Aura Toggle** | **Ein Klick. Datei löschen, wenn du fertig bist** |

Wer schon mal gedacht hat *„ich will einfach nur heute Nacht die LEDs aus, nicht gleich ein
ganzes Softwarepaket"* — genau dafür ist das hier.

## ✨ Was es kann

- 🔌 Schaltet **alle** Kanäle: Onboard-Zone, 12-V-RGB-Header, jeden adressierbaren ARGB-Header
- 🎨 Neun eingebaute Effekte mit Farbwahl, sofort wirksam
- 🖥️ Ein Fenster: ein Knopf, der den **laufenden Effekt animiert** und ihn umschaltet
- 📌 Lebt im Infobereich, Rechtsklick für An/Aus
- ⌨️ Vollständige Kommandozeile mit Exit-Codes — Aufgabenplanung, Skripte, Verknüpfungen
- 🔒 Keine Adminrechte, kein Treiber, kein Netzwerk, keine Telemetrie
- 🇩🇪 🇬🇧 Deutsch und Englisch

## 📥 Download

| | Größe | Braucht |
|---|---|---|
| **Portable** `aura.exe` | 540 KB | [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) |
| **Installer** x64 / ARM64 | 34 MB / 31 MB | Nichts — die Runtime steckt drin |

Portable: herunterladen, doppelklicken, fertig. Installer: nach Programme, optional Autostart
und Desktop-Verknüpfung, restlose Deinstallation.

## 🚀 Bedienung

<img src="docs/preview-dark.png" alt="Dunkles Design" width="360" align="right">

1. **Großer Knopf** — zeigt den Zustand und schaltet ihn. Solange die Beleuchtung an ist,
   animiert er den laufenden Effekt.
2. **Auswahlliste** — Effekt wählen, wird sofort gesetzt.
3. **Farbfelder** — erscheinen bei Effekten mit Farbe, inklusive freier Farbwahl.
4. **⚙️ Zahnrad** — Autostart, minimiert starten, beim Schließen minimieren, Beleuchtung beim
   Start, Animation an/aus, Sprache.

Minimieren schickt das Fenster in den Infobereich. Rechtsklick auf das Symbol schaltet um,
öffnet oder beendet.

## ⌨️ Kommandozeile

```bat
aura                            REM öffnet das Fenster
aura -off                       REM Beleuchtung aus
aura -on                        REM zurück auf den letzten Effekt
aura -preset rainbow            REM Effekt wechseln
aura -preset static "#20C0FF"   REM Effekt mit Farbe
```

`-on`, `--on`, `/on`, `on` — alles erlaubt, Groß-/Kleinschreibung egal. Ebenso bei `off` und
`preset`.

**Exit-Codes:** `0` ok · `2` falsches Argument · `3` kein Controller · `4` Controller belegt ·
`5` Kommunikationsfehler. Fehler gehen nach stderr.

> ⚠️ **PowerShell** wartet nicht auf Fensteranwendungen. Für den Exit-Code:
> `Start-Process aura.exe -ArgumentList "-off" -Wait -NoNewWindow`.

**Nachts automatisch aus:**

```bat
schtasks /create /tn "LEDs aus" /tr "C:\tools\aura.exe -off" /sc daily /st 23:30
schtasks /create /tn "LEDs an"  /tr "C:\tools\aura.exe -on"  /sc daily /st 08:00
```

Neben der Exe liegen zwei fertige Verknüpfungen, **Aura An** und **Aura Aus**. Sie enthalten
einen relativen Pfad, der Ordner lässt sich also beliebig verschieben.

## 🎨 Effekte

| Name | Sieht aus wie | Farbe |
|---|---|---|
| `static` | Eine feste Farbe | ✅ |
| `breathing` | Auf- und abblenden | ✅ |
| `flashing` | Blinken | ✅ |
| `spectrum-cycle` | Alle LEDs durchlaufen gemeinsam das Spektrum | — |
| `rainbow` | Verlauf, der über die LEDs wandert *(ASUS-Standard)* | — |
| `rainbow-breathing` | Farbwechsel mit Blenden | — |
| `chase-fade` | Lauflicht mit ausblendendem Schweif | ✅ |
| `chase` | Lauflicht | ✅ |
| `wave` | Langsam driftendes Spektrum über den Strip | — |

Namen werden großzügig erkannt: Groß-/Kleinschreibung, Leerzeichen, Binde- und Unterstriche
sind egal, die übersetzten Namen funktionieren ebenfalls. Ein unbekannter Name gibt die Liste
aus.

> Es gibt **keine Geschwindigkeit und keine Richtung**. Der Controller kennt das nicht — sein
> Effektbefehl trägt einen Kanal und einen Modus, sonst nichts.

## 🔒 Ist das sicher für mein Mainboard?

**Ja**, und der Grund ist wichtig.

Der Aura-Controller hält seine Konfiguration im eigenen Flash, und dieser Flash ist das, was
dein Board beim Einschalten anwendet. Aura Toggle sendet **nie den Befehl, der dort
hineinschreibt**. Nur flüchtige Effektbefehle, die im RAM des Controllers stehen.

- ✅ Deine BIOS-Beleuchtungseinstellungen bleiben unangetastet
- ✅ Nach einem Neustart ist die Beleuchtung wieder da, auch wenn du sie vorher ausgeschaltet hast
- ✅ Deinstallieren heißt: eine Datei löschen
- ✅ Kein Kerneltreiber, keine Adminrechte — es ist ein normales USB-HID-Gerät

## 💻 Voraussetzungen

- Windows 10 oder 11, 64 Bit oder ARM64
- Ein ASUS-Mainboard mit Aura-USB-Controller — ab X470-/Z390-Generation, inklusive aktueller
  AM5- und LGA1700-Boards

Entwickelt und geprüft auf einem **ROG STRIX Z790-E GAMING WIFI**. Der Controller wird im
Dialog mit dem Gerät erkannt, nicht über eine Modellliste — nicht aufgeführte ASUS-Boards
derselben Familie sollten also funktionieren.

## 🛠️ Wenn etwas klemmt

**„Kein AURA-LED-Controller gefunden"**
Kein Aura-USB-Controller auf dem Board, oder die Beleuchtung ist im BIOS aus. Im Geräte-Manager
unter „Eingabegeräte" nach der Hardware-ID `USB\VID_0B05` schauen.

**„Der AURA-LED-Controller wird von einem anderen Programm belegt"**
Armoury Crate, OpenRGB oder SignalRGB halten ihn offen. Beenden — zwei Programme können
denselben Controller nicht gleichzeitig steuern.

**Die Beleuchtung kommt anders zurück**
Der Controller kann nicht mitteilen, welcher Effekt läuft, das Tool merkt sich also, was es
zuletzt gesetzt hat. Beim allerersten Einschalten greift der ASUS-Regenbogen. Neustarten oder
einfach den gewünschten Effekt wählen.

## 🔨 Selbst bauen

Braucht das .NET 10 SDK. Für die Installer zusätzlich [Inno Setup 6](https://jrsoftware.org/isinfo.php).

```bat
build.bat            REM dist\aura.exe, 540 KB
build.bat standalone REM Standalone, ~120 MB, ohne Runtime lauffähig
build.bat installer  REM dist\installer\Setup-AuraToggle-<Version>-x64.exe
build.bat all        REM Portable plus beide Installer
```

Regressionssuite — sie schaltet währenddessen die Beleuchtung und lässt sie danach an:

```bat
powershell -ExecutionPolicy Bypass -File tests\aura-tests.ps1
```

## ⚙️ Wie es funktioniert

Der Controller ist ein USB-HID-Gerät. Aura Toggle zählt die HID-Schnittstellen auf, fragt jede
in Frage kommende nach Firmware-String und Konfigurationstabelle und behält die, die antwortet
— deshalb ist keine Schnittstellennummer fest verdrahtet. Die Konfigurationstabelle liefert die
Kanalaufteilung, ein Effektbefehl pro Kanal erledigt den Rest.

Die Befehle werden getaktet und die Sequenz zweimal gesendet: Der Controller verwirft
stillschweigend Befehle, die eintreffen, während er noch beschäftigt ist — sonst blieben die
ARGB-Header an, während die Onboard-Zone schon geschaltet hatte.

Der Zustand liegt unter `%LOCALAPPDATA%\aura-toggle` — `state.json` für den letzten Effekt,
`settings.json` für die Einstellungen. Portable und installierte Variante teilen sie sich.

## 📄 Lizenz und Marken

MIT, siehe [LICENSE](LICENSE). Die Software kommt **ohne jede Gewähr**, und niemand haftet
dafür, was sie auf deinem Rechner anstellt.

Dies ist ein unabhängiges Projekt. Es stammt **nicht** von ASUSTeK Computer Inc. und wird von
dort weder unterstützt noch empfohlen. „ASUS", „ROG", „TUF" und „Aura" sind Marken der
jeweiligen Inhaber und werden hier ausschließlich zur Beschreibung der angesprochenen Hardware
verwendet. Es wird keine ASUS-Software, kein Treiber und keine Bibliothek verwendet,
mitgeliefert oder vorausgesetzt.
