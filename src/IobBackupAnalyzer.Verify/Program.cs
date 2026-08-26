using System.Diagnostics;
using System.Formats.Tar;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml;
using IobBackupAnalyzer.Core;

// Prüft die Kernlogik gegen die echten Backups in testdaten/ — ohne GUI und ohne
// Test-Framework (keine externen NuGet-Pakete).
//
// Die Bestandszahlen der Testanlagen (wie viele Objekte, Skripte, Aliasse …) stehen
// bewusst NICHT in dieser Datei: Sie beschreiben reale Installationen und haben im
// oeffentlichen Repository nichts verloren. Sie liegen neben den Backups in
// testdaten/referenzwerte.json und werden ueber CheckRef gelesen. Fehlt die Datei,
// werden genau diese Pruefungen uebersprungen statt fehlzuschlagen — der Rest laeuft.

// Dieselbe Anzeigekultur wie in beiden Oberflächen. Ohne diese Zeile prüfte der Lauf die
// Kultur des Rechners mit, auf dem er gerade läuft: Auf einem deutschen Windows waren die
// Zahlentexte grün, auf einem englischen System oder einem frischen Linux wären dieselben
// Prüfungen rot gewesen. Ein Verifikationslauf soll überall dasselbe Ergebnis liefern.
AppCulture.Apply();

var root = FindProjectRoot();
var testdaten = Path.Combine(root, "testdaten");

if (!Directory.Exists(testdaten))
{
    Console.Error.WriteLine($"testdaten/ nicht gefunden unter {root}");
    return 2;
}

// Alles, was die geprueften Anlagen beschreibt — Erwartungszahlen und Archivnamen —,
// liegt neben den Backups statt im Quelltext.
var (referenzwerte, referenzdateien) = LadeReferenzen(Path.Combine(testdaten, "referenzwerte.json"));

// Archiv aus referenzwerte.json, sonst das neueste passende im Ordner. Ohne Muster und
// ohne Eintrag bleibt es bei einem Pfad, den es nicht gibt — der Aufrufer prueft das.
string WaehleArchiv(string schluessel, string? muster)
{
    if (referenzdateien.TryGetValue(schluessel, out var name))
        return Path.Combine(testdaten, name);
    if (muster is null) return Path.Combine(testdaten, schluessel + ".tar.gz");

    return Directory.EnumerateFiles(testdaten, muster)
                    .OrderByDescending(f => f, StringComparer.Ordinal)
                    .FirstOrDefault() ?? Path.Combine(testdaten, muster);
}

// Welche Archive geprueft werden, steht in testdaten/referenzwerte.json — Dateinamen von
// Backitup tragen den Sicherungszeitpunkt und gehoeren damit ebenfalls nicht ins
// Repository. Ohne Eintrag wird das jeweils neueste passende Archiv im Ordner genommen.
var full = WaehleArchiv("voll-backup", "iobroker_*_backupiobroker.tar.gz");
var js = WaehleArchiv("skript-backup", "javascripts_*_backupiobroker.tar.gz");

// Admin-Export eines echten Blockly-Skripts als Vergleichsmassstab. Dateiname und die
// zugehoerige Skript-ID stehen in testdaten/ (nie im Repository) - der Quelltext nennt
// keine Skriptnamen einer echten Anlage.
var refXml = Path.Combine(testdaten, "blockly-referenz.xml");
var refIdFile = Path.Combine(testdaten, "blockly-referenz.txt");
var refId = File.Exists(refIdFile) ? File.ReadAllText(refIdFile).Trim() : null;

// Voll-Backup einer zweiten, eigenstaendigen ioBroker-Installation. Optional — nur damit
// laesst sich die Herkunftspruefung gegen echte Daten statt gegen Konstrukte testen.
var second = WaehleArchiv("zweites-voll-backup", null);

var passed = 0;
var failed = 0;
var skipped = 0;

// Pruefblöcke, die gar nicht erst gelaufen sind — nicht nur mitgezaehlt, sondern am Ende
// beim Namen genannt. Anlass: Die Ausfuehrungstests des Aufraeum-Skripts blieben bei jedem
// Build stillschweigend aus, weil bash nicht im Pfad von pwsh steht. Die Hinweiszeile stand
// in der Ausgabe, ging aber zwischen 800 Zeilen unter. Ein Lauf, der etwas nicht geprueft
// hat, muss das am Schluss sagen — sonst liest man gruen, wo nur nichts geprueft wurde.
var nichtGelaufen = new List<string>();

if (referenzwerte.Count == 0)
    Console.WriteLine("Hinweis: testdaten/referenzwerte.json fehlt — bestandsabhaengige " +
                      "Pruefungen werden uebersprungen.\n");

void Check(string label, bool ok, string? detail = null)
{
    if (ok) { passed++; Console.WriteLine($"  [OK]   {label}"); }
    else { failed++; Console.WriteLine($"  [FEHL] {label}{(detail is null ? "" : "  -> " + detail)}"); }
}

void CheckEq<T>(string label, T actual, T expected)
    => Check($"{label}: {actual}", Equals(actual, expected), $"erwartet {expected}");

/// <summary>
/// Wie <see cref="CheckEq{T}"/>, aber der Erwartungswert kommt aus
/// testdaten/referenzwerte.json. Ohne hinterlegten Wert gilt die Pruefung als
/// uebersprungen — der Istwert wird trotzdem angezeigt, damit ein neuer Referenzwert
/// direkt ablesbar ist.
/// </summary>
void CheckRef(string label, int actual, string schluessel)
{
    if (!referenzwerte.TryGetValue(schluessel, out var expected))
    {
        skipped++;
        Console.WriteLine($"  [--]   {label}: {actual}  (kein Referenzwert \"{schluessel}\")");
        return;
    }
    CheckEq(label, actual, expected);
}

/// <summary>
/// Liest testdaten/referenzwerte.json: Zahlen werden zu Erwartungswerten, Zeichenketten
/// zu Archivnamen. Fehlt oder bricht die Datei, laeuft der Rest trotzdem durch.
/// </summary>
static (Dictionary<string, int> Werte, Dictionary<string, string> Dateien) LadeReferenzen(string pfad)
{
    var werte = new Dictionary<string, int>(StringComparer.Ordinal);
    var dateien = new Dictionary<string, string>(StringComparer.Ordinal);
    if (!File.Exists(pfad)) return (werte, dateien);

    try
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(pfad));
        foreach (var p in doc.RootElement.EnumerateObject())
        {
            if (p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetInt32(out var v))
                werte[p.Name] = v;
            else if (p.Value.ValueKind == JsonValueKind.String && p.Name[0] != '_')
                dateien[p.Name] = p.Value.GetString()!;
        }
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"referenzwerte.json nicht lesbar: {ex.Message}");
    }
    return (werte, dateien);
}

// ---------------------------------------------------------------- Skript-Backup

Console.WriteLine("=== JavaScript-Backup (javascripts_*.tar.gz) ===");
var sw = Stopwatch.StartNew();
var scriptsOnly = BackupLoader.Load(js);
sw.Stop();
Console.WriteLine($"  Ladezeit: {sw.ElapsedMilliseconds} ms");

CheckRef("Skripte", scriptsOnly.Scripts.Count, "skripte");
CheckEq("Erkannt als ScriptsOnly", scriptsOnly.Kind, BackupKind.ScriptsOnly);
CheckRef("Blockly", scriptsOnly.Scripts.Count(s => s.Engine == ScriptEngine.Blockly), "skripte-blockly");
CheckRef("JavaScript", scriptsOnly.Scripts.Count(s => s.Engine == ScriptEngine.JavaScript), "skripte-javascript");
CheckRef("Deaktiviert", scriptsOnly.ScriptsDisabled, "skripte-deaktiviert");
CheckEq("Uebersprungen", scriptsOnly.SkippedCount, 0);
Check("Backup-Datum aus Dateiname geparst",
      scriptsOnly.CreatedAt == new DateTime(2026, 8, 10, 9, 0, 26),
      scriptsOnly.CreatedAt?.ToString("o"));

var brokenXml = scriptsOnly.Scripts.Count(s => s.BlocklyBroken);
CheckEq("Blockly ohne dekodierbares XML", brokenXml, 0);

// ------------------------------------------------- Blockly gegen Admin-Export

Console.WriteLine();
Console.WriteLine("=== Blockly-Roundtrip gegen echten Admin-Export ===");

// Welches Skript als Referenz dient, steht in testdaten/ und nicht hier: Aus dem
// Quelltext soll nicht hervorgehen, wie die Skripte einer echten Anlage heissen.
var referenz = refId is null
    ? null
    : scriptsOnly.Scripts.FirstOrDefault(s => s.Id == refId);

if (refId is null || !File.Exists(refXml))
{
    Console.WriteLine("  [--]   keine Blockly-Referenz in testdaten/ - uebersprungen");
    nichtGelaufen.Add("Blockly-Vergleich gegen die Referenz (keine Referenz in testdaten/)");
}
else if (referenz is null)
{
    Check("Referenz-Skript im Backup gefunden", false, refId);
}
else
{
    // Ordner und Name muessen dem entsprechen, was die ID vorgibt - genau das ist die
    // Zerlegung, die der Lader leisten soll.
    var ohnePraefix = refId["script.js.".Length..];
    var letzterPunkt = ohnePraefix.LastIndexOf('.');
    var erwarteterOrdner = letzterPunkt < 0 ? "" : ohnePraefix[..letzterPunkt];
    var erwarteterName = letzterPunkt < 0 ? ohnePraefix : ohnePraefix[(letzterPunkt + 1)..];

    Check("XML dekodiert", referenz.BlocklyXml is not null);
    Check("Ordnerpfad entspricht der ID", referenz.Folder == erwarteterOrdner);
    Check("Name entspricht der ID", referenz.Name == erwarteterName);
    Check("CleanSource ohne Base64-Blob",
          !referenz.CleanSource.Contains("//PHhtbCB") && referenz.CleanSource.Length > 100);

    var expected = File.ReadAllText(refXml);
    Check("XML strukturell identisch zum Admin-Export",
          XmlEqual(referenz.BlocklyXml ?? "", expected));
}

// ------------------------------------------------- Hinweise zum Blockly-Aufbau

Console.WriteLine();
Console.WriteLine("=== Skript-Hinweise (Blockly-Aufbau) ===");

// Konstruierte Faelle statt echter Skripte: Jede Regel wird an genau dem Aufbau
// geprueft, den sie meldet - und, wichtiger, an dem, den sie NICHT melden darf.
static string Rumpf() => "<statement name=\"STATEMENT\"><block type=\"update\"/></statement>";

static IReadOnlyList<ScriptHint> Hinweise(string inneresXml)
    => ScriptQualityAnalyzer.Analyze($"<xml xmlns=\"https://developers.google.com/blockly/xml\">{inneresXml}</xml>");

// Der entscheidende Fall: Zwei Trigger NEBENeinander haengen ueber <next> aneinander und
// sind im XML genauso eingerueckt wie ein verschachtelter. Ohne diese Unterscheidung
// meldet die Pruefung jede normale Anlage falsch.
var nebeneinander = Hinweise(
    $"<block type=\"on\" id=\"A\">{Rumpf()}<next><block type=\"on\" id=\"B\">{Rumpf()}</block></next></block>");
CheckEq("Zwei Trigger nebeneinander (<next>): kein Befund", nebeneinander.Count, 0);

// Und derselbe Aufbau als echte Verschachtelung.
var ineinander = Hinweise(
    "<block type=\"on\" id=\"A\"><statement name=\"STATEMENT\">"
  + $"<block type=\"on\" id=\"B\">{Rumpf()}</block></statement></block>");
Check("Trigger im Trigger erkannt",
      ineinander.Count == 1
      && ineinander[0].Kind == ScriptHintKind.TriggerInTrigger
      && ineinander[0].BlockId == "B",
      string.Join(" / ", ineinander.Select(h => h.ShortText + ":" + h.BlockId)));

// on_source ist der Baustein "Ausloesung durch". Er steht immer im Rumpf eines Triggers
// und ist selbst keiner - eine Typliste, die ihn mitzaehlt, meldet fast jedes Skript.
var mitOnSource = Hinweise(
    "<block type=\"on\" id=\"A\"><statement name=\"STATEMENT\">"
  + "<block type=\"update\"><value name=\"VALUE\"><block type=\"on_source\"/></value></block>"
  + "</statement></block>");
CheckEq("on_source im Rumpf: kein Befund", mitOnSource.Count, 0);

// schedule_create ist dafuer gedacht, zur Laufzeit einen Zeitplan anzulegen - im Rumpf
// eines Triggers ist er richtig aufgehoben und darf nicht gemeldet werden.
var scheduleCreate = Hinweise(
    "<block type=\"on\" id=\"A\"><statement name=\"STATEMENT\">"
  + $"<block type=\"schedule_create\" id=\"C\">{Rumpf()}</block></statement></block>");
CheckEq("schedule_create im Rumpf: kein Befund", scheduleCreate.Count, 0);

var abgeloest = Hinweise(
    $"<block type=\"on\" id=\"A\"><statement name=\"STATEMENT\"><block type=\"request\" id=\"R\"/></statement></block>");
Check("Abgeloester Baustein (request) erkannt",
      abgeloest.Count == 1
      && abgeloest[0].Kind == ScriptHintKind.DeprecatedBlock
      && abgeloest[0].BlockId == "R",
      string.Join(" / ", abgeloest.Select(h => h.ShortText)));

var ohneRumpf = Hinweise("<block type=\"on\" id=\"A\"/>");
Check("Trigger ohne Inhalt erkannt",
      ohneRumpf.Count == 1 && ohneRumpf[0].Kind == ScriptHintKind.TriggerWithoutBody,
      string.Join(" / ", ohneRumpf.Select(h => h.ShortText)));

// Kein XML und kaputtes XML duerfen nicht werfen: Ein Skript ohne dekodierbares Blockly
// ist bereits ueber BlocklyBroken gemeldet und soll hier nicht ein zweites Mal auffallen.
CheckEq("Ohne XML: keine Hinweise", ScriptQualityAnalyzer.Analyze(null).Count, 0);
CheckEq("Kaputtes XML: keine Hinweise statt Absturz",
        ScriptQualityAnalyzer.Analyze("<xml><block type=\"on\"").Count, 0);

// JavaScript und TypeScript werden bewusst nicht textuell geprueft - dort gaebe es nur
// Fehltreffer. Die Aufbau-Befunde bleiben bei ihnen leer. Ausgenommen sind die beiden
// Schalter am Skript-Objekt (Debuggen, Ausfuehrliche Protokollausgaben): Die haengen nicht
// an einem Baustein und gelten fuer jede Sprache.
var nichtBlocklyMitHinweis = scriptsOnly.Scripts
    .Count(s => s.Engine != ScriptEngine.Blockly
             && s.Hints.Any(h => h.Kind is not (ScriptHintKind.DebugMode
                                             or ScriptHintKind.VerboseLogging)));
CheckEq("Aufbau-Hinweise ausschliesslich bei Blockly", nichtBlocklyMitHinweis, 0);

// ------------------------------------------- Debuggen / Ausfuehrliche Protokollausgaben
//
// Beide Schalter stehen in common.debug und common.verbose (js-controller, ScriptCommon).
// "Debuggen" ist kein Protokollschalter: Der javascript-Adapter unterdrueckt damit jede
// schreibende Operation und protokolliert sie nur als Warnung - das Skript laeuft und tut
// nichts. Deshalb gehoert es in die Hinweisspalte.

var nurDebug = ScriptQualityAnalyzer.Analyze(null, debug: true);
Check("Debug-Schalter ergibt einen Hinweis, auch ohne XML",
      nurDebug.Count == 1 && nurDebug[0].Kind == ScriptHintKind.DebugMode,
      string.Join(" / ", nurDebug.Select(h => h.ShortText)));

var nurVerbose = ScriptQualityAnalyzer.Analyze(null, verbose: true);
Check("Verbose-Schalter ergibt einen Hinweis, auch ohne XML",
      nurVerbose.Count == 1 && nurVerbose[0].Kind == ScriptHintKind.VerboseLogging,
      string.Join(" / ", nurVerbose.Select(h => h.ShortText)));

CheckEq("Ohne Schalter kein Hinweis", ScriptQualityAnalyzer.Analyze(null).Count, 0);
CheckEq("Beide Schalter ergeben zwei Hinweise",
        ScriptQualityAnalyzer.Analyze(null, debug: true, verbose: true).Count, 2);

var beideMitXml = ScriptQualityAnalyzer.Analyze(
    "<xml xmlns=\"https://developers.google.com/blockly/xml\"><block type=\"on\" id=\"A\"/></xml>",
    debug: true, verbose: true);
Check("Schalter und Aufbau-Befunde stehen nebeneinander", beideMitXml.Count == 3,
      string.Join(" / ", beideMitXml.Select(h => h.ShortText)));

// ------------------------------------------------------- steuern / aktualisieren (ack)
//
// control erzeugt setState(id, wert)        -> ack=false, Befehl an den Adapter
// update  erzeugt setState(id, wert, true)  -> ack=true,  reine Wertmeldung
// (javascript-Adapter, blocks_system.ts; js-controller zum ack-Feld: "Direction flag:
// false for desired value and true for actual value")

static string AckXml(string blocktyp, string oid) =>
    $"<xml xmlns=\"https://developers.google.com/blockly/xml\">" +
    $"<block type=\"{blocktyp}\" id=\"B1\"><field name=\"OID\">{oid}</field></block></xml>";

ScriptQualityAnalyzer.StateOwner Besitzer(string id) =>
    id.StartsWith("0_userdata.", StringComparison.Ordinal)
        ? ScriptQualityAnalyzer.StateOwner.Own
    : id.StartsWith("hue.", StringComparison.Ordinal)
        ? ScriptQualityAnalyzer.StateOwner.Adapter
        : ScriptQualityAnalyzer.StateOwner.Unknown;

var unquittiert = ScriptQualityAnalyzer.AckHints(
    AckXml("control", "0_userdata.0.zaehler"), Besitzer, _ => true);
Check("Steuern auf unquittiertem eigenem Datenpunkt wird gemeldet",
      unquittiert.Count == 1 && unquittiert[0].Kind == ScriptHintKind.ControlOnOwnState
      && unquittiert[0].Detail == "0_userdata.0.zaehler",
      string.Join(" / ", unquittiert.Select(h => h.ShortText)));

CheckEq("Steuern auf quittiertem eigenem Datenpunkt bleibt still",
        ScriptQualityAnalyzer.AckHints(
            AckXml("control", "0_userdata.0.befehl"), Besitzer, _ => false).Count, 0);

var falschHerum = ScriptQualityAnalyzer.AckHints(
    AckXml("update", "hue.0.lampe.on"), Besitzer, _ => false);
Check("Aktualisieren auf Adapter-Datenpunkt wird gemeldet",
      falschHerum.Count == 1 && falschHerum[0].Kind == ScriptHintKind.UpdateOnAdapterState,
      string.Join(" / ", falschHerum.Select(h => h.ShortText)));

CheckEq("Steuern auf Adapter-Datenpunkt ist richtig und bleibt still",
        ScriptQualityAnalyzer.AckHints(
            AckXml("control", "hue.0.lampe.on"), Besitzer, _ => true).Count, 0);
CheckEq("Aktualisieren auf eigenem Datenpunkt ist richtig und bleibt still",
        ScriptQualityAnalyzer.AckHints(
            AckXml("update", "0_userdata.0.zaehler"), Besitzer, _ => true).Count, 0);
CheckEq("Unbekannter Namensraum wird nicht beurteilt",
        ScriptQualityAnalyzer.AckHints(
            AckXml("update", "fremd.0.wert"), Besitzer, _ => true).Count, 0);
CheckEq("control_ex bleibt aussen vor",
        ScriptQualityAnalyzer.AckHints(
            AckXml("control_ex", "hue.0.lampe.on"), Besitzer, _ => true).Count, 0);

// --------------------------------------------------------------- Befehlskanaele
//
// Ein eigener Datenpunkt, den ein anderes Skript als Befehl entgegennimmt, gehoert mit
// "steuern" beschrieben - dort waere der Hinweis falsch. Beleg ist ein Ausloeser, der
// etwas TUT und dabei auf Befehle lauscht (ACK_CONDITION=false) oder quittiert
// (Baustein on_ack_value).
//
// Der Rumpf muss etwas tun: Ein Ausloeser, der nur quittiert, ist ein Pflaster gegen die
// rote Darstellung unquittierter Werte in der Objektuebersicht - kein Befehlskanal.
// Wuerde er als solcher zaehlen, verschwiege das Werkzeug genau den Befund, der das
// Sammelskript ueberfluessig machen wuerde.

static string TriggerXml(string oid, string ackBedingung, string rumpf) =>
    "<xml xmlns=\"https://developers.google.com/blockly/xml\">"
  + $"<block type=\"on\" id=\"T1\"><field name=\"OID\">{oid}</field>"
  + (ackBedingung.Length > 0 ? $"<field name=\"ACK_CONDITION\">{ackBedingung}</field>" : "")
  + $"<statement name=\"STATEMENT\">{rumpf}</statement></block></xml>";

const string AckBaustein = "<block type=\"on_ack_value\" id=\"Q1\"></block>";
const string EchteArbeit = "<block type=\"control\" id=\"C1\">"
                         + "<field name=\"OID\">hue.0.lampe.on</field></block>";

var kanalBefehl = ScriptQualityAnalyzer.AcknowledgedStates(
    TriggerXml("0_userdata.0.befehl", "false", EchteArbeit));
Check("Ausloeser auf Befehle mit echtem Rumpf gilt als Befehlskanal",
      kanalBefehl.Contains("0_userdata.0.befehl"), string.Join(", ", kanalBefehl));

var kanalQuittiert = ScriptQualityAnalyzer.AcknowledgedStates(
    TriggerXml("0_userdata.0.befehl", "", EchteArbeit + AckBaustein));
Check("Ausloeser, der arbeitet und quittiert, gilt als Befehlskanal",
      kanalQuittiert.Contains("0_userdata.0.befehl"), string.Join(", ", kanalQuittiert));

var nurQuittieren = ScriptQualityAnalyzer.AcknowledgedStates(
    TriggerXml("0_userdata.0.anzeige", "false", AckBaustein));
CheckEq("Ausloeser, der NUR quittiert, ist kein Befehlskanal", nurQuittieren.Count, 0);

var ohneAlles = ScriptQualityAnalyzer.AcknowledgedStates(
    TriggerXml("0_userdata.0.wert", "", EchteArbeit));
CheckEq("Gewoehnlicher Ausloeser ohne ack-Bezug ist kein Befehlskanal", ohneAlles.Count, 0);

CheckEq("update-Baustein allein macht keinen Befehlskanal",
        ScriptQualityAnalyzer.AcknowledgedStates(AckXml("update", "0_userdata.0.wert")).Count, 0);

// Wirkung auf den Hinweis: derselbe unquittierte Datenpunkt, einmal mit und einmal ohne
// Befehlskanal.
CheckEq("Befehlskanal stellt den Hinweis still",
        ScriptQualityAnalyzer.AckHints(AckXml("control", "0_userdata.0.befehl"), Besitzer,
                                       _ => true, id => id == "0_userdata.0.befehl").Count, 0);
CheckEq("Ohne Befehlskanal bleibt der Hinweis stehen",
        ScriptQualityAnalyzer.AckHints(AckXml("control", "0_userdata.0.zaehler"), Besitzer,
                                       _ => true, _ => false).Count, 1);

// Ein OID aus einem eingesetzten Wert-Baustein gehoert nicht zum aeusseren Block: Sonst
// wuerde "aktualisiere Zaehler mit dem Wert der Lampe" auf der Lampe gemeldet.
var verschachtelt = ScriptQualityAnalyzer.AckHints(
    "<xml xmlns=\"https://developers.google.com/blockly/xml\">"
  + "<block type=\"update\" id=\"B1\"><field name=\"OID\">0_userdata.0.zaehler</field>"
  + "<value name=\"VALUE\"><block type=\"get_value\" id=\"B2\">"
  + "<field name=\"OID\">hue.0.lampe.on</field></block></value></block></xml>",
    Besitzer, _ => true);
CheckEq("OID eines eingesetzten Bausteins zaehlt nicht zum aeusseren Block",
        verschachtelt.Count, 0);

// Gegen die echten Skripte: Wie viele Skripte tragen einen Befund? Der Erwartungswert
// steht - wie alle Bestandszahlen - in testdaten/referenzwerte.json.
var mitHinweis = scriptsOnly.Scripts.Count(s => s.Hints.Count > 0);
CheckRef("Skripte mit Hinweisen", mitHinweis, "skripte-mit-hinweisen");

foreach (var kind in Enum.GetValues<ScriptHintKind>())
{
    var anzahl = scriptsOnly.Scripts.Sum(s => s.Hints.Count(h => h.Kind == kind));
    Console.WriteLine($"    {kind,-20}: {anzahl}");
}

// -------------------------------------------- Erzeugtes Archiv: Sonderfaelle im Aufbau
//
// Diese Faelle kommen in den vorhandenen Testbackups nicht vor, sind aber genau die, an
// denen ein fremdes Backup scheitern kann. Das Archiv wird deshalb erfunden und erzeugt -
// es enthaelt keinerlei Daten aus einer echten Anlage.

Console.WriteLine();
Console.WriteLine("=== Erzeugtes Archiv (Sonderfaelle) ===");

var kunstOrdner = Path.Combine(Path.GetTempPath(), "iob-analyzer-pruefarchiv");
var kunstArchiv = Path.Combine(kunstOrdner, "pruef.tar.gz");
ErzeugePruefarchiv(kunstArchiv);

var kunst = BackupLoader.Load(kunstArchiv);

CheckEq("Erzeugtes Archiv wird als Voll-Backup erkannt", kunst.Kind, BackupKind.Full);
CheckEq("Die echte objects.jsonl wurde verwendet", kunst.Objects.Count, 2);
Check("Die untergeschobene objects.jsonl wurde NICHT verwendet",
      kunst.Objects.Any(o => o.Id == "pruefadapter.0.echt")
      && kunst.Objects.All(o => o.Id != "fremd.0.untergeschoben"),
      string.Join(", ", kunst.Objects.Select(o => o.Id)));
Check("Der uebergangene Fund wird benannt statt verschwiegen",
      kunst.Validation.IgnoredDuplicates.Count == 1
      && kunst.Validation.IgnoredDuplicates[0].Contains("fremdadapter", StringComparison.Ordinal),
      string.Join(" / ", kunst.Validation.IgnoredDuplicates));

// Stroemende JSON-Pruefung: dieselben Urteile wie zuvor, nur ohne die Datei zu puffern.
JsonFileCheck Datei(string endeAuf) =>
    kunst.Validation.OptionalFiles.First(f => f.Path.EndsWith(endeAuf, StringComparison.Ordinal));

Check("Gueltige JSON gilt als gueltig", Datei("gut.json").Valid);
Check("JSON mit //-Kommentar gilt als ungueltig", !Datei("kommentar.json").Valid);
Check("Leere JSON gilt als ungueltig", !Datei("leer.json").Valid);
Check("JSON mit BOM gilt als ungueltig", !Datei("bom.json").Valid);
Check("BOM wird als Ursache benannt",
      Datei("bom.json").Error.Contains("BOM", StringComparison.Ordinal), Datei("bom.json").Error);
Check("Abgeschnittene JSON gilt als ungueltig", !Datei("abgeschnitten.json").Valid);

// Eine grosse Datei darf keinen nennenswerten Speicher kosten - genau dafuer wurde die
// Pruefung auf einen Datenstrom umgestellt.
var vorher = GC.GetTotalMemory(forceFullCollection: true);
var kunst2 = BackupLoader.Load(kunstArchiv);
var nachher = GC.GetTotalMemory(forceFullCollection: true);
var zuwachsMB = (nachher - vorher) / 1048576.0;
Console.WriteLine($"  Grosse JSON im Archiv: 40 MB   Speicherzuwachs beim Laden: {zuwachsMB:N1} MB");
Check("Grosse JSON belegt keinen Speicher in Dateigroesse", zuwachsMB < 20, $"{zuwachsMB:N1} MB");
Check("Grosse JSON wurde trotzdem geprueft", Datei("gross.json").Valid);
CheckEq("Zweiter Ladevorgang liefert dasselbe", kunst2.Objects.Count, kunst.Objects.Count);

try { Directory.Delete(kunstOrdner, true); } catch { /* Aufraeumen ist Kuer */ }

// ---------------------------------------------------------------- Voll-Backup

Console.WriteLine();
Console.WriteLine("=== Voll-Backup (iobroker_*.tar.gz) ===");
sw.Restart();
var fullData = BackupLoader.Load(full);
sw.Stop();
Console.WriteLine($"  Ladezeit: {sw.ElapsedMilliseconds} ms  (Zielwert < 15000 ms)");
Check("Ladezeit unter 15 s", sw.ElapsedMilliseconds < 15000, $"{sw.ElapsedMilliseconds} ms");

CheckEq("Erkannt als Full", fullData.Kind, BackupKind.Full);
CheckRef("Objekte", fullData.Objects.Count, "objekte");
CheckRef("States gezaehlt", fullData.StateCount, "states");
CheckRef("Adapter-Instanzen", fullData.Instances.Count, "instanzen");
CheckRef("Skripte", fullData.Scripts.Count, "skripte");
CheckEq("Uebersprungen", fullData.SkippedCount, 0);
CheckEq("VIS-View-Dateien gelesen", fullData.VisViews.Count, 2);
Check("VIS-Views nicht leer", fullData.VisViews.All(v => v.Content.Length > 1000));

// Skripte aus beiden Quellen muessen uebereinstimmen.
var idsA = scriptsOnly.Scripts.Select(s => s.Id).OrderBy(x => x).ToArray();
var idsB = fullData.Scripts.Select(s => s.Id).OrderBy(x => x).ToArray();
Check("Skript-IDs in Voll- und JS-Backup identisch", idsA.SequenceEqual(idsB),
      $"{idsA.Length} vs {idsB.Length}");

// Stichprobe Adapter-Versionen (Abnahmetest 5).
Console.WriteLine("  Stichprobe Adapter-Instanzen:");
foreach (var i in fullData.Instances.Take(5))
    Console.WriteLine($"    {i.Namespace,-28} v{i.Version,-12} aktiv={i.EnabledText,-5} Objekte={i.ObjectCount}");

var withVersion = fullData.Instances.Count(i => !string.IsNullOrEmpty(i.Version));
Check("Alle Instanzen haben eine Version", withVersion == fullData.Instances.Count,
      $"{withVersion}/{fullData.Instances.Count}");

var withObjects = fullData.Instances.Count(i => i.ObjectCount > 0);
Check("Mehrheit der Instanzen hat zugeordnete Objekte",
      withObjects > fullData.Instances.Count / 2, $"{withObjects}/{fullData.Instances.Count}");

// ------------------------------------------------- Objektlimit je Instanz (js-controller)
//
// Der js-controller meldet beim Start jeder Instanz „This instance has N objects, the limit
// for this instance is set to M." und legt eine System-Meldung an (numberObjectsLimitExceeded).
// Die Uebersicht rechnet dasselbe aus dem Backup aus. Geprueft wird beides: das Lesen des
// Limits aus dem Backup und die Schwellenlogik des Presenters.

var mitEigenemLimit = fullData.Instances.Count(i => i.ObjectLimit != AdapterInstance.DefaultObjectLimit);
var limitObjekte = fullData.Objects.Count(o => o.ObjectsWarnLimit is not null);
Console.WriteLine($"  Objektlimit: {limitObjekte} objectsWarnLimit-Objekte gelesen, " +
                  $"{mitEigenemLimit} Instanzen mit abweichendem Limit, " +
                  $"{OverviewPresenter.OverObjectLimit(fullData).Count} ueber ihrem Limit");

Check("objectsWarnLimit-Objekte im Backup gefunden", limitObjekte > 0, $"{limitObjekte}");
Check("Jede Instanz hat ein Limit groesser null",
      fullData.Instances.All(i => i.ObjectLimit > 0));
Check("Ueberschreitungen stimmen mit den Zahlen ueberein",
      OverviewPresenter.OverObjectLimit(fullData).All(i => i.ObjectCount > i.ObjectLimit));

