using System.Text.RegularExpressions;

namespace IobBackupAnalyzer.Core;

/// <summary>Wie ein Skript mit Zugangsdaten umgeht — die Aussage der Listenspalte.</summary>
public enum CredentialStatus
{
    /// <summary>Weder Klartext gefunden noch der zentrale Speicher benutzt.</summary>
    None,
    /// <summary>Benutzt <c>SECRETS</c>, und im Quelltext steht nichts mehr im Klartext.</summary>
    Converted,
    /// <summary>Zugangsdaten stehen im Quelltext; der zentrale Speicher wird nicht benutzt.</summary>
    Plaintext,
    /// <summary>Beides: <c>SECRETS</c> wird benutzt, aber es steht noch Klartext im Skript.</summary>
    Mixed
}

/// <summary>Woran eine Klartext-Fundstelle erkannt wurde.</summary>
public enum CredentialFindingKind
{
    /// <summary><c>…&amp;password=geheim</c> in einer Adresse.</summary>
    UrlParameter,
    /// <summary><c>http://benutzer:geheim@host</c>.</summary>
    UrlUserInfo,
    /// <summary><c>apiKey = 'geheim'</c> oder <c>password: "geheim"</c>.</summary>
    Assignment,
    /// <summary><c>'Bearer geheim'</c> oder <c>'Basic geheim'</c>.</summary>
    AuthHeader
}

/// <summary>Eine Klartext-Fundstelle in einem Skript.</summary>
/// <param name="Kind">Welches Muster angeschlagen hat.</param>
/// <param name="Line">Zeile im Quelltext, 1-basiert — bei Blockly im erzeugten JavaScript.</param>
/// <param name="Masked">
/// Die Fundstelle zum Wiederfinden, mit geschwärztem Wert: <c>&amp;password=&lt;8 Zeichen&gt;</c>.
/// Der Wert selbst wird nirgends gespeichert — was nicht im Modell steht, kann in keiner
/// Anzeige, keinem CSV und keinem Bildschirmfoto auftauchen.
/// </param>
/// <param name="InComment">
/// true, wenn die Zeile auskommentiert ist. Gemeldet wird sie trotzdem: Ein beim Umbau
/// stehen gelassenes <c>// alte Adresse</c> trägt das Passwort genauso ins Forum wie eine
/// laufende Zeile.
/// </param>
public sealed record CredentialFinding(CredentialFindingKind Kind, int Line, string Masked,
                                       bool InComment = false)
{
    public string KindText => Kind switch
    {
        CredentialFindingKind.UrlParameter => "URL-Parameter",
        CredentialFindingKind.UrlUserInfo => "Anmeldung in der Adresse",
        CredentialFindingKind.Assignment => "Zuweisung",
        _ => "Authorization-Kopfzeile"
    };
}

/// <summary>Ein Zugriff auf den zentralen Speicher: <c>SECRETS.Name.feld</c>.</summary>
/// <param name="Name">Name des Eintrags; leer, wenn er sich nicht aus dem Quelltext ergibt.</param>
/// <param name="Field">Das gelesene Feld; leer, wenn das Skript den Eintrag als Ganzes holt.</param>
public sealed record SecretUse(string Name, string Field);

/// <summary>Das Ergebnis der Zugangsdaten-Prüfung für ein Skript.</summary>
public sealed class ScriptCredentials
{
    public IReadOnlyList<CredentialFinding> Findings { get; init; } = Array.Empty<CredentialFinding>();
    public IReadOnlyList<SecretUse> Uses { get; init; } = Array.Empty<SecretUse>();

    /// <summary>
    /// Benutzte Eintragsnamen, zu denen es im Backup kein Objekt <c>system.credentials.*</c>
    /// gibt — Tippfehler oder ein inzwischen gelöschter Eintrag. Vom Lader gesetzt, weil
    /// erst er den Objektbestand kennt.
    /// </summary>
    public IReadOnlyList<string> UnknownNames { get; set; } = Array.Empty<string>();

    public CredentialStatus Status =>
        Findings.Count > 0 ? (Uses.Count > 0 ? CredentialStatus.Mixed : CredentialStatus.Plaintext)
        : Uses.Count > 0 ? CredentialStatus.Converted
        : CredentialStatus.None;

    /// <summary>Text der Listenspalte.</summary>
    public string Text
    {
        get
        {
            var text = Status switch
            {
                CredentialStatus.Converted => "umgebaut",
                CredentialStatus.Plaintext => $"Klartext ({Findings.Count})",
                CredentialStatus.Mixed => $"gemischt ({Findings.Count} Klartext)",
                _ => ""
            };
            if (UnknownNames.Count > 0)
                text += $", unbekannt: {string.Join(", ", UnknownNames)}";
            return text;
        }
    }
}

