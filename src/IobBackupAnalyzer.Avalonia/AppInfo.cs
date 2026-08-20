using System.Reflection;
using IobBackupAnalyzer.Core;

namespace IobBackupAnalyzer.Avalonia;

/// <summary>
/// Zentrale Versionsangabe, inhaltlich identisch zur WinForms-App. Quelle ist die
/// Assembly-Version (aus &lt;Version&gt; in der csproj), damit Titelleiste und
/// Statusleiste garantiert dasselbe zeigen.
/// </summary>
internal static class AppInfo
{
    public static string Name => AppIdentity.Name;

    /// <summary>
    /// Titelzeile des Fensters — wortgleich zur Windows-Fassung, samt Herkunftshinweis.
    /// </summary>
    public static string WindowTitle => $"{Name}  {ShortVersion}  —  {AppIdentity.AiNoticeShort}";

    /// <summary>Version in der Form „1.0.0".</summary>
    public static string Version
    {
        get
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version;
            return v is null ? "?" : $"{v.Major}.{v.Minor}.{v.Build}";
        }
    }

    /// <summary>
    /// Erstellungszeitpunkt des laufenden Programms — der Zeitstempel der ausführbaren
    /// Datei. So ist auch zwischen zwei Builds mit gleicher Versionsnummer erkennbar,
    /// welcher Stand läuft.
    /// </summary>
    public static DateTime? BuildTime
    {
        get
        {
            try
            {
                var path = Environment.ProcessPath;
                return string.IsNullOrEmpty(path) ? null : File.GetLastWriteTime(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                return null;
            }
        }
    }

    /// <summary>„v1.0.0" — kompakt für die Titelleiste.</summary>
    public static string ShortVersion => "v" + Version;

    /// <summary>„Version 1.0.0 · Build 10.08.2026 16:52" — ausführlich für die Statusleiste.</summary>
    public static string LongVersion
    {
        get
        {
            var b = BuildTime;
            var version = b is null
                ? $"Version {Version}"
                : $"Version {Version} · Build {b:dd.MM.yyyy HH:mm}";
            return $"{version} · {AppIdentity.AiNoticeShort}";
        }
    }
}
