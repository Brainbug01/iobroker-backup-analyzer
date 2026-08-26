using System.Text.Json;
using System.Text.RegularExpressions;

namespace IobBackupAnalyzer.Core;

/// <summary>Wie ein Widget-Satz in einem Projekt vorkommt.</summary>
public sealed class WidgetSetRow
{
    /// <summary>Name aus dem Feld <c>widgetSet</c> bzw. der Verzeichnisname eines Verweises.</summary>
    public required string Set { get; init; }

    /// <summary>Zugehörige Instanz, wenn eine gefunden wurde — sonst leer.</summary>
    public string Instance { get; init; } = "";

    /// <summary>Anzahl Widgets mit diesem <c>widgetSet</c> im VIS-1-Projekt.</summary>
    public int WidgetsVis1 { get; set; }

    /// <summary>Anzahl Widgets mit diesem <c>widgetSet</c> im VIS-2-Projekt.</summary>
    public int WidgetsVis2 { get; set; }

    /// <summary>Verweise auf Dateien des Satzes (<c>/name/bild.png</c>) im VIS-1-Projekt.</summary>
    public int FilesVis1 { get; set; }

    /// <summary>Verweise auf Dateien des Satzes im VIS-2-Projekt.</summary>
    public int FilesVis2 { get; set; }

    /// <summary>true, wenn der Satz zum Kern von VIS gehört und keinen eigenen Adapter hat.</summary>
    public bool BuiltIn { get; init; }

    /// <summary>true, wenn eine Instanz dazu im Backup steht.</summary>
    public bool Installed => Instance.Length > 0;

    public int Widgets => WidgetsVis1 + WidgetsVis2;
    public int Files => FilesVis1 + FilesVis2;
    public int Total => Widgets + Files;

    /// <summary>
    /// Die installierte VIS-2-Entsprechung dieses Satzes, falls es eine gibt — etwa
    /// <c>vis-2-widgets-inventwo</c> zu <c>vis-inventwo</c>. Sonst leer.
    /// </summary>
    public string Vis2Successor { get; init; } = "";

    /// <summary>
    /// Ein alter Satz, der in einer VIS-2-Ansicht steckt, <b>obwohl seine Nachfolge bereits
    /// installiert ist</b> — genau das, was beim Umstieg übrig bleibt.
    ///
    /// <b>Warum die Nachfolge dazugehört:</b> Ein fehlendes <c>vis-2-</c>-Präfix allein sagt
    /// nichts. Viele Adapter bringen ihre Widgets ohne solches Präfix mit und verwenden sie
    /// in beiden Fassungen — in der Referenzanlage <c>mytime</c>, <c>trashschedule</c> und
    /// <c>energiefluss-erweitert</c>. Die als Altlast zu melden, wäre schlicht falsch.
    /// Ein Befund entsteht erst, wenn zum alten Satz ein neuer bereitliegt und die Ansicht
    /// trotzdem den alten benutzt.
    /// </summary>
    public bool Vis1InVis2 =>
        (WidgetsVis2 > 0 || FilesVis2 > 0)
        && !BuiltIn
        && Vis2Successor.Length > 0
        && !Set.StartsWith("vis-2-", StringComparison.OrdinalIgnoreCase);

    public string WidgetsText => Widgets == 0 ? "—" : Widgets.ToString("N0");
    public string FilesText => Files == 0 ? "—" : Files.ToString("N0");

    public string ProjectText =>
        (WidgetsVis1 + FilesVis1 > 0, WidgetsVis2 + FilesVis2 > 0) switch
        {
            (true, true) => "VIS 1 + 2",
            (true, false) => "VIS 1",
            (false, true) => "VIS 2",
            _ => ""
        };

    /// <summary>
    /// true, wenn die Zeile nichts belegt, sondern nur eine Abwesenheit meldet. Genau diese
    /// Aussage ist die unsichere: Ein eingebettetes Symbol hinterlässt keinen Verweis, und
    /// ein VIS-Projekt, das nicht gesichert wurde, taucht hier gar nicht auf.
    /// </summary>
    public bool Uncertain => Installed && !BuiltIn && Total == 0;

    /// <summary>Kurzbefund für die Listenspalte.</summary>
    public string Verdict =>
        BuiltIn ? "Teil von VIS"
        : !Installed ? "Adapter fehlt im Backup"
        : Total == 0 ? "kein Verweis im Backup — vor dem Entfernen prüfen"
        : Vis1InVis2 ? $"alter Satz, {Vis2Successor} liegt bereit"
        : "in Gebrauch";
}

