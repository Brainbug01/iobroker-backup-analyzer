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
