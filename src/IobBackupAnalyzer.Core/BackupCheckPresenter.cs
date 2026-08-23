using System.Text.RegularExpressions;

namespace IobBackupAnalyzer.Core;

/// <summary>Einstufung einer Prüfzeile — bestimmt, wie die Oberfläche sie hervorhebt.</summary>
public enum CheckSeverity
{
    /// <summary>Datei ist in Ordnung.</summary>
    Ok,
    /// <summary>Beschädigt — das ist der Befund, auf den es ankommt.</summary>
    Problem,
    /// <summary>Kein Fehler, aber erwähnenswert (z. B. fehlende states.jsonl).</summary>
    Info
}

/// <summary>Eine Zeile der Backup-Prüfung.</summary>
public sealed record CheckRow(string File, string Kind, string Result, string Detail,
                              CheckSeverity Severity);

/// <summary>
/// UI-neutrale Logik des Tabs „Backup-Prüfung": Gesamturteil, Zusammenfassung und die
/// Prüfzeilen. Geteilt von WinForms- und Avalonia-Oberfläche — siehe
/// <see cref="OverviewPresenter"/> zur Begründung.
///
/// Farben bleiben bewusst außen vor: Der Presenter liefert die <see cref="CheckSeverity"/>,
/// die Darstellung entscheidet jede Oberfläche für sich.
/// </summary>
public static class BackupCheckPresenter
{
    public static readonly string[] Columns = { "Datei", "Art", "Ergebnis", "Details" };

    /// <summary>Gesamturteil in Worten, z. B. „Backup gültig".</summary>
    public static string VerdictText(BackupData data) => data.Validation.HealthText;

    /// <summary>
    /// Zwei Zeilen Kontext: was geprüft wurde und wie geprüft wurde.
    /// Zeilentrenner ist <c>\n</c>.
    /// </summary>
    public static string SummaryText(BackupData data)
    {
        var v = data.Validation;
        var skipped = v.SkippedObjects > 0 ? $"   ·   {v.SkippedObjects} Objekte übersprungen" : "";

        return $"{Path.GetFileName(data.SourceFile)}   ·   Backup vom {data.CreatedAt:dd.MM.yyyy HH:mm}   ·   " +
               $"{data.Objects.Count:N0} Objekte   ·   {data.StateCount:N0} States   ·   " +
               $"{v.OptionalCount} optionale JSON-Datei(en) geprüft{skipped}\n" +
               "Geprüft wird wie beim „iobroker backup\": objects.jsonl/states.jsonl zeilenweise (Pflicht), " +
               "files/**/*.json strikt (optional). //-Kommentare gelten als ungültig.";
    }

    /// <summary>
    /// Alle Prüfzeilen: zuerst die beiden Pflichtdateien, dann die optionalen JSON-Dateien
    /// aus dem files/-Baum — kaputte zuerst, damit sie nicht in der Liste untergehen.
    /// </summary>
    public static List<CheckRow> BuildRows(BackupData data)
    {
        var v = data.Validation;
        var rows = new List<CheckRow>
        {
            new("objects.jsonl", "Pflicht", v.Objects.StatusText, v.Objects.Detail,
                v.Objects.Ok ? CheckSeverity.Ok : CheckSeverity.Problem),

            // Eine fehlende states.jsonl ist bei älteren Backitup-Ständen normal —
            // das ist eine Information, kein Schaden.
            new("states.jsonl", "Pflicht", v.States.StatusText, v.States.Detail,
                v.States.Ok ? CheckSeverity.Ok
                            : v.States.Present ? CheckSeverity.Problem : CheckSeverity.Info)
        };

        // Gefundene, aber nicht verwendete Pflichtdateien zuerst: Wer ein Archiv vor sich
        // hat, in dem eine zweite objects.jsonl steckt, soll das erfahren, bevor er sich
        // über Zahlen wundert. Ein Befund ist es nicht — die richtige Datei wurde ja
        // genommen —, aber eine Auskunft, die man kennen will.
        foreach (var doppelt in v.IgnoredDuplicates)
            rows.Add(new CheckRow(doppelt, "Übergangen", "nicht verwendet",
                                  "Diese Datei trägt den Namen einer Pflichtdatei, liegt aber nicht "
                                + "oben im Archiv. Sie stammt vermutlich aus einem mitgesicherten "
                                + "fremden Backup und wurde deshalb nicht verwendet — gelesen wurde "
                                + "die Datei aus dem Wurzelordner.",
                                  CheckSeverity.Info));

        foreach (var f in v.OptionalFiles
                     .OrderBy(f => f.Valid)
                     .ThenBy(f => f.ShortPath, StringComparer.OrdinalIgnoreCase))
            rows.Add(new CheckRow(f.ShortPath, "Optional", f.StatusText,
                                  f.Valid ? "OK"
                                          : FriendlyError(f.Error) + "  ·  " + WhereToFind(f.ShortPath),
                                  f.Valid ? CheckSeverity.Ok : CheckSeverity.Problem));

        return rows;
    }

