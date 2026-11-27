using IobBackupAnalyzer.Core;

namespace IobBackupAnalyzer.Web.Services;

/// <summary>
/// Der gemeinsame Ablauf des Objekt-Exports für den Reiter „Objekte" und den Vergleich —
/// dasselbe wie in den Desktop-Fassungen: Bestätigung mit den Hinweisen, Datei aus dem
/// Backup lesen, herunterladen oder kopieren. Das Backup liegt dabei schon im Speicher des
/// Browser-Reiters; nichts verlässt den Rechner.
/// </summary>
public static class ObjektExport
{
    public static async Task AusfuehrenAsync(BrowserIo io, DialogService dialoge, BackupData daten,
                                             ObjectsPresenter.Selection auswahl, string dateiname,
                                             bool zwischenablage)
    {
        if (auswahl.Export.Count == 0)
        {
            await dialoge.MessageAsync("Hinweis",
                auswahl.Guarded.Count > 0
                    ? "Alle gewählten Objekte werden zurückgehalten (Zugangsdaten oder Adapter/Instanz).\n\n"
                      + "Wer sie trotzdem braucht, schaltet „Zurückgehaltene zulassen\" ein."
                    : "Bitte zuerst mindestens ein Objekt anhaken.");
            return;
        }

        if (!await dialoge.ConfirmAsync(
                zwischenablage ? "In die Zwischenablage kopieren" : "Als Import-JSON speichern",
                ObjectsPresenter.ConfirmText(auswahl) + "\n\nFortfahren?"))
            return;

        try
        {
            var ergebnis = ObjectExporter.Build(daten, auswahl.Export);

            if (zwischenablage)
            {
                var ok = await io.CopyAsync(ergebnis.Json);
                await dialoge.MessageAsync(ok ? "Fertig" : "Hinweis",
                    ok ? ObjectsPresenter.DoneText(ergebnis, "in die Zwischenablage kopiert")
                       : "Der Browser hat das Kopieren abgelehnt. Bitte stattdessen speichern.");
            }
            else
            {
                // UTF-8 ohne BOM — so, wie der Admin die Datei erwartet.
                await io.SaveTextAsync(dateiname, ergebnis.Json, "application/json", bom: false);
                if (ergebnis.Missing.Count > 0)
                    await dialoge.MessageAsync("Hinweis", ObjectsPresenter.DoneText(ergebnis, "gespeichert"));
            }
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException)
        {
            await dialoge.MessageAsync("Fehler", "Der Export ist fehlgeschlagen:\n\n" + ex.Message);
        }
    }
}