// common.def wird nur am objectsWarnLimit-Objekt als Limit gelesen — an keinem anderen.
// Sonst wuerde der Vorgabewert eines beliebigen Datenpunkts als Schwelle gelten.
Check("Limit nur an objectsWarnLimit-Objekten gelesen",
      fullData.Objects.Where(o => o.ObjectsWarnLimit is not null).All(o =>
          o.Id.StartsWith("system.adapter.", StringComparison.Ordinal)
          && o.Id.EndsWith(".objectsWarnLimit", StringComparison.Ordinal)));

// Zuordnung Objekt -> Instanz: Was im Backup steht, muss an der Instanz ankommen.
var limitAusBackup = fullData.Objects
    .Where(o => o.ObjectsWarnLimit is not null)
    .ToDictionary(
        o => o.Id["system.adapter.".Length..^".objectsWarnLimit".Length],
        o => o.ObjectsWarnLimit!.Value,
        StringComparer.OrdinalIgnoreCase);

var zugeordnet = fullData.Instances.Count(i => limitAusBackup.ContainsKey(i.Namespace));
Check("Limit aus dem Backup landet bei der Instanz", zugeordnet > 0
      && fullData.Instances.All(i => !limitAusBackup.TryGetValue(i.Namespace, out var l)
                                     || i.ObjectLimit == l),
      $"{zugeordnet}/{fullData.Instances.Count} Instanzen mit eigenem Objekt");

// Ohne eigenes Objekt bleibt es bei der Systemvorgabe des js-controllers.
Check("Ohne eigenes Objekt gilt die Systemvorgabe",
      fullData.Instances.Where(i => !limitAusBackup.ContainsKey(i.Namespace))
                        .All(i => i.ObjectLimit == AdapterInstance.DefaultObjectLimit));

// Schwellenlogik: genau auf dem Limit ist noch keine Ueberschreitung — der js-controller
// vergleicht ebenfalls echt groesser.
BackupData Kunstanlage(params AdapterInstance[] instanzen) => new()
{
    SourceFile = fullData.SourceFile,
    Kind = BackupKind.Full,
    Instances = instanzen.ToList()
};

var genauAufLimit = new AdapterInstance
    { Adapter = "grenz", Instance = 0, Enabled = true, ObjectCount = 5000, ObjectLimit = 5000 };
var einsDarueber = new AdapterInstance
    { Adapter = "grenz", Instance = 1, Enabled = true, ObjectCount = 5001, ObjectLimit = 5000 };

Check("Genau auf dem Limit ist keine Ueberschreitung", !genauAufLimit.OverObjectLimit);
Check("Ein Objekt darueber ist eine Ueberschreitung", einsDarueber.OverObjectLimit);
Check("Ohne Ueberschreitung keine Warnzeile",
      OverviewPresenter.ObjectLimitWarning(Kunstanlage(genauAufLimit)) is null);

var eineWarnung = OverviewPresenter.ObjectLimitWarning(Kunstanlage(genauAufLimit, einsDarueber));
Check("Warnzeile nennt Instanz, Zahl und Limit",
      eineWarnung is not null && eineWarnung.Contains("grenz.1")
      && eineWarnung.Contains("5.001") && eineWarnung.Contains("5.000"),
      eineWarnung);
Check("Warnzeile im Singular bei einer Instanz",
      eineWarnung!.Contains("1 Instanz über dem Objekt-Limit"));
Check("Warnzeile nennt nur die betroffene Instanz", !eineWarnung.Contains("grenz.0"));

// Deaktivierte Instanzen starten nicht und melden im Betrieb nichts — sie werden deshalb
// gekennzeichnet, aber nicht verschwiegen: ihre Objekte liegen trotzdem in der Datenbank.
var ausgeschaltet = new AdapterInstance
    { Adapter = "ruht", Instance = 0, Enabled = false, ObjectCount = 9000, ObjectLimit = 5000 };
var mitRuhender = OverviewPresenter.ObjectLimitWarning(Kunstanlage(ausgeschaltet));
Check("Deaktivierte Instanz wird als solche gekennzeichnet",
      mitRuhender is not null && mitRuhender.Contains("(deaktiviert)"), mitRuhender);

// Viele Treffer: hoechstens acht werden genannt, der Rest gezaehlt.
var viele = Enumerable.Range(0, 11)
    .Select(n => new AdapterInstance
        { Adapter = "viel", Instance = n, Enabled = true, ObjectCount = 6000 + n, ObjectLimit = 5000 })
    .ToArray();
var vieleText = OverviewPresenter.ObjectLimitWarning(Kunstanlage(viele))!;
Check("Warnzeile kuerzt lange Listen ab", vieleText.Contains("und 3 weitere"),
      vieleText.Length > 120 ? vieleText[..120] + " …" : vieleText);
Check("Warnzeile im Plural bei mehreren Instanzen", vieleText.Contains("11 Instanzen"));

// Groesste zuerst — die auffaelligste Instanz steht vorn.
var reihenfolge = OverviewPresenter.OverObjectLimit(Kunstanlage(viele));
Check("Ueberschreitungen sind absteigend sortiert",
      reihenfolge.Select(i => i.ObjectCount).SequenceEqual(
          reihenfolge.Select(i => i.ObjectCount).OrderByDescending(x => x)));

// --------------------------------- Skript-Befunde gegen die echten Skripte

var debugAusBackup = fullData.Scripts.Count(s => s.Debug);
var verboseAusBackup = fullData.Scripts.Count(s => s.Verbose);
Console.WriteLine($"  Schalter im Backup: Debuggen={debugAusBackup}, Verbose={verboseAusBackup}");
Check("Debug-Schalter fuehrt zum Hinweis am Skript",
      fullData.Scripts.All(s => s.Debug == s.Hints.Any(h => h.Kind == ScriptHintKind.DebugMode)));
Check("Verbose-Schalter fuehrt zum Hinweis am Skript",
      fullData.Scripts.All(s => s.Verbose == s.Hints.Any(h => h.Kind == ScriptHintKind.VerboseLogging)));

// Gegen die echten Skripte.
var ackBefunde = fullData.Scripts
    .SelectMany(s => s.Hints.Where(h => h.Kind is ScriptHintKind.ControlOnOwnState
                                             or ScriptHintKind.UpdateOnAdapterState))
    .ToList();
Console.WriteLine("  ack-Befunde im Backup: " +
    $"{ackBefunde.Count(h => h.Kind == ScriptHintKind.ControlOnOwnState)}x steuern-auf-eigen, " +
    $"{ackBefunde.Count(h => h.Kind == ScriptHintKind.UpdateOnAdapterState)}x aktualisieren-auf-adapter");

Check("Jeder ack-Befund nennt seinen Datenpunkt",
      ackBefunde.All(h => h.Detail.Length > 0));
Check("Gemeldete eigene Datenpunkte liegen wirklich unquittiert",
      ackBefunde.Where(h => h.Kind == ScriptHintKind.ControlOnOwnState)
                .All(h => h.Detail.StartsWith("alias.", StringComparison.Ordinal)
                          || (fullData.States.TryGetValue(h.Detail, out var st) && !st.Ack)));
Check("Skript-Backups erzeugen keine ack-Befunde (kein Objektbestand)",
      scriptsOnly.Scripts.SelectMany(s => s.Hints).All(h =>
          h.Kind is not (ScriptHintKind.ControlOnOwnState or ScriptHintKind.UpdateOnAdapterState)));


// ---------------------------------------------------------------- Analysen

Console.WriteLine();
Console.WriteLine("=== Saeule 3: Analysen ===");
sw.Restart();
var orphans = OrphanAnalyzer.FindOrphanObjects(fullData);
var unused = OrphanAnalyzer.FindUnusedDatapoints(fullData);
sw.Stop();
Console.WriteLine($"  Analysedauer: {sw.ElapsedMilliseconds} ms");

Console.WriteLine($"  Analyse A — Objekt-Leichen: {orphans.Count}");
foreach (var g in orphans.GroupBy(o => o.MissingInstance).OrderByDescending(g => g.Count()).Take(8))
    Console.WriteLine($"    {g.Key,-30} {g.Count()} Objekte");

Check("Analyse A enthaelt keine System-Namespaces",
      !orphans.Any(o => o.Id.StartsWith("system.") || o.Id.StartsWith("alias.")
                     || o.Id.StartsWith("0_userdata.") || o.Id.StartsWith("enum.")
                     || o.Id.StartsWith("script.js.")));

Check("Analyse A meldet keine existierende Instanz als fehlend",
      !orphans.Any(o => fullData.Instances.Any(i =>
          string.Equals(i.Namespace, o.MissingInstance, StringComparison.OrdinalIgnoreCase))));

// Ein Ergebnis von 0 Leichen kann "System sauber" oder "Analyse defekt" bedeuten.
// Gegenprobe: eine real existierende Instanz kuenstlich entfernen — dann muessen genau
// deren Objekte als Leichen auftauchen (entspricht Abnahmetest 6).
var victim = fullData.Instances.Where(i => i.ObjectCount > 10)
                               .OrderBy(i => i.ObjectCount).First();
var reduced = new BackupData
{
    SourceFile = fullData.SourceFile,
    Kind = fullData.Kind,
    Objects = fullData.Objects,
    Scripts = fullData.Scripts,
    Instances = fullData.Instances.Where(i => i != victim).ToList(),
    VisViews = fullData.VisViews
};
var synthetic = OrphanAnalyzer.FindOrphanObjects(reduced);
Console.WriteLine($"  Gegenprobe: Instanz {victim.Namespace} entfernt ({victim.ObjectCount} Objekte)");
Check("Gegenprobe findet die kuenstlich verwaisten Objekte",
      synthetic.Count > 0 && synthetic.All(o =>
          string.Equals(o.MissingInstance, victim.Namespace, StringComparison.OrdinalIgnoreCase)),
      $"{synthetic.Count} Treffer");

var candidates = unused.Where(u => u.IsCandidate).ToList();

// Wirkung der VIS-Pruefung sichtbar machen (siehe STRUKTUR_VERIFIZIERUNG 5).
var withoutVis = unused.Count(u => u.InScripts == FindKind.Nicht && !u.AliasTarget && !u.LoggingActive);
Console.WriteLine($"  Kandidaten ohne VIS-Pruefung: {withoutVis}  ->  mit VIS-Pruefung: {candidates.Count}");
Check("VIS-Pruefung verhindert Falsch-Positive", withoutVis > candidates.Count,
      $"{withoutVis} vs {candidates.Count}");
Console.WriteLine($"  Analyse B — geprueft: {unused.Count}, davon Kandidaten: {candidates.Count}");
Console.WriteLine($"    in Skripten gefunden : {unused.Count(u => u.InScripts == FindKind.Exakt)}");
Console.WriteLine($"    nur Praefix gefunden : {unused.Count(u => u.InScripts == FindKind.NurPraefix)}");
Console.WriteLine($"    in VIS gefunden      : {unused.Count(u => u.InVis == FindKind.Exakt)}");
Console.WriteLine($"    Alias-Ziel           : {unused.Count(u => u.AliasTarget)}");
Console.WriteLine($"    Logging aktiv        : {unused.Count(u => u.LoggingActive)}");

Check("Analyse B prueft ueberhaupt Datenpunkte", unused.Count > 0);

// --------------------------------------------- Analyse B: Index gegen Textsuche
//
// Analyse B durchsuchte fuer JEDEN eigenen Datenpunkt den gesamten VIS- und Skripttext.
// Bei einer Anlage mit sehr vielen eigenen Datenpunkten und einem grossen VIS-Projekt
// dauerte das Minuten. Seit dem Index wird der Text einmal abgesucht und danach nur noch
// nachgeschlagen.
//
// Die geradeheraus geschriebene Fassung bleibt als Massstab erhalten. Beide Wege muessen
// Feld fuer Feld dasselbe liefern - eine schnellere Analyse mit anderen Befunden waere in
// einer Liste, aus der Leute Datenpunkte loeschen, ein besonders unangenehmer Fehler.

static string Fingerabdruck(UnusedDatapoint u) =>
    $"{u.Id}|{u.InScripts}|{u.InVis}|{u.AliasTarget}|{u.LoggingActive}|{u.InChart}|" +
    $"{u.HasState}|{u.LastChange:O}|{u.AgeDays}|{u.IsCandidate}";

var swIndex = System.Diagnostics.Stopwatch.StartNew();
var mitIndex = OrphanAnalyzer.FindUnusedDatapoints(fullData);
swIndex.Stop();

var swOhne = System.Diagnostics.Stopwatch.StartNew();
var ohneIndex = OrphanAnalyzer.FindUnusedDatapointsOhneIndex(fullData);
swOhne.Stop();

Console.WriteLine($"  Analyse B: mit Index {swIndex.ElapsedMilliseconds} ms, " +
                  $"ohne Index {swOhne.ElapsedMilliseconds} ms");

CheckEq("Index und Textsuche finden gleich viele Datenpunkte", mitIndex.Count, ohneIndex.Count);

var abweichungen = mitIndex.Zip(ohneIndex)
                           .Where(p => Fingerabdruck(p.First) != Fingerabdruck(p.Second))
                           .ToList();
Check("Index und Textsuche stufen jeden Datenpunkt gleich ein",
      abweichungen.Count == 0,
      abweichungen.Count == 0
          ? "keine Abweichung"
          : $"{abweichungen.Count} Abweichungen, erste: {Fingerabdruck(abweichungen[0].First)} " +
            $"vs {Fingerabdruck(abweichungen[0].Second)}");

Check("Kandidaten sind echte Teilmenge", candidates.Count < unused.Count,
      $"{candidates.Count}/{unused.Count}");
Check("Kein Kandidat wird in Skripten verwendet",
      candidates.All(c => c.InScripts == FindKind.Nicht));
Check("VIS-Pruefung greift (mind. ein DP nur in VIS gefunden)",
      unused.Any(u => u.InVis == FindKind.Exakt && u.InScripts == FindKind.Nicht));

if (candidates.Count > 0)
{
    Console.WriteLine("  Erste Kandidaten:");
    foreach (var c in candidates.Take(10)) Console.WriteLine($"    {c.Id}");
}

// ---------------------------------------------------------------- ID-Eindeutigkeit

Console.WriteLine();
Console.WriteLine("=== Objekt-IDs: Eindeutigkeit und Schreibweise ===");

var exactIds = new HashSet<string>(StringComparer.Ordinal);
var trueDups = new List<string>();
foreach (var o in fullData.Objects)
    if (!exactIds.Add(o.Id)) trueDups.Add(o.Id);

Check("Keine echten Duplikat-IDs", trueDups.Count == 0, string.Join(", ", trueDups.Take(3)));
CheckEq("Eindeutige IDs entsprechen Objektzahl", exactIds.Count, fullData.Objects.Count);

// IDs, die sich nur in der Gross-/Kleinschreibung unterscheiden, sind fuer ioBroker
// verschiedene Objekte — fast immer aber ein Versehen und deshalb meldenswert.
var caseGroups = fullData.Objects
    .GroupBy(o => o.Id, StringComparer.OrdinalIgnoreCase)
    .Where(g => g.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() > 1)
    .ToList();

Console.WriteLine($"  ID-Paare, die sich nur in der Schreibweise unterscheiden: {caseGroups.Count}");
foreach (var g in caseGroups)
    Console.WriteLine("     " + string.Join("   <->   ", g.Select(x => x.Id).Distinct(StringComparer.Ordinal)));

Check("Vergleiche laufen case-sensitiv (sonst waeren diese Paare unsichtbar)",
      caseGroups.Count == 0 || exactIds.Count == fullData.Objects.Count);

// ---------------------------------------------------------------- VIS

Console.WriteLine();
Console.WriteLine("=== VIS-Datenpunkte (getrennt nach VIS 1 und VIS 2) ===");
Check("VIS 1 erkannt", fullData.VisViews.Any(v => v.Version == VisVersion.Vis1));
Check("VIS 2 erkannt", fullData.VisViews.Any(v => v.Version == VisVersion.Vis2));
foreach (var vf in fullData.VisViews)
    Console.WriteLine($"    {vf.VersionText}  {vf.Path}  ({vf.Content.Length / 1024} KB)");

sw.Restart();
var visDps = VisAnalyzer.Analyze(fullData);
sw.Stop();

var onlyV1 = visDps.Count(d => d.InVis1 && !d.InVis2);
var onlyV2 = visDps.Count(d => !d.InVis1 && d.InVis2);
var both = visDps.Count(d => d.InVis1 && d.InVis2);
var missing = visDps.Where(d => !d.ExistsInBackup).ToList();

Console.WriteLine($"  Analysedauer: {sw.ElapsedMilliseconds} ms");
Console.WriteLine($"  Datenpunkte gesamt : {visDps.Count}");
Console.WriteLine($"    nur VIS 1        : {onlyV1}");
Console.WriteLine($"    nur VIS 2        : {onlyV2}");
Console.WriteLine($"    in beiden        : {both}");
Console.WriteLine($"    ohne Objekt im Backup (totes Widget): {missing.Count}");

Check("VIS-Datenpunkte gefunden", visDps.Count > 0);
Check("Beide VIS-Versionen liefern Treffer", visDps.Any(d => d.InVis1) && visDps.Any(d => d.InVis2));
Check("Jeder Eintrag hat mindestens eine View",
      visDps.All(d => d.Vis1Views.Count + d.Vis2Views.Count > 0));
Check("Platzhalter nothing_selected nicht enthalten",
      !visDps.Any(d => d.Id.Contains("nothing_selected", StringComparison.OrdinalIgnoreCase)));
Check("Keine leeren IDs", visDps.All(d => d.Id.Trim().Length > 0));
Check("Alle IDs sehen wie Objekt-IDs aus (mind. ein Punkt)", visDps.All(d => d.Id.Contains('.')));

// --- VIS-Projekte: eine Installation kann mehrere je VIS-Version haben ---
// Auf einer anderen Anlage lagen drei Projekte unter vis.0. Ohne den
// Projektnamen waeren gleichnamige Views verschiedener Projekte nicht zu unterscheiden —
// bei kopierten Projekten ist Gleichnamigkeit der Normalfall.
Check("Projektname wird aus dem Pfad gelesen",
      fullData.VisViews.All(v => v.Project.Length > 0));
Console.WriteLine("  VIS-Projekte: " +
    string.Join(", ", fullData.VisViews.Select(v => $"{v.VersionText}:{v.Project}")));

// Der Pfad im Archiv ist die Quelle, nicht der Dateiname — gegen erfundene Faelle geprueft.
var mehrereProjekte = new[]
{
    new VisFile { Version = VisVersion.Vis1, Path = "backup/files/vis.0/tablet/vis-views.json", Content = "{}" },
    new VisFile { Version = VisVersion.Vis1, Path = "backup/files/vis.0/main/vis-views.json",   Content = "{}" },
    new VisFile { Version = VisVersion.Vis2, Path = "backup/files/vis-2.0/wandpanel/vis-views.json", Content = "{}" }
};
CheckEq("Projekt aus vis.0/tablet", mehrereProjekte[0].Project, "tablet");
CheckEq("Projekt aus vis.0/main", mehrereProjekte[1].Project, "main");
CheckEq("Projekt aus vis-2.0/wandpanel", mehrereProjekte[2].Project, "wandpanel");

// Gleichnamige Views in verschiedenen Projekten muessen unterscheidbar bleiben.
var ausTablet = new VisUsage
{
    Version = VisVersion.Vis1, Project = "tablet", View = "Wohnzimmer",
    WidgetId = "w00001", Template = "tplValueString", Field = "oid"
};
var ausMain = new VisUsage
{
    Version = VisVersion.Vis1, Project = "main", View = "Wohnzimmer",
    WidgetId = "w00001", Template = "tplValueString", Field = "oid"
};
CheckEq("Gleiche View, anderes Projekt: tablet", ausTablet.ViewPath, "tablet/Wohnzimmer");
CheckEq("Gleiche View, anderes Projekt: main", ausMain.ViewPath, "main/Wohnzimmer");
Check("Beide Fundstellen sind unterscheidbar", ausTablet.ViewPath != ausMain.ViewPath);

// Ohne Projektangabe (aeltere Aufrufe) bleibt der blanke View-Name stehen.
CheckEq("Ohne Projekt bleibt der View-Name unveraendert",
        new VisUsage { Version = VisVersion.Vis1, View = "Kueche", WidgetId = "w1", Template = "t", Field = "oid" }.ViewPath,
        "Kueche");

// Am echten Backup: Die Views tragen jetzt ihr Projekt vor sich her.
Check("View-Angaben enthalten das Projekt",
      visDps.All(d => d.Vis1Views.Concat(d.Vis2Views).All(v => v.Contains('/'))));

// Die Fundstellenliste zeigt das Projekt als eigene Spalte — Reihenfolge muss zur
// Spaltenliste passen, sonst stehen die Werte in den falschen Spalten.
CheckEq("Fundstellen-Spalten enthalten Projekt an zweiter Stelle",
        VisPresenter.UsageColumns[1], "Projekt");
var beispielZeile = VisPresenter.UsageRow(ausTablet);
CheckEq("Fundstellen-Zeile passt zur Spaltenzahl",
        beispielZeile.Length, VisPresenter.UsageColumns.Length);
CheckEq("Projekt steht in der Fundstellen-Zeile", beispielZeile[1], "tablet");

// Gegenprobe: Ein per Struktur gefundener Datenpunkt muss auch im Rohtext seiner
// VIS-Datei vorkommen — sonst hat der Extraktor etwas erfunden.
var sample = visDps.Where(d => d.InVis1 && !d.InVis2).Take(5).ToList();
var v1Text = string.Join("\n", fullData.VisViews.Where(v => v.Version == VisVersion.Vis1).Select(v => v.Content));
Check("Stichprobe VIS-1-Datenpunkte im Rohtext auffindbar",
      sample.Count > 0 && sample.All(d => v1Text.Contains(d.Id, StringComparison.Ordinal)),
      $"{sample.Count} geprueft");

// Die Trennung muss echt sein: ein nur in VIS 2 gefundener DP darf nicht im VIS-1-Text stehen.
var v2Only = visDps.Where(d => d.InVis2 && !d.InVis1).Take(20).ToList();
var falsePositives = v2Only.Count(d => v1Text.Contains(d.Id, StringComparison.Ordinal));
Check("Trennung VIS 1 / VIS 2 ist korrekt", falsePositives == 0,
      $"{falsePositives} von {v2Only.Count} faelschlich nur VIS 2");

// Attributzugriffe wie {…Meldung.ts} duerfen nicht als eigener (fehlender) Datenpunkt
// erscheinen, sondern muessen dem Datenpunkt …Meldung zugeschlagen werden.
Check("Kein Datenpunkt endet faelschlich auf ein Zustandsattribut",
      !visDps.Any(d => !d.ExistsInBackup
                    && d.Id.LastIndexOf('.') > 0
                    && new[] { "val", "ts", "lc", "ack", "q", "from", "user", "expire" }
                           .Contains(d.Id[(d.Id.LastIndexOf('.') + 1)..])
                    && visDps.Any(o => o.Id == d.Id[..d.Id.LastIndexOf('.')])),
      string.Join(", ", visDps.Where(d => !d.ExistsInBackup).Select(d => d.Id).Take(3)));

var tsUsages = visDps.SelectMany(d => d.Usages.Select(u => (d, u)))
                     .Where(x => x.u.Attribute.Length > 0).ToList();
Console.WriteLine($"  Attributzugriffe (nicht .val): {tsUsages.Count}");
foreach (var g in tsUsages.GroupBy(x => x.u.Attribute))
    Console.WriteLine($"    .{g.Key,-6} {g.Count()}x   z. B. {g.First().d.Id}");

Check("Attributzugriffe zeigen auf existierende Datenpunkte",
      tsUsages.All(x => x.d.ExistsInBackup),
      string.Join(", ", tsUsages.Where(x => !x.d.ExistsInBackup).Select(x => x.d.Id).Take(3)));

// Attributzugriffe muessen dem Datenpunkt zugeschlagen werden, statt als eigener
// (scheinbar fehlender) Datenpunkt zu zaehlen. Gesucht ueber die Eigenschaft und
// nicht ueber eine feste ID: im Testbackup steckt die Anlage eines Anwenders.
var meldung = visDps.FirstOrDefault(d => d.Usages.Any(u => u.Attribute == "ts"));
Check("Ein Datenpunkt mit ts-Attributzugriff ist vorhanden", meldung is not null);
Check("Der Datenpunkt dahinter existiert im Backup", meldung is { ExistsInBackup: true });
Check("Kein Attributzugriff steht als eigener Datenpunkt in der Liste",
      !visDps.Any(d => d.Id.EndsWith(".ts")));

Console.WriteLine("  Verwendete Datenpunkte nach Namespace:");
foreach (var g in visDps.GroupBy(d => d.Id.Split('.')[0])
                        .OrderByDescending(g => g.Count()).Take(10))
    Console.WriteLine($"    {g.Key,-24} {g.Count()}");

var aliasDps = visDps.Where(d => d.IsAlias).ToList();
var aliasBroken = visDps.Where(d => d.AliasTargetMissing).ToList();
Console.WriteLine($"  davon Aliasse: {aliasDps.Count} (im Backup existieren {fullData.AliasCount} Alias-Objekte)");
Console.WriteLine($"  Aliasse mit fehlendem Ziel: {aliasBroken.Count}");
foreach (var a in aliasDps.Take(3)) Console.WriteLine($"    {a.Id}  ->  {a.AliasTarget}");
foreach (var a in aliasBroken.Take(5)) Console.WriteLine($"    ZIEL FEHLT: {a.Id}  ->  {a.AliasTarget}");

Check("Aliasse werden in der VIS-Liste gefuehrt", aliasDps.Count > 0);
Check("Jeder in VIS genutzte Alias hat ein aufgeloestes Ziel",
      aliasDps.All(a => !a.ExistsInBackup || a.AliasTarget.Length > 0),
      string.Join(", ", aliasDps.Where(a => a.ExistsInBackup && a.AliasTarget.Length == 0)
                                .Select(a => a.Id).Take(3)));
Check("Alias-Ziele sind vollstaendige Objekt-IDs", aliasDps.All(a => a.AliasTarget.Contains('.')));
Check("AliasTargetMissing nur bei tatsaechlich fehlendem Ziel",
      aliasBroken.All(a => !fullData.Objects.Any(o => o.Id == a.AliasTarget)));

// Jede Fundstelle muss Widget und Feld benennen — sonst ist die Angabe wertlos.
Check("Jede Fundstelle hat eine Widget-ID", visDps.All(d => d.Usages.All(u => u.WidgetId.Length > 0)));
Check("Jede Fundstelle hat ein Feld", visDps.All(d => d.Usages.All(u => u.Field.Length > 0)));
Check("Fast alle Fundstellen haben einen Widget-Typ",
      visDps.SelectMany(d => d.Usages).Count(u => u.Template.Length == 0) == 0);
Check("Widget-IDs sehen wie VIS-Schluessel aus",
      visDps.SelectMany(d => d.Usages).All(u => u.WidgetId.StartsWith('w') || u.WidgetId.Length > 2));
Check("UsageCount stimmt mit Fundstellenzahl ueberein",
      visDps.All(d => d.UsageCount == d.Usages.Count));
Check("WidgetCount ist nie groesser als UsageCount",
      visDps.All(d => d.WidgetCount <= d.UsageCount));

var allUsages = visDps.SelectMany(d => d.Usages).ToList();
Console.WriteLine($"  Fundstellen gesamt: {allUsages.Count}   verschiedene Widget-Typen: " +
                  $"{allUsages.Select(u => u.Template).Distinct().Count()}");

Console.WriteLine("  Haeufigste Felder:");
foreach (var g in allUsages.GroupBy(u => u.Field).OrderByDescending(g => g.Count()).Take(6))
    Console.WriteLine($"    {g.Count(),4}x  {g.Key}");

Console.WriteLine("  Meistgenutzte VIS-Datenpunkte:");
foreach (var d in visDps.OrderByDescending(x => x.WidgetCount).Take(4))
{
    Console.WriteLine($"    {d.WidgetCount,3} Widgets  [{(d.InVis1 ? "1" : " ")}{(d.InVis2 ? "2" : " ")}]  {d.Id}");
    foreach (var u in d.Usages.Take(2)) Console.WriteLine($"           {u.Short}");
}

if (missing.Count > 0)
{
    Console.WriteLine("  Widgets ohne existierenden Datenpunkt (Auszug):");
    foreach (var d in missing.Take(5))
        Console.WriteLine($"    {d.Id}\n           {string.Join("\n           ", d.Usages.Take(2).Select(u => u.Short))}");
}

// ---------------------------------------------------------------- States

Console.WriteLine();
Console.WriteLine("=== States (states.jsonl) ===");

CheckRef("States eingelesen", fullData.States.Count, "states");
// Anteilig statt gegen eine feste Zahl: Die Zusicherung ist „so gut wie jeder State bringt
// diese Metadaten mit" — sie darf nicht an der Groesse der geprueften Anlage haengen.
var mitZeitstempel = fullData.States.Values.Count(s => s.LastChange is not null);
var mitQuelle = fullData.States.Values.Count(s => s.From.Length > 0);
Check("States tragen Zeitstempel",
      mitZeitstempel > fullData.States.Count * 0.9,
      $"{mitZeitstempel} von {fullData.States.Count}");
Check("States tragen eine Quelle",
      mitQuelle > fullData.States.Count * 0.9,
      $"{mitQuelle} von {fullData.States.Count}");

// Alle Zeitstempel muessen plausibel sein: nach dem ersten ioBroker-Release und nicht
// nach dem Backup-Zeitpunkt. Ein Umrechnungsfehler (Sekunden statt Millisekunden) faellt
// hier sofort auf.
var backupTime = fullData.CreatedAt!.Value;
var implausible = fullData.States.Values
    .Where(s => s.LastChange is { } t && (t.Year < 2013 || t > backupTime.AddDays(1)))
    .ToList();
Check("Alle Zeitstempel liegen im plausiblen Bereich", implausible.Count == 0,
      string.Join(", ", implausible.Take(3).Select(s => $"{s.Id}={s.LastChange:o}")));

// --- Werte (val) -------------------------------------------------------------------
// Der Wert wird seit v1.25.0 geladen, aber bei StateInfo.MaxValLength gekappt. Geprueft
// wird beides: dass ueberhaupt Werte ankommen und dass die Kappung wirklich greift — ohne
// sie liefe der Speicher an den wenigen sehr grossen Werten voll.
var mitWert = fullData.States.Values.Count(s => s.HasVal);
var gekappt = fullData.States.Values.Where(s => s.ValTruncated).ToList();
var groesster = fullData.States.Values.OrderByDescending(s => s.ValLength).First();

Console.WriteLine($"  States mit Wert                    : {mitWert:N0} von {fullData.States.Count:N0}");
Console.WriteLine($"  davon gekappt (> {StateInfo.MaxValLength} Zeichen)   : {gekappt.Count}");
Console.WriteLine($"  groesster Wert                     : {groesster.ValLength:N0} Zeichen " +
                  $"({groesster.Id})");

Check("Die meisten States tragen einen Wert",
      mitWert > fullData.States.Count * 0.9,
      $"{mitWert} von {fullData.States.Count}");

Check("Kein gespeicherter Wert ueberschreitet die Grenze",
      fullData.States.Values.All(s => s.Val.Length <= StateInfo.MaxValLength),
      fullData.States.Values.FirstOrDefault(s => s.Val.Length > StateInfo.MaxValLength)?.Id);

Check("Gekappte Werte kennen ihre Originallaenge",
      gekappt.All(s => s.ValLength > StateInfo.MaxValLength),
      gekappt.FirstOrDefault(s => s.ValLength <= StateInfo.MaxValLength)?.Id);

// Ohne diesen Fall im Testbackup wuerde die Kappung nie durchlaufen und der Test nichts
// aussagen. Die Referenzanlage hat einen Wert von rund 380.000 Zeichen — faellt der eines
// Tages weg, soll das auffallen und nicht still die Pruefung entwerten.
Check("Das Testbackup enthaelt mindestens einen gekappten Wert", gekappt.Count > 0);

