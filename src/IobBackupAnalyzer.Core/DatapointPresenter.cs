using System.Text;
using System.Text.Json;

namespace IobBackupAnalyzer.Core;

/// <summary>
/// Ein Suchtreffer: ein Datenpunkt mit seiner Beschreibung und seinem letzten Wert.
/// </summary>
public sealed class DatapointHit
{
    public required string Id { get; init; }
    public string Name { get; init; } = "";

    /// <summary>Datentyp aus common.type; leer, wenn nicht angegeben.</summary>
    public string TypeText { get; init; } = "";

    public string Unit { get; init; } = "";
    public string Role { get; init; } = "";
    public string Min { get; init; } = "";
    public string Max { get; init; } = "";
    public string Default { get; init; } = "";

    /// <summary>common.write: true = beschreibbar, false = nur lesend, null = nicht angegeben.</summary>
    public bool? Writable { get; init; }

    /// <summary>Wertetabelle aus common.states (Gerätewert → Klartext); null, wenn keine.</summary>
    public IReadOnlyDictionary<string, string>? States { get; init; }

    /// <summary>false, wenn es zu diesem Objekt keinen Eintrag in states.jsonl gibt.</summary>
    public bool HasState { get; init; }

    /// <summary>true, wenn es umgekehrt einen Wert ohne zugehöriges Objekt gibt.</summary>
    public bool HasObject { get; init; } = true;

    public DateTime? LastChange { get; init; }
    public int? AgeDays { get; init; }

    /// <summary>Schreibende Instanz ohne system.adapter.-Präfix.</summary>
    public string From { get; init; } = "";

    public bool Ack { get; init; } = true;
    public string QualityText { get; init; } = "";

    public string Val { get; init; } = "";
    public bool HasVal { get; init; }
    public bool ValTruncated { get; init; }
    public int ValLength { get; init; }

    /// <summary>Der Wert einzeilig für die Trefferliste.</summary>
    public string ValText => StateInfo.FormatVal(Val, HasVal, ValTruncated, ValLength);

    public string LastChangeText =>
        !HasState ? "kein Wert"
        : LastChange is null ? "unbekannt"
        : AgeDays is null ? LastChange.Value.ToString("dd.MM.yyyy HH:mm")
        : $"{LastChange.Value:dd.MM.yyyy HH:mm}  ({AgeDays} T)";

    public string WritableText => Writable switch { true => "Ja", false => "Nein", _ => "" };

    /// <summary>Typ und Einheit zusammen — „number (°C)" statt zweier halbleerer Spalten.</summary>
    public string TypeAndUnit =>
        Unit.Length == 0 ? TypeText : TypeText.Length == 0 ? Unit : $"{TypeText} ({Unit})";
}

