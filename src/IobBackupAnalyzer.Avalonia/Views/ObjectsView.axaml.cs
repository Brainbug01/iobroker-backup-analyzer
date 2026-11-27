using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using IobBackupAnalyzer.Core;

namespace IobBackupAnalyzer.Avalonia.Views;

/// <summary>
/// Objekte aus dem Backup als Datei für „Objekte hochladen" im Admin exportieren. Suche,
/// Auswahl und Texte kommen aus <see cref="ObjectsPresenter"/>, die Datei aus
/// <see cref="ObjectExporter"/> — dieselben wie in der Windows-App.
/// </summary>
public partial class ObjectsView : UserControl
{
    /// <summary>Eine Tabellenzeile mit Haken. Der Haken lebt in der gemeinsamen Auswahlmenge,
    /// damit er beim Umfiltern erhalten bleibt.</summary>
    public sealed class ObjectRow : INotifyPropertyChanged
    {
        private readonly HashSet<string> _checked;
        private readonly Action _changed;

        public ObjectRow(IobObject o, HashSet<string> checkedIds, Action changed)
        {
            Object = o;
            _checked = checkedIds;
            _changed = changed;
            var row = ObjectsPresenter.Row(o);
            Id = row[0]; Type = row[1]; Name = row[2]; Reason = row[3];
        }

        public IobObject Object { get; }
        public string Id { get; }
        public string Type { get; }
        public string Name { get; }
        public string Reason { get; }

        public bool IsChecked
        {
            get => _checked.Contains(Id);
            set
            {
                if (value == IsChecked) return;
                if (value) _checked.Add(Id); else _checked.Remove(Id);
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked)));
                _changed();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private readonly TextBox _pattern;
    private readonly ComboBox _type;
    private readonly CheckBox _includeChildren;
    private readonly CheckBox _allowGuarded;
    private readonly TextBlock _count;
    private readonly DataGrid _rows;
    private readonly TextBlock _placeholder;

    private BackupData? _data;
    private List<ObjectRow> _filtered = new();
    private readonly HashSet<string> _checked = new(StringComparer.Ordinal);
    private bool _revertingGuard;

    public ObjectsView()
    {
        AvaloniaXamlLoader.Load(this);
        TableLayout.FillLastColumn(this);

        _pattern = this.FindControl<TextBox>("Pattern")!;
        _type = this.FindControl<ComboBox>("TypeFilter")!;
        _includeChildren = this.FindControl<CheckBox>("IncludeChildren")!;
        _allowGuarded = this.FindControl<CheckBox>("AllowGuarded")!;
        _count = this.FindControl<TextBlock>("Count")!;
        _rows = this.FindControl<DataGrid>("Rows")!;
        _placeholder = this.FindControl<TextBlock>("PlaceholderText")!;

        _includeChildren.Content = ObjectsPresenter.IncludeChildrenLabel;
        ToolTip.SetTip(_includeChildren, ObjectsPresenter.IncludeChildrenHint);
        _allowGuarded.Content = ObjectsPresenter.AllowGuardedLabel;
        ToolTip.SetTip(_allowGuarded, ObjectsPresenter.AllowGuardedHint);
        ToolTip.SetTip(_pattern, ObjectsPresenter.PatternHint);

        _pattern.TextChanged += (_, _) => Fill();
        _type.SelectionChanged += (_, _) => Fill();
        _allowGuarded.IsCheckedChanged += async (_, _) => await OnAllowGuardedChangedAsync();

        this.FindControl<Button>("CheckAll")!.Click += (_, _) => CheckAll(true);
        this.FindControl<Button>("Uncheck")!.Click += (_, _) => CheckAll(false);
        this.FindControl<Button>("Clipboard")!.Click += async (_, _) => await ExportAsync(toClipboard: true);
        this.FindControl<Button>("Export")!.Click += async (_, _) => await ExportAsync(toClipboard: false);

        _rows.LoadingRow += (_, e) =>
        {
            var muted = e.Row.DataContext is ObjectRow r
                        && ObjectsPresenter.Emphasis(r.Object) == RowEmphasis.Muted;
            if (muted) { if (!e.Row.Classes.Contains("gedaempft")) e.Row.Classes.Add("gedaempft"); }
            else e.Row.Classes.Remove("gedaempft");
        };

        SetData(null);
    }