// Eine Tabellenzelle vertraegt keinen Zeilenumbruch: Die Anzeige muss einzeilig sein,
// egal was im Wert steht (JSON mit Umbruechen, mehrzeilige Texte).
var mehrzeilig = fullData.States.Values
    .Where(s => s.ValText.Contains('\n') || s.ValText.Contains('\r') || s.ValText.Contains('\t'))
    .ToList();
Check("Kein Anzeigewert enthaelt Zeilenumbrueche", mehrzeilig.Count == 0,
      mehrzeilig.FirstOrDefault()?.Id);

Check("Anzeigewerte bleiben kurz genug fuer eine Zelle",
      fullData.States.Values.All(s => s.ValText.Length <= StateInfo.DisplayLength + 40),
      fullData.States.Values.OrderByDescending(s => s.ValText.Length).First().Id);

// Ein fehlender Wert ist etwas anderes als ein leerer: Das eine heisst "hier steht
// nichts", das andere "hier steht eine leere Zeichenkette".
CheckEq("Fehlender Wert wird als Strich angezeigt",
        StateInfo.FormatVal("", hasVal: false, truncated: false, length: 0), "—");
CheckEq("Leerer Wert bleibt leer",
        StateInfo.FormatVal("", hasVal: true, truncated: false, length: 0), "");
Check("Gekappter Wert weist die Originallaenge aus",
      StateInfo.FormatVal(new string('x', 200), hasVal: true, truncated: true, length: 380344)
               .Contains("380.344"));

// Spaltenkoepfe und Zeilen muessen zusammenpassen. Ohne diese Pruefung faellt eine
// vergessene Spalte erst in der Oberflaeche auf — und im CSV womoeglich gar nicht.
var beispielB = unused.First();
var beispielC = StateAnalyzer.Analyze(fullData).All.First();
CheckEq("Analyse B: Spaltenzahl passt zur Zeile",
        OrphansPresenter.ColumnsB.Length, OrphansPresenter.DisplayRowB(beispielB).Length);
CheckEq("Analyse B: CSV-Spalten passen zur CSV-Zeile",
        OrphansPresenter.CsvColumnsB.Length, OrphansPresenter.RowB(beispielB).Length);
CheckEq("Analyse C: Spaltenzahl passt zur Zeile",
        OrphansPresenter.ColumnsC.Length, OrphansPresenter.DisplayRowC(beispielC).Length);
CheckEq("Analyse C: CSV-Spalten passen zur CSV-Zeile",
        OrphansPresenter.CsvColumnsC.Length, OrphansPresenter.RowC(beispielC).Length);

// Die CSV fuehrt den vollstaendigen gespeicherten Wert, die Anzeige die gekuerzte Fassung.
// Vertauscht waere beides unbrauchbar: eine unlesbare Tabelle und eine wertlose CSV.
var langerWert = StateAnalyzer.Analyze(fullData).All
    .OrderByDescending(r => r.ValLength).First();
Check("CSV traegt den vollstaendigen gespeicherten Wert",
      OrphansPresenter.RowC(langerWert)[^1].Length > OrphansPresenter.DisplayRowC(langerWert)[^1].Length,
      langerWert.Id);

sw.Restart();
var stateReport = StateAnalyzer.Analyze(fullData);
sw.Stop();
Console.WriteLine($"  Analysedauer: {sw.ElapsedMilliseconds} ms");
Console.WriteLine($"  States ohne Objekt (Werte-Leichen) : {stateReport.StatesWithoutObject.Count}");
Console.WriteLine($"  state-Objekte ohne Wert            : {stateReport.ObjectsWithoutState.Count}");
Console.WriteLine($"  Qualitaet ungleich gut (q != 0)     : {stateReport.BadQuality.Count}");
Console.WriteLine($"  Nicht quittiert (ack = false)      : {stateReport.Unacknowledged.Count}");
Console.WriteLine("  Letzte Wertaenderung:");
foreach (var b in stateReport.Ages)
    Console.WriteLine($"    {b.Label,-20} {b.Count,6:N0}");

Check("Auswertung findet Werte-Leichen", stateReport.StatesWithoutObject.Count > 0);
Check("Auswertung findet Objekte ohne Wert", stateReport.ObjectsWithoutState.Count > 0);
CheckRef("state-Objekte gezaehlt", stateReport.TotalStateObjects, "state-objekte");

Check("Werte-Leichen haben wirklich kein Objekt",
      stateReport.StatesWithoutObject.All(r => !fullData.Objects.Any(o => o.Id == r.Id)),
      stateReport.StatesWithoutObject.FirstOrDefault()?.Id);

Check("Objekte ohne Wert haben wirklich keinen State",
      stateReport.ObjectsWithoutState.All(r => !fullData.States.ContainsKey(r.Id)),
      stateReport.ObjectsWithoutState.FirstOrDefault()?.Id);

// Aliasse haben nie einen eigenen Wert — sie in "Objekte ohne Wert" zu fuehren, waere
// bei jedem einzelnen ein Fehlalarm (Rueckfrage aus der Praxis, v1.14.0).
var aliasObjects = fullData.Objects
    .Count(o => o.Type == "state" && o.Id.StartsWith("alias.", StringComparison.OrdinalIgnoreCase));
Console.WriteLine($"  Alias-Objekte (type=state)         : {aliasObjects}");
Console.WriteLine($"  davon mit eigenem State            : " +
                  fullData.Objects.Count(o => o.Id.StartsWith("alias.", StringComparison.OrdinalIgnoreCase)
                                              && fullData.States.ContainsKey(o.Id)));

Check("Kein Alias in der Liste der Objekte ohne Wert",
      !stateReport.ObjectsWithoutState.Any(r => r.Id.StartsWith("alias.", StringComparison.OrdinalIgnoreCase)),
      stateReport.ObjectsWithoutState.FirstOrDefault(r => r.Id.StartsWith("alias."))?.Id);
CheckEq("Ausgenommene Aliasse werden ausgewiesen", stateReport.AliasesWithoutOwnState, aliasObjects);
Check("Kennzeile nennt die ausgenommenen Aliasse",
      OrphansPresenter.StatsC(stateReport).Contains("Aliasse"));

Check("Werte-Leichen enthalten keine Adapter-Verwaltungs-States",
      !stateReport.StatesWithoutObject.Any(r => r.Id.StartsWith("system.adapter.")
                                             || r.Id.StartsWith("system.host.")));

Check("Altersverteilung summiert sich auf die Statesumme",
      stateReport.Ages.Sum(b => b.Count) == fullData.States.Count,
      $"{stateReport.Ages.Sum(b => b.Count)} vs {fullData.States.Count}");

// Die Klartext-Tabelle deckt das ioBroker-Schema vollstaendig ab. Ein Rest-Code waere
// entweder ein Adapter, der sich nicht daran haelt, oder eine Luecke in unserer Tabelle —
// beides will man sehen und nicht als "Code 0x20" an den Benutzer durchreichen.
var unknownQuality = stateReport.BadQuality
    .Where(r => r.QualityText.StartsWith("unbekannter Code", StringComparison.Ordinal))
    .Select(r => r.QualityText).Distinct().ToList();
Check("Jeder Qualitaetscode hat einen Klartext",
      unknownQuality.Count == 0,
      string.Join(", ", unknownQuality.Take(5)));

Console.WriteLine("  Qualitaetscodes ungleich gut:");
foreach (var g in stateReport.BadQuality.GroupBy(r => r.QualityText)
                             .OrderByDescending(g => g.Count()))
    Console.WriteLine($"    {g.Key,-34} {g.Count()}");

// Die Einfaerbung muss die beiden Faelle trennen, sonst ist die Sicht wertlos: Ein
// Startwert (0x20) ist kein Befund, eine gemeldete Stoerung schon. Geprueft wird an den
// echten Daten, nicht an konstruierten Zeilen.
var startwerte = stateReport.BadQuality.Where(r => !r.QualityIsFault).ToList();
var stoerungen = stateReport.BadQuality.Where(r => r.QualityIsFault).ToList();
Console.WriteLine($"  davon Ersatz-/Startwerte: {startwerte.Count}, echte Stoerungen: {stoerungen.Count}");
Check("Ersatz- und Startwerte werden nicht hervorgehoben",
      startwerte.Where(r => r.HasObject && r.HasState && r.Ack)
                .All(r => OrphansPresenter.EmphasisC(r) == RowEmphasis.Muted));
Check("Echte Stoerungen werden hervorgehoben",
      stoerungen.All(r => OrphansPresenter.EmphasisC(r) is RowEmphasis.Warn or RowEmphasis.Problem));
Check("Startwert 0x20 gilt nicht als Stoerung", !new StateRow { Id = "test", Quality = 0x20 }.QualityIsFault);
Check("Geraetefehler 0x44 gilt als Stoerung", new StateRow { Id = "test", Quality = 0x44 }.QualityIsFault);

Console.WriteLine("  Werte-Leichen nach Namensraum:");
foreach (var g in stateReport.StatesWithoutObject.GroupBy(r => r.Namespace)
                             .OrderByDescending(g => g.Count()).Take(6))
    Console.WriteLine($"    {g.Key,-28} {g.Count()}");

// Die Zeitstempel muessen in Analyse B ankommen — das ist der eigentliche Zweck.
var withAge = unused.Count(u => u.AgeDays is not null);
var neverWritten = unused.Count(u => !u.HasState);
Console.WriteLine($"  Analyse B: {withAge} Datenpunkte mit Zeitstempel, {neverWritten} nie beschrieben");
Check("Analyse B kennt die letzte Wertaenderung", withAge > 0);
Check("Kein Datenpunkt hat Alter und gleichzeitig keinen State",
      !unused.Any(u => !u.HasState && u.AgeDays is not null));

var strongCandidates = unused.Where(u => u.IsStrongCandidate).ToList();
var activeCandidates = unused.Where(u => u.IsCandidate && u.RecentlyChanged).ToList();
Console.WriteLine($"  Kandidaten gesamt: {candidates.Count}   davon tot: {strongCandidates.Count}   " +
                  $"davon noch aktiv: {activeCandidates.Count}");
Check("Starke Kandidaten sind Teilmenge der Kandidaten",
      strongCandidates.All(u => u.IsCandidate));
Check("Ein Kandidat ist nie gleichzeitig tot und aktiv",
      !unused.Any(u => u.IsStrongCandidate && u.RecentlyChanged));

if (activeCandidates.Count > 0)
{
    Console.WriteLine("  Kandidaten, die sich zuletzt noch geaendert haben (Falsch-Positiv-Risiko):");
    foreach (var u in activeCandidates.OrderBy(u => u.AgeDays).Take(5))
        Console.WriteLine($"    {u.Id}  ({u.LastChangeText})");
}

// ------------------------------------------- Erweiterungen (Charts/Logging/Adapter/Alias)

Console.WriteLine();
Console.WriteLine("=== Erweiterungen: Charts, Logging, Adapter ohne Instanz, Aliasse ===");

// --- Feature 1: Chart-Referenzen in Analyse B ---
var chartObjects = fullData.Objects.Where(o => o.Type == "chart").ToList();
var chartRefs = new HashSet<string>(
    fullData.Objects.Where(o => o.ChartRefs is not null).SelectMany(o => o.ChartRefs!),
    StringComparer.Ordinal);
Console.WriteLine($"  Chart-Objekte: {chartObjects.Count}   referenzierte Datenpunkte (distinct): {chartRefs.Count}");
CheckRef("Chart-Objekte gefunden", chartObjects.Count, "charts");
Check("Charts referenzieren Datenpunkte", chartRefs.Count > 0);
Check("Chart-Referenzen sehen wie Datenpunkt-IDs aus", chartRefs.All(r => r.Contains('.')));

// Ein per Chart genutzter User-Datenpunkt darf kein Verwaisten-Kandidat sein.
var chartUserDps = unused.Where(u => u.InChart).ToList();
Console.WriteLine($"  Analyse B: {chartUserDps.Count} User-Datenpunkte in Charts referenziert");
Check("Chart-Prüfung erreicht Analyse B", chartUserDps.Count > 0);
Check("Kein Kandidat ist zugleich in einem Chart referenziert", !candidates.Any(c => c.InChart));

// Der am echten Backup verifizierte Fall: Ein Chart bezieht einen User-Datenpunkt ueber
// eine Quell-Instanz jenseits von history/influxdb/sql (hier "json") — auch der zaehlt als
// genutzt. Geprueft wird die Eigenschaft, nicht eine bestimmte ID: Datenpunktnamen einer
// echten Anlage gehoeren nicht in den Quelltext.
Check("Datenpunkte aus Chart-Definitionen werden als referenziert erkannt",
      unused.Any(u => u.InChart));

// Invariante: die Chart-Prüfung entfernt genau die Kandidaten, die nur durch sie gerettet
// werden — nicht mehr und nicht weniger.
var candidatesNoChart = unused.Count(u => u.InScripts == FindKind.Nicht && u.InVis == FindKind.Nicht
                                       && !u.AliasTarget && !u.LoggingActive);
var savedOnlyByChart = unused.Count(u => u.InChart && u.InScripts == FindKind.Nicht
                                      && u.InVis == FindKind.Nicht && !u.AliasTarget && !u.LoggingActive);
Console.WriteLine($"  Kandidaten ohne Chart-Prüfung: {candidatesNoChart}  ->  mit: {candidates.Count}  " +
                  $"(nur durch Chart gerettet: {savedOnlyByChart})");
Check("Chart-Prüfung entfernt genau die per Chart genutzten Kandidaten",
      candidates.Count == candidatesNoChart - savedOnlyByChart,
      $"{candidates.Count} == {candidatesNoChart} - {savedOnlyByChart}");

// --- Feature 3: Logging-Übersicht ---
var logging = LoggingAnalyzer.Analyze(fullData);
var logInstances = logging.Select(r => r.Instance).Distinct(StringComparer.OrdinalIgnoreCase)
                          .OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
Console.WriteLine($"  Logging-Einträge: {logging.Count}   Instanzen: {string.Join(", ", logInstances)}");
Check("Logging-Einträge gefunden", logging.Count > 0);
Check("Jeder Logging-Eintrag hat eine Instanz", logging.All(r => r.Instance.Length > 0));
Check("Adapter wird aus der Instanz abgeleitet (ohne Instanznummer)",
      logging.All(r => r.Adapter.Length > 0 && !r.Adapter.Contains('.')));
// Alle Logging-Instanzen werden erfasst, nicht nur InfluxDB.
Check("Logging erfasst alle Instanzen (influxdb.0 und sourceanalytix.0)",
      logInstances.Contains("influxdb.0") && logInstances.Contains("sourceanalytix.0"));
CheckRef("InfluxDB-Logging-Datenpunkte", logging.Count(r => r.Instance == "influxdb.0"), "logging-influxdb");
CheckRef("sourceanalytix-Logging-Datenpunkte", logging.Count(r => r.Instance == "sourceanalytix.0"), "logging-sourceanalytix");
// Beide Wege müssen dasselbe sagen: Was hier geloggt wird, gilt in Analyse B als geloggt.
var loggedIds = new HashSet<string>(logging.Select(r => r.Id), StringComparer.Ordinal);
Check("Logging-Datenpunkte gelten in Analyse B als geloggt",
      unused.Where(u => loggedIds.Contains(u.Id)).All(u => u.LoggingActive));

// --- Feature 4: Adapter ohne Instanz ---
var noInst = OrphanAnalyzer.FindAdaptersWithoutInstance(fullData);
Console.WriteLine($"  Adapter ohne Instanz: {noInst.Count}  ({string.Join(", ", noInst.Take(10).Select(a => a.Adapter))})");
var adaptersWithInstance = new HashSet<string>(
    fullData.Instances.Select(i => i.Adapter), StringComparer.OrdinalIgnoreCase);
Check("Kein gemeldeter Adapter hat eine Instanz",
      !noInst.Any(a => adaptersWithInstance.Contains(a.Adapter)));
Check("Keine host-gebundenen Objekte in der Liste", !noInst.Any(a => a.Adapter.Contains('.')));
var adapterObjNames = fullData.Objects
    .Where(o => o.Type == "adapter" && o.Id.StartsWith("system.adapter.")
             && !o.Id["system.adapter.".Length..].Contains('.'))
    .Select(o => o.Id["system.adapter.".Length..]).ToHashSet(StringComparer.OrdinalIgnoreCase);
var withInstAndObj = adapterObjNames.Count(n => adaptersWithInstance.Contains(n));
CheckRef("Echte Adapter-Objekte (system.adapter.<name>)", adapterObjNames.Count, "adapter-objekte");
Check("Adapterobjekte = mit Instanz + ohne Instanz",
      adapterObjNames.Count == withInstAndObj + noInst.Count,
      $"{adapterObjNames.Count} = {withInstAndObj} + {noInst.Count}");

// --- Feature 5: Alias-Übersicht ---
var aliasRows = AliasAnalyzer.Analyze(fullData);
Console.WriteLine($"  Aliasse: {aliasRows.Count}   davon Ziel fehlt: {aliasRows.Count(a => a.Broken)}   " +
                  $"getrenntes Schreibziel: {aliasRows.Count(a => !a.SingleTarget)}");
CheckRef("Alias-Datenpunkte", aliasRows.Count, "aliasse");
Check("Jeder Alias hat ein Lese-Ziel", aliasRows.All(a => a.ReadTarget.Length > 0));
Check("Alias-Ziele sind vollständige Objekt-IDs", aliasRows.All(a => a.ReadTarget.Contains('.')));
var knownIds = new HashSet<string>(fullData.Objects.Select(o => o.Id), StringComparer.Ordinal);
Check("Vorhanden-Prüfung stimmt mit dem Objektbestand überein",
      aliasRows.All(a => a.ReadExists == knownIds.Contains(a.ReadTarget)));
Check("Kaputte Aliasse haben wirklich kein existierendes Ziel",
      aliasRows.Where(a => a.Broken).All(a =>
          !knownIds.Contains(a.ReadTarget) || (!a.SingleTarget && !knownIds.Contains(a.WriteTarget))));
// .1/.3-Instanzen in den Zielen: Beleg, dass die Instanznummer mitgeführt und nicht auf .0
// reduziert wird (im Testbackup gibt es Ziele mit Instanznummer 1 und 3).
Check("Ziel-Instanznummern bleiben erhalten (z. B. .1/.3)",
      aliasRows.Any(a => a.ReadTarget.Contains(".1.") || a.ReadTarget.Contains(".3.")));

// --- Konvertierungsfunktionen und Konverter-Generator ---
var withConverter = aliasRows.Where(a => a.HasConverter).ToList();
Console.WriteLine($"  Aliasse mit Konvertierungsfunktion: {withConverter.Count}");
CheckRef("Aliasse mit Konverter", withConverter.Count, "aliasse-mit-konverter");
// Konverter (common.alias.read/write) dürfen nicht mit den Ziel-IDs verwechselt werden:
// Ein Konverter ist JS-Code, kein Datenpunkt — er enthält typischerweise "val".
Check("Konverter sind Code, keine Ziel-IDs", withConverter.All(a =>
      (a.ConverterRead.Length == 0 || a.ConverterRead.Contains("val"))
      && (a.ConverterWrite.Length == 0 || a.ConverterWrite.Contains("val"))));

// Ein Alias mit Konverter, dessen Ziel eine Wertetabelle traegt - daran haengen
// Konverter-Erkennung und Generator. Auch hier ueber die Eigenschaften gesucht,
// nicht ueber eine feste ID aus einer realen Anlage.
var flur = aliasRows.FirstOrDefault(a => a.HasConverter && a.SingleTarget
      && fullData.Objects.Any(o => o.Id == a.ReadTarget && o.States is { Count: > 0 }));
Check("Alias mit Konverter und Wertetabellen-Ziel gefunden", flur is not null);
if (flur is not null)
{
    Check("Ziel ist im Objektbestand aufloesbar",
          fullData.Objects.Any(o => o.Id == flur.ReadTarget), flur.ReadTarget);
    Check("Lese-Konverter ist Code und keine ID (enthaelt val)",
          flur.ConverterRead.Contains("val"));
    Check("Es ist ein String-Alias (Lesen = Schreiben, kein getrenntes Ziel)", flur.SingleTarget);
}

// Ein Ziel-Datenpunkt mit Wertetabelle, aus der der Generator schöpft.
var modeTarget = fullData.Objects.FirstOrDefault(o => o.States is { Count: > 0 }
      && o.States.ContainsKey("off") && o.States.ContainsKey("auto") && o.States.ContainsKey("heat"));
Check("Ziel-Datenpunkt hat eine Wertetabelle (common.states)",
      modeTarget is { States: { Count: > 0 } });
if (modeTarget is not null)
    Check("Wertetabelle enthält off/auto/heat",
          modeTarget.States is not null && modeTarget.States.ContainsKey("off")
          && modeTarget.States.ContainsKey("auto") && modeTarget.States.ContainsKey("heat"));

// Generator: aus dem Ziel-Datenpunkt entsteht ein Konverter-Gerüst mit allen Werten.
var gen = ConverterGenerator.Generate(modeTarget);
Console.WriteLine($"  Generator (system_mode) -> Lesen: {gen.Read}");
Check("Generator kann für den Wertelisten-Datenpunkt erzeugen", gen.CanGenerate);
Check("Erzeugter Lese-Konverter enthält alle Gerätewerte",
      gen.Read.Contains("'off'") && gen.Read.Contains("'auto'") && gen.Read.Contains("'heat'"));
Check("Erzeugte Ternär-Kette endet mit dem unveränderten Wert",
      gen.Read.TrimEnd().EndsWith(": val") && gen.Write.TrimEnd().EndsWith(": val"));

// Gegenprobe: ein numerischer Datenpunkt ohne Wertetabelle liefert bewusst keinen Vorschlag.
var numericNoStates = fullData.Objects.FirstOrDefault(o =>
    o.Type == "state" && string.Equals(o.CommonType, "number", StringComparison.OrdinalIgnoreCase)
    && o.States is null);
if (numericNoStates is not null)
{
    var genNum = ConverterGenerator.Generate(numericNoStates);
    Check("Zahlen-Datenpunkt ohne Wertetabelle: kein erfundener Konverter",
          !genNum.CanGenerate && genNum.Read.Length == 0, numericNoStates.Id);
}

// Fehlendes Ziel: der Generator meldet ehrlich, dass nichts möglich ist.
var genMissing = ConverterGenerator.Generate(null);
Check("Fehlendes Ziel: Generator liefert keinen Vorschlag", !genMissing.CanGenerate);

// --- Aufräum-Skript-Generator (Waisen-States) ---
var orphanNamespaces = stateReport.StatesWithoutObject
    .Select(r => r.Namespace).Distinct(StringComparer.Ordinal).ToList();
Console.WriteLine($"  Werte-Leichen verteilen sich auf {orphanNamespaces.Count} Namensräume");
Check("Werte-Leichen lassen sich nach Namensraum gruppieren", orphanNamespaces.Count > 0);

// Exakte Waisen-IDs statt Namensraum-Enumeration, und bewusst als Shell-Skript für die
// ioBroker-CLI: deleteState() im JavaScript-Adapter kann keine States fremder Adapter
// löschen ("States from other adapters cannot be deleted") und meldete gegen die Waisen
// 795 von 795 Mal "Not found". deleteObject() scheidet ebenso aus — zu einem Waisen-State
// gibt es per Definition kein Objekt. Nur "iobroker state delete <id>" greift durch.
var cleanupIds = stateReport.StatesWithoutObject.Select(r => r.Id).Take(20).ToList();
var cleanupScript = CleanupScriptGenerator.Generate(cleanupIds);
Check("Aufräum-Skript startet im Trockenlauf (DRY_RUN=true)",
      cleanupScript.Contains("DRY_RUN=true"));
Check("Aufräum-Skript erklärt beide Modi im Kommentar",
      cleanupScript.Contains("Trockenlauf") && cleanupScript.Contains("scharf"));
// Der Modus wird beim Start abgefragt, nicht mehr im Text umgestellt: Wer mit der
// Kommandozeile wenig vertraut ist, soll die Datei nur ablegen und starten müssen.
Check("Aufräum-Skript fragt beim Start nach dem Modus",
      cleanupScript.Contains("Wirklich loeschen?") && cleanupScript.Contains("read -r antwort"));
Check("Nur ein grosses J schaltet scharf",
      cleanupScript.Contains("if [ \"$antwort\" = \"J\" ] || [ \"$antwort\" = \"JA\" ]; then"));
// Zeichenvergleich statt Mustervergleich: 'case' liesse sich über die Shell-Option
// nocasematch aushebeln, die ein per 'source' geholtes Skript aus der aufrufenden Shell
// erbt - dann löschte auch ein kleines j.
Check("Die Modusabfrage haengt nicht an einem Mustervergleich",
      !cleanupScript.Contains("case \"$antwort\""));
// Klein geschriebenes j darf nicht greifen: Der Grossbuchstabe verlangt die
// Umschalttaste, und genau das ist die Absicht - ein Tastendruck loescht nichts.
Check("Kleines j schaltet nicht scharf", !cleanupScript.Contains("j|J"));
// Ohne Terminal (Pipe, cron) käme read leer zurück — dort darf nie stillschweigend
// gelöscht werden.
Check("Ohne Terminal bleibt es beim Trockenlauf",
      cleanupScript.Contains("if [ -t 0 ]"));
Check("Schalter für den unbeaufsichtigten Lauf vorhanden",
      cleanupScript.Contains("--dry-run|-n") && cleanupScript.Contains("--delete|-y"));
// Ein Tippfehler wie --dryrun darf nicht als "kein Schalter" durchgehen und scharf laufen.
Check("Unbekanntes Argument bricht ab statt zu löschen",
      cleanupScript.Contains("Unbekanntes Argument"));
Check("Aufräum-Skript enthält die exakten Waisen-IDs",
      cleanupIds.Count > 0 && cleanupIds.All(id => cleanupScript.Contains("'" + id + "'")));
Check("Aufräum-Skript löscht über die ioBroker-CLI",
      cleanupScript.Contains("iobroker state delete \"$id\""));
Check("Aufräum-Skript ist ein Shell-Skript mit Shebang",
      cleanupScript.StartsWith("#!/bin/bash"));
// Eine gespeicherte .sh lebt auf dem Host weiter; ohne Versionszeile sieht man ihr nicht an,
// aus welcher Fassung sie stammt - und aeltere verhalten sich anders.
Check("Aufräum-Skript nennt die Programmversion im Kopf",
      cleanupScript.Contains($"Erzeugt vom {AppIdentity.Name} {AppIdentity.Version}."),
      AppIdentity.Version);
// Die drei JS-Sackgassen dürfen nicht zurückkehren: deleteState/deleteObject scheitern an
// Fremd-Namensräumen bzw. am fehlenden Objekt, getStates existiert im JS-Adapter gar nicht.
// Geprüft wird nur der ausführbare Teil — im Kommentarkopf stehen die drei Namen bewusst,
// weil dort erklärt wird, warum das Skript sie gerade nicht verwendet.
var cleanupCode = string.Join("\n", cleanupScript
    .Split('\n')
    .Where(l => !l.TrimStart().StartsWith('#')));
Check("Aufräum-Skript nutzt kein deleteState (kann keine Fremd-States löschen)",
      !cleanupCode.Contains("deleteState"));
Check("Aufräum-Skript nutzt kein deleteObject (zu Waisen gibt es kein Objekt)",
      !cleanupCode.Contains("deleteObject"));
Check("Aufräum-Skript nutzt kein nicht existierendes getStates",
      !cleanupCode.Contains("getStates"));
// Kein getObject-Vorabcheck: das wuerde bei fehlenden Objekten einen Warn-Sturm ins
// ioBroker-Log schreiben (ein Warn je Waise).
Check("Aufräum-Skript vermeidet den getObject-Warn-Sturm",
      !cleanupCode.Contains("getObject"));
Check("Aufräum-Skript fängt Löschfehler je ID ab",
      cleanupScript.Contains("fehler=$((fehler + 1))") && cleanupScript.Contains("FEHLER bei"));
Check("Aufräum-Skript bricht ohne ioBroker-CLI sauber ab",
      cleanupScript.Contains("command -v iobroker") && cleanupScript.Contains("exit 1"));

// ID mit Sonderzeichen wird sauber als Shell-String escaped (kein Skript-Bruch): das
// Literal wird geschlossen, das Anführungszeichen maskiert, das Literal wieder geöffnet.
var tricky = CleanupScriptGenerator.Generate(new[] { "weird.0.o'brien" });
Check("Anführungszeichen in IDs werden escaped", tricky.Contains("'weird.0.o'\\''brien'"));

// Leere Auswahl bleibt ein gültiges (aber wirkungsloses) Skript.
var cleanupEmpty = CleanupScriptGenerator.Generate(Array.Empty<string>());
Check("Leere Auswahl ergibt trotzdem ein gültiges Skript mit DRY_RUN",
      cleanupEmpty.Contains("DRY_RUN=true") && cleanupEmpty.Contains("IDS=("));
Check("Leeres Skript beendet sich, statt nach dem Modus zu fragen",
      cleanupEmpty.Contains("keine Eintraege"));

// Die gespeicherte Datei muss LF haben: mit CRLF stirbt jedes Bash-Skript sofort an
// "$'\r': command not found" — eine Fehlermeldung, die kaum jemand deuten kann.
var cleanupFile = CleanupScriptGenerator.ForFile(cleanupScript);
Check("Gespeichertes Skript enthält keine CR-Zeichen", !cleanupFile.Contains('\r'));
Check("Gespeichertes Skript behält alle Zeilen",
      cleanupFile.Split('\n').Length == cleanupScript.Replace("\r\n", "\n").Split('\n').Length);
Check("Dateinamensvorschlag trägt den Backup-Namen",
      CleanupScriptGenerator.SuggestedFileName("iobroker_2026_01_02-03_04_05_backupiobroker")
          == "aufraeumen_iobroker_2026_01_02-03_04_05_backupiobroker.sh");
Check("Dateinamensvorschlag ohne Backup-Namen bleibt brauchbar",
      CleanupScriptGenerator.SuggestedFileName(null) == "aufraeumen.sh");

// --- Auswahl im Aufräum-Dialog: ganzer Namensraum oder einzelne Werte ---
// Ein Namensraum ist selten durchgehend Müll: Neben Werten, die weg sollen, stehen dort
// welche, die man behalten will. Die Auswahl liegt in Core, damit die Windows- und die
// plattformübergreifende Fassung sich Klick für Klick gleich verhalten.
var selGroups = new List<(string, IReadOnlyList<string>)>
{
    ("alexa2.0", new[] { "alexa2.0.b", "alexa2.0.a", "alexa2.0.c" }),
    ("mqtt.0",   new[] { "mqtt.0.x", "mqtt.0.y" }),
    // Doppelter und leerer Eintrag: beide dürfen nicht in der Auswahl landen.
    ("hm-rpc.1", new[] { "hm-rpc.1.ABC.STATE", "hm-rpc.1.ABC.LEVEL", "hm-rpc.1.ABC.LEVEL", "   " })
};
var sel = new CleanupSelection(selGroups);

Check("Auswahl entfernt doppelte und leere IDs", sel.TotalIds == 7, sel.TotalIds.ToString());
Check("Namensräume nach Anzahl, bei Gleichstand nach Name",
      string.Join(",", sel.VisibleGroups.Select(g => g.Namespace)) == "alexa2.0,hm-rpc.1,mqtt.0");
Check("Anfangs ist nichts ausgewählt", sel.SelectedCount == 0 && sel.SelectedIds.Count == 0);

var selAlexa = sel.VisibleGroups[0];
sel.SelectGroup(selAlexa, true);
Check("Namensraum anhaken wählt alle seine Werte",
      sel.StateOf(selAlexa) == GroupCheck.All && sel.SelectedCount == 3);

sel.Select("alexa2.0.b", false);
Check("Einzelner Wert lässt sich wieder abwählen",
      sel.StateOf(selAlexa) == GroupCheck.Partial && sel.SelectedCount == 2);
Check("Ausgewählte IDs kommen sortiert und ohne die abgewählte",
      string.Join(",", sel.SelectedIds) == "alexa2.0.a,alexa2.0.c");
