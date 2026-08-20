# Struktur-Verifizierung: das Backitup-Backupformat im Detail

**Datum:** 10.08.2026
**Geprüfte Daten:** reale Backitup-Backups aus mehreren ioBroker-Installationen

| Datei | Rolle |
|---|---|
| `iobroker_*_backupiobroker.tar.gz` | Voll-Backup |
| `javascripts_*_backupiobroker.tar.gz` | Skript-Backup |
| `*-scripts.zip` | Admin-Export, als Referenz für den Skriptvergleich |
| `blockly-referenz.xml` | Einzel-Export eines Blockly-Skripts, als Referenz für die XML-Rekonstruktion |

> **Keine Zahlen aus realen Anlagen.** Dieses Dokument beschreibt das *Format* — also was
> in einem Backup steht, wie es aufgebaut ist und was sich daraus auswerten lässt. Die
> Bestandszahlen der geprüften Installationen stehen bewusst nicht darin: Sie sind für
> niemanden nachvollziehbar und sagen über das Format nichts aus.

---

## 1. Es gibt keine `backup.json`

Naheliegend wäre eine zentrale `backup.json`. **Die gibt es nicht.** Das aktuelle Backitup
legt stattdessen zwei **JSON-Lines**-Dateien (JSONL) an — eine Zeile pro Datensatz, kein
umschließendes JSON-Objekt:

```
backup/config.json               ioBroker-Systemkonfiguration
backup/objects.jsonl             eine Zeile = ein Objekt  (die mit Abstand größte Datei)
backup/states.jsonl              eine Zeile = ein State
backup/files/…                   Datei-Assets (VIS-Views, Bilder, MP3s)
backup/zigbee_0…4/               Zigbee-Teilbackups (außerhalb des Scopes)
backup/<adapter>.0/              Teilbackups einzelner Adapter (außerhalb des Scopes)
```

**Konsequenz für den Parser:** Kein DOM-Parsing eines Riesen-JSON. Zeilenweises Lesen mit
`JsonDocument.Parse` pro Zeile. Das ist speicherschonender als jede der zuvor
erwogenen Varianten — der Streaming-vs-DOM-Konflikt entfällt komplett.

### Objektformat in `objects.jsonl`

Die ID steht **im Objekt** als Feld `_id`, nicht als Schlüssel (anders als in `script.json`):

```json
{"common":{"name":"Akku_Farbe","desc":"Manuell erzeugt","role":"value","type":"string",
 "read":true,"write":true,"def":""},"type":"state","_id":"0_userdata.0.Akku_Farbe",
 "acl":{…},"from":"system.adapter.admin.0","user":"system.user.admin","ts":1719044449427}
```

Feldreihenfolge ist **nicht** garantiert (`_id` steht mal vorn, mal hinten) — der Parser darf
sich nicht auf Positionen verlassen.

### Stateformat in `states.jsonl`

```json
{"id":"javascript.1.scriptEnabled.Beispiel.Skriptname",
 "state":{"val":false,"ack":true,"ts":1589608687016,"q":0,"from":"…","lc":1589608686811}}
```

Hier heißt das Feld `id` (ohne Unterstrich). **Wichtig:** State-Werte enthalten Binärdaten —
ein Kamera-Snapshot-Datenpunkt trägt ein komplettes JPEG als String. State-`val` wird
deshalb grundsätzlich verworfen und nie in den Speicher geladen.

---

## 2. Welche Objekttypen vorkommen

Der Bestand besteht ganz überwiegend aus `state`, dahinter mit deutlichem Abstand `channel`,
`device` und `folder`. Außerdem vorhanden: `script`, `adapter`, `meta`, `instance`, `chart`,
`config`, `enum`, `design`. Der Parser muss also mit allen zwölf Typen umgehen können und
darf keinen davon als Fehler behandeln.

**Kreuzvalidierung:** `objects.jsonl` und `script.json` melden übereinstimmend dieselbe
Anzahl Skripte — beide Lesepfade führen zum gleichen Ergebnis. Die Referenzwerte, gegen die
dabei geprüft wird, stehen im Verifikationslauf selbst, nicht in diesem Dokument.

---

## 3. `script.json` — bestätigt

- **Variante A** bestätigt: Objekt-IDs direkt auf oberster Ebene, kein `objects`-Wrapper.
  Variante B wird trotzdem unterstützt (Praxisbefund).
