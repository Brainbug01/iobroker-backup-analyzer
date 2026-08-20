using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using IobBackupAnalyzer.Core;

namespace IobBackupAnalyzer.Avalonia.Views;

/// <summary>
/// Alias-Übersicht samt Detailbereich: zeigt Lese-/Schreibziel je Alias, ob das Ziel
/// existiert, und erzeugt auf Wunsch ein Konverter-Gerüst für den gewählten Alias.
/// Zusammenfassung, Filter und Spalten kommen aus <see cref="AliasPresenter"/>.
/// </summary>
public partial class AliasView : UserControl
{
    private readonly TextBlock _summary;
    private readonly TextBox _filter;
    private readonly ComboBox _scope;
    private readonly TextBlock _count;
    private readonly Button _csv;
    private readonly DataGrid _rows;
    private readonly TextBlock _placeholder;
    private readonly Grid _split;

    private readonly TextBlock _detailHeader;
    private readonly TextBox _convRead;
    private readonly TextBox _convWrite;
    private readonly Button _generate;
    private readonly TextBox _genRead;
    private readonly TextBox _genWrite;
    private readonly TextBlock _genNote;

    private BackupData? _data;
    private List<AliasRow> _all = new();

    /// <summary>Ziel-Datenpunkte nachschlagbar — Grundlage des Konverter-Generators.</summary>
    private Dictionary<string, IobObject> _byId = new(StringComparer.Ordinal);

    public AliasView()
    {
        AvaloniaXamlLoader.Load(this);
        TableLayout.FillLastColumn(this);

        _summary = this.FindControl<TextBlock>("Summary")!;
        _filter = this.FindControl<TextBox>("Filter")!;
        _scope = this.FindControl<ComboBox>("Scope")!;
        _count = this.FindControl<TextBlock>("Count")!;
        _csv = this.FindControl<Button>("Csv")!;
        _rows = this.FindControl<DataGrid>("Rows")!;
        _placeholder = this.FindControl<TextBlock>("PlaceholderText")!;
        _split = this.FindControl<Grid>("Split")!;

        _detailHeader = this.FindControl<TextBlock>("DetailHeader")!;
        _convRead = this.FindControl<TextBox>("ConvRead")!;
        _convWrite = this.FindControl<TextBox>("ConvWrite")!;
        _generate = this.FindControl<Button>("Generate")!;
        _genRead = this.FindControl<TextBox>("GenRead")!;
        _genWrite = this.FindControl<TextBox>("GenWrite")!;
        _genNote = this.FindControl<TextBlock>("GenNote")!;

        _scope.ItemsSource = AliasPresenter.ScopeLabels;
        _scope.SelectedIndex = 0;

        _filter.TextChanged += (_, _) => Fill();
        _scope.SelectionChanged += (_, _) => Fill();
        _rows.SelectionChanged += (_, _) => ShowDetails();
        _generate.Click += (_, _) => GenerateForSelected();
        _csv.Click += async (_, _) => await Dialogs.SaveCsvAsync(this, "alias-uebersicht.csv",
            AliasPresenter.CsvColumns, CurrentRows().Select(AliasPresenter.Row).ToList());

        _rows.LoadingRow += (_, e) =>
        {
            var broken = (e.Row.DataContext as AliasRow)?.Broken ?? false;
            if (broken) { if (!e.Row.Classes.Contains("kaputt")) e.Row.Classes.Add("kaputt"); }
            else e.Row.Classes.Remove("kaputt");
        };

        SetData(null);
    }

    public void SetData(BackupData? data)
    {
        _data = data;

        if (data is null || data.Kind != BackupKind.Full)
        {
            _all = new List<AliasRow>();
            _byId = new Dictionary<string, IobObject>(StringComparer.Ordinal);
            _summary.Text = "";
            _count.Text = "";
            _rows.ItemsSource = null;
            _placeholder.Text = data is null
                ? "Kein Backup geladen.\n\nBitte oben eine Datei öffnen oder hineinziehen."
                : "Für die Alias-Übersicht wird ein Voll-Backup benötigt.\n\n" +
                  "Die geladene Datei enthält nur Skripte.";
            ShowPlaceholder(true);
            ClearDetails();
            return;
        }

        _all = AliasAnalyzer.Analyze(data);

        _byId = new Dictionary<string, IobObject>(StringComparer.Ordinal);
        foreach (var o in data.Objects) _byId[o.Id] = o;

        _summary.Text = AliasPresenter.SummaryText(_all);
        ShowPlaceholder(false);
        Fill();
        ClearDetails();
    }

    private void ShowPlaceholder(bool show)
    {
        _placeholder.IsVisible = show;
        _split.IsVisible = !show;
    }

    private List<AliasRow> CurrentRows() =>
        AliasPresenter.Filter(_all, (AliasScope)Math.Max(0, _scope.SelectedIndex), _filter.Text);

    private void Fill()
    {
        if (_data is null || _data.Kind != BackupKind.Full) return;

        var rows = CurrentRows();
        _rows.ItemsSource = rows;
        _count.Text = AliasPresenter.CountText(rows.Count, _all.Count);
    }

    // ---------------------------------------------------------------- Detailbereich

    private AliasRow? SelectedRow => _rows.SelectedItem as AliasRow;

    private void ShowDetails()
    {
        var a = SelectedRow;
        if (a is null)
        {
            ClearDetails();
            return;
        }

        _detailHeader.Text = AliasPresenter.DetailHeader(a);
        _convRead.Text = a.ConverterRead;
        _convWrite.Text = a.ConverterWrite;

        // Vorschlagsfelder erst nach Knopfdruck füllen.
        _genRead.Text = "";
        _genWrite.Text = "";
        _genNote.Text = "";
        _generate.IsEnabled = true;
    }

    private void ClearDetails()
    {
        _detailHeader.Text = AliasPresenter.DetailHeader(null);
        _convRead.Text = "";
        _convWrite.Text = "";
        _genRead.Text = "";
        _genWrite.Text = "";
        _genNote.Text = "";
        _generate.IsEnabled = false;
    }

    private void GenerateForSelected()
    {
        var a = SelectedRow;
        if (a is null) return;

        // Der Lese-Konverter bezieht sich auf das Lese-Ziel — von dort kommt die Wertetabelle.
        _byId.TryGetValue(a.ReadTarget, out var target);
        var result = ConverterGenerator.Generate(target);

        _genRead.Text = result.Read;
        _genWrite.Text = result.Write;
        _genNote.Text = result.Note;
        _genNote.Foreground = result.CanGenerate
            ? this.FindControl<TextBlock>("DetailHeader")!.Foreground
            : new SolidColorBrush(Color.Parse("#D9534F"));
    }
}