// Ein zugeklappter Namensraum zeigt seine Auswahl sonst nirgends.
Check("Beschriftung nennt Anzahl und Auswahl",
      sel.GroupLabel(selAlexa).Contains("(3, ausgewählt: 2)"), sel.GroupLabel(selAlexa));

// Die Suche blendet aus, sie wählt nicht ab — sonst verlöre ein Tippfehler im Suchfeld
// die halbe Arbeit.
sel.SetFilter("hm-rpc");
Check("Suche zeigt nur den passenden Namensraum",
      sel.VisibleGroups.Count == 1 && sel.VisibleGroups[0].Namespace == "hm-rpc.1");
Check("Suche wählt nichts ab", sel.SelectedCount == 2);
Check("Zähler nennt trotz Suche die Gesamtauswahl",
      sel.CountText.StartsWith("Ausgewählt: 2 von 7"), sel.CountText);
Check("Hinweis auf die ausgeblendete Auswahl erscheint", sel.HiddenSelectionHint is not null);

sel.SelectAllVisible(true);
Check("„Alle\" wirkt nur auf das Sichtbare", sel.SelectedCount == 4, sel.SelectedCount.ToString());

sel.SetFilter("LEVEL");
var selHm = sel.VisibleGroups[0];
Check("Gruppenhäkchen bezieht sich auf die sichtbaren Werte", sel.StateOf(selHm) == GroupCheck.All);
Check("Beschriftung zeigt bei Suche Treffer und Gesamtzahl",
      sel.GroupLabel(selHm).Contains("1 von 2"), sel.GroupLabel(selHm));

sel.SelectAllVisible(false);
Check("„Keine\" hebt nur das Sichtbare auf",
      sel.SelectedCount == 3 && !sel.IsSelected("hm-rpc.1.ABC.LEVEL")
                             && sel.IsSelected("hm-rpc.1.ABC.STATE"));

sel.SetFilter("");
Check("Ohne Suche sind wieder alle Namensräume da",
      sel.VisibleGroups.Count == 3 && sel.HiddenSelectionHint is null);
sel.SelectAllVisible(true);
Check("Ohne Suche wählt „Alle\" wirklich alles", sel.SelectedCount == sel.TotalIds);

// Und die Probe aufs Exempel: Ein einzeln gewählter Wert steht allein im Skript.
var selSingle = new CleanupSelection(selGroups);
selSingle.Select("mqtt.0.y", true);
var selSingleScript = CleanupScriptGenerator.Generate(selSingle.SelectedIds);
Check("Einzeln gewählter Wert steht allein im Skript",
      selSingleScript.Contains("'mqtt.0.y'") && !selSingleScript.Contains("'mqtt.0.x'"));

// Gegen das echte Backup: Die Gruppen des Tabs und die Auswahl müssen dieselbe Menge sein.
var selReal = new CleanupSelection(OrphansPresenter.CleanupGroups(stateReport));
selReal.SelectAllVisible(true);
Check("Echtes Backup: Auswahl deckt alle Werte-Leichen ab",
      selReal.SelectedIds.Count == stateReport.StatesWithoutObject
          .Select(r => r.Id).Distinct(StringComparer.Ordinal).Count(),
      $"{selReal.SelectedIds.Count}");

// --- Aufräum-Skript wirklich ausführen, sofern eine bash erreichbar ist ---
// Ohne diesen Lauf bliebe das Skript ungetestet: Syntaxfehler oder eine falsch herum
// gedrehte Abfrage fielen erst dem Nutzer auf dem ioBroker-Host auf. Der Trockenlauf
// löscht nichts und braucht keine ioBroker-CLI.
RunBashChecks(cleanupFile);

void RunBashChecks(string script)
{
    var shPath = Path.Combine(Path.GetTempPath(), "iob_aufraeumen_test.sh");
    File.WriteAllText(shPath, script, new UTF8Encoding(false));

    // Für bash den Pfad mit Schrägstrichen: unter Windows würde eine Git-Bash die
    // Backslashes im Argument als Escapes lesen.
    var shArg = shPath.Replace('\\', '/');

    // Einmal ermitteln, welcher bash-Aufruf auf diesem Rechner traegt — siehe FindeBash.
    var bashExe = FindeBash();

    string? Run(string args, string stdin, out int exitCode)
    {
        if (bashExe is null) { exitCode = -1; return null; }

        try
        {
            var psi = new ProcessStartInfo(bashExe, args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                UseShellExecute = false
            };
            using var p = Process.Start(psi);
            if (p is null) { exitCode = -1; return null; }

            p.StandardInput.Write(stdin);
            p.StandardInput.Close();
            var output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            p.WaitForExit(30_000);
            exitCode = p.ExitCode;
            return output;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // Keine bash auf diesem Rechner — dann eben ohne diese Prüfungen.
            exitCode = -1;
            return null;
        }
    }

    // Umgeleitete Standardeingabe ist per Definition kein Terminal; genau so läuft das
    // Skript auch in einer Pipe oder aus cron.
    var syntax = Run($"-n \"{shArg}\"", "", out var syntaxExit);
    if (syntax is null)
    {
        Console.WriteLine("  [--]   bash nicht verfügbar - Ausführungstests übersprungen");
        nichtGelaufen.Add("Ausfuehrungstests des Aufraeum-Skripts (keine bash erreichbar)");
        return;
    }

    Check("Aufräum-Skript ist syntaktisch gültige bash", syntaxExit == 0, syntax);

    var dry = Run($"\"{shArg}\" --dry-run", "", out var dryExit);
    Check("--dry-run listet auf und löscht nichts",
          dryExit == 0 && dry!.Contains("WUERDE LOESCHEN") && dry.Contains("es wurde nichts geloescht"),
          dry);

    // Ohne Schalter und ohne Terminal: kein Löschen, sondern Trockenlauf mit Hinweis.
    var piped = Run($"\"{shArg}\"", "", out var pipedExit);
    Check("Ohne Terminal läuft es trocken statt zu löschen",
          pipedExit == 0 && piped!.Contains("Kein Terminal") && piped.Contains("WUERDE LOESCHEN"),
          piped);

    // Ein Tippfehler im Schalter muss abbrechen — nicht in den scharfen Lauf rutschen.
    var wrong = Run($"\"{shArg}\" --dryrun", "", out var wrongExit);
    Check("Falscher Schalter bricht ab", wrongExit == 1 && wrong!.Contains("Unbekanntes Argument"), wrong);

    // Scharfer Lauf ohne ioBroker-CLI: sauberer Abbruch statt einer Fehlerflut.
    // (Auf einem Rechner mit installiertem ioBroker würde diese Prüfung wirklich löschen -
    //  deshalb nur dort ausführen, wo die CLI fehlt.)
    var hasCli = Run("-c \"command -v iobroker\"", "", out var cliExit) is not null && cliExit == 0;
    if (!hasCli)
    {
        var sharp = Run($"\"{shArg}\" --delete", "", out var sharpExit);
        Check("Scharfer Lauf ohne ioBroker-CLI bricht ab",
              sharpExit == 1 && sharp!.Contains("nicht gefunden"), sharp);

        // --- Die Modusabfrage Antwort für Antwort durchspielen ---
        // Bisher war nur geprüft, dass die richtigen Zeichen im Skripttext stehen. Was die
        // Shell daraus macht, blieb ungetestet — und genau da liegt die gefährliche Frage:
        // Löscht ein kleines „j"? An einer umgeleiteten Eingabe ist [ -t 0 ] immer falsch,
        // deshalb wird für diesen Lauf allein die Terminal-Probe durch „true" ersetzt;
        // alles andere bleibt Wort für Wort das erzeugte Skript.
        var askPath = Path.Combine(Path.GetTempPath(), "iob_aufraeumen_frage.sh");
        File.WriteAllText(askPath, script.Replace("if [ -t 0 ]; then", "if true; then"),
                          new UTF8Encoding(false));
        var askArg = askPath.Replace('\\', '/');

        // Ohne CLI endet der scharfe Lauf mit „nicht gefunden" statt zu löschen — das ist
        // hier das verlässliche Erkennungszeichen dafür, dass scharf geschaltet wurde.
        string Modus(string antwort)
        {
            var o = Run($"\"{askArg}\"", antwort + "\n", out _) ?? "";
            return o.Contains("TESTLAUF") ? "trocken"
                 : o.Contains("nicht gefunden") ? "scharf"
                 : "unklar";
        }

        Check("Grosses J loescht", Modus("J") == "scharf", Modus("J"));
        Check("„JA\" loescht ebenfalls", Modus("JA") == "scharf", Modus("JA"));
        Check("Kleines j loescht NICHT (nur Testlauf)", Modus("j") == "trocken", Modus("j"));
        Check("Kleines ja loescht NICHT", Modus("ja") == "trocken", Modus("ja"));
        Check("n ist der Testlauf", Modus("n") == "trocken", Modus("n"));
        Check("Blosse Eingabetaste ist der Testlauf", Modus("") == "trocken", Modus(""));
        // y/yes waren bis v1.17.0 scharf — sie dürfen es nicht wieder werden.
        Check("y und yes loeschen nicht mehr",
              Modus("y") == "trocken" && Modus("yes") == "trocken");

        File.Delete(askPath);
    }
    else
    {
        Console.WriteLine("  [--]   ioBroker-CLI vorhanden - scharfer Lauf nicht getestet");
        nichtGelaufen.Add("Scharfer Lauf und Modusabfrage des Aufraeum-Skripts "
                          + "(ioBroker-CLI auf diesem Rechner vorhanden)");
    }

    File.Delete(shPath);
}

// --- Fehlermeldung und Reparaturhinweis der Backup-Prüfung ---
// Der JSON-Leser zählt Zeilen ab 0, jeder Editor ab 1. Ohne Umrechnung schickt die
// Meldung den Nutzer eine Zeile zu weit nach oben. Gegenprobe am echten Praxisfall:
// Kommentar in Zeile 6, .NET meldet LineNumber: 5.
var rawJsonError = "'/' is an invalid start of a property name. Expected a '\"'. " +
                   "LineNumber: 5 | BytePositionInLine: 0.";
var friendly = BackupCheckPresenter.FriendlyError(rawJsonError);
Console.WriteLine($"  Übersetzte JSON-Fehlermeldung: {friendly}");
Check("Zeilennummer wird von 0-basiert auf 1-basiert umgerechnet", friendly.Contains("Zeile 6"));
Check("Spalte wird ebenfalls umgerechnet", friendly.Contains("Zeichen 1"));
Check("//-Kommentar wird als solcher benannt", friendly.Contains("Kommentare"));
Check("Übersetzung lässt den Parser-Jargon weg", !friendly.Contains("LineNumber"));

// Unbekannte Meldungen bleiben im Original — lieber unübersetzt als falsch übersetzt.
const string unknownError = "Some future parser message nobody translated yet.";
Check("Unbekannte Fehlermeldung bleibt unverändert",
      BackupCheckPresenter.FriendlyError(unknownError) == unknownError);

// Ein Befund ohne Weg zur Behebung nützt niemandem: Der Hinweis nennt die Datei und
// beide CLI-Wege, und stellt klar, dass das Dateisystem der falsche Ort ist.
var brokenRows = new List<CheckRow>
{
    new("esphome.admin/tsconfig.json", "Optional", "BESCHÄDIGT", friendly, CheckSeverity.Problem)
};
var repair = BackupCheckPresenter.RepairHint(brokenRows);
Check("Reparaturhinweis erscheint bei beschädigter optionaler Datei", repair is not null);
Check("Reparaturhinweis nennt die betroffene Datei",
      repair!.Contains("esphome.admin/tsconfig.json"));
Check("Reparaturhinweis nennt beide CLI-Wege",
      repair.Contains("iobroker file rm") && repair.Contains("iobroker upload esphome"));

// Der js-controller nimmt den ganzen Pfad als EIN Argument und trennt davon das erste
// Segment als Meta-ID ab; seine Usage-Beispiele schreiben ihn mit führendem Schrägstrich
// („file rm /vis-2.0/main/img/picture.png"). Ohne Slash liefe es zwar auch, aber ein
// Befehl zum Kopieren soll der dokumentierten Form entsprechen.
Check("Löschbefehl folgt der dokumentierten Pfadform mit führendem Schrägstrich",
      repair.Contains("iobroker file rm /esphome.admin/tsconfig.json"));

// Das Installationsskript legt Symlinks /usr/bin/iobroker und /usr/bin/iob an. Ohne
// diesen Satz fragt jeder nach, ob er vorher nach /opt/iobroker wechseln muss.
Check("Reparaturhinweis klärt, dass kein Verzeichniswechsel nötig ist",
      repair.Contains("Verzeichniswechsel ist nicht nötig")
      && repair.Contains("cd /opt/iobroker"));
Check("Reparaturhinweis warnt vor der Dateisystem-Falle", repair.Contains("NICHT"));

// Der Weg ohne Kommandozeile steht vorn: Nicht jeder mit einem ioBroker hat eine
// SSH-Sitzung zur Hand, und für eine tsconfig.json ist Löschen ohnehin die richtige
// Antwort. Er nutzt denselben Admin-Tab, in dem oben schon der Fundort steht.
Check("Reparaturhinweis nennt das Löschen im Admin",
      repair.Contains("Papierkorb") && repair.Contains("Dateien"));
// Gemessen wird an den Weg-Marken, nicht an den Befehlen: „iobroker upload" steht auch in
// der Risiko-Einschätzung darüber — dort aber als Begründung, nicht als Handlungsweg.
Check("Reparaturhinweis stellt den Weg ohne Kommandozeile voran",
      repair.IndexOf("Weg 1 (am einfachsten", StringComparison.Ordinal)
      < repair.IndexOf("Weg 2: Datei korrigieren", StringComparison.Ordinal));

// Von den beiden CLI-Wegen kommt der Upload zuerst — aber nur, wenn die Quelldatei
// vorher korrigiert wurde. Ohne diesen Hinweis schreibt er den Fehler nur erneut hinein.
Check("Reparaturhinweis nennt den Upload vor dem CLI-Löschen",
      repair.IndexOf("Weg 2: Datei korrigieren", StringComparison.Ordinal)
      < repair.IndexOf("Weg 3: dasselbe Löschen", StringComparison.Ordinal)
      && repair.IndexOf("iobroker upload esphome", StringComparison.Ordinal)
         < repair.IndexOf("iobroker file rm", StringComparison.Ordinal));
Check("Reparaturhinweis nennt die Bedingung für den Upload",
      repair.Contains("korrigieren") && repair.Contains("erneut hinein"));
Check("Reparaturhinweis stellt klar, dass das Backup nutzbar bleibt",
      repair.Contains("einspielbar"));

// Der Upload ist nicht selektiv — er schreibt alle Dateien des Adapters neu. Wer an einer
// anderen Datei desselben Adapters etwas angepasst hat, verliert das sonst stillschweigend.
Check("Reparaturhinweis warnt vor der Reichweite des Uploads",
      repair.Contains("ALLE Dateien dieses Adapters"));

// --- Risiko-Einschätzung: *.admin gehört dem Adapter, alles andere dem Nutzer ---
// Bei einer tsconfig.json ist Löschen folgenlos: „iobroker upload" legt sie neu an. Eine
// Backup-Aufforderung wäre hier nur Lärm und würde die echte Warnung entwerten.
Check("Bei reinen *.admin-Befunden gibt der Hinweis Entwarnung",
      repair.Contains("Zu verlieren gibt es dabei nichts"));
Check("Entwarnung verlangt kein Backup",
      !repair.Contains("Sorge vorher für ein aktuelles Backup"));

// Gegenprobe: Eine Datei im Instanz-Namensraum stammt aus keinem Installationsordner. Sie
// zu löschen ist endgültig — hier muss vor dem Eingriff ein aktuelles Backup stehen.
var ownContentRows = new List<CheckRow>
{
    new("esphome.admin/tsconfig.json", "Optional", "BESCHÄDIGT", friendly, CheckSeverity.Problem),
    new("vis-2.0/main/vis-views.json", "Optional", "BESCHÄDIGT", friendly, CheckSeverity.Problem)
};
var ownHint = BackupCheckPresenter.RepairHint(ownContentRows, false,
                                              new DateTime(2026, 8, 3, 4, 30, 0))!;
// Der Wortlaut steht hier im Protokoll: Dieser Absatz entscheidet, ob jemand vor dem
// Löschen ein Backup zieht — er gehört bei der Abnahme gelesen, nicht nur gezählt.
Console.WriteLine("  --- Risiko-Hinweis bei eigenen Inhalten ---");
foreach (var line in ownHint.Split('\n')
             .SkipWhile(l => !l.StartsWith("Wie riskant"))
             .TakeWhile(l => !l.StartsWith("Reparieren lässt sich")))
    Console.WriteLine("  " + line);
Check("Eigene Inhalte lösen die Backup-Aufforderung aus",
      ownHint.Contains("Sorge vorher für ein aktuelles Backup"));
Check("Backup-Aufforderung nennt beide Wege zum Backup",
      ownHint.Contains("BackitUp") && ownHint.Contains("iobroker backup"));
Check("Backup-Aufforderung nennt das Datum des geprüften Backups",
      ownHint.Contains("vom 03.08.2026 04:30"));
Check("Risiko-Hinweis benennt die betroffene Datei",
      ownHint.Contains("vis-2.0/main/vis-views.json"));
Check("Risiko-Hinweis stellt klar, dass Löschen endgültig ist",
      ownHint.Contains("Löschen ist endgültig"));
// Weg 2 setzt eine Quelldatei im Adapter-Ordner voraus. Die gibt es für eigene Inhalte
// nicht — ohne diesen Satz schickt der Hinweis den Nutzer auf einen Weg, den es nicht gibt.
Check("Weg 2 nennt für eigene Inhalte den Umweg über Herunterladen und Hochladen",
      ownHint.Contains("keinen Upload-Befehl")
      && ownHint.Contains("herunterladen") && ownHint.Contains("wieder hochladen"));

// Der Kern des Fehlers, der in der Oberfläche auffiel: „AdapterOf" macht aus
// „vis-2.0/main/vis-views.json" den Adapternamen „vis-2" — der Upload-Befehl dazu wäre
// wirkungslos (er schreibt die Adapterdateien neu, nicht die View) und widerspräche der
// Warnung direkt darüber. Angeboten werden dürfen nur Adapter aus *.admin-Befunden.
Check("Kein Upload-Befehl für eigene Inhalte",
      !ownHint.Contains("iobroker upload vis-2"));
Check("Upload-Befehl für den Adapter-Befund bleibt erhalten",
      ownHint.Contains("iobroker upload esphome"));

// Gegenprobe ohne jeden *.admin-Befund: Dann darf gar kein Upload-Befehl dastehen.
var onlyOwnHint = BackupCheckPresenter.RepairHint(new List<CheckRow>
{
    new("vis-2.0/main/vis-views.json", "Optional", "BESCHÄDIGT", friendly, CheckSeverity.Problem)
})!;
// Gemessen an der eingerückten Befehlszeile: Im Fließtext darf „iobroker upload"
// vorkommen — dort erklärt der Hinweis ja gerade, warum der Befehl hier nicht hilft.
Check("Ohne *.admin-Befund erscheint kein Upload-Befehl zum Kopieren",
      !onlyOwnHint.Contains("\n    iobroker upload"));
Check("Ohne *.admin-Befund bleibt der Löschbefehl bestehen",
      onlyOwnHint.Contains("iobroker file rm /vis-2.0/main/vis-views.json"));
Check("Risiko-Hinweis steht vor den drei Wegen",
      ownHint.IndexOf("Wie riskant ist das?", StringComparison.Ordinal)
      < ownHint.IndexOf("Reparieren lässt sich das auf drei Wegen", StringComparison.Ordinal));

// Ohne verwertbares Datum bleibt der Satz allgemein statt ein leeres Datum zu zeigen.
var noDateHint = BackupCheckPresenter.RepairHint(ownContentRows)!;
Check("Ohne Backup-Datum bleibt die Aufforderung allgemein",
      noDateHint.Contains("Das hier geprüfte Backup taugt dazu nur")
      && !noDateHint.Contains("01.01.0001"));

// Ein abgeschnittenes Archiv hat weiter Vorrang: Dort ist nichts zu reparieren, also darf
// auch keine Risiko-Abwägung zu Wegen erscheinen, die es in dem Fall gar nicht gibt.
var truncatedHint = BackupCheckPresenter.RepairHint(ownContentRows, true)!;
Check("Bei abgeschnittenem Archiv bleibt der Risiko-Hinweis außen vor",
      !truncatedHint.Contains("Wie riskant ist das?"));

// Der Fundort ist der Punkt, an dem in der Praxis reihenweise Leute
// steckenblieben: BackitUp meldet einen tmp/backup/-Pfad, der nach dem Backup leer ist.
// Wer dort nachsieht, findet nichts und hält die Warnung für einen Fehlalarm.
Check("Reparaturhinweis nennt den echten Pfad in der Datei-Datenbank",
      repair.Contains("/opt/iobroker/iobroker-data/files/esphome.admin/tsconfig.json"));
Check("Reparaturhinweis nennt den Weg über die Oberfläche",
      repair.Contains("Dateien") && repair.Contains("Experten-Modus"));
Check("Reparaturhinweis klärt den irreführenden BackitUp-Pfad auf",
      repair.Contains("tmp/backup/files") && repair.Contains("Arbeitsverzeichnis"));

// Derselbe Fundort steht in jeder betroffenen Tabellenzeile — der CSV-Export enthält
// nur die Tabelle, nicht den Hinweis darunter. Ohne das geht der Fundort beim
// Weitergeben der CSV verloren, und genau dafür wird sie benutzt.
var whereRow = BackupCheckPresenter.WhereToFind("esphome.admin/tsconfig.json");
Console.WriteLine($"  Fundort in der Details-Spalte: {whereRow}");
Check("Fundort in der Zeile nennt den Host-Pfad",
      whereRow.Contains("/opt/iobroker/iobroker-data/files/esphome.admin/tsconfig.json"));
Check("Fundort in der Zeile nennt den Dateien-Tab und den Meta-Ordner",
      whereRow.Contains("Dateien") && whereRow.Contains("esphome.admin"));

// Ohne Befund kein Hinweis — sonst stünde dauerhaft eine Anleitung ohne Anlass da.
Check("Ohne beschädigte Dateien kein Reparaturhinweis",
      BackupCheckPresenter.RepairHint(new List<CheckRow>
      {
          new("objects.jsonl", "Pflicht", "gültig", "alle gültig", CheckSeverity.Ok)
      }) is null);

// --- Lücken, die ioBroker meldet, die Prüfung aber übersah ---
// Maßstab ist durchgehend, was node beim Einlesen tut. Alle drei Fälle wurden gegen
// node gegengeprüft: dort ungültig, hier zuvor „gültig" oder gar nicht erst geprüft.
Console.WriteLine("\n=== Erkennung beschädigter Dateien (BOM, leer, abgeschnitten) ===");

var luecken = Path.Combine(Path.GetTempPath(), "iob-verify-luecken");
if (Directory.Exists(luecken)) Directory.Delete(luecken, true);
Directory.CreateDirectory(Path.Combine(luecken, "backup", "files", "test.admin"));

File.WriteAllText(Path.Combine(luecken, "backup", "objects.jsonl"),
    "{\"_id\":\"system.adapter.test.0\",\"type\":\"instance\"," +
    "\"common\":{\"name\":\"test\",\"version\":\"1.0.0\",\"enabled\":true},\"native\":{}}\n");

// BOM vor gültigem JSON: .NET liest darüber hinweg, node bricht ab.
File.WriteAllBytes(Path.Combine(luecken, "backup", "files", "test.admin", "bom.json"),
    new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes("{\"a\":1}")).ToArray());

// 0-Byte-Datei: hat keinen Datenstrom und fiel früher komplett aus der Prüfung.
File.WriteAllBytes(Path.Combine(luecken, "backup", "files", "test.admin", "leer.json"),
    Array.Empty<byte>());

var lueckenTar = Path.Combine(luecken, "iobroker_2026_08_12-10_00_00_backupiobroker.tar.gz");
CreateTarGz(Path.Combine(luecken, "backup"), lueckenTar);

// Baut ein tar.gz wie Backitup: der Ordner „backup" bleibt als oberste Ebene erhalten.
static void CreateTarGz(string sourceDir, string targetFile)
{
    using var fs = File.Create(targetFile);
    using var gz = new GZipStream(fs, CompressionLevel.Optimal);
    TarFile.CreateFromDirectory(sourceDir, gz, includeBaseDirectory: true);
}

var lueckenData = BackupLoader.Load(lueckenTar);
var lueckenRows = BackupCheckPresenter.BuildRows(lueckenData);

var bomRow = lueckenRows.FirstOrDefault(r => r.File.EndsWith("bom.json", StringComparison.Ordinal));
Check("Datei mit BOM wird gefunden", bomRow is not null);
Check("Datei mit BOM gilt als beschädigt", bomRow?.Severity == CheckSeverity.Problem);
Check("BOM-Meldung nennt die Ursache beim Namen", bomRow?.Detail.Contains("BOM") == true);

var leerRow = lueckenRows.FirstOrDefault(r => r.File.EndsWith("leer.json", StringComparison.Ordinal));
Check("Leere Datei wird überhaupt geprüft", leerRow is not null);
Check("Leere Datei gilt als beschädigt", leerRow?.Severity == CheckSeverity.Problem);
Check("Meldung zur leeren Datei kommt ohne sinnlose Positionsangabe",
      leerRow?.Detail.Contains("Zeile") == false);

// Jede beschädigte Zeile trägt ihren Fundort — unabhängig davon, woran die Datei
// scheitert. Heile Dateien nicht: Dort gibt es nichts zu suchen, und 700 Zeilen „OK"
// mit angehängtem Pfad wären in der CSV nur Lärm.
Check("Beschädigte Zeile nennt den Fundort", bomRow?.Detail.Contains("Fundort:") == true);
Check("Fundort der beschädigten Zeile zeigt in die Datei-Datenbank",
      bomRow?.Detail.Contains("/opt/iobroker/iobroker-data/files/test.admin/") == true);
Check("Heile Zeilen bleiben ohne Fundort",
      lueckenRows.Where(r => r.Severity == CheckSeverity.Ok && r.Kind == "Optional")
                 .All(r => r.Detail == "OK"));

// Abgeschnittenes Archiv: Die früh liegenden Pflichtdateien sind vollständig lesbar,
// deshalb wirkte ein halbes Backup zuvor tadellos.
var halbeDatei = Path.Combine(luecken, "abgeschnitten_2026_08_12-10_00_00_backupiobroker.tar.gz");
var alleBytes = File.ReadAllBytes(full);
File.WriteAllBytes(halbeDatei, alleBytes.Take(alleBytes.Length / 2).ToArray());

var halbesBackup = BackupLoader.Load(halbeDatei);
Console.WriteLine($"  Abgeschnitten: {halbesBackup.Validation.EntriesRead} Einträge gelesen, " +
                  $"Urteil: {halbesBackup.Validation.Health}");
Check("Abgeschnittenes Archiv wird als solches erkannt",
      halbesBackup.Validation.ArchiveTruncated);
Check("Abgeschnittenes Archiv gilt nicht mehr als gültig",
      halbesBackup.Validation.Health == BackupHealth.Invalid);
Check("Urteil benennt die Unvollständigkeit",
      halbesBackup.Validation.HealthText.Contains("unvollständig"));
Check("Hinweis erklärt, dass nur Ersetzen hilft",
      BackupCheckPresenter.RepairHint(BackupCheckPresenter.BuildRows(halbesBackup), true)
          ?.Contains("nicht reparieren") == true);

// Das vollständige Backup darf davon unberührt bleiben — sonst wäre die Erkennung wertlos.
Check("Vollständiges Archiv wird nicht fälschlich als abgeschnitten gemeldet",
      !fullData.Validation.ArchiveTruncated);

Directory.Delete(luecken, true);

// --- Backup-Prüfung (JSON-Validierung nach js-controller-Vorbild) ---
var val = fullData.Validation;
Console.WriteLine($"  Backup-Prüfung: {val.HealthText}   " +
                  $"(objects {val.Objects.Lines} Z./{val.Objects.InvalidLines} ungültig, " +
                  $"states {val.States.Lines} Z./{val.States.InvalidLines} ungültig, " +
                  $"optionale JSON {val.OptionalCount}/{val.OptionalInvalid} ungültig)");
Check("Backup-Prüfung wurde durchgeführt", val.WasChecked);
Check("objects.jsonl als vorhanden erkannt", val.Objects.Present);
CheckRef("objects.jsonl geprüfte Zeilen", val.Objects.Lines, "objekte");
CheckEq("objects.jsonl ungültige Zeilen", val.Objects.InvalidLines, 0);
Check("states.jsonl als vorhanden erkannt", val.States.Present);
CheckRef("states.jsonl geprüfte Zeilen", val.States.Lines, "states");
CheckEq("states.jsonl ungültige Zeilen", val.States.InvalidLines, 0);
CheckEq("Optionale JSON-Dateien geprüft (die zwei vis-views.json)", val.OptionalCount, 2);
Check("Alle optionalen JSON-Dateien gültig", val.OptionalInvalid == 0);
CheckEq("Gesamturteil: Backup gültig", val.Health, BackupHealth.Valid);
Check("Urteilstext meldet „gültig\"", val.HealthText.Contains("gültig"));

// Urteilslogik synthetisch: leer=gültig, kaputte optionale Datei=Warnung, kaputte Pflicht=beschädigt.
var vTest = new BackupValidation { WasChecked = true };
CheckEq("Leeres Ergebnis gilt als gültig", vTest.Health, BackupHealth.Valid);
vTest.OptionalFiles.Add(new JsonFileCheck { Path = "backup/files/0_userdata.0/demo.json", Valid = false, Error = "x" });
CheckEq("Kaputte optionale Datei → nur Warnung", vTest.Health, BackupHealth.Warnings);
vTest.Objects.InvalidLines = 1;
CheckEq("Kaputte Pflichtdatei → beschädigt", vTest.Health, BackupHealth.Invalid);

// Echte Gegenprobe: eine objects.jsonl mit einer kaputten Zeile laden.
var badJsonl = Path.Combine(Path.GetTempPath(), "iob_bad_objects.jsonl");
File.WriteAllText(badJsonl,
    "{\"_id\":\"a.0\",\"type\":\"state\",\"common\":{}}\n" +
    "{\"_id\":\"b.0\",\"type\":\"state\",\"common\":{},}\n" +   // ungültig: abschließendes Komma
    "{\"_id\":\"c.0\",\"type\":\"state\",\"common\":{}}\n");
try
{
    var bad = BackupLoader.Load(badJsonl);
    CheckEq("Kaputte JSONL: 3 Zeilen geprüft", bad.Validation.Objects.Lines, 3);
    CheckEq("Kaputte JSONL: 1 ungültige Zeile erkannt", bad.Validation.Objects.InvalidLines, 1);
    CheckEq("Kaputte JSONL: Fehler in Zeile 2 lokalisiert", bad.Validation.Objects.FirstErrorLine, 2);
    CheckEq("Kaputte JSONL: Urteil beschädigt", bad.Validation.Health, BackupHealth.Invalid);
    CheckEq("Kaputte JSONL: Rest wird trotzdem geladen", bad.Objects.Count, 2);
}
finally { File.Delete(badJsonl); }

// ---------------------------------------------------------------- Vergleich

Console.WriteLine();
Console.WriteLine("=== Backup-Vergleich ===");

// 1. Ein Backup gegen sich selbst: Es darf nicht eine einzige Aenderung gemeldet werden.
var same = BackupComparer.Compare(fullData, fullData);
Check("Vergleich mit sich selbst meldet keine Aenderung", same.IsIdentical,
      $"{same.ChangedInstances} Instanzen, {same.ChangedScripts} Skripte, " +
      $"{same.AddedObjects}+/{same.RemovedObjects}- Objekte, {same.ChangedViews} Views");
Check("Kennzahlen sind beim Selbstvergleich deckungsgleich",
      same.Metrics.All(m => m.Delta == 0));