    /// <summary>Verzeichnis der Datei-Datenbank auf einem Standard-ioBroker-Host.</summary>
    private const string FileDbRoot = "/opt/iobroker/iobroker-data/files/";

    /// <summary>
    /// Fundort einer beschädigten Datei, kurz genug für die Details-Spalte.
    ///
    /// Steht bewusst in jeder Zeile und nicht nur im Reparaturhinweis darunter: Der
    /// CSV-Export enthält ausschließlich die Tabelle. Wer die CSV beim Nachfragen
    /// weitergibt — der Normalfall — hätte den Fundort sonst nirgends stehen.
    ///
    /// Der Zusatz zum Experten-Modus ist keine Höflichkeit: Ohne ihn zeigt der
    /// Datei-Browser die *.admin-Ordner nicht an, der genannte Ordner fehlt also
    /// schlicht in der Liste — was wie ein falscher Fundort aussieht.
    /// </summary>
    public static string WhereToFind(string shortPath) =>
        $"Fundort: {FileDbRoot}{shortPath} " +
        $"(im Admin: Tab „Dateien\" → {shortPath.Split('/')[0]} — Experten-Modus nötig)";

    /// <summary>Nur die beschädigten Dateien — für den Schalter „Nur Probleme anzeigen".</summary>
    public static List<CheckRow> OnlyProblems(IEnumerable<CheckRow> rows) =>
        rows.Where(r => r.Severity == CheckSeverity.Problem).ToList();

    /// <summary>Stelle im Text, an der .NET Zeile und Position meldet.</summary>
    private static readonly Regex JsonPosition =
        new(@"LineNumber:\s*(\d+)\s*\|\s*BytePositionInLine:\s*(\d+)", RegexOptions.Compiled,
            RegexLimits.MatchTimeout);

