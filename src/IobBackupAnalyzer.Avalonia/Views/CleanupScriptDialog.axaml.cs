using System.ComponentModel;
using System.Text;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using IobBackupAnalyzer.Core;

namespace IobBackupAnalyzer.Avalonia.Views;

/// <summary>
/// Dialog zum Erzeugen eines Aufräum-Skripts für Waisen-States. Links steht ein Baum:
/// oben der Namensraum, aufgeklappt darunter jeder einzelne Wert — beides mit Häkchen.
/// Rechts entsteht daraus das Shell-Skript. Der Analyzer löscht nichts — er erzeugt nur
/// die Datei.
///
/// Die Auswahl selbst liegt in <see cref="CleanupSelection"/> und nicht in den Knoten:
/// So überlebt sie das Filtern und das Neuaufbauen des Baums, und die WinForms-Fassung
/// verhält sich Klick für Klick genauso.
///
/// „Skript speichern…" ist der empfohlene Weg: Die Datei wird mit LF und ohne BOM
/// geschrieben. Über die Zwischenablage in einen Windows-Editor kopiert, landet sonst
/// leicht CRLF im Skript, woran es auf dem Host mit <c>$'\r': command not found</c>
/// scheitert.
/// </summary>
public partial class CleanupScriptDialog : Window
{
    /// <summary>
    /// Ein Knoten des Baums — entweder ein Namensraum (mit <see cref="Children"/>) oder ein
    /// einzelner Wert. Der Zustand kommt bei jedem Zugriff frisch aus der
    /// <see cref="CleanupSelection"/>; der Knoten hält nichts eigenes, was auseinanderlaufen
    /// könnte.
    /// </summary>
    private sealed class Node : INotifyPropertyChanged
    {
        private readonly CleanupScriptDialog _dialog;
        private readonly CleanupSelection.Group? _group;
        private readonly string? _id;
        private List<Node>? _children;

        public Node(CleanupScriptDialog dialog, CleanupSelection.Group group)
        {
            _dialog = dialog;
            _group = group;
        }

        public Node(CleanupScriptDialog dialog, string id)
        {
            _dialog = dialog;
            _id = id;
        }

        public string Label => _group is null ? _id! : _dialog._selection.GroupLabel(_group);

        /// <summary>Die Werte des Namensraums; bei einem Wert-Knoten leer.</summary>
        public IReadOnlyList<Node> Children => _children ??= _group is null
            ? new List<Node>()
            : _group.Visible.Select(id => new Node(_dialog, id)).ToList();