// 2. Synthetischer Vorgaengerstand. Vier bekannte Unterschiede werden eingebaut, und der
//    Vergleich muss genau diese vier finden — nicht mehr und nicht weniger:
//      • eine Adapter-Instanz samt ihrer Objekte fehlt im alten Stand  -> "neu"
//      • ein Skript hatte eine Zeile mehr und war anders aktiviert     -> "geaendert"
//      • ein Skript gab es nur im alten Stand                          -> "geloescht"
//      • ein Skript gibt es nur im neuen Stand                         -> "neu"
var victimInstance = fullData.Instances.OrderBy(i => i.ObjectCount).First(i => i.ObjectCount > 5);
var editedOriginal = fullData.Scripts.First(s => s.Engine == ScriptEngine.JavaScript);
var newOnlyScript = fullData.Scripts.Last(s => s.Engine == ScriptEngine.Blockly);

var editedOld = new ScriptInfo
{
    Id = editedOriginal.Id,
    Name = editedOriginal.Name,
    Folder = editedOriginal.Folder,
    Engine = editedOriginal.Engine,
    Enabled = !editedOriginal.Enabled,          // zusaetzlich der Status
    Source = editedOriginal.Source,
    CleanSource = "// eine Zeile, die es vorher gab\n" + editedOriginal.CleanSource,
    BlocklyXml = editedOriginal.BlocklyXml
};

var goneScript = new ScriptInfo
{
    Id = "script.js.Nur_Im_Alten_Stand",
    Name = "Nur_Im_Alten_Stand",
    Folder = "",
    Engine = ScriptEngine.JavaScript,
    Enabled = true,
    Source = "log('weg');",
    CleanSource = "log('weg');"
};

var older = new BackupData
{
    SourceFile = "synthetisch-alt.tar.gz",
    Kind = BackupKind.Full,
    CreatedAt = fullData.CreatedAt!.Value.AddDays(-7),
    Objects = fullData.Objects.Where(o => !o.Id.StartsWith(victimInstance.Namespace + ".",
                                                           StringComparison.Ordinal)).ToList(),
    Scripts = fullData.Scripts
                      .Where(s => s.Id != editedOriginal.Id && s.Id != newOnlyScript.Id)
                      .Append(editedOld)
                      .Append(goneScript)
                      .OrderBy(s => s.DisplayPath, StringComparer.OrdinalIgnoreCase)
                      .ToList(),
    Instances = fullData.Instances.Where(i => i != victimInstance).ToList(),
    VisViews = fullData.VisViews,
    StateCount = fullData.StateCount,
    States = fullData.States
};

var cmp = BackupComparer.Compare(older, fullData);

Console.WriteLine($"  Reihenfolge: {Path.GetFileName(cmp.Before.SourceFile)} -> " +
                  $"{Path.GetFileName(cmp.After.SourceFile)}   ({cmp.Span?.TotalDays:F0} Tage)");
Check("Aelteres Backup wird als Vorher-Stand erkannt",
      cmp.Before.SourceFile == "synthetisch-alt.tar.gz");
Check("Reihenfolge gilt als sicher", !cmp.OrderUncertain);

// Auch bei umgekehrter Uebergabe muss dasselbe herauskommen — sonst haengt das Ergebnis
// davon ab, welche Datei zuerst angeklickt wurde.
var reversed = BackupComparer.Compare(fullData, older);
Check("Uebergabereihenfolge aendert das Ergebnis nicht",
      reversed.Before.SourceFile == cmp.Before.SourceFile
      && reversed.ChangedInstances == cmp.ChangedInstances
      && reversed.ChangedScripts == cmp.ChangedScripts
      && reversed.AddedObjects == cmp.AddedObjects);

var addedInstance = cmp.Instances.Where(i => i.Kind == ChangeKind.Added).ToList();
Check("Die entfernte Instanz erscheint als neu hinzugekommen",
      addedInstance.Count == 1 && addedInstance[0].Namespace == victimInstance.Namespace,
      string.Join(", ", addedInstance.Select(i => i.Namespace)));

var addedNs = cmp.Namespaces.FirstOrDefault(n => n.Namespace == victimInstance.Namespace);
Check("Die Objekte der Instanz erscheinen als neu",
      addedNs is not null && addedNs.Added == victimInstance.ObjectCount && addedNs.Removed == 0,
      $"{addedNs?.Added} neu / {addedNs?.Removed} entfernt, erwartet {victimInstance.ObjectCount}/0");

var edited = cmp.Scripts.FirstOrDefault(s => s.Id == editedOriginal.Id);
Check("Das geaenderte Skript wird als geaendert gemeldet", edited is { Kind: ChangeKind.Changed });
Check("Die entfernte Zeile wird gezaehlt", edited is { RemovedLines: 1, AddedLines: 0 },
      $"+{edited?.AddedLines} / -{edited?.RemovedLines}");
Check("Der Statuswechsel des Skripts wird erkannt", edited?.EnabledChanged == true);

Check("Unveraenderte Skripte werden nicht als geaendert gemeldet",
      cmp.Scripts.Count(s => s.Kind == ChangeKind.Changed) == 1,
      string.Join(", ", cmp.Scripts.Where(s => s.Kind == ChangeKind.Changed)
                                   .Select(s => s.DisplayPath).Take(3)));

Check("Das nur im alten Stand vorhandene Skript gilt als geloescht",
      cmp.Scripts.Count(s => s.Kind == ChangeKind.Removed) == 1
      && cmp.Scripts.Any(s => s.Id == goneScript.Id && s.Kind == ChangeKind.Removed),
      string.Join(", ", cmp.Scripts.Where(s => s.Kind == ChangeKind.Removed).Select(s => s.Id)));

Check("Das nur im neuen Stand vorhandene Skript gilt als neu",
      cmp.Scripts.Count(s => s.Kind == ChangeKind.Added) == 1
      && cmp.Scripts.Any(s => s.Id == newOnlyScript.Id && s.Kind == ChangeKind.Added),
      string.Join(", ", cmp.Scripts.Where(s => s.Kind == ChangeKind.Added).Select(s => s.Id)));

Check("Identische VIS-Views ergeben keine View-Aenderung", cmp.ChangedViews == 0,
      string.Join(", ", cmp.Views.Where(v => v.Kind != ChangeKind.Unchanged)
                                 .Select(v => v.View).Take(3)));
Check("Views werden trotzdem geprueft (nicht stillschweigend uebersprungen)",
      cmp.Views.Count > 0, $"{cmp.Views.Count} Views verglichen");

Console.WriteLine($"  Ergebnis: {cmp.ChangedInstances} Instanzen, {cmp.ChangedScripts} Skripte, " +
                  $"+{cmp.AddedObjects}/-{cmp.RemovedObjects} Objekte, {cmp.ChangedViews} Views");

// 3. Versionsvergleich: 1.10.0 ist neuer als 1.9.0 — ein reiner Textvergleich saehe das falsch.
var up = new InstanceChange
{
    Namespace = "test.0", Adapter = "test", Kind = ChangeKind.Changed,
    VersionBefore = "1.9.0", VersionAfter = "1.10.0"
};
var down = new InstanceChange
{
    Namespace = "test.0", Adapter = "test", Kind = ChangeKind.Changed,
    VersionBefore = "1.10.0", VersionAfter = "1.9.0"
};
Check("1.9.0 -> 1.10.0 gilt als Update", up.VersionDirection < 0 && up.Detail.Contains("Update"));
Check("1.10.0 -> 1.9.0 gilt als Downgrade", down.VersionDirection > 0 && down.Detail.Contains("Downgrade"));

// ---------------------------------------------------------------- Herkunft

Console.WriteLine();
Console.WriteLine("=== Herkunft der Backups ===");

var id1 = fullData.System;
Console.WriteLine($"  Voll-Backup : {id1.Describe()}");
Check("Installations-UUID gefunden", id1.InstallationId.Length > 0);
Check("UUID hat UUID-Form", id1.InstallationId.Count(c => c == '-') == 4, id1.ShortId);
Check("Hostname trotz Platzhalter im Backup ermittelt",
      id1.Hostname.Length > 0 && !id1.Hostname.Contains("$$"), id1.Hostname);
Check("IPv4-Adresse ermittelt", id1.Address.Count(c => c == '.') == 3, id1.Address);
Check("Keine IPv6-Adresse im UI (enthaelt die MAC)", !id1.Address.Contains(':'));
Check("js-controller-Version ermittelt", id1.ControllerVersion.Length > 0, id1.ControllerVersion);
Check("System gilt als bekannt", id1.IsKnown);

// Die Systemzeile steht im UI und landet damit auf jedem Bildschirmfoto. Sie darf nur
// technische Merkmale enthalten — kein Ort, keine Koordinaten, keine vollstaendige UUID.
var described = id1.Describe();
var describedParts = described.Split("  ·  ");
Console.WriteLine($"  Beschreibung: {described}");
CheckEq("Systembeschreibung hat genau vier Bestandteile", describedParts.Length, 4);
Check("Bestandteile sind Hostname, IPv4, Controller-Version und gekuerzte ID",
      describedParts[0] == id1.Hostname
      && describedParts[1] == id1.Address
      && describedParts[2] == "js-controller " + id1.ControllerVersion
      && describedParts[3] == "ID " + id1.ShortId + "…",
      described);
Check("Vollstaendige Installations-ID wird nie angezeigt",
      !described.Contains(id1.InstallationId, StringComparison.Ordinal));

// Gegenprobe gegen die Datenquelle: Der Ort steht in system.config und darf nirgends
// im geladenen Ergebnis auftauchen — auch nicht ueber einen anderen Weg.
var configObject = fullData.Objects.FirstOrDefault(o => o.Id == "system.config");
Check("system.config wird als Objekt gelesen, aber ohne Standortdaten",
      configObject is not null && !described.Contains(configObject.Name, StringComparison.OrdinalIgnoreCase));

// Ein Skript-Backup enthaelt keine Systemobjekte — das muss ehrlich als „unbekannt" gelten
// statt eine falsche Uebereinstimmung vorzutaeuschen.
Check("Skript-Backup traegt keine Systemkennung", !scriptsOnly.System.IsKnown);
CheckEq("Skript-Backup gegen Voll-Backup: nicht pruefbar",
        BackupComparer.MatchSystems(scriptsOnly.System, fullData.System), SystemMatch.Unknown);

CheckEq("Gleiches Backup gegen sich selbst: selbes System",
        BackupComparer.MatchSystems(id1, id1), SystemMatch.Same);

if (File.Exists(second))
{
    Console.WriteLine();
    Console.WriteLine("  --- zweites, eigenstaendiges ioBroker-System ---");
    sw.Restart();
    var otherSystem = BackupLoader.Load(second);
    sw.Stop();

    var id2 = otherSystem.System;
    Console.WriteLine($"  Zweitsystem : {id2.Describe()}");
    Console.WriteLine($"  Umfang      : {otherSystem.Objects.Count:N0} Objekte, " +
                      $"{otherSystem.StateCount:N0} States, {otherSystem.Instances.Count} Instanzen, " +
                      $"{otherSystem.Scripts.Count} Skripte   (geladen in {sw.ElapsedMilliseconds} ms)");

    Check("Zweitsystem hat eine eigene Installations-UUID", id2.InstallationId.Length > 0);
    Check("Die beiden UUIDs sind verschieden", id1.InstallationId != id2.InstallationId,
          $"{id1.ShortId} vs {id2.ShortId}");

    var verdict = BackupComparer.MatchSystems(id1, id2);
    CheckEq("Herkunftspruefung erkennt verschiedene Systeme", verdict, SystemMatch.Different);
    CheckEq("Pruefung ist symmetrisch", BackupComparer.MatchSystems(id2, id1), SystemMatch.Different);
    CheckEq("Zweitsystem gegen sich selbst: selbes System",
            BackupComparer.MatchSystems(id2, id2), SystemMatch.Same);

    // Der Vergleich muss trotzdem durchlaufen — verschiedene Systeme zu vergleichen ist
    // erlaubt, es wird nur davor gewarnt.
    var cross = BackupComparer.Compare(fullData, otherSystem);
    CheckEq("Systemwarnung landet im Vergleichsergebnis", cross.SystemMatch, SystemMatch.Different);
    Check("Warntext benennt beide Systeme",
          cross.SystemMatchText.Contains("Verschiedene Systeme"), cross.SystemMatchText);
    Check("Vergleich zweier Systeme laeuft durch", !cross.IsIdentical);
    Console.WriteLine($"  Quervergleich: {cross.ChangedInstances} Instanzen, " +
                      $"{cross.ChangedScripts} Skripte, +{cross.AddedObjects:N0}/-{cross.RemovedObjects:N0} Objekte");

    // Zwei verschiedene Systeme muessen sich massiv unterscheiden. Kaeme hier wenig heraus,
    // waere der Vergleich selbst kaputt.
    Check("Quervergleich meldet erwartungsgemaess sehr viele Unterschiede",
          cross.AddedObjects + cross.RemovedObjects > 1000,
          $"{cross.AddedObjects + cross.RemovedObjects} Objektunterschiede");

    // Gemeinsamkeiten sind trotzdem plausibel: beide Systeme sind ioBroker.
    var sharedAdapters = fullData.Instances.Select(i => i.Adapter)
        .Intersect(otherSystem.Instances.Select(i => i.Adapter), StringComparer.OrdinalIgnoreCase)
        .OrderBy(a => a, StringComparer.OrdinalIgnoreCase).ToList();
    Console.WriteLine($"  Adapter auf beiden Systemen: {sharedAdapters.Count} " +
                      $"({string.Join(", ", sharedAdapters.Take(8))}…)");
    Check("Beide Systeme teilen sich Adapter", sharedAdapters.Count > 0);
}
else
{
    Console.WriteLine($"  (uebersprungen: {Path.GetFileName(second)} nicht vorhanden)");
}

// 4. Zeilenvergleich
Console.WriteLine();
Console.WriteLine("=== Zeilenvergleich (Diff) ===");

var d1 = TextDiff.Compare("a\nb\nc", "a\nB\nc");
Check("Eine geaenderte Zeile ergibt +1/-1", d1 is { Added: 1, Removed: 1 }, $"+{d1.Added}/-{d1.Removed}");
Check("Unveraenderte Zeilen bleiben erhalten",
      d1.Lines.Count(l => l.Kind == DiffKind.Unchanged) == 2);
Check("Zeilennummern werden mitgefuehrt",
      d1.Lines.All(l => l.Kind switch
      {
          DiffKind.Added => l.NewLine is not null && l.OldLine is null,
          DiffKind.Removed => l.OldLine is not null && l.NewLine is null,
          _ => l.OldLine is not null && l.NewLine is not null
      }));

Check("Identischer Text ergibt keine Aenderung", !TextDiff.Compare("x\ny", "x\ny").HasChanges);
Check("Leer gegen Text ergibt nur Hinzufuegungen",
      TextDiff.Compare("", "a\nb") is { Added: 2, Removed: 0 });
Check("Text gegen leer ergibt nur Entfernungen",
      TextDiff.Compare("a\nb", "") is { Added: 0, Removed: 2 });
Check("Unterschiedliche Zeilenenden gelten nicht als Aenderung",
      !TextDiff.Compare("a\r\nb", "a\nb").HasChanges);

var d2 = TextDiff.Compare("1\n2\n3\n4\n5", "1\n2\n2b\n3\n4\n5");
Check("Eingefuegte Zeile in der Mitte ergibt genau +1", d2 is { Added: 1, Removed: 0 },
      $"+{d2.Added}/-{d2.Removed}");

// Echtes Skript gegen sich selbst mit einer entfernten Zeile — realistischer Umfang.
var realScript = ScriptChange.ComparableText(fullData.Scripts.OrderByDescending(
    s => ScriptChange.ComparableText(s).Length).First());
var realLines = realScript.Replace("\r\n", "\n").Split('\n');
var realModified = string.Join("\n", realLines.Where((_, i) => i != realLines.Length / 2));
sw.Restart();
var d3 = TextDiff.Compare(realScript, realModified);
sw.Stop();
Console.WriteLine($"  Groesstes Skript: {realLines.Length} Zeilen, Diff in {sw.ElapsedMilliseconds} ms");
Check("Diff des groessten Skripts meldet genau die entfernte Zeile",
      d3 is { Added: 0, Removed: 1 }, $"+{d3.Added}/-{d3.Removed}");
Check("Diff bleibt schnell", sw.ElapsedMilliseconds < 3000, $"{sw.ElapsedMilliseconds} ms");

// ---------------------------------------------------------------- Sortierung

Console.WriteLine();
Console.WriteLine("=== Sortierung der Tabellenspalten ===");

// Kurzschreibweisen: a steht vor b / a steht hinter b / gleichwertig.
bool Before(string a, string b) => DisplayCompare.Compare(a, b) < 0;
bool After(string a, string b) => DisplayCompare.Compare(a, b) > 0;

Check("Zahlen numerisch statt alphabetisch", Before("20", "100"));
Check("Tausenderpunkt wird verstanden", Before("994", "1.064"));
Check("Differenzen mit Vorzeichen", Before("-3", "+12") && Before("±0", "+12") && Before("-3", "±0"));
Check("Typografisches Minus wird erkannt", Before("−3", "+12"));

Check("Versionen segmentweise: 1.9.0 vor 1.10.0", Before("1.9.0", "1.10.0"));
Check("Versionen segmentweise: 8.0.4 vor 8.1.0", Before("8.0.4", "8.1.0"));
Check("Versionen mit Vorabkennung", Before("3.28.3-beta.1", "3.28.3"));
Check("Gleiche Version ist gleichwertig", DisplayCompare.Compare("7.2.2", "7.2.2") == 0);

Check("Datum chronologisch, nicht alphabetisch",
      Before("21.07.2018 22:39", "10.08.2026 09:00"));
Check("Datum mit Alterszusatz",
      Before("21.07.2018 22:39  (2941 T)", "10.08.2026 09:00  (0 T)"));
Check("Datum ohne Uhrzeit", Before("01.01.2020", "02.01.2020"));

Check("Leere Zellen stehen hinten", After("", "irgendwas") && Before("irgendwas", ""));
Check("Zwei leere Zellen sind gleichwertig", DisplayCompare.Compare("", "") == 0);
Check("Text alphabetisch ohne Ruecksicht auf Schreibweise",
      Before("adapter.0", "Zigbee.0") && DisplayCompare.Compare("Ja", "ja") == 0);
Check("Text und Datum gemischt faellt auf Textvergleich zurueck",
      DisplayCompare.Compare("nie beschrieben", "21.07.2018 22:39") != 0);

// Gegenprobe an echten Tabellenwerten: eine Spalte mit Zeitstempeln muss sich in eine
// Reihenfolge bringen lassen, die dem tatsaechlichen Alter entspricht.
var displayOrder = Comparer<string>.Create((a, b) => DisplayCompare.Compare(a, b));

var timeSample = stateReport.All.Where(r => r.LastChange is not null).Take(200)
                                .Select(r => r.LastChangeText).ToList();
var sortedTimes = timeSample.OrderBy(s => s, displayOrder).ToList();
var expectedTimes = stateReport.All.Where(r => r.LastChange is not null).Take(200)
                                   .OrderBy(r => r.LastChange!.Value)
                                   .Select(r => r.LastChangeText).ToList();
Check("Sortierte Zeitstempel entsprechen der echten Reihenfolge",
      sortedTimes.SequenceEqual(expectedTimes),
      $"{sortedTimes.FirstOrDefault()} … {sortedTimes.LastOrDefault()}");

// Und dieselbe Probe fuer die Versionsspalte der Adapter-Instanzen.
var versions = fullData.Instances.Select(i => i.Version).Distinct().ToList();
var sortedVersions = versions.OrderBy(v => v, displayOrder).ToList();
Check("Versionsliste sortiert ohne Ausnahme",
      sortedVersions.Count == versions.Count);
Console.WriteLine($"  Versionen sortiert (Auszug): {string.Join(", ", sortedVersions.Take(6))} …");

// ---------------------------------------------------------------- Export

Console.WriteLine();
Console.WriteLine("=== Export (Grundlage fuer Abnahmetest 3) ===");
var exportDir = Path.Combine(Path.GetTempPath(), "iob_export_test");
if (Directory.Exists(exportDir)) Directory.Delete(exportDir, true);

// Standardfall: nur das Ursprungsformat — Blockly als .xml, alles andere als .js.
// Das ergibt genau eine Datei je Skript, so wie sie auch in ioBroker liegt.
var exp = ScriptExporter.Export(fullData.Scripts, exportDir, ScriptExportFormat.OriginalOnly,
                                fullData.SourceFile);
Console.WriteLine($"  {exp.Scripts} Skripte -> {exp.Files} Dateien (nur Ursprungsformat)");
Check("Export ohne Fehler", exp.Errors.Count == 0, string.Join("; ", exp.Errors.Take(3)));
CheckRef("Erzeugte Dateien: eine je Skript", exp.Files, "skripte");

// Der Export liegt in einem Ordner mit dem Namen des Backups (Anwenderwunsch):
// zwei ausgewertete Backups im selben Zielordner ueberschreiben einander sonst.
var backupFolder = BackupNaming.FolderName(fullData.SourceFile);
// Erwartung aus dem geladenen Archiv abgeleitet statt fest eingetragen — der Dateiname
// eines echten Backups gehoert nicht in den Quelltext.
CheckEq("Backup-Name aus dem Dateinamen abgeleitet", backupFolder,
        Path.GetFileName(fullData.SourceFile).Replace(".tar.gz", "", StringComparison.Ordinal));
var backupDir = Path.Combine(exportDir, backupFolder);
var rootDir = Path.Combine(backupDir, ScriptExporter.RootFolderName);
Check("Ueberordner mit Backup-Namen angelegt", Directory.Exists(backupDir), backupDir);
Check("Ueberordner ioBroker-Skripte angelegt", Directory.Exists(rootDir), rootDir);
CheckEq("Zielordner enthaelt nur den Backup-Ordner",
        Directory.GetFileSystemEntries(exportDir).Length, 1);
CheckEq("Export meldet den Ueberordner", exp.RootDir, rootDir);

// Ohne bekannte Quelldatei bleibt es beim bisherigen Aufbau ohne Zwischenebene.
CheckEq("Ohne Quelldatei kein Backup-Ordner", BackupNaming.FolderName(null), "");
CheckEq("Doppelte Endung .tar.gz wird vollstaendig entfernt",
        BackupNaming.FolderName("/pfad/javascripts_2026_01_02-03_04_05_backupiobroker.tar.gz"),
        "javascripts_2026_01_02-03_04_05_backupiobroker");

var xmlFiles = Directory.GetFiles(exportDir, "*.xml", SearchOption.AllDirectories);
var jsFiles = Directory.GetFiles(exportDir, "*.js", SearchOption.AllDirectories);
CheckRef("XML-Dateien (Blockly)", xmlFiles.Length, "skripte-blockly");
CheckRef("JS-Dateien (JavaScript/TypeScript)", jsFiles.Length, "skripte-javascript");

// Kein Blockly-Skript darf im Ursprungsformat ein .js danebenliegen haben.
var doppelt = xmlFiles.Count(f => File.Exists(Path.ChangeExtension(f, ".js")));
CheckEq("Kein erzeugtes JS neben dem Blockly-XML", doppelt, 0);

// Der Aktiv-Status darf die Ordnerstruktur nicht mehr veraendern; er steht nur im Dateinamen.
Check("Kein Ordner _DEAKTIVIERT mehr",
      Directory.GetDirectories(exportDir, "_DEAKTIVIERT", SearchOption.AllDirectories).Length == 0);
var disabledFiles = xmlFiles.Concat(jsFiles)
    .Count(f => Path.GetFileNameWithoutExtension(f)
                    .EndsWith(ScriptExporter.DisabledSuffix, StringComparison.Ordinal));
CheckRef("Deaktivierte Skripte am Namenszusatz erkennbar", disabledFiles, "skripte-deaktiviert");

// script.js.global.* gehoert unter "global", script.js.common.* direkt in die Wurzel.
var globalScripts = fullData.Scripts
    .Where(s => s.Id.StartsWith("script.js.global.", StringComparison.OrdinalIgnoreCase)).ToList();
var globalDir = Path.Combine(rootDir, "global");
Console.WriteLine($"  {globalScripts.Count} Skripte im Bereich global");
if (globalScripts.Count > 0)
{
    CheckEq("Globale Skripte unter global/",
            Directory.Exists(globalDir) ? Directory.GetFiles(globalDir, "*.js", SearchOption.AllDirectories).Length : 0,
            globalScripts.Count);
}
else
{
    Check("Kein leerer global-Ordner ohne globale Skripte", !Directory.Exists(globalDir));
}

// Das exportierte XML muss wohlgeformt sein, sonst schlaegt der Import im Admin fehl.
var badXml = xmlFiles.Where(f => !XmlWellFormed(File.ReadAllText(f))).ToList();
Check("Alle exportierten XML-Dateien sind wohlgeformt", badXml.Count == 0,
      string.Join("; ", badXml.Take(3).Select(Path.GetFileName)));

// Stichprobe gegen den Admin-Referenzexport: Der Zielpfad wird aus dem Skript selbst
// gebildet, nicht aus einem im Quelltext genannten Ordner- und Dateinamen.
if (referenz is not null && File.Exists(refXml))
{
    var exportiert = Path.Combine(rootDir,
        Path.Combine(referenz.Folder.Split('/', StringSplitOptions.RemoveEmptyEntries)
                              .Select(ScriptExporter.SanitizeFileName).ToArray()),
        ScriptExporter.SanitizeFileName(referenz.Name) + ".xml");

    Check("Erwartete Ordnerstruktur im Export", File.Exists(exportiert));
    if (File.Exists(exportiert))
        Check("Exportiertes XML identisch zum Admin-Export",
              XmlEqual(File.ReadAllText(exportiert), File.ReadAllText(refXml)));
}

// Kein exportiertes JS darf noch den Base64-Blob enthalten.
var withBlob = jsFiles.Count(f =>
{
    var t = File.ReadAllText(f).TrimEnd();
    return t.EndsWith("=") && t.Contains("//PHhtb");
});
CheckEq("JS-Dateien ohne Blockly-Blob", withBlob, 0);

// Zweiter Durchgang mit dem erzeugten JavaScript: bei Blockly kommt eine Datei dazu.
Directory.Delete(exportDir, true);
var expBoth = ScriptExporter.Export(fullData.Scripts, exportDir, ScriptExportFormat.WithGeneratedJs);
Console.WriteLine($"  {expBoth.Scripts} Skripte -> {expBoth.Files} Dateien (mit erzeugtem JS)");
CheckEq("Erzeugte Dateien mit JS-Beigabe", expBoth.Files, 159 * 2 + 34);
CheckEq("JS-Dateien mit Beigabe",
        Directory.GetFiles(exportDir, "*.js", SearchOption.AllDirectories).Length, 193);

Directory.Delete(exportDir, true);

// Export folgt der Filterung: Was in der Liste steht, wird geschrieben — nicht mehr.
var gefiltert = ScriptsPresenter.Filter(fullData.Scripts, hideDisabled: false, typeIndex: 1,
                                        ScriptSearchMode.NameAndPath, "Alarm");
Console.WriteLine($"  Filter „Blockly + Alarm\": {gefiltert.Count} von {fullData.Scripts.Count} Skripten");
Check("Filter grenzt wirklich ein", gefiltert.Count is > 0 and < 193);

var expFilter = ScriptExporter.Export(gefiltert, exportDir);
CheckEq("Export schreibt nur die gefilterten Skripte", expFilter.Scripts, gefiltert.Count);
CheckEq("Dateien auf der Platte entsprechen der Filterung",
        Directory.GetFiles(exportDir, "*.*", SearchOption.AllDirectories).Length, gefiltert.Count);

CheckEq("Knopfbeschriftung ohne Filter",
        ScriptsPresenter.ExportAllLabel(193, 193), "Alle exportieren");
Check("Knopfbeschriftung mit Filter nennt die Zahl",
      ScriptsPresenter.ExportAllLabel(gefiltert.Count, 193).Contains(gefiltert.Count.ToString()),
      ScriptsPresenter.ExportAllLabel(gefiltert.Count, 193));
CheckEq("Dateien-Tab beschriftet gleich",
        FilesPresenter.ExportAllLabel(5, 81), ScriptsPresenter.ExportAllLabel(5, 81));

Directory.Delete(exportDir, true);

// ---------------------------------------------------------------- Dateien (Admin-Dateibereich)

Console.WriteLine();
Console.WriteLine("=== Dateien aus dem files/-Baum ===");
Console.WriteLine($"  {fullData.Files.Count} Dateien, zusammen " +
                  BackupFileInfo.FormatSize(fullData.Files.Sum(f => f.Size)));


Check("Dateien werden erfasst", fullData.Files.Count > 0);
// Gegengezaehlt mit "tar -tzf ... | grep ^backup/files/ | grep -v /$".
CheckRef("Dateien im Voll-Backup", fullData.Files.Count, "dateien");
Check("Kein Eintrag ohne Namensraum", fullData.Files.All(f => f.Namespace.Length > 0));
Check("Kein Eintrag mit leerem Pfad", fullData.Files.All(f => f.Path.Length > 0));
Check("Keine Verzeichnisse in der Liste", fullData.Files.All(f => !f.Path.EndsWith('/')));

// Adapter-eigene Ablagen (energiefluss-erweitert/userFiles) liegen neben files/ und
// gehoeren nicht in den Admin-Dateibereich.
Check("Nur der Backitup-Dateibaum",
      fullData.Files.All(f => f.ArchivePath.StartsWith("backup/files/", StringComparison.OrdinalIgnoreCase)),
      fullData.Files.FirstOrDefault(f => !f.ArchivePath.StartsWith("backup/files/"))?.ArchivePath);

foreach (var g in fullData.Files.GroupBy(f => f.Namespace).OrderByDescending(g => g.Count()).Take(5))
    Console.WriteLine($"    {g.Key,-28} {g.Count(),4} Dateien   {BackupFileInfo.FormatSize(g.Sum(f => f.Size))}");

Check("vis-2.0 ist unter den Namensraeumen",
      fullData.Files.Any(f => f.Namespace.Equals("vis-2.0", StringComparison.OrdinalIgnoreCase)));
Check("Die vis-views.json steht auch in der Dateiliste",
      fullData.Files.Any(f => f.Name.Equals("vis-views.json", StringComparison.OrdinalIgnoreCase)));
Check("Bilder werden als Bild eingeordnet",
      fullData.Files.Any(f => f.Kind == BackupFileKind.Bild));
Check("Namensraum-Auswahl beginnt mit „Alle“",
      FilesPresenter.NamespaceChoices(fullData.Files)[0] == FilesPresenter.AllNamespaces);

// Export: Inhalte werden erst hier aus dem Archiv nachgelesen.
var fileDir = Path.Combine(Path.GetTempPath(), "iob_files_test");
if (Directory.Exists(fileDir)) Directory.Delete(fileDir, true);

var expFiles = BackupFileExporter.Export(fullData, fullData.Files, fileDir);
Console.WriteLine($"  Export: {expFiles.Files} Dateien, {BackupFileInfo.FormatSize(expFiles.Bytes)}, " +
                  $"{expFiles.Renamed} umbenannt");

Check("Datei-Export ohne Fehler", expFiles.Errors.Count == 0, string.Join("; ", expFiles.Errors.Take(3)));
Check("Datei-Export findet alles im Archiv", expFiles.Missing.Count == 0,
      string.Join("; ", expFiles.Missing.Take(3)));
CheckEq("Exportierte Dateizahl", expFiles.Files, fullData.Files.Count);

// Auch der Datei-Export liegt unter dem Backup-Namen — Skripte und Dateien desselben
// Backups landen so nebeneinander, ohne sich zu vermischen.
var filesRoot = Path.Combine(fileDir, backupFolder, BackupFileExporter.RootFolderName);
Check("Ueberordner ioBroker-Dateien angelegt", Directory.Exists(filesRoot), filesRoot);
CheckEq("Datei-Export meldet den Ueberordner", expFiles.RootDir, filesRoot);
CheckEq("Zielordner enthaelt nur den Backup-Ordner",
        Directory.GetFileSystemEntries(fileDir).Length, 1);
CheckEq("Geschriebene Dateien auf der Platte",
        Directory.GetFiles(filesRoot, "*", SearchOption.AllDirectories).Length, fullData.Files.Count);

