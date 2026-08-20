using System.Text;
using System.Windows.Forms.VisualStyles;
using IobBackupAnalyzer.Core;

namespace IobBackupAnalyzer.App;

/// <summary>
/// Dialog zum Erzeugen eines Aufräum-Skripts für Waisen-States. Links steht ein Baum:
/// oben der Namensraum, aufgeklappt darunter jeder einzelne Wert — beides mit Häkchen.
/// Rechts entsteht daraus das Shell-Skript. Der Analyzer löscht nichts — er erzeugt nur
/// die Datei.
///
/// <b>Warum ein eigener Zustand statt <see cref="TreeView.CheckBoxes"/>?</b> Ein Namensraum
/// braucht drei Zustände: nichts, teilweise, alles. Der Windows-Baum kennt nur an und aus.
/// Deshalb zeichnet der Dialog die Kästchen über die <see cref="TreeView.StateImageList"/>
/// selbst und hält die Auswahl in <see cref="CleanupSelection"/>. Das hat einen zweiten
/// Nutzen: Die Auswahl überlebt das Filtern und das Zu- und Aufklappen, denn sie hängt nicht
/// an den Knoten.
///
/// Die Kindknoten entstehen erst beim Aufklappen. Ein Namensraum kann einige tausend Werte
/// enthalten; sie alle im Voraus als Knoten anzulegen, ließe den Dialog beim Öffnen hängen.
///
/// Gespeichert wird über „Skript speichern…"; das ist der empfohlene Weg, weil die Datei
/// dabei mit LF-Zeilenenden und ohne BOM geschrieben wird. Über die Zwischenablage in einen
/// Windows-Editor kopiert, landet sonst leicht CRLF im Skript — auf dem ioBroker-Host
/// scheitert es dann an <c>$'\r': command not found</c>, was für Ungeübte kaum zu deuten ist.
/// </summary>
public sealed class CleanupScriptDialog : Form
{
    /// <summary>
    /// Steht am noch nicht aufgeklappten Namensraum, damit Windows das Aufklapp-Dreieck
    /// zeichnet. Beim Aufklappen wird er gegen die echten Knoten getauscht.
    /// </summary>
    private static readonly object Placeholder = new();

    /// <summary>
    /// Ab dieser Zahl an Treffern klappt die Suche die Namensräume nicht mehr von selbst
    /// auf: Bei ein paar Treffern will man sie sofort sehen, bei tausenden dauert das
    /// Anlegen der Knoten länger als das Tippen des nächsten Buchstabens.
    /// </summary>
    private const int AutoExpandLimit = 200;

    private readonly TreeView _tree = new();
    private readonly TextBox _search = new();
    private readonly Label _count = new();
    private readonly Label _hidden = new();
    private readonly TextBox _script = new();
    private readonly Button _save = new();
    private readonly Button _copy = new();
    private readonly Button _all = new();
    private readonly Button _none = new();
    private readonly ImageList _checkImages = BuildCheckImages();
    private readonly CleanupSelection _selection;
    private readonly string _backupName;

    /// <param name="sourceFile">
    /// Pfad des geladenen Backups — nur für den Dateinamensvorschlag. Darf leer sein.
    /// </param>
    public CleanupScriptDialog(IEnumerable<(string Namespace, IReadOnlyList<string> Ids)> namespaces,
                               string? sourceFile = null)
    {
        _backupName = BackupNaming.FolderName(sourceFile);
        _selection = new CleanupSelection(namespaces);

        BuildUi();
        BuildTree();
    }

    private void BuildUi()
    {
        Text = "Aufräum-Skript für Waisen-States erzeugen";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(760, 480);
        ClientSize = new Size(980, 600);
        Font = new Font("Segoe UI", 9F);

        var hint = new Label
        {
            Dock = DockStyle.Top,
            Height = 66,
            Padding = new Padding(10, 8, 10, 4),
            Text = "Namensraum anhaken, dessen übrig gebliebene Werte weg sollen — oder ihn " +
                   "aufklappen und einzelne Werte auswählen. Rechts entsteht daraus ein Shell-Skript.\r\n" +
                   "Speichern, auf den ioBroker-Host kopieren, dort „bash <Datei>" + "“ aufrufen — " +
                   "das Skript fragt beim Start, ob es löschen oder nur testen soll."
        };

        // ---------- untere Knopfleiste ----------
        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 46, Padding = new Padding(10, 8, 10, 8) };

        var close = new Button
        {
            Text = "Schließen",
            Size = new Size(110, 28),
            Dock = DockStyle.Right,
            DialogResult = DialogResult.OK
        };

