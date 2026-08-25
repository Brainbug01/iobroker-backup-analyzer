using System.Text.RegularExpressions;

namespace IobBackupAnalyzer.Core;

/// <summary>
/// Datenpunkt-IDs, die in Skripten stehen, zu denen es aber kein Objekt gibt.
///
/// Anders als die übrigen Analysen findet diese keinen Müll, sondern einen <b>Fehler</b>:
/// ein Skript, das auf einen Datenpunkt zugreift, den es nicht gibt. Die Grenzen der
/// Erkennbarkeit stehen bei DeadReferences.
/// </summary>
public static class ExtraAnalyzer
{
    public static List<DeadRefRow> Analyze(BackupData data, CancellationToken ct = default)
        => DeadReferences(data, ct);

    /// <summary>
    /// Datenpunkt-IDs, die in einem Skript stehen, zu denen es aber kein Objekt gibt.
    ///
    /// Ein <c>setState('0_userdata.0.Tepmeratur', …)</c> mit Tippfehler läuft
    /// jahrelang ins Nichts, ohne dass etwas darauf hinweist. Ebenso ein Skript, das noch
    /// auf einen Datenpunkt eines längst entfernten Adapters schreibt.
    ///
    /// <b>Wie stark ein Treffer wiegt, entscheidet der Namensraum.</b> Existiert
    /// <c>hue.0</c>, nicht aber <c>hue.0.Licht_alt</c>, ist das ein deutlicher Hinweis:
    /// Der Adapter ist da, der Datenpunkt nicht. Fehlt dagegen der ganze Namensraum, kann
    /// es ebenso gut ein Skript für eine andere Anlage sein oder ein Adapter, der zur
    /// Laufzeit erst anlegt. Beides steht in der Liste, aber getrennt.
    ///
    /// <b>Grenzen.</b> Gesucht wird in Zeichenketten-Literalen. Setzt ein Skript seine IDs
    /// zur Laufzeit zusammen, steht hier bestenfalls der feste Anfang. Und ein Datenpunkt,
    /// den das Skript selbst mit <c>createState</c> anlegt, existiert im Backup zu Recht
    /// nicht — solche Fälle sind ausgenommen, solange der Aufruf erkennbar ist.
    /// </summary>
    private static List<DeadRefRow> DeadReferences(BackupData data, CancellationToken ct)
    {
        var known = new HashSet<string>(StringComparer.Ordinal);
        var namespaces = new HashSet<string>(StringComparer.Ordinal);

        foreach (var o in data.Objects)
        {
            ct.ThrowIfCancellationRequested();
            known.Add(o.Id);

            // Namensraum = die ersten beiden Ebenen (hue.0, 0_userdata.0, alias.0).
            var ns = Namespace(o.Id);
            if (ns.Length > 0) namespaces.Add(ns);
        }

        var result = new List<DeadRefRow>();
        var gesehen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var script in data.Scripts)
        {
            ct.ThrowIfCancellationRequested();

            // Selbst angelegte Datenpunkte gelten nicht als tot: Sie entstehen erst zur
            // Laufzeit und stehen deshalb zu Recht nicht im Backup.
            var selbstAngelegt = SelfCreated(script.SearchableCode);

            // CleanSource statt SearchableCode: Gesucht wird im erzeugten JavaScript, nicht
            // im Blockly-XML. Ein deaktivierter Block liegt zwar im XML, wird aber nicht
            // übersetzt und läuft nie — seine Datenpunkte fehlen dann zu Recht. Genau daran
            // ist diese Analyse zuerst gescheitert: Ein Skript mit abgeschaltetem Gaszähler-
            // Teil erschien als kaputt, obwohl es das nicht war.
            foreach (var kandidat in IdCandidates(script.CleanSource))
            {
                // Endet der Fund auf einen Punkt, ist es kein Datenpunkt, sondern der feste
                // Anfang einer ID, die das Skript zur Laufzeit vervollständigt
                // ('0_userdata.0.Astro.' + name). Darüber ist nichts auszusagen.
                if (kandidat.EndsWith(".", StringComparison.Ordinal)) continue;

                if (known.Contains(kandidat)) continue;
                if (selbstAngelegt.Contains(kandidat)) continue;

                var ns = Namespace(kandidat);
                if (ns.Length == 0) continue;

                // Ein Präfix-Treffer genügt: Steht im Skript nur der Anfang einer ID, weil
                // der Rest zur Laufzeit entsteht, ist das kein toter Verweis.
                if (known.Any(k => k.StartsWith(kandidat + ".", StringComparison.Ordinal))) continue;

                var schluessel = script.Id + "|" + kandidat;
                if (!gesehen.Add(schluessel)) continue;

                result.Add(new DeadRefRow
                {
                    ScriptId = script.Id,
                    ScriptName = script.Name,
                    ScriptEnabled = script.Enabled,
                    StateId = kandidat,
                    NamespaceExists = namespaces.Contains(ns),
                });
            }
        }

