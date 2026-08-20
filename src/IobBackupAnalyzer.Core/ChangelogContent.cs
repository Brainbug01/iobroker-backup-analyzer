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