// Ein Doppelpunkt im Dateinamen (etwa eine Uhrzeit wie 07:27:54) ist unter Windows
// verboten; der Export muss ihn ersetzen und die Aenderung ausweisen.
var mitDoppelpunkt = fullData.Files.Count(f => f.Name.Contains(':'));
Console.WriteLine($"  Dateinamen mit Doppelpunkt: {mitDoppelpunkt}");
Check("Umbenennungen werden gemeldet", expFiles.Renamed >= mitDoppelpunkt,
      $"{expFiles.Renamed} gemeldet, {mitDoppelpunkt} erwartet");

// Stichprobe: Groesse auf der Platte muss der Angabe aus dem Archiv entsprechen.
var probe = fullData.Files.OrderByDescending(f => f.Size).First();
var probePfad = Path.Combine(filesRoot,
    Path.Combine(probe.DisplayPath.Split('/').Select(ScriptExporter.SanitizeFileName).ToArray()));
Check("Groesste Datei liegt am erwarteten Platz", File.Exists(probePfad), probePfad);
if (File.Exists(probePfad))
    CheckEq($"Groesse stimmt ({probe.Name})", new FileInfo(probePfad).Length, probe.Size);

// Teilexport: nur ein Namensraum.
var nurVis = fullData.Files.Where(f => f.Namespace.Equals("vis-2.0", StringComparison.OrdinalIgnoreCase)).ToList();
var teilDir = Path.Combine(Path.GetTempPath(), "iob_files_teil");
if (Directory.Exists(teilDir)) Directory.Delete(teilDir, true);
var expTeil = BackupFileExporter.Export(fullData, nurVis, teilDir);
CheckEq("Teilexport schreibt nur die Auswahl", expTeil.Files, nurVis.Count);
Check("Teilexport ohne fehlende Dateien", expTeil.Missing.Count == 0);

Directory.Delete(fileDir, true);
Directory.Delete(teilDir, true);


// ---------------------------------------------------- VIS-Projekt als ZIP (Projektimport)

Console.WriteLine();
Console.WriteLine("=== VIS-Projekt als ZIP fuer den Projektimport ===");

var visProjekte = VisProjectExporter.FindProjects(fullData);
Console.WriteLine("  Projekte: " + string.Join(", ",
    visProjekte.Select(p => $"{p.VersionText}/{p.Name} ({p.Files.Count})")));

Check("VIS-Projekte im Dateibaum gefunden", visProjekte.Count > 0);
Check("Beide VIS-Versionen unter den Projekten",
      visProjekte.Any(p => p.Version == VisVersion.Vis1)
      && visProjekte.Any(p => p.Version == VisVersion.Vis2));
Check("Jedes angebotene Projekt hat eine vis-views.json",
      visProjekte.All(p => p.Views is not null),
      string.Join(", ", visProjekte.Where(p => p.Views is null).Select(p => p.Name)));

// Nicht jeder Ordner unter vis.0 ist ein Projekt: Ordner ohne vis-views.json sind
// Bilderablagen und duerfen nicht zur Auswahl stehen - eine ZIP daraus nimmt der
// Projektimport nicht an.
var visOrdner = fullData.Files
    .Where(f => f.Namespace.Equals("vis.0", StringComparison.OrdinalIgnoreCase)
             || f.Namespace.Equals("vis-2.0", StringComparison.OrdinalIgnoreCase))
    .Where(f => f.Path.Contains('/'))
    .Select(f => (f.Namespace, Ordner: f.Path[..f.Path.IndexOf('/')]))
    .Distinct()
    .ToList();

var ordnerMitViews = fullData.Files
    .Where(f => f.Name.Equals(VisProjectExporter.ViewsFileName, StringComparison.OrdinalIgnoreCase))
    .Where(f => f.Path.Contains('/'))
    .Select(f => (f.Namespace, Ordner: f.Path[..f.Path.IndexOf('/')]))
    .Distinct()
    .ToList();

Console.WriteLine($"  Ordner unter vis.0/vis-2.0: {visOrdner.Count}, davon mit vis-views.json: " +
                  $"{ordnerMitViews.Count}");
CheckEq("Angeboten werden genau die Ordner mit vis-views.json",
        visProjekte.Count, ordnerMitViews.Count);
Check("Ordner ohne vis-views.json bleiben aussen vor",
      visProjekte.All(p => ordnerMitViews.Contains((p.Namespace, p.Name))));

// Und was uebrig bleibt, benennt SiblingFolders - damit im Hinweistext steht, was der
// ZIP fehlt, wenn sie auf eine fremde Anlage wandert.
if (visProjekte.FirstOrDefault() is { } erstesProjekt)
{
    var nachbarn = VisProjectExporter.SiblingFolders(fullData, erstesProjekt);
    var erwartet = visOrdner
        .Where(o => o.Namespace.Equals(erstesProjekt.Namespace, StringComparison.OrdinalIgnoreCase))
        .Select(o => o.Ordner)
        .Where(o => !ordnerMitViews.Contains((erstesProjekt.Namespace, o)))
        .Distinct()
        .ToList();
    CheckEq($"Nachbarordner in {erstesProjekt.Namespace} benannt", nachbarn.Count, erwartet.Count);
}

// Dateien direkt im Namensraum (vis-2.0/vis-common-user.css) gehoeren keinem Projekt.
var loseVisDateien = fullData.Files.Count(f =>
    f.Namespace.StartsWith("vis", StringComparison.OrdinalIgnoreCase) && !f.Path.Contains('/'));
Check("Namensraumweite Dateien landen in keiner Projekt-ZIP",
      visProjekte.All(p => p.Files.All(f => f.Path.Contains('/'))),
      $"{loseVisDateien} namensraumweite Datei(en) im Backup");

var zipDir = Path.Combine(Path.GetTempPath(), "iob_vis_zip");
if (Directory.Exists(zipDir)) Directory.Delete(zipDir, true);
Directory.CreateDirectory(zipDir);

if (visProjekte.FirstOrDefault(p => p.Version == VisVersion.Vis1 && p.Views is not null) is { } v1Projekt)
{
    var zipVoll = Path.Combine(zipDir, v1Projekt.SuggestedFileName(fullData.CreatedAt));
    var ergebnis = VisProjectExporter.Export(fullData, v1Projekt, zipVoll, includeAssets: true);

    Console.WriteLine($"  {Path.GetFileName(zipVoll)}: {ergebnis.Files} Datei(en), " +
                      $"{BackupFileInfo.FormatSize(ergebnis.Bytes)} roh -> " +
                      $"{BackupFileInfo.FormatSize(ergebnis.ZipBytes)} gepackt");

    Check("ZIP-Export ohne Fehler", ergebnis.Errors.Count == 0, string.Join("; ", ergebnis.Errors.Take(3)));
    Check("ZIP-Export findet alles im Archiv", ergebnis.Missing.Count == 0,
          string.Join("; ", ergebnis.Missing.Take(3)));
    CheckEq("Geschriebene Dateien", ergebnis.Files, v1Projekt.Files.Count);
    Check("vis-views.json ist enthalten", ergebnis.ViewsIncluded);
    Check("ZIP-Datei liegt am gemeldeten Platz", File.Exists(ergebnis.ZipPath), ergebnis.ZipPath);
    Check("Keine Bruchstueckdatei zurueckgelassen", !File.Exists(zipVoll + ".teil"));
    Check("Dateiname endet auf .zip", zipVoll.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));

    // Der Dateiname ist in VIS zugleich der vorbelegte Projektname: Beim Hineinziehen in
    // den Import-Dialog traegt VIS ihn selbst ein - ohne fuehrendes Datum. Wer nicht
    // darauf achtet, importiert unter diesem Namen. Zwei Eigenschaften muss er deshalb
    // haben, sonst trifft er im Ernstfall das laufende Projekt.
    var vorschlag = Path.GetFileNameWithoutExtension(v1Projekt.SuggestedFileName(fullData.CreatedAt));
    Console.WriteLine($"  Vorgeschlagener Name -> Projekt in VIS: {vorschlag}");

    Check("Vorschlag ist nicht der blosse Projektname",
          !vorschlag.Equals(v1Projekt.Name, StringComparison.OrdinalIgnoreCase), vorschlag);
    Check("Vorschlag nennt die VIS-Version", vorschlag.Contains("vis1", StringComparison.Ordinal));
    // Vorn wuerde VIS das Datum abschneiden - dann hiessen zwei Importe aus verschiedenen
    // Backups gleich, und der zweite ueberschriebe den ersten.
    Check("Datum steht hinten, nicht vorn",
          !System.Text.RegularExpressions.Regex.IsMatch(vorschlag, @"^\d{4}-\d{2}-\d{2}-")
          && System.Text.RegularExpressions.Regex.IsMatch(vorschlag, @"\d{4}-\d{2}-\d{2}$"),
          vorschlag);

    var ausAnderemBackup = v1Projekt.SuggestedFileName(fullData.CreatedAt?.AddDays(-7));
    Check("Zwei Backup-Staende ergeben zwei Projektnamen",
          !ausAnderemBackup.Equals(v1Projekt.SuggestedFileName(fullData.CreatedAt),
                                   StringComparison.OrdinalIgnoreCase));

    var eintraege = ZipEintraege(ergebnis.ZipPath);
    var dateien = eintraege.Where(e => !e.EndsWith("/", StringComparison.Ordinal)).ToList();

    // Der Kern des Formats: Der Projektordner selbst steht NICHT in der ZIP. VIS erwartet
    // seinen Inhalt flach in der Wurzel und vergibt den Projektnamen beim Import neu.
    Check("vis-views.json liegt in der Wurzel", dateien.Contains(VisProjectExporter.ViewsFileName),
          string.Join(", ", dateien.Take(5)));
    Check("Kein Eintrag traegt den Projektnamen als Ordner",
          !eintraege.Any(e => e.StartsWith(v1Projekt.Name + "/", StringComparison.OrdinalIgnoreCase)));
    CheckEq("Dateieintraege in der ZIP", dateien.Count, v1Projekt.Files.Count);

    // Pfadhygiene: ZIP-Eintraege muessen relativ und mit / getrennt sein, sonst legt der
    // Import sie an falscher Stelle ab (oder verweigert sie).
    Check("Keine Rueckwaerts-Schraegstriche in den Eintraegen", !eintraege.Any(e => e.Contains('\\')));
    Check("Keine absoluten Eintraege",
          !eintraege.Any(e => e.StartsWith("/", StringComparison.Ordinal)));
    Check("Kein Eintrag zeigt aus der ZIP heraus",
          !eintraege.Any(e => e.Split('/').Contains("..")));

    // Ohne Beiwerk bleibt genau eine Datei uebrig - der Fall "nur die Views zurueckholen".
    var zipNurViews = Path.Combine(zipDir, "nur-views.zip");
    var nurViews = VisProjectExporter.Export(fullData, v1Projekt, zipNurViews, includeAssets: false);
    var nurEintraege = ZipEintraege(zipNurViews)
        .Where(e => !e.EndsWith("/", StringComparison.Ordinal)).ToList();
    CheckEq("Ohne Beiwerk genau eine Datei", nurEintraege.Count, 1);
    Check("Und das ist die vis-views.json",
          nurEintraege.SingleOrDefault() == VisProjectExporter.ViewsFileName);
    Check("Ohne Beiwerk ist die ZIP kleiner", nurViews.ZipBytes < ergebnis.ZipBytes,
          $"{nurViews.ZipBytes} vs {ergebnis.ZipBytes}");

    // --- Befund 3 aus dem Code-Review: Quelle und Ziel duerfen nie dieselbe Datei sein ---
    var quelleVorher = new FileInfo(fullData.SourceFile).Length;
    var aufSichSelbst = VisProjectExporter.Export(fullData, v1Projekt, fullData.SourceFile,
                                                  includeAssets: true);
    CheckEq("Export auf die Backup-Datei schreibt nichts", aufSichSelbst.Files, 0);
    Check("Und sagt auch, warum",
          aufSichSelbst.Errors.Any(e => e.Contains("dieselbe Datei", StringComparison.Ordinal)));
    CheckEq("Das Backup selbst ist unveraendert",
            new FileInfo(fullData.SourceFile).Length, quelleVorher);
    Check("Kein Bruchstueck neben dem Backup", !File.Exists(fullData.SourceFile + ".teil"));

    // Auch mit anderer Schreibweise desselben Pfades - unter Windows ist der Vergleich
    // unabhaengig von Gross- und Kleinschreibung.
    if (OperatingSystem.IsWindows())
    {
        var andersGeschrieben = VisProjectExporter.Export(fullData, v1Projekt,
            fullData.SourceFile.ToUpperInvariant(), includeAssets: false);
        CheckEq("GROSS geschriebener Quellpfad wird ebenfalls abgewehrt", andersGeschrieben.Files, 0);
    }

    // --- Abbruch mitten im Lauf: kein halbes Ergebnis am Zielort ---
    var zipAbbruch = Path.Combine(zipDir, "abgebrochen.zip");
    using (var cts = new CancellationTokenSource())
    {
        cts.Cancel();
        try
        {
            VisProjectExporter.Export(fullData, v1Projekt, zipAbbruch, true, cts.Token);
            Check("Abbruch wird gemeldet", false, "keine OperationCanceledException");
        }
        catch (OperationCanceledException)
        {
            Check("Abbruch wird gemeldet", true);
        }
    }
    Check("Nach Abbruch keine Zieldatei", !File.Exists(zipAbbruch));
    Check("Nach Abbruch kein Bruchstueck", !File.Exists(zipAbbruch + ".teil"));

    // Der Zeitstempel kommt aus dem Backup, nicht von der Uhr: Die entpackte Datei soll
    // zeigen, von wann sie stammt. Das ZIP-Format kann nichts vor 1980.
    using (var zeitZip = ZipFile.OpenRead(ergebnis.ZipPath))
    {
        var eintrag = zeitZip.GetEntry(VisProjectExporter.ViewsFileName);
        var stempel = eintrag?.LastWriteTime.DateTime ?? DateTime.MinValue;
        Console.WriteLine($"  Zeitstempel der vis-views.json in der ZIP: {stempel:dd.MM.yyyy HH:mm}");
        Check("Zeitstempel stammt nicht von der Uhr des Exports",
              stempel.Year >= 1980 && stempel < DateTime.Now.AddMinutes(-1),
              stempel.ToString("dd.MM.yyyy HH:mm"));
    }

    // Eine bereits vorhandene Zieldatei wird ersetzt, nicht angehaengt.
    File.WriteAllText(zipNurViews, "kein zip");
    var ersetzt = VisProjectExporter.Export(fullData, v1Projekt, zipNurViews, includeAssets: false);
    Check("Vorhandene Zieldatei wird ersetzt", ersetzt.Files == 1 && ersetzt.ViewsIncluded);
    Check("Und ist danach ein lesbares ZIP", ZipEintraege(zipNurViews).Count > 0);
}
else
{
    Console.WriteLine("  uebersprungen: kein VIS-1-Projekt mit vis-views.json im Testbackup");
    skipped += 2;
}

// --- Abgleich mit echten Exporten aus VIS 1 und VIS 2 ---
// testdaten/vis1 und testdaten/vis2 enthalten je ein "Tools -> Projekt exportieren" aus
// einer laufenden Anlage. Sie sind der Massstab fuer das Format: Weicht unser Aufbau ab,
// nimmt der Projektimport die Datei nicht an.
foreach (var (ordner, bezeichnung) in new[] { ("vis1", "VIS 1"), ("vis2", "VIS 2") })
{
    var referenzOrdner = Path.Combine(testdaten, ordner);
    var refExport = Directory.Exists(referenzOrdner)
        ? Directory.EnumerateFiles(referenzOrdner, "*.zip").FirstOrDefault()
        : null;

    if (refExport is null)
    {
        Console.WriteLine($"  uebersprungen: kein Referenz-Export unter testdaten/{ordner}");
        skipped++;
        continue;
    }

    var refEintraege = ZipEintraege(refExport);
    Console.WriteLine($"  {bezeichnung}-Referenz ({Path.GetFileName(refExport)}): " +
                      string.Join(", ", refEintraege.Take(6)));

    Check($"{bezeichnung}: Referenz hat die vis-views.json in der Wurzel",
          refEintraege.Contains(VisProjectExporter.ViewsFileName));
    Check($"{bezeichnung}: Referenz kennt keinen Projektordner",
          !refEintraege.Any(e => e.Split('/').Length > 1
                              && e.Split('/')[0].Equals("main", StringComparison.OrdinalIgnoreCase)));
    Check($"{bezeichnung}: Referenz nutzt / als Trenner", !refEintraege.Any(e => e.Contains('\\')));
}

// --- Sonderzeichen und Unterordner: synthetisches Backup, damit der Fall sicher vorkommt ---
var umlautDir = Path.Combine(Path.GetTempPath(), "iob-vis-umlaut");
if (Directory.Exists(umlautDir)) Directory.Delete(umlautDir, true);
var umlautProjekt = Path.Combine(umlautDir, "backup", "files", "vis.0", "main");
Directory.CreateDirectory(Path.Combine(umlautProjekt, "img", "tief"));
File.WriteAllText(Path.Combine(umlautDir, "backup", "objects.jsonl"),
    "{\"_id\":\"system.adapter.vis.0\",\"type\":\"instance\"," +
    "\"common\":{\"name\":\"vis\",\"version\":\"1.5.6\",\"enabled\":true},\"native\":{}}\n");
File.WriteAllText(Path.Combine(umlautProjekt, "vis-views.json"), "{\"Uebersicht\":{\"widgets\":{}}}");
File.WriteAllText(Path.Combine(umlautProjekt, "Grundriss Küche.png"), "x");
File.WriteAllText(Path.Combine(umlautProjekt, "img", "tief", "straße.png"), "y");
File.WriteAllText(Path.Combine(umlautProjekt, "leer.css"), "");

var umlautTar = Path.Combine(umlautDir, "iobroker_2026_08_20-10_00_00_backupiobroker.tar");
// Bewusst NICHT ueber TarFile.CreateFromDirectory, und bewusst unkomprimiert:
// .NETs TarWriter schreibt in einen nicht seekbaren Stream (also in einen GZipStream
// hinein) PAX-Kopfsaetze, an denen sein eigener TarReader mittendrin scheitert ("Unable to
// parse number"), waehrend GNU tar dasselbe Archiv anstandslos liest. Das trifft nur das
// Erzeugen der Testdaten: Backitup schreibt seine Archive mit node, und der Loader liest
// eine unkomprimierte .tar genauso wie eine .tar.gz.
CreatePaxTar(umlautTar, new[]
{
    (Path.Combine(umlautDir, "backup", "objects.jsonl"), "backup/objects.jsonl"),
    (Path.Combine(umlautProjekt, "vis-views.json"), "backup/files/vis.0/main/vis-views.json"),
    (Path.Combine(umlautProjekt, "Grundriss Küche.png"),
        "backup/files/vis.0/main/Grundriss Küche.png"),
    (Path.Combine(umlautProjekt, "img", "tief", "straße.png"),
        "backup/files/vis.0/main/img/tief/straße.png"),
    (Path.Combine(umlautProjekt, "leer.css"), "backup/files/vis.0/main/leer.css")
});

var umlautData = BackupLoader.Load(umlautTar);
var umlautProjekte = VisProjectExporter.FindProjects(umlautData);
Console.WriteLine($"  Synthetisches Backup: {umlautData.Files.Count} Datei(en), " +
                  $"Projektdateien={umlautProjekte.FirstOrDefault()?.Files.Count ?? 0}");
CheckEq("Synthetisches Backup: ein Projekt", umlautProjekte.Count, 1);

if (umlautProjekte.FirstOrDefault() is { } umlautP)
{
    var zipUmlaut = Path.Combine(umlautDir, "umlaut.zip");
    var umlautErgebnis = VisProjectExporter.Export(umlautData, umlautP, zipUmlaut, includeAssets: true);
    CheckEq("Alle vier Dateien gepackt", umlautErgebnis.Files, 4);

    var umlautEintraege = ZipEintraege(zipUmlaut);
    Check("Umlaut im Dateinamen bleibt erhalten",
          umlautEintraege.Contains("Grundriss Küche.png"),
          string.Join(", ", umlautEintraege));
    Check("Unterordner bleibt als Pfad erhalten",
          umlautEintraege.Contains("img/tief/straße.png"));
    Check("Ordnereintraege werden mitgeschrieben, wie VIS es tut",
          umlautEintraege.Contains("img/") && umlautEintraege.Contains("img/tief/"));

    // Dateinamen muessen als UTF-8 in der ZIP stehen, nicht als CP437 - sonst liest die
    // JavaScript-Seite in VIS Kraut und Rueben.
    var rohBytes = File.ReadAllBytes(zipUmlaut);
    Check("Dateinamen stehen als UTF-8 in der ZIP",
          IndexOfBytes(rohBytes, Encoding.UTF8.GetBytes("Küche")) >= 0);

    // Eine 0-Byte-Datei hat im Tar keinen Datenstrom - sie muss trotzdem als leerer
    // Eintrag ankommen (die vis-user.css im echten Export ist genau das).
    Check("Leere Datei kommt als leerer Eintrag an", umlautEintraege.Contains("leer.css"));
    using (var umlautZip = ZipFile.OpenRead(zipUmlaut))
        CheckEq("Und ist auch wirklich leer", umlautZip.GetEntry("leer.css")?.Length ?? -1L, 0L);
}

Directory.Delete(zipDir, true);
Directory.Delete(umlautDir, true);

// Schreibt ein unkomprimiertes .tar aus einer festen Liste (Quelldatei, Name im Archiv).
static void CreatePaxTar(string zielArchiv, IEnumerable<(string Pfad, string Name)> dateien)
{
    using var fs = File.Create(zielArchiv);
    using var writer = new TarWriter(fs, TarEntryFormat.Pax);

    foreach (var (pfad, name) in dateien)
        writer.WriteEntry(pfad, name);
}

// Liest die Eintragsnamen einer ZIP - Dateien wie Ordner, in der gespeicherten Reihenfolge.
static List<string> ZipEintraege(string pfad)
{
    using var zip = ZipFile.OpenRead(pfad);
    return zip.Entries.Select(e => e.FullName).ToList();
}

static int IndexOfBytes(byte[] haystack, byte[] needle)
{
    for (var i = 0; i + needle.Length <= haystack.Length; i++)
    {
        var treffer = true;
        for (var j = 0; j < needle.Length; j++)
            if (haystack[i + j] != needle[j]) { treffer = false; break; }
        if (treffer) return i;
    }
    return -1;
}


// ---------------------------------------------------------------- Robustheit

Console.WriteLine();
Console.WriteLine("=== Verwendung: Skripte <-> Datenpunkte ===");

// Zuerst gegen erfundene, aber typische Zeilen: Ob die Richtung stimmt, muss nachweisbar
// sein und nicht davon abhaengen, was im Testbackup zufaellig steht.
var usageProbe = new BackupData
{
    SourceFile = "test",
    Kind = BackupKind.Full,
    Objects = new List<IobObject>
    {
        new() { Id = "0_userdata.0.Heizung.Soll", Type = "state", Name = "Solltemperatur" },
        new() { Id = "0_userdata.0.Heizung.Ist", Type = "state", Name = "Isttemperatur" },
        new() { Id = "0_userdata.0.Heizung.Modus", Type = "state", Name = "Modus" },
        new() { Id = "0_userdata.0.Nie.Benutzt", Type = "state", Name = "Waise" },
        new() { Id = "alias.0.Bad.Licht", Type = "state", Name = "Licht Bad",
                AliasTarget = "hue.0.lampe.on" },
        new() { Id = "0_userdata.0.Dyn.Raum1", Type = "state", Name = "Raum 1" },
        new() { Id = "0_userdata.0.Dyn.Raum2", Type = "state", Name = "Raum 2" }
    },
    Scripts = new List<ScriptInfo>
    {
        new()
        {
            Id = "script.js.Heizung.Regler", Name = "Regler", Folder = "Heizung",
            Engine = ScriptEngine.JavaScript, Enabled = true,
            CleanSource = """
                          on({id: '0_userdata.0.Heizung.Ist', change: 'any'}, function (obj) {
                              setState('0_userdata.0.Heizung.Soll', getState('0_userdata.0.Heizung.Ist').val + 1);
                              const raum = 'Raum1';
                              setState(`0_userdata.0.Dyn.${raum}`, true);
                          });
                          """
        },
        new()
        {
            Id = "script.js.Heizung.Zweitschreiber", Name = "Zweitschreiber", Folder = "Heizung",
            Engine = ScriptEngine.JavaScript, Enabled = true,
            CleanSource = "setState('0_userdata.0.Heizung.Soll', 21); // schreibt denselben Wert"
        },
        new()
        {
            Id = "script.js.Ohne", Name = "Ohne", Engine = ScriptEngine.JavaScript, Enabled = false,
            CleanSource = "console.log('hier steht kein Datenpunkt');"
        }
    }
};

var usageProbeReport = UsageAnalyzer.Analyze(usageProbe);

var regler = usageProbeReport.Scripts.First(s => s.ScriptId == "script.js.Heizung.Regler");
// Vier: Soll, Ist und beide Kandidaten unter Dyn - `${raum}` laesst offen, welcher gemeint
// ist, also stehen beide da. Genau dafuer gibt es die Kennzeichnung "zusammengesetzt".
CheckEq("Regler-Skript findet seine Datenpunkte", regler.StateCount, 4);
CheckEq("Beide Kandidaten der zusammengesetzten ID stehen da",
        regler.Links.Count(l => l.Dynamic), 2);
CheckEq("Geschriebener Datenpunkt als schreibend erkannt",
        regler.Links.First(l => l.StateId == "0_userdata.0.Heizung.Soll").Access,
        UsageAccess.Schreibt);
// Derselbe Datenpunkt steht im Trigger und im getState - beides lesend.
CheckEq("Trigger und getState gelten als lesend",
        regler.Links.First(l => l.StateId == "0_userdata.0.Heizung.Ist").Access,
        UsageAccess.Liest);
// `0_userdata.0.Dyn.${raum}` nennt den Datenpunkt nicht beim Namen; erkennbar ist nur der
// feste Anfang - und genau so wird der Fund auch ausgewiesen.
var dyn = regler.Links.FirstOrDefault(l => l.StateId.StartsWith("0_userdata.0.Dyn.", StringComparison.Ordinal));
Check("Zusammengesetzte ID wird ueber ihren Anfang gefunden", dyn is not null);
Check("Zusammengesetzter Fund ist als solcher gekennzeichnet", dyn?.Dynamic == true);

// Der eigentliche Zweck der Analyse: zwei Skripte schreiben denselben Datenpunkt.
var soll = usageProbeReport.States.First(s => s.Id == "0_userdata.0.Heizung.Soll");
CheckEq("Datenpunkt kennt beide Skripte", soll.ScriptCount, 2);
CheckEq("Beide Skripte zaehlen als Schreiber", soll.Writers, 2);
Check("Mehrfach beschriebener Datenpunkt wird gemeldet", soll.MultipleWriters);
CheckEq("Mehrfachschreiber im Bericht gezaehlt", usageProbeReport.StatesMultiWriter, 1);

// Und die Gegenprobe: Was nirgends vorkommt, faellt auf - auch der ungenutzte Alias.
var nieBenutzt = usageProbeReport.States.First(s => s.Id == "0_userdata.0.Nie.Benutzt");
Check("Unbenutzter Datenpunkt bleibt ohne Skript", nieBenutzt.Unused);
var aliasUnused = usageProbeReport.States.First(s => s.Id == "alias.0.Bad.Licht");
Check("Alias wird als Alias gefuehrt", aliasUnused.IsAlias);
Check("Nie verwendeter Alias faellt auf", aliasUnused.Unused);
CheckEq("Ungenutzte Aliasse gezaehlt", usageProbeReport.AliasesUnused, 1);

// Der Modus-Datenpunkt kommt in keinem Skript vor: kein Treffer nur wegen aehnlichem Namen.
Check("Kein Treffer durch blosse Namensaehnlichkeit",
      usageProbeReport.States.First(s => s.Id == "0_userdata.0.Heizung.Modus").Unused);

// Ein Skript ohne Datenpunkte bleibt in der Liste - "benutzt nichts" ist auch eine Antwort.
CheckEq("Skript ohne Datenpunkte wird mitgefuehrt",
        usageProbeReport.Scripts.Count(s => s.StateCount == 0), 1);
CheckEq("Skripte mit Datenpunktbezug gezaehlt", usageProbeReport.ScriptsWithStates, 2);

// Ein Apostroph im Kommentar darf nicht den Rest der Datei als Zeichenkette verschlucken.
var apostroph = UsageAnalyzer.Analyze(new BackupData
{
    SourceFile = "test", Kind = BackupKind.Full,
    Objects = new List<IobObject> { new() { Id = "0_userdata.0.Test.Wert", Type = "state" } },
    Scripts = new List<ScriptInfo>
    {
        new() { Id = "script.js.A", Name = "A", Engine = ScriptEngine.JavaScript,
                CleanSource = "// don't panic\nsetState(\"0_userdata.0.Test.Wert\", 1);" }
    }
});
CheckEq("Apostroph im Kommentar bricht die Suche nicht",
        apostroph.Scripts[0].StateCount, 1);

// Blockly: Der Zugriff steht im erzeugten JavaScript, nicht im XML - und was nur im XML
// steht, gehoert zu einem Block, der nicht mitlaeuft.
var blockly = UsageAnalyzer.Analyze(new BackupData
{
    SourceFile = "test", Kind = BackupKind.Full,
    Objects = new List<IobObject>
    {
        new() { Id = "0_userdata.0.Blockly.Aktiv", Type = "state" },
        new() { Id = "0_userdata.0.Blockly.Inaktiv", Type = "state" }
    },
    Scripts = new List<ScriptInfo>
    {
        new()
        {
            Id = "script.js.B", Name = "B", Engine = ScriptEngine.Blockly,
            CleanSource = "setState(\"0_userdata.0.Blockly.Aktiv\", true);",
            BlocklyXml = "<xml><block type=\"control\"><field name=\"OID\">0_userdata.0.Blockly.Aktiv</field></block>"
                       + "<block type=\"control\" disabled=\"true\"><field name=\"OID\">0_userdata.0.Blockly.Inaktiv</field></block></xml>"
        }
    }
});
var blocklyLinks = blockly.Scripts[0].Links;
CheckEq("Blockly: beide Datenpunkte gefunden", blocklyLinks.Count, 2);
Check("Blockly: der laufende Block zaehlt als Code-Fund",
      blocklyLinks.First(l => l.StateId.EndsWith("Aktiv", StringComparison.Ordinal)).OnlyInXml == false);
Check("Blockly: der nicht erzeugte Block wird als XML-only ausgewiesen",
      blocklyLinks.First(l => l.StateId.EndsWith("Inaktiv", StringComparison.Ordinal)).OnlyInXml);

// --- Aktiv-Status: aktiv ist nur, was ausdruecklich enabled: true traegt ---
// Ein Skript ohne common.enabled stand in der Verwender-Tabelle auf „Aktiv", lief in
// ioBroker aber nicht. Beim Kopieren oder Importieren eines Skripts entsteht genau das:
// ioBroker prueft das Feld in JavaScript auf Wahrheitswert, ein fehlendes Feld ist dort
// undefined und damit aus. Gegenprobe im echten Backup ist der Laufzeit-Datenpunkt
// javascript.0.scriptEnabled.<Pfad> — er steht bei genau diesem Skript auf false.
var statusDir = Path.Combine(Path.GetTempPath(), "iob-verify-status");
if (Directory.Exists(statusDir)) Directory.Delete(statusDir, true);
Directory.CreateDirectory(statusDir);