/// <summary>
/// Wertet aus, welche Widget-Sätze die VIS-Projekte tatsächlich verwenden — und welche
/// installierten Widget-Adapter im Backup nirgends vorkommen.
///
/// <b>Zwei Wege, nicht einer.</b> Ein Widget-Adapter wird auf zwei Arten in Anspruch
/// genommen, und wer nur den ersten prüft, kommt zu gefährlichen Schlüssen:
/// <list type="number">
/// <item>Als <b>Widget-Satz</b> — das Feld <c>widgetSet</c> am Widget.</item>
/// <item>Als <b>Dateiquelle</b> — ein Pfad wie <c>/vis-icontwo/Doors_Windows/door-open.png</c>
/// in einer Widget-Eigenschaft. In der Referenzanlage steht <c>vis-icontwo</c> in keinem
/// einzigen <c>widgetSet</c> und wird trotzdem 680-mal als Bildpfad verwendet.</item>
/// </list>
///
/// <b>Was sich nicht feststellen lässt.</b> VIS 2 bettet ausgewählte Symbole als
/// <c>data:image/svg+xml;base64,…</c> in das Projekt ein. Der dekodierte Inhalt trägt keinen
/// Hinweis auf seine Herkunft — in der Referenzanlage 519 solcher Symbole. Ein Icon-Satz für
/// VIS 2 kann also die Anzeige tragen und hier trotzdem ohne einen einzigen Verweis
/// dastehen. Deshalb heißt der Befund „kein Verweis gefunden" und nicht „ungenutzt", und
/// deshalb ist diese Liste eine Prüfliste, keine Deinstallationsliste.
/// </summary>
public static class WidgetSetAnalyzer
{
    /// <summary>
    /// Der Vorbehalt über der Liste. Er steht hier und nicht in den Oberflächen, weil er
    /// Teil der fachlichen Aussage ist und in allen drei Fassungen wortgleich erscheinen muss.
    ///
    /// <b>Warum so ausführlich:</b> Diese Liste verleitet zu einer Handlung, die sich nicht
    /// zurücknehmen lässt — einen Adapter zu deinstallieren. „Kein Verweis gefunden" ist aber
    /// keine Abwesenheit von Verwendung, sondern nur die Abwesenheit einer <i>Spur</i>.
    /// </summary>
    public const string Warning =
        "Prüfliste, keine Deinstallationsliste. „Kein Verweis im Backup\" heißt nicht, dass " +
        "ein Satz unbenutzt ist:\n" +
        "• VIS 2 bettet ausgewählte Symbole vollständig in das Projekt ein (data:image/…). " +
        "Der eingebettete Inhalt trägt keinen Hinweis darauf, aus welchem Satz er stammt — " +
        "ein Icon-Satz kann die Anzeige tragen und hier trotzdem ohne Verweis dastehen.\n" +
        "• Gezählt wird nur, was im Backup steht. Ein VIS-Projekt, das nicht gesichert wurde, " +
        "kommt hier nicht vor.\n" +
        "Vor dem Entfernen eines Adapters also im laufenden System gegenprüfen.";

    /// <summary>
    /// Sätze, die VIS selbst mitbringt. Sie haben keinen eigenen Adapter, und ihr Fehlen in
    /// der Instanzliste ist deshalb kein Befund.
    ///
    /// <c>vis-2-widgets-basic</c> gehört dazu: Trotz des Namensmusters der Zusatzpakete ist
    /// das der Grundbaukasten von VIS 2 — das Gegenstück zu <c>basic</c> in VIS 1 — und
    /// steckt im Adapter vis-2 selbst. Ohne diesen Eintrag meldete die Liste ihn als
    /// „Adapter fehlt im Backup", und zwar in jedem VIS-2-Projekt.
    /// </summary>
    private static readonly HashSet<string> BuiltInSets =
        new(StringComparer.OrdinalIgnoreCase)
        { "basic", "jqui", "vis", "vis-2-widgets-basic" };