    /// <summary>
    /// Übersetzt die Fehlermeldung des JSON-Lesers in einen Satz, mit dem man etwas
    /// anfangen kann.
    ///
    /// <b>Wichtig — die Zeilennummer wird umgerechnet:</b> .NET zählt Zeilen und Spalten
    /// ab 0, jeder Editor ab 1. Unverändert weitergereicht schickt die Meldung den Nutzer
    /// also eine Zeile zu weit nach oben, wo meist etwas völlig Harmloses steht. Gemessen
    /// am Praxisfall: gemeldet „LineNumber: 2", tatsächlich stand der Kommentar in Zeile 3.
    /// </summary>
    public static string FriendlyError(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return raw;

        // Bei einer leeren Datei wäre „Zeile 1, Zeichen 1" nur Ballast — es gibt keine
        // Stelle, an der man nachsehen könnte.
        if (raw.Contains("does not contain any JSON tokens"))
            return "Die Datei ist leer oder enthält nur Leerzeichen — das ist kein gültiges JSON.";

        var where = "";
        try
        {
            var m = JsonPosition.Match(raw);
            if (m.Success
                && int.TryParse(m.Groups[1].Value, out var line)
                && int.TryParse(m.Groups[2].Value, out var col))
                where = $"Zeile {line + 1}, Zeichen {col + 1}: ";
        }
        catch (RegexMatchTimeoutException)
        {
            // Ohne Fundstelle ist die Meldung ärmer, aber immer noch eine Meldung.
        }

        // Die Fälle, die in ioBroker-Backups tatsächlich vorkommen. Alles andere behält
        // den Originaltext — lieber unübersetzt als falsch übersetzt.
        var what = raw switch
        {
            _ when raw.Contains("'/' is an invalid start of a property name")
                || raw.Contains("'/' is an invalid start of a value")
                => "JSON erlaubt keine Kommentare — hier steht ein // oder /* */.",

            _ when raw.Contains("trailing comma")
                => "Ein Komma zu viel vor der schließenden Klammer.",

            _ when raw.Contains("is an invalid start of a value")
                => "Unerwartetes Zeichen — an dieser Stelle wird ein Wert erwartet.",

            _ when raw.Contains("Expected a ','")
                => "Hier fehlt ein Komma zwischen zwei Einträgen.",

            _ when raw.Contains("is invalid after a value")
                => "Nach einem Wert steht ein Zeichen, das dort nicht hingehört.",

            // Null-Bytes deuten auf UTF-16 statt UTF-8: Jedes zweite Byte ist dann 0x00.
            _ when raw.Contains("'0x00' is an invalid")
                => "Die Datei ist nicht UTF-8-kodiert (sie enthält Null-Bytes, typisch für " +
                   "UTF-16). ioBroker erwartet UTF-8 ohne BOM.",

            _ => raw
        };

        // War nichts zu übersetzen, steht die Position schon im Originaltext.
        return ReferenceEquals(what, raw) ? raw : where + what;
    }