    public void SetData(BackupData? data)
    {
        _data = data;
        _checked.Clear();

        if (data is null)
        {
            _filtered = new List<ObjectRow>();
            _rows.ItemsSource = null;
            _count.Text = "";
            _placeholder.Text = "Kein Backup geladen.\n\nBitte oben eine Datei öffnen oder hineinziehen.";
            _placeholder.IsVisible = true;
            _rows.IsVisible = false;
            return;
        }

        _placeholder.IsVisible = false;
        _rows.IsVisible = true;
        _type.ItemsSource = ObjectsPresenter.TypeLabels(data);
        _type.SelectedIndex = 0;
        Fill();
    }

    private void Fill()
    {
        if (_data is null) return;

        _filtered = ObjectsPresenter.Filter(_data.Objects, _pattern.Text, _type.SelectedItem as string)
                                    .Select(o => new ObjectRow(o, _checked, UpdateCount))
                                    .ToList();
        _rows.ItemsSource = _filtered;
        UpdateCount();
    }

    private void CheckAll(bool check)
    {
        if (check) foreach (var r in _filtered) _checked.Add(r.Id);
        else _checked.Clear();
        Fill();
    }

    private void UpdateCount()
    {
        if (_data is null) return;
        _count.Text = ObjectsPresenter.CountText(_filtered.Count, _data.Objects.Count, _checked.Count);
    }

    private async Task OnAllowGuardedChangedAsync()
    {
        if (_revertingGuard || _allowGuarded.IsChecked != true) return;

        if (!await Dialogs.ConfirmAsync(this, "Zurückgehaltene zulassen", ObjectsPresenter.AllowGuardedWarning))
        {
            _revertingGuard = true;
            _allowGuarded.IsChecked = false;
            _revertingGuard = false;
        }
    }

    private async Task ExportAsync(bool toClipboard)
    {
        if (_data is null) return;

        var sel = ObjectsPresenter.Resolve(_data, _checked, _includeChildren.IsChecked == true,
                                           _allowGuarded.IsChecked == true);
        await ExportSelectionAsync(this, _data, sel, ObjectExporter.FileName(_data, _pattern.Text), toClipboard);
    }

    /// <summary>
    /// Der gemeinsame Ablauf für diese Ansicht und den Vergleich: Bestätigung mit den
    /// Hinweisen, Datei lesen, speichern oder kopieren.
    /// </summary>
    internal static async Task ExportSelectionAsync(Control owner, BackupData data,
                                                    ObjectsPresenter.Selection sel, string fileName,
                                                    bool toClipboard)
    {
        if (sel.Export.Count == 0)
        {
            await Dialogs.MessageAsync(owner, "Hinweis",
                sel.Guarded.Count > 0
                    ? "Alle gewählten Objekte werden zurückgehalten (Zugangsdaten oder Adapter/Instanz).\n\n"
                      + "Wer sie trotzdem braucht, schaltet „Zurückgehaltene zulassen\" ein."
                    : "Bitte zuerst mindestens ein Objekt anhaken.");
            return;
        }

        if (!await Dialogs.ConfirmAsync(owner,
                toClipboard ? "In die Zwischenablage kopieren" : "Als Import-JSON speichern",
                ObjectsPresenter.ConfirmText(sel) + "\n\nFortfahren?"))
            return;

        var top = TopLevel.GetTopLevel(owner);
        if (top is null) return;

        string? target = null;
        if (!toClipboard)
        {
            var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Import-JSON speichern",
                SuggestedFileName = fileName,
                DefaultExtension = "json",
                FileTypeChoices = new[] { new FilePickerFileType("JSON-Datei") { Patterns = new[] { "*.json" } } }
            });
            target = file?.TryGetLocalPath();
            if (string.IsNullOrEmpty(target)) return;

            if (ExportPaths.ArchiveWarning(target) is { } warning
                && !await Dialogs.ConfirmAsync(owner, "Sicher?", warning))
                return;
        }

        try
        {
            var result = await Task.Run(() => ObjectExporter.Build(data, sel.Export));

            if (target is null)
            {
                if (top.Clipboard is { } clipboard) await clipboard.SetTextAsync(result.Json);
                await Dialogs.MessageAsync(owner, "Fertig",
                    ObjectsPresenter.DoneText(result, "in die Zwischenablage kopiert"));
            }
            else
            {
                ObjectExporter.WriteFile(result.Json, target, data.SourceFile);
                await Dialogs.MessageAsync(owner, "Fertig",
                    ObjectsPresenter.DoneText(result, "gespeichert in:\n" + target));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                      or InvalidOperationException or System.Text.Json.JsonException)
        {
            await Dialogs.MessageAsync(owner, "Fehler", "Der Export ist fehlgeschlagen:\n\n" + ex.Message);
        }
    }
}
