# Aura Toggle

**Ein Knopf. Die LEDs auf dem ASUS-Mainboard gehen aus. Ohne Armoury Crate, ohne
Hintergrunddienst, ohne Installation.**

*English version: [README.md](README.md)*

---

## Das Problem

Halb zwei nachts. Ein großer Download läuft, ein Render wird fertig, ein Backup arbeitet sich
durch die Nacht — der Rechner muss anbleiben. Also geht man ins Bett, und die Kiste steht da
und leuchtet wie eine Jukebox. Die Onboard-Beleuchtung pulsiert an die Decke, der Strip hinter
dem Gehäuse wirft Farbe an die Wand, und richtig dunkel wird es nie.

Die naheliegende Lösung ist, die Beleuchtung auszuschalten. Weniger naheliegend ist der Preis:
Der offizielle Weg heißt, eine komplette RGB-Suite zu installieren — mit Hintergrunddienst,
Autostart-Eintrag, Benutzerkonto, Updater und ein paar hundert Megabyte. Alles nur, um
gelegentlich einen Wert auf null zu setzen. Viele behalten lieber die leuchtenden LEDs, als
sich das anzutun. Also bleibt das Licht an. Jede Nacht.

Über das BIOS geht es auch — aber nur mit Neustart, und dann bleibt es aus bis zum nächsten
Neustart. Das ist kein Lichtschalter, das ist ein Ritual.

**Aura Toggle ist der fehlende Lichtschalter.** Eine portable Datei mit 300 KB. Starten, Licht
aus. Nochmal starten, Licht wieder an — genau so wie vorher. Nichts wird installiert, nichts
läuft im Hintergrund, nichts wird dauerhaft ins Mainboard geschrieben. Datei löschen, und es
ist, als wäre das Tool nie da gewesen.

Wer schon mal gedacht hat *„ich will einfach nur heute Nacht die LEDs aus, nicht gleich ein
ganzes Softwarepaket"* — genau dafür ist das hier.

## Was es macht

- Schaltet **alle** Kanäle des Aura-Controllers: Onboard-Zone, 12-V-RGB-Header und jeden
  adressierbaren ARGB-Header.
- Stellt beim Einschalten den zuletzt gesetzten Effekt wieder her.
- Lässt dich einen der eingebauten Effekte des Controllers wählen — im Fenster oder über die
  Kommandozeile.
- Läuft über die Kommandozeile, also auch aus Aufgabenplanung, Verknüpfung oder Skript.
- Braucht keine Administratorrechte.
- Spricht Deutsch und Englisch, passend zur Windows-Anzeigesprache.

## Was es bewusst nicht macht

Keine eigenen Animationen, keine Profile, kein Tray-Icon, kein Updater, keine Telemetrie,
überhaupt kein Netzwerkzugriff. Es schaltet die Beleuchtung, wählt einen der Effekte, die der
Controller ohnehin kann, und setzt dessen Farbe. Das ist der komplette Funktionsumfang, und das
soll auch so bleiben.

## Loslegen

1. `aura.exe` herunterladen und hinlegen, wo du willst — Desktop, Tools-Ordner, USB-Stick.
2. Doppelklick. Ein kleines Fenster mit einem großen Knopf, der den Zustand zugleich anzeigt
   und umschaltet — solange die Beleuchtung an ist, animiert der Knopf den laufenden Effekt.
3. Darunter den Effekt aus der Liste wählen, er wird sofort gesetzt. Effekte mit Farbe zeigen
   darunter Farbfelder, inklusive freier Farbwahl über den Farbdialog.

Das Zahnrad oben rechts enthält vier Einstellungen: mit Windows starten, minimiert starten,
beim Schließen nur minimieren, und welcher Zustand beim Start gesetzt wird.

Mehr Einrichtung gibt es nicht.

### Kommandozeile

| Befehl | Wirkung |
|---|---|
| `aura` | Öffnet das Umschaltfenster |
| `aura -off` | Beleuchtung aus |
| `aura -on` | Beleuchtung zurück auf den zuletzt gesetzten Effekt |
| `aura -preset <Name>` | Wechselt auf diesen Effekt und schaltet die Beleuchtung ein |
| `aura -preset <Name> <Farbe>` | Dasselbe mit Farbe, als `#RRGGBB` oder Farbname |