/// <summary>Ein Eintrag des zentralen Speichers — nur Name und Feldnamen, nie Werte.</summary>
public sealed record CredentialEntry(string Name, IReadOnlyList<string> Fields);

/// <summary>Die Zugangsdaten-Prüfung über das ganze Backup.</summary>
public sealed class CredentialReport
{
    /// <summary>Version des javascript-Adapters; leer, wenn sie nicht im Backup steht.</summary>
    public string JavascriptVersion { get; init; } = "";

    /// <summary>Version des admin-Adapters; leer, wenn sie nicht im Backup steht.</summary>
    public string AdminVersion { get; init; } = "";

    /// <summary>
    /// true, wenn beide Voraussetzungen erfüllt sind: Der Admin hat die zentrale Ablage
    /// (System → Zugangsdaten, ab 8.0.0), und der javascript-Adapter kennt <c>SECRETS</c>
    /// (ab 10.1.1). Nur dann wird geprüft: Wer die Funktion nicht hat, kann nicht umbauen,
    /// und eine Liste von Fundstellen ohne Abhilfe wäre bloß ein Vorwurf.
    /// </summary>
    public bool Supported { get; init; }

    /// <summary>Einträge unter <c>system.credentials.*</c>.</summary>
    public IReadOnlyList<CredentialEntry> Entries { get; init; } = Array.Empty<CredentialEntry>();

    /// <summary>Einträge, die kein Skript benutzt.</summary>
    public IReadOnlyList<string> UnusedEntries { get; init; } = Array.Empty<string>();

