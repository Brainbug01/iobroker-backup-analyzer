using System.Reflection;
using IobBackupAnalyzer.Core;

namespace IobBackupAnalyzer.Web;

/// <summary>
/// Zentrale Versionsangabe, inhaltlich identisch zu den beiden Desktop-Fassungen. Quelle
/// ist die Assembly-Version (aus &lt;Version&gt; in der csproj), damit Kopfzeile und
/// Fußzeile garantiert dasselbe zeigen.
/// </summary>
internal static class AppInfo
{
    public static string Name => AppIdentity.Name;

    /// <summary>Version in der Form „1.22.2".</summary>
    public static string Version
    {
        get
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version;
            return v is null ? "?" : $"{v.Major}.{v.Minor}.{v.Build}";
        }
    }

    /// <summary>„v1.22.2" — kompakt für die Kopfzeile.</summary>
    public static string ShortVersion => "v" + Version;

    /// <summary>
    /// Kein „Build …" wie in den Desktop-Fassungen: Dort ist das der Zeitstempel der EXE,
    /// den es hier nicht gibt. Der Zusatz nennt stattdessen die Bauform, weil ein Anwender
    /// im Zweifel wissen muss, ob er die Browser- oder die Desktop-Fassung vor sich hat.
    /// </summary>
    public static string LongVersion =>
        $"Version {Version} · Browser-Fassung · {AppIdentity.AiNoticeShort}";

    public static string Title => $"{Name}  {ShortVersion}  —  {AppIdentity.AiNoticeShort}";
}