    /// <summary>
    /// Konkrete Handlungsanweisung, wenn optionale JSON-Dateien beschädigt sind — oder
    /// <c>null</c>, wenn es nichts zu tun gibt.
    ///
    /// Der entscheidende Punkt steht bewusst zuerst: Diese Dateien liegen in ioBrokers
    /// Datei-Datenbank, nicht im Dateisystem. Wer sie unter
    /// <c>/opt/iobroker/node_modules/…</c> mit einem Editor repariert, ändert eine andere
    /// Datei — der Fehler bleibt beim nächsten Backup bestehen. Genau diese Falle hat
    /// in der Praxis schon mehrere Anläufe gekostet.
    ///
    /// <paramref name="backupCreatedAt"/> wird nur genannt, um die Sicherungsfrage zu
    /// beantworten, die sich beim Löschen eigener Inhalte stellt: Wer hier steht, hat ein
    /// Backup vor sich — die einzige offene Frage ist, ob es aktuell genug ist.
    /// </summary>
    public static string? RepairHint(IEnumerable<CheckRow> rows, bool archiveTruncated = false,
                                     DateTime? backupCreatedAt = null)
    {
        // Ein unvollständiges Archiv hat Vorrang: Was fehlt, kann man nicht reparieren —
        // nur ersetzen. Alles andere daran zu erklären wäre irreführend.
        if (archiveTruncated)
            return "Was tun? Das Archiv endet mitten im Datenstrom — die Datei ist " +
                   "unvollständig. Fast immer steckt ein abgebrochener Download oder " +
                   "Kopiervorgang dahinter, seltener ein voller Datenträger beim Erstellen.\n\n" +
                   "    1. Dateigröße mit dem Original vergleichen (auf dem ioBroker-Host " +
                   "liegen die Backups unter /opt/iobroker/backups)\n" +
                   "    2. Ist das Original vollständig: Datei erneut übertragen, besser " +
                   "per scp/sftp als über eine Weboberfläche\n" +
                   "    3. Ist schon das Original zu klein: Speicherplatz prüfen (df -h) " +
                   "und ein neues Backup erstellen (iobroker backup)\n\n" +
                   "Ein abgeschnittenes Archiv lässt sich nicht reparieren. Es taugt auch " +
                   "nicht als Notnagel: Die Prüfung oben bezieht sich nur auf den Teil, " +
                   "der noch lesbar war.";

        var broken = rows.Where(r => r.Severity == CheckSeverity.Problem
                                  && r.Kind == "Optional").ToList();
        if (broken.Count == 0) return null;

        var text = "Was tun? Diese Dateien liegen in ioBrokers Datei-Datenbank, nicht im " +
                   "Dateisystem. Sie dort allein zu reparieren genügt NICHT — die Datenbank " +
                   "merkt davon nichts.\n\n" +
                   "Wo die Dateien zu finden sind: im Admin im Tab „Dateien\" (in der linken " +
                   "Navigation; die Reihenfolge dort ist einstellbar). Der Experten-Modus ist " +
                   "nötig — ohne ihn " +
                   "blendet der Datei-Browser die *.admin-Ordner aus, und der betroffene Ordner " +
                   "taucht gar nicht erst in der Liste auf. Auf dem Host liegen sie hier:\n";

        foreach (var r in broken.Take(3))
            text += $"    {FileDbRoot}{r.File}\n";

        if (broken.Count > 3)
            text += $"    … und {broken.Count - 3} weitere Datei(en)\n";

        // Diese Aufklärung ist der eigentliche Grund für den Abschnitt: BackitUp meldet
        // einen Pfad, unter dem garantiert nichts zu finden ist. In der Praxis hat
        // genau das mehrere Leute in die Irre laufen lassen — sie
        // suchten im leeren tmp-Ordner und hielten die Meldung für einen Fehlalarm.
        text += "\nDer Pfad aus der BackitUp-Meldung führt in die Irre: " +
                "…/iobroker.js-controller/tmp/backup/files/… ist nur ein Arbeitsverzeichnis, " +
                "das während des Backups gefüllt und danach geleert wird. Dort ist nichts " +
                "zu finden — das ist normal und kein zweiter Fehler.\n\n" +
                RiskHint(broken, backupCreatedAt) +
                "Reparieren lässt sich das auf drei Wegen:\n\n" +
                "Weg 1 (am einfachsten, ganz ohne Kommandozeile): Datei im Admin löschen — " +
                "im selben Tab „Dateien\", in dem sie oben zu finden ist, über das " +
                "Papierkorb-Symbol in der Dateizeile. Das genügt, wenn die Datei im laufenden " +
                "Betrieb nichts zu suchen hat; eine tsconfig.json etwa ist eine reine " +
                "Entwicklerdatei, die kein Adapter zum Laufen braucht.\n\n" +
                // Häufige Rückfrage aus der Praxis. Das Installationsskript legt Symlinks
                // /usr/bin/iobroker und /usr/bin/iob an (unter FreeBSD/macOS in
                // /usr/local/bin) — ein „cd /opt/iobroker" ist also unnötig. Nur bei alten
                // oder von Hand aufgesetzten Installationen fehlt der Symlink.
                "Die beiden anderen Wege laufen auf der Kommandozeile des ioBroker-Hosts. " +
                "Ein Verzeichniswechsel ist nicht nötig: „iobroker\" (und die Kurzform " +
                "„iob\") sind systemweit verfügbar, egal wo man steht. Nur falls die Shell " +
                "„command not found\" meldet, fehlt der Symlink — dann stattdessen " +
                "„cd /opt/iobroker\" und die Befehle mit „./iobroker …\" aufrufen.\n\n" +
                "Weg 2: Datei korrigieren und neu einspielen.\n";

        // Der Upload speist sich aus dem Installationsordner des Adapters. Für eigene
        // Inhalte gibt es dort keine Quelldatei — „iobroker upload vis-2" schriebe die
        // Adapterdateien neu und ließe die kaputte View unangetastet. Ein Befehl, der
        // wirkungslos ist, aber nach Lösung aussieht, ist schlimmer als gar keiner.
        var adapterOwned = broken.Where(r => IsAdapterOwned(r.File)).ToList();
        var ownContent = broken.Where(r => !IsAdapterOwned(r.File)).ToList();

        if (adapterOwned.Count > 0)
        {
            text += "\nFür die Dateien aus den *.admin-Ordnern: im Adapter-Ordner auf dem " +
                    "Host korrigieren — Kommentare raus, das ist meist die ganze Ursache — " +
                    "und danach neu hochladen. Der Upload überträgt den Dateistand in die " +
                    "Datenbank; ohne die Korrektur davor schreibt er den Fehler nur erneut " +
                    "hinein. Er ist dabei nicht wählerisch: Er überträgt ALLE Dateien dieses " +
                    "Adapters, nicht nur die beanstandete — eigene Anpassungen an anderen " +
                    "Dateien desselben Adapters werden dabei überschrieben:\n";

            foreach (var adapter in adapterOwned.Take(3).Select(r => AdapterOf(r.File))
                                                .Where(a => a.Length > 0)
                                                .Distinct(StringComparer.OrdinalIgnoreCase))
                text += $"    iobroker upload {adapter}\n";
        }

        if (ownContent.Count > 0)
            text += "\nFür deine eigenen Inhalte gibt es keinen Upload-Befehl — sie stehen " +
                    "in keinem Adapter-Ordner, aus dem er sie holen könnte. Hier führt nur " +
                    "der Weg über den Admin: im Tab „Dateien\" die Datei herunterladen, den " +
                    "Fehler im Editor beheben und sie an derselben Stelle wieder hochladen.\n";

        text += "\nWeg 3: dasselbe Löschen wie Weg 1, nur als Befehl — praktisch, wenn " +
                "gleich mehrere Dateien betroffen sind:\n";

        // Führender Schrägstrich wie in den Usage-Beispielen des js-controllers
        // („file rm /vis-2.0/main/img/picture.png"). Der Befehl nimmt den ganzen Pfad als
        // EIN Argument und trennt davon das erste Segment als Meta-ID ab — hier also
        // „esphome.admin". Ohne Slash funktioniert es zwar auch, weicht aber von der
        // dokumentierten Schreibweise ab.
        foreach (var r in broken.Take(3))
            text += $"    iobroker file rm /{r.File}\n";

        if (broken.Count > 3)
            text += $"    … und {broken.Count - 3} weitere Datei(en)\n";

        text += "\nDas Backup selbst ist trotzdem einspielbar — die Pflichtdateien sind in Ordnung. " +
                "Kehrt der Fehler nach einem Adapter-Update zurück, gehört er dem Adapter-Autor gemeldet.";

        return text;
    }

