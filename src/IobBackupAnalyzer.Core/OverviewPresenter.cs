namespace IobBackupAnalyzer.Core;

/// <summary>
/// UI-neutrale Logik des Übersicht-Tabs: Kopfzeile, Kennzahlen, Filter, Sortierung und
/// die CSV-Definition der beiden Tabellen.
///
/// <b>Warum hier und nicht im Tab?</b> Es gibt zwei Oberflächen — die WinForms-App
/// (Windows) und die Avalonia-App (Windows/macOS/Linux). Beide sollen dieselben Zahlen,
/// dieselbe Sortierreihenfolge und denselben CSV-Inhalt zeigen. Läge das je Oberfläche
/// doppelt vor, würde jede künftige Änderung zweimal gebaut und könnte zweimal
/// unterschiedlich falsch werden. Die Oberflächen bleiben damit reine Anzeige.
/// </summary>
public static class OverviewPresenter
{
    /// <summary>Spaltenreihenfolge der Instanztabelle — Index wie in der Anzeige.</summary>
    public static readonly string[] InstanceColumns =
        { "Adapter", "Instanz", "Version", "Aktiviert", "Objekte" };

    /// <summary>Spaltenreihenfolge der Tabelle „Adapter ohne eigene Instanz".</summary>
    public static readonly string[] NoInstanceColumns = { "Adapter", "Version" };

    /// <summary>„backup.tar.gz · Backup vom 10.08.2026 16:52" — Kopfzeile über den Kennzahlen.</summary>
    public static string HeaderText(BackupData data) =>
        $"{Path.GetFileName(data.SourceFile)}   ·   Backup vom {data.CreatedAt:dd.MM.yyyy HH:mm}";

    /// <summary>Die zwei Kennzahlenzeilen. Zeilentrenner ist <c>\n</c>.</summary>
    public static string MetricsText(BackupData data) =>
        $"Objekte: {data.Objects.Count:N0}     States: {data.StateCount:N0}     " +
        $"Adapter-Instanzen: {data.Instances.Count:N0}\n" +
        $"Skripte: {data.Scripts.Count:N0} ({data.ScriptsEnabled} aktiv, {data.ScriptsDisabled} deaktiviert)     " +
        $"Aliasse: {data.AliasCount:N0}     User-Datenpunkte: {data.UserDataCount:N0}     " +
        $"Enums: {data.EnumCount:N0}";

    /// <summary>
    /// Höchstens so viele Instanzen werden in der Warnzeile namentlich genannt. Darüber
    /// hinaus zählt sie die restlichen nur noch — sonst wird aus einem Hinweis eine Tabelle.
    /// </summary>
    private const int MaxNamedOverLimit = 8;

    /// <summary>
    /// Instanzen, deren Objektzahl über ihrem Limit liegt — größte zuerst. Genau diese
    /// beanstandet der js-controller beim Start mit „This instance has N objects, the limit
    /// for this instance is set to M." und legt dazu eine System-Meldung an
    /// (Kategorie numberObjectsLimitExceeded, Schweregrad alert).
    /// </summary>
    public static List<AdapterInstance> OverObjectLimit(BackupData data) =>
        data.Instances
            .Where(i => i.OverObjectLimit)
            .OrderByDescending(i => i.ObjectCount)
            .ThenBy(i => i.Adapter, StringComparer.OrdinalIgnoreCase)
            .ThenBy(i => i.Instance)
            .ToList();

    /// <summary>
    /// Die Warnzeile unter den Kennzahlen — <b>null</b>, wenn keine Instanz über ihrem Limit
    /// liegt. Dann zeigt die Oberfläche gar nichts an: Ein Hinweis, der immer dasteht, wird
    /// nicht gelesen.
    ///
    /// Deaktivierte Instanzen werden mitgezählt, aber gekennzeichnet — sie starten nicht und
    /// melden im Betrieb deshalb nichts. Ihre Objekte liegen trotzdem in der Datenbank.
    /// </summary>
    public static string? ObjectLimitWarning(BackupData data)
    {
        var over = OverObjectLimit(data);
        if (over.Count == 0) return null;

        var named = over.Take(MaxNamedOverLimit)
                        .Select(i => $"{i.Namespace}: {i.ObjectCount:N0} von {i.ObjectLimit:N0}"
                                     + (i.Enabled ? "" : " (deaktiviert)"));

        var list = string.Join("   ·   ", named);
        var rest = over.Count - MaxNamedOverLimit;
        if (rest > 0) list += $"   ·   … und {rest} weitere";

        var head = over.Count == 1
            ? "⚠ 1 Instanz über dem Objekt-Limit"
            : $"⚠ {over.Count} Instanzen über dem Objekt-Limit";

        return $"{head} — ioBroker meldet das bei jedem Start dieser Instanzen:\n{list}";
    }