        /// <summary>
        /// <c>null</c> steht für „teilweise ausgewählt". Beim Setzen zählt nur, ob der Wert
        /// <c>true</c> ist: Ein Klick auf einen teilweise gefüllten Namensraum wählt ihn
        /// vollständig aus, der nächste hebt ihn ganz auf.
        /// </summary>
        public bool? Checked
        {
            get => _group is null
                ? _dialog._selection.IsSelected(_id!)
                : _dialog._selection.StateOf(_group) switch
                {
                    GroupCheck.All => true,
                    GroupCheck.None => false,
                    _ => null
                };
            set
            {
                if (_group is null) _dialog._selection.Select(_id!, value == true);
                else _dialog._selection.SelectGroup(_group, value == true);

                _dialog.AfterSelectionChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>Meldet Häkchen und Beschriftung neu — samt bereits erzeugter Kinder.</summary>
        public void Refresh()
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Checked)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Label)));

            if (_children is null) return;
            foreach (var child in _children) child.Refresh();
        }
    }

    private readonly TreeView _tree;
    private readonly TextBox _search;
    private readonly TextBlock _count;
    private readonly TextBlock _hidden;
    private readonly TextBox _script;
    private readonly Button _copy;
    private readonly Button _save;
    private readonly CleanupSelection _selection;
    private readonly string _backupName;
    private List<Node> _nodes = new();

    /// <summary>Parameterloser Konstruktor nur für den XAML-Lader.</summary>
    public CleanupScriptDialog() : this(new List<(string, IReadOnlyList<string>)>()) { }

    /// <param name="sourceFile">
    /// Pfad des geladenen Backups — nur für den Dateinamensvorschlag. Darf leer sein.
    /// </param>
    public CleanupScriptDialog(IEnumerable<(string Namespace, IReadOnlyList<string> Ids)> namespaces,
                               string? sourceFile = null)
    {
        _backupName = BackupNaming.FolderName(sourceFile);
        _selection = new CleanupSelection(namespaces);

        AvaloniaXamlLoader.Load(this);
        TableLayout.FillLastColumn(this);

        _tree = this.FindControl<TreeView>("Tree")!;
        _search = this.FindControl<TextBox>("Search")!;
        _count = this.FindControl<TextBlock>("Count")!;
        _hidden = this.FindControl<TextBlock>("Hidden")!;
        _script = this.FindControl<TextBox>("Script")!;
        _copy = this.FindControl<Button>("Copy")!;
        _save = this.FindControl<Button>("Save")!;

        _search.TextChanged += (_, _) =>
        {
            _selection.SetFilter(_search.Text);
            BuildTree();
        };

        this.FindControl<Button>("All")!.Click += (_, _) => SetAllVisible(true);
        this.FindControl<Button>("None")!.Click += (_, _) => SetAllVisible(false);
        this.FindControl<Button>("CloseButton")!.Click += (_, _) => Close();
        _copy.Click += async (_, _) => await CopyAsync();
        _save.Click += async (_, _) => await SaveAsync();

        BuildTree();
    }

    /// <summary>Baut die Namensraum-Ebene neu auf — nach dem Öffnen und nach jeder Suche.</summary>
    private void BuildTree()
    {
        _nodes = _selection.VisibleGroups.Select(g => new Node(this, g)).ToList();
        _tree.ItemsSource = _nodes;
        AfterSelectionChanged();
    }

    private void SetAllVisible(bool on)
    {
        _selection.SelectAllVisible(on);
        AfterSelectionChanged();
    }

    /// <summary>Häkchen, Zähler, Hinweis und Skript nachziehen — nach jeder Änderung.</summary>
    private void AfterSelectionChanged()
    {
        foreach (var node in _nodes) node.Refresh();

        _count.Text = _selection.CountText;

        var hidden = _selection.HiddenSelectionHint;
        _hidden.Text = hidden ?? "";
        _hidden.IsVisible = hidden is not null;

        var ids = _selection.SelectedIds;
        _script.Text = CleanupScriptGenerator.Generate(ids);
        _copy.IsEnabled = ids.Count > 0;
        _save.IsEnabled = ids.Count > 0;
    }

    private async Task CopyAsync()
    {
        if (Clipboard is null || string.IsNullOrEmpty(_script.Text)) return;

        await Clipboard.SetTextAsync(_script.Text);
        await Dialogs.MessageAsync(this, "Kopiert",
            "Das Skript liegt in der Zwischenablage.\n\n" +
            "Auf dem ioBroker-Host als Datei speichern (z. B. aufraeumen.sh) und mit " +
            "„bash aufraeumen.sh“ starten — es fragt dann selbst, ob gelöscht oder nur " +
            "getestet werden soll.\n\n" +
            "Sicherer ist „Skript speichern…“: Die Datei bekommt dabei die Zeilenenden, " +
            "die eine Linux-Shell erwartet.");
    }

    /// <summary>
    /// Schreibt das Skript als Datei: UTF-8 ohne BOM und mit LF. Beides ist keine Kosmetik —
    /// eine BOM würde die Shebang-Zeile entwerten, CRLF jede Zeile mit einem \r beenden.
    /// </summary>
    private async Task SaveAsync()
    {
        if (string.IsNullOrEmpty(_script.Text)) return;

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Aufräum-Skript speichern",
            SuggestedFileName = CleanupScriptGenerator.SuggestedFileName(_backupName),
            DefaultExtension = "sh",
            ShowOverwritePrompt = true,
            FileTypeChoices = new[]
            {
                new FilePickerFileType("Shell-Skript") { Patterns = new[] { "*.sh" } }
            }
        });

        var path = file?.TryGetLocalPath();
        if (string.IsNullOrEmpty(path)) return;

        try
        {
            await File.WriteAllTextAsync(path,
                CleanupScriptGenerator.ForFile(_script.Text), new UTF8Encoding(false));

            await Dialogs.MessageAsync(this, "Skript gespeichert",
                "Gespeichert:\n" + path + "\n\n" +
                "So geht es weiter:\n" +
                "1. Datei auf den ioBroker-Host kopieren — per SFTP, z. B. mit FileZilla,\n" +
                "   oder in der Konsole mit scp. Es gelten dieselben Zugangsdaten wie bei SSH.\n" +
                "2. In der Shell aufrufen:  bash " + Path.GetFileName(path) + "\n" +
                "3. Auf die Frage zuerst mit Enter antworten — das ist der Testlauf.\n" +
                "   Erst wenn die Liste stimmt, noch einmal starten und mit einem großen „J“ bestätigen.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                      or NotSupportedException or PathTooLongException)
        {
            await Dialogs.MessageAsync(this, "Fehler",
                "Das Skript konnte nicht gespeichert werden:\n\n" + ex.Message);
        }
    }
}