`-on`, `--on`, `/on` und einfach `on` funktionieren alle, Groß- und Kleinschreibung egal.
Ebenso bei `off` und `preset`.

### Effekte

| Name | Wie es aussieht | Nutzt Farbe |
|---|---|---|
| `static` | Eine feste Farbe | ja |
| `breathing` | Auf- und abblenden | ja |
| `flashing` | Blinken | ja |
| `spectrum-cycle` | Alle LEDs durchlaufen gemeinsam das Farbspektrum | nein |
| `rainbow` | Farbverlauf, der über die LEDs wandert — der ASUS-Standard | nein |
| `rainbow-breathing` | Farbwechsel mit Auf- und Abblenden | nein |
| `chase-fade` | Lauflicht mit ausblendendem Schweif | ja |
| `chase` | Lauflicht | ja |
| `wave` | Welle, die über die LEDs läuft | nein |

Namen werden großzügig erkannt: Groß-/Kleinschreibung, Leerzeichen, Binde- und Unterstriche
sind egal, also funktionieren `spectrum-cycle`, `"Spectrum Cycle"` und `spectrumcycle`
gleichermaßen. Die übersetzten Namen aus dem Fenster werden ebenfalls akzeptiert. Ein
unbekannter Name gibt die vollständige Liste aus.

Die mit „nutzt Farbe" markierten Effekte nehmen die Farbe, die du im Fenster wählst oder auf
der Kommandozeile übergibst; die anderen ignorieren sie und laufen im Spektrum des Controllers.

Geschwindigkeit oder Richtung gibt es nicht, weil dieser Controller das nicht kann: Der
Effektbefehl trägt einen Kanal und einen Modus, sonst nichts.

Exit-Codes: `0` Erfolg, `2` unbekanntes Argument, `3` kein Controller gefunden, `4` Controller
von einem anderen Programm belegt, `5` Kommunikationsfehler. Fehler gehen nach stderr, damit
Skripte darauf reagieren können.

> **Hinweis zu PowerShell:** `aura.exe` ist eine Fensteranwendung, und darauf wartet PowerShell
> nicht. Wer den Exit-Code braucht, nutzt
> `Start-Process aura.exe -ArgumentList "-off" -Wait -NoNewWindow`.

### Fertige Verknüpfungen

Neben der Exe liegen zwei Verknüpfungen: **Aura An** und **Aura Aus**. Sie enthalten einen
relativen Pfad, du kannst den ganzen Ordner also beliebig verschieben oder kopieren, ohne dass
sie kaputtgehen. Zieh sie auf den Desktop, in die Taskleiste oder ins Startmenü, dann schaltest
du mit einem Klick.

### Nachts automatisch ausschalten

Windows-Aufgabenplanung, zwei Aufgaben, keine Zusatzsoftware:

```bat
schtasks /create /tn "LEDs aus" /tr "C:\tools\aura.exe -off" /sc daily /st 23:30
schtasks /create /tn "LEDs an"  /tr "C:\tools\aura.exe -on"  /sc daily /st 08:00
```

## Voraussetzungen

- Windows 10 oder Windows 11, 64 Bit.
- Ein ASUS-Mainboard mit Aura-USB-Beleuchtungscontroller. Boards ab etwa der X470- und
  Z390-Generation haben einen, aktuelle AM5- und LGA1700-Boards ebenfalls.
- Die .NET 10 Desktop Runtime für die kleine Variante. Wer gar keine Voraussetzungen will,
  nimmt den Standalone-Build — deutlich größer, dafür ohne alles.

Entwickelt und geprüft auf einem ROG STRIX Z790-E GAMING WIFI. Das Tool erkennt den Controller
im Dialog mit dem Gerät statt über eine feste Modellliste, deshalb funktionieren auch nicht
aufgeführte ASUS-Boards derselben Controller-Familie voraussichtlich.

## Ist das sicher für mein Mainboard?

Ja — und der Grund ist wichtig.

