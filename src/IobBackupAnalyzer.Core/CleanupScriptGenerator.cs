using System.Text;

namespace IobBackupAnalyzer.Core;

/// <summary>
/// Erzeugt ein Shell-Skript, das ausgewählte Waisen-States (Werte ohne Objekt) über die
/// ioBroker-CLI löscht.
///
/// Das Skript ist Text zum Kopieren — der Analyzer selbst löscht nichts und verbindet sich
/// nicht mit dem laufenden System. Ausgeführt wird es vom Nutzer auf dem ioBroker-Host.
///
/// <b>Warum Shell und nicht JavaScript-Adapter?</b> Frühere Fassungen erzeugten ein
/// JS-Skript mit <c>deleteState</c>. Das kann nicht funktionieren, und zwar aus einem
/// Grund, der hier genau benannt sein will: <c>deleteState</c> verlangt, dass zu der ID ein
/// <b>Objekt</b> existiert — zu einem Waisen-State existiert per Definition keines mehr.
/// Ohne Objekt fällt der Aufruf in den letzten Zweig und meldet „Not found", für
/// <i>jede</i> ID (gemessen: Fehler für jede einzelne, nichts gelöscht).
///
/// Die Adapter-Doku nennt zusätzlich eine Namensraum-Grenze — „States from other adapters
/// cannot be deleted" —, die im Quelltext etwas weiter gefasst ist als dort beschrieben
/// (<c>0_userdata.0.*</c> ist eingeschlossen). Am Ergebnis ändert das nichts: Über das
/// fehlende Objekt scheitert es ohnehin. <c>deleteObject</c> scheitert aus demselben Grund.
/// Nur <c>iobroker state delete &lt;id&gt;</c> greift auf die States-DB durch — ohne
/// Namensraum-Beschränkung und ohne ein Objekt vorauszusetzen.
///
/// Bewusst mit den <b>exakten IDs</b> aus dem Backup (kein Enumerieren, kein Muster): So
/// löscht das Skript genau das, was im Analyzer ausgewählt wurde. Der Trockenlauf
/// (<c>DRY_RUN</c>) listet nur; beim scharfen Lauf wird je ID der Exit-Code ausgewertet und
/// protokolliert.
///
/// <b>Der Modus wird beim Start abgefragt</b> (Anwenderwunsch), nicht mehr im Text
/// umgestellt: Wer mit der Linux-Kommandozeile wenig vertraut ist, sollte eine Datei ablegen und
/// starten müssen — nicht vorher eine Variable im Skript ändern. Jede Antwort außer einem
/// großen „J" führt zum Trockenlauf — bewusst mit Umschalttaste, damit ein versehentlicher
/// Tastendruck nicht löscht. Verglichen wird mit <c>[ "$antwort" = "J" ]</c> und nicht mit
/// <c>case</c>: Beim Mustervergleich entscheidet die Shell-Option <c>nocasematch</c> über
/// Groß- und Kleinschreibung, und die erbt ein Skript, das mit <c>source datei.sh</c> statt
/// mit <c>bash datei.sh</c> geholt wird — dann löschte auch ein kleines „j".
/// Ohne Terminal (Pipe, cron, Editor-Lauf)
/// wird gar nicht erst gefragt: dort gilt der Trockenlauf. Für den skriptbaren Fall gibt es
/// <c>--dry-run</c> und <c>--delete</c>.
/// </summary>
public static class CleanupScriptGenerator
{
    /// <summary>
    /// Zeilenenden für die gespeicherte Datei. Bash bricht bei CRLF sofort mit
    /// <c>$'\r': command not found</c> ab — deshalb schreibt der Speichern-Knopf reines LF,
    /// unabhängig davon, dass die Datei auf Windows entsteht.
    /// </summary>
    public const string LineEnding = "\n";

    /// <summary>Vorschlag für den Dateinamen; <paramref name="backupName"/> darf leer sein.</summary>
    public static string SuggestedFileName(string? backupName) =>
        string.IsNullOrWhiteSpace(backupName)
            ? "aufraeumen.sh"
            : "aufraeumen_" + backupName + ".sh";

    /// <summary>
    /// Wandelt das erzeugte Skript in die Form, die auf dem ioBroker-Host laufen muss:
    /// ausschließlich LF, kein BOM (das schreibt der Aufrufer über die Kodierung).
    /// </summary>
    public static string ForFile(string script) =>
        script.Replace("\r\n", "\n").Replace("\r", "\n");

