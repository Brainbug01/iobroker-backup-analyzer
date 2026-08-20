namespace IobBackupAnalyzer.App;

/// <summary>
/// Gemeinsame Maße der Kopfleisten aller Tabs — ein Ort statt sieben Kopien.
///
/// <b>Warum das nötig ist:</b> Ein frisch erzeugtes <see cref="Panel"/> ist 200 px breit.
/// Beim Docken wächst es zwar sofort auf die Fensterbreite, aber WinForms merkt sich den
/// Abstand rechts verankerter Kinder zum rechten Rand aus der Breite, die beim Hinzufügen
/// gilt. Sitzt das Kind bei x = 930, ist dieser Abstand also negativ — und beim ersten
/// Vergrößern wandert es aus dem sichtbaren Bereich. Genau so waren die Knöpfe „Als CSV
/// exportieren" in fünf Tabs unsichtbar, obwohl sie die ganze Zeit vorhanden waren.
///
/// <see cref="TopBar"/> setzt die Entwurfsbreite deshalb mit, und
/// <see cref="RightAligned"/> rechnet Positionen daraus aus, statt sie je Tab von Hand
/// zu schätzen. Wer eine neue Kopfleiste baut, erbt die Korrektur damit automatisch.
/// </summary>
internal static class TabLayout
{
    /// <summary>
    /// Breite, für die die Kopfleisten entworfen sind. Sie muss nicht der echten
    /// Fensterbreite entsprechen — sie legt nur fest, welche Abstände die Anker beim
    /// Wachsen beibehalten.
    /// </summary>
    public const int DesignWidth = 1100;

    /// <summary>
    /// Angedockte Leiste am oberen Rand, in Entwurfsbreite — für Tab-Köpfe ebenso wie
    /// für die Zwischenleisten über einzelnen Tabellen.
    /// </summary>
    public static Panel TopBar(int height, Padding? padding = null)
    {
        var p = new Panel { Dock = DockStyle.Top, Height = height, Width = DesignWidth };
        if (padding is { } pad) p.Padding = pad;
        return p;
    }

    /// <summary>
    /// Position für ein rechtsbündiges Control der Breite <paramref name="width"/>.
    /// Zusammen mit <c>Anchor = Top | Right</c> bleibt es beim Vergrößern am rechten Rand.
    /// </summary>
    public static Point RightAligned(int width, int y) => new(DesignWidth - width, y);
}