    /// <summary>
    /// Einschätzung, wie riskant das Reparieren ist — und ob vorher ein Backup nötig ist.
    ///
    /// <b>Warum das entscheidend ist:</b> Die Prüfung erfasst alle JSON-Dateien unter
    /// <c>files/</c>. Darunter fallen zwei völlig verschiedene Dinge. Ein
    /// <c>*.admin</c>-Ordner ist eine Kopie aus dem Installationsordner des Adapters —
    /// „iobroker upload" legt ihn jederzeit neu an, Löschen kostet also nichts. Ein
    /// Instanz-Namensraum (<c>vis-2.0/</c>, <c>javascript.0/</c> …) enthält dagegen die
    /// Inhalte des Nutzers; eine <c>vis-views.json</c> ist eine komplette Ansicht. Die
    /// steht in keinem Installationsordner und ist nach dem Löschen weg.
    ///
    /// Deshalb wird nur der <c>*.admin</c>-Fall als gefahrlos ausgewiesen. Alles andere
    /// bekommt die Warnung, auch wenn es im Einzelfall doch Adapter-Inhalt sein sollte:
    /// Eine Warnung zu viel kostet ein paar Sekunden, eine Entwarnung zu viel eine View.
    /// </summary>
    private static string RiskHint(IReadOnlyList<CheckRow> broken, DateTime? backupCreatedAt)
    {
        var own = broken.Where(r => !IsAdapterOwned(r.File)).ToList();

        if (own.Count == 0)
            return "Wie riskant ist das? Hier kaum: Betroffen sind ausschließlich " +
                   "*.admin-Ordner. Deren Inhalt gehört dem Adapter und ist nur eine Kopie " +
                   "aus dessen Installationsordner — „iobroker upload\" stellt ihn jederzeit " +
                   "wieder her. Zu verlieren gibt es dabei nichts.\n\n";

        var text = "Wie riskant ist das? Hier ist Vorsicht nötig. Diese Datei(en) liegen " +
                   "NICHT in einem *.admin-Ordner, sondern in einem Instanz-Namensraum:\n";

        foreach (var r in own.Take(3))
            text += $"    {r.File}\n";

        if (own.Count > 3)
            text += $"    … und {own.Count - 3} weitere\n";

        text += "\nDort liegen deine eigenen Inhalte — VIS-Ansichten, hochgeladene Dateien, " +
                "Konfigurationen. Sie stammen aus keinem Adapter-Installationsordner: " +
                "„iobroker upload\" bringt sie nicht zurück, Löschen ist endgültig. Eine " +
                "vis-views.json etwa ist eine komplette Ansicht. Löschen (Weg 1 und 3) " +
                "heißt hier also: Inhalt weg — für diese Dateien ist Weg 2 der richtige.\n\n" +
                "Sorge vorher für ein aktuelles Backup — über BackitUp oder „iobroker " +
                "backup\" auf dem Host. " + BackupAtHand(backupCreatedAt) + "\n\n";

        return text;
    }