- Datei ist pretty-printed (2 Leerzeichen Einrückung).
- Enthält **ausschließlich** `type: "script"` — keine Ordnerobjekte. Die Ordnerfilterung
  bleibt dennoch implementiert, weil Voll-Backups sehr wohl `folder`-Objekte enthalten.
- **Ein Skript hat kein `enabled`-Feld.** Fehlendes `enabled` wird als „aktiv" gewertet
  (ioBroker-Semantik) und darf keine NullReferenceException auslösen.
- `engineType`-Werte real: `Blockly`, `Javascript/js`. (`TypeScript/ts` kommt hier nicht vor,
  wird aber unterstützt.)

---

## 4. Blockly-XML-Rekonstruktion — verifiziert, bitgenau

Der Rekonstruktions-Algorithmus wurde gegen den echten ioBroker-Admin-Export geprüft.

**Testfall:** ein Blockly-Skript aus `script.json` (welches, steht in `testdaten/blockly-referenz.txt`)
**Referenz:** `testdaten/blockly-referenz.xml` (aus dem ioBroker-Admin exportiert)

Ablauf: Regex `//([A-Za-z0-9+/=%]{50,})\s*$` → Treffer → Base64-Dekodierung → UTF-8 →
**URL-Dekodierung** → Ergebnis beginnt mit `<xml`.

| Prüfung | Ergebnis |
|---|---|
| Regex greift | ✅ |
| Base64 dekodierbar | ✅ |
| URL-dekodiert, beginnt mit `<xml` | ✅ |
| Strukturell identisch zum Admin-Export | ✅ **zeichengenau gleich** |

Die einzige Differenz zum Referenz-Export ist die Einrückung: Der Admin-Export ist
pretty-printed, der Backup-Blob nicht. Nach XML-Normalisierung sind beide zeichengenau
identisch — gleiche Länge, gleicher Inhalt.

**Folgerung:** Die URL-Dekodierung ist zwingend (ohne sie ist das XML unbrauchbar), und das
Tool muss beim Export/der Anzeige pretty-printen, um dem Admin-Format zu entsprechen.

---

## 5. Befund mit Auswirkung auf Säule 3: VIS-Views liegen im Backup vor

Zunächst war die VIS-View-Prüfung ausgeschlossen, begründet mit „Datenlage im Backup
unklar". **Die Datenlage ist geklärt** — die Views liegen als reine JSON-Textdateien im
Voll-Backup:

```
backup/files/vis-2.0/main/vis-views.json
backup/files/vis.0/main/vis-views.json
```

Eine Substring-Suche nach einer Datenpunkt-ID in diesen beiden Dateien ist technisch trivial
und schließt die größte Falsch-Positiv-Quelle der Analyse B (ein Datenpunkt wird nur in VIS
genutzt, in keinem Skript). Da das Tool ausdrücklich eine *Prüfliste* und keine
Löschliste liefern soll, senkt diese Prüfung direkt das Risiko einer Fehllöschung.

**Umsetzung:** als zusätzliche Spalte „In VIS gefunden" in Analyse B. Ein Datenpunkt gilt nur
dann als Verwaisten-Kandidat, wenn auch diese Prüfung negativ ist. Der Rest der Logik
bleibt unverändert.

---

## 5a. Aufbau der vis-views.json (verifiziert, beide VIS-Versionen identisch)

```
{
  "___settings": { … },                     // globale Einstellungen, keine View
  "<ViewName>": {
    "name": "…", "settings": {…}, "activeWidgets": […],
    "widgets": {
      "<WidgetId>": {
        "tpl": "i-vis-universal",
        "data": { "oid": "0_userdata.0.…", "visibility-oid": "…", … },
        "style": {…}
      }
    }
  }
}
```

Datenpunkte stecken ausschließlich im `data`-Objekt der Widgets, und zwar auf zwei Wegen:

1. **Schlüssel, die `oid` enthalten.** Real vorkommend:

   | Schlüssel | VIS 1 | VIS 2 |
   |---|---|---|
   | `oid` | ✅ | ✅ |
   | `oid1`…`oidN` | – | ✅ |
   | `visibility-oid` | ✅ | ✅ |
   | `iTblCellThresholds-oid<N>` | ✅ | – |
   | `countdown_oid` | ✅ | ✅ |
   | `iPopUpCloseDp-oid` | ✅ | – |

