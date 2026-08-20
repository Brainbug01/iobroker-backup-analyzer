namespace IobBackupAnalyzer.Core;

public enum DiffKind
{
    Unchanged,
    Added,
    Removed
}

/// <summary>Eine Zeile im Vergleichsergebnis samt ihrer Zeilennummern in beiden Fassungen.</summary>
public sealed class DiffLine
{
    public required DiffKind Kind { get; init; }
    public required string Text { get; init; }

    /// <summary>Zeilennummer in der älteren Fassung; null bei hinzugefügten Zeilen.</summary>
    public int? OldLine { get; init; }

    /// <summary>Zeilennummer in der neueren Fassung; null bei entfernten Zeilen.</summary>
    public int? NewLine { get; init; }

    public string Marker => Kind switch
    {
        DiffKind.Added => "+",
        DiffKind.Removed => "-",
        _ => " "
    };
}

public sealed class DiffResult
{
    public List<DiffLine> Lines { get; init; } = new();
    public int Added { get; init; }
    public int Removed { get; init; }

    /// <summary>
    /// true, wenn der Text zu groß für einen zeilengenauen Vergleich war und stattdessen
    /// die alte Fassung komplett als entfernt und die neue komplett als neu gilt.
    /// </summary>
    public bool Truncated { get; init; }

    public bool HasChanges => Added > 0 || Removed > 0;
}

/// <summary>
/// Zeilenweiser Textvergleich über die längste gemeinsame Teilfolge (LCS).
///
/// Bewusst ohne Fremdbibliothek (keine externen NuGet-Pakete). Damit die
/// quadratische Matrix nicht ausufert, werden zuerst gemeinsames Anfangs- und Endstück
/// abgezogen — bei einer typischen Skriptänderung bleibt danach nur ein kleiner Kern übrig.
/// </summary>
public static class TextDiff
{
    /// <summary>
    /// Obergrenze für die LCS-Matrix je Seite, nachdem gemeinsamer Anfang und gemeinsames
    /// Ende abgezogen wurden. 2.500 × 2.500 Zellen belegen kurzzeitig rund 25 MB — darüber
    /// wäre der Nutzen eines zeilengenauen Diffs den Speicher nicht mehr wert.
    /// </summary>
    private const int MaxMatrixSide = 2500;

    public static DiffResult Compare(string? oldText, string? newText)
    {
        var a = SplitLines(oldText);
        var b = SplitLines(newText);

        // Gemeinsamer Anfang.
        var start = 0;
        while (start < a.Length && start < b.Length && a[start] == b[start]) start++;

        // Gemeinsames Ende (ohne in den bereits verbrauchten Anfang zu laufen).
        var endA = a.Length - 1;
        var endB = b.Length - 1;
        while (endA >= start && endB >= start && a[endA] == b[endB]) { endA--; endB--; }

        var midA = a[start..(endA + 1)];
        var midB = b[start..(endB + 1)];

        if (midA.Length > MaxMatrixSide && midB.Length > MaxMatrixSide)
            return Wholesale(a, b);

        var lines = new List<DiffLine>(a.Length + b.Length);

        // Unveränderter Anfang.
        for (var i = 0; i < start; i++)
            lines.Add(new DiffLine { Kind = DiffKind.Unchanged, Text = a[i], OldLine = i + 1, NewLine = i + 1 });

        var added = 0;
        var removed = 0;
        foreach (var d in DiffCore(midA, midB, start))
        {
            lines.Add(d);
            if (d.Kind == DiffKind.Added) added++;
            else if (d.Kind == DiffKind.Removed) removed++;
        }

        // Unverändertes Ende.
        for (var i = endA + 1; i < a.Length; i++)
        {
            var offset = i - (endA + 1);
            lines.Add(new DiffLine
            {
                Kind = DiffKind.Unchanged,
                Text = a[i],
                OldLine = i + 1,
                NewLine = endB + 2 + offset
            });
        }

        return new DiffResult { Lines = lines, Added = added, Removed = removed };
    }