/// <summary>
/// UI-neutrale Logik des Tabs „Datenpunkte": Suche über ID und Name, der vollständige Wert
/// zum Herauskopieren und die Beschreibung des Datenpunkts daneben.
///
/// <b>Wozu das dient:</b> Ein Backup ist die einzige Stelle, an der ein überschriebener Wert
/// noch steht. Die <c>states.jsonl</c> von Hand zu durchsuchen führt zwar zur richtigen Zeile,
/// liefert den Wert aber in der Form, in der er dort abgelegt ist: Ein JSON-Wert steht als
/// maskierter String (<c>"val":"{\"a\":1}"</c>) mitten in einer Zeile, die selbst JSON ist.
/// Bis daraus etwas Einsetzbares wird, ist von Hand einiges zu tun. Diesen Schritt nimmt der
/// Tab ab — entmaskiert, eingerückt, kopierbar.
///
/// Bewusst nicht dabei: Schreiben ins laufende System. Der Analyzer liest Backups, mehr nicht.
/// </summary>
public static class DatapointPresenter
{
    /// <summary>
    /// Obergrenze der <b>angezeigten</b> Zeilen. Ohne Suchbegriff wären es alle Datenpunkte
    /// der Anlage — in der Referenzanlage 14.791, im Belastungstest 190.000.
    ///
    /// <b>Was die Grenze nicht tut:</b> Speicher sparen. Die vollständige Liste liegt ohnehin
    /// im Arbeitsspeicher, sonst ließe sich darin nicht suchen. Begrenzt wird allein, wie
    /// viele Zeilen die Oberfläche daraus aufbaut — und das ist teuer. Gemessen am
    /// Gesamtdurchlauf über alle Reiter, mit und ohne Grenze:
    ///
    ///   Referenzanlage (14.791 Datenpunkte)   13,7 s → 15,8 s   (+2,1 s)
    ///   Belastungstest (190.000 Datenpunkte)  13,1 s → 40,0 s   (+26,9 s)
    ///
    /// Ohne Grenze würde dieser eine Reiter in einer großen Anlage den 15-Sekunden-Zielwert
    /// allein reißen. 2.000 Zeilen kosten davon hochgerechnet rund 0,3 Sekunden — das ist der
    /// Preis dafür, beim Stöbern nicht schon nach 500 Zeilen abgeschnitten zu werden.
    ///
    /// Es ist bewusst dieselbe Zahl wie in den übrigen begrenzten Listen — der Sicht
    /// „Älteste" (<see cref="OrphansPresenter.DisplayLimitOldest"/>) und den Fundstellen der
    /// Widget-Sätze (<see cref="VisPresenter.WidgetSetHitLimit"/>). Eine einheitliche Grenze
    /// ist leichter zu merken als drei verschiedene. Der CSV-Export enthält weiterhin alles.
    /// </summary>
    public const int DisplayLimit = OrphansPresenter.DisplayLimitOldest;

    public static readonly string[] Columns =
        { "Datenpunkt-ID", "Name", "Typ", "Rolle", "Zuletzt geändert", OrphansPresenter.ValueColumn };

    /// <summary>Wie <see cref="Columns"/>, aber mit dem vollständigen Wert und dem Alter.</summary>
    public static readonly string[] CsvColumns =
        { "Datenpunkt-ID", "Name", "Typ", "Einheit", "Rolle", "Schreibbar", "Zuletzt geändert",
          "Alter (Tage)", "Quelle", "Quittiert", "Qualität", OrphansPresenter.ValueColumn };

    public const string Hint =
        "Sucht in Datenpunkt-ID und Name. Der vollständige Wert steht unten und lässt sich " +
        "kopieren — auch dann, wenn er im Backup als maskiertes JSON abgelegt ist.";

