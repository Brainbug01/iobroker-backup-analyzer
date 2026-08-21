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