2. **Bindings in beliebigen Textfeldern:** `{id}`, `{id;date(hh:mm)}`, `{a:id1;b:id2;a+b}`.

Auszufiltern sind **leere Werte** und der Platzhalter **`nothing_selected`**,
den VIS in unbelegte Felder schreibt — beides kommt reichlich vor.

**Zustandsattribute in Bindings.** Ein Binding kann statt des Werts ein Attribut des
Zustands lesen — das Suffix gehört dann *nicht* zur Objekt-ID:

```
{0_userdata.0.Meldungen.Text.ts;date(hh:mm:ss DD.MM.YYYY)}
 └────── Datenpunkt ───────┘└┬┘
                            Attribut, kein ID-Bestandteil
```

Mögliche Attribute: `val` (Standard, meist weggelassen), `ts`, `lc`, `ack`, `q`, `from`,
`user`, `expire`. Ohne Auflösung erschiene `…Meldung.ts` als eigener, nicht existierender
Datenpunkt und damit fälschlich als totes Widget.

Aufgelöst wird **nur**, wenn die ID selbst nicht im Objektbestand existiert, das letzte
Segment ein bekanntes Attribut ist *und* der Datenpunkt ohne dieses Segment existiert.
Damit bleiben echte Datenpunkte unangetastet, die zufällig auf `.ts` oder `.from` enden —
etwa `mytime.0.Countdown.*.timer`, wo `.timer` ein echter Datenpunkt ist.

Ergebnis: Die Auswertung findet die in VIS 1 und VIS 2 verwendeten Datenpunkte samt
Überschneidung und weist die aus, zu denen es **kein Objekt mehr im Backup** gibt — tote
Widgets, typischerweise aus nicht mehr installierten Adaptern.

> **Wirkung der Attribut-Auflösung:** Ohne sie zählte ein Binding mit `.ts`-Suffix als
> eigener, scheinbar fehlender Datenpunkt mit. Da `.ts` nur der Zeitstempel des
> existierenden Datenpunkts ohne dieses Suffix ist, wird die Fundstelle diesem
> zugeschlagen — die Zahl der „fehlenden" sinkt entsprechend.

## 5b. ioBroker-IDs sind case-sensitiv — und das ist praxisrelevant

`objects.jsonl` enthält **keine echten Duplikate**: Jede Zeile ergibt eine eigene ID. Es
gibt jedoch **ID-Paare, die sich ausschließlich in der Groß-/Kleinschreibung
unterscheiden** — nach diesem Muster:

```
0_userdata.0.Beispiel.Temperatur  <->  …temperatur
admin.0.info.newsFeed           <->  admin.0.info.newsfeed
script.js.Test                  <->  script.js.test
```

Für ioBroker sind das verschiedene Objekte. Alle ID-Vergleiche im Werkzeug laufen deshalb
mit `StringComparer.Ordinal`. Ein case-insensitiver Vergleich wäre nicht nur formal falsch,
er würde genau die Tippfehler-Dubletten unsichtbar machen, die die Verwaisten-Analyse
aufdecken soll.

## 5c. `states.jsonl` — was darin steckt und was es wert ist

Ursprünglich wurde states.jsonl nur gezählt. Die Datei enthält aber je Datensatz
Metadaten, die als einzige Quelle im gesamten Backup etwas über die **tatsächliche
Nutzung** eines Datenpunkts aussagen statt nur über seine Existenz:

| Feld | Bedeutung | Nutzen |
|---|---|---|
| `lc` | last change — letzte echte Wertänderung | trennt lebende von eingeschlafenen Datenpunkten |
| `ts` | letzter Schreibvorgang, auch ohne Wertänderung | Ersatz, wenn `lc` fehlt |
| `from` | schreibende Instanz | verrät, wer einen Datenpunkt versorgt |
| `q` | Qualitätscode, 0 = gut | laufende Störungen zum Backup-Zeitpunkt |
| `ack` | Quittierung | geschriebene Befehle, die nie beantwortet wurden |

`val` wird weiterhin **nie** übernommen (Binärdaten, siehe Abschnitt 1).

**Was sich daraus auswerten lässt:**

