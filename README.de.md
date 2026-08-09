<div align="center">

# 💡 Aura Toggle

**Mainboard-Beleuchtung aus. Ohne Armoury Crate.**

Eine Datei · keine Installation · kein Hintergrunddienst · nichts wird ins Board geschrieben

[Download](#-download) · [Kommandozeile](#-kommandozeile) · [Effekte](#-effekte) · [Ist das sicher?](#-ist-das-sicher-für-mein-mainboard) · [English](README.md)

<img src="docs/preview-dark.png" alt="Das Aura-Toggle-Fenster" width="360">

</div>

---

| | Aura Toggle | Armoury Crate |
|---|---|---|
| Größe | ~580 KB | Hunderte MB |
| Hintergrunddienst | Keiner | Läuft immer |
| Konto | Keins | Anmeldung nötig |

**Schnellstart:**

1. `AuraToggle.exe` unten herunterladen (oder das Setup)
2. Starten — keine Installation, keine Adminrechte
3. Auf den Knopf klicken

---

## 🌙 Das Problem

RGB-Beleuchtung auf einem Mainboard ist meist Alles-oder-nichts: Entweder läuft der Effekt, den
das BIOS zuletzt gesetzt hat, oder der, den die Hersteller-Software zuletzt gesetzt hat. Sie für
eine Weile abzuschalten — ein dunkler Raum nachts, ein langes Rendering, eine Phase ohne die
Lichtshow — bedeutet normalerweise einen von zwei Wegen:

| Weg | Was er kostet |
|---|---|
| Armoury Crate | Hintergrunddienst, Autostart, Konto, Updater, hunderte MB |
| BIOS | Neustart zum Ausschalten, noch ein Neustart zum Einschalten |
| **Aura Toggle** | Ein Klick. Datei löschen, wenn sie nicht mehr gebraucht wird |

Aura Toggle gibt es für den Fall, dass keiner dieser Wege den Aufwand für einen einzelnen
Schalter wert ist.

## ✨ Was es kann

- 🔌 Schaltet **alle** Kanäle: Onboard-Zone, 12-V-RGB-Header, jeden adressierbaren ARGB-Header
- 🎯 Oder **einzelne Kanäle** — Onboard-Zone statisch weiß, während ein ARGB-Header rot atmet
- 🎨 Neun eingebaute Effekte mit Farbwahl, sofort wirksam
- 🔆 **Helligkeit** für die Farbeffekte, 10 – 100 %, pro Kanal oder für das ganze Board
- 🧩 **Eigene Presets** — Name, ein Effekt und eine Farbe pro Kanal, gespeichert und wiederverwendbar
- 🖥️ Ein Fenster: ein Knopf, der den **laufenden Effekt animiert** und ihn umschaltet
- 📌 Lebt im Infobereich, Rechtsklick für An/Aus
- ⌨️ Vollständige Kommandozeile mit Exit-Codes — Aufgabenplanung, Skripte, Verknüpfungen
- 🔥 Ein globaler Hotkey, frei belegbar, schaltet das ganze Board von überall
- 🔒 Keine Adminrechte, kein Treiber, kein Netzwerk, keine Telemetrie
- 🇩🇪 EN Deutsch und Englisch, unabhängig von Windows umschaltbar

## 🚫 Was es nicht kann

- Kein Tempo und keine Richtung für die Lauflicht-Effekte — der Controller kennt so etwas nicht
- Keine einzelne LED ansteuerbar — Effekt und Farbe gelten für einen ganzen Kanal, nicht eine LED
- Nur ein dynamischer Effekt pro Controller — Spectrum Cycle, Regenbogen, Regenbogen-Atmen und
  Wave laufen auf allen Kanälen eines Controllers gleichzeitig, nicht einzeln
- Nur Mainboard-Beleuchtung — keine GPU, kein RAM, keine Lüfter oder anderen Aura-Sync-Geräte

## 📥 Download

| | Größe | Braucht |
|---|---|---|
| **Portable** `AuraToggle.exe` | ~580 KB | [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) |
| **Installer** | ~2,3 MB | Nichts — er holt die Runtime bei Bedarf |

Portable: herunterladen, doppelklicken, fertig. Installer: für alle oder nur für dich, optional
Autostart und Desktop-Verknüpfung, restlose Deinstallation. Fehlt die .NET 10 Desktop Runtime,
fragt er einmal, lädt sie bei Microsoft herunter und installiert sie — statt 60 MB Runtime in
jedem Download mitzuschleppen.

## 🚀 Bedienung

<img src="docs/preview-light.png" alt="Helles Design" width="360" align="right">

1. **Großer Knopf** — zeigt den Zustand und schaltet ihn. Solange die Beleuchtung an ist,
   animiert er den laufenden Effekt, Helligkeit inklusive.
2. **Auswahlliste** — eingebauten Effekt oder gespeichertes eigenes Preset wählen, wird sofort
   gesetzt. Die letzte Zeile legt ein Preset an; jedes eigene Preset hat ein ✏️ zum Bearbeiten
   und ein ✕ zum Löschen, das noch einmal nachfragt.
3. **Kanal-Auswahl** — alle Kanäle oder ein einzelner: die Onboard-Zone, ein ARGB-Header oder
   ein ganzer Controller, wenn das Board mehrere hat. Beim Überfahren eines Kanals erscheint ein
   ✏️ zum Umbenennen.
4. **Farbfelder** — erscheinen bei Effekten mit Farbe, inklusive eigenem Farbwähler.
5. **Helligkeit** — erscheint zusammen mit den Farbfeldern, 10 bis 100 %, und folgt der
   Kanalauswahl: einen Header allein dimmen oder das ganze Board setzen, was jeden Kanal wieder
   dem boardweiten Wert überlässt.
6. **⚙️ Zahnrad** — Autostart, beim Schließen minimieren, Beleuchtung beim Start, ein globaler
   Hotkey, Animation an/aus, Sprache, Log-Ordner öffnen, alles auf Werkseinstellungen zurücksetzen.

Minimieren schickt das Fenster in den Infobereich. Rechtsklick auf das Symbol schaltet um,
öffnet oder beendet.

### Eigene Presets

Ein eigenes Preset bündelt einen Effekt und eine Farbe **pro Kanal** unter einem selbst
gewählten Namen — die Onboard-Zone statisch weiß, während ein ARGB-Header rot atmet. Anlegen
über die letzte Zeile der Effektliste: benennen, dann pro Kanal Effekt und Farbe wählen,
speichern. Jeder Kanal startet mit dem, was gerade läuft, ein Preset für den aktuellen Look
braucht also keine einzige Änderung. Danach erscheint es in der Effektliste neben einem kleinen
Personen-Symbol, auf einen Blick von den eingebauten Effekten unterscheidbar. Jeder Kanal hat
dort auch seine eigene Helligkeit, ein Preset kann also einen Header auf 30 % und den nächsten
auf voller Helligkeit halten. Das Fenster hat keine eigene Titelleiste, lässt sich aber an seiner
Überschrift verschieben und steht damit nie im Weg.

## ⌨️ Kommandozeile

```bat
AuraToggle.exe                            :: öffnet das Fenster
AuraToggle.exe -off                       :: Beleuchtung aus
AuraToggle.exe -on                        :: zurück auf den letzten Effekt
AuraToggle.exe -preset rainbow            :: Effekt wechseln
AuraToggle.exe -preset static "#20C0FF"   :: Effekt mit Farbe
AuraToggle.exe -brightness 40             :: Farbeffekte dimmen, 10 bis 100
AuraToggle.exe -custom "Filmabend"        :: ein im Fenster gespeichertes Preset anwenden
AuraToggle.exe -list                      :: jeden Controller und Kanal durchnummerieren
AuraToggle.exe -status                    :: aktueller Effekt, Farbe, Helligkeit, an/aus
AuraToggle.exe -help                      :: alle Befehle, erklaert (englisch)
AuraToggle.exe --version                  :: nur die Versionsnummer
```

`-on`, `--on`, `/on`, `on` — alles erlaubt, Groß-/Kleinschreibung egal. Ebenso bei `off`,
`preset`, `brightness`, `custom`, `list`, `status`, `version` und `help` (auch `-h` und `/?`). Angelegt wird ein eigenes Preset
weiterhin nur im Fenster; angewendet werden kann es von hier aus genauso.

**Ein einzelner Kanal oder Controller**, bei `-on`, `-off`, `-preset` und `-brightness`:

```bat
AuraToggle.exe -preset static red -channel 2        :: Nummer aus -list
AuraToggle.exe -preset static red -channel 1.2      :: Controller 1, Kanal 2
AuraToggle.exe -preset static red -channel "ARGB 1" :: Standardname oder eigener Name
AuraToggle.exe -on -device 1                        :: jeder Kanal von Controller 1
```

`-channel` akzeptiert eine flache Nummer aus `-list`, die Form `<Controller>.<Kanal>`, den
Standardnamen in beiden Sprachen oder einen im Fenster vergebenen Namen - genauso nachsichtig
verglichen wie Effektnamen (Groß-/Kleinschreibung, Leerzeichen und Bindestriche egal). Unbekannt
oder mehrdeutig beendet sich mit `2` und listet die möglichen Ziele auf stderr. `-list` und
`-status` sind immer englisch, unabhängig von der Sprache des Fensters, damit ein Skript beim
Sprachwechsel nicht bricht; Fehlermeldungen bleiben übersetzt.

**Exit-Codes:** `0` ok · `2` falsches Argument · `3` kein Controller · `4` Controller belegt ·
`5` Kommunikationsfehler. Fehler gehen nach stderr.

> ⚠️ **PowerShell** wartet nicht auf Fensteranwendungen. Für den Exit-Code:
> `Start-Process AuraToggle.exe -ArgumentList "-off" -Wait -NoNewWindow`.

**Nachts automatisch aus:**

```bat
schtasks /create /tn "LEDs aus" /tr "C:\tools\AuraToggle.exe -off" /sc daily /st 23:30
schtasks /create /tn "LEDs an"  /tr "C:\tools\AuraToggle.exe -on"  /sc daily /st 08:00
```

Im portablen Download liegen neben der Exe zwei fertige Verknüpfungen, **Aura On** und **Aura Off**.
Sie enthalten einen relativen Pfad, der Ordner lässt sich also beliebig verschieben. Der Installer
legt sie nicht an — er trägt nur das Programm selbst ins Startmenü ein.

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
>
> **Helligkeit** entsteht dadurch, dass die gesendete Farbe skaliert wird, gilt also für die
> fünf oben mit ✅ markierten Effekte. Die anderen vier erzeugt die Firmware des Controllers
> selbst; sie nimmt weder Farbe noch Helligkeit an — dimmen lässt sich dort nichts.
>
> Effekte lassen sich über die Kanäle **mischen** — ein Header statisch rot, der nächste atmend —
> aber nur die fünf Farbeffekte. Die anderen vier sind ein Effekt-Generator im Controller, den
> alle seine Kanäle teilen: setzt man den Regenbogen auf einen Header, läuft er auf allen Headern
> dieses Controllers. Das Fenster bietet trotzdem alle neun bei ausgewähltem Einzelkanal an, weist
> aber mit einem Hinweis darauf hin, statt die Wahl still auf die Nachbarn auszudehnen.

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

- Windows 10 oder 11, 64 Bit
- Ein ASUS-Mainboard mit eigenem Aura-USB-Controller (die meisten ASUS-Boards mit Aura Sync
  oder adressierbaren RGB-Headern haben einen, über mehrere Chipsatz-Generationen zurück)

Entwickelt und geprüft auf einem **ASUS-Z790-Mainboard**. Der Controller wird direkt im Dialog
mit dem Gerät erkannt, nicht über eine Modellliste — er funktioniert also entweder, oder meldet
„kein Controller gefunden", siehe [Fehlerbehebung](#-wenn-etwas-klemmt).

## 🛠️ Wenn etwas klemmt

**„Kein AURA-LED-Controller gefunden“**
Kein Aura-USB-Controller auf dem Board, oder die Beleuchtung ist im BIOS aus. Im Geräte-Manager
unter „Eingabegeräte“ nach der Hardware-ID `USB\VID_0B05` schauen.

**„Der AURA-LED-Controller wird von einem anderen Programm belegt“**
Armoury Crate, OpenRGB oder SignalRGB halten ihn offen. Beenden — zwei Programme können
denselben Controller nicht gleichzeitig steuern.

**Die Beleuchtung kommt anders zurück**
Der Controller kann nicht mitteilen, welcher Effekt läuft, das Tool merkt sich also, was es
zuletzt gesetzt hat. Beim allerersten Einschalten greift der ASUS-Regenbogen. Neustarten oder
einfach den gewünschten Effekt wählen.

## 🔨 Selbst bauen

Braucht das .NET 10 SDK. Für den Installer zusätzlich [Inno Setup 6](https://jrsoftware.org/isinfo.php).

`build.bat` im Wurzelverzeichnis ist der komplette Build. Ohne Argument aufrufen oder doppelklicken,
dann entsteht alles, woraus ein Release besteht:

```bat
build.bat                REM alles: portable Exe, Installer, Prüfsummen
build.bat portable       REM nur die portable x64-Exe und ihre beiden Verknüpfungen
build.bat installer      REM nur das Setup
```

`dist\` wird vorher geleert, dort steht danach also genau das Release: `AuraToggle.exe`, die
Verknüpfungen `Aura On` und `Aura Off`, `AuraToggle-Setup-<Version>.exe` und `SHA256SUMS.txt`.
Die Versionsnummer kommt aus der Projektdatei, nicht aus einer zweiten Stelle. Mit `NOPAUSE=1`
läuft das Skript ohne den abschließenden Tastendruck durch.

Der einzelne Befehl hinter dem portablen Build, falls es ohne das Skript sein soll:

```powershell
dotnet publish AuraToggle.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o dist
```

Für den Inno-Setup-Installer: `installer\aura.iss` mit `ISCC.exe` packen (braucht
[Inno Setup 6](https://jrsoftware.org/isinfo.php)).

Regressionssuite — sie schaltet währenddessen die Beleuchtung und lässt sie danach an:

```bat
powershell -ExecutionPolicy Bypass -File tests\aura-tests.ps1
```

## ⚙️ Wie es funktioniert

Der Controller ist ein USB-HID-Gerät. Aura Toggle zählt die HID-Schnittstellen auf, fragt jede
in Frage kommende nach Firmware-String und Konfigurationstabelle und behält die, die antworten
— deshalb ist keine Schnittstellennummer fest verdrahtet, und deshalb werden auf Boards mit
mehreren Controllern auch mehrere gefunden. Die Konfigurationstabelle liefert pro Controller die
Kanalaufteilung, ein Effektbefehl pro Kanal erledigt den Rest.

Die Befehle werden getaktet und die Sequenz zweimal gesendet: Der Controller verwirft
stillschweigend Befehle, die eintreffen, während er noch beschäftigt ist — sonst blieben die
ARGB-Header an, während die Onboard-Zone schon geschaltet hatte.

Der Zustand liegt unter `%LOCALAPPDATA%\aura-toggle` — `state.json` für den letzten Effekt und
die Helligkeit, `settings.json` für die Einstellungen, `presets.json` für eigene Presets,
`channel-state.json` für den letzten Stand jedes Kanals samt eigener Helligkeit,
`channel-names.json` für umbenannte Kanäle, und `log.txt` (rotiert ab 200 KB nach `log.old.txt`)
für Start, Version und Fehler. Portable und installierte Variante teilen sie sich, jeder
Schreibvorgang läuft über eine temporäre Datei, damit ein Abbruch keine Datei beschädigt, und die
Deinstallation fragt, ob der Ordner gelöscht werden soll.

## 📄 Lizenz und Marken

[MIT](LICENSE): frei nutzbar, privat wie kommerziell, weitergeben und verändern erlaubt, einzige
Bedingung ist, den Copyright-Hinweis dabei zu lassen. Die Software kommt **ohne jede Gewähr**,
und niemand haftet dafür, was sie auf deinem Rechner anstellt.

Dies ist ein unabhängiges Projekt. Es stammt **nicht** von ASUSTeK Computer Inc. und wird von
dort weder unterstützt noch empfohlen. „ASUS“, „ROG“, „TUF“ und „Aura“ sind Marken der
jeweiligen Inhaber und werden hier ausschließlich zur Beschreibung der angesprochenen Hardware
verwendet. Es wird keine ASUS-Software, kein Treiber und keine Bibliothek verwendet,
mitgeliefert oder vorausgesetzt.