        _copy.Text = "In Zwischenablage kopieren";
        _copy.Size = new Size(190, 28);
        _copy.Dock = DockStyle.Right;
        _copy.Click += (_, _) => CopyToClipboard();

        _save.Text = "Skript speichern…";
        _save.Size = new Size(150, 28);
        _save.Dock = DockStyle.Right;
        _save.Click += (_, _) => SaveScript();

        // Dock=Right stapelt in Einfügereihenfolge von rechts nach links:
        // [ Skript speichern… ] [ In Zwischenablage kopieren ] [ Schließen ]
        bottom.Controls.Add(close);
        bottom.Controls.Add(new Panel { Dock = DockStyle.Right, Width = 8 });
        bottom.Controls.Add(_copy);
        bottom.Controls.Add(new Panel { Dock = DockStyle.Right, Width = 8 });
        bottom.Controls.Add(_save);
        AcceptButton = close;

        // ---------- links: Suche, Baum, Auswahlleiste ----------
        _search.Dock = DockStyle.Fill;
        _search.PlaceholderText = "Suchen — Teil einer ID oder eines Adapternamens";
        _search.TextChanged += (_, _) => ApplyFilter();

        var searchBar = new Panel { Dock = DockStyle.Top, Height = 30, Padding = new Padding(0, 0, 0, 6) };
        searchBar.Controls.Add(_search);

