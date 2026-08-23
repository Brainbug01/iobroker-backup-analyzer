using System.Text.Json;

namespace IobBackupAnalyzer.Core;

/// <summary>Gewählte Darstellung der Avalonia-Oberfläche.</summary>
public enum ThemeChoice
{
    /// <summary>Folgt der Einstellung des Betriebssystems.</summary>
    System,
    Light,
    Dark
}

/// <summary>
/// Fenstergröße, zuletzt geöffnete Datei und gewählte Darstellung. Wird bevorzugt neben
/// dem Programm abgelegt, damit es portabel bleibt; ist der Ordner schreibgeschützt
/// (USB-Stick, Programme-Ordner, macOS-App-Bundle), weicht es auf den
/// Anwendungsdaten-Ordner des Nutzers aus — unter Windows %APPDATA%, unter macOS
/// ~/Library/Application Support, unter Linux ~/.config.
///
/// Beide Oberflächen nutzen dieselbe Klasse, aber getrennte Dateien: Ein gemeinsamer
/// Fensterzustand zweier gleichzeitig laufender Programme wäre eher störend als hilfreich.
/// </summary>
public sealed class UserSettings
{
    public int WindowWidth { get; set; } = 1180;
    public int WindowHeight { get; set; } = 780;
    public bool Maximized { get; set; }
    public string? LastFile { get; set; }

    /// <summary>
    /// Nur für die Avalonia-Fassung; WinForms kennt keine Themenwahl und ignoriert den Wert.
    /// </summary>
    public ThemeChoice Theme { get; set; } = ThemeChoice.System;

    private static readonly Dictionary<string, string> ResolvedPaths = new(StringComparer.Ordinal);

    /// <summary>
    /// Der Ort, an dem eine Begleitdatei liegt — neben dem Programm, sonst im
    /// Anwendungsdatenordner. Auch das Ladeprotokoll nutzt diese Auflösung, damit alles,
    /// was das Programm ablegt, am selben Ort liegt.
    /// </summary>
    public static string ResolveFilePath(string fileName) => ResolvePath(fileName);

    private static string ResolvePath(string fileName)
    {
        if (ResolvedPaths.TryGetValue(fileName, out var known)) return known;

        var beside = Path.Combine(AppContext.BaseDirectory, fileName);
        string resolved;
        try
        {
            // Schreibbarkeit testen, statt sie anzunehmen.
            var probe = Path.Combine(AppContext.BaseDirectory, ".schreibtest.tmp");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            resolved = beside;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ioBroker-Backup-Analyzer");
            Directory.CreateDirectory(dir);
            resolved = Path.Combine(dir, fileName);
        }

        ResolvedPaths[fileName] = resolved;
        return resolved;
    }

    public static UserSettings Load(string fileName)
    {
        try
        {
            var path = ResolvePath(fileName);
            if (!File.Exists(path)) return new UserSettings();
            return JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(path)) ?? new UserSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new UserSettings();   // kaputte Einstellungen dürfen den Start nicht verhindern
        }
    }

    public void Save(string fileName)
    {
        try
        {
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ResolvePath(fileName), json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Einstellungen sind Komfort, kein Muss — Fehler hier bleiben still.
        }
    }
}
