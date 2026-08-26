namespace IobBackupAnalyzer.Core;

/// <summary>Ein Versionseintrag: Nummer, Datum und die Änderungen als Aufzählung.</summary>
/// <param name="Version">„1.18.0" — ohne führendes v.</param>
/// <param name="Date">Tag der Fertigstellung, Format dd.MM.yyyy.</param>
/// <param name="Changes">Je Eintrag eine Zeile, ohne Aufzählungszeichen.</param>
public sealed record ChangelogEntry(string Version, string Date, IReadOnlyList<string> Changes);

/// <summary>
/// Was sich von Version zu Version geändert hat — sichtbar in der Hilfe, damit man nach
/// einem Update nicht raten muss.
///
/// <b>Warum in der Anwendung und nicht nur im README?</b> Das Werkzeug wird als einzelne
/// Datei weitergegeben; wer eine neuere Fassung bekommt, hat das Repository nicht. In der
/// Titelleiste steht dann eine neue Nummer und sonst nichts.
///
/// Gepflegt wird die Liste beim Versionssprung, zusammen mit der Versionsnummer in den
/// beiden csproj-Dateien. Nur was für die Benutzung einen Unterschied macht — interne
/// Umbauten gehören in die Git-Historie, nicht hierher.
/// </summary>
public static class ChangelogContent
{
    /// <summary>Neueste Version zuerst.</summary>
    public static IReadOnlyList<ChangelogEntry> Entries { get; } = new ChangelogEntry[]
    {
        new("1.27.0", "26.08.2026", new[]
        {
            "Der Untertab „Widget-Sätze\" zeigt jetzt unter der Liste, an welcher Stelle ein " +
            "Satz steckt: View, Widget-ID und die Art der Verwendung. Bisher stand dort nur, wie " +
            "oft — und wer einen alten Satz loswerden wollte, musste die vis-views.json " +
            "selbst durchsuchen. Genau den Schritt soll das Werkzeug abnehmen.",

            "Unterschieden wird dabei, wie der Satz in Anspruch genommen wird: als " +
            "Widget-Satz (dann steht die Widget-Vorlage daneben) oder als Dateiverweis (dann " +
            "das Feld und der Pfad, etwa iImageFalse mit /vis-icontwo/tuer-offen.png).",

            "Die Zählung der Dateiverweise läuft dafür nicht mehr über den Rohtext, sondern " +
            "Feld für Feld. Beide Verfahren liefern dieselbe Zahl — am Testbackup 678 und 2 " +
            "Treffer, identisch —, aber nur das zweite weiß, in welchem Widget der Verweis " +
            "steht.",

            "Anzeigegrenzen vereinheitlicht: Die Datenpunkt-Suche, die Sicht „Älteste\" und " +
            "die neuen Fundstellen zeigen jetzt alle höchstens 2.000 Zeilen statt 500 " +
            "beziehungsweise 2.000. Gemessen kostet das rund eine Drittelsekunde beim " +
            "Aufbau; der CSV-Export enthält weiterhin alles."
        }),

        new("1.26.1", "26.08.2026", new[]
        {
            "Korrektur im neuen Untertab „Widget-Sätze\": Der Grundbaukasten von VIS 2 " +
            "(vis-2-widgets-basic) wurde rot als „Adapter fehlt im Backup\" gemeldet. Er " +
            "trägt zwar das Namensmuster der Zusatzpakete, steckt aber im Adapter vis-2 " +
            "selbst — genau wie „basic\" in VIS 1 zum vis-Adapter gehört. Betroffen war " +
            "jedes VIS-2-Projekt."
        }),

        new("1.26.0", "26.08.2026", new[]
        {
            "Neue Prüfung bei den Skripten: „Timer wird nie gelöscht\". Sie meldet einen " +
            "Timeout- oder Intervall-Baustein, der im Rumpf eines Auslösers startet, zu dem " +
            "es aber nirgends im Skript ein „Timeout löschen\" gibt. Blockly erzeugt daraus " +
            "eine Zuweisung; löst der Auslöser erneut aus, bevor der Timer abgelaufen ist, " +
            "wird nur die Variable überschrieben — der vorige Timer läuft weiter und feuert " +
            "trotzdem. Bei jedem Auslösen kommt einer hinzu, und die Anlage bekommt Last, " +
            "ohne dass die CPU auffällig würde.",

            "Abgeschaltete Bausteine und Skripte werden dabei getrennt ausgewiesen. Im " +
            "Testbackup sind von 38 gefundenen Timern 19 abgeschaltete Bausteine und 16 in " +
            "abgeschalteten Skripten — ohne diese Unterscheidung wäre der Befund zwölfmal " +
            "so groß und wertlos.",

            "Neuer Untertab „Widget-Sätze\" bei den VIS-Datenpunkten: welcher Widget-Baukasten " +
            "in welcher Projektfassung steckt, wie oft, und ob der zugehörige Adapter " +
            "installiert ist. Beantwortet die Frage, welche Sätze man nach einem Umstieg auf " +
            "VIS 2 noch braucht.",

            "Gezählt werden dabei zwei Wege, nicht nur einer: das Feld „widgetSet\" am Widget " +
            "und Dateiverweise wie /vis-icontwo/tuer-offen.png in den Widget-Eigenschaften. " +
            "Im Testbackup steht ein Icon-Satz in keinem einzigen widgetSet und wird trotzdem " +
            "680-mal als Bildpfad verwendet — wer nur die erste Zählung kennt, hält ihn " +
            "fälschlich für entbehrlich.",

            "Ein alter Satz gilt nur dann als Umstiegsrest, wenn seine VIS-2-Fassung bereits " +
            "installiert ist. Adapter, die ihre Widgets ohne solches Gegenstück für beide " +
            "Fassungen mitbringen, stehen weiterhin als „in Gebrauch\".",

            "Über der Liste steht ein Vorbehalt, und er gehört zur Aussage: VIS 2 bettet " +
            "ausgewählte Symbole vollständig in das Projekt ein, ohne Hinweis auf ihre " +
            "Herkunft. Ein Icon-Satz kann die Anzeige tragen und trotzdem ohne einen einzigen " +
            "Verweis dastehen. Die Liste ist deshalb eine Prüfliste, keine " +
            "Deinstallationsliste.",

            "Korrektur in der Übersicht: Adapter, die nur Dateien ausliefern und gar keinen " +
            "eigenen Prozess haben (common.onlyWWW — VIS-Widget-Sätze sind der Regelfall), " +
            "wurden als „Aktiviert: Nein\" geführt und grau gedämpft. Bei ihnen sagt dieses " +
            "Feld nichts über die Funktion: Im Testbackup steht ein Satz auf „nicht " +
            "aktiviert\" und bedient trotzdem 56 Widgets einwandfrei. Solche Instanzen zeigen " +
            "jetzt „nur Dateien\" und werden nicht mehr gedämpft — wer nach Aufräumkandidaten " +
            "sucht, hätte sonst ausgerechnet die entfernt, die gebraucht werden."
        }),

        new("1.25.1", "26.08.2026", new[]
        {
            "Im Reiter „Datenpunkte\" steht jetzt der vollständige Name des Datenpunkts in " +
            "der Beschreibung unter der Liste. Vorher war nicht immer erkennbar, warum eine " +
            "Zeile in der Trefferliste steht: Manche Adapter legen ganze Sätze als Namen ab " +
            "— im Testbackup bis zu 583 Zeichen —, und die Namensspalte schneidet sie ab. " +
            "Eine Suche nach „fore\" traf so auf „…can take up to 24 hours before reported\", " +
            "ohne dass die Fundstelle zu sehen war."
        }),

        new("1.25.0", "26.08.2026", new[]
        {
            "Neuer Reiter „Datenpunkte\": Suche über Datenpunkt-ID und Name, darunter der " +
            "zuletzt gespeicherte Wert vollständig zum Kopieren. Damit lässt sich ein Wert " +
            "aus einem alten Backup zurückholen, ohne dafür das ganze Backup wiederherstellen " +
            "zu müssen.",

            "Werte, die als JSON abgelegt sind, erscheinen eingerückt statt als eine lange " +
            "Zeile. Im Backup stehen sie als Text mit maskierten Anführungszeichen mitten " +
            "in einer JSON-Zeile; von Hand ist daraus nur mühsam etwas Einsetzbares zu " +
            "machen. Der Inhalt bleibt dabei unverändert, es kommt nur Weißraum hinzu.",

            "Neben dem Wert steht, was ihn einordnet: Typ, Einheit, Rolle, Grenzen und " +
            "Vorgabewert aus der Objektdefinition, dazu die schreibende Instanz, die " +
            "Quittierung und der Qualitätscode. Ohne Einheit ist „21.5\" nicht viel wert.",

            "Die Suche nimmt mehrere Begriffe, deren Reihenfolge egal ist: „wohnzimmer " +
            "temp\" findet den Datenpunkt auch dann, wenn seine ID ganz anders aussieht " +
            "als sein Name.",

            "Neue Spalte „Letzter Wert\" in den Listen „Verwaiste Datenpunkte\" (Analysen " +
            "B und C) und „Verwendung\". Bei der Frage, ob ein Datenpunkt weg kann, ist " +
            "sein letzter Wert oft aussagekräftiger als jede Kennzahl daneben.",

            "Hintergrund: Werte wurden bisher beim Laden verworfen, weil einzelne " +
            "Datenpunkte sehr groß werden können. Die Messung zeigt, dass das nur wenige " +
            "betrifft — sie zu laden kostet keine messbare Ladezeit. Sehr große Werte " +
            "werden bei 64 KB gekappt; die Anzeige weist das aus."
        }),

        new("1.24.0", "25.08.2026", new[]
        {
            "Neu im Reiter „Verwendung\": die Sicht „Verweise ins Leere\". Sie zeigt " +
            "Datenpunkte, die ein Skript anspricht, die es im Backup aber nicht gibt — " +
            "ein Tippfehler in einer ID, ein gelöschter Datenpunkt, ein Gerät, das nicht " +
            "mehr da ist. Anders als die übrigen Listen sucht diese keine Altlasten, " +
            "sondern Fehler: Ein Skript, das ins Leere schreibt, tut das unbemerkt — " +
            "im Log steht bestenfalls eine Warnung, die niemand liest.",

            "Rot stehen die deutlichen Fälle — den Namensraum gibt es, den Datenpunkt " +
            "nicht. Fehlt der Namensraum ganz, kann es ebenso gut ein Skript für eine " +
            "andere Anlage sein; solche Zeilen bleiben unauffällig. Gesucht wird im " +
            "erzeugten JavaScript und nicht im Blockly-XML: Ein abgeschalteter Baustein " +
            "läuft nicht, seine Datenpunkte fehlen dann zu Recht.",

            "Die Hinweise zum Aufbau von Blockly-Skripten sagen jetzt dazu, wenn der " +
            "betroffene Baustein im Editor abgeschaltet ist. Der Befund verschwindet " +
            "nicht — er greift ja, sobald jemand den Baustein wieder einschaltet —, wird " +
            "aber als Möglichkeit formuliert statt als Tatsache.",

            "Die Adapterliste in der Übersicht hat zwei neue Spalten: „Protokoll\" und " +
            "„Neustart\". Ein Adapter auf „Debug\" schreibt das Protokoll voll, ein " +
            "geplanter Neustart erklärt, warum eine Instanz immer zur selben Uhrzeit " +
            "aussetzt. Beides steht im Backup und war dort bisher nicht zu sehen.",

            "Beide Spalten bleiben leer, wo nichts eingestellt ist. Bei der Protokollstufe " +
            "heißt leer ausdrücklich nicht „kein Protokoll\", sondern „Vorgabe des " +
            "js-controllers\" — und die steht nicht im Backup. Behauptet wird deshalb " +
            "nichts.",
        }),

        new("1.23.4", "25.08.2026", new[]
        {
            "Die Browser-Fassung ruft die Serverprüfung nicht mehr von selbst auf. Sie " +
            "erschien bisher beim ersten Aufruf je Browser, auch wenn der Server richtig " +
            "eingestellt war. Die Prüfseite bleibt liegen und lässt sich jederzeit " +
            "aufrufen; die beiliegende Anleitung nennt den Weg.",

            "Nebenwirkung, die einigen aufgefallen war: Auf einem Server ohne diese " +
            "Prüfseite hinterliess die Nachfrage eine Fehlermeldung in der Browserkonsole, " +
            "die wie ein Defekt aussah, aber keiner war. Die ist damit ebenfalls weg.",

            "Das Ladeprotokoll der Browser-Fassung wird jetzt so gespeichert, dass jeder " +
            "Editor die Umlaute richtig anzeigt. Bisher konnte aus einem Gedankenstrich " +
            "je nach Programm „â€”\" werden. Die Fassungen für Windows, macOS und Linux " +
            "waren davon nie betroffen.",
        }),

        new("1.23.3", "25.08.2026", new[]
        {
            "Behoben: Die Browser-Fassung startete nicht, wenn der Browser auf eine andere " +
            "Sprache als Deutsch eingestellt war. Statt des Programms erschien eine " +
            "Fehlermeldung. Grund war die Zahlendarstellung — das Programm stellt sie auf " +
            "Deutsch um, damit „16.576\" überall gleich aussieht, und ein Browser mit " +
            "anderer Spracheinstellung brachte die dafür nötigen Daten nicht mit. Jetzt " +
            "wird die Sprache gleich beim Start festgelegt. Wer einen deutschen Browser " +
            "benutzt, hat davon nie etwas gemerkt.",

            "In der Browser-Fassung lässt sich die Linie zwischen der Liste oben und den " +
            "Einzelheiten unten jetzt verschieben — wie in den Programmen für Windows, " +
            "macOS und Linux. Betroffen sind dieselben fünf Reiter: „Verwendung\", " +
            "„Skripte\", „Aliasse\", „Vergleich\" und „VIS-Datenpunkte\". Wer die Liste " +
            "grösser haben will als die Einzelheiten, zieht die Linie einfach nach unten.",

            "Ganz zuziehen lässt sie sich nicht: Beide Bereiche behalten eine Mindesthöhe, " +
            "so wie in den Programmen auch. Das ist kein Selbstzweck — sonst käme etwa der " +
            "Doppelklick aus „Verwendung\" zwar beim richtigen Skript an, nur wäre die " +
            "Zeile hinter dem zugezogenen Rand nicht mehr zu sehen.",

            "Im Reiter „Aliasse\" waren die vier Konverter-Felder drei Zeilen hoch, auch " +
            "wenn nichts darin stand — zwei grosse leere Kästen bei fast jedem Alias. Sie " +
            "sind jetzt einzeilig wie in den Programmen; steht doch etwas darin, lässt es " +
            "sich im Feld weiterrollen.",
        }),

        new("1.23.2", "25.08.2026", new[]
        {
            "Die Fassung für den Browser wertet Backups jetzt rund viermal so schnell " +
            "aus. Ein eigens erzeugtes Testarchiv mit 190.000 Datenpunkten brauchte " +
            "vorher 29 Sekunden und jetzt 7. Bei kleinen Anlagen merkt man es kaum, bei " +
            "grossen entscheidet es darüber, ob das Warten zumutbar ist.",

            "Der Grund liegt darin, wie das Programm in den Browser kommt. Bisher lag es " +
            "in einer Zwischensprache vor, die der Browser Schritt für Schritt las — für " +
            "eine Oberfläche belanglos, teuer aber bei einem Programm, das Zehntausende " +
            "JSON-Zeilen durchgeht. Jetzt wird es schon beim Erstellen in die Sprache " +
            "des Browsers übersetzt.",

            "Bezahlt wird das beim ersten Aufruf: Die Seite lädt nun 17,3 statt 7,5 " +
            "Megabyte. Danach liegt sie im Zwischenspeicher des Browsers, während ein " +
            "Backup bei jedem Blick neu eingelesen wird — deshalb ist der Tausch die " +
            "Sache wert.",

            "Am Arbeitsspeicher ändert sich nichts. Dasselbe Testarchiv belegte vorher " +
            "wie nachher 138 Megabyte; die Grenze von 512 MB je Backup bleibt bestehen.",
        }),

        new("1.23.1", "25.08.2026", new[]
        {
            "Im Reiter „Skripte\" steht der Kopierknopf jetzt direkt über der Vorschau — " +
            "dort, wo man den Quelltext liest. Bisher gab es ihn nur oben in der " +
            "Aktionsleiste, wo ihn beim Lesen niemand sucht; wer den Text brauchte, hat " +
            "ihn von Hand markiert.",

            "Seine Aufschrift sagt, was er mitnimmt: Bei einem Blockly-Skript steht dort " +
            "je nach Umschaltung „XML kopieren\" oder „JavaScript kopieren\", sonst " +
            "„Quelltext kopieren\". Der Unterschied ist keiner der Form — nur das XML " +
            "lässt sich in ioBroker wieder als Blockly einfügen.",

            "In der Browser-Fassung funktioniert das Kopieren jetzt auch dann, wenn die " +
            "Seite über http statt https ausgeliefert wird. Browser sperren dort den " +
            "üblichen Weg zur Zwischenablage; es gibt deshalb einen zweiten. Betroffen " +
            "war praktisch jeder, der die Seite im eigenen Netz betreibt.",
        }),

        new("1.23.0", "24.08.2026", new[]
        {
            "Neu: eine Fassung für den Browser. Sie wird einmal auf einen Webserver im " +
            "eigenen Netz gelegt und ist danach von jedem Rechner im Netz aufrufbar, " +
            "ohne Installation. Der Server muss dafür nichts können außer statische " +
            "Dateien ausliefern — gerechnet wird im Browser. Mitgeliefert sind Anleitung " +
            "und Einstellungen für den Apache; wer keinen betreibt, richtet ihn dafür ein.",

            "Das Backup wird dabei NICHT hochgeladen. Der Server liefert nur das Programm " +
            "aus; gelesen und ausgewertet wird es im Browser, auf dem Rechner davor. Wer " +
            "das nachsehen möchte: In den Entwicklerwerkzeugen ist unter „Netzwerk\" nach " +
            "dem Laden der Seite keine weitere Übertragung zu sehen.",

            "Alle Reiter sind dieselben wie in den Programmen für Windows, macOS und " +
            "Linux, und sie zeigen dieselben Zahlen — die Auswertung ist dieselbe.",

            "Zwei Unterschiede, die der Browser vorgibt: Wo die Programme einen Zielordner " +
            "anbieten — beim Export von Skripten und Dateien —, lädt diese Fassung ein ZIP " +
            "mit denselben Dateien darin herunter. Und während ein Backup gelesen wird, " +
            "steht die Seite still: In einem Browser rechnet dasselbe an der Auswertung, " +
            "was auch zeichnet.",

            "Angenommen werden Backups bis 512 MB. Ein Browser-Reiter hat rund 2 GB " +
            "Speicher zur Verfügung; übliche Backitup-Archive liegen bei 10 bis 30 MB. " +
            "Wie viel gerade belegt ist, steht unten rechts.",

            "Beim ersten Aufruf erscheint eine Prüfseite, die den Webserver durchmisst und " +
            "zu jedem Fehlbefund den passenden Befehl nennt. Danach nicht wieder.",
        }),

        new("1.22.2", "23.08.2026", new[]
        {
            "Die Analyse „unbenutzte Datenpunkte\" ist bei großen Anlagen nicht mehr " +
            "quälend langsam. Sie durchsuchte bisher für jeden einzelnen eigenen " +
            "Datenpunkt den kompletten VIS- und Skripttext. Solange beides klein ist, " +
            "fällt das nicht auf; bei einem umfangreichen VIS-Projekt und vielen tausend " +
            "Datenpunkten unter 0_userdata wuchs es sich zu Minuten aus, in denen nichts " +
            "anderes geschah — während alles Übrige zusammen wenige Sekunden brauchte.",

            "Der Text wird jetzt einmal abgesucht statt einmal je Datenpunkt. Gemessen an " +
            "erzeugten Testarchiven mit elf Megabyte VIS: bei 8.000 eigenen Datenpunkten " +
            "15 Millisekunden statt 26 Sekunden, und die Dauer wächst nicht mehr mit ihrer " +
            "Zahl. Die Befunde bleiben dabei buchstäblich dieselben — der Verifikationslauf " +
            "rechnet beide Wege parallel und vergleicht jeden Datenpunkt Feld für Feld.",

            "Die Statuszeile nennt jetzt auch die einzelnen Analyseschritte und stellt " +
            "jeder Meldung ein „Bitte warten\" voran — aus „Backup wird analysiert\" wird " +
            "„Bitte warten — Analyse 3/5: unbenutzte Datenpunkte …\". Bei einer großen " +
            "Anlage sieht man damit, woran das Programm gerade arbeitet, und dass es " +
            "arbeitet und nicht steht.",
        }),

        new("1.22.1", "23.08.2026", new[]
        {
            "Das Fenster friert beim Laden nicht mehr ein. Die aufwendigen Auswertungen — " +
            "Verwendung, verwaiste Datenpunkte und VIS — liefen bisher genau dort, wo die " +
            "Tabs ihre Daten bekamen, und das ist die Oberfläche selbst. Bei einer großen " +
            "Anlage stand das Fenster deshalb minutenlang still und wurde als „Keine " +
            "Rückmeldung\" gemeldet, obwohl das Programm arbeitete. Gerechnet wird jetzt " +
            "einmal im Hintergrund, und die Statuszeile nennt jeden Schritt beim Namen — " +
            "bis dahin blieb sie auf der letzten Meldung des Ladevorgangs stehen und " +
            "zeigte „VIS-Views werden gelesen\", während längst etwas anderes lief.",

            "Große JSON-Dateien im Dateibereich kosten keinen Arbeitsspeicher mehr. Für " +
            "die Backup-Prüfung wurde bisher jede Datei vollständig eingelesen — bei " +
            "einer Datei im dreistelligen Megabytebereich ein Vielfaches ihrer Größe, nur " +
            "um am Ende „gültig\" zu sagen. Geprüft wird jetzt strömend. Gemessen an einer " +
            "40 MB großen Datei: statt eines dreistelligen Megabyte-Zuwachses weniger als " +
            "ein Megabyte. Am Ergebnis ändert sich nichts, es wird nichts übersprungen.",

            "Ein zweites Backup im Backup führt nicht mehr in die Irre. Erkannt wurden " +
            "objects.jsonl, states.jsonl, backup.json und script.json bisher allein am " +
            "Dateinamen — eine gleichnamige Datei tief im Archiv, etwa aus einem Adapter, " +
            "der seine eigenen Daten mitsichert, ersetzte damit stillschweigend die echte " +
            "Objektliste. Jetzt gilt die Datei aus dem Wurzelordner; ein übergangener Fund " +
            "steht mit Pfad in der Backup-Prüfung. Gibt es oben nichts, wird der Fund von " +
            "unten weiterhin genommen — sonst ließe sich ein ungewohnt aufgebautes Archiv " +
            "gar nicht mehr laden.",

            "Neu ist ein Ladeprotokoll. Es liegt als „ladeprotokoll.txt\" dort, wo auch die " +
            "Einstellungen liegen, und wird bei jedem Laden neu geschrieben. Jede Zeile " +
            "geht sofort auf die Platte — auch ein abgeschossenes Programm hinterlässt " +
            "damit die Stelle, an der es nicht weiterkam. Das Protokoll enthält " +
            "ausschließlich Struktur: Schritte, Zeiten, Größen und Adapter-Namensräume, " +
            "aber keine Objekt-IDs, keine Werte, keine Namen von Skripten, Ansichten oder " +
            "Geräten und keine vollständigen Pfade. Es kann deshalb bedenkenlos " +
            "weitergegeben werden.",
        }),

        new("1.22.0", "23.08.2026", new[]
        {
            "Die Übersicht warnt, wenn eine Adapter-Instanz mehr Objekte hat, als ihr " +
            "Limit erlaubt. Über dem Filter erscheint dann eine Zeile mit den betroffenen " +
            "Instanzen, und ihre Objektzahl wird hervorgehoben. Es ist dieselbe Grenze, " +
            "die ioBroker selbst zieht: Der js-controller meldet bei jedem Start einer " +
            "solchen Instanz „This instance has N objects, the limit for this instance is " +
            "set to M.\" und legt eine System-Meldung an. Vorgabe sind 5.000 Objekte je " +
            "Instanz; manche Adapter bringen einen eigenen Wert mit. Das ist eine " +
            "Leistungswarnung, kein Defekt — viele Objekte verlangsamen Start, Admin und " +
            "Backup.",

            "Neuer Hinweis „Debug-Modus aktiv\" im Tab „Skripte\". Der Haken „Debuggen\" " +
            "im Editor ist kein Protokollschalter: Der javascript-Adapter führt das Skript " +
            "zwar aus, unterdrückt aber jede schreibende Operation — setState, exec und " +
            "writeFile passieren nicht, sondern werden nur als Warnung protokolliert. Ein " +
            "Skript mit vergessenem Haken läuft also und bewirkt nichts, ohne dass ein " +
            "Fehler auffällt. Ebenfalls neu: ein Hinweis auf eingeschaltete „Ausführliche " +
            "Protokollausgaben\", die im Dauerbetrieb das Log füllen.",

            "Neuer Hinweis zu „steuern\" und „aktualisieren\" in Blockly. Der Baustein " +
            "„Zustand steuern\" schreibt unquittiert (ack=false) und ist für " +
            "Adapter-Datenpunkte gedacht — der Adapter reagiert nur darauf und quittiert " +
            "selbst, sobald er den Befehl ausgeführt hat. „Zustand aktualisieren\" " +
            "schreibt quittiert (ack=true) und gehört zu selbst angelegten Datenpunkten. " +
            "Gemeldet wird beides falsch herum: „aktualisieren\" auf einem " +
            "Adapter-Datenpunkt, wo dann nichts passiert, und „steuern\" auf einem eigenen " +
            "Datenpunkt. Letzteres nur, wenn der Datenpunkt im Backup tatsächlich " +
            "unquittiert liegt und ihn kein anderes Skript als Befehl entgegennimmt — als " +
            "Befehlskanal zwischen zwei Skripten ist „steuern\" nämlich richtig. Ein " +
            "Sammelskript, das nur quittiert und sonst nichts tut, zählt dabei nicht als " +
            "Befehlskanal: Es macht unquittierte Werte in der Objektübersicht weiß, ändert " +
            "an der Ursache aber nichts.",
        }),

        new("1.21.1", "23.08.2026", new[]
        {
            "Die plattformübergreifende Fassung läuft jetzt tatsächlich unter Linux. " +
            "Dem Paket liegt ein Startskript „starte.sh\" bei, das vor dem Start prüft, ob " +
            "die beiden Systembibliotheken ICU und fontconfig vorhanden sind. Fehlen " +
            "sie, nennt es den passenden Installationsbefehl mit dem auf diesem System " +
            "gültigen Paketnamen. Vorher brach das Programm an dieser Stelle mit einem " +
            "englischen Fehlerbericht ab, aus dem nicht hervorging, was fehlt. Fehlt " +
            "eine Bildschirmoberfläche, wird auch das im Klartext gesagt.",

            "Zahlen werden überall deutsch dargestellt, unabhängig von der " +
            "Spracheinstellung des Rechners. Auf einem System ohne deutsche " +
            "Spracheinstellung stand bisher „16,576\" statt „16.576\": die " +
            "Beschriftung deutsch, die Zahlen englisch. Das betraf jede Bestandszahl " +
            "und fiel erst beim ersten Test auf einem echten Linux auf.",

            "Mehrere Tabellenspalten waren zu schmal für ihren Inhalt. In den Tabs " +
            "„Verwaiste Datenpunkte\" und „Verwendung\" waren Spaltenköpfe " +
            "abgeschnitten, die Angabe „Zuletzt geändert\" verlor bei vierstelligen " +
            "Tageszahlen das Ende, und in der Verwendungsliste stand „Deaktivier\" " +
            "statt „Deaktiviert\". Das war kein Linux-Problem: Es trat unter Windows " +
            "genauso auf und ist dort nur nie jemandem aufgefallen."
        }),

        new("1.21.0", "22.08.2026", new[]
        {
            "Der Tab „Skripte\" hat eine Spalte „Hinweise\". Sie meldet drei Muster im " +
            "Aufbau eines Blockly-Skripts, die im Betrieb Ärger machen: einen Auslöser " +
            "im Rumpf eines anderen Auslösers, einen vom javascript-Adapter selbst als " +
            "abgelöst gekennzeichneten Baustein („request\"), und einen Auslöser ohne " +
            "Inhalt. Unter der Liste steht zu jedem Befund, warum er einer ist und an " +
            "welchem Baustein er hängt — die Baustein-ID ist dieselbe, die auch der " +
            "Blockly-Editor führt. Der Filter „Nur mit Hinweisen\" zeigt die betroffenen " +
            "Skripte allein.",

            "Der wichtigste dieser Befunde ist der Auslöser im Auslöser: Er wird bei " +
            "jeder Auslösung des äußeren erneut angelegt und nie wieder entfernt, " +
            "sodass nach einigen Stunden dieselben Aktionen vielfach parallel laufen. " +
            "Der Blockly-Editor zeigt an dieser Stelle selbst ein Warndreieck — nur " +
            "sieht man es dort erst, wenn man das Skript ohnehin geöffnet hat.",

            "Geprüft wird ausschließlich Blockly. Dort hängt jeder Befund an einem " +
            "benannten Baustein; bei JavaScript und TypeScript ließe sich dasselbe nur " +
            "über Textsuche vermuten, und ein „on(\" in einem Kommentar wäre davon nicht " +
            "zu unterscheiden. Die Spalte bleibt bei diesen Skripten deshalb leer. Es " +
            "gibt auch keine Note und keine Punktzahl: Was dort steht, sind einzelne " +
            "Fundstellen mit Begründung."
        }),

        new("1.20.0", "21.08.2026", new[]
        {
            "Der Tab „VIS-Datenpunkte\" kann ein ganzes VIS-Projekt aus dem Backup als " +
            "ZIP-Datei schreiben — in genau dem Aufbau, den „Tools → Projektimport\" in " +
            "VIS 1 und VIS 2 erwartet. Damit lässt sich eine gelöschte Ansicht " +
            "zurückholen, ohne das Backup einzuspielen: Projekt importieren, die " +
            "vermisste Ansicht über „Views → Exportieren\" herausholen und im eigenen " +
            "Projekt wieder einfügen. Wahlweise nur die vis-views.json oder alles " +
            "mitsamt Bildern und CSS.",

            "Der vorgeschlagene Dateiname trägt den Projektnamen, die VIS-Version und " +
            "das Backup-Datum — in dieser Reihenfolge, und das mit Absicht: VIS trägt " +
            "beim Hineinziehen der Datei den Projektnamen selbst in den Import-Dialog " +
            "ein, und zwar den Dateinamen ohne ein führendes Datum. Wer nicht darauf " +
            "achtet, importiert unter diesem Namen. So kann er das laufende Projekt " +
            "nicht treffen, und zwei Importe aus verschiedenen Backups überschreiben " +
            "sich nicht gegenseitig.",

            "Exporte schreiben nicht mehr unmittelbar auf die Zieldatei, sondern " +
            "zunächst daneben und benennen erst am Ende um. Ein abgebrochener Lauf " +
            "hinterlässt damit kein Bruchstück, das wie ein fertiges Ergebnis aussieht. " +
            "Zeigt ein Speicherziel auf die geladene Backup-Datei, wird gar nicht erst " +
            "geschrieben; zeigt es auf ein anderes Archiv, kommt eine Rückfrage."
        }),

        new("1.19.1", "20.08.2026", new[]
        {
            "Die Analyse kann nicht mehr stillstehen. Die " +
            "Mustersuche in Skriptquelltexten, VIS-Ansichten und Dateinamen bricht nach " +
            "zwei Sekunden ab. Betroffen ist dann die einzelne Fundstelle — ein " +
            "Blockly-Skript erscheint ohne seine Grafik, ein Textfeld ohne seine " +
            "Bindings —, nicht der ganze Durchlauf. Ein reales Backup erreicht diese " +
            "Grenze nicht; sie ist die Absicherung dagegen, dass ein Backup mit " +
            "ungewöhnlichem Inhalt das Fenster ohne jede Erklärung einfriert."
        }),

        new("1.19.0", "19.08.2026", new[]
        {
            "Ein Skript ohne Angabe zum Aktiv-Status gilt nicht mehr als aktiv. Beim " +
            "Kopieren oder Importieren eines Skripts entsteht im Objekt kein Eintrag " +
            "„enabled\"; ioBroker startet ein solches Skript nicht. Bisher stand es " +
            "trotzdem überall als „Aktiv\" — in der Skriptliste, in der Verwendung und im " +
            "Export. Am deutlichsten war das in der Tabelle der Skripte zu einem " +
            "Datenpunkt: Dort erschien ein längst stillgelegtes Skript als aktiver " +
            "Schreiber. Aktiv ist ab jetzt nur, was ausdrücklich so gekennzeichnet ist; " +
            "die Gegenprobe ist der Laufzeit-Datenpunkt javascript.0.scriptEnabled.",

            "Der Tab „Verwendung\" benennt den Zustand, wenn im Backup keine " +
            "Adapter-Konfigurationen stehen: „keine Adapter-Konfigurationen im Backup — " +
            "diese Quelle fehlt hier ganz\". Damit ist „kein Adapter-Verweis\" nicht mehr " +
            "mit „keine vorhanden\" zu verwechseln."
        }),

        new("1.18.11", "18.08.2026", new[]
        {
            "Der Verlauf zeigt wieder die Fassungen 1.13.1 und 1.14.0. Beide gab es, beide " +
            "fehlten hier von Anfang an: 1.13.1 brachte die Spalte „Schreibbar\" in Analyse C, " +
            "1.14.0 den Skript-Export in der Ordnerstruktur des Admin. Der Eintrag zu 1.13.0 " +
            "hatte außerdem den Inhalt zweier anderer Fassungen mitgenommen und ist " +
            "richtiggestellt; sein Datum stand einen Tag zu spät.",

            "Die Verifikation prüft den Verlauf jetzt vollständig auf Lücken — jede " +
            "Versionsreihe und jede Fassung darin, nicht mehr nur die laufende. Was vor dem " +
            "ältesten Eintrag liegt, steht weiterhin nur in der Entwicklungshistorie."
        }),

        new("1.18.10", "18.08.2026", new[]
        {
            "Sicht „Auffällige Qualität\": Nur echte Störungen sind noch orange hervorgehoben " +
            "— nicht verbunden, meldet Fehler, allgemeines Problem. Start- und Ersatzwerte " +
            "stehen grau. Bisher war jede Zeile mit einem Code ungleich gut orange, und weil " +
            "das in der Praxis fast alle Zeilen betrifft, gingen die wenigen echten " +
            "Störungen darin unter. Unterschieden wird am Qualitätscode selbst: Seine unteren " +
            "drei Bits sind die Fehlerbits, die oberen sagen nur, woher ein Ersatzwert stammt.",

            "Der Verlauf zeigt wieder die Version 1.18.3. Ihr Eintrag war beim Anlegen von " +
            "1.18.4 überschrieben worden, sodass der Verlauf sechs Fassungen lang von 1.18.4 " +
            "auf 1.18.2 sprang. Die Verifikation prüft die laufende Versionsreihe jetzt auf " +
            "Lücken."
        }),

        new("1.18.9", "18.08.2026", new[]
        {
            "Der Änderungsverlauf hat einen eigenen Tab „Änderungen\" und steht nicht mehr " +
            "in der Hilfe. Dort war er der längste Abschnitt und stand gleich am Anfang: Wer " +
            "etwas nachschlagen wollte, musste erst an allen Versionen vorbeiscrollen. Der " +
            "neue Tab zeigt jede Fassung mit eigener Überschrift, neueste zuerst."
        }),

        new("1.18.8", "18.08.2026", new[]
        {
            "Die Spalte „Qualität\" im Tab „Verwaiste Datenpunkte\" (Sicht C) zeigt jetzt für " +
            "jeden Qualitätscode Klartext. Bisher stand dort bei Code 0x20 — dem mit Abstand " +
            "häufigsten — nur „Code 0x20\", und mehrere der übersetzten Codes waren falsch " +
            "beschriftet: 0x40 stand als „Gerät meldet Fehler\" da, obwohl es ein Ersatzwert " +
            "ist, und 0x44 als „Sensor nicht verbunden\", obwohl es die Fehlermeldung des " +
            "Geräts ist. Die Tabelle folgt jetzt vollständig dem Objekt-Schema des " +
            "js-controllers; ein Code außerhalb davon wird als „unbekannter Code\" ausgewiesen " +
            "statt stillschweigend als Zahl.",

            "Die Sicht heißt nicht mehr „Störungen\", sondern „Auffällige Qualität (Code " +
            "ungleich gut)\". Der Grund ist derselbe Befund: Der häufigste Code bedeutet " +
            "„Startwert, nie mit einem echten Wert beschrieben\" und ist keine Störung. Die " +
            "Hilfe erklärt die drei Gruppen — Startwert, Ersatzwert, echte Störung — und was " +
            "daraus jeweils folgt."
        }),

        new("1.18.7", "17.08.2026", new[]
        {
            "Verwendung: Ein Doppelklick auf ein Skript wechselt in den Tab „Skripte\" und " +
            "wählt es dort aus — die Vorschau zeigt sofort den Quelltext. Bisher musste man " +
            "sich den Pfad merken, den Tab wechseln und das Skript dort erneut suchen. Der " +
            "Sprung gilt in der Sicht „Skript → Datenpunkte\" für die obere Liste und in der " +
            "Sicht „Datenpunkt → Skripte\" für die untere; Adapter-Zeilen lösen nichts aus, " +
            "weil hinter ihnen kein Skript steht.",

            "Verdeckt die Filterung im Tab „Skripte\" das angesprungene Skript — etwa weil " +
            "dort noch ein Suchbegriff steht oder deaktivierte Skripte ausgeblendet sind —, " +
            "werden diese Filter zurückgesetzt. Sonst endete der Sprung in einer leeren Liste."
        }),

        new("1.18.6", "17.08.2026", new[]
        {
            "Aufräum-Skript: Die Frage „Wirklich löschen?\" vergleicht die Antwort jetzt " +
            "Zeichen für Zeichen statt über ein Muster. Der bisherige „case\"-Block hing an " +
            "der Shell-Option „nocasematch\": Wer das Skript mit „source aufraeumen.sh\" oder " +
            "„. aufraeumen.sh\" aus einer Shell holt, in der sie gesetzt ist, hätte auch mit " +
            "kleinem „j\" gelöscht. Mit „bash aufraeumen.sh\" war das nie der Fall — jetzt " +
            "unter keinen Umständen mehr. Gültig bleibt allein ein großes „J\" (oder „JA\"), " +
            "alles andere ist ein Testlauf.",

            "Das erzeugte Skript nennt im Kopf die Programmversion, aus der es stammt. Eine " +
            "gespeicherte .sh liegt oft noch Monate später auf dem Host, und ältere Fassungen " +
            "verhalten sich anders — bis v1.17.0 löschte dort auch ein kleines „j\".",

            "Die Verifikation spielt die Abfrage jetzt in einer echten bash durch: J und JA " +
            "löschen, j, ja, n, y, yes und die bloße Eingabetaste nicht. Vorher war nur " +
            "geprüft, dass die richtigen Zeichen im Skripttext stehen."
        }),

        new("1.18.5", "17.08.2026", new[]
        {
            "Aufräum-Skript: Es lassen sich jetzt einzelne Werte auswählen, nicht mehr nur " +
            "ganze Namensräume. Der Namensraum ist aufklappbar, darunter steht jeder " +
            "Waisen-State mit eigenem Häkchen; das Häkchen am Namensraum zeigt „teilweise\", " +
            "wenn nur ein Teil gewählt ist. Hintergrund: Ein Namensraum ist selten " +
            "durchgehend Müll — bisher musste man zu viel löschen oder gar nichts.",

            "Neues Suchfeld über der Auswahl. Es blendet nur aus und wählt nichts ab; eine " +
            "Zeile unter dem Baum nennt immer die Gesamtauswahl und weist darauf hin, wenn " +
            "die Suche gerade angehakte Werte verdeckt.",

            "Nach dem Speichern nennt der Hinweis SFTP (FileZilla, WinSCP) als Weg auf den " +
            "ioBroker-Host — der bisherige Vorschlag Samba ist auf einem ioBroker-Host " +
            "selten eingerichtet."
        }),

        new("1.18.4", "16.08.2026", new[]
        {
            "Tab „Verwendung\": Der Hinweis über der Tabelle behauptete noch, Adapter würden " +
            "nicht ausgewertet — er stammte von vor der Adapter-Auswertung. Jetzt steht dort, " +
            "was ein Adapter-Treffer wirklich bedeutet: Die ID ist in der Konfiguration " +
            "eingetragen; ob die Funktion benutzt wird, sagt das Backup nicht. Alte Einträge " +
            "bleiben dort stehen.",

            "Die Detailtabelle heißt jetzt „Skripte und Adapter, die … nennen\" — vorher stand " +
            "dort „Skripte, die … verwenden\", obwohl darunter Adapter-Zeilen erscheinen.",

            "Neue Spalte „Zuletzt geändert\" in der Datenpunkt-Sicht. Sie zeigt, wann der " +
            "Datenpunkt selbst zuletzt einen Wert bekam: Liegt das Jahre zurück, ist ein " +
            "Adapter-Eintrag darauf sicher eine Altlast. Umgekehrt beweist ein frischer " +
            "Zeitstempel nichts — ein Fühler sendet weiter, ganz gleich ob ein Adapter den " +
            "Wert noch verwendet."
        }),

        new("1.18.3", "16.08.2026", new[]
        {
            "Tab „Verwendung\": Der Hinweis über der Tabelle behauptete noch, Adapter würden " +
            "nicht ausgewertet — er stammte von vor der Adapter-Auswertung. Jetzt steht dort, " +
            "was ein Adapter-Treffer wirklich bedeutet: Die ID ist in der Konfiguration " +
            "eingetragen; ob die Funktion benutzt wird, sagt das Backup nicht. Alte Einträge " +
            "bleiben dort stehen.",

            "Die Detailtabelle heißt jetzt „Skripte und Adapter, die … nennen\" — vorher stand " +
            "dort „Skripte, die … verwenden\", obwohl darunter Adapter-Zeilen erscheinen."
        }),

        new("1.18.2", "16.08.2026", new[]
        {
            "Tab „Verwendung\": Die Fundstelle einer Adapter-Zeile nennt jetzt den Namen des " +
            "Eintrags aus der Adapter-Konfiguration — „power (customApps[0].objId)\" statt " +
            "nur des technischen Pfads. Übernommen werden ausschließlich Namensfelder; " +
            "verschlüsselte Werte und Benutzernamen bleiben außen vor."
        }),

        new("1.18.1", "16.08.2026", new[]
        {
            "Tab „Verwendung\": neuer Filter „In einem Adapter eingetragen\" — er zeigt alle " +
            "Datenpunkte mit Adapterbezug, auch die, auf die zusätzlich Skripte zugreifen. " +
            "Der bisherige Filter zeigte nur die skriptlosen Fälle und ließ damit gerade die " +
            "häufigsten Adapter-Datenpunkte aus."
        }),

        new("1.18.0", "16.08.2026", new[]
        {
            "Tab „Verwendung\": Datenpunkte, die eine Adapter-Instanz in ihrer eigenen " +
            "Konfiguration nennt (Shuttercontrol, awtrix-light, text2command …), zählen jetzt " +
            "als verwendet. Neue Spalte „Adapter\", neuer Filter „Nur im Adapter " +
            "eingetragen\", und die Detailtabelle nennt Instanz und Feld.",

            "Aufräum-Skript: Zum Löschen ist jetzt ein großes „J\" nötig — mit " +
            "Umschalttaste, damit ein versehentlicher Tastendruck nichts löscht.",

            "Hilfe: dieser Änderungsverlauf."
        }),

        new("1.17.0", "16.08.2026", new[]
        {
            "Neuer Tab „Verwendung\": Kreuzreferenz zwischen Skripten und Datenpunkten in " +
            "beide Richtungen, getrennt nach lesend und schreibend. Zeigt, welche Datenpunkte " +
            "von mehreren Skripten beschrieben werden — die häufigste Ursache für Werte, die " +
            "sich scheinbar von allein ändern — und welche Aliasse nie in einem Skript " +
            "gelandet sind.",

            "Exporte legen einen Ordner mit dem Namen der Backup-Datei an; zwei ausgewertete " +
            "Backups überschreiben einander nicht mehr.",

            "Aufräum-Skript lässt sich als Datei speichern (mit Linux-Zeilenenden) und fragt " +
            "beim Start selbst, ob es löschen oder nur testen soll. Kein DRY_RUN mehr von Hand.",

            "Die Anwendung ist als KI-erstellt gekennzeichnet — Titelleiste, Statusleiste, " +
            "Hilfe und jede erzeugte Datei."
        }),

        new("1.16.0", "15.08.2026", new[]
        {
            "Exporte folgen der Filterung: Was in der Liste steht, wird geschrieben — der " +
            "Knopf sagt die Zahl an."
        }),

        new("1.15.0", "15.08.2026", new[]
        {
            "Neuer Tab „Dateien\": der Dateibereich des Backups mit Namensraum, Pfad, " +
            "Größe und Typ, einzeln oder komplett exportierbar.",

            "Skript-Export standardmäßig im Ursprungsformat — Blockly als .xml, alles andere " +
            "als .js.",

            "Aliasse gelten nicht mehr als „Objekt ohne Wert\": Sie haben systembedingt " +
            "keinen eigenen Eintrag in der States-Datenbank (Fehlalarm behoben)."
        }),

        new("1.14.0", "14.08.2026", new[]
        {
            "Der Skript-Export legt einen Überordner „ioBroker-Skripte\" an, statt in den " +
            "gewählten Zielordner zu streuen — wer den Schreibtisch wählte, bekam dort vierzig " +
            "einzelne Skriptordner.",

            "Deaktivierte Skripte liegen nicht mehr unter „_DEAKTIVIERT\", sondern an ihrem " +
            "regulären Platz, mit „ (deaktiviert)\" im Dateinamen. Ob ein Skript gerade läuft, " +
            "ist ein Zustand und keine Ordnerebene; im Admin liegt es ebenfalls an derselben " +
            "Stelle, nur ausgegraut. Vorher fiel der halbe Baum auseinander, weil er ein " +
            "zweites Mal unter „_DEAKTIVIERT\" aufgebaut wurde.",

            "Der Bereich „common\"/„global\" steht wieder im Exportpfad. Ohne ihn landeten " +
            "script.js.global.X und script.js.common.X in derselben Datei, und die zweite " +
            "überschrieb die erste."
        }),

        new("1.13.1", "13.08.2026", new[]
        {
            "Analyse C hat eine Spalte „Schreibbar\". In „Objekte ohne Wert\" stehen auch " +
            "Datenpunkte, die gar nicht beschreibbar sind — ein Kontaktsensor etwa, der nur " +
            "liefert. An der Tabelle war das bisher nicht ablesbar. Die Spalte gilt für alle " +
            "fünf Sichten und den CSV-Export; bei States ohne Objekt bleibt sie leer, weil " +
            "dort kein Objekt existiert, das über die Schreibbarkeit etwas sagen könnte."
        }),
    };

