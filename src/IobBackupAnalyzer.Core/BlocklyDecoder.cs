using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace IobBackupAnalyzer.Core;

/// <summary>
/// Rekonstruiert das Blockly-XML aus dem common.source eines Blockly-Skripts.
///
/// Verifiziert gegen einen echten ioBroker-Admin-Export: das dekodierte XML ist nach
/// Normalisierung zeichengenau identisch zum Admin-Export (siehe STRUKTUR_VERIFIZIERUNG.md,
/// Abschnitt 4).
/// </summary>
public static class BlocklyDecoder
{
    /// <summary>
    /// Der Base64-Blob hängt als Zeilenkommentar am Ende des Sources. Das Zeichenset
    /// enthält "%", weil der dekodierte Inhalt selbst URL-kodiert ist und ältere
    /// Skripte den Blob teilweise unkodiert ablegen.
    /// </summary>
    private static readonly Regex Base64Comment =
        new(@"//([A-Za-z0-9+/=%]{50,})\s*$", RegexOptions.Compiled, RegexLimits.MatchTimeout);

    /// <summary>Fallback für ältere Skripte, die das XML im Klartext anhängen.</summary>
    private static readonly Regex RawXml =
        new(@"<xml\b.*?</xml>", RegexOptions.Compiled | RegexOptions.Singleline,
            RegexLimits.MatchTimeout);

    public sealed record Result(string? Xml, string CleanSource, bool Broken);

    /// <summary>
    /// Zerlegt einen Skript-Source in bereinigten Code und Blockly-XML.
    /// </summary>
    /// <param name="source">Roher common.source.</param>
    /// <param name="isBlockly">true, wenn engineType=Blockly ist. Steuert nur das Broken-Flag.</param>
    public static Result Decode(string? source, bool isBlockly)
    {
        source ??= "";

        try
        {
            // 1./2. Base64-Kommentar am Ende: dekodieren, UTF-8 lesen, URL-dekodieren.
            var m = Base64Comment.Match(source);
            if (m.Success)
            {
                var xml = TryDecodeBase64(m.Groups[1].Value);
                if (xml is not null)
                {
                    var clean = source[..m.Index].TrimEnd();
                    return new Result(PrettyPrint(xml), clean, false);
                }
            }

            // 3. Fallback: rohes <xml>…</xml> im Source suchen.
            var raw = RawXml.Match(source);
            if (raw.Success)
            {
                var clean = (source[..raw.Index] + source[(raw.Index + raw.Length)..]).Trim();
                return new Result(PrettyPrint(raw.Value), clean, false);
            }
        }
        catch (RegexMatchTimeoutException)
        {
            // Ein einzelnes Skript, an dem die Suche zu lange braucht, darf die Analyse
            // des Backups nicht beenden: Der Source bleibt unverändert erhalten und wird
            // wie ein Skript ohne gewinnbares XML behandelt.
            return new Result(null, source, isBlockly);
        }

        // 4. Kein XML gewinnbar. Bei engineType=Blockly ist das ein Defekt, sonst normal.
        return new Result(null, source, isBlockly);
    }

    /// <summary>
    /// Base64 → UTF-8 → URL-Dekodierung. Die URL-Dekodierung ist zwingend: ohne sie
    /// besteht das Ergebnis aus %3C%78%6D%6C-Sequenzen und ist unbrauchbar.
    /// </summary>
    private static string? TryDecodeBase64(string b64)
    {
        try
        {
            var bytes = Convert.FromBase64String(b64);
            var urlEncoded = Encoding.UTF8.GetString(bytes);
            var xml = Uri.UnescapeDataString(urlEncoded);
            return xml.TrimStart().StartsWith("<xml", StringComparison.OrdinalIgnoreCase) ? xml : null;
        }
        catch (FormatException)
        {
            return null;   // kein gültiges Base64
        }
        catch (ArgumentException)
        {
            return null;   // ungültige Escape-Sequenz beim URL-Dekodieren
        }
    }

    /// <summary>
    /// Formatiert das XML mit Einrückung — so sieht es aus wie ein Export aus dem
    /// ioBroker-Admin. Schlägt das Parsen fehl, wird das Original unverändert
    /// zurückgegeben (lieber unformatiert anzeigen als gar nichts).
    /// </summary>
    public static string PrettyPrint(string xml)
    {
        try
        {
            var doc = new XmlDocument { PreserveWhitespace = false };
            doc.LoadXml(xml);

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings
            {
                Indent = true,
                IndentChars = "  ",
                OmitXmlDeclaration = true,
                NewLineChars = "\r\n"
            };
            using (var w = XmlWriter.Create(sb, settings))
            {
                doc.Save(w);
            }
            return sb.ToString();
        }
        catch (XmlException)
        {
            return xml;
        }
    }
}