var statusFile = Path.Combine(statusDir, "objects.jsonl");
File.WriteAllLines(statusFile, new[]
{
    "{\"_id\":\"0_userdata.0.Fenster.Terrasse\",\"type\":\"state\",\"common\":{\"name\":\"Terrasse\"}}",
    "{\"_id\":\"script.js.Fenster.Terrasse\",\"type\":\"script\",\"common\":{\"name\":\"Terrasse\","
        + "\"engineType\":\"Javascript/js\",\"enabled\":true,"
        + "\"source\":\"setState('0_userdata.0.Fenster.Terrasse', 1);\"}}",
    // Der Fall aus der Meldung: kein enabled-Feld.
    "{\"_id\":\"script.js.Obsolet.Terrasse\",\"type\":\"script\",\"common\":{\"name\":\"Terrasse\","
        + "\"engineType\":\"Javascript/js\","
        + "\"source\":\"setState('0_userdata.0.Fenster.Terrasse', 1);\"}}",
    "{\"_id\":\"script.js.Alt.Terrasse\",\"type\":\"script\",\"common\":{\"name\":\"Terrasse\","
        + "\"engineType\":\"Javascript/js\",\"enabled\":false,"
        + "\"source\":\"setState('0_userdata.0.Fenster.Terrasse', 1);\"}}"
});

var statusData = BackupLoader.Load(statusFile);
CheckEq("Statusprobe: alle drei Skripte geladen", statusData.Scripts.Count, 3);

string StatusOf(string id) =>
    statusData.Scripts.First(s => s.Id == id).StatusText;

CheckEq("enabled: true gilt als aktiv", StatusOf("script.js.Fenster.Terrasse"), "Aktiv");
CheckEq("enabled: false gilt als deaktiviert", StatusOf("script.js.Alt.Terrasse"), "Deaktiviert");
CheckEq("Fehlendes enabled gilt als deaktiviert", StatusOf("script.js.Obsolet.Terrasse"), "Deaktiviert");

// Derselbe Status muss auch in der Verwender-Tabelle unter dem Datenpunkt stehen — dort
// ist er aufgefallen.
var statusUsage = UsageAnalyzer.Analyze(statusData);
var statusLinks = statusUsage.States.First(s => s.Id == "0_userdata.0.Fenster.Terrasse").Links;
CheckEq("Statusprobe: alle drei Skripte nennen den Datenpunkt", statusLinks.Count, 3);
CheckEq("Verwender-Tabelle zeigt das Skript ohne enabled als deaktiviert",
        statusLinks.First(l => l.SourceId == "script.js.Obsolet.Terrasse").StatusText, "Deaktiviert");
CheckEq("Verwender-Tabelle zeigt das laufende Skript weiterhin als aktiv",
        statusLinks.First(l => l.SourceId == "script.js.Fenster.Terrasse").StatusText, "Aktiv");

// --- und nun gegen das echte Voll-Backup ---
var usageSw = Stopwatch.StartNew();
var usage = UsageAnalyzer.Analyze(fullData);
usageSw.Stop();
Console.WriteLine($"  Analysedauer: {usageSw.ElapsedMilliseconds} ms");
Console.WriteLine($"  {usage.ScriptsWithStates} von {usage.ScriptsTotal} Skripten mit Datenpunktbezug, " +
                  $"{usage.Links} Verbindungen zu {usage.StatesUsed} Datenpunkten");
Console.WriteLine($"  Von mehreren Skripten beschrieben: {usage.StatesMultiWriter} · " +
                  $"nie verwendete Aliasse: {usage.AliasesUnused}");

CheckEq("Alle Skripte erscheinen in der Liste", usage.Scripts.Count, fullData.Scripts.Count);
Check("Skripte mit Datenpunktbezug gefunden", usage.ScriptsWithStates > 0);
Check("Verbindungen gefunden", usage.Links > 0);
// Ohne Zeitgrenze taugt die Analyse nicht fuer einen Tab, der beim Laden mitrechnet.
Check("Analyse bleibt unter 5 Sekunden", usageSw.ElapsedMilliseconds < 5000,
      $"{usageSw.ElapsedMilliseconds} ms");

// Beide Richtungen muessen dieselbe Menge beschreiben, sonst zeigt eine Sicht etwas anderes
// als die andere.
// Die Skript-Verbindungen muessen in beiden Richtungen dieselbe Menge beschreiben. Die
// Adapter-Verbindungen kommen nur in der Datenpunkt-Sicht vor - ein Adapter ist kein Skript.
CheckEq("Skript-Verbindungen stimmen in beiden Richtungen ueberein",
        usage.States.Sum(s => s.Links.Count(l => l.Source == UsageSource.Skript)), usage.Links);
CheckEq("Adapter-Verbindungen sind vollstaendig zugeordnet",
        usage.States.Sum(s => s.Links.Count(l => l.Source == UsageSource.Adapter)),
        usage.AdapterLinks);

// --- Datenpunkte aus Adapter-Konfigurationen ---
// Viele Adapter tragen ihre Datenpunkte selbst ein (Shuttercontrol seine Rolllaeden,
// awtrix-light die Werte seiner Apps). Ohne diese Quelle stuenden sie faelschlich in der
// Liste der nie verwendeten.
Console.WriteLine($"  Adapter-Konfiguration vorhanden: {usage.HasAdapterConfig} · " +
                  $"{usage.AdaptersWithStates} Instanzen mit Datenpunkten · " +
                  $"{usage.AdapterLinks} Verweise · {usage.StatesOnlyInAdapter} nur im Adapter");
Check("Adapter-Konfigurationen im Voll-Backup vorhanden", usage.HasAdapterConfig);
Check("Adapter-Verweise gefunden", usage.AdapterLinks > 0);
Check("Jeder Adapter-Verweis nennt ein Feld",
      usage.States.SelectMany(s => s.Links).Where(l => l.Source == UsageSource.Adapter)
           .All(l => l.Field.Length > 0));

// Die Fundstelle soll lesbar sein: Steht neben der ID ein Name („power" bei einer
// Anzeige-App), gehoert er davor - ein blosses customApps[0].objId sagt niemandem etwas.
var mitLabel = fullData.AdapterRefs.Count(r => r.Label.Length > 0);
Console.WriteLine($"  Fundstellen mit sprechendem Namen: {mitLabel} von {fullData.AdapterRefs.Count}");
Check("Ein Teil der Fundstellen traegt einen Namen", mitLabel > 0);
Check("Fundstelle mit Namen nennt auch den technischen Pfad",
      fullData.AdapterRefs.Where(r => r.Label.Length > 0)
              .All(r => r.Where.Contains(r.Label, StringComparison.Ordinal)
                     && r.Where.Contains(r.Field, StringComparison.Ordinal)));
Check("Fundstelle ohne Namen bleibt der reine Feldpfad",
      fullData.AdapterRefs.Where(r => r.Label.Length == 0).All(r => r.Where == r.Field));

// Der Name kommt aus derselben Konfiguration, in der auch Zugangsdaten stehen. Deshalb
// nur eine kurze Positivliste an Feldnamen und zwei Sperren gegen offensichtlich
// sensible Werte - hier gegengeprueft.
Check("Kein Name traegt eine verschluesselte Kennzeichnung",
      fullData.AdapterRefs.All(r => !r.Label.Contains("$/", StringComparison.Ordinal)));
Check("Kein Name sieht wie eine Adresse oder ein Benutzername aus",
      fullData.AdapterRefs.All(r => !r.Label.Contains('@')));
Check("Namen bleiben kurz genug fuer eine Tabellenspalte",
      fullData.AdapterRefs.All(r => r.Label.Length <= 40));

// --- Letzte Wertaenderung als Gegenprobe zum Eintrag ---
// Rueckfrage aus der Praxis: "Diese Datenpunkte nutze ich im Adapter gar nicht." Der
// Eintrag in der Konfiguration sagt darueber nichts - der Zeitstempel des Datenpunkts
// schon. Er stammt aus states.jsonl, nicht aus dem Instanzobjekt: Wer einmal an den
// Adaptereinstellungen dreht, macht dessen Zeitstempel frisch, ohne dass der Datenpunkt
// deshalb lebt.
var mitZeit = usage.States.Count(s => s.LastChange is not null);
Console.WriteLine($"  Datenpunkte mit Zeitstempel: {mitZeit} von {usage.States.Count}");
Check("Letzte Wertaenderung wird mitgefuehrt", mitZeit > 0);
Check("Alter ist gesetzt, wo ein Zeitstempel vorliegt",
      usage.States.Where(s => s.LastChange is not null).All(s => s.AgeDays is not null));
// Bezugspunkt ist der Backup-Zeitpunkt: sonst waere jeder Datenpunkt in einem alten
// Backup kuenstlich "tot".
Check("Alter bezieht sich auf den Backup-Zeitpunkt, nicht auf heute",
      usage.States.Where(s => s.AgeDays is not null).All(s => s.AgeDays >= 0));
// Gegenprobe gegen Analyse C: Beide lesen denselben Zeitstempel.
var ausAnalyseC = stateReport.All.Where(r => r.LastChange is not null)
                             .ToDictionary(r => r.Id, r => r.LastChange!.Value, StringComparer.Ordinal);
var abweichend = usage.States.Count(s => s.LastChange is not null
                                      && ausAnalyseC.TryGetValue(s.Id, out var c)
                                      && c != s.LastChange);
CheckEq("Zeitstempel stimmen mit Analyse C ueberein", abweichend, 0);
// Ein Alias hat systembedingt keinen eigenen Wert - dort darf keine Zahl stehen, die
// eine tote Verbindung suggeriert.
Check("Aliasse zeigen keinen eigenen Zeitstempel",
      usage.States.Where(s => s.IsAlias).All(s => s.LastChangeText.Contains("Alias")));
CheckEq("Spaltenzahl passt weiterhin zur Zeile", UsagePresenter.ColumnsStates.Length,
        UsagePresenter.RowState(usage.States[0]).Length);
CheckEq("CSV-Spalten passen weiterhin zur CSV-Zeile", UsagePresenter.CsvColumnsStates.Length,
        UsagePresenter.CsvRowState(usage.States[0]).Length);
// Der Abgleich mit dem Objektbestand ist die Sicherung dagegen, dass Zugangsdaten oder
// Hostnamen aus dem native-Abschnitt als "Datenpunkt" durchgehen.
var objektIds = new HashSet<string>(fullData.Objects.Where(o => o.Type == "state").Select(o => o.Id),
                                    StringComparer.Ordinal);
Check("Jeder Adapter-Verweis zeigt auf ein echtes state-Objekt",
      fullData.AdapterRefs.All(r => objektIds.Contains(r.StateId)));
// Zwei Filter, zwei Fragen: "welche Datenpunkte hat sich ein Adapter eingetragen?" und
// "welche davon kennt kein Skript?". Der zweite ist eine Teilmenge des ersten - wer nur
// den zweiten anbietet, verschweigt alle Datenpunkte, die Adapter UND Skript nutzen.
var imAdapter = UsagePresenter.FilterStates(usage.States, UsageStateFilter.ImAdapter, null);
var nurAdapter = UsagePresenter.FilterStates(usage.States, UsageStateFilter.NurImAdapter, null);
Console.WriteLine($"  Im Adapter eingetragen: {imAdapter.Count} · davon ohne Skript: {nurAdapter.Count}");
CheckEq("Filter „in einem Adapter\" trifft die gezaehlte Menge",
        imAdapter.Count, usage.StatesInAdapter);
Check("Filter „in einem Adapter\" enthaelt nur Datenpunkte mit Adapterbezug",
      imAdapter.All(s => s.AdapterCount > 0));
Check("Filter „nur im Adapter\" enthaelt nur skriptlose Datenpunkte",
      nurAdapter.All(s => s.ScriptCount == 0 && s.AdapterCount > 0));
Check("„Nur im Adapter\" ist eine Teilmenge von „in einem Adapter\"",
      nurAdapter.All(s => imAdapter.Contains(s)) && nurAdapter.Count <= imAdapter.Count);
// Der Fall, der die Rueckfrage ausgeloest hat: Datenpunkte, die Adapter und Skript
// gemeinsam nutzen, duerfen nicht unter den Tisch fallen.
Check("Datenpunkte mit Adapter UND Skript werden erfasst",
      imAdapter.Any(s => s.ScriptCount > 0));
CheckEq("Filterliste und Aufzaehlung sind gleich lang",
        UsagePresenter.StateFilterLabels.Length, Enum.GetValues<UsageStateFilter>().Length);
// Ein nur im Adapter eingetragener Datenpunkt darf nicht mehr als "nirgends verwendet" gelten.
Check("Nur im Adapter genutzte Datenpunkte gelten als verwendet",
      UsagePresenter.FilterStates(usage.States, UsageStateFilter.NirgendsVerwendet, null)
                    .All(s => s.AdapterCount == 0));

// Gegenprobe gegen die bereits verifizierte Analyse B: Was dort als "in Skripten gefunden"
// gilt, muss hier ein Skript haben. Umgekehrt gilt das nicht - Analyse B sucht die blosse
// Zeichenkette im gesamten Code, diese Analyse nur in Zeichenketten-Literalen.
var usageById = usage.States.ToDictionary(s => s.Id, StringComparer.Ordinal);
var exakt = unused.Where(u => u.InScripts == FindKind.Exakt).ToList();
var ohneTreffer = exakt.Where(u => !usageById.TryGetValue(u.Id, out var st) || st.Unused).ToList();
Console.WriteLine($"  Analyse B: {exakt.Count} Datenpunkte exakt in Skripten, " +
                  $"davon hier ohne Skript: {ohneTreffer.Count}");
Check("Exakte Treffer aus Analyse B werden hier wiedergefunden",
      exakt.Count == 0 || ohneTreffer.Count * 10 <= exakt.Count,
      string.Join("; ", ohneTreffer.Take(3).Select(u => u.Id)));

// Jede Verbindung zeigt auf einen Verwender, den es wirklich gibt: ein Skript aus dem
// Backup oder eine Adapter-Instanz aus der Instanzliste.
var scriptPaths = new HashSet<string>(fullData.Scripts.Select(s => s.DisplayPath), StringComparer.Ordinal);
var instanzNamen = new HashSet<string>(fullData.Instances.Select(i => i.Namespace),
                                       StringComparer.OrdinalIgnoreCase);
Check("Jede Verbindung nennt einen vorhandenen Verwender",
      usage.States.SelectMany(s => s.Links).All(l => l.Source == UsageSource.Skript
          ? scriptPaths.Contains(l.SourceName)
          : instanzNamen.Contains(l.SourceName)));

// Filter und Zaehltexte der Oberflaeche.
var mehrfach = UsagePresenter.FilterStates(usage.States, UsageStateFilter.MehrfachBeschrieben, null);
CheckEq("Filter „mehrfach beschrieben\" trifft die gezaehlte Menge",
        mehrfach.Count, usage.StatesMultiWriter);
Check("Filter „mehrfach beschrieben\" enthaelt nur solche Datenpunkte",
      mehrfach.All(s => s.Writers > 1));
var nirgends = UsagePresenter.FilterStates(usage.States, UsageStateFilter.NirgendsVerwendet, null);
Check("Filter „nirgends verwendet\" enthaelt nur unbenutzte", nirgends.All(s => s.Unused));
var nurMitStates = UsagePresenter.FilterScripts(usage.Scripts, onlyWithStates: true, null);
CheckEq("Filter „nur Skripte mit Datenpunkten\"", nurMitStates.Count, usage.ScriptsWithStates);
CheckEq("Spaltenzahl passt zur Zeile Skript", UsagePresenter.ColumnsScripts.Length,
        UsagePresenter.RowScript(usage.Scripts[0]).Length);
CheckEq("Spaltenzahl passt zur Zeile Datenpunkt", UsagePresenter.ColumnsStates.Length,
        UsagePresenter.RowState(usage.States[0]).Length);
CheckEq("CSV-Spalten passen zur CSV-Zeile", UsagePresenter.CsvColumnsStates.Length,
        UsagePresenter.CsvRowState(usage.States[0]).Length);

// Ein Skript-Backup hat keinen Objektbestand - dann gibt es nichts abzugleichen, und die
// Analyse muss das aushalten statt zu raten.
var usageOhneObjekte = UsageAnalyzer.Analyze(scriptsOnly);
CheckEq("Ohne Objektbestand keine Verbindungen", usageOhneObjekte.Links, 0);
CheckEq("Ohne Objektbestand bleiben die Skripte gelistet",
        usageOhneObjekte.Scripts.Count, scriptsOnly.Scripts.Count);

// --- Backup einer weiteren Anlage, falls vorhanden ---
// Eine weitere Anlage ist die einzige belastbare Probe darauf, ob die Auswertung
// auch mit Formen zurechtkommt, die im eigenen System zufaellig nicht vorkommen: IDs mit
// Leerzeichen und Rauten, Objekte ohne type, VIS 1 statt VIS 2, mehrere Instanzen
// desselben Adapters. Optional - solche Backups liegen nie im Repository.
//
// Gesucht wird in den Unterordnern von testdaten/, ohne einen davon beim Namen zu nennen:
// Wer sein Backup zur Verfuegung stellt, soll dafuer nicht im Quelltext auftauchen.
//
// Ordner mit fuehrendem Unterstrich bleiben aussen vor. Dort liegt Werkstattmaterial —
// etwa das erzeugte Beispiel-Backup fuer die Bildschirmfotos. Ohne diese Regel wuerde ein
// solcher Ordner die Pruefung kapern (er sortiert nach vorn) und sie stillschweigend
// entwerten: Ein selbst gebautes Backup enthaelt genau die Formen nicht, auf die es hier
// ankommt.
var weitere = Directory.GetDirectories(testdaten)
    .Where(d => !Path.GetFileName(d).StartsWith('_'))
    .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
    .SelectMany(d => Directory.GetFiles(d, "*.tar.gz"))
    .FirstOrDefault();

if (weitere is null)
{
    Console.WriteLine("  [--]   kein weiteres Backup in einem Unterordner von testdaten/ - uebersprungen");
    nichtGelaufen.Add("Herkunftspruefung gegen ein zweites Voll-Backup (keines in testdaten/)");
}
else
{
    var weitereData = BackupLoader.Load(weitere);
    Console.WriteLine($"  Weitere Anlage: {weitereData.Objects.Count:N0} Objekte, " +
                      $"{weitereData.Scripts.Count} Skripte, {weitereData.StateCount:N0} States");

    CheckEq("Weiteres Backup ohne uebersprungene Objekte", weitereData.SkippedCount, 0);
    Check("Weiteres Backup gilt als Voll-Backup", weitereData.Kind == BackupKind.Full);

    // Objekte ohne type (Design-Dokumente, einzelne Geraeteknoten) duerfen den Lader nicht stoeren.
    var ohneTyp = weitereData.Objects.Count(o => string.IsNullOrEmpty(o.Type));
    Console.WriteLine($"  Objekte ohne type: {ohneTyp}");
    Check("Objekte ohne type werden gelesen statt verworfen", ohneTyp > 0);

    var weitereUsage = UsageAnalyzer.Analyze(weitereData);
    Console.WriteLine($"  Verwendung: {weitereUsage.Links} Verbindungen, " +
                      $"{weitereUsage.StatesMultiWriter} mehrfach beschrieben, " +
                      $"{weitereUsage.AliasesUnused} ungenutzte Aliasse");
    Check("Verwendungsanalyse findet Verbindungen im weiteren Backup", weitereUsage.Links > 0);

    // Der eigentliche Grund fuer die Suche in Zeichenketten statt in Woertern: Eine ID mit
    // Leerzeichen im Namensteil wuerde ein Wortscanner mitten in der ID zerschneiden.
    var mitLeerzeichen = weitereUsage.States.Count(s => s.Id.Contains(' ') && !s.Unused);
    Console.WriteLine($"  Verwendete IDs mit Leerzeichen: {mitLeerzeichen}");
    Check("IDs mit Leerzeichen werden in Skripten gefunden", mitLeerzeichen > 0);

    // Shelly-IDs tragen die Geraetekennung zwischen Rauten: shelly.0.SHSW-1#<kennung>#1.Relay0.Switch
    var mitRaute = weitereUsage.States.Count(s => s.Id.Contains('#') && !s.Unused);
    Console.WriteLine($"  Verwendete IDs mit Raute: {mitRaute}");
    Check("IDs mit Raute werden in Skripten gefunden", mitRaute > 0);

    // Blockly-Zugriffe muessen eine Richtung bekommen; bei ueberwiegend Blockly waere ein
    // hoher Anteil "erwaehnt" ein Zeichen dafuer, dass die Erkennung ins Leere greift.
    var alleLinks = weitereUsage.Scripts.SelectMany(s => s.Links).ToList();
    var ohneRichtung = alleLinks.Count(l => l.Access == UsageAccess.Unbekannt);
    Console.WriteLine($"  Funde ohne erkennbare Richtung: {ohneRichtung} von {alleLinks.Count}");
    Check("Die Richtung ist bei den meisten Funden bestimmbar",
          ohneRichtung * 4 <= alleLinks.Count, $"{ohneRichtung}/{alleLinks.Count}");

    // Skriptnamen mit fuer Windows verbotenen Zeichen (etwa einem Groesserzeichen).
    var heikleNamen = weitereData.Scripts.Count(s => ScriptExporter.SanitizeFileName(s.Name) != s.Name);
    Console.WriteLine($"  Skriptnamen, die der Export entschaerfen muss: {heikleNamen}");

    var weitereExport = Path.Combine(Path.GetTempPath(), "iob_export_weitere");
    if (Directory.Exists(weitereExport)) Directory.Delete(weitereExport, true);
    var weitereErgebnis = ScriptExporter.Export(weitereData.Scripts, weitereExport,
                                              ScriptExportFormat.OriginalOnly, weitereData.SourceFile);
    Check("Skripte der weiteren Anlage exportieren ohne Fehler", weitereErgebnis.Errors.Count == 0,
          string.Join("; ", weitereErgebnis.Errors.Take(3)));
    CheckEq("Export der weiteren Anlage schreibt eine Datei je Skript",
            weitereErgebnis.Files, weitereData.Scripts.Count);
    Check("Export der weiteren Anlage liegt unter dem Backup-Namen",
          weitereErgebnis.RootDir.Contains(BackupNaming.FolderName(weitereData.SourceFile), StringComparison.Ordinal),
          weitereErgebnis.RootDir);
    Directory.Delete(weitereExport, true);

    // Die uebrigen Analysen duerfen an diesen Daten nicht scheitern.
    Check("Alle Analysen laufen auf dem weiteren Backup durch", RunAll(weitereData));

    static bool RunAll(BackupData data)
    {
        try
        {
            OrphanAnalyzer.FindOrphanObjects(data);
            OrphanAnalyzer.FindUnusedDatapoints(data);
            OrphanAnalyzer.FindAdaptersWithoutInstance(data);
            StateAnalyzer.Analyze(data);
            VisAnalyzer.Analyze(data);
            LoggingAnalyzer.Analyze(data);
            AliasAnalyzer.Analyze(data);
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine("       -> " + ex.Message);
            return false;
        }
    }
}

// --- Datenpunkt-Suche ---
Console.WriteLine();
Console.WriteLine("=== Datenpunkte: Suche und Wert ===");

sw.Restart();
var punkte = DatapointPresenter.Build(fullData);
sw.Stop();
Console.WriteLine($"  Aufbaudauer: {sw.ElapsedMilliseconds} ms");
Console.WriteLine($"  Datenpunkte: {punkte.Count:N0}");

Check("Aufbau dauert unter 15 s", sw.ElapsedMilliseconds < 15000, $"{sw.ElapsedMilliseconds} ms");

// Die Liste deckt beide Richtungen ab: state-Objekte und Werte ohne Objekt. Fehlte eine,
// waere ausgerechnet der Fall nicht auffindbar, in dem ein Datenpunkt nur noch als Wert
// existiert.
var stateObjekte = fullData.Objects.Count(o => o.Type == "state");
var werteOhneObjekt = punkte.Count(p => !p.HasObject);
Console.WriteLine($"  davon state-Objekte: {stateObjekte:N0}, Werte ohne Objekt: {werteOhneObjekt:N0}");
CheckEq("Jedes state-Objekt ist auffindbar", punkte.Count(p => p.HasObject), stateObjekte);
Check("Werte ohne Objekt sind auffindbar", werteOhneObjekt > 0);
Check("Keine ID kommt doppelt vor",
      punkte.Select(p => p.Id).Distinct(StringComparer.Ordinal).Count() == punkte.Count);

CheckEq("Spaltenzahl passt zur Zeile",
        DatapointPresenter.Columns.Length, DatapointPresenter.DisplayRow(punkte[0]).Length);
CheckEq("CSV-Spalten passen zur CSV-Zeile",
        DatapointPresenter.CsvColumns.Length, DatapointPresenter.Row(punkte[0]).Length);

// Die Suche muss ueber ID und Name gehen — nur ueber die ID waere sie fuer jemanden
// nutzlos, der den Datenpunkt unter seinem Klarnamen kennt.
var mitNamen = punkte.FirstOrDefault(p => p.Name.Length > 3 && !p.Id.Contains(p.Name));
if (mitNamen is not null)
    Check("Suche findet ueber den Namen",
          DatapointPresenter.Filter(punkte, mitNamen.Name).Any(p => p.Id == mitNamen.Id),
          mitNamen.Name);

var beispielId = punkte[punkte.Count / 2].Id;
Check("Suche findet ueber die ID",
      DatapointPresenter.Filter(punkte, beispielId).Any(p => p.Id == beispielId), beispielId);

// Mehrere Begriffe muessen alle zutreffen, ihre Reihenfolge aber egal sein.
var teile = beispielId.Split('.');
if (teile.Length >= 3)
{
    var umgedreht = $"{teile[^1]} {teile[0]}";
    Check("Mehrere Suchbegriffe wirken unabhaengig von der Reihenfolge",
          DatapointPresenter.Filter(punkte, umgedreht).Any(p => p.Id == beispielId), umgedreht);
}

Check("Leere Suche liefert alles", DatapointPresenter.Filter(punkte, "").Count == punkte.Count);
Check("Unsinnige Suche liefert nichts",
      DatapointPresenter.Filter(punkte, "kjhgfdsaqwertz").Count == 0);

// Der eigentliche Zweck: ein JSON-Wert muss eingerueckt und damit pruefbar herauskommen.
var jsonPunkt = punkte.Where(p => !p.ValTruncated
                                  && p.Val.TrimStart().StartsWith('{')
                                  && p.Val.Length > 200)
                      .OrderByDescending(p => p.Val.Length)
                      .FirstOrDefault();

Check("Das Testbackup enthaelt einen JSON-Wert zum Pruefen", jsonPunkt is not null);

if (jsonPunkt is not null)
{
    var voll = DatapointPresenter.FullValue(jsonPunkt);
    Console.WriteLine($"  JSON-Beispiel: {jsonPunkt.Id} ({jsonPunkt.ValLength:N0} Zeichen)");

    Check("JSON wird eingerueckt ausgegeben", voll.Contains('\n'), jsonPunkt.Id);
    Check("Eingeruecktes JSON bleibt gueltiges JSON",
          DatapointPresenter.PrettyJson(voll) is not null, jsonPunkt.Id);

    // Der Inhalt darf sich dabei nicht aendern — nur seine Formatierung. Verglichen wird
    // deshalb nicht der Rohtext (der traegt die Einrueckung ja gerade), sondern beide Seiten
    // erneut kompakt geschrieben. Sind sie dann zeichengleich, ist nur Weissraum dazugekommen.
    using var rohWert = System.Text.Json.JsonDocument.Parse(jsonPunkt.Val);
    using var formatierterWert = System.Text.Json.JsonDocument.Parse(voll);
    CheckEq("Einruecken aendert den Inhalt nicht",
            System.Text.Json.JsonSerializer.Serialize(rohWert.RootElement),
            System.Text.Json.JsonSerializer.Serialize(formatierterWert.RootElement));
}

// Was kein JSON ist, muss unveraendert bleiben — eine Zahl in Anfuehrungszeichen zu
// setzen waere eine stille Veraenderung des Werts.
Check("Zahl bleibt unveraendert", DatapointPresenter.PrettyJson("42") is null);
Check("Text bleibt unveraendert", DatapointPresenter.PrettyJson("eingeschaltet") is null);
Check("Kaputtes JSON bleibt unveraendert", DatapointPresenter.PrettyJson("{\"a\":") is null);
Check("Umlaute bleiben lesbar",
      DatapointPresenter.PrettyJson("{\"raum\":\"Küche\"}")?.Contains("Küche") == true);

// Ein gekappter Wert darf nicht als vollstaendig ausgegeben werden.
var gekappterPunkt = punkte.FirstOrDefault(p => p.ValTruncated);
if (gekappterPunkt is not null)
    Check("Gekappter Wert wird nicht formatiert",
          DatapointPresenter.FullValue(gekappterPunkt) == gekappterPunkt.Val
          && DatapointPresenter.ValueInfo(gekappterPunkt).Contains("gekürzt"),
          gekappterPunkt.Id);

// Die Beschreibung muss die Felder zeigen, die ein Wert zum Einordnen braucht.
var mitEinheit = punkte.FirstOrDefault(p => p.Unit.Length > 0);
Check("Das Testbackup liefert Einheiten", mitEinheit is not null);
if (mitEinheit is not null)
    Check("Beschreibung nennt die Einheit",
          DatapointPresenter.Definition(mitEinheit).Contains(mitEinheit.Unit), mitEinheit.Id);

var mitRolle = punkte.FirstOrDefault(p => p.Role.Length > 0);
Check("Das Testbackup liefert Rollen", mitRolle is not null);
if (mitRolle is not null)
    Check("Beschreibung nennt die Rolle",
          DatapointPresenter.Definition(mitRolle).Contains(mitRolle.Role), mitRolle.Id);

Check("Werte ohne Objekt melden die fehlende Definition",
      DatapointPresenter.Definition(punkte.First(p => !p.HasObject)).Contains("ohne Objekt"));

CheckEq("Ohne Auswahl bleibt das Wertfeld leer", DatapointPresenter.FullValue(null), "");

// --- Hilfe und Aenderungsverlauf ---
// Der Verlauf steht in der Anwendung, weil sie als einzelne Datei weitergegeben wird: Wer
// eine neuere Fassung bekommt, hat kein Repository, sondern nur eine neue Versionsnummer.
Console.WriteLine();
Console.WriteLine("=== Hilfe und Aenderungsverlauf ===");
Check("Hilfe hat einen Titel", HelpContent.Blocks[0].Kind == HelpBlockKind.Title);
Check("Hilfe nennt die KI-Herkunft",
      HelpContent.Blocks.Any(b => b.Text.Contains("mit KI erstellt", StringComparison.OrdinalIgnoreCase)));
// Der Verlauf hat seit 1.18.9 einen eigenen Tab. Geprueft wird deshalb dreierlei: dass die
// Hilfe ihn nicht mehr selbst enthaelt (sonst stuende er doppelt), dass sie auf den neuen
// Tab hinweist, und dass die Blockliste des Tabs jede Version tatsaechlich zeigt.
Check("Hilfe traegt den Verlauf nicht mehr selbst",
      !HelpContent.Blocks.Any(b => b.Kind == HelpBlockKind.Heading && b.Text == "Was ist neu"));
Check("Hilfe erklaert den Tab „Aenderungen\"",
      HelpContent.Blocks.Any(b => b.Kind == HelpBlockKind.Heading && b.Text.Contains("Änderungen")));
Check("Verlaufs-Tab hat einen Titel", ChangelogContent.Blocks[0].Kind == HelpBlockKind.Title);
Check("Verlaufs-Tab zeigt jede Version",
      ChangelogContent.Entries.All(e => ChangelogContent.Blocks.Any(
          b => b.Kind == HelpBlockKind.Heading && b.Text.Contains(e.Version)
                                              && b.Text.Contains(e.Date))));
Check("Verlaufs-Tab zeigt jede einzelne Aenderung",
      ChangelogContent.Entries.SelectMany(e => e.Changes)
                              .All(c => ChangelogContent.Blocks.Any(b => b.Text.Contains(c))));
Check("Aenderungsverlauf ist nicht leer", ChangelogContent.Entries.Count > 0);
Console.WriteLine($"  {ChangelogContent.Entries.Count} Versionen im Verlauf, neueste: " +
                  $"{ChangelogContent.Entries[0].Version}");