    /// <summary>Nur die Änderungszahlen — ohne die Zeilenliste aufzubauen.</summary>
    public static (int Added, int Removed) CountChanges(string? oldText, string? newText)
    {
        var r = Compare(oldText, newText);
        return (r.Added, r.Removed);
    }

    /// <summary>Fallback für sehr große Texte: alt komplett raus, neu komplett rein.</summary>
    private static DiffResult Wholesale(string[] a, string[] b)
    {
        var lines = new List<DiffLine>(a.Length + b.Length);
        for (var i = 0; i < a.Length; i++)
            lines.Add(new DiffLine { Kind = DiffKind.Removed, Text = a[i], OldLine = i + 1 });
        for (var i = 0; i < b.Length; i++)
            lines.Add(new DiffLine { Kind = DiffKind.Added, Text = b[i], NewLine = i + 1 });

        return new DiffResult { Lines = lines, Added = b.Length, Removed = a.Length, Truncated = true };
    }

    /// <summary>
    /// Klassische LCS-Matrix mit anschließendem Rückwärtslauf. <paramref name="offset"/> ist
    /// die Zahl der zuvor abgezogenen gemeinsamen Anfangszeilen und dient nur dazu, die
    /// ausgegebenen Zeilennummern wieder auf den Originaltext zu beziehen.
    /// </summary>
    private static List<DiffLine> DiffCore(string[] a, string[] b, int offset)
    {
        var result = new List<DiffLine>();

        if (a.Length == 0 && b.Length == 0) return result;

        // Einseitige Fälle brauchen keine Matrix.
        if (a.Length == 0)
        {
            for (var j = 0; j < b.Length; j++)
                result.Add(new DiffLine { Kind = DiffKind.Added, Text = b[j], NewLine = offset + j + 1 });
            return result;
        }
        if (b.Length == 0)
        {
            for (var i = 0; i < a.Length; i++)
                result.Add(new DiffLine { Kind = DiffKind.Removed, Text = a[i], OldLine = offset + i + 1 });
            return result;
        }

        var lcs = new int[a.Length + 1, b.Length + 1];
        for (var i = a.Length - 1; i >= 0; i--)
            for (var j = b.Length - 1; j >= 0; j--)
                lcs[i, j] = a[i] == b[j]
                    ? lcs[i + 1, j + 1] + 1
                    : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);

        var x = 0;
        var y = 0;
        while (x < a.Length && y < b.Length)
        {
            if (a[x] == b[y])
            {
                result.Add(new DiffLine
                {
                    Kind = DiffKind.Unchanged,
                    Text = a[x],
                    OldLine = offset + x + 1,
                    NewLine = offset + y + 1
                });
                x++; y++;
            }
            else if (lcs[x + 1, y] >= lcs[x, y + 1])
            {
                result.Add(new DiffLine { Kind = DiffKind.Removed, Text = a[x], OldLine = offset + x + 1 });
                x++;
            }
            else
            {
                result.Add(new DiffLine { Kind = DiffKind.Added, Text = b[y], NewLine = offset + y + 1 });
                y++;
            }
        }

        while (x < a.Length)
        {
            result.Add(new DiffLine { Kind = DiffKind.Removed, Text = a[x], OldLine = offset + x + 1 });
            x++;
        }
        while (y < b.Length)
        {
            result.Add(new DiffLine { Kind = DiffKind.Added, Text = b[y], NewLine = offset + y + 1 });
            y++;
        }

        return result;
    }

    /// <summary>
    /// Zerlegt in Zeilen und normalisiert dabei die Zeilenenden — sonst gälte eine Datei,
    /// die nur von LF auf CRLF gewechselt ist, als komplett geändert.
    /// </summary>
    private static string[] SplitLines(string? text)
    {
        if (string.IsNullOrEmpty(text)) return Array.Empty<string>();
        return text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
    }
}