    /// <summary>
    /// Ein Verweis auf eine Datei eines Adapters: <c>"/name/…</c> am Anfang eines Werts.
    /// Der Name muss mindestens drei Zeichen haben, sonst träfe die Regel auch Pfade wie
    /// <c>/a/b</c> aus Freitext.
    /// </summary>
    private static readonly Regex FileReference =
        new(@"""/([A-Za-z0-9][A-Za-z0-9._-]{2,40})/", RegexOptions.Compiled,
            RegexLimits.MatchTimeout);

    public static List<WidgetSetRow> Analyze(BackupData data, CancellationToken ct = default)
    {
        var rows = new Dictionary<string, WidgetSetRow>(StringComparer.OrdinalIgnoreCase);

        // Alle Instanzen als mögliche Herkunft, nicht nur die reinen Datei-Adapter: Ein
        // gewöhnlicher Adapter kann neben seinem Dienst sehr wohl Widgets mitbringen —
        // trashschedule, mytime und energiefluss-erweitert tun in der Referenzanlage genau
        // das. Nur auf onlyWWW zu sehen, meldete sie fälschlich als fehlend.
        var kandidaten = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var inst in data.Instances)
        {
            kandidaten[inst.Adapter] = inst.Namespace;

            // Der Satzname lässt das Präfix oft weg: Der Adapter vis-timeandweather liefert
            // den Satz „timeandweather".
            if (inst.Adapter.StartsWith("vis-", StringComparison.OrdinalIgnoreCase))
                kandidaten[inst.Adapter["vis-".Length..]] = inst.Namespace;
        }

        // Für die Dateiverweise gilt eine engere Auswahl: Gesucht wird ein Verzeichnis, das
        // ein Adapter ausliefert. Ohne diese Eingrenzung zählte auch /vis.0/ mit — das ist
        // der Projektordner selbst und kein Widget-Satz.
        var dateiQuellen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var inst in data.Instances)
        {
            if (!inst.OnlyWww) continue;

            dateiQuellen[inst.Adapter] = inst.Namespace;
            if (inst.Adapter.StartsWith("vis-", StringComparison.OrdinalIgnoreCase))
                dateiQuellen[inst.Adapter["vis-".Length..]] = inst.Namespace;
        }

        // Zu welchen Sätzen liegt bereits eine VIS-2-Fassung bereit? Der Adapter heißt dann
        // vis-2-widgets-<name>; verglichen wird der Kern des Namens, damit vis-inventwo und
        // vis-2-widgets-inventwo zueinanderfinden.
        var nachfolger = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var inst in data.Instances)
        {
            const string präfix = "vis-2-widgets-";
            if (!inst.Adapter.StartsWith(präfix, StringComparison.OrdinalIgnoreCase)) continue;

            var kern = inst.Adapter[präfix.Length..];
            nachfolger[kern] = inst.Adapter;
            nachfolger["vis-" + kern] = inst.Adapter;
        }

        WidgetSetRow Row(string set)
        {
            if (rows.TryGetValue(set, out var vorhanden)) return vorhanden;

            kandidaten.TryGetValue(set, out var instanz);
            nachfolger.TryGetValue(set, out var neuer);
            var neu = new WidgetSetRow
            {
                Set = set,
                Instance = instanz ?? "",
                Vis2Successor = neuer ?? "",
                BuiltIn = BuiltInSets.Contains(set)
            };
            rows[set] = neu;
            return neu;
        }

        foreach (var file in data.VisViews)
        {
            ct.ThrowIfCancellationRequested();

            var istVis2 = file.Version == VisVersion.Vis2;
            CountWidgetSets(file, istVis2, Row, ct);
            CountFileReferences(file, istVis2, dateiQuellen, Row);
        }

        // Installierte Datei-Instanzen, zu denen gar nichts gefunden wurde, gehören ebenfalls
        // in die Liste — sie sind der eigentliche Anlass, hier nachzusehen.
        foreach (var inst in data.Instances)
        {
            if (!inst.OnlyWww) continue;
            if (rows.Values.Any(r => r.Instance == inst.Namespace)) continue;

            rows[inst.Adapter] = new WidgetSetRow
            {
                Set = inst.Adapter,
                Instance = inst.Namespace,
                BuiltIn = false
            };
        }

        return rows.Values
                   .OrderByDescending(r => r.Total)
                   .ThenBy(r => r.Set, StringComparer.OrdinalIgnoreCase)
                   .ToList();
    }

    /// <summary>Zählt die Widgets je <c>widgetSet</c> — beide Projektfassungen getrennt.</summary>
    private static void CountWidgetSets(VisFile file, bool istVis2,
                                        Func<string, WidgetSetRow> row, CancellationToken ct)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(file.Content); }
        catch (JsonException) { return; }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return;

            foreach (var view in doc.RootElement.EnumerateObject())
            {
                ct.ThrowIfCancellationRequested();

                if (view.Value.ValueKind != JsonValueKind.Object) continue;
                if (!view.Value.TryGetProperty("widgets", out var widgets)) continue;
                if (widgets.ValueKind != JsonValueKind.Object) continue;

                foreach (var widget in widgets.EnumerateObject())
                {
                    if (widget.Value.ValueKind != JsonValueKind.Object) continue;
                    if (!widget.Value.TryGetProperty("widgetSet", out var ws)) continue;
                    if (ws.ValueKind != JsonValueKind.String) continue;

                    var name = ws.GetString();
                    if (string.IsNullOrWhiteSpace(name)) continue;

                    var r = row(name);
                    if (istVis2) r.WidgetsVis2++; else r.WidgetsVis1++;
                }
            }
        }
    }

    /// <summary>
    /// Zählt Verweise auf Dateien eines Satzes. Gesucht wird im Rohtext statt Feld für Feld:
    /// Solche Pfade stehen in beliebig benannten Eigenschaften — <c>iImageFalse</c>,
    /// <c>src</c>, <c>signals-icon-1</c> —, und eine Liste bekannter Feldnamen wäre nach dem
    /// nächsten Widget-Satz wieder unvollständig.
    ///
    /// Gezählt werden nur Verzeichnisse, die zu einer Datei-Instanz gehören; ein Verweis auf
    /// <c>/vis.0/…</c> ist eine eigene Datei des Projekts und kein Widget-Satz.
    /// </summary>
    private static void CountFileReferences(VisFile file, bool istVis2,
                                            IReadOnlyDictionary<string, string> kandidaten,
                                            Func<string, WidgetSetRow> row)
    {
        foreach (Match m in FileReference.Matches(file.Content))
        {
            var name = m.Groups[1].Value;
            if (!kandidaten.ContainsKey(name)) continue;

            var r = row(name);
            if (istVis2) r.FilesVis2++; else r.FilesVis1++;
        }
    }
}
