using System.Globalization;

namespace IobBackupAnalyzer.Core;

/// <summary>
/// Vergleicht zwei Tabellenwerte so, wie ein Mensch sie erwarten würde, und erkennt dabei
/// selbst, womit es zu tun hat.
///
/// Grundlage ist der angezeigte Text, weil die Tabellen genau den enthalten. Jede andere
/// Lösung müsste in jeder Tabelle zusätzliche Sortierschlüssel mitführen — für ein
/// Werkzeug dieser Größe unverhältnismäßig.
///
/// Die Reihenfolge der Prüfungen ist bewusst gewählt:
/// <list type="number">
/// <item>Zahl — „1.064" ist im deutschen Format tausendvierundsechzig.</item>
/// <item>Datum — auch mit Zusatz wie „21.07.2018 22:39  (2941 T)". Muss <b>vor</b> der
/// Versionsprüfung stehen: „10.08.2026" sähe sonst wie die Version 10.8.2026 aus und
/// würde vor „21.07.2018" einsortiert.</item>
/// <item>Version — „1.10.0" ist neuer als „1.9.0", „3.28.3-beta.1" älter als „3.28.3".
/// Greift erst ab zwei Punkten und kommt sich deshalb mit der Tausenderschreibweise
/// nicht in die Quere.</item>
/// <item>alles Übrige alphabetisch.</item>
/// </list>
/// </summary>
public static class DisplayCompare
{
    /// <summary>Die Tabellen zeigen deutsche Zahlen- und Datumsformate.</summary>
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    public static int Compare(string? left, string? right)
    {
        var a = (left ?? "").Trim();
        var b = (right ?? "").Trim();

        // Leere Zellen gehören ans Ende — in beiden Richtungen, sonst füllen sie beim
        // Umdrehen der Sortierung den sichtbaren Bereich.
        if (a.Length == 0 && b.Length == 0) return 0;
        if (a.Length == 0) return 1;
        if (b.Length == 0) return -1;

        if (TryNumber(a, out var na) && TryNumber(b, out var nb))
            return na.CompareTo(nb);

        if (TryDate(a, out var da) && TryDate(b, out var db))
            return da.CompareTo(db);

        if (TryVersion(a, out var va, out var preA) && TryVersion(b, out var vb, out var preB))
        {
            var byNumber = CompareSegments(va, vb);
            if (byNumber != 0) return byNumber;

            // Bei gleichen Zahlen ist die Vorabversion die ältere: 3.28.3-beta.1 vor 3.28.3.
            if (preA != preB) return preA ? -1 : 1;
        }

        return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Erkennt Zahlen samt der Schreibweisen aus den Tabellen: „1.064" mit Tausenderpunkt,
    /// „+12" und „−3" als Differenz, „±0" für keine Änderung.
    /// </summary>
    public static bool TryNumber(string s, out double value)
    {
        // Das typografische Minus und das Plusminus-Zeichen kennt der Parser nicht.
        s = s.Replace('−', '-').Replace("±", "");

        return double.TryParse(s, NumberStyles.Number | NumberStyles.AllowLeadingSign,
                               German, out value);
    }

    /// <summary>
    /// Erkennt Versionsangaben mit mindestens zwei Punkten (1.9.0, 8.0.4, 3.28.3-beta.1).
    ///
    /// Bewusst streng: Der Teil vor einer Vorabkennung muss vollständig aus durch Punkte
    /// getrennten Zahlen bestehen. Eine lockerere Prüfung würde Zeitstempel wie
    /// „21.07.2018 22:39" als Version durchgehen lassen.
    /// </summary>
    /// <param name="preRelease">true bei einer Vorabkennung nach - oder + (beta, rc …).</param>
    public static bool TryVersion(string s, out int[] segments, out bool preRelease)
    {
        segments = Array.Empty<int>();

        var cut = s.IndexOfAny(new[] { '-', '+' });
        preRelease = cut >= 0;

        var core = cut < 0 ? s : s[..cut];
        if (core.Count(c => c == '.') < 2) return false;

        var parts = core.Split('.');
        var result = new int[parts.Length];

        for (var i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length == 0) return false;
            if (!parts[i].All(char.IsAsciiDigit)) return false;
            if (!int.TryParse(parts[i], out result[i])) return false;
        }

        segments = result;
        return true;
    }

    private static int CompareSegments(int[] a, int[] b)
    {
        for (var i = 0; i < Math.Max(a.Length, b.Length); i++)
        {
            var x = i < a.Length ? a[i] : 0;
            var y = i < b.Length ? b[i] : 0;
            if (x != y) return x < y ? -1 : 1;
        }
        return 0;
    }

    /// <summary>
    /// Erkennt die Datumsangaben der Tabellen. Sie tragen oft einen Zusatz — etwa
    /// „21.07.2018 22:39  (2941 T)" —, deshalb wird nur der vordere Teil geprüft.
    /// </summary>
    public static bool TryDate(string s, out DateTime value)
    {
        value = default;

        if (s.Length >= 16 && DateTime.TryParseExact(s[..16], "dd.MM.yyyy HH:mm",
                German, DateTimeStyles.None, out value))
            return true;

        return s.Length >= 10 && DateTime.TryParseExact(s[..10], "dd.MM.yyyy",
                   German, DateTimeStyles.None, out value);
    }
}