    /// <summary>
    /// Erklärung zur Warnzeile, für den Kurzhinweis der Oberfläche. Bewusst kurz: Das Limit
    /// ist eine Leistungswarnung, kein Fehler — nichts ist kaputt, nur groß geworden.
    /// </summary>
    public const string ObjectLimitHint =
        "ioBroker warnt, sobald eine Instanz mehr Objekte hat als ihr Limit erlaubt " +
        "(Vorgabe 5.000, je Instanz einstellbar). Das ist eine Leistungswarnung, kein Defekt: " +
        "Viele Objekte verlangsamen Start, Admin und Backup. Meist lohnt der Blick, ob die " +
        "Instanz Datenpunkte anlegt, die niemand braucht.";

    /// <summary>
    /// Instanzen nach Adaptername filtern. Leerer Suchbegriff heißt „alles".
    /// Groß-/Kleinschreibung spielt hier bewusst keine Rolle — anders als bei ID-Vergleichen,
    /// weil der Nutzer den Namen tippt und nicht kopiert.
    /// </summary>
    public static List<AdapterInstance> Filter(BackupData data, string? term)
    {
        var t = (term ?? "").Trim();
        return t.Length == 0
            ? data.Instances.ToList()
            : data.Instances
                  .Where(i => i.Adapter.Contains(t, StringComparison.OrdinalIgnoreCase))
                  .ToList();
    }

    /// <summary>
    /// Sortiert nach Spaltenindex aus <see cref="InstanceColumns"/>. Die Zahlenspalten
    /// „Instanz" und „Objekte" werden numerisch sortiert — als Text stünde sonst 100 vor 20.
    /// Ein unbekannter oder negativer Index sortiert nach Adaptername (Grundzustand).
    /// </summary>
    public static List<AdapterInstance> Sort(IEnumerable<AdapterInstance> instances,
                                             int column, bool ascending)
    {
        var list = instances as IList<AdapterInstance> ?? instances.ToList();

        IOrderedEnumerable<AdapterInstance> sorted = column switch
        {
            1 => ascending ? list.OrderBy(i => i.Instance) : list.OrderByDescending(i => i.Instance),
            2 => ascending
                ? list.OrderBy(i => i.Version, StringComparer.OrdinalIgnoreCase)
                : list.OrderByDescending(i => i.Version, StringComparer.OrdinalIgnoreCase),
            3 => ascending ? list.OrderBy(i => i.Enabled) : list.OrderByDescending(i => i.Enabled),
            4 => ascending ? list.OrderBy(i => i.ObjectCount) : list.OrderByDescending(i => i.ObjectCount),
            _ => ascending
                ? list.OrderBy(i => i.Adapter, StringComparer.OrdinalIgnoreCase)
                : list.OrderByDescending(i => i.Adapter, StringComparer.OrdinalIgnoreCase)
        };

        // Stabiler Zweitschlüssel: gleiche Werte in der Sortierspalte behalten so eine
        // nachvollziehbare Reihenfolge statt einer zufälligen.
        return sorted.ThenBy(i => i.Adapter, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(i => i.Instance)
                     .ToList();
    }

    /// <summary>„12 von 47 Instanzen" bzw. „47 Instanzen", wenn nicht gefiltert wird.</summary>
    public static string CountText(int shown, int total) =>
        shown == total ? $"{total} Instanzen" : $"{shown} von {total} Instanzen";

    /// <summary>Überschrift der unteren Tabelle, samt Anzahl bzw. Entwarnung.</summary>
    public static string NoInstanceHeader(int count) =>
        count == 0
            ? "Installierte Adapter ohne eigene Instanz — keine (jeder installierte Adapter hat mindestens eine Instanz)"
            : $"Installierte Adapter ohne eigene Instanz — {count}";

    /// <summary>
    /// Ehrlicher Hinweis unter der Überschrift: instanzlos heißt nicht ungenutzt.
    /// Socket-Backends wie ws/socketio laufen bewusst ohne eigene Instanz.
    /// </summary>
    public const string NoInstanceHint =
        "Prüfliste, keine Löschliste: Manche Adapter laufen bewusst ohne eigene Instanz — " +
        "Socket-Backends wie ws/socketio (von admin/web genutzt) oder reine Abhängigkeiten.";

    /// <summary>
    /// Eine Instanz für die <b>Anzeige</b>, Spaltenreihenfolge wie <see cref="InstanceColumns"/>.
    /// Die Objektzahl bekommt Tausenderpunkte, weil sie in der Tabelle gelesen wird.
    /// </summary>
    public static string[] DisplayRow(AdapterInstance i) =>
        new[] { i.Adapter, i.Instance.ToString(), i.Version, i.EnabledText, i.ObjectCount.ToString("N0") };

    /// <summary>
    /// Dieselbe Instanz für den <b>CSV-Export</b> — bewusst ohne Tausenderpunkte: die
    /// würden im deutschen Format mit dem Spaltentrenner kollidieren und die Zahl in
    /// Tabellenkalkulationen unbrauchbar machen.
    /// </summary>
    public static string[] Row(AdapterInstance i) =>
        new[] { i.Adapter, i.Instance.ToString(), i.Version, i.EnabledText, i.ObjectCount.ToString() };

    /// <summary>Ein instanzloser Adapter als Zeile, Reihenfolge wie <see cref="NoInstanceColumns"/>.</summary>
    public static string[] Row(AdapterWithoutInstance a) => new[] { a.Adapter, a.Version };
}
