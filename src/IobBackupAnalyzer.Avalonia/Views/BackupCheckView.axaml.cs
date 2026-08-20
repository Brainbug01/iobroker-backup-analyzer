using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using IobBackupAnalyzer.Core;

namespace IobBackupAnalyzer.Avalonia.Views;

/// <summary>
/// Backup-Prüfung: validiert die JSON-Dateien im Backup nach dem Vorbild des js-controllers.
/// Zahlen und Zeilen kommen aus <see cref="BackupCheckPresenter"/>; hier steht nur Anzeige-Code.
/// </summary>
public partial class BackupCheckView : UserControl
{
    private readonly TextBlock _verdict;
    private readonly TextBlock _summary;
    private readonly CheckBox _onlyProblems;
    private readonly Button _csv;
    private readonly DataGrid _rows;
    private readonly TextBlock _placeholder;
    private readonly Border _repairHintBox;
    private readonly SelectableTextBlock _repairHint;

    private BackupData? _data;

    public BackupCheckView()
    {
        AvaloniaXamlLoader.Load(this);
        TableLayout.FillLastColumn(this);

        _verdict = this.FindControl<TextBlock>("Verdict")!;
        _summary = this.FindControl<TextBlock>("Summary")!;
        _onlyProblems = this.FindControl<CheckBox>("OnlyProblems")!;
        _csv = this.FindControl<Button>("Csv")!;
        _rows = this.FindControl<DataGrid>("Rows")!;
        _placeholder = this.FindControl<TextBlock>("PlaceholderText")!;
        _repairHintBox = this.FindControl<Border>("RepairHintBox")!;
        _repairHint = this.FindControl<SelectableTextBlock>("RepairHint")!;

        _onlyProblems.IsCheckedChanged += (_, _) => Fill();
        // Exportiert, was in der Liste steht — inklusive „nur Probleme".
        _csv.Click += async (_, _) => await Dialogs.SaveCsvAsync(this, "backup-pruefung.csv",
            BackupCheckPresenter.Columns, CurrentRows().Select(BackupCheckPresenter.Row).ToList());

        _rows.LoadingRow += (_, e) =>
        {
            var severity = (e.Row.DataContext as CheckRow)?.Severity ?? CheckSeverity.Ok;
            SetClass(e.Row, "problem", severity == CheckSeverity.Problem);
            SetClass(e.Row, "info", severity == CheckSeverity.Info);
        };

        SetData(null);
    }

    private static void SetClass(DataGridRow row, string name, bool on)
    {
        // Zeilen werden beim Scrollen wiederverwendet — die Klasse muss deshalb auch
        // wieder entfernt werden, sonst bleibt die Einfärbung an der falschen Zeile hängen.
        if (on) { if (!row.Classes.Contains(name)) row.Classes.Add(name); }
        else row.Classes.Remove(name);
    }

    public void SetData(BackupData? data)
    {
        _data = data;

        if (data is null || data.Kind != BackupKind.Full)
        {
            _verdict.Text = "";
            _summary.Text = "";
            _rows.ItemsSource = null;
            _placeholder.Text = data is null
                ? "Kein Backup geladen.\n\nBitte oben eine Datei öffnen oder hineinziehen."
                : "Für die Backup-Prüfung wird ein Voll-Backup (objects.jsonl) benötigt.\n\n" +
                  "Die geladene Datei enthält nur Skripte.";
            ShowPlaceholder(true);
            return;
        }

        _verdict.Text = BackupCheckPresenter.VerdictText(data);
        _verdict.Foreground = new SolidColorBrush(data.Validation.Health switch
        {
            // Bewusst eigene Töne statt SeaGreen/Firebrick: Diese sind auf hellem wie
            // dunklem Hintergrund lesbar — die App folgt dem Systemthema.
            BackupHealth.Valid => Color.Parse("#2FA36B"),
            BackupHealth.Warnings => Color.Parse("#D98A00"),
            BackupHealth.Invalid => Color.Parse("#D9534F"),
            _ => Colors.Gray
        });

        _summary.Text = BackupCheckPresenter.SummaryText(data);
        ShowPlaceholder(false);
        Fill();
    }

    private void ShowPlaceholder(bool show)
    {
        _placeholder.IsVisible = show;
        _rows.IsVisible = !show;
    }

    private List<CheckRow> BuildRows() =>
        _data is null ? new List<CheckRow>() : BackupCheckPresenter.BuildRows(_data);

    private void Fill()
    {
        if (_data is null || _data.Kind != BackupKind.Full) return;

        var rows = BuildRows();

        // Der Hinweis bezieht sich immer auf alle Befunde, nicht auf die gefilterte Sicht.
        var hint = BackupCheckPresenter.RepairHint(rows, _data.Validation.ArchiveTruncated,
                                                   _data.CreatedAt);
        _repairHintBox.IsVisible = hint is not null;
        _repairHint.Text = hint ?? "";

        _rows.ItemsSource = CurrentRows();
    }

    /// <summary>Die Zeilen, wie sie gerade in der Liste stehen — Grundlage auch für die CSV.</summary>
    private List<CheckRow> CurrentRows()
    {
        var rows = BuildRows();
        return _onlyProblems.IsChecked == true ? BackupCheckPresenter.OnlyProblems(rows) : rows;
    }
}
