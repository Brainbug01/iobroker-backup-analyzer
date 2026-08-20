using IobBackupAnalyzer.Core;

namespace IobBackupAnalyzer.App;

/// <summary>
/// Backup-Prüfung: validiert die JSON-Dateien im Backup nach dem Vorbild des js-controllers
/// (objects.jsonl/states.jsonl zeilenweise strikt = Pflicht; files/**/*.json strikt =
/// optional). Zeigt ein Gesamturteil und benennt beschädigte Dateien mit Fundort.
/// </summary>
public sealed class BackupCheckTab : UserControl
{
    private readonly Label _verdict = new();
    private readonly Label _summary = new();
    private readonly CheckBox _onlyProblems = new();
    private readonly Button _csv = new();
    private readonly ListView _list = new();
    private readonly Label _placeholder = new();

    /// <summary>
    /// Handlungsanweisung unter der Tabelle — nur sichtbar, wenn es etwas zu tun gibt.
    /// Ein Befund ohne Weg zur Behebung nützt niemandem.
    /// </summary>
    private readonly TextBox _repairHint = new();

    private BackupData? _data;

    public BackupCheckTab()
    {
        BuildUi();
    }

    private void BuildUi()
    {
        Padding = new Padding(8);

        var head = TabLayout.TopBar(100);

        _verdict.Location = new Point(0, 0);
        _verdict.Size = new Size(1100, 30);
        _verdict.Font = new Font("Segoe UI", 12F, FontStyle.Bold);
        _verdict.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        _verdict.TextAlign = ContentAlignment.MiddleLeft;
        _verdict.AutoEllipsis = true;

        _summary.Location = new Point(0, 34);
        _summary.Size = new Size(1100, 38);
        _summary.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        _summary.ForeColor = SystemColors.GrayText;

        _onlyProblems.Text = "Nur Probleme anzeigen";
        _onlyProblems.Location = new Point(0, 76);
        _onlyProblems.Size = new Size(200, 22);
        _onlyProblems.CheckedChanged += (_, _) => Fill();

        _csv.Text = "Als CSV exportieren";
        _csv.Size = new Size(160, 26);
        _csv.Location = TabLayout.RightAligned(160, 72);
        _csv.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _csv.Click += (_, _) => ExportCsv();

        head.Controls.AddRange(new Control[] { _verdict, _summary, _onlyProblems, _csv });

        _list.Dock = DockStyle.Fill;
        _list.View = View.Details;
        _list.FullRowSelect = true;
        _list.GridLines = true;
        _list.HideSelection = false;
        _list.MultiSelect = true;
        _list.Columns.Add("Datei", 380);
        _list.Columns.Add("Art", 90);
        _list.Columns.Add("Ergebnis", 110);
        // Breit, weil in der Details-Spalte hinter dem Fehler noch der Fundort steht —
        // abgeschnitten nützt er niemandem.
        _list.Columns.Add("Details", 760);
        ListViewCopy.Attach(_list);
        ListViewSort.Attach(_list);

        _placeholder.Dock = DockStyle.Fill;
        _placeholder.TextAlign = ContentAlignment.MiddleCenter;
        _placeholder.ForeColor = SystemColors.GrayText;
        _placeholder.Text = "Kein Backup geladen.";

        // Kopierbar, aber wie ein Hinweistext gestaltet: Die Befehle darin will man
        // markieren und in die Shell übernehmen können.
        _repairHint.Dock = DockStyle.Bottom;
        _repairHint.Height = 230;
        _repairHint.Multiline = true;
        _repairHint.ReadOnly = true;
        _repairHint.ScrollBars = ScrollBars.Vertical;
        _repairHint.BorderStyle = BorderStyle.FixedSingle;
        _repairHint.BackColor = Color.FromArgb(255, 249, 219);
        _repairHint.ForeColor = Color.FromArgb(90, 60, 0);
        _repairHint.Font = new Font("Consolas", 9F);
        _repairHint.Visible = false;

        Controls.Add(_list);
        Controls.Add(_repairHint);
        Controls.Add(_placeholder);
        Controls.Add(head);
    }

    public void SetAvailable(bool available)
    {
        _placeholder.Visible = !available;
        if (!available)
        {
            _placeholder.Text = _data is null
                ? "Kein Backup geladen.\r\n\r\nBitte oben eine Datei öffnen oder hineinziehen."
                : "Für die Backup-Prüfung wird ein Voll-Backup (objects.jsonl) benötigt.\r\n\r\n" +
                  "Die geladene Datei enthält nur Skripte.";
            _placeholder.BringToFront();
        }
    }

    public void SetData(BackupData data)
    {
        _data = data;
        Fill();
    }

    private List<CheckRow> BuildRows() =>
        _data is null ? new List<CheckRow>() : BackupCheckPresenter.BuildRows(_data);

    private void Fill()
    {
        if (_data is null) return;

        _verdict.Text = BackupCheckPresenter.VerdictText(_data);
        _verdict.ForeColor = _data.Validation.Health switch
        {
            BackupHealth.Valid => Color.SeaGreen,
            BackupHealth.Warnings => Color.DarkOrange,
            BackupHealth.Invalid => Color.Firebrick,
            _ => SystemColors.GrayText
        };

        _summary.Text = BackupCheckPresenter.SummaryText(_data).Replace("\n", "\r\n");

        var rows = BuildRows();

        // Der Hinweis bezieht sich immer auf alle Befunde, nicht auf die gefilterte Sicht.
        var hint = BackupCheckPresenter.RepairHint(rows, _data.Validation.ArchiveTruncated,
                                                   _data.CreatedAt);
        _repairHint.Visible = hint is not null;
        _repairHint.Text = hint?.Replace("\n", "\r\n") ?? "";

        if (_onlyProblems.Checked) rows = BackupCheckPresenter.OnlyProblems(rows);

        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var r in rows)
        {
            var item = new ListViewItem(BackupCheckPresenter.Row(r));
            item.ForeColor = r.Severity switch
            {
                CheckSeverity.Problem => Color.Firebrick,
                CheckSeverity.Info => SystemColors.GrayText,
                _ => SystemColors.ControlText
            };
            _list.Items.Add(item);
        }
        _list.EndUpdate();
    }

    /// <summary>Exportiert, was in der Liste steht — inklusive „nur Probleme".</summary>
    private void ExportCsv()
    {
        var rows = BuildRows();
        if (_onlyProblems.Checked) rows = BackupCheckPresenter.OnlyProblems(rows);

        CsvExport.Save(this, "backup-pruefung.csv",
            BackupCheckPresenter.Columns,
            rows.Select(BackupCheckPresenter.Row));
    }
}