    /// <summary>
    /// Alle Datenpunkte der Anlage als Suchgrundlage: die state-Objekte samt ihrem Wert, dazu
    /// die Werte ohne Objekt. Letztere gehören dazu, weil sie besonders schwer erreichbar sind:
    /// Ein Datenpunkt, den es im laufenden System nicht mehr gibt, steht nur noch hier.
    /// </summary>
    public static List<DatapointHit> Build(BackupData? data)
    {
        if (data is null) return new List<DatapointHit>();

        var reference = data.CreatedAt ?? DateTime.Now;
        var result = new List<DatapointHit>(data.Objects.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var o in data.Objects)
        {
            if (o.Type != "state") continue;

            seen.Add(o.Id);
            var hasState = data.States.TryGetValue(o.Id, out var st);

            result.Add(new DatapointHit
            {
                Id = o.Id,
                Name = o.Name,
                TypeText = o.CommonType ?? "",
                Unit = o.Unit ?? "",
                Role = o.Role ?? "",
                Min = o.Min ?? "",
                Max = o.Max ?? "",
                Default = o.Default ?? "",
                Writable = o.Writable,
                States = o.States,
                HasState = hasState,
                HasObject = true,
                LastChange = hasState ? st!.LastChange : null,
                AgeDays = hasState && st!.LastChange is { } lc
                    ? (int)(reference - lc).TotalDays : null,
                From = hasState ? st!.FromShort : "",
                Ack = !hasState || st!.Ack,
                QualityText = hasState ? st!.QualityText : "",
                Val = hasState ? st!.Val : "",
                HasVal = hasState && st!.HasVal,
                ValTruncated = hasState && st!.ValTruncated,
                ValLength = hasState ? st!.ValLength : 0
            });
        }

        foreach (var (id, st) in data.States)
        {
            if (seen.Contains(id)) continue;

            result.Add(new DatapointHit
            {
                Id = id,
                HasObject = false,
                HasState = true,
                LastChange = st.LastChange,
                AgeDays = st.LastChange is { } lc ? (int)(reference - lc).TotalDays : null,
                From = st.FromShort,
                Ack = st.Ack,
                QualityText = st.QualityText,
                Val = st.Val,
                HasVal = st.HasVal,
                ValTruncated = st.ValTruncated,
                ValLength = st.ValLength
            });
        }

        return result.OrderBy(h => h.Id, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Sucht in ID und Name. Mehrere durch Leerzeichen getrennte Begriffe müssen alle
    /// vorkommen, in beliebiger Reihenfolge — „wohnzimmer temp" findet
    /// <c>hm-rpc.0.ABC.1.ACTUAL_TEMPERATURE</c> mit dem Namen „Wohnzimmer Temperatur",
    /// ohne dass man die Schreibweise der ID kennen muss.
    /// </summary>
    public static List<DatapointHit> Filter(IEnumerable<DatapointHit> all, string? term)
    {
        var parts = (term ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries
                                          | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return all.ToList();

        return all.Where(h => parts.All(p =>
                       h.Id.Contains(p, StringComparison.OrdinalIgnoreCase)
                    || h.Name.Contains(p, StringComparison.OrdinalIgnoreCase)))
                  .ToList();
    }

    public static string Count(int shown, int total, bool filtered)
    {
        var suffix = total > DisplayLimit ? $" (angezeigt: {DisplayLimit:N0})" : "";

        return filtered
            ? $"{shown:N0} von {total:N0} Datenpunkten{suffix}"
            : $"{total:N0} Datenpunkte{suffix}  ·  Suchbegriff eingeben, um einzugrenzen";
    }

    public static RowEmphasis Emphasis(DatapointHit h) =>
        !h.HasObject ? RowEmphasis.Warn
        : !h.HasState ? RowEmphasis.Muted
        : RowEmphasis.None;

    public static string[] DisplayRow(DatapointHit h) =>
        new[] { h.Id, h.Name, h.TypeAndUnit, h.Role, h.LastChangeText, h.ValText };

    public static string[] Row(DatapointHit h) =>
        new[] { h.Id, h.Name, h.TypeText, h.Unit, h.Role, h.WritableText,
                h.LastChange?.ToString("dd.MM.yyyy HH:mm") ?? "", h.AgeDays?.ToString() ?? "",
                h.From, h.HasState ? (h.Ack ? "Ja" : "Nein") : "", h.QualityText, h.Val };

    // ------------------------------------------------------------------ Detailbereich

    /// <summary>
    /// Der Wert, wie man ihn braucht: entmaskiert und — wenn er JSON enthält — eingerückt.
    ///
    /// Der Fall, für den das gebaut ist: In der <c>states.jsonl</c> steht ein JSON-Wert als
    /// String mit maskierten Anführungszeichen. Beim Laden löst der JSON-Leser die Maskierung
    /// bereits auf, sodass hier der rohe Text ankommt — er ist damit schon einsetzbar. Bleibt
    /// die Lesbarkeit: Ein 40 KB langes JSON in einer Zeile kann niemand prüfen, bevor er es
    /// weiterverwendet. Deshalb wird es eingerückt, wenn es sich als JSON parsen lässt.
    ///
    /// Nicht-JSON bleibt unverändert. Ein gekappter Wert wird nicht formatiert, weil ein
    /// abgeschnittenes JSON nicht parsbar ist und der Versuch nur Zeit kostet.
    /// </summary>
    public static string FullValue(DatapointHit? h)
    {
        if (h is null) return "";
        if (!h.HasVal) return "";
        if (h.ValTruncated) return h.Val;

        return PrettyJson(h.Val) ?? h.Val;
    }

    /// <summary>
    /// Gibt den Text eingerückt zurück, wenn er ein JSON-Objekt oder eine JSON-Liste ist —
    /// sonst null.
    ///
    /// Bewusst nur Objekt und Liste: Eine nackte Zahl oder ein Wort ist zwar für sich
    /// gültiges JSON, würde beim Zurückschreiben aber nur seine Anführungszeichen ändern.
    /// Das wäre keine Verbesserung, sondern eine Veränderung des Werts.
    /// </summary>
    public static string? PrettyJson(string text)
    {
        var trimmed = text.AsSpan().Trim();
        if (trimmed.Length < 2) return null;
        if (trimmed[0] is not ('{' or '[')) return null;

        try
        {
            using var doc = JsonDocument.Parse(text);
            return JsonSerializer.Serialize(doc.RootElement,
                new JsonSerializerOptions
                {
                    WriteIndented = true,
                    // Ohne diese Einstellung schreibt System.Text.Json Umlaute und alles
                    // andere außerhalb von ASCII als \uXXXX — genau das, was hier weg soll.
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                });
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Die Beschreibung des Datenpunkts als Text — was im Admin unter „Objekt bearbeiten"
    /// steht, soweit es im Backup vorhanden ist. Leere Felder werden weggelassen, damit die
    /// Anzeige nicht aus Bindestrichen besteht.
    /// </summary>
    public static string Definition(DatapointHit? h)
    {
        if (h is null) return "";

        var sb = new StringBuilder();

        void Add(string label, string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            if (sb.Length > 0) sb.Append("   ·   ");
            sb.Append(label).Append(": ").Append(value);
        }

        if (!h.HasObject)
        {
            sb.Append("Wert ohne Objekt — im Backup gibt es keine Definition mehr dazu.");
            return sb.ToString();
        }

        // Der Name steht voran, obwohl er auch in der Tabelle steht: Dort ist die Spalte
        // begrenzt und schneidet ab. Manche Adapter legen ganze Sätze als Namen ab —
        // „Remaining battery in %, can take up to 24 hours before reported" etwa. Sucht
        // jemand nach „fore", trifft das genau solche Namen, und in der Tabelle ist die
        // Fundstelle dann nicht zu sehen. Ohne diese Zeile bliebe unerklärlich, warum eine
        // Zeile in der Trefferliste steht.
        Add("Name", h.Name);
        Add("Typ", h.TypeText);
        Add("Einheit", h.Unit);
        Add("Rolle", h.Role);
        Add("Schreibbar", h.WritableText);
        Add("Minimum", h.Min);
        Add("Maximum", h.Max);
        Add("Vorgabe", h.Default);

        if (h.States is { Count: > 0 })
            Add("Wertetabelle", string.Join(", ", h.States.Take(6).Select(kv => $"{kv.Key}={kv.Value}"))
                               + (h.States.Count > 6 ? " …" : ""));

        return sb.Length == 0 ? "Keine weitere Beschreibung im Backup." : sb.ToString();
    }

    /// <summary>
    /// Zeile über dem Wertfeld: woher der Wert stammt und wie verlässlich er ist. Ein nicht
    /// quittierter Wert oder ein Qualitätscode ungleich „gut" ändert die Bedeutung dessen,
    /// was darunter steht — das gehört daneben und nicht in eine Fußnote.
    /// </summary>
    public static string ValueInfo(DatapointHit? h)
    {
        if (h is null) return "";
        if (!h.HasState) return "Zu diesem Datenpunkt steht kein Wert im Backup.";
        if (!h.HasVal) return "Der Datenpunkt hat einen Eintrag, aber keinen Wert (null).";

        var teile = new List<string> { $"{h.ValLength:N0} Zeichen" };

        if (h.ValTruncated)
            teile.Add($"gekürzt auf {StateInfo.MaxValLength:N0} — der Rest steht nicht zur Verfügung");

        if (h.From.Length > 0) teile.Add($"geschrieben von {h.From}");
        if (!h.Ack) teile.Add("nicht quittiert");
        if (h.QualityText.Length > 0 && h.QualityText != "gut") teile.Add($"Qualität: {h.QualityText}");

        return string.Join("   ·   ", teile);
    }
}