    /// <summary>
    /// Der Satz zum Backup, das der Nutzer gerade geöffnet hat. Ohne verwertbares Datum
    /// bleibt er allgemein — ein erfundenes oder leeres Datum wäre schlimmer als keins.
    /// </summary>
    private static string BackupAtHand(DateTime? createdAt) =>
        createdAt is { } d && d != default
            ? $"Das hier geprüfte Backup vom {d:dd.MM.yyyy HH:mm} taugt dazu nur, wenn es " +
              "deinem heutigen Stand entspricht."
            : "Das hier geprüfte Backup taugt dazu nur, wenn es deinem heutigen Stand " +
              "entspricht.";

    /// <summary>
    /// true, wenn die Datei dem Adapter gehört und sich per „iobroker upload" jederzeit
    /// wiederherstellen lässt — erkennbar am Meta-Ordner <c>&lt;adapter&gt;.admin</c>.
    /// </summary>
    private static bool IsAdapterOwned(string shortPath) =>
        shortPath.Split('/')[0].EndsWith(".admin", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Adaptername aus einem Dateipfad der Prüfung: aus „esphome.admin/tsconfig.json"
    /// wird „esphome". Leerer String, wenn sich nichts Sinnvolles ableiten lässt.
    /// </summary>
    private static string AdapterOf(string shortPath)
    {
        var ns = shortPath.Split('/')[0];
        var dot = ns.IndexOf('.');
        return dot > 0 ? ns[..dot] : "";
    }

    /// <summary>Eine Prüfzeile als Tabellen-/CSV-Zeile, Reihenfolge wie <see cref="Columns"/>.</summary>
    public static string[] Row(CheckRow r) => new[] { r.File, r.Kind, r.Result, r.Detail };
}
