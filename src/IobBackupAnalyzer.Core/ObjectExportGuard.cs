using System.Text.Json;

namespace IobBackupAnalyzer.Core;

/// <summary>
/// Entscheidet, welche Objekte der Objekt-Export standardmäßig zurückhält.
///
/// Zwei Gründe, ein Objekt nicht ungefragt in eine Importdatei zu schreiben:
/// <list type="number">
/// <item><b>Es trägt Zugangsdaten.</b> Benutzer, Zertifikate, die zentrale Ablage der
/// Zugangsdaten, Lizenzen, der Systemschlüssel in <c>system.config</c> — und jedes Objekt,
/// dessen <c>native</c> ein gefülltes Feld mit einem Namen wie password, token oder key
/// hat. Eine Importdatei wird herumgereicht, ins Forum gestellt, in Clouds abgelegt.</item>
/// <item><b>Es so zurückzuspielen schadet.</b> Adapter- und Instanzobjekte gehören dem
/// js-controller; über „Objekte hochladen" eingespielt, überschreiben sie Version,
/// Laufzeitdaten und Einstellungen einer installierten Instanz. Wiederhergestellt wird eine
/// Instanz über Backitup, nicht über einzelne Objekte.</item>
/// </list>
/// Zurückgehaltene Objekte stehen trotzdem in der Liste — mit Begründung — und lassen sich
/// bewusst freischalten.
/// </summary>
public static class ObjectExportGuard
{
    /// <summary>
    /// Der Grund, aus dem ein Objekt zurückgehalten wird, oder null. Kurz genug für eine
    /// Tabellenspalte.
    /// </summary>
    public static string? Reason(IobObject o)
    {
        var id = o.Id;

        if (id.StartsWith("system.adapter.", StringComparison.Ordinal))
            return "Adapter/Instanz – nicht so zurückspielen";
        if (id.StartsWith("system.host.", StringComparison.Ordinal))
            return "Host-Objekt – nicht so zurückspielen";
        if (id.StartsWith("system.user.", StringComparison.Ordinal))
            return "Benutzer (Passwort-Hash)";
        if (id.StartsWith("system.group.", StringComparison.Ordinal))
            return "Benutzergruppe (Rechte)";
        if (id == "system.certificates")
            return "Zertifikate und Schlüssel";
        if (id == "system.config")
            return "Systemeinstellungen mit Schlüssel";
        if (id.StartsWith(CredentialAnalyzer.CredentialPrefix, StringComparison.Ordinal))
            return "Zentrale Zugangsdaten";
        if (id == "system.licenses")
            return "Lizenzen";
        if (id == "system.meta.uuid")
            return "Kennung der Installation";
        if (o.NativeHasSecrets)
            return "Zugangsdaten im native-Abschnitt";

        return null;
    }

    /// <summary>
    /// true, wenn das Objekt verschlüsselte oder geschützte native-Felder ausweist oder sein
    /// <c>native</c> ein gefülltes Feld mit einem Zugangsdaten-Namen enthält — auch tiefer
    /// verschachtelt, etwa <c>native.devices[3].password</c>. Leere Felder zählen nicht.
    /// Vom <see cref="ObjectParser"/> beim Laden ermittelt; gespeichert wird nur das Ergebnis.
    /// </summary>
    internal static bool HasSecrets(JsonElement obj)
    {
        foreach (var listName in new[] { "encryptedNative", "protectedNative" })
        {
            if (obj.TryGetProperty(listName, out var l) && l.ValueKind == JsonValueKind.Array
                && l.GetArrayLength() > 0)
                return true;
            if (obj.TryGetProperty("common", out var c) && c.ValueKind == JsonValueKind.Object
                && c.TryGetProperty(listName, out var cl) && cl.ValueKind == JsonValueKind.Array
                && cl.GetArrayLength() > 0)
                return true;
        }

        return obj.TryGetProperty("native", out var native)
               && native.ValueKind == JsonValueKind.Object
               && Scan(native, 0);
    }

    private static bool Scan(JsonElement el, int depth)
    {
        // Tiefer als acht Ebenen liegt in keiner Adapter-Konfiguration ein Passwort; die
        // Grenze schützt vor entarteten Strukturen.
        if (depth > 8) return false;

        switch (el.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var p in el.EnumerateObject())
                {
                    if (IsSecretKey(p.Name) && IsFilled(p.Value)) return true;
                    if (p.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array
                        && Scan(p.Value, depth + 1))
                        return true;
                }
                return false;

            case JsonValueKind.Array:
                foreach (var item in el.EnumerateArray())
                    if (Scan(item, depth + 1)) return true;
                return false;

            default:
                return false;
        }
    }

    private static bool IsFilled(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.String => v.GetString()!.Length > 0,
        JsonValueKind.Number => true,
        JsonValueKind.Object => v.EnumerateObject().Any(),
        JsonValueKind.Array => v.GetArrayLength() > 0,
        _ => false
    };

    /// <summary>
    /// Name eines Zugangsdaten-Feldes. Wie <see cref="ShadowCredentialAnalyzer.IsKeyName"/>,
    /// dazu „key" für sich allein (<c>key</c>, <c>privateKey</c>, <c>api_key</c>): In einer
    /// Adapter-Konfiguration ist ein Schlüssel eher ein geheimer als ein Wörterbuchschlüssel —
    /// und ein unnötig zurückgehaltenes Objekt lässt sich freischalten, ein verschicktes
    /// Passwort nicht zurückholen.
    /// </summary>
    internal static bool IsSecretKey(string name)
    {
        if (ShadowCredentialAnalyzer.IsKeyName(name)) return true;
        var lower = name.ToLowerInvariant();
        if (lower == "key") return true;
        if (lower.EndsWith("_key", StringComparison.Ordinal)) return true;              // access_key
        if (name.Length > 3 && name.EndsWith("Key", StringComparison.Ordinal)) return true; // privateKey
        return lower.Contains("passphrase");
    }
}