        // Die mit vorhandenem Namensraum zuerst — dort ist der Verdacht am stärksten.
        return result
            .OrderByDescending(r => r.NamespaceExists)
            .ThenBy(r => r.ScriptName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.StateId, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Die ersten beiden Ebenen einer ID, sonst leer.</summary>
    private static string Namespace(string id)
    {
        var first = id.IndexOf('.');
        if (first < 0) return "";

        var second = id.IndexOf('.', first + 1);
        return second < 0 ? id : id[..second];
    }

    /// <summary>
    /// Sieht aus wie eine Datenpunkt-ID: mindestens drei Ebenen, nur erlaubte Zeichen, und
    /// die zweite Ebene ist eine Instanznummer oder ein bekannter Sonderfall.
    ///
    /// Die Zeitgrenze ist Pflicht — ein Skript kann jede beliebige Zeichenkette enthalten,
    /// und ein Rückschritt im regulären Ausdruck darf die Auswertung nicht anhalten.
    /// </summary>
    /// <remarks>
    /// <c>\w</c> statt einer Buchstabenliste: Datenpunktnamen sind in der Praxis deutsch,
    /// und eine Liste aus A-Z hätte „…Gasverbrauch.Zählerstand_Aktuell" stillschweigend
    /// übergangen — der Treffer wäre nie geprüft worden. In .NET umfasst <c>\w</c> auch
    /// Umlaute und alles andere, was ioBroker in einer ID zulässt.
    /// </remarks>
    private static readonly Regex IdMuster = new(
        @"^[\w-]+\.\d+\.[\w.\-]+$",
        RegexOptions.Compiled,
        RegexLimits.MatchTimeout);

    private static readonly Regex CreateStateMuster = new(
        @"createState\s*\(\s*['""`]([^'""`]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        RegexLimits.MatchTimeout);

    private static HashSet<string> SelfCreated(string code)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);

        try
        {
            foreach (Match m in CreateStateMuster.Matches(code))
            {
                var id = m.Groups[1].Value;

                // createState('Name') legt unter javascript.<n>.<Name> an; der volle Pfad
                // steht im Skript nicht. Beide Schreibweisen aufnehmen.
                result.Add(id);
                result.Add("javascript.0." + id);
            }
        }
        catch (RegexMatchTimeoutException)
        {
            // Dann eben ohne diese Ausnahme — lieber ein Fehlalarm als ein Stillstand.
        }

        return result;
    }

    /// <summary>Alle Zeichenketten-Literale, die wie eine Datenpunkt-ID aussehen.</summary>
    private static IEnumerable<string> IdCandidates(string code)
    {
        foreach (var literal in Literals(code))
        {
            if (literal.Length is < 5 or > 200) continue;
            if (literal.IndexOf('.') < 0) continue;

            bool passt;
            try { passt = IdMuster.IsMatch(literal); }
            catch (RegexMatchTimeoutException) { continue; }

            if (passt) yield return literal;
        }
    }

    /// <summary>
    /// Zeichenketten aus JavaScript und Textinhalte aus Blockly-XML — dieselbe Grundlage,
    /// auf der auch die Verwendungsanalyse sucht.
    /// </summary>
    private static IEnumerable<string> Literals(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            var zeichen = text[i];

            // XML-Textinhalte: >…<
            if (zeichen == '>')
            {
                var ende = text.IndexOf('<', i + 1);
                if (ende < 0) yield break;

                var inhalt = text[(i + 1)..ende].Trim();
                if (inhalt.Length > 0) yield return inhalt;

                i = ende - 1;
                continue;
            }

            if (zeichen is not ('"' or '\'' or '`')) continue;

            var start = i + 1;
            var j = start;

            while (j < text.Length)
            {
                if (text[j] == '\\') { j += 2; continue; }
                if (text[j] == zeichen) break;
                if (zeichen != '`' && text[j] == '\n') break;
                j++;
            }

            if (j < text.Length && text[j] == zeichen && j > start)
                yield return text[start..j];

            i = j;
        }
    }
}
