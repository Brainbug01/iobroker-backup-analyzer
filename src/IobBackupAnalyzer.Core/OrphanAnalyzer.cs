namespace IobBackupAnalyzer.Core;

/// <summary>
/// Säule 3: Analysen für verwaiste Datenpunkte.
///
/// Beide Analysen liefern ausdrücklich Prüflisten, keine Löschlisten — die Grenzen der
/// Erkennbarkeit sind in den Kommentaren der jeweiligen Methode dokumentiert.
/// </summary>
public static class OrphanAnalyzer
{
    /// <summary>
    /// Namespaces, die keiner Adapter-Instanz zugeordnet sind und deshalb nie als
    /// Objekt-Leiche gelten können (Analyse A).
    /// </summary>
    private static readonly string[] SystemNamespaces =
    {
        "system.", "_design", "enum.", "alias.", "0_userdata.", "script.js."
    };

    /// <summary>
    /// Analyse A: Objekte, deren Adapter-Instanz nicht mehr existiert.
    /// </summary>
    public static List<OrphanObject> FindOrphanObjects(BackupData data)
    {
        var known = new HashSet<string>(
            data.Instances.Select(i => i.Namespace), StringComparer.OrdinalIgnoreCase);

        var result = new List<OrphanObject>();

        foreach (var o in data.Objects)
        {
            if (IsSystemNamespace(o.Id)) continue;
            if (o.DontDelete) continue;   // siehe Begründung an DarfNichtGeloeschtWerden

            // Namespace ist <adapter>.<instanz> — die ersten beiden Segmente.
            var first = o.Id.IndexOf('.');
            if (first <= 0) continue;
            var second = o.Id.IndexOf('.', first + 1);
            if (second < 0) continue;

            // Nur numerische Instanznummern gelten als Adapter-Namespace; damit fallen
            // Objekte wie "mqtt.foo.bar" (kein Instanzmuster) nicht fälschlich an.
            var instancePart = o.Id[(first + 1)..second];
            if (!int.TryParse(instancePart, out _)) continue;

            var ns = o.Id[..second];
            if (known.Contains(ns)) continue;

            result.Add(new OrphanObject
            {
                Id = o.Id,
                Type = o.Type,
                Name = o.Name,
                MissingInstance = ns,
                Expert = o.Expert
            });
        }

        return result
            .OrderBy(o => o.MissingInstance, StringComparer.OrdinalIgnoreCase)
            .ThenBy(o => o.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsSystemNamespace(string id)
    {
        foreach (var ns in SystemNamespaces)
            if (id.StartsWith(ns, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>
    /// Warum Objekte mit <c>common.dontDelete</c> aus beiden Analysen herausfallen.
    ///
    /// Die Spezifikation sagt dazu „this object may not be deleted", und das Kennzeichen
    /// steht in <c>ObjectCommon</c> — es gilt also für jeden Objekttyp, auch für einen
    /// Datenpunkt. Ein solcher Datenpunkt in einer Liste, die zum Aufräumen einlädt, wäre
    /// mehr als nur nutzlos: Der erzeugte Befehl liefe ins Leere, und der Nutzer bekäme eine
    /// Fehlermeldung für eine Zeile, die das Programm ihm selbst vorgeschlagen hat.
    ///
    /// <b>Vorsorge, kein behobener Fehler.</b> In den geprüften Anlagen trägt kein einziger
    /// Datenpunkt das Kennzeichen — dort sind es ausschließlich Systemobjekte
    /// (<c>0_userdata.0</c>, <c>alias.0</c>, <c>enum.rooms</c>, <c>system.group.*</c>), die
    /// über <see cref="IsSystemNamespace"/> ohnehin nicht in Frage kommen. Ein Adapter darf
    /// aber einen so gekennzeichneten Datenpunkt anlegen, und dann greift diese Prüfung.
    /// Gefunden beim Abgleich der Aufräumlisten mit der Typdefinition des js-controllers
    /// (<c>ObjectCommon</c> in <c>packages/types-dev/objects.d.ts</c>).
    /// </summary>
    private const string DarfNichtGeloeschtWerden =
        "Objekte mit common.dontDelete gehören in keine Aufräumliste.";

    /// <summary>
    /// Installierte Adapter ohne eigene Instanz — eine Bestandsaufnahme, keine Löschliste.
    ///
    /// Grundmenge: echte Adapter-Objekte <c>system.adapter.&lt;name&gt;</c> (type=adapter,
    /// genau drei ID-Segmente). Die host-gebundenen <c>system.host.&lt;host&gt;.adapter.*</c>
    /// tragen dieses Präfix nicht und fallen von selbst heraus. Ein Adapter gilt als „ohne
    /// eigene Instanz", wenn zu seinem Namen keine Instanz
    /// (system.adapter.&lt;name&gt;.&lt;nr&gt;) existiert.
    ///
    /// Wichtig: instanzlos heißt nicht ungenutzt. Socket-Backends wie ws/socketio werden von
    /// admin/web genutzt, ohne eine eigene Instanz zu haben; reine Abhängigkeiten ebenso. Ein
    /// belastbares Backup-Merkmal, das „gebraucht" von „übrig" trennt, existiert nicht — alle
    /// tragen common.mode=daemon. Die Einordnung bleibt daher dem Nutzer überlassen.
    /// </summary>
    public static List<AdapterWithoutInstance> FindAdaptersWithoutInstance(BackupData data)
    {
        var withInstance = new HashSet<string>(
            data.Instances.Select(i => i.Adapter), StringComparer.OrdinalIgnoreCase);

        var result = new List<AdapterWithoutInstance>();

        foreach (var o in data.Objects)
        {
            if (o.Type != "adapter") continue;
            if (!o.Id.StartsWith("system.adapter.", StringComparison.Ordinal)) continue;

            var name = o.Id["system.adapter.".Length..];
            // Adapternamen sind einsegmentig; alles mit weiterem Punkt wäre kein Adapter-Objekt.
            if (name.Length == 0 || name.Contains('.')) continue;
            if (withInstance.Contains(name)) continue;

            result.Add(new AdapterWithoutInstance { Adapter = name, Version = o.Version ?? "" });
        }

        return result.OrderBy(a => a.Adapter, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Analyse B: User-Datenpunkte, die nirgends verwendet zu werden scheinen.
    ///
    /// Grundmenge: type=state unter 0_userdata.0.* und javascript.&lt;X&gt;.*.
    /// Vier Prüfungen je Kandidat: Vorkommen in Skripten, Vorkommen in VIS-Views,
    /// Alias-Ziel, aktives Logging (common.custom).
    ///
    /// Zusätzlich wird der Zeitstempel der letzten Wertänderung aus states.jsonl
    /// mitgeführt. Er ersetzt keine der vier Prüfungen, ordnet die Treffer aber ein:
    /// Ein Datenpunkt, der sich vorgestern noch geändert hat, wird von irgendetwas
    /// beschrieben — auch wenn weder Skript noch VIS ihn erwähnen.
    ///
    /// Bekannte Grenzen: IDs, die ein Skript zur Laufzeit aus Teilen zusammensetzt,
    /// werden nur über die Präfix-Prüfung erfasst; Nutzung durch externe Systeme
    /// (MQTT-Clients, Grafana, App-Widgets) ist im Backup grundsätzlich nicht sichtbar.
    /// </summary>
    public static List<UnusedDatapoint> FindUnusedDatapoints(BackupData data,
                                                             CancellationToken ct = default)
        => FindUnusedDatapoints(data, mitIndex: true, ct);

    /// <summary>
    /// Dieselbe Analyse ohne Index — die geradeheraus geschriebene Fassung, die für jeden
    /// Datenpunkt den ganzen Text durchsucht.
    ///
    /// <b>Wozu die noch da ist:</b> Sie ist offensichtlich richtig, und genau das macht sie
    /// zum Maßstab. Der Verifikationslauf lässt beide Wege über dieselben Backups laufen
    /// und vergleicht die Ergebnisse Feld für Feld. Eine schnellere Analyse, die andere
    /// Befunde liefert, wäre keine Verbesserung, sondern ein neuer Fehler — und in einer
    /// Liste, aus der Leute Datenpunkte löschen, ein besonders unangenehmer.
    ///
    /// Für den laufenden Betrieb ist sie ungeeignet: Bei sehr vielen eigenen Datenpunkten
    /// und einem umfangreichen VIS-Projekt braucht sie mehrere Minuten.
    /// </summary>
    public static List<UnusedDatapoint> FindUnusedDatapointsOhneIndex(BackupData data,
                                                                      CancellationToken ct = default)
        => FindUnusedDatapoints(data, mitIndex: false, ct);

    private static List<UnusedDatapoint> FindUnusedDatapoints(BackupData data, bool mitIndex,
                                                              CancellationToken ct)
    {
        // Ein einziger Suchtext über alle Skripte: 1 Substring-Suche statt N.
        var scriptText = string.Join("\n", data.Scripts.Select(s => s.SearchableCode));
        var visText = string.Join("\n", data.VisViews.Select(v => v.Content));

        // Alle Alias-Ziele einmal einsammeln. Case-sensitiv, weil ioBroker-IDs es sind —
        // in gewachsenen Anlagen gibt es ID-Paare, die sich nur in der Schreibweise
        // unterscheiden (…Temperatur vs …temperatur).
        var aliasTargets = new HashSet<string>(
            data.Objects.Where(o => !string.IsNullOrEmpty(o.AliasTarget)).Select(o => o.AliasTarget!),
            StringComparer.Ordinal);

        // Alle von Charts referenzierten Datenpunkte einmal einsammeln. Ein Datenpunkt, der
        // in einer Chart-Linie steht, wird angezeigt und ist damit kein Verwaisten-Kandidat —
        // die Quell-Instanz (history/influxdb/sql) spielt dabei keine Rolle. Case-sensitiv,
        // wie alle ID-Vergleiche in diesem Werkzeug.
        var chartRefs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var o in data.Objects)
            if (o.ChartRefs is not null)
                foreach (var r in o.ChartRefs)
                    chartRefs.Add(r);

        // Der Index beantwortet dieselbe Frage wie die Textsuche, aber ohne den Text je
        // Datenpunkt erneut zu durchlaufen. Siehe IdIndex — dort steht auch, warum das
        // dieselben Antworten gibt und nicht bloß ähnliche.
        var namensraeume = Namensraeume(data.Objects);
        var visIndex = mitIndex ? IdIndex.Baue(visText, namensraeume) : null;
        var scriptIndex = mitIndex ? IdIndex.Baue(scriptText, namensraeume) : null;

        var result = new List<UnusedDatapoint>();

        // Bezugspunkt für das Alter ist der Backup-Zeitpunkt, nicht „heute" — sonst würde
        // ein altes Backup alle Datenpunkte künstlich als tot erscheinen lassen.
        var reference = data.CreatedAt ?? DateTime.Now;

        foreach (var o in data.Objects)
        {
            ct.ThrowIfCancellationRequested();

            if (o.Type != "state") continue;
            if (o.DontDelete) continue;   // siehe Begründung an DarfNichtGeloeschtWerden
            if (!IsUserDatapoint(o.Id)) continue;

            var hasState = data.States.TryGetValue(o.Id, out var st);
            var lastChange = hasState ? st!.LastChange : null;

            result.Add(new UnusedDatapoint
            {
                Id = o.Id,
                Name = o.Name,
                InScripts = scriptIndex?.Finde(o.Id) ?? FindIn(scriptText, o.Id),
                InVis = visText.Length == 0
                    ? FindKind.Nicht
                    : visIndex?.Finde(o.Id) ?? FindIn(visText, o.Id),
                AliasTarget = aliasTargets.Contains(o.Id),
                LoggingActive = o.HasCustom,
                InChart = chartRefs.Contains(o.Id),
                HasState = hasState,
                LastChange = lastChange,
                AgeDays = lastChange is null ? null : (int)(reference - lastChange.Value).TotalDays,
                Val = hasState ? st!.Val : "",
                HasVal = hasState && st!.HasVal,
                ValTruncated = hasState && st!.ValTruncated,
                ValLength = hasState ? st!.ValLength : 0,
                Expert = o.Expert
            });
        }

        return result.OrderBy(r => r.Id, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Verwaltungsobjekte, die der javascript-Adapter selbst anlegt. Sie sind keine
    /// Nutzer-Datenpunkte und wären als Löschkandidaten schlicht falsch.
    /// </summary>
    private static readonly string[] JavascriptInternals =
    {
        ".scriptEnabled.", ".scriptProblem.", ".debug.", ".info.",
        ".memHeap", ".memRss", ".uptime", ".cpu", ".eventLoopLag"
    };

    private static bool IsUserDatapoint(string id)
    {
        if (id.StartsWith("0_userdata.0.", StringComparison.OrdinalIgnoreCase)) return true;

        // javascript.<X>.* — aber nicht die Verwaltungsobjekte des Adapters selbst.
        if (!id.StartsWith("javascript.", StringComparison.OrdinalIgnoreCase)) return false;

        foreach (var marker in JavascriptInternals)
            if (id.Contains(marker, StringComparison.OrdinalIgnoreCase)) return false;

        if (id.EndsWith(".alive", StringComparison.OrdinalIgnoreCase)) return false;
        if (id.EndsWith(".connected", StringComparison.OrdinalIgnoreCase)) return false;

        return true;
    }

    /// <summary>
    /// Sucht die ID im Text. Wird die volle ID nicht gefunden, wird geprüft, ob wenigstens
    /// der Elternpfad vorkommt — dann setzt ein Skript die ID möglicherweise dynamisch
    /// zusammen und der Datenpunkt ist kein sicherer Löschkandidat.
    ///
    /// Case-sensitiv (Ordinal), weil ioBroker-IDs es sind. Ein case-insensitiver Vergleich
    /// würde genau die Tippfehler-Dubletten verschleiern, die dieses Werkzeug aufdecken soll.
    /// </summary>
    /// <summary>
    /// Die Namensräume, mit denen eigene Datenpunkte beginnen — jeweils bis zum zweiten
    /// Punkt einschließlich, also „0_userdata.0." oder „javascript.0.". In einer Anlage
    /// sind das eine Handvoll.
    /// </summary>
    private static List<string> Namensraeume(IEnumerable<IobObject> objects)
    {
        var gefunden = new HashSet<string>(StringComparer.Ordinal);

        foreach (var o in objects)
        {
            if (o.Type != "state" || !IsUserDatapoint(o.Id)) continue;

            var erster = o.Id.IndexOf('.');
            if (erster < 0) continue;
            var zweiter = o.Id.IndexOf('.', erster + 1);
            if (zweiter < 0) continue;

            gefunden.Add(o.Id[..(zweiter + 1)]);
        }

        return gefunden.ToList();
    }

    /// <summary>
    /// Beantwortet „kommt diese Datenpunkt-ID im Text vor?" — ohne den Text je Frage erneut
    /// zu durchsuchen.
    ///
    /// <b>Das Problem.</b> Die Analyse fragt das für jeden eigenen Datenpunkt einmal — der
    /// Aufwand ist also Datenpunkte mal Textlänge. Bei kleinen Anlagen fällt das nicht auf.
    /// An erzeugten Testarchiven mit elf Megabyte VIS-Text gemessen: 500 eigene Datenpunkte
    /// brauchten 1,8 Sekunden, 2.000 schon 6,6 und 8.000 rund 26 — sauber linear. Eine
    /// gewachsene Anlage liegt um Größenordnungen darüber, und daraus werden Minuten, in
    /// denen nichts anderes passiert.
    ///
    /// <b>Der Ausweg — und warum er dieselben Antworten gibt.</b> Jede gesuchte ID beginnt
    /// mit ihrem Namensraum („0_userdata.0." oder „javascript.0."). Wo immer die ID im Text
    /// steht, steht dort also auch ihr Namensraum. Es genügt daher, den Text <b>einmal</b>
    /// nach den paar Namensräumen abzusuchen und an jeder Fundstelle die dort beginnende
    /// Zeichenkette mitzunehmen. Steht die gesuchte ID irgendwo im Text, ist sie der Anfang
    /// einer dieser Zeichenketten — und genau darauf lässt sich mit einer Binärsuche
    /// antworten.
    ///
    /// Die mitgenommene Zeichenkette reicht dabei bis zum ersten Zeichen, das in einer
    /// ioBroker-ID nicht vorkommen darf. Der Zeichenvorrat ist bewusst großzügig (er
    /// enthält auch Leerzeichen, Klammern und Schrägstriche): Wäre er zu eng, endete die
    /// Zeichenkette mitten in einer ID, und ein tatsächlich benutzter Datenpunkt stünde
    /// anschließend als Löschkandidat in der Liste. Lieber zu viel mitnehmen als zu wenig.
    /// </summary>
    private sealed class IdIndex
    {
        /// <summary>
        /// Keine ID ist länger als das. Die Obergrenze verhindert, dass eine einzelne
        /// Fundstelle in einem langen Text (ein eingebettetes Bild etwa) eine riesige
        /// Zeichenkette erzeugt.
        /// </summary>
        private const int MaxLaenge = 512;

        private readonly string[] _stellen;

        private IdIndex(string[] stellen) => _stellen = stellen;

        public static IdIndex Baue(string text, List<string> namensraeume)
        {
            var stellen = new List<string>();

            foreach (var praefix in namensraeume)
            {
                if (praefix.Length == 0) continue;

                var pos = 0;
                while ((pos = text.IndexOf(praefix, pos, StringComparison.Ordinal)) >= 0)
                {
                    var ende = pos;
                    var grenze = Math.Min(text.Length, pos + MaxLaenge);
                    while (ende < grenze && IstIdZeichen(text[ende])) ende++;

                    stellen.Add(text[pos..ende]);
                    pos++;
                }
            }

            stellen.Sort(StringComparer.Ordinal);
            return new IdIndex(stellen.ToArray());
        }

        /// <summary>
        /// Dieselbe Einstufung wie <see cref="FindIn"/>: volle ID gefunden, sonst der
        /// Elternpfad, sonst nichts.
        /// </summary>
        public FindKind Finde(string id)
        {
            if (Enthaelt(id)) return FindKind.Exakt;

            var letzterPunkt = id.LastIndexOf('.');
            if (letzterPunkt > 0)
            {
                var eltern = id[..letzterPunkt];
                // Sehr kurze Präfixe träfen praktisch immer zu und wären wertlos —
                // dieselbe Grenze wie in FindIn.
                if (eltern.Length > 14 && Enthaelt(eltern)) return FindKind.NurPraefix;
            }

            return FindKind.Nicht;
        }

        /// <summary>
        /// Beginnt eine der mitgenommenen Zeichenketten mit <paramref name="gesucht"/>?
        /// Binärsuche auf der sortierten Liste: Die erste Zeichenkette, die nicht kleiner
        /// ist als der gesuchte Text, ist auch die einzige, die mit ihm beginnen kann.
        /// </summary>
        private bool Enthaelt(string gesucht)
        {
            var links = 0;
            var rechts = _stellen.Length;

            while (links < rechts)
            {
                var mitte = (links + rechts) / 2;
                if (string.CompareOrdinal(_stellen[mitte], gesucht) < 0) links = mitte + 1;
                else rechts = mitte;
            }

            return links < _stellen.Length
                   && _stellen[links].StartsWith(gesucht, StringComparison.Ordinal);
        }

        /// <summary>
        /// Darf das Zeichen in einer ioBroker-ID stehen? Der Vorrat ist die Umkehrung von
        /// FORBIDDEN_CHARS aus dem js-controller (packages/common-db, tools.ts) und
        /// bewusst großzügig gehalten — siehe die Erklärung an der Klasse.
        /// </summary>
        private static bool IstIdZeichen(char c) =>
            char.IsLetterOrDigit(c) || "._-/ :!#$%&()+=@^{}|~".Contains(c);
    }

    private static FindKind FindIn(string haystack, string id)
    {
        if (haystack.Contains(id, StringComparison.Ordinal))
            return FindKind.Exakt;

        var lastDot = id.LastIndexOf('.');
        if (lastDot > 0)
        {
            var parent = id[..lastDot];
            // Sehr kurze Präfixe (z. B. "0_userdata.0") träfen praktisch immer zu und
            // wären als Signal wertlos.
            if (parent.Length > 14 && haystack.Contains(parent, StringComparison.Ordinal))
                return FindKind.NurPraefix;
        }

        return FindKind.Nicht;
    }
}