    public static string Generate(IEnumerable<string> ids)
    {
        var list = (ids ?? Enumerable.Empty<string>())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

        var sb = new StringBuilder();

        sb.AppendLine("#!/bin/bash");
        sb.AppendLine("# ===================================================================");
        sb.AppendLine("#  Aufraeum-Skript: Waisen-States loeschen (Werte ohne Objekt)");
        // Mit Version: Eine gespeicherte .sh liegt oft noch Monate spaeter auf dem Host, und
        // aeltere Fassungen verhalten sich anders (bis v1.17.0 loeschte auch ein kleines "j").
        sb.AppendLine("#  Erzeugt vom " + AppIdentity.Name + " " + AppIdentity.Version + ".");
        sb.AppendLine("#  " + AppIdentity.AiNoticeAscii);
        sb.AppendLine("#  Vor dem scharfen Lauf selbst hineinsehen - es ist deine Anlage.");
        sb.AppendLine("#  Laeuft in der SHELL auf dem ioBroker-Host -");
        sb.AppendLine("#  NICHT im JavaScript-Adapter.");
        sb.AppendLine("#");
        sb.AppendLine("#  WARUM SHELL UND NICHT JAVASCRIPT?");
        sb.AppendLine("#    deleteState() im JavaScript-Adapter kann keine States fremder");
        sb.AppendLine("#    Adapter loeschen - laut Doku: \"States from other adapters cannot");
        sb.AppendLine("#    be deleted\". Gegen IDs fremder Adapter meldet es fuer JEDE ID");
        sb.AppendLine("#    \"Not found\", auch wenn der Wert sehr wohl existiert.");
        sb.AppendLine("#    deleteObject() hilft ebenfalls nicht: Zu einem Waisen-State gibt");
        sb.AppendLine("#    es per Definition kein Objekt mehr. Nur die ioBroker-CLI kommt an");
        sb.AppendLine("#    einen reinen State-Wert heran.");
        sb.AppendLine("#");
        sb.AppendLine("#  AUSFUEHREN:");
        sb.AppendLine("#    1. Diese Datei auf den ioBroker-Host kopieren, z. B. aufraeumen.sh");
        sb.AppendLine("#    2. bash aufraeumen.sh");
        sb.AppendLine("#       (kein chmod noetig - 'bash <datei>' startet sie auch ohne");
        sb.AppendLine("#        Ausfuehrrecht. Wer mag: chmod +x aufraeumen.sh && ./aufraeumen.sh)");
        sb.AppendLine("#");
        sb.AppendLine("#  DAS SKRIPT FRAGT SELBST, was es tun soll:");
        sb.AppendLine("#      Wirklich loeschen? [J = loeschen / n = nur Testlauf]");
        sb.AppendLine("#    J     = scharf. Loescht die aufgefuehrten Waisen-States endgueltig.");
        sb.AppendLine("#            GROSS geschrieben, also mit Umschalttaste - ein");
        sb.AppendLine("#            versehentlicher Tastendruck loescht so nichts.");
        sb.AppendLine("#    alles = Trockenlauf. Es wird NICHTS geloescht, nur ausgegeben,");
        sb.AppendLine("#    andere   was geloescht WUERDE. Auch die blosse Eingabetaste.");
        sb.AppendLine("#    Erst den Trockenlauf ansehen, dann scharf laufen lassen.");
        sb.AppendLine("#");
        sb.AppendLine("#  OHNE RUECKFRAGE (z. B. fuer cron oder eine Pipe):");
        sb.AppendLine("#    bash aufraeumen.sh --dry-run    nur auflisten");
        sb.AppendLine("#    bash aufraeumen.sh --delete     ohne Nachfrage loeschen");
        sb.AppendLine("#  Laeuft das Skript ohne Terminal und ohne diese Schalter, macht es");
        sb.AppendLine("#  von sich aus einen Trockenlauf - nie ein ungefragtes Loeschen.");
        sb.AppendLine("#");
        sb.AppendLine("#  VORHER: ein Backitup-Backup ziehen - dann ist alles umkehrbar.");
        sb.AppendLine("#");
        sb.AppendLine("#  DAUER: je ID ein CLI-Aufruf. Bei mehreren hundert IDs laeuft das");
        sb.AppendLine("#  entsprechend lange - einmal starten und warten, nicht abbrechen.");
        sb.AppendLine("# ===================================================================");
        sb.AppendLine();
        sb.AppendLine("# Standard ist der Trockenlauf. Umgestellt wird er nur durch --delete");
        sb.AppendLine("# oder eine ausdrueckliche Antwort auf die Rueckfrage weiter unten.");
        sb.AppendLine("DRY_RUN=true");
        sb.AppendLine("GEFRAGT=false   # true, sobald der Modus feststeht (Schalter oder Antwort)");
        sb.AppendLine();
        sb.AppendLine("# Schalter fuer den unbeaufsichtigten Lauf. Ein unbekanntes Argument wird");
        sb.AppendLine("# nicht stillschweigend ignoriert - sonst loeschte ein Tippfehler wie");
        sb.AppendLine("# '--dryrun' ungewollt scharf.");
        sb.AppendLine("while [ $# -gt 0 ]; do");
        sb.AppendLine("    case \"$1\" in");
        sb.AppendLine("        --dry-run|-n) DRY_RUN=true;  GEFRAGT=true ;;");
        sb.AppendLine("        --delete|-y)  DRY_RUN=false; GEFRAGT=true ;;");
        sb.AppendLine("        -h|--help)");
        sb.AppendLine("            echo \"Aufruf: bash $0 [--dry-run|--delete]\"");
        sb.AppendLine("            echo \"  ohne Schalter: fragt beim Start nach\"");
        sb.AppendLine("            echo \"  --dry-run      listet nur auf, loescht nichts\"");
        sb.AppendLine("            echo \"  --delete       loescht ohne Rueckfrage\"");
        sb.AppendLine("            exit 0 ;;");
        sb.AppendLine("        *)");
        sb.AppendLine("            echo \"Unbekanntes Argument: $1\"");
        sb.AppendLine("            echo \"Aufruf: bash $0 [--dry-run|--delete]\"");
        sb.AppendLine("            exit 1 ;;");
        sb.AppendLine("    esac");
        sb.AppendLine("    shift");
        sb.AppendLine("done");
        sb.AppendLine();
        sb.AppendLine("# Exakte Waisen-States (Werte ohne Objekt) aus dem Backup - im Analyzer ausgewaehlt.");
        sb.AppendLine("# Hinweis: Basis sind die Waisen aus dem Backup. Stelle sicher, dass die");
        sb.AppendLine("# betroffenen Adapter wirklich weg sind, bevor du DRY_RUN auf false stellst.");

        sb.AppendLine("IDS=(");
        if (list.Count == 0)
            sb.AppendLine("    # (noch keine Namensraeume ausgewaehlt)");
        else
            foreach (var id in list)
                sb.AppendLine("    " + ShellString(id));
        sb.AppendLine(")");

        sb.AppendLine();
        sb.AppendLine("gesamt=${#IDS[@]}");
        sb.AppendLine();
        sb.AppendLine("# Ohne Eintraege gibt es nichts zu fragen und nichts zu tun.");
        sb.AppendLine("if [ \"$gesamt\" -eq 0 ]; then");
        sb.AppendLine("    echo \"Dieses Skript enthaelt keine Eintraege - im Analyzer war nichts ausgewaehlt.\"");
        sb.AppendLine("    exit 0");
        sb.AppendLine("fi");
        sb.AppendLine();
        sb.AppendLine("# Rueckfrage, wenn kein Schalter den Modus schon festgelegt hat.");
        sb.AppendLine("# [ -t 0 ] ist die Probe, ob ueberhaupt jemand antworten kann: Haengt die");
        sb.AppendLine("# Eingabe an einer Pipe oder laeuft das Skript aus cron, wuerde 'read' sofort");
        sb.AppendLine("# leer zurueckkommen - dann bleibt es beim Trockenlauf.");
        sb.AppendLine("if [ \"$GEFRAGT\" != true ]; then");
        sb.AppendLine("    if [ -t 0 ]; then");
        sb.AppendLine("        echo \"$gesamt Waisen-States stehen in diesem Skript.\"");
        sb.AppendLine("        echo \"Loeschen ist endgueltig - vorher ein Backitup-Backup ziehen.\"");
        sb.AppendLine("        printf 'Wirklich loeschen? [J = loeschen / n = nur Testlauf]: '");
        sb.AppendLine("        read -r antwort");
        sb.AppendLine("        # Absichtlich ein Zeichenvergleich und kein 'case': Beim");
        sb.AppendLine("        # Mustervergleich haengt die Gross-/Kleinschreibung an der");
        sb.AppendLine("        # Shell-Option nocasematch - und die erbt das Skript, wenn es");
        sb.AppendLine("        # mit 'source datei.sh' oder '. datei.sh' aus einer Shell");
        sb.AppendLine("        # geholt wird, in der sie gesetzt ist. Dann wuerde auch ein");
        sb.AppendLine("        # kleines j loeschen. [ \"$a\" = \"J\" ] vergleicht dagegen immer");
        sb.AppendLine("        # Zeichen fuer Zeichen, unabhaengig von jeder Shell-Option.");
        sb.AppendLine("        if [ \"$antwort\" = \"J\" ] || [ \"$antwort\" = \"JA\" ]; then");
        sb.AppendLine("            DRY_RUN=false");
        sb.AppendLine("        else");
        sb.AppendLine("            DRY_RUN=true");
        sb.AppendLine("        fi");
        sb.AppendLine("        echo");
        sb.AppendLine("    else");
        sb.AppendLine("        echo \"Kein Terminal fuer die Rueckfrage - es wird nur ein Testlauf gemacht.\"");
        sb.AppendLine("        echo \"Zum Loeschen: bash $0 --delete\"");
        sb.AppendLine("        DRY_RUN=true");
        sb.AppendLine("    fi");
        sb.AppendLine("fi");
        sb.AppendLine();
        sb.AppendLine("if [ \"$DRY_RUN\" = true ]; then");
        sb.AppendLine("    echo \"TESTLAUF - es wird nichts geloescht.\"");
        sb.AppendLine("fi");
        sb.AppendLine();
        sb.AppendLine("# Ohne die CLI laeuft nichts - lieber sofort sauber abbrechen als lauter Fehler.");
        sb.AppendLine("# Nur beim scharfen Lauf: Der Trockenlauf listet bloss und darf ueberall laufen,");
        sb.AppendLine("# auch auf dem Rechner, auf dem der Analyzer laeuft.");
        sb.AppendLine("if [ \"$DRY_RUN\" != true ] && ! command -v iobroker > /dev/null 2>&1; then");
        sb.AppendLine("    echo \"FEHLER: 'iobroker' nicht gefunden. Dieses Skript gehoert auf den\"");
        sb.AppendLine("    echo \"       ioBroker-Host, nicht in den JavaScript-Adapter.\"");
        sb.AppendLine("    exit 1");
        sb.AppendLine("fi");
        sb.AppendLine();
        sb.AppendLine("if [ \"$DRY_RUN\" != true ]; then");
        sb.AppendLine("    echo \"Loesche $gesamt Eintraege. Je Eintrag ein CLI-Aufruf -\"");
        sb.AppendLine("    echo \"das dauert bei mehreren hundert IDs etliche Minuten. Bitte laufen lassen.\"");
        sb.AppendLine("fi");
        sb.AppendLine();
        sb.AppendLine("n=0; geloescht=0; fehler=0");
        sb.AppendLine("for id in \"${IDS[@]}\"; do");
        sb.AppendLine("    n=$((n + 1))");
        sb.AppendLine("    if [ \"$DRY_RUN\" = true ]; then");
        sb.AppendLine("        echo \"WUERDE LOESCHEN: $id\"");
        sb.AppendLine("        continue");
        sb.AppendLine("    fi");
        sb.AppendLine("    # Lebenszeichen alle 25 Eintraege: ohne das sieht ein langer Lauf aus,");
        sb.AppendLine("    # als haenge er - und wird womoeglich mittendrin abgebrochen.");
        sb.AppendLine("    if [ $((n % 25)) -eq 0 ]; then");
        sb.AppendLine("        echo \"  $n/$gesamt   (geloescht: $geloescht, Fehler: $fehler)\"");
        sb.AppendLine("    fi");
        sb.AppendLine("    # Loeschfehler je ID abfangen, damit man sie sieht statt eines stillen Fehlschlags.");
        sb.AppendLine("    if iobroker state delete \"$id\" > /dev/null 2>&1; then");
        sb.AppendLine("        geloescht=$((geloescht + 1))");
        sb.AppendLine("    else");
        sb.AppendLine("        fehler=$((fehler + 1))");
        sb.AppendLine("        echo \"FEHLER bei $id\"");
        sb.AppendLine("    fi");
        sb.AppendLine("done");
        sb.AppendLine();
        sb.AppendLine("if [ \"$DRY_RUN\" = true ]; then");
        sb.AppendLine("    echo \"Fertig. $n Eintraege   (DRY_RUN - es wurde nichts geloescht)\"");
        sb.AppendLine("else");
        sb.AppendLine("    echo \"Fertig. $n Eintraege   geloescht: $geloescht, Fehler: $fehler\"");
        sb.AppendLine("fi");

        return sb.ToString();
    }

    /// <summary>
    /// Shell-String-Literal mit einfachem Anführungszeichen. In einfachen Anführungszeichen
    /// ist für die Shell jedes Zeichen literal — nur das Anführungszeichen selbst muss aus
    /// dem Literal heraus- und als <c>\'</c> wieder hineingeführt werden.
    /// </summary>
    private static string ShellString(string s) =>
        "'" + s.Replace("'", "'\\''") + "'";
}