    /// <summary>
    /// Der Verlauf als Hilfe-Blöcke — dieselbe Struktur, die auch die Hilfe rendert.
    /// Dadurch zeigt der Tab „Änderungen" den Text mit demselben Renderer und in
    /// derselben Typografie, ohne dass eine der beiden Oberflächen etwas Zusätzliches
    /// beherrschen muss.
    ///
    /// Bis Version 1.18.8 stand der Verlauf als ein Block in der Hilfe. Er ist dort
    /// herausgewachsen: Wer die Hilfe zum Nachschlagen öffnet, musste erst an elf
    /// Versionen vorbeiscrollen.
    /// </summary>
    public static IReadOnlyList<HelpBlock> Blocks { get; } = BuildBlocks();

    private static HelpBlock[] BuildBlocks()
    {
        var blocks = new List<HelpBlock>
        {
            new(HelpBlockKind.Title, "Änderungsverlauf"),
            new(HelpBlockKind.Text,
                "Was sich von Fassung zu Fassung geändert hat, neueste zuerst. Aufgeführt ist " +
                "nur, was für die Benutzung einen Unterschied macht. Die laufende Fassung steht " +
                "unten rechts in der Statusleiste.")
        };

        foreach (var e in Entries)
        {
            blocks.Add(new(HelpBlockKind.Heading, $"Version {e.Version}  ({e.Date})"));
            blocks.Add(new(HelpBlockKind.Text, string.Join("\n", e.Changes.Select(c => "• " + c))));
        }

        return blocks.ToArray();
    }
}
