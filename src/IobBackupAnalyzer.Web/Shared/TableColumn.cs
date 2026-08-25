using IobBackupAnalyzer.Core;

namespace IobBackupAnalyzer.Web.Shared;

/// <summary>
/// Eine Spalte der Tabelle — das Gegenstück zu <c>DataGridTextColumn</c> in der
/// Avalonia-Fassung.
///
/// Die Breiten sind bewusst dieselben wie dort: Wer eine Anleitung für die Desktop-Fassung
/// liest, soll die Spalten hier an derselben Stelle wiederfinden.
/// </summary>
/// <typeparam name="T">Der Zeilentyp, etwa <see cref="AdapterInstance"/>.</typeparam>
public sealed class TableColumn<T>
{
    public required string Header { get; init; }

    /// <summary>Der anzuzeigende Text der Zelle.</summary>
    public required Func<T, string> Text { get; init; }

    /// <summary>Breite in Pixeln. Ohne Angabe richtet sie sich nach dem Inhalt.</summary>
    public int Width { get; init; } = 160;

    /// <summary>Zahlenspalte: rechtsbündig, damit sich Ziffern untereinander vergleichen lassen.</summary>
    public bool Number { get; init; }

    /// <summary>
    /// Wonach sortiert wird, wenn der Spaltenkopf angeklickt wird. Ohne Angabe nach dem
    /// angezeigten Text.
    ///
    /// Nötig überall dort, wo Anzeige und Ordnung auseinanderfallen: „Ja/Nein" gehört nach
    /// dem Wahrheitswert sortiert, ein Datum nach dem Zeitpunkt und nicht nach seiner
    /// deutschen Schreibweise.
    /// </summary>
    public Func<T, IComparable?>? SortKey { get; init; }

    /// <summary>Zusätzliche CSS-Klasse für alle Zellen dieser Spalte.</summary>
    public string? CellClass { get; init; }

    /// <summary>
    /// Eine CSS-Klasse, die von der Zeile abhängt — für Fälle, in denen nicht die ganze
    /// Zeile auffällt, sondern eine einzelne Zelle. In der Übersicht ist das die Spalte
    /// „Objekte": Die Instanz ist in Ordnung, allein ihr Objektbestand ist zu groß.
    /// </summary>
    public Func<T, string?>? CellClassOf { get; init; }

    /// <summary>Der Sortierschlüssel dieser Spalte — Text, falls keiner angegeben ist.</summary>
    internal IComparable? KeyOf(T item)
    {
        if (SortKey is not null) return SortKey(item);
        return Text(item);
    }
}

/// <summary>
/// Einstufung einer Zeile in der Farbgebung der Desktop-Fassungen. Die Namen der Klassen
/// stehen im Stylesheet.
/// </summary>
public static class EmphasisClass
{
    public static string Of(RowEmphasis e) => e switch
    {
        RowEmphasis.Muted => "zeile-gedaempft",
        RowEmphasis.Positive => "zeile-positiv",
        RowEmphasis.Warn => "zeile-warnung",
        RowEmphasis.Problem => "zeile-problem",
        _ => ""
    };
}