    /// <summary>
    /// javascript-Instanzen, in denen <c>native.enableSecrets</c> ausdrücklich auf false
    /// steht. Fehlt das Feld, steht die Instanz nicht hier — „nicht bekannt" ist nicht „aus".
    /// </summary>
    public IReadOnlyList<string> InstancesWithSecretsOff { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Prüft, ob Skripte ihre Zugangsdaten aus dem zentralen Speicher des Admins holen
/// (<c>SECRETS.Name.feld</c>, javascript-Adapter ab 10.1.1) oder noch im Quelltext tragen.
///
/// <b>Warum Textsuche, obwohl <see cref="ScriptQualityAnalyzer"/> sie ablehnt?</b> Dort geht
/// es um Programmstruktur — ein <c>on(</c> in einer Zeichenkette ist kein Auslöser. Hier ist
/// die Zeichenkette selbst der Befund: Ein Passwort in einer Adresse ist ein Passwort im
/// Skript, gleich ob die Zeile läuft, auskommentiert ist oder aus Blockly erzeugt wurde.
/// Geprüft wird deshalb bei allen Skripttypen der Quelltext, bei Blockly der erzeugte.
///
/// <b>Warum nicht exakt?</b> Das Backup enthält den Schlüssel, mit dem ioBroker die
/// gespeicherten Passwörter verschlüsselt; man könnte sie entschlüsseln und wörtlich
/// suchen. Geprüft und verworfen: Der Schlüssel hat in einem Anzeigewerkzeug nichts zu
/// suchen (siehe <see cref="SystemIdentity"/>), und die Muster unten fanden an der
/// Referenzanlage jedes Skript, das beim Umbau angefasst wurde.
///
/// <b>Zwei Fallen,</b> beide an echten Skripten gefunden: <c>'&amp;password=' + SECRETS…</c>
/// (die Zeichenkette endet nach dem Gleichheitszeichen — kein Wert) und
/// <c>`&amp;password=${pass}`</c> (ein Platzhalter — kein Wert). Beides ist die umgebaute
/// Form und darf nicht als Klartext gelten.
/// </summary>
public static class CredentialAnalyzer
{
    /// <summary>Erste Fassung des javascript-Adapters mit <c>SECRETS</c>.</summary>
    public static readonly Version MinJavascriptVersion = new(10, 1, 1);

    private const RegexOptions Opt = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    private const string KeyWord = "passwor[dt]|passwd|pwd|token|api_?key|secret";

    private static readonly Regex UrlParameter = new(
        @"[?&](?<key>passwor[dt]|passwd|pwd|pass|pw|token|access_token|api_?key|key|secret)=(?<val>[^'""`&\s]{3,})",
        Opt, RegexLimits.MatchTimeout);

    private static readonly Regex UrlUserInfo = new(
        @"\b(?<scheme>https?|rtsp|rtmp|s?ftp|mqtts?|wss?)://(?<user>[^/\s:@'""`]+):(?<val>[^/\s@'""`]+)@",
        Opt, RegexLimits.MatchTimeout);

    // Der Name darf nicht auf ? oder & folgen — das ist der URL-Parameter von oben. Der Wert
    // ohne Leerraum: Ein Satz wie 'Bitte Passwort eingeben' ist kein Passwort, und
    // '&password=' + SECRETS['…'] scheitert an derselben Regel.
    private static readonly Regex Assignment = new(
        @"(?<![?&\w$.])(?<key>[\w$.]*(?:" + KeyWord + @")[\w$]*)['""]?\s*[:=]\s*(?<q>['""`])(?<val>[^\s'""`]{6,})\k<q>",
        Opt, RegexLimits.MatchTimeout);

    private static readonly Regex AuthHeader = new(
        @"['""`](?<key>Bearer|Basic)\s+(?<val>[A-Za-z0-9+/=._-]{12,})",
        Opt, RegexLimits.MatchTimeout);

    private static readonly Regex SecretsUse = new(
        @"\bSECRETS\s*(?:(?:\?\.|\.)\s*(?<name>[A-Za-z_$][\w$]*)|(?:\?\.)?\[\s*(?:(?<q>['""`])(?<name>[^'""`\]]+)\k<q>|(?<var>[A-Za-z_$][\w$]*))\s*\])"
      + @"(?:\s*\??\.\s*(?<field>[A-Za-z_$][\w$]*)|\s*(?:\?\.)?\[\s*(?<q2>['""`])(?<field>[^'""`\]]+)\k<q2>\s*\])?",
        RegexOptions.CultureInvariant, RegexLimits.MatchTimeout);

    /// <summary>Erste Fassung des Admins mit der zentralen Ablage (System → Zugangsdaten).</summary>
    public static readonly Version MinAdminVersion = new(8, 0, 0);

    /// <summary>true, wenn diese javascript-Version <c>SECRETS</c> kennt.</summary>
    public static bool Supports(string? javascriptVersion) =>
        AtLeast(javascriptVersion, MinJavascriptVersion);

    /// <summary>true, wenn diese Admin-Version die zentrale Ablage hat.</summary>
    public static bool AdminSupports(string? adminVersion) =>
        AtLeast(adminVersion, MinAdminVersion);

    private static bool AtLeast(string? version, Version minimum)
    {
        if (string.IsNullOrWhiteSpace(version)) return false;

        // „10.2.0-alpha.3" — der Vorabzusatz ändert nichts an der Frage.
        var kern = version.Split('-', '+')[0];
        return Version.TryParse(kern, out var v) && v >= minimum;
    }

    /// <summary>Prüft den Quelltext eines Skripts. Bei Blockly das erzeugte JavaScript.</summary>
    public static ScriptCredentials Analyze(string source)
    {
        var findings = new List<CredentialFinding>();
        var uses = new List<SecretUse>();

        var lines = source.Replace("\r\n", "\n").Split('\n');
        try
        {
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (line.Length == 0) continue;
                var comment = line.AsSpan().TrimStart().StartsWith("//");

                foreach (Match m in UrlParameter.Matches(line))
                    Add(findings, CredentialFindingKind.UrlParameter, i, comment, m,
                        $"&{m.Groups["key"].Value}=");

                foreach (Match m in UrlUserInfo.Matches(line))
                    Add(findings, CredentialFindingKind.UrlUserInfo, i, comment, m,
                        $"{m.Groups["scheme"].Value}://<Benutzer>:", "@");

                foreach (Match m in Assignment.Matches(line))
                    Add(findings, CredentialFindingKind.Assignment, i, comment, m,
                        $"{m.Groups["key"].Value} = ");

                foreach (Match m in AuthHeader.Matches(line))
                    Add(findings, CredentialFindingKind.AuthHeader, i, comment, m,
                        $"{m.Groups["key"].Value} ");

                // Ein auskommentiertes SECRETS ist kein Umbau.
                if (comment) continue;
                foreach (Match m in SecretsUse.Matches(line))
                {
                    var name = m.Groups["name"].Success
                        ? m.Groups["name"].Value
                        : ResolveConstant(source, m.Groups["var"].Value);
                    uses.Add(new SecretUse(name, m.Groups["field"].Value));
                }
            }
        }
        catch (RegexMatchTimeoutException)
        {
            // Dieses Skript überspringen, nicht den Ladevorgang — siehe RegexLimits.
            return new ScriptCredentials();
        }

        return new ScriptCredentials
        {
            Findings = findings,
            Uses = uses.Distinct().ToList()
        };
    }

    private static void Add(List<CredentialFinding> findings, CredentialFindingKind kind, int lineIndex,
                            bool comment, Match m, string prefix, string suffix = "")
    {
        var value = m.Groups["val"].Value;
        if (IsPlaceholder(value)) return;
        findings.Add(new CredentialFinding(kind, lineIndex + 1,
            $"{prefix}<{value.Length} Zeichen>{suffix}", comment));
    }

    /// <summary>
    /// Kein Wert, sondern eine Leerstelle: <c>${pass}</c>, <c>&lt;password&gt;</c>,
    /// <c>{{token}}</c>, <c>%s</c>, <c>xxxx</c>, <c>****</c> — oder ein zwischen
    /// Pluszeichen eingeklemmter Variablenname aus <c>'…='+token+'…'</c>.
    /// </summary>
    private static bool IsPlaceholder(string value) =>
        value.StartsWith("${", StringComparison.Ordinal)
        || value[0] is '<' or '[' or '{' or '%'
        || (value[0] == '+' && value[^1] == '+')
        || value.All(c => c is 'x' or 'X' or '*' or '.' or '-');

    /// <summary>
    /// <c>SECRETS[CREDENTIAL]</c>: Steht der Name in einer Konstante desselben Skripts, wird
    /// er von dort genommen. Sonst bleibt er leer — der Zugriff zählt trotzdem als Umbau,
    /// nur der Abgleich mit den angelegten Einträgen entfällt.
    /// </summary>
    private static string ResolveConstant(string source, string identifier)
    {
        if (identifier.Length == 0) return "";

        var m = Regex.Match(source,
            @"\b(?:const|let|var)\s+" + Regex.Escape(identifier) + @"\s*=\s*(?<q>['""`])(?<name>[^'""`\n]+)\k<q>",
            RegexOptions.CultureInvariant, RegexLimits.MatchTimeout);
        return m.Success ? m.Groups["name"].Value : "";
    }

    /// <summary>
    /// Der Durchlauf über das ganze Backup. Setzt <see cref="ScriptInfo.Credentials"/> an
    /// jedem Skript — aber nur, wenn der javascript-Adapter die Funktion überhaupt hat.
    /// </summary>
    public static CredentialReport Apply(IReadOnlyList<IobObject> objects, IReadOnlyList<ScriptInfo> scripts)
    {
        // Das Adapter-Objekt trägt die installierte Version; fehlt es, die der Instanz.
        string VersionOf(string adapter) =>
            objects.FirstOrDefault(o => o.Id == "system.adapter." + adapter)?.Version
            ?? objects.Where(o => o.Type == "instance"
                                  && o.Id.StartsWith("system.adapter." + adapter + ".", StringComparison.Ordinal))
                      .Select(o => o.Version)
                      .FirstOrDefault(v => !string.IsNullOrEmpty(v))
            ?? "";

        var version = VersionOf("javascript");
        var admin = VersionOf("admin");

        if (!Supports(version) || !AdminSupports(admin))
            return new CredentialReport { JavascriptVersion = version, AdminVersion = admin };

        var entries = objects
            .Where(o => o.CredentialFields is not null)
            .Select(o => new CredentialEntry(o.Id[CredentialPrefix.Length..], o.CredentialFields!))
            .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Bekannt ist ein Name über die Objekt-ID oder über common.name — wie der Admin
        // Sonderzeichen im Namen auf die ID abbildet, ist nicht dokumentiert.
        var known = new HashSet<string>(entries.Select(e => e.Name), StringComparer.Ordinal);
        foreach (var o in objects.Where(o => o.CredentialFields is not null && o.Name.Length > 0))
            known.Add(o.Name);

        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var s in scripts)
        {
            var c = Analyze(s.CleanSource);
            c.UnknownNames = c.Uses.Select(u => u.Name)
                              .Where(n => n.Length > 0 && !known.Contains(n))
                              .Distinct().ToList();
            foreach (var u in c.Uses) used.Add(u.Name);
            s.Credentials = c;
        }

        return new CredentialReport
        {
            JavascriptVersion = version,
            AdminVersion = admin,
            Supported = true,
            Entries = entries,
            // Holt ein Skript den Namen zur Laufzeit (SECRETS[variable]), ist nicht zu sagen,
            // welcher Eintrag gemeint ist — dann wird keiner als unbenutzt gemeldet.
            UnusedEntries = used.Contains("") ? Array.Empty<string>() : objects
                .Where(o => o.CredentialFields is not null
                            && !used.Contains(o.Id[CredentialPrefix.Length..])
                            && !used.Contains(o.Name))
                .Select(o => o.Id[CredentialPrefix.Length..])
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            InstancesWithSecretsOff = objects
                .Where(o => o.EnableSecrets == false)
                .Select(o => o.Id["system.adapter.".Length..])
                .ToList()
        };
    }

    internal const string CredentialPrefix = "system.credentials.";
    internal const string InstancePrefix = "system.adapter.javascript.";
}
