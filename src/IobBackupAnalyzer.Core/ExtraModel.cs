namespace IobBackupAnalyzer.Core;

/// <summary>Eine Datenpunkt-ID aus einem Skript, zu der es kein Objekt gibt.</summary>
public sealed class DeadRefRow
{
    public required string ScriptId { get; init; }
    public required string ScriptName { get; init; }
    public bool ScriptEnabled { get; init; }
    public required string StateId { get; init; }

    /// <summary>
    /// true, wenn es den Namensraum gibt (hue.0 existiert, hue.0.Licht_alt nicht) — dann
    /// ist der Verdacht deutlich stärker, als wenn der ganze Adapter fehlt.
    /// </summary>
    public bool NamespaceExists { get; init; }

    public string VerdachtText => NamespaceExists
        ? "Adapter vorhanden, Datenpunkt fehlt"
        : "Namensraum fehlt ganz";

    public string StatusText => ScriptEnabled ? "Aktiv" : "Deaktiviert";
}
