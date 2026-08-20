namespace IobBackupAnalyzer.Core;

/// <summary>
/// Zeitgrenze für alle regulären Ausdrücke dieses Programms.
///
/// Die Muster hier sind bewusst einfach gehalten — verschachtelte Quantoren, an denen
/// katastrophales Backtracking entsteht, gibt es in keinem von ihnen. Trotzdem läuft ein
/// Teil davon über Inhalte, die das Programm nicht selbst erzeugt hat: Skriptquelltext,
/// VIS-Views, Dateinamen. Auf solche Eingaben ohne Zeitgrenze zu matchen heißt, sich
/// darauf zu verlassen, dass niemand je ein Muster ergänzt, das doch eskaliert — und dass
/// die Analyse nie stillsteht, ohne zu sagen warum.
///
/// Zwei Sekunden sind für jedes reale Backup unerreichbar weit weg und kosten im
/// Normalfall nichts. Schlägt die Grenze doch an, wird die betroffene Datei übersprungen
/// (siehe die Aufrufstellen), nicht der ganze Durchlauf abgebrochen.
/// </summary>
internal static class RegexLimits
{
    internal static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(2);
}
