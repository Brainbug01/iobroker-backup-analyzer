namespace IobBackupAnalyzer.Core;

/// <summary>Art eines Hilfe-Absatzes — bestimmt die Formatierung in der Oberfläche.</summary>
public enum HelpBlockKind
{
    /// <summary>Titel der Hilfe.</summary>
    Title,
    /// <summary>Abschnittsüberschrift.</summary>
    Heading,
    /// <summary>Fließtext. Kann mit <c>\n</c> getrennte Aufzählungszeilen enthalten.</summary>
    Text
}

/// <summary>Ein Absatz der Hilfe.</summary>
public sealed record HelpBlock(HelpBlockKind Kind, string Text);

/// <summary>
/// Der vollständige Text der In-App-Hilfe, als Struktur statt als fertig formatierter
/// Text. Beide Oberflächen rendern daraus ihre eigene Darstellung.
///
/// <b>Warum hier?</b> Dieser Text wächst mit jeder Funktion. Läge er je Oberfläche
/// getrennt vor, würde er zwangsläufig auseinanderlaufen — und eine Hilfe, die etwas
/// anderes behauptet als das Programm tut, ist schlimmer als keine.
/// </summary>
public static class HelpContent
{
    /// <summary>
    /// Platzhalter, den <see cref="Resolve"/> durch den tatsächlichen Ablageort des
    /// Ladeprotokolls ersetzt.
    ///
    /// <b>Warum nicht gleich der Pfad?</b> Die Blockliste ist statisch; den Pfad dort
    /// einzusetzen hieße, beim bloßen Lesen der Hilfe eine Schreibprobe auf die Platte
    /// auszulösen. Ersetzt wird deshalb erst beim Anzeigen.
    /// </summary>
    public const string LogPlaceholder = "{ladeprotokoll}";

    /// <summary>
    /// Setzt die Platzhalter eines Hilfetextes ein. Beide Oberflächen rufen das beim
    /// Aufbauen der Hilfe auf — ein Pfad, den man nur ungefähr beschreibt, hilft niemandem
    /// beim Suchen.
    /// </summary>
    public static string Resolve(string text) =>
        text.Contains(LogPlaceholder, StringComparison.Ordinal)
            ? text.Replace(LogPlaceholder, LoadLog.DefaultPath(), StringComparison.Ordinal)
            : text;