| Sicht | Grundlage |
|---|---|
| States ohne zugehöriges Objekt | Werte-Leichen. Adapter-Verwaltungs-States (`system.adapter.*.upload`) werden vorher abgezogen, sonst wäre die Liste voller Fehlalarme |
| state-Objekte ohne jeden Wert | Objekt vorhanden, kein Eintrag in der States-DB |
| Störungen | `q` ≠ 0 |
| nicht quittiert | `ack` = false |
| Altersverteilung | nach `lc`, gestaffelt von „jünger als ein Tag" bis „älter als ein Jahr" |

Die Werte-Leichen häufen sich erwartungsgemäß bei längst entfernten Adaptern und bei
`javascript.*` (Verwaltungs-States gelöschter Skripte).

### Wirkung auf Analyse B — der eigentliche Ertrag

Die Kandidaten der Analyse B zerfallen mit den Zeitstempeln in zwei sehr
unterschiedliche Gruppen:

- Die einen sind seit über einem Jahr unverändert → belastbar tot.
- Die anderen haben sich noch am Backup-Tag geändert → irgendetwas beschreibt sie
  weiterhin. Typischer Fall: ein Skript, das seine Ziel-IDs zur Laufzeit zusammensetzt,
  weshalb die Textsuche sie nicht findet.

Ohne diese Prüfung hätten alle gleich ausgesehen. Die Zeitstempel entlarven damit den
Großteil der bisherigen Kandidaten als Falsch-Positive — mehr als jede der vier
Textprüfungen zuvor geleistet hat.

## 5d. Was sonst noch im Voll-Backup liegt (Bestandsaufnahme)

Vollständige Inventur des Archivs, als Grundlage für spätere Erweiterungen. Nicht alles
davon ist umgesetzt — die Spalte „Stand" sagt, was damit geschieht.

| Quelle | Inhalt | Stand |
|---|---|---|
| `backup/objects.jsonl` | alle Objekte | ausgewertet |
| `backup/states.jsonl` | alle States (nur Metadaten) | ausgewertet (5c) |
| `backup/files/vis*/main/vis-views.json` | VIS-Views | ausgewertet (5a) |
| `backup/config.json` | Systemkonfiguration: Objects/States = Redis, Log-Level, dataDir, controller-eigene Backup-Einstellungen | nicht ausgewertet |
| `system.host.*` | js-controller-Version, Betriebssystem, CPUs, RAM, Netzwerkinterfaces, IP | nicht ausgewertet |
| `common.custom` | Datenpunkte mit Logging-Einstellungen je Instanz (InfluxDB, History, SQL, sourceanalytix …), samt `changesOnly`/`debounce`/`aliasId` | **ausgewertet:** Tab „Logging" je Datenpunkt und Instanz (11.08.2026), zusätzlich Ja/Nein in Analyse B. `native.data.l[].id` je Chart-Linie liefert die Referenz — Instanz (`system.adapter.<name>.<nr>` oder `json`) wird ignoriert. |
| `common.states` + `common.alias.read`/`.write` | Wertetabellen (Gerätewert → Label) und Aliasse mit Konvertierungsfunktion (JS-Code, getrennt von der Ziel-`id`) | **ausgewertet:** Alias-Tab zeigt die Konverter, Generator erzeugt aus `common.states` einen Konverter-Vorschlag (11.08.2026). Reine Zahlenumrechnung bleibt nicht ableitbar. |
| `type: chart` | echarts-Definitionen mit Datenpunkt-Referenzen (`native.data.l[]`, je Linie `id` + Quell-`instance`) | **ausgewertet:** 5. Kriterium in Analyse B (11.08.2026), instanzunabhängig — history/sql/eigen werden mit erfasst |
| `type: adapter` gegen `type: instance` | installiert, aber ohne Instanz. Achtung: Ein erheblicher Teil der `adapter`-Objekte sind host-gebundene `system.host.*.adapter.*`-Einträge, keine echten Kandidaten; gezählt werden nur die echten `system.adapter.<name>`-Objekte | **ausgewertet:** Abschnitt „Adapter ohne Instanz" im Übersicht-Tab (11.08.2026) |
| Objekt-`ts` | wann ein Objekt zuletzt geändert wurde | nicht ausgewertet |
| Instanz-`native` | Adapter-Konfigurationen im Klartext (teils zig KB je Instanz); `protectedNative`/`encryptedNative` markieren schützenswerte Felder | **bewusst nicht ausgewertet** — enthält Zugangsdaten |
| `backup/files/**` | Assets: Kamerabilder, Sprachausgabe-MP3s, VIS-Bilder, Videos | nicht ausgewertet |
| `backup/zigbee_*`, `backup/<adapter>.0` | Teilbackups fremder Adapter | außerhalb des Scopes |

