namespace IobBackupAnalyzer.Core;

/// <summary>
/// Name und Herkunftshinweis des Programms — an einer Stelle, damit Windows-Fassung,
/// plattformübergreifende Fassung, Hilfe und erzeugte Dateien dasselbe sagen.
///
/// <b>Warum die KI-Kennzeichnung?</b> Diese Anwendung ist vollständig von einer KI
/// geschrieben worden. Wer ein Werkzeug an sein Smart-Home-Backup lässt, soll wissen,
/// woher es stammt — deshalb steht der Hinweis nicht im Kleingedruckten, sondern in der
/// Titelleiste, in der Statusleiste, in der Hilfe und in jeder Datei, die das Programm
/// erzeugt.
/// </summary>
public static class AppIdentity
{
    public const string Name = "ioBroker Backup Analyzer";

    /// <summary>
    /// Versionsnummer für Dateien, die das Programm erzeugt. Quelle ist der oberste Eintrag
    /// des Änderungsverlaufs; die Verifikation prüft, dass er zu <c>&lt;Version&gt;</c> in
    /// beiden csproj passt. Core hat keine eigene Assembly-Version — die Oberflächen lesen
    /// ihre aus dem jeweils eigenen Assembly (siehe <c>AppInfo</c>).
    ///
    /// <b>Warum überhaupt in erzeugte Dateien?</b> Ein einmal gespeichertes Aufräum-Skript
    /// lebt auf dem ioBroker-Host weiter. Ohne Versionszeile sieht man einer solchen Datei
    /// nicht an, aus welcher Fassung sie stammt — und ältere Fassungen verhalten sich
    /// anders (bis v1.17.0 löschte dort auch ein kleines „j").
    /// </summary>
    public static string Version => ChangelogContent.Entries[0].Version;

    /// <summary>Kurzform für Titel- und Statusleiste.</summary>
    public const string AiNoticeShort = "mit KI erstellt";

    /// <summary>Einzeiler für Kommentarköpfe erzeugter Dateien (reines ASCII).</summary>
    public const string AiNoticeAscii =
        "Diese Software wurde vollstaendig mit KI erstellt (Claude, Anthropic).";

    /// <summary>
    /// Ausführliche Fassung für Hilfe und Infozeile. Der zweite Teil ist kein Kleinreden,
    /// sondern die ehrliche Einordnung: Geprüft ist die Auswertung gegen echte Backups,
    /// entschieden wird trotzdem vom Menschen davor.
    /// </summary>
    public const string AiNoticeLong =
        "Diese Anwendung wurde vollständig mit KI erstellt: Programmcode, Auswertungslogik und " +
        "sämtliche Texte stammen von Claude (Anthropic), erarbeitet in Claude Code. " +
        "Jede Auswertung ist gegen echte ioBroker-Backups verifiziert und das Werkzeug liest " +
        "ausschließlich — geschrieben wird nichts. Trotzdem gilt: Die Listen hier sind Prüflisten. " +
        "Was gelöscht oder geändert wird, entscheidest du.";
}