Der Aura-Controller hält seine Beleuchtungskonfiguration im eigenen Flash, und dieser Flash ist
das, was das Mainboard beim Einschalten anwendet. Aura Toggle sendet **nie** den Befehl, der in
diesen Flash schreibt. Es sendet ausschließlich flüchtige Effektbefehle, die nur im RAM des
Controllers stehen.

Praktisch heißt das:

- Deine BIOS-Beleuchtungseinstellungen bleiben unangetastet.
- Nach einem Neustart ist die Beleuchtung wieder an, auch wenn du sie vorher ausgeschaltet
  hattest.
- Deinstallieren heißt: eine Datei löschen.

Es wird auch kein Kerneltreiber geladen und kein Administratorrecht gebraucht — der Controller
ist ein normales USB-HID-Gerät, dafür reichen Benutzerrechte.

## Wenn etwas klemmt

**„Kein AURA-LED-Controller gefunden"**
Vielleicht hat das Board keinen Aura-USB-Controller, oder die Beleuchtung ist im BIOS
deaktiviert. Im Geräte-Manager unter „Eingabegeräte (Human Interface Devices)" nach einem Gerät
mit der Hardware-ID `USB\VID_0B05` schauen.

**„Der AURA-LED-Controller wird von einem anderen Programm belegt"**
Armoury Crate, OpenRGB, SignalRGB und ähnliche Tools halten den Controller offen. Das andere
Programm zuerst beenden — zwei Programme können denselben Beleuchtungscontroller nicht
gleichzeitig steuern.

**Die Beleuchtung kommt anders zurück**
Der Controller kann nicht mitteilen, welcher Effekt gerade läuft. Aura Toggle merkt sich daher
den zuletzt gesetzten Effekt. Beim allerersten Einschalten greift der ASUS-Standard
(Regenbogen). Ein Neustart holt deine BIOS-Einstellung zurück — oder du wählst den gewünschten
Effekt einfach in der Auswahlliste.

## Selbst bauen

Braucht das .NET 10 SDK.

```bat
build.bat
```

Ergebnis ist `dist\aura.exe`. Für eine Variante ohne installierte .NET-Runtime:

```bat
build.bat standalone
```

In `tests\` liegt eine Regressionssuite. Sie schaltet die Beleuchtung während des Laufs und
lässt sie danach eingeschaltet:

```bat
powershell -ExecutionPolicy Bypass -File tests\aura-tests.ps1
```

## Wie es funktioniert

Der Aura-Controller ist ein USB-HID-Gerät. Aura Toggle zählt die HID-Schnittstellen auf, fragt
jede in Frage kommende nach Firmware-String und Konfigurationstabelle und behält die, die
korrekt antwortet — deshalb hängt es nicht an einer fest verdrahteten Schnittstellennummer. Aus
der Konfigurationstabelle liest es, wie viele Beleuchtungskanäle das Board hat, und schickt
dann pro Kanal einen Effektbefehl.

Der einzige Zustand auf deinem Rechner ist eine kleine Datei unter
`%LOCALAPPDATA%\aura-toggle\state.json` mit dem zuletzt gesetzten Effekt.

Die Befehle werden getaktet und die Schaltsequenz zweimal gesendet. Der Controller verwirft
stillschweigend Befehle, die eintreffen, während er den vorherigen noch anwendet — sonst
blieben die ARGB-Header an, während die Onboard-Zone schon geschaltet hatte.

## Keine Verbindung zu ASUS

Dies ist ein unabhängiges Projekt. Es stammt nicht von ASUSTeK Computer Inc., wird von dort
weder unterstützt noch empfohlen. „ASUS", „ROG", „TUF" und „Aura" sind Marken der jeweiligen
Inhaber und werden hier ausschließlich zur Beschreibung der angesprochenen Hardware verwendet.
Es wird keine ASUS-Software, kein Treiber und keine Bibliothek verwendet, mitgeliefert oder
vorausgesetzt.

## Lizenz

MIT, siehe [LICENSE](LICENSE). Insbesondere: Die Software kommt ohne jede Gewähr, und niemand
haftet dafür, was sie auf deinem Rechner anstellt.