        _tree.Dock = DockStyle.Fill;
        _tree.HideSelection = false;
        _tree.ShowLines = true;
        _tree.ShowRootLines = true;
        // Eigene Kästchen statt TreeView.CheckBoxes — nur so gibt es den Zustand „teilweise".
        _tree.CheckBoxes = false;
        _tree.StateImageList = _checkImages;
        _tree.BeforeExpand += (_, e) =>
        {
            if (e.Node is not null) Materialize(e.Node);
        };
        _tree.NodeMouseClick += (_, e) =>
        {
            // Nur der Klick auf das Kästchen schaltet um — sonst würde jeder Klick auf den
            // Namen die Auswahl ändern, was beim bloßen Nachsehen niemand erwartet.
            if (_tree.HitTest(e.Location).Location == TreeViewHitTestLocations.StateImage)
                Toggle(e.Node);
        };
        _tree.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Space || _tree.SelectedNode is null) return;
            Toggle(_tree.SelectedNode);
            e.Handled = true;
            e.SuppressKeyPress = true;   // sonst scrollt der Baum zusätzlich
        };

        _all.Text = "Alle";
        _all.Size = new Size(80, 26);
        _all.Dock = DockStyle.Left;
        _all.Click += (_, _) => SetAllVisible(true);

        _none.Text = "Keine";
        _none.Size = new Size(80, 26);
        _none.Dock = DockStyle.Left;
        _none.Click += (_, _) => SetAllVisible(false);

        _count.Dock = DockStyle.Fill;
        // Voll qualifiziert: System.Windows.Forms.VisualStyles kennt ein gleichnamiges enum.
        _count.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
        _count.Padding = new Padding(8, 0, 0, 0);

        var selBar = new Panel { Dock = DockStyle.Bottom, Height = 34, Padding = new Padding(0, 4, 0, 0) };
        selBar.Controls.Add(_count);
        selBar.Controls.Add(_none);
        selBar.Controls.Add(_all);

        // Eigene Zeile, damit der Zähler einzeilig bleibt; sie verschwindet samt ihrem Platz,
        // solange die Suche nichts von der Auswahl verdeckt.
        _hidden.Dock = DockStyle.Bottom;
        _hidden.Height = 20;
        _hidden.Visible = false;
        _hidden.ForeColor = Color.FromArgb(160, 80, 0);
        _hidden.Padding = new Padding(2, 2, 0, 0);

        var left = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 0, 4, 0) };
        left.Controls.Add(_tree);
        left.Controls.Add(_hidden);
        left.Controls.Add(selBar);
        left.Controls.Add(searchBar);

        // ---------- rechts: erzeugtes Skript ----------
        _script.Multiline = true;
        _script.ReadOnly = true;
        _script.WordWrap = false;
        _script.ScrollBars = ScrollBars.Both;
        _script.Dock = DockStyle.Fill;
        _script.Font = new Font("Consolas", 9F);
        _script.BackColor = SystemColors.Window;

        var right = new Panel { Dock = DockStyle.Fill, Padding = new Padding(4, 0, 10, 0) };
        right.Controls.Add(_script);

        // Panel1/2MinSize bewusst NICHT im Initializer setzen: Am noch ungesizten Control
        // (Default-Größe) würde ein großes Panel2MinSize die interne SplitterDistance
        // ungültig machen und sofort werfen. Erst setzen, wenn genug Breite da ist.
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical
        };
        var splitInit = false;
        split.SizeChanged += (_, _) =>
        {
            if (splitInit) return;
            const int p1min = 260, p2min = 320;
            if (split.Width < p1min + p2min + split.SplitterWidth + 20) return;   // noch zu schmal
            split.Panel1MinSize = p1min;
            split.Panel2MinSize = p2min;
            split.SplitterDistance = Math.Clamp(380, p1min, split.Width - p2min - split.SplitterWidth);
            splitInit = true;
        };
        split.Panel1.Controls.Add(left);
        split.Panel2.Controls.Add(right);

        Controls.Add(split);
        Controls.Add(bottom);
        Controls.Add(hint);
    }

    // ---------------------------------------------------------------- Baum

    /// <summary>Baut die Namensraum-Ebene neu auf; die Werte darunter folgen beim Aufklappen.</summary>
    private void BuildTree()
    {
        var groups = _selection.VisibleGroups;
        var expand = _selection.Filter.Length > 0 && _selection.VisibleIdCount <= AutoExpandLimit;

        _tree.BeginUpdate();
        _tree.Nodes.Clear();

        foreach (var group in groups)
        {
            var node = new TreeNode(_selection.GroupLabel(group))
            {
                Tag = group,
                StateImageIndex = ImageFor(_selection.StateOf(group))
            };
            node.Nodes.Add(new TreeNode(string.Empty) { Tag = Placeholder });
            _tree.Nodes.Add(node);

            if (expand) node.Expand();   // löst BeforeExpand aus und füllt die Werte nach
        }

        _tree.EndUpdate();

        if (groups.Count == 0)
            _tree.Nodes.Add(new TreeNode("Kein Treffer für diese Suche."));

        AfterSelectionChanged();
    }

    /// <summary>Tauscht den Platzhalter gegen die echten Werte des Namensraums.</summary>
    private void Materialize(TreeNode node)
    {
        if (node.Tag is not CleanupSelection.Group group) return;
        if (node.Nodes.Count != 1 || !ReferenceEquals(node.Nodes[0].Tag, Placeholder)) return;

        var children = group.Visible
            .Select(id => new TreeNode(id)
            {
                Tag = id,
                StateImageIndex = ImageFor(_selection.IsSelected(id) ? GroupCheck.All : GroupCheck.None)
            })
            .ToArray();

        _tree.BeginUpdate();
        node.Nodes.Clear();
        node.Nodes.AddRange(children);
        _tree.EndUpdate();
    }

    /// <summary>Häkchen eines Knotens umschalten — Namensraum oder einzelner Wert.</summary>
    private void Toggle(TreeNode node)
    {
        switch (node.Tag)
        {
            case CleanupSelection.Group group:
                // Teilweise ausgewählt zählt als „noch nicht fertig": ein Klick wählt alles.
                _selection.SelectGroup(group, _selection.StateOf(group) != GroupCheck.All);
                RefreshGroupNode(node);
                break;

            case string id:
                _selection.Select(id, !_selection.IsSelected(id));
                node.StateImageIndex = ImageFor(_selection.IsSelected(id) ? GroupCheck.All : GroupCheck.None);
                if (node.Parent is not null) RefreshGroupNode(node.Parent, childrenToo: false);
                break;

            default:
                return;   // Hinweiszeile „Kein Treffer"
        }

        AfterSelectionChanged();
    }

    private void SetAllVisible(bool on)
    {
        _selection.SelectAllVisible(on);

        _tree.BeginUpdate();
        foreach (TreeNode node in _tree.Nodes) RefreshGroupNode(node);
        _tree.EndUpdate();

        AfterSelectionChanged();
    }

    /// <summary>Beschriftung und Kästchen eines Namensraums nachziehen, auf Wunsch samt Werten.</summary>
    private void RefreshGroupNode(TreeNode node, bool childrenToo = true)
    {
        if (node.Tag is not CleanupSelection.Group group) return;

        node.Text = _selection.GroupLabel(group);
        node.StateImageIndex = ImageFor(_selection.StateOf(group));

        if (!childrenToo) return;

        foreach (TreeNode child in node.Nodes)
            if (child.Tag is string id)
                child.StateImageIndex = ImageFor(_selection.IsSelected(id) ? GroupCheck.All : GroupCheck.None);
    }

    private void ApplyFilter()
    {
        _selection.SetFilter(_search.Text);
        BuildTree();
    }

    /// <summary>Zähler, Hinweis und Skript nachziehen — nach jeder Änderung der Auswahl.</summary>
    private void AfterSelectionChanged()
    {
        _count.Text = _selection.CountText;

        var hidden = _selection.HiddenSelectionHint;
        _hidden.Text = hidden ?? "";
        _hidden.Visible = hidden is not null;

        var ids = _selection.SelectedIds;
        _script.Text = CleanupScriptGenerator.Generate(ids);
        _copy.Enabled = ids.Count > 0;
        _save.Enabled = ids.Count > 0;
    }

    // ---------------------------------------------------------------- Kästchen

    private static int ImageFor(GroupCheck state) => state switch
    {
        GroupCheck.All => 2,
        GroupCheck.Partial => 3,
        _ => 1
    };

    /// <summary>
    /// Die drei Kästchen als Bilder. Index 0 bleibt leer: Im Windows-Baum bedeutet
    /// <c>StateImageIndex = 0</c> „kein Zustandsbild", das erste Bild wäre also nie zu sehen.
    /// </summary>
    private static ImageList BuildCheckImages()
    {
        var list = new ImageList { ImageSize = new Size(16, 16), ColorDepth = ColorDepth.Depth32Bit };
        list.Images.Add(new Bitmap(16, 16));
        list.Images.Add(RenderCheckBox(CheckBoxState.UncheckedNormal));
        list.Images.Add(RenderCheckBox(CheckBoxState.CheckedNormal));
        list.Images.Add(RenderCheckBox(CheckBoxState.MixedNormal));
        return list;
    }

    private static Bitmap RenderCheckBox(CheckBoxState state)
    {
        var bmp = new Bitmap(16, 16);
        using var g = Graphics.FromImage(bmp);

        try
        {
            CheckBoxRenderer.DrawCheckBox(g, new Point(1, 1), state);
        }
        catch (Exception)
        {
            // Ohne aktive Designs wirft der Renderer; dann tut es die schlichte Zeichnung.
            if (state == CheckBoxState.MixedNormal)
                ControlPaint.DrawMixedCheckBox(g, 1, 1, 13, 13, ButtonState.Checked);
            else
                ControlPaint.DrawCheckBox(g, 1, 1, 13, 13,
                    state == CheckBoxState.CheckedNormal ? ButtonState.Checked : ButtonState.Normal);
        }

        return bmp;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _checkImages.Dispose();
        base.Dispose(disposing);
    }

    // ---------------------------------------------------------------- Ausgabe

    /// <summary>
    /// Schreibt das Skript als Datei: UTF-8 ohne BOM und mit LF. Beides ist keine Kosmetik —
    /// eine BOM würde die Shebang-Zeile entwerten, CRLF jede Zeile mit einem \r beenden.
    /// </summary>
    private void SaveScript()
    {
        using var dlg = new SaveFileDialog
        {
            Title = "Aufräum-Skript speichern",
            FileName = CleanupScriptGenerator.SuggestedFileName(_backupName),
            Filter = "Shell-Skript (*.sh)|*.sh|Alle Dateien (*.*)|*.*",
            DefaultExt = "sh",
            AddExtension = true,
            OverwritePrompt = true
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            File.WriteAllText(dlg.FileName,
                CleanupScriptGenerator.ForFile(_script.Text), new UTF8Encoding(false));

            MessageBox.Show(this,
                "Gespeichert:\r\n" + dlg.FileName + "\r\n\r\n" +
                "So geht es weiter:\r\n" +
                "1. Datei auf den ioBroker-Host kopieren — per SFTP, z. B. mit FileZilla\r\n" +
                "   oder WinSCP. Beide verbinden sich mit denselben Zugangsdaten wie SSH.\r\n" +
                "2. In der Shell aufrufen:  bash " + Path.GetFileName(dlg.FileName) + "\r\n" +
                "3. Auf die Frage zuerst mit Enter antworten — das ist der Testlauf.\r\n" +
                "   Erst wenn die Liste stimmt, noch einmal starten und mit einem großen „J\" bestätigen.",
                "Skript gespeichert", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                      or NotSupportedException or PathTooLongException)
        {
            MessageBox.Show(this, "Das Skript konnte nicht gespeichert werden:\r\n\r\n" + ex.Message,
                "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void CopyToClipboard()
    {
        try
        {
            if (!string.IsNullOrEmpty(_script.Text))
                Clipboard.SetText(_script.Text);
        }
        catch (Exception)
        {
            // Die Zwischenablage kann kurzzeitig von einem anderen Prozess belegt sein.
            MessageBox.Show(this, "Kopieren in die Zwischenablage ist gerade nicht möglich. " +
                "Bitte den Text im rechten Feld markieren und mit Strg+C kopieren.",
                "Hinweis", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
