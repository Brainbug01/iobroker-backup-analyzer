using System.Text.Json;

namespace IobBackupAnalyzer.Core;

/// <summary>
/// Sammelt beim Lesen der Objekte die Kennzeichen der Installation ein.
///
/// Wird für jedes Objekt aufgerufen und greift sich die zwei relevanten heraus:
/// <c>system.meta.uuid</c> (Installations-UUID) und das Host-Objekt (Name, Adresse,
/// Controller-Version). Alles andere wird ignoriert, damit der Durchlauf nichts kostet.
///
/// <c>system.config</c> wird bewusst nicht gelesen: Dort stehen Ort und Koordinaten des
/// Systems, die auf jedem geteilten Bildschirmfoto den Wohnort verraten würden — und zur
/// Unterscheidung zweier Systeme taugen sie ohnehin nicht.
/// </summary>
internal sealed class SystemIdentityReader
{
    /// <summary>
    /// Backitup ersetzt den echten Hostnamen im Backup durch diesen Platzhalter.
    /// Wer ihn anzeigt, zeigt nichts an: Taucht er auf, wird an der nächsten Stelle
    /// weitergesucht, statt den Platzhalter für einen Namen zu halten.
    /// </summary>
    internal const string HostnamePlaceholder = "$$__hostname__$$";

    private string _uuid = "";
    private string _hostname = "";
    private string _address = "";
    private string _controller = "";

    public void Feed(JsonElement el, string id, string type)
    {
        if (id == "system.meta.uuid")
        {
            if (el.TryGetProperty("native", out var n) && n.ValueKind == JsonValueKind.Object
                && n.TryGetProperty("uuid", out var u) && u.ValueKind == JsonValueKind.String)
                _uuid = u.GetString() ?? "";
            return;
        }

        if (type != "host") return;

        if (!el.TryGetProperty("common", out var common) || common.ValueKind != JsonValueKind.Object)
            return;

        _controller = ReadString(common, "installedVersion");
        _hostname = ResolveHostname(el, id, ReadString(common, "hostname"));

        if (common.TryGetProperty("address", out var addr) && addr.ValueKind == JsonValueKind.Array)
        {
            foreach (var a in addr.EnumerateArray())
            {
                if (a.ValueKind != JsonValueKind.String) continue;
                var s = a.GetString() ?? "";
                // Nur IPv4: die Link-Local-IPv6 enthält die MAC-Adresse und gehört nicht ins UI.
                if (s.Length > 0 && !s.Contains(':')) { _address = s; break; }
            }
        }
    }

    /// <summary>
    /// Findet den echten Hostnamen. Im Backup steht sowohl in der Objekt-ID als auch im
    /// Feld common.hostname der Platzhalter; das <c>from</c>-Feld trägt dagegen den
    /// tatsächlichen Namen (z. B. <c>system.host.raspi</c>).
    /// </summary>
    private static string ResolveHostname(JsonElement el, string id, string fromCommon)
    {
        if (fromCommon.Length > 0 && fromCommon != HostnamePlaceholder) return fromCommon;

        var fromId = Suffix(id, "system.host.");
        if (fromId.Length > 0 && fromId != HostnamePlaceholder) return fromId;

        if (el.TryGetProperty("from", out var f) && f.ValueKind == JsonValueKind.String)
        {
            var fromField = Suffix(f.GetString() ?? "", "system.host.");
            if (fromField.Length > 0 && fromField != HostnamePlaceholder) return fromField;
        }

        return "";
    }

    private static string Suffix(string value, string prefix) =>
        value.StartsWith(prefix, StringComparison.Ordinal) ? value[prefix.Length..] : "";

    private static string ReadString(JsonElement obj, string property) =>
        obj.TryGetProperty(property, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString() ?? "" : "";

    public SystemIdentity Build() => new()
    {
        InstallationId = _uuid,
        Hostname = _hostname,
        Address = _address,
        ControllerVersion = _controller
    };
}