Zwei Befunde, die eine Auswertung **nicht** zuverlässig lohnen: Die Enums (`enum.rooms`,
`enum.functions`, `enum.functions.color`) können komplett ohne Mitglieder dastehen — dann
liefe eine Raum-/Gewerke-Auswertung leer. Und wo nur der eine Standardbenutzer
(`system.user.admin`) existiert, hätte eine Rechteauswertung nichts zu zeigen.

## 5e. Woran sich die Herkunft eines Backups erkennen lässt

Für den Backup-Vergleich muss feststellbar sein, ob zwei Dateien überhaupt aus derselben
ioBroker-Installation stammen. Geprüft an zwei echten Backups von **zwei verschiedenen
Systemen**:

| Merkmal | Quelle | Eignung |
|---|---|---|
| **Installations-UUID** | `system.meta.uuid` → `native.uuid` | **maßgeblich.** Bei der Erstinstallation vergeben und danach unverändert. Die beiden Testsysteme tragen verschiedene UUIDs und sind daran sicher zu unterscheiden |
| Hostname | `system.host.<name>` | brauchbar, aber **durch einen Platzhalter ersetzt**: Sowohl die Objekt-ID als auch `common.hostname` enthalten nur `$$__hostname__$$`. Der echte Name steckt im `from`-Feld (`system.host.<hostname>`) und wird von dort rekonstruiert |
| IPv4-Adresse | `common.address[0]` | brauchbar als Rückfallebene, kann sich aber ändern |
| IPv6-Link-Local | `common.address[1]` | **bewusst ungenutzt** — die Link-Local-Adresse enthält die MAC-Adresse |
| js-controller-Version | `common.installedVersion` | nur Anzeige. Zwei gepflegte Systeme melden meist dieselbe Version und taugen damit nicht zur Unterscheidung |
| Ort und Koordinaten | `system.config.common.city` / `longitude` / `latitude` | **bewusst ungenutzt.** Stünden auf jedem geteilten Bildschirmfoto und verraten den Wohnort; zur Unterscheidung taugen sie ohnehin nicht, weil zwei Systeme desselben Haushalts denselben Ort melden |
| `secret` | `system.config.native.secret` | **bewusst ungenutzt.** Wäre als Kennung ideal (48 Zeichen, installationsweit eindeutig), ist aber der Schlüssel, mit dem ioBroker Passwörter verschlüsselt |

`system.config` wird deshalb gar nicht erst gelesen — was nicht im Speicher landet, kann
auch nicht versehentlich in einem Export oder Bildschirmfoto auftauchen.

Ein **Skript-Backup** enthält keines dieser Objekte — die Herkunft ist dort grundsätzlich
nicht prüfbar und wird ehrlich als „unbekannt" ausgewiesen, statt eine Übereinstimmung
vorzutäuschen.

## 6. Weitere Befunde und Anpassungen

| Ursprüngliche Annahme | Realität | Anpassung |
|---|---|---|
| `backup.json` im Archiv suchen | `backup/objects.jsonl` | Erkennung auf beide Namen, JSONL bevorzugt |
| Objekt-IDs als JSON-Schlüssel | `_id`-Feld in der Zeile | Zwei Lesepfade (JSONL und klassisches JSON) |
| backup.json 50–200 MB | `objects.jsonl` bleibt auch bei großen Anlagen im zweistelligen MB-Bereich | Zeilenweises Parsen, unkritisch |
| States mit `val` laden | `val` enthält JPEG-Binärdaten | State-Werte werden verworfen; Metadaten (Zeitstempel, Quelle, Qualität) werden gelesen (5c) |

Für ältere Backitup-Versionen mit klassischer `backup.json` bleibt der Lesepfad erhalten —
die Erkennung erfolgt am Inhalt, nicht am Dateinamen.