// Die oberste Version muss die ausgelieferte sein - sonst zeigt die Hilfe einen aelteren
// Stand als die Titelleiste. Massgeblich ist die csproj: Die Core-Bibliothek traegt keine
// eigene Versionsnummer, die beiden Oberflaechen schon.
foreach (var projekt in new[] { "IobBackupAnalyzer.App", "IobBackupAnalyzer.Avalonia" })
{
    var csproj = Path.Combine(root, "src", projekt, projekt + ".csproj");
    if (!File.Exists(csproj)) { Console.WriteLine($"  [--]   {projekt}.csproj nicht gefunden"); continue; }

    // Nur ein Element mit Versionsnummer zaehlt: In den Kommentaren der csproj steht
    // "<Version>" auch im Fliesstext, und der erste Treffer waere sonst der Kommentar.
    var text = File.ReadAllText(csproj);
    var treffer = System.Text.RegularExpressions.Regex.Match(
        text, @"<Version>\s*(\d+\.\d+\.\d+)\s*</Version>");
    var version = treffer.Success ? treffer.Groups[1].Value : "?";

    CheckEq($"Verlauf passt zur Version in {projekt}", version, ChangelogContent.Entries[0].Version);
}

// Absteigend sortiert, keine doppelten Nummern, jeder Eintrag mit Inhalt.
var versionen = ChangelogContent.Entries.Select(e => new Version(e.Version)).ToList();
Check("Verlauf ist absteigend sortiert",
      versionen.SequenceEqual(versionen.OrderByDescending(v => v)));
// Lueckenlos, und zwar ueber den gesamten Verlauf. Zweimal ist hier schon etwas
// verschwunden: 1.18.3 wurde beim Anlegen von 1.18.4 ueberschrieben statt vorangestellt,
// und 1.13.1 samt der kompletten Reihe 1.14.x fehlte von Anfang an. Der Verlauf beginnt
// bewusst erst bei seinem aeltesten Eintrag - was davor liegt, steht in der
// Git-Historie. Ab dort aber muss jede Fassung vorkommen.
var fehlend = new List<string>();

// Innerhalb jeder Minor-Reihe: von deren kleinstem bis groesstem Patch keine Luecke.
foreach (var gruppe in versionen.GroupBy(v => (v.Major, v.Minor)))
{
    var patches = gruppe.Select(v => v.Build).OrderBy(p => p).ToList();
    fehlend.AddRange(Enumerable.Range(patches[0], patches[^1] - patches[0] + 1)
                               .Except(patches)
                               .Select(p => $"{gruppe.Key.Major}.{gruppe.Key.Minor}.{p}"));
}

// Und keine ausgelassene Minor-Reihe: 1.14.x fehlte komplett und fiel niemandem auf.
var reihen = versionen.Select(v => v.Minor).Distinct().OrderBy(m => m).ToList();
fehlend.AddRange(Enumerable.Range(reihen[0], reihen[^1] - reihen[0] + 1)
                           .Except(reihen)
                           .Select(m => $"{versionen[0].Major}.{m}.x"));

Check("Keine Luecke im Aenderungsverlauf",
      fehlend.Count == 0,
      "fehlt: " + string.Join(", ", fehlend.OrderBy(s => s)));

CheckEq("Keine doppelten Versionsnummern",
        versionen.Distinct().Count(), versionen.Count);
Check("Jeder Eintrag nennt mindestens eine Aenderung",
      ChangelogContent.Entries.All(e => e.Changes.Count > 0 && e.Changes.All(c => c.Length > 10)));
Check("Jeder Eintrag hat ein Datum",
      ChangelogContent.Entries.All(e => DateTime.TryParseExact(e.Date, "dd.MM.yyyy",
          System.Globalization.CultureInfo.InvariantCulture,
          System.Globalization.DateTimeStyles.None, out _)));

Console.WriteLine();
Console.WriteLine("=== Robustheit ===");
var zip = WaehleArchiv("skript-zip", "*-scripts.zip");
Check("Fremde ZIP wird sauber abgelehnt", Throws<NotABackupException>(() => BackupLoader.Load(zip)));

// Backup ohne gzip-Huelle: Auf macOS packen Safari und iCloud Drive .gz beim Herunterladen
// selbsttaetig aus - uebrig bleibt ein reines .tar. Frueher landete das beim JSON-Leser und
// scheiterte mit "'b' is an invalid start of a value" (das b von "backup/..." im Tar-Kopf).
var entpackt = Path.Combine(Path.GetTempPath(), "iob_ohne_gzip.tar");
using (var src = File.OpenRead(full))
using (var gzOut = new System.IO.Compression.GZipStream(src, System.IO.Compression.CompressionMode.Decompress))
using (var dst = File.Create(entpackt))
    gzOut.CopyTo(dst);

try
{
    var ausTar = BackupLoader.Load(entpackt);
    CheckEq("Entpacktes .tar wird gelesen: Objekte", ausTar.Objects.Count, fullData.Objects.Count);
    CheckEq("Entpacktes .tar wird gelesen: Skripte", ausTar.Scripts.Count, fullData.Scripts.Count);
    CheckEq("Entpacktes .tar wird gelesen: States", ausTar.StateCount, fullData.StateCount);
    Check("Entpacktes .tar gilt als Voll-Backup", ausTar.Kind == BackupKind.Full);
}
finally { File.Delete(entpackt); }
Check("Nicht existierende Datei wird sauber abgelehnt",
      Throws<NotABackupException>(() => BackupLoader.Load(Path.Combine(testdaten, "gibtsnicht.tar.gz"))));

Check("Dateinamen-Bereinigung", ScriptExporter.SanitizeFileName("Modell+") == "Modell+");
Check("Dateinamen-Bereinigung entfernt verbotene Zeichen",
      ScriptExporter.SanitizeFileName("a/b:c*d") == "a_b_c_d",
      ScriptExporter.SanitizeFileName("a/b:c*d"));

// Zaehlwoerter: Bei genau einem Stueck darf keine Klammerform und keine Mehrzahl stehen.
CheckEq("Einzahl ohne Klammerform", VisPresenter.Count(1, "Datei", "Dateien"), "1 Datei");
CheckEq("Mehrzahl ab zwei", VisPresenter.Count(2, "Datei", "Dateien"), "2 Dateien");
CheckEq("Null nimmt die Mehrzahl", VisPresenter.Count(0, "Datei", "Dateien"), "0 Dateien");
CheckEq("Grosse Zahlen mit Tausenderpunkt",
        VisPresenter.Count(1234, "Datei", "Dateien"), "1.234 Dateien");

// Regression zum Linux-Test vom 22.08.2026: Auf einem System ohne deutsche Spracheinstellung
// stand in der Oberfläche "16,576" statt "16.576" — die Beschriftung deutsch, die Zahlen
// englisch. Hier wird bewusst eine fremde Kultur gesetzt und geprüft, dass AppCulture sie
// überstimmt. Ohne diese Prüfung fiele ein Rückbau erst wieder jemandem auf einem
// nicht-deutschen Rechner auf, also frühestens nach der Auslieferung.
CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
AppCulture.Apply();
CheckEq("Zahlen bleiben deutsch, auch bei fremder Systemkultur",
        VisPresenter.Count(1234, "Datei", "Dateien"), "1.234 Dateien");
CheckEq("Zahlenformat direkt geprueft", 16576.ToString("N0"), "16.576");
Check("Kein neuer Text traegt eine Klammerform",
      !VisPresenter.ZipIntro(1).Contains("(e)")
      && !VisPresenter.ZipIntro(3).Contains("(e)"),
      VisPresenter.ZipIntro(1));

// Schutz der Speicherziele: Kein Export darf ueber ein Backup-Archiv schreiben.
Check("Derselbe Pfad wird als dieselbe Datei erkannt",
      ExportPaths.IsSameFile(full, Path.Combine(Path.GetDirectoryName(full)!, ".",
                                                Path.GetFileName(full))));
Check("Zwei verschiedene Dateien sind nicht dieselbe",
      !ExportPaths.IsSameFile(full, js));
Check("Leerer Pfad ist nie dieselbe Datei", !ExportPaths.IsSameFile(full, null));
Check("Archivendungen werden erkannt",
      ExportPaths.LooksLikeArchive("sicherung.tar.gz")
      && ExportPaths.LooksLikeArchive("sicherung.TGZ")
      && ExportPaths.LooksLikeArchive("sicherung.tar"));
Check("Gewoehnliche Ziele loesen keine Rueckfrage aus",
      ExportPaths.ArchiveWarning("liste.csv") is null
      && ExportPaths.ArchiveWarning("projekt.zip") is null);
Check("Rueckfrage nennt die bedrohte Datei",
      ExportPaths.ArchiveWarning("sicherung.tar.gz")?.Contains("sicherung.tar.gz") == true);

Check("Blockly-Decoder ohne XML liefert Original",
      BlocklyDecoder.Decode("console.log('hallo');", false) is { Xml: null, Broken: false });
Check("Blockly-Decoder markiert defektes Blockly",
      BlocklyDecoder.Decode("console.log('x');", true).Broken);

// Regulaere Ausdruecke laufen ueber Inhalte, die das Programm nicht selbst erzeugt hat -
// Skriptquelltext, VIS-Ansichten, Dateinamen. Ohne Zeitgrenze haengt
// das Programm bei einem ungluecklichen Muster, statt die Datei zu ueberspringen. Der
// Check liest den Quelltext, damit ein spaeter ergaenztes Muster nicht stillschweigend
// wieder ohne Grenze laeuft.
var coreOrdner = Path.Combine(root, "src", "IobBackupAnalyzer.Core");
// TopDirectoryOnly: bin/ und obj/ liegen darunter und gehoeren nicht zum Quelltext.
var coreQuellen = Directory.EnumerateFiles(coreOrdner, "*.cs", SearchOption.TopDirectoryOnly)
                           .ToList();
var mitMuster = 0;
var ohneZeitgrenze = new List<string>();
foreach (var datei in coreQuellen)
{
    // Deklaration bis zum abschliessenden Semikolon - die Muster stehen teils ueber
    // mehrere Zeilen, ein zeilenweiser Vergleich wuerde sie faelschlich melden.
    foreach (var stelle in Deklarationen(File.ReadAllText(datei), "Regex "))
    {
        mitMuster++;
        if (!stelle.Contains("RegexLimits.MatchTimeout", StringComparison.Ordinal))
            ohneZeitgrenze.Add(Path.GetFileName(datei));
    }
}

// ------------------------------------------- Fortschrittsmeldungen: „Bitte warten"
//
// Ein Schritt, der nur seinen Namen nennt ("Analyse 3/5: unbenutzte Datenpunkte"), sieht
// aus wie ein Ergebnis. Beide Oberflaechen stellen deshalb "Bitte warten" voran - und zwar
// an einer einzigen Stelle je Fassung, damit es nicht in zehn Zeichenketten gepflegt
// werden muss und in einer davon fehlt.

foreach (var (datei, bezeichnung) in new[]
         {
             (Path.Combine(root, "src", "IobBackupAnalyzer.App", "MainForm.cs"), "WinForms"),
             (Path.Combine(root, "src", "IobBackupAnalyzer.Avalonia", "MainWindow.axaml.cs"), "Avalonia")
         })
{
    var quelle = File.Exists(datei) ? File.ReadAllText(datei) : "";

    Check($"{bezeichnung}: Fortschrittsmeldungen sagen „Bitte warten\"",
          quelle.Contains("Bitte warten", StringComparison.Ordinal)
          && quelle.Contains("Wartetext(msg)", StringComparison.Ordinal));

    // Die Analyse-Schritte sind der laengste Abschnitt - dort ist der Hinweis am wichtigsten.
    Check($"{bezeichnung}: auch die Analyse meldet sich mit Wartehinweis",
          quelle.Contains("Wartetext(\"Backup wird analysiert", StringComparison.Ordinal));
}

// Die Schritte selbst kommen aus dem Core und muessen benannt und gezaehlt sein.
var analyseQuelle = File.ReadAllText(Path.Combine(root, "src", "IobBackupAnalyzer.Core",
                                                  "AnalysisResults.cs"));
Check("Analyse-Schritte sind durchnummeriert",
      analyseQuelle.Contains("Analyse {nummer}/5", StringComparison.Ordinal));

// ---------------------------------------------- Hilfe: Platzhalter fuer das Ladeprotokoll
//
// Der Pfad wird erst beim Anzeigen eingesetzt. Vergisst eine der beiden Oberflaechen den
// Aufruf, stuende in der Hilfe woertlich "{ladeprotokoll}" - und ausgerechnet der Hinweis,
// den jemand sucht, waere unbrauchbar.

var hilfeMitPlatzhalter = HelpContent.Blocks
    .Count(b => b.Text.Contains(HelpContent.LogPlaceholder, StringComparison.Ordinal));
Check("Die Hilfe nennt den Ort des Ladeprotokolls", hilfeMitPlatzhalter == 1,
      $"{hilfeMitPlatzhalter} Bloecke mit Platzhalter");

var aufgeloest = HelpContent.Resolve(HelpContent.Blocks
    .First(b => b.Text.Contains(HelpContent.LogPlaceholder, StringComparison.Ordinal)).Text);
Check("Der Platzhalter wird durch einen echten Pfad ersetzt",
      !aufgeloest.Contains(HelpContent.LogPlaceholder, StringComparison.Ordinal)
      && aufgeloest.Contains("ladeprotokoll.txt", StringComparison.OrdinalIgnoreCase));

// Beide Oberflaechen muessen Resolve benutzen - sonst wirkt der Platzhalter nicht.
foreach (var (datei, bezeichnung) in new[]
         {
             (Path.Combine(root, "src", "IobBackupAnalyzer.App", "HelpTab.cs"), "WinForms"),
             (Path.Combine(root, "src", "IobBackupAnalyzer.Avalonia", "Views", "HelpView.axaml.cs"), "Avalonia")
         })
{
    var quelle = File.Exists(datei) ? File.ReadAllText(datei) : "";
    Check($"Hilfe der {bezeichnung}-Fassung loest Platzhalter auf",
          quelle.Contains("HelpContent.Resolve", StringComparison.Ordinal));
}

Check("Regulaere Ausdruecke werden ueberhaupt gefunden", mitMuster > 0, mitMuster.ToString());
Check("Jeder regulaere Ausdruck hat eine Zeitgrenze",
      ohneZeitgrenze.Count == 0, string.Join(", ", ohneZeitgrenze.Distinct()));
Check("Die Zeitgrenze steht an einer einzigen Stelle",
      File.Exists(Path.Combine(coreOrdner, "RegexLimits.cs"))
      && File.ReadAllText(Path.Combine(coreOrdner, "RegexLimits.cs"))
             .Contains("TimeSpan.FromSeconds", StringComparison.Ordinal));

// Kein Weg an TarSource vorbei.
//
// Das ist keine Stilfrage, sondern die Voraussetzung dafür, dass die Browser-Fassung
// ueberhaupt ein Backup oeffnen kann: Wer irgendwo wieder direkt zu TarReader greift,
// baut eine Stelle ein, die auf dem Rechner laeuft und im Browser sofort mit
// "System.Formats.Tar is not supported on this platform" abbricht. Auf dem
// Entwicklungsrechner faellt das nie auf.
//
// Genau das ist beim Bauen einmal passiert — eine zurueckgenommene Aenderung nahm die
// Umstellung im BackupLoader mit. Aufgefallen ist es beim Durchsehen, nicht beim Testen.
var tarDirekt = Directory.EnumerateFiles(coreOrdner, "*.cs")
    .Where(f => Path.GetFileName(f) != "TarSource.cs")
    .Where(f => System.Text.RegularExpressions.Regex.IsMatch(
        File.ReadAllText(f), @"\bnew TarReader\b|\bTarEntry\b"))
    .Select(Path.GetFileName)
    .ToList();

Check("Nur TarSource greift direkt auf System.Formats.Tar zu",
      tarDirekt.Count == 0,
      string.Join(", ", tarDirekt));

// ---------------------------------------------------------------- Tar-Leser

// Seit der Browser-Fassung gibt es zwei Wege, ein Archiv zu lesen: den eingebauten
// TarReader von .NET und den eigenen Leser in TarSource. Der eigene springt nur in
// WebAssembly ein, wo .NET statt System.Formats.Tar nur eine Attrappe ausliefert — und
// genau deshalb wird er hier geprueft: Auf dem Rechner laeuft er sonst nie, sein Ausfall
// wuerde also erst im Browser auffallen, wo niemand hinsieht.
//
// Geprueft wird nicht "laeuft durch", sondern "liefert dasselbe": Name, Groesse, Art und
// Pruefsumme des Inhalts jedes einzelnen Eintrags, an den echten Archiven.

Console.WriteLine();
Console.WriteLine("Tar-Leser: eigener gegen eingebauten");

var tarArchive = new List<string>();
if (File.Exists(full)) tarArchive.Add(full);
if (File.Exists(js)) tarArchive.Add(js);

// Dazu das selbst erzeugte Pruefarchiv: Es enthaelt bewusst Sonderfaelle, die in einem
// echten Backup nicht jedes Mal vorkommen.
var tarPruefarchiv = Path.Combine(Path.GetTempPath(), "iob-tarvergleich.tar.gz");
try
{
    ErzeugePruefarchiv(tarPruefarchiv);
    tarArchive.Add(tarPruefarchiv);
}
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
{
    // Ohne das Zusatzarchiv bleiben die echten Backups — die sind der wichtigere Massstab.
    // Gesperrte Dateien im Temp-Ordner sind unter Windows Alltag (Virenscanner, ein
    // zweiter Lauf daneben) und duerfen den Vergleich nicht zu Fall bringen.
    Console.WriteLine("  [--]   Zusatzarchiv nicht erzeugbar: " + ex.Message);
}

if (tarArchive.Count == 0)
{
    nichtGelaufen.Add("Tar-Leser: kein Archiv zum Vergleichen gefunden");
}
else
{
    foreach (var archiv in tarArchive)
    {
        var name = Path.GetFileName(archiv);

        try
        {
            var mitEingebautem = TarEintraege(archiv, eigener: false);
            var mitEigenem = TarEintraege(archiv, eigener: true);

            CheckEq($"Tar {name}: gleiche Eintragszahl", mitEigenem.Count, mitEingebautem.Count);

            var unterschiede = new List<(string A, string B)>();
            var namensvarianten = 0;

            foreach (var (a, b) in mitEingebautem.Zip(mitEigenem, (a, b) => (a, b)))
            {
                if (a == b) continue;

                // Ein bekannter, harmloser Fall: Bei Archiven im GNU-Format liefert der
                // eingebaute Leser den Eintragsnamen mitsamt der Zeitstempel, die dort an
                // der Stelle des Namensvorspanns stehen — aus "backup/" wird
                // "15236273373 15236273374/backup/". Der eigene Leser gibt den blanken
                // Namen zurueck. Fuer die Auswertung ist das gleichwertig: Der Loader
                // sucht seine Dateien ohnehin mit EndsWith auf Segmentgrenze.
                //
                // Gezaehlt und ausgegeben wird es trotzdem — was hier stillschweigend
                // wuechse, waere beim naechsten Mal ein echter Fehler.
                var (nameA, restA) = Zerlegen(a);
                var (nameB, restB) = Zerlegen(b);

                if (restA == restB && (nameA.EndsWith("/" + nameB, StringComparison.Ordinal)
                                       || nameA.TrimStart().EndsWith(nameB, StringComparison.Ordinal)))
                {
                    namensvarianten++;
                    continue;
                }

                unterschiede.Add((a, b));
            }

            Check($"Tar {name}: jeder Eintrag deckungsgleich",
                  unterschiede.Count == 0,
                  unterschiede.Count == 0
                      ? null
                      : $"{unterschiede.Count} Abweichungen, erste: eingebaut „{unterschiede[0].A}“ " +
                        $"/ eigener „{unterschiede[0].B}“");

            if (namensvarianten > 0)
                Console.WriteLine($"  [--]   {name}: {namensvarianten} Eintragsnamen im GNU-Format " +
                                  "unterschiedlich geschrieben, inhaltlich gleich");
        }
        catch (Exception ex)
        {
            Check($"Tar {name}: beide Leser kommen durch", false, ex.Message);
        }
    }

    try { File.Delete(tarPruefarchiv); } catch { /* Aufraeumen ist Kuer */ }
}

// Die Browser-Fassung darf hinter den Desktop-Fassungen zurueckliegen — sie ist juenger.
// Was sie nicht darf: eine Nummer tragen, die es im Verlauf gar nicht gibt, oder eine,
// die neuer ist als der Verlauf selbst.
var webCsproj = Path.Combine(root, "src", "IobBackupAnalyzer.Web", "IobBackupAnalyzer.Web.csproj");
if (!File.Exists(webCsproj))
{
    nichtGelaufen.Add("Version der Browser-Fassung (Projektdatei nicht gefunden)");
}
else
{
    var webVersion = System.Text.RegularExpressions.Regex.Match(
        File.ReadAllText(webCsproj), @"<Version>\s*(\d+\.\d+\.\d+)\s*</Version>");

    var webNummer = webVersion.Success ? webVersion.Groups[1].Value : "?";

    Check($"Browser-Fassung {webNummer} steht im Aenderungsverlauf",
          ChangelogContent.Entries.Any(e => e.Version == webNummer));
    Check("Browser-Fassung ist nicht neuer als der Verlauf",
          webVersion.Success
          && new Version(webNummer) <= new Version(ChangelogContent.Entries[0].Version));
}

// ---------------------------------------------------------------- Ergebnis

Console.WriteLine();
Console.WriteLine(new string('-', 60));
Console.WriteLine($"Bestanden: {passed}   Fehlgeschlagen: {failed}" +
                  (skipped > 0 ? $"   Uebersprungen: {skipped} (ohne Referenzwert)" : ""));

if (nichtGelaufen.Count > 0)
{
    // Bewusst nach der Ergebniszeile und nicht mittendrin: Was hier steht, ist nicht
    // geprueft worden. Ein Lauf ohne Fehlschlag ist deshalb noch keine vollstaendige
    // Verifikation, und wer ausliefert, soll das sehen, ohne die Ausgabe durchzublaettern.
    Console.WriteLine();
    Console.WriteLine($"Nicht geprueft ({nichtGelaufen.Count}):");
    foreach (var luecke in nichtGelaufen) Console.WriteLine($"  - {luecke}");
}
return failed == 0 ? 0 : 1;

// ---------------------------------------------------------------- Helfer

// Jede Stelle, an der das Stichwort steht, vom Stichwort bis zum abschliessenden
// Semikolon. Gedacht fuer Felddeklarationen, die sich ueber mehrere Zeilen ziehen.
static IEnumerable<string> Deklarationen(string text, string stichwort)
{
    var i = 0;
    while ((i = text.IndexOf(stichwort, i, StringComparison.Ordinal)) >= 0)
    {
        var ende = text.IndexOf(';', i);
        if (ende < 0) yield break;
        yield return text[i..ende];
        i = ende;
    }
}

static bool Throws<T>(Action a) where T : Exception
{
    try { a(); return false; }
    catch (T) { return true; }
    catch { return false; }
}

static bool XmlWellFormed(string xml)
{
    try { new XmlDocument().LoadXml(xml); return true; }
    catch (XmlException) { return false; }
}

static bool XmlEqual(string a, string b)
{
    try
    {
        var da = new XmlDocument { PreserveWhitespace = false };
        da.LoadXml(a);
        var db = new XmlDocument { PreserveWhitespace = false };
        db.LoadXml(b);
        return da.OuterXml == db.OuterXml;
    }
    catch (XmlException)
    {
        return false;
    }
}

static string FindProjectRoot()
{
    var dir = AppContext.BaseDirectory;
    for (var i = 0; i < 8 && dir is not null; i++)
    {
        if (Directory.Exists(Path.Combine(dir, "testdaten"))) return dir;
        dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar));
    }
    return Directory.GetCurrentDirectory();
}

/// <summary>
/// Liefert den Aufruf fuer bash, oder null, wenn auf diesem Rechner keine erreichbar ist.
///
/// Zuerst der schlichte Name: Auf Linux und macOS ist damit alles gesagt, und unter Windows
/// greift er, wenn der Lauf aus einer Git-Bash kommt. Danach die ueblichen Orte einer
/// Git-Installation.
///
/// <b>Warum der Umweg?</b> build.ps1 startet den Verifikationslauf aus pwsh heraus, und dort
/// steht die Git-Bash nicht im Pfad. Die Ausfuehrungstests des Aufraeum-Skripts fielen
/// dadurch bei jedem Build vor der Auslieferung aus — also ausgerechnet die Pruefungen, die
/// belegen, dass das erzeugte Skript wirklich laeuft und ein kleines "j" nicht loescht. Der
/// Lauf war nie rot, er war nur zwoelf Pruefungen kuerzer, und die Hinweiszeile ging in
/// achthundert Zeilen Ausgabe unter.
/// </summary>
static string? FindeBash()
{
    var kandidaten = new List<string> { "bash" };

    foreach (var variable in new[] { "ProgramFiles", "ProgramFiles(x86)", "LOCALAPPDATA" })
    {
        var wurzel = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrEmpty(wurzel)) continue;
        kandidaten.Add(Path.Combine(wurzel, "Git", "bin", "bash.exe"));
        kandidaten.Add(Path.Combine(wurzel, "Git", "usr", "bin", "bash.exe"));
    }

    foreach (var kandidat in kandidaten)
    {
        // Der schlichte Name laesst sich nicht vorab pruefen — er wird einfach versucht.
        if (kandidat != "bash" && !File.Exists(kandidat)) continue;

        try
        {
            var psi = new ProcessStartInfo(kandidat, "-c \"exit 0\"")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            using var p = Process.Start(psi);
            if (p is null) continue;

            // Eine bash, die auf "exit 0" nicht binnen zehn Sekunden antwortet, ist fuer
            // diese Pruefungen unbrauchbar - dann lieber der naechste Kandidat.
            if (!p.WaitForExit(10_000)) { try { p.Kill(true); } catch { /* schon weg */ } continue; }
            if (p.ExitCode == 0) return kandidat;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // Diesen Kandidaten gibt es nicht oder er laesst sich nicht starten.
        }
    }

    return null;
}

/// <summary>
/// Baut ein frei erfundenes Archiv mit genau den Sonderfaellen, die in echten Testdaten
/// fehlen: eine untergeschobene zweite objects.jsonl in einem Adapter-Unterordner sowie
/// JSON-Dateien, die gueltig, leer, kommentiert, mit BOM versehen, abgeschnitten oder sehr
/// gross sind. Es enthaelt keine Daten aus einer Anlage.
/// </summary>
static void ErzeugePruefarchiv(string ziel)
{
    var ordner = Path.GetDirectoryName(ziel)!;
    var bau = Path.Combine(ordner, "bau");
    if (Directory.Exists(ordner)) Directory.Delete(ordner, true);
    Directory.CreateDirectory(Path.Combine(bau, "backup", "files", "pruef.0"));
    Directory.CreateDirectory(Path.Combine(bau, "backup", "fremdadapter.0", "backup"));

    // Die echte Objektliste - oben im Archiv.
    File.WriteAllText(Path.Combine(bau, "backup", "objects.jsonl"),
        "{\"_id\":\"system.adapter.pruefadapter.0\",\"type\":\"instance\"," +
        "\"common\":{\"name\":\"pruefadapter\",\"version\":\"1.0.0\",\"enabled\":true},\"native\":{}}\n" +
        "{\"_id\":\"pruefadapter.0.echt\",\"type\":\"state\"," +
        "\"common\":{\"name\":\"echt\",\"type\":\"number\"},\"native\":{}}\n");

    File.WriteAllText(Path.Combine(bau, "backup", "states.jsonl"),
        "{\"id\":\"pruefadapter.0.echt\",\"state\":{\"val\":1,\"ack\":true,\"ts\":1786000000000," +
        "\"lc\":1786000000000,\"from\":\"system.adapter.pruefadapter.0\",\"q\":0}}\n");

    // Ein Adapter, der sein eigenes Backup mitsichert - frueher gewann diese Liste.
    File.WriteAllText(Path.Combine(bau, "backup", "fremdadapter.0", "backup", "objects.jsonl"),
        "{\"_id\":\"fremd.0.untergeschoben\",\"type\":\"state\"," +
        "\"common\":{\"name\":\"fremd\",\"type\":\"number\"},\"native\":{}}\n");

    var dateien = Path.Combine(bau, "backup", "files", "pruef.0");
    File.WriteAllText(Path.Combine(dateien, "gut.json"), "{\"a\":1,\"b\":[1,2,3]}");
    File.WriteAllText(Path.Combine(dateien, "kommentar.json"), "{\n// Kommentar\n\"a\":1\n}");
    File.WriteAllText(Path.Combine(dateien, "leer.json"), "");
    File.WriteAllBytes(Path.Combine(dateien, "bom.json"),
        new byte[] { 0xEF, 0xBB, 0xBF }.Concat("{\"a\":1}"u8.ToArray()).ToArray());
    File.WriteAllText(Path.Combine(dateien, "abgeschnitten.json"), "{\"a\":1,\"b\":[1,2");

    // Eine grosse, gueltige JSON - der Fall, der frueher ein Vielfaches ihrer Groesse an
    // Arbeitsspeicher belegte.
    using (var gross = new StreamWriter(Path.Combine(dateien, "gross.json")))
    {
        gross.Write("{\"werte\":[");
        for (var i = 0; i < 1_500_000; i++)
        {
            if (i > 0) gross.Write(',');
            gross.Write(i % 1000);
        }
        gross.Write("]}");
    }

    using (var fs = File.Create(ziel))
    using (var gz = new System.IO.Compression.GZipStream(fs, System.IO.Compression.CompressionLevel.Fastest))
    using (var tw = new TarWriter(gz, TarEntryFormat.Pax))
    {
        foreach (var datei in Directory.EnumerateFiles(bau, "*", SearchOption.AllDirectories)
                                       .OrderBy(x => x, StringComparer.Ordinal))
            tw.WriteEntry(datei, Path.GetRelativePath(bau, datei).Replace('\\', '/'));
    }

    Directory.Delete(bau, true);
}

/// <summary>
/// Alle Eintraege eines Archivs als Zeilen „Name | Groesse | Art | Pruefsumme".
///
/// Die Pruefsumme des Inhalts gehoert ausdruecklich dazu: Zwei Leser, die dieselben Namen
/// und Groessen melden, koennen die Daten trotzdem um einen Block verschoben liefern —
/// und genau das ist der Fehler, den man in einem Tar-Leser macht.
/// </summary>
static List<string> TarEintraege(string archiv, bool eigener)
{
    var zeilen = new List<string>();

    // Am Inhalt erkannt, nicht am Namen — dieselbe Regel wie im Loader. Dessen eigene
    // Erkennung ist bibliotheksintern, deshalb steht sie hier noch einmal in zwei Zeilen.
    var kennung = new byte[2];
    using (var probe = File.OpenRead(archiv)) probe.ReadExactly(kennung);
    var gepackt = kennung[0] == 0x1F && kennung[1] == 0x8B;

    using var datei = File.OpenRead(archiv);
    using Stream quelle = gepackt ? new GZipStream(datei, CompressionMode.Decompress) : datei;
    using var tar = eigener ? TarSource.OpenMinimal(quelle) : TarSource.Open(quelle);

    while (tar.GetNextEntry() is { } eintrag)
    {
        var summe = "-";

        if (eintrag.DataStream is { } daten)
        {
            using var sha = System.Security.Cryptography.SHA256.Create();
            summe = Convert.ToHexString(sha.ComputeHash(daten))[..16];
        }

        zeilen.Add($"{eintrag.Name.Replace('\\', '/')} | {eintrag.Length} | " +
                   $"{(eintrag.IsRegularFile ? "Datei" : "sonstiges")} | {summe}");
    }

    return zeilen;
}

/// <summary>
/// Zerlegt eine Vergleichszeile in „Name" und „alles Weitere" (Groesse, Art, Pruefsumme).
/// Gebraucht vom Tar-Vergleich, der beim Namen eine bekannte Schreibvariante zulaesst,
/// beim Inhalt aber nicht.
/// </summary>
static (string, string) Zerlegen(string zeile)
{
    var trenner = zeile.IndexOf(" | ", StringComparison.Ordinal);
    return trenner < 0 ? (zeile, "") : (zeile[..trenner], zeile[(trenner + 3)..]);
}