    public static IReadOnlyList<HelpBlock> Blocks { get; } = new HelpBlock[]
    {
        new(HelpBlockKind.Title, "ioBroker Backup Analyzer — Hilfe"),

        new(HelpBlockKind.Heading, "Mit KI erstellt"),
        new(HelpBlockKind.Text, AppIdentity.AiNoticeLong),

        new(HelpBlockKind.Text,
            "Dieses Werkzeug liest ein ioBroker-Backup (Backitup) offline ein und wertet es aus. " +
            "Es ist ein reines Lesewerkzeug: Es schreibt nichts zurück, verbindet sich nicht mit " +
            "deinem laufenden System und kann daran nichts verändern. Alle „Löschen\"-Themen liefern " +
            "höchstens Prüflisten oder fertigen Skripttext zum Kopieren — gelöscht wird nur, was du " +
            "selbst im ioBroker ausführst."),

        new(HelpBlockKind.Heading, "Backup laden"),
        new(HelpBlockKind.Text,
            "Oben per „Backup öffnen …\" oder Datei ins Fenster ziehen. Erkannt werden Voll-Backups " +
            "(backupiobroker_*.tar.gz), JavaScript-Backups (javascripts_*.tar.gz) und entpackte " +
            "Einzeldateien (objects.jsonl, backup.json, script.json). Ein reines Skript-Backup schaltet " +
            "nur den Tab „Skripte\" frei; alle anderen Auswertungen brauchen ein Voll-Backup."),

        new(HelpBlockKind.Heading, "Farben in den Tabellen"),
        new(HelpBlockKind.Text,
            "• Grau = unauffällig / deaktiviert / bereits als „verwendet\" erkannt.\n" +
            "• Orange = Aufmerksamkeit: ein Kandidat, der sich aber zuletzt noch geändert hat, oder eine " +
            "Störung/nicht quittierter Befehl.\n" +
            "• Rot = Problem: ein toter Verweis (fehlender Datenpunkt, kaputter Alias) oder ein seit " +
            "langem toter Datenpunkt."),

        new(HelpBlockKind.Heading, "Tab „Übersicht\""),
        new(HelpBlockKind.Text,
            "Kennzahlen des Backups und alle Adapter-Instanzen mit Version, Status und Objektzahl. " +
            "Unten „Installierte Adapter ohne eigene Instanz\": eine Bestandsaufnahme, keine Löschliste — " +
            "manche Adapter (z. B. Socket-Backends wie ws/socketio) laufen bewusst ohne eigene Instanz."),
        new(HelpBlockKind.Text,
            "„Aktiviert\" und „Betriebsart\" gehören zusammen. Nur ein Adapter im Dauerbetrieb läuft " +
            "ständig; die übrigen Betriebsarten sind ebenso vorgesehen: „nach Zeitplan\" startet zu " +
            "festen Zeiten und beendet sich wieder, „einmalig\" läuft einmal, „startet nicht\" nie, " +
            "und „in anderem Adapter\" läuft innerhalb eines fremden Prozesses. Ein „Nein\" unter " +
            "„Aktiviert\" ist bei diesen Betriebsarten also kein Befund. Dasselbe gilt für „nur " +
            "Dateien\": Solche Instanzen liefern Widgets oder Symbole aus und haben nichts zu starten."),
        new(HelpBlockKind.Text,
            "Die Spalte „Zeitplan\" zeigt den Plan, der zur Betriebsart gehört: bei „nach Zeitplan\" " +
            "die Ausführungszeiten, sonst einen geplanten Neustart. Beides steht im Admin nur im " +
            "Experten-Modus. Leer heißt: kein Plan hinterlegt."),
        new(HelpBlockKind.Text,
            "Hat eine Instanz mehr Objekte, als ihr Limit erlaubt, erscheint über dem Filter eine " +
            "Warnzeile und die betroffene Objektzahl wird orange. Dieselbe Grenze zieht ioBroker " +
            "selbst: Der js-controller meldet bei jedem Start einer solchen Instanz " +
            "„This instance has N objects, the limit for this instance is set to M.\" und legt " +
            "eine System-Meldung an. Vorgabe sind 5.000 Objekte je Instanz; manche Adapter bringen " +
            "einen eigenen Wert mit. Das ist eine Leistungswarnung, kein Defekt — viele Objekte " +
            "verlangsamen Start, Admin und Backup. Ein von Hand hochgesetztes Limit sieht der " +
            "Analyzer allerdings nicht: Es steht im Wert des Datenpunkts, und Werte liest er " +
            "bewusst nicht mit."),

        new(HelpBlockKind.Text,
            "Zwei der Hinweise hängen nicht am Aufbau, sondern an den beiden Schaltern unter dem " +
            "Zahnrad des Editors und gelten deshalb für jede Sprache. „Debug-Modus aktiv\" ist der " +
            "wichtigere: Der Haken „Debuggen\" ist kein Protokollschalter — der javascript-Adapter " +
            "unterdrückt damit jede schreibende Operation (setState, exec, writeFile) und " +
            "protokolliert sie nur als Warnung. Das Skript läuft und bewirkt nichts. " +
            "„Ausführliche Protokollausgaben\" füllt dagegen nur das Protokoll."),
        new(HelpBlockKind.Text,
            "Ebenfalls geprüft wird der Unterschied zwischen „Zustand steuern\" und „Zustand " +
            "aktualisieren\". Steuern schreibt unquittiert (ack=false) — das ist ein Befehl, auf den " +
            "ein Adapter reagiert und den er quittiert, sobald er ihn ausgeführt hat. Aktualisieren " +
            "schreibt quittiert (ack=true), also eine reine Wertmeldung. Daraus folgt: " +
            "Adapter-Datenpunkte steuern, selbst angelegte Datenpunkte (0_userdata, javascript) " +
            "aktualisieren. Gemeldet wird beides falsch herum — „steuern\" auf einem eigenen " +
            "Datenpunkt allerdings nur dann, wenn er im Backup wirklich unquittiert liegt und " +
            "ihn kein anderes Skript als Befehl entgegennimmt. Echte Befehlskanäle bleiben so " +
            "still. Ein Sammelskript, das nur quittiert und sonst nichts tut, gilt dabei nicht " +
            "als Befehlskanal: Solche Skripte machen die rote Darstellung unquittierter Werte in " +
            "der Objektübersicht weiß und ändern an der Ursache nichts — wer stattdessen auf " +
            "„aktualisieren\" umstellt, braucht sie für den betreffenden Datenpunkt nicht mehr."),

        new(HelpBlockKind.Heading, "Wenn etwas nicht lädt"),
        new(HelpBlockKind.Text,
            "Bei jedem Laden schreibt das Programm ein Ladeprotokoll. Es liegt hier:\n\n" +
            LogPlaceholder + "\n\n" +
            "Die Datei wird bei jedem Laden überschrieben und nennt jeden Schritt mit Zeit und " +
            "Speicherbedarf. Bleibt das Programm einmal stehen, steht in der letzten Zeile, wobei — " +
            "auch dann, wenn man es über den Task-Manager beenden musste."),
        new(HelpBlockKind.Text,
            "Das Protokoll enthält bewusst nur Struktur: Schritte, Zeiten, Größen und die " +
            "Namensräume der Adapter. Objekt-IDs, Werte, Namen von Skripten, Ansichten oder Geräten " +
            "und vollständige Pfade stehen nicht darin — es lässt sich also weitergeben, ohne dass " +
            "etwas aus der eigenen Anlage mitgeht."),

        new(HelpBlockKind.Heading, "Tab „Backup-Prüfung\""),
        new(HelpBlockKind.Text,
            "Prüft, ob das Backup heil ist — nach demselben Muster wie „iobroker backup\" es beim " +
            "Erstellen tut. objects.jsonl und states.jsonl werden zeilenweise strikt als JSON geprüft " +
            "(Pflicht: eine kaputte Zeile = Backup beschädigt); alle JSON-Dateien im files/-Baum werden " +
            "ebenfalls strikt geprüft (optional: nur Warnung). //-Kommentare gelten dabei als ungültig. " +
            "Oben steht ein Ampel-Urteil (grün/orange/rot); die Tabelle nennt jede beschädigte Datei mit " +
            "Fundort und Fehler — so muss man den Übeltäter nicht im laufenden System suchen. " +
            "Der Fundort ist dabei der Pfad in ioBrokers Datei-Datenbank " +
            "(/opt/iobroker/iobroker-data/files/…, im Admin im Tab „Dateien\" — dort ist der " +
            "Experten-Modus nötig, sonst sind die *.admin-Ordner ausgeblendet). Das ist bewusst " +
            "ein anderer Pfad als der, den BackitUp in seiner Warnung nennt: Dessen " +
            "tmp/backup/-Ordner ist nur ein Arbeitsverzeichnis und nach dem Backup wieder leer."),
        new(HelpBlockKind.Text,
            "Unter der Tabelle steht, wie sich der Befund beheben lässt — samt Einschätzung, wie " +
            "riskant der Eingriff ist. Der Unterschied liegt im Ordner: Was in einem " +
            "*.admin-Ordner liegt, gehört dem Adapter und ist nur eine Kopie aus dessen " +
            "Installationsordner — „iobroker upload\" holt es jederzeit zurück, Löschen kostet " +
            "nichts. Was dagegen in einem Instanz-Namensraum liegt (vis-2.0/, javascript.0/ …), " +
            "sind deine eigenen Inhalte; eine vis-views.json ist eine komplette Ansicht. Die ist " +
            "nach dem Löschen weg, also vorher ein aktuelles Backup ziehen. Das Werkzeug sagt " +
            "dir in beiden Fällen, welcher davon vorliegt."),

        new(HelpBlockKind.Heading, "Tab „Skripte\""),
        new(HelpBlockKind.Text,
            "Alle Skripte mit Typ (Blockly/JavaScript/TypeScript) und Status. Der Suchumschalter „Im Code " +
            "suchen\" durchsucht auch das dekodierte Blockly-XML — so findest du, welche Skripte einen " +
            "bestimmten Datenpunkt verwenden; vollständig beantwortet das der Tab „Verwendung\". " +
            "Der Export legt im gewählten Zielordner einen Ordner mit dem Namen der Backup-Datei an, " +
            "darin den Überordner „ioBroker-Skripte\" und darunter genau die ioBroker-Ordnerstruktur " +
            "(globale Skripte im " +
            "Ordner „global\"). Ob ein Skript deaktiviert ist, ändert die Struktur nicht — das steht nur " +
            "als „ (deaktiviert)\" im Dateinamen. Blockly kommt als .xml (im Admin wieder importierbar) " +
            "und als bereinigtes .js."),
        new(HelpBlockKind.Text,
            "Die Spalte „Hinweise\" meldet vier Muster im Aufbau eines Blockly-Skripts. Erstens den " +
            "Auslöser im Rumpf eines anderen Auslösers: Er wird bei jeder Auslösung des äußeren erneut " +
            "angelegt und nie wieder entfernt, sodass nach einigen Stunden dieselben Aktionen vielfach " +
            "parallel laufen — der Blockly-Editor zeigt an dieser Stelle selbst ein Warndreieck. " +
            "Zweitens einen Baustein, den der javascript-Adapter mit dem Zusatz „(deprecated)\" führt; " +
            "das ist derzeit genau einer, nämlich „request\" — Nachfolger ist „HTTP-Get\". Drittens " +
            "einen Auslöser ohne Inhalt, der also auslöst, aber nichts tut. Viertens einen Timer, der " +
            "im Rumpf eines Auslösers startet und nirgends im Skript gelöscht wird: Löst der Auslöser " +
            "erneut aus, bevor der Timer abgelaufen ist, wird nur die Variable überschrieben — der " +
            "vorige Timer läuft weiter und feuert trotzdem, bei jedem Auslösen einer mehr. Abhilfe ist " +
            "der Baustein „Timeout löschen\" mit demselben Namen vor dem Starten. Ob daraus ein " +
            "Problem wird, hängt davon ab, wie oft der Auslöser feuert; diese Häufigkeit steht nicht " +
            "im Backup. Zu jedem Befund steht " +
            "unter der Liste die Begründung und die Baustein-ID, mit der er sich im Blockly-Editor " +
            "wiederfinden lässt; der Filter „Nur mit Hinweisen\" zeigt die betroffenen Skripte allein."),
        new(HelpBlockKind.Text,
            "Zwei Einschränkungen dazu, damit die Spalte richtig gelesen wird. Geprüft wird " +
            "ausschließlich Blockly — dort hängt jeder Befund an einem benannten Baustein. Bei " +
            "JavaScript und TypeScript ließe sich dasselbe nur über Textsuche vermuten, und ein " +
            "„on(\" in einem Kommentar oder in einer Zeichenkette wäre davon nicht zu unterscheiden; " +
            "die Spalte bleibt dort deshalb leer. Und es gibt keine Note: Was dort steht, sind " +
            "einzelne Fundstellen mit Begründung. Ob daraus etwas folgt, entscheidet, wer das Skript " +
            "geschrieben hat."),

        new(HelpBlockKind.Heading, "Tab „Verwendung\""),
        new(HelpBlockKind.Text,
            "Die Kreuzreferenz zwischen Skripten und Datenpunkten, umschaltbar in beide Richtungen. " +
            "Oben die Liste, unten die Gegenseite des angeklickten Eintrags. Ein Doppelklick auf " +
            "ein Skript wechselt in den Tab „Skripte\" und wählt es dort aus — samt Quelltext. " +
            "Das gilt für die obere Liste in der Sicht „Skript → Datenpunkte\" und für die " +
            "untere Liste in der Sicht „Datenpunkt → Skripte\"; verdeckt die dortige Filterung " +
            "das Skript, wird sie dafür zurückgesetzt.\n" +
            "• „Skript → Datenpunkte\": Was fasst dieses Skript alles an — und liest es nur oder " +
            "schreibt es auch?\n" +
            "• „Datenpunkt → Skripte\": Wer hängt an diesem Wert? Die Auswahl „Von mehreren Skripten " +
            "beschrieben\" beantwortet die Frage, warum sich ein Wert scheinbar von allein ändert: " +
            "Meist schreibt ein zweites, längst vergessenes Skript ebenfalls darauf. Solche Zeilen " +
            "stehen rot.\n" +
            "• „Nirgends verwendet\" zeigt die Gegenrichtung: angelegte Aliasse und eigene " +
            "Datenpunkte, die weder ein Skript noch ein Adapter benutzt."),
        new(HelpBlockKind.Text,
            "Die Spalte „Adapter\" zählt eine zweite Quelle mit: Viele Adapter bekommen ihre " +
            "Datenpunkte direkt in der Instanzkonfiguration eingetragen — Shuttercontrol seine " +
            "Rollläden, awtrix-light die Werte seiner Apps, text2command die Ziele seiner Regeln. " +
            "Solche Datenpunkte stehen in keinem Skript und sahen früher wie Karteileichen aus. " +
            "Dafür gibt es zwei Auswahlen: „In einem Adapter eingetragen\" zeigt alle Datenpunkte " +
            "mit Adapterbezug — auch die, auf die zusätzlich Skripte zugreifen; das ist die " +
            "Antwort auf „was hat sich ein Adapter eingetragen?\". „Im Adapter, aber in keinem " +
            "Skript\" grenzt auf die Fälle ein, die sonst wie Karteileichen aussähen. " +
            "In der unteren Tabelle " +
            "stehen dann Instanz und Feld (z. B. customApps[0].objId).\n" +
            "Ob ein Adapter seinen Datenpunkt liest oder schreibt, verrät das Backup nicht — die " +
            "Spalte „Zugriff\" bleibt dort leer.\n" +
            "Und noch etwas sagt das Backup nicht: ob der Adapter die Funktion überhaupt benutzt. " +
            "Ein Adapter-Treffer heißt allein, dass die ID in der Konfiguration steht. Wer vor " +
            "Jahren einmal einen Datenpunkt zum Ausprobieren eingetragen und die Funktion später " +
            "abgewählt hat, findet ihn hier trotzdem — das Eingabefeld bleibt gefüllt. Solche " +
            "Zeilen sind also kein Fehler der Auswertung, sondern ein Fund: eine Altlast in der " +
            "Adapter-Konfiguration, die sich im Admin unter Instanzen nachsehen und leeren lässt.\n" +
            "Die Spalte „Zuletzt geändert\" hilft beim Einordnen, aber nur in einer Richtung. Sie " +
            "zeigt, wann der Datenpunkt selbst zuletzt einen Wert bekam — nicht, wann ein Adapter " +
            "ihn gelesen hat. Liegt das Jahre zurück, ist der Fall klar: Dann liefert schon die " +
            "Quelle nichts mehr, und der Eintrag ist tot. Ein frischer Zeitstempel beweist " +
            "dagegen nichts über den Adapter — ein Temperaturfühler sendet weiter, ganz gleich, " +
            "ob irgendein Adapter den Wert noch verwendet. Ob ein Adapter seinen Eintrag " +
            "tatsächlich benutzt, weiß nur er selbst zur Laufzeit; in einem Backup steht es " +
            "nicht."),
        new(HelpBlockKind.Text,
            "Die Farben in der Datenpunkt-Sicht (die Legende steht auch über der Tabelle):\n" +
            "• Rot = mehr als ein Skript schreibt diesen Datenpunkt. Kein Fehler an sich, aber die " +
            "häufigste Ursache für Werte, die sich unerklärlich verhalten.\n" +
            "• Orange = zu dem Wert gibt es kein Objekt mehr (Werte-Leiche), ein Skript spricht ihn " +
            "aber weiterhin an.\n" +
            "• Grau = kommt in keinem Skript vor.\n" +
            "• Ohne Hervorhebung = unauffällig: höchstens ein Skript schreibt darauf.\n" +
            "In der Skript-Sicht steht Grau für ein deaktiviertes Skript oder eines ohne " +
            "Datenpunkt-Bezug; alles ohne Hervorhebung ist ein aktives Skript mit Datenpunkten."),
        new(HelpBlockKind.Text,
            "Gesucht wird in den Zeichenketten der Skripte — im JavaScript und im Blockly-XML. Bei " +
            "Blockly ist das erzeugte JavaScript maßgeblich, weil erst dort steht, ob ein Block liest " +
            "oder schreibt. Steht eine ID nur im XML und nicht im erzeugten Code, gehört sie zu einem " +
            "deaktivierten Block; die Spalte „Fundstelle\" sagt das.\n" +
            "Zwei Grenzen: Setzt ein Skript IDs zur Laufzeit zusammen (\"0_userdata.0.Raum.\" + name), " +
            "ist der genaue Datenpunkt nicht bestimmbar — dann erscheinen die Kandidaten unter dem " +
            "erkannten Anfang, gekennzeichnet als „zur Laufzeit zusammengesetzt\". Und Nutzung " +
            "außerhalb der Skripte (VIS, Adapter, externe Systeme) sieht dieser Tab nicht; dafür sind " +
            "„VIS-Datenpunkte\" und „Verwaiste Datenpunkte\" da."),

        new(HelpBlockKind.Heading, "Tab „VIS-Datenpunkte\""),
        new(HelpBlockKind.Text,
            "Alle in der Visualisierung verwendeten Datenpunkte, getrennt nach VIS 1 und VIS 2, mit " +
            "Fundstelle je Widget und View. Aliasse werden auf ihr Ziel aufgelöst. Rot markiert sind " +
            "„tote Widgets\": Datenpunkte, die im Backup nicht mehr existieren, auf die eine View aber " +
            "weiterhin zeigt."),
        new(HelpBlockKind.Text,
            "In derselben Kopfleiste steht „Projekt als ZIP (VIS-Import)\": Damit wird ein ganzes " +
            "VIS-Projekt aus dem Backup als ZIP geschrieben — im Aufbau, den „Tools → Projektimport\" " +
            "in VIS 1 und VIS 2 erwartet. Das ist der Weg, eine gelöschte Ansicht zurückzuholen, ohne " +
            "das Backup einzuspielen. Angeboten werden nur Ordner mit einer vis-views.json; reine " +
            "Bilderordner neben dem Projekt sind keine Projekte und deshalb auch nicht in der ZIP."),
        new(HelpBlockKind.Text, VisPresenter.ImportHint),
        new(HelpBlockKind.Text,
            "Der zweite Untertab „Widget-Sätze\" beantwortet eine andere Frage: welchen Baukasten " +
            "brauchen die Ansichten überhaupt noch. Je Satz steht dort, in welcher Projektfassung er " +
            "vorkommt, wie viele Widgets ihn nutzen und ob der zugehörige Adapter installiert ist.\n" +
            "Gezählt wird auf zwei Wegen, weil ein Satz auf zwei Arten in Anspruch genommen wird: über " +
            "das Feld „widgetSet\" am Widget und über Dateiverweise wie /vis-icontwo/tuer-offen.png in " +
            "einer Widget-Eigenschaft. Ein Icon-Satz kann in keinem einzigen widgetSet stehen und " +
            "trotzdem hunderte Male als Bildpfad verwendet werden — wer nur die erste Zählung kennt, " +
            "hält ihn für entbehrlich.\n" +
            "Als Umstiegsrest gilt ein alter Satz nur, wenn seine VIS-2-Fassung schon installiert ist. " +
            "Adapter, die ihre Widgets ohne solches Gegenstück für beide Fassungen mitbringen, sind " +
            "keine Altlast und stehen weiterhin als „in Gebrauch\".\n" +
            "Unter der Liste steht, an welcher Stelle der gewählte Satz vorkommt: View, " +
            "Widget-ID und die Art der Verwendung — als Widget-Satz mit der Widget-Vorlage " +
            "daneben oder als Dateiverweis mit Feld und Pfad. Damit führt der Weg von " +
            "„dieser Satz ist eine Altlast\" zu „dieses eine Widget hält ihn fest\" ohne " +
            "Umweg über die vis-views.json.\n" +
            "Der Vorbehalt über der Liste gehört zur Aussage: VIS 2 bettet ausgewählte Symbole " +
            "vollständig in das Projekt ein, ohne Hinweis auf ihre Herkunft. Ein Icon-Satz kann also " +
            "die Anzeige tragen und hier trotzdem ohne einen einzigen Verweis dastehen. Vor dem " +
            "Entfernen eines Adapters deshalb im laufenden System gegenprüfen."),

        new(HelpBlockKind.Heading, "Tab „Verwaiste Datenpunkte\""),
        new(HelpBlockKind.Text,
            "Drei Analysen — allesamt Prüflisten, keine Löschlisten. Nutzung durch externe Systeme oder " +
            "per zur Laufzeit zusammengesetzte IDs ist im Backup nicht erkennbar."),
        new(HelpBlockKind.Text,
            "• A — Objekt-Leichen: Objekte, deren Adapter-Instanz im Backup fehlt.\n" +
            "• B — Unbenutzte User-Datenpunkte: Für jeden Datenpunkt wird geprüft, ob er in Skripten, in " +
            "VIS-Views, als Alias-Ziel, im Logging oder in einer Chart-Definition vorkommt. Schlägt keine " +
            "der fünf Prüfungen an, ist es ein Kandidat. Zusätzlich zeigt „Zuletzt geändert\", ob der Wert " +
            "noch beschrieben wird — ein orange markierter Kandidat lebt also noch.\n" +
            "• C — States: fünf Sichten auf die States-Datenbank (Werte ohne Objekt, Objekte ohne Wert, " +
            "älteste Datenpunkte, auffällige Qualität, nicht quittierte Befehle)."),
        new(HelpBlockKind.Text,
            "„Objekte ohne Wert\" nimmt Aliasse ausdrücklich aus. Ein Alias hat nie einen eigenen Eintrag " +
            "in der States-DB: Der js-controller reicht Lesen und Schreiben an das Ziel aus " +
            "common.alias.id durch. Im Admin sieht man am Alias trotzdem einen Wert samt Zeitstempel — " +
            "das ist der des Ziels. Stünden die Aliasse hier, wäre jeder einzelne ein Fehlalarm. Ob das " +
            "Ziel eines Alias überhaupt noch existiert, beantwortet der Tab „Aliasse\"."),
        new(HelpBlockKind.Text,
            "„States ohne Objekt\" (Werte-Leichen) sind Werte in der States-DB, zu denen kein Objekt mehr " +
            "existiert — meist von Adaptern, die längst entfernt wurden. Der Admin zeigt sie nicht, weil " +
            "es dazu kein Objekt im Baum gibt. Über „Aufräum-Skript erzeugen …\" kannst du die betroffenen " +
            "Namensräume anhaken und bekommst ein Shell-Skript, das diese Werte über die ioBroker-CLI " +
            "löscht. Ein Namensraum ist selten durchgehend Müll — deshalb lässt er sich aufklappen und " +
            "einzeln abhaken; das Häkchen am Namensraum zeigt dann „teilweise\". Das Suchfeld darüber " +
            "blendet nur aus, es wählt nichts ab: Was du außerhalb der Suche angehakt hast, bleibt im " +
            "Skript, und die Zeile unter dem Baum sagt, wie viel das insgesamt ist. " +
            "„Skript speichern …\" legt die Datei gleich richtig ab — mit Linux-Zeilenenden, " +
            "sonst scheitert sie auf dem Host an einem unsichtbaren Wagenrücklauf. Danach nur noch auf " +
            "den ioBroker-Host kopieren und dort „bash <Datei>\" aufrufen; das Skript fragt selbst, ob " +
            "es löschen oder nur testen soll. Nur ein GROSSES „J\" löscht — alles andere ist ein Testlauf, " +
            "auch die bloße Eingabetaste. Ausgeführt wird es von dir in der Shell, nicht hier.\n" +
            "Warum nicht im JavaScript-Adapter? „deleteState\" darf dort nur eigene States löschen — gegen " +
            "fremde Namensräume meldet es für jede ID „Not found\". Und „deleteObject\" greift nicht, weil " +
            "zu einer Werte-Leiche gerade kein Objekt mehr existiert. Nur die CLI kommt an den reinen Wert."),

        new(HelpBlockKind.Text,
            "Die Spalte „Qualität\" ist der Qualitätscode aus der States-DB — ioBroker merkt sich zu " +
            "jedem Wert, wie verlässlich er ist. „gut\" heißt: ein echter Wert vom Erzeuger. Alles " +
            "andere ist nicht automatisch eine Störung, deshalb heißt die Sicht „Auffällige Qualität\" " +
            "und nicht „Fehler\":\n" +
            "• Startwert (nie echt beschrieben) — der Adapter hat den Datenpunkt angelegt und mit einem " +
            "Vorgabewert gefüllt, ein echter Messwert kam nie an. Der häufigste Fall überhaupt und meist " +
            "harmlos: Geräteeigenschaften, die der Adapter vorsorglich anlegt. Sammeln sich viele davon " +
            "bei einer Instanz, lohnt der Blick, ob sie überhaupt noch etwas liefert.\n" +
            "• Ersatzwert (vom js-controller, von Gerät/Instanz, vom Sensor) — der angezeigte Wert " +
            "stammt nicht vom eigentlichen Erzeuger, sondern ist eingesetzt worden.\n" +
            "• Nicht verbunden / meldet Fehler / allgemeines Problem — das sind die echten Störungen: " +
            "Instanz, Gerät oder Sensor waren zum Backup-Zeitpunkt nicht erreichbar oder haben einen " +
            "Fehler gemeldet.\n" +
            "Die Farbe folgt derselben Einteilung: Nur echte Störungen stehen orange, Start- " +
            "und Ersatzwerte grau. Sonst wäre in dieser Sicht fast jede Zeile farbig und die " +
            "paar Störungen gingen darin unter.\n" +
            "Steht dort „unbekannter Code 0x…\", hält sich der schreibende Adapter nicht an das " +
            "ioBroker-Schema — dann hilft nur die Doku des Adapters."),

        new(HelpBlockKind.Heading, "Tab „Datenpunkte\""),
        new(HelpBlockKind.Text,
            "Der Weg zu einem bestimmten Wert. Oben wird über Datenpunkt-ID und Name gesucht — mehrere " +
            "Begriffe dürfen in beliebiger Reihenfolge stehen, „wohnzimmer temp\" findet den Datenpunkt " +
            "also auch dann, wenn seine ID ganz anders aussieht als sein Name. Unten steht der zuletzt " +
            "gespeicherte Wert vollständig und lässt sich mit einem Knopfdruck kopieren.\n" +
            "Dafür ist der Tab gedacht: Ein Wert, der im laufenden System überschrieben wurde, steht im " +
            "Backup noch — und von dort kommt man mit Suchen und Kopieren schneller an ihn heran als über " +
            "eine Wiederherstellung.\n" +
            "Werte, die als JSON abgelegt sind, erscheinen eingerückt. Im Backup stehen sie als Text mit " +
            "maskierten Anführungszeichen mitten in einer Zeile, die selbst JSON ist; hier stehen sie so, " +
            "wie man sie einsetzen kann. Am Inhalt ändert sich dabei nichts, es kommt nur Weißraum hinzu.\n" +
            "Über dem Wert steht, was ihn einordnet: Typ, Einheit, Rolle, Grenzen und Vorgabewert aus der " +
            "Objektdefinition, dazu die schreibende Instanz, die Quittierung und der Qualitätscode. Fehlt " +
            "zu einem Wert das Objekt — der Datenpunkt wurde gelöscht, sein Wert blieb stehen —, wird das " +
            "ausgewiesen; solche Zeilen stehen orange.\n" +
            "Sehr große Werte werden bei 64 KB gekappt, damit ein Kamerabild im Backup den Arbeitsspeicher " +
            "nicht unnötig füllt. Passiert das, steht es über dem Wertfeld.\n" +
            "Geschrieben wird nichts: Der Analyzer liest Backups. Was mit dem kopierten Wert geschieht, " +
            "entscheidet man selbst im Admin oder in einem Skript."),

        new(HelpBlockKind.Heading, "Tab „Logging\""),
        new(HelpBlockKind.Text,
            "Je Datenpunkt und loggender Instanz (History, InfluxDB, SQL …) eine Zeile: ob das Logging " +
            "aktiv ist, ob nur bei Wertänderung geloggt wird und mit welcher Entprellzeit. Deaktivierte " +
            "Einträge stehen grau — nützlich, um vergessene Logging-Verbindungen zu finden."),

        new(HelpBlockKind.Heading, "Tab „Aliasse\""),
        new(HelpBlockKind.Text,
            "Jeder Alias mit Lese- und Schreibziel und der Angabe, ob das Ziel noch existiert; kaputte " +
            "Aliasse (Ziel gelöscht) stehen rot. Der Detailbereich unten zeigt die Konvertierungsfunktionen " +
            "des Alias. Der Knopf „Konverter-Vorschlag …\" erzeugt aus der Wertetabelle des Ziel-Datenpunkts " +
            "einen fertigen Konverter zum Kopieren — bei reinen Zahlenumrechnungen (val/5+21) meldet er " +
            "ehrlich, dass sich das aus dem Backup nicht ableiten lässt."),

        new(HelpBlockKind.Heading, "Tab „Dateien\""),
        new(HelpBlockKind.Text,
            "Der Dateibereich des Backups — dasselbe, was der Admin unter „Dateien\" zeigt: Namensraum, " +
            "Pfad, Größe und Typ, filterbar nach Namensraum und Text. Der Export legt — wie bei den " +
            "Skripten — einen Ordner mit dem Namen der Backup-Datei an, darin den Überordner " +
            "„ioBroker-Dateien\" und darunter die Original-Ordnerstruktur. So überschreiben zwei " +
            "nacheinander ausgewertete Backups einander nicht.\n" +
            "Zwei Dinge fallen dabei auf. Erstens fehlen die Dateien der Adapter selbst — im Admin die " +
            "Ordner ohne Instanznummer wie „vis\" oder „echarts\". Das ist kein Mangel des Backups: " +
            "Diese Dateien gehören dem Adapter, sind reproduzierbar und werden beim Wiederherstellen " +
            "von ioBroker selbst wieder angelegt. Von Hand nachladen („iobroker upload <adapter>\") " +
            "muss man sie nur, wenn im laufenden System etwas fehlt. Wer allerdings eine " +
            "Adapter-Datei selbst verändert hat, verliert die Änderung — sie steht in keinem Backup " +
            "und wird beim Upload überschrieben. Eigene Inhalte gehören deshalb in einen " +
            "Instanz-Ordner wie „vis-2.0\" oder „0_userdata.0\".\n" +
            "Zweitens: Dateinamen, die unter Windows verbotene Zeichen tragen (ioBroker erlaubt " +
            "z. B. den Doppelpunkt), werden beim Export entschärft; wie viele es waren, steht in " +
            "der Meldung danach."),

        new(HelpBlockKind.Heading, "Tab „Vergleich\""),
        new(HelpBlockKind.Text,
            "Zwei Backups gegenüberstellen: geänderte Adapter-Versionen (Updates/Downgrades), neue und " +
            "gelöschte Objekte je Namensraum, geänderte VIS-Views und inhaltlich geänderte Skripte mit " +
            "zeilengenauem Diff (bei Blockly wird das XML verglichen). Zusätzlich wird über die " +
            "Installations-UUID geprüft, ob beide Backups überhaupt aus demselben System stammen."),

        new(HelpBlockKind.Heading, "Tab „Änderungen\""),
        new(HelpBlockKind.Text,
            "Der Änderungsverlauf: was jede Fassung gebracht hat, neueste zuerst. Bis Version 1.18.8 " +
            "stand er hier in der Hilfe und war ihr längster Abschnitt — wer etwas nachschlagen wollte, " +
            "musste erst an allen Versionen vorbei. Die laufende Fassung steht unten rechts in der " +
            "Statusleiste, dieselbe Nummer wie im Fenstertitel."),

        new(HelpBlockKind.Heading, "Tabellen bedienen"),
        new(HelpBlockKind.Text,
            "Jede Tabelle lässt sich per Klick auf den Spaltenkopf sortieren (zweiter Klick dreht um), " +
            "Spalten sind per Maus verschiebbar, und über der Tabelle steht ein Textfilter. Jede " +
            "Ergebnisliste kann als CSV exportiert werden. Zellen lassen sich mit Strg+C kopieren."),
        new(HelpBlockKind.Text,
            "Exportiert wird immer das, was gerade in der Liste steht — Suchbegriff, Auswahllisten und " +
            "Checkboxen wirken mit. Der Knopf sagt es an: ohne Filter „Alle exportieren\", mit Filter " +
            "„Gefilterte exportieren (30)\". Einzige Ausnahme ist die Sicht „Älteste Datenpunkte\": Sie " +
            "zeigt nur die ersten 2.000 Zeilen, die CSV enthält aber alle Treffer."),
        new(HelpBlockKind.Text,
            "Ist eine Spalte zu schmal für ihren Inhalt, macht ein Rechtsklick auf den Spaltenkopf " +
            "sie so breit wie ihr längster Text — nur diese eine Spalte, nicht die ganze Tabelle. " +
            "Passt eine Tabelle danach nicht mehr ins Fenster, erscheint unter ihr ein Rollbalken; " +
            "in der plattformübergreifenden Fassung markiert die schmale leere Spalte ganz rechts " +
            "das Ende."),
        new(HelpBlockKind.Text,
            "Eine Grenze der Windows-Fassung: Sehr lange Zellinhalte zeigt sie nur bis etwa 260 " +
            "Zeichen an — das ist eine Eigenheit der Windows-Tabellenanzeige, keine Frage der " +
            "Spaltenbreite. Betroffen ist in der Praxis nur die Spalte „Views\" bei Datenpunkten, " +
            "die in sehr vielen Ansichten vorkommen. Vollständig stehen solche Werte im " +
            "CSV-Export, in der Fundstellen-Tabelle darunter und in der plattformübergreifenden " +
            "Fassung."),

        new(HelpBlockKind.Heading, "Datenschutz & Grenzen"),
        new(HelpBlockKind.Text,
            "Ein ioBroker-Backup enthält Zugangsdaten (in den native-Abschnitten der Adapter). Diese " +
            "werden hier bewusst nirgends angezeigt. Trotzdem: Backup nicht weitergeben und vor einem " +
            "Screenshot kurz prüfen, was in der Tabelle steht. Redis-, InfluxDB- und Zigbee-Teilbackups " +
            "werden nicht ausgewertet.")
    };
}
