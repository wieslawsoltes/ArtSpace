using System.Diagnostics;

namespace ArtSpace.Workbench;

public sealed partial class StudioWorkbench
{
    [Flags]
    private enum UiDirty
    {
        None = 0, Header = 1, Inspector = 2, Layers = 4, LayerSelection = 8,
        Artboards = 16, History = 32, Tools = 64, ControlBar = 128, Pages = 256, Assets = 512, Appearance = 1024, GraphicStyles = 2048, Stroke = 4096,
        All = Header | Inspector | Layers | LayerSelection | Artboards | History | Tools | ControlBar | Pages | Assets | Appearance | GraphicStyles | Stroke
    }
    private UiDirty _uiDirty;
    private bool _uiQueued;
    private double _displayedZoom = double.NaN;
    private readonly Dictionary<string, LayerEntry> _layerEntries = new(StringComparer.Ordinal);
    private readonly Dictionary<string, StudioButton> _artboardButtons = new(StringComparer.Ordinal);
    private readonly List<TextBlock> _historyRows = [];
    private StudioButton? _newArtboardButton, _historyUndo, _historyRedo, _assetCreateButton;
    private string? _assetSignature;
    private UIElement? _assetView;
    private DesignNode? _controlPrimary;
    private readonly HashSet<string> _controlSelection = [];
    public event Action? UiRefreshed;
    public bool UiPending => _uiQueued;
    public bool IsDisposed => _disposed;
    public string ActivePanel => _illustrationDock.SelectedName;
    public long UiFlushes { get; private set; }
    public long UiRefreshFailures { get; private set; }
    public double LastUiRefreshMs { get; private set; }
    public long LayerPasses { get; private set; }
    public long LayerEntryBuilds { get; private set; }
    public long LayerSelectionChanges { get; private set; }
    public long ArtboardRefreshes { get; private set; }
    public long HistoryRefreshes { get; private set; }
    public long LayerCollectionResets => _entries.ResetCount;
    public IEnumerable<string> SelectedLayerIds => _layers.SelectedItems.OfType<LayerEntry>().Select(x => x.Id);

    private bool IsPanelVisible(string name) => !_illustrationReady ||
        (_rightPanel.Visibility == Visibility.Visible && _illustrationDock.SelectedName == name);

    private void RequestUi(UiDirty dirty)
    {
        if (_disposed) return;
        if ((dirty & UiDirty.Inspector) != 0) dirty |= UiDirty.Appearance | UiDirty.GraphicStyles | UiDirty.Stroke;
        _uiDirty |= dirty;
        if (_uiQueued) return;
        _uiQueued = true;
        if (!DispatcherQueue.TryEnqueue(FlushUi)) _uiQueued = false;
    }

    private void FlushUi()
    {
        _uiQueued = false;
        if (_disposed) return;
        var dirty = _uiDirty; _uiDirty = UiDirty.None;
        var started = Stopwatch.GetTimestamp();
        try
        {
            if ((dirty & UiDirty.Header) != 0) RefreshHeader();
            if ((dirty & UiDirty.Tools) != 0) RefreshTools();
            if ((dirty & UiDirty.ControlBar) != 0) RefreshControlBar();
            if ((dirty & UiDirty.Inspector) != 0)
            {
                if (IsPanelVisible("Properties")) RefreshInspector();
                else { _inspectorView?.SuspendEditing(); _uiDirty |= UiDirty.Inspector; }
            }
            if ((dirty & (UiDirty.Layers | UiDirty.LayerSelection | UiDirty.Assets)) != 0)
            {
                if (IsPanelVisible("Layers"))
                {
                    if (_assets)
                    {
                        if ((dirty & UiDirty.Assets) != 0) RefreshLeftContent();
                        if (_assetCreateButton is not null) _assetCreateButton.IsEnabled = Session.SelectionRoots.Count == 1;
                        _uiDirty |= dirty & (UiDirty.Layers | UiDirty.LayerSelection);
                    }
                    else
                    {
                        if ((dirty & UiDirty.Layers) != 0) RefreshLayers();
                        else if ((dirty & UiDirty.LayerSelection) != 0) SyncLayerSelection();
                        _uiDirty |= dirty & UiDirty.Assets;
                    }
                }
                else _uiDirty |= dirty & (UiDirty.Layers | UiDirty.LayerSelection | UiDirty.Assets);
            }
            if ((dirty & UiDirty.Artboards) != 0)
            {
                if (IsPanelVisible("Artboards")) RefreshArtboards(); else _uiDirty |= UiDirty.Artboards;
            }
            if ((dirty & UiDirty.History) != 0)
            {
                if (IsPanelVisible("History")) RefreshHistory(); else _uiDirty |= UiDirty.History;
            }
            RefreshAppearancePanels(dirty);
            RefreshStrokePanel(dirty);
            // The illustration shell does not display the legacy Pages list.
            if ((dirty & UiDirty.Pages) != 0 && !_illustrationReady) RefreshPages();
        }
        catch (Exception ex)
        {
            UiRefreshFailures++;
            ShowStatus("Panel update failed: " + ex.Message, true);
        }
        finally
        {
            LastUiRefreshMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            UiFlushes++;
            UiRefreshed?.Invoke();
        }
    }

    private void RefreshHeader()
    {
        if (!Equals(_title.Content, Session.Document.Name)) _title.Content = Session.Document.Name;
        if (AutomationProperties.GetName(_title) != "Rename document") AutomationProperties.SetName(_title, "Rename document");
        if (_displayedZoom == Session.Viewport.Zoom) return;
        _displayedZoom = Session.Viewport.Zoom;
        _zoom.Content = Numbers.Format(_displayedZoom * 100) + "%⌄";
    }

    private void RefreshControlBar()
    {
        if (!_illustrationReady) return;
        var primary = Session.Primary;
        var retarget = !ReferenceEquals(primary, _controlPrimary) || !_controlSelection.SetEquals(Session.SelectedIds);
        _controlPrimary = primary;
        if (retarget) { _controlSelection.Clear(); _controlSelection.UnionWith(Session.SelectedIds); }
        var label = primary is null ? "No Selection" : primary.Kind == NodeKind.Frame ? "Artboard" : primary.Kind.ToString();
        if (_selectionLabel.Text != label) _selectionLabel.Text = label;
        _syncingAppearance = true;
        try
        {
            if (primary is not null)
            {
                Surface.FillColor = primary.Fill;
                Surface.StrokeColor = primary.Strokes.FirstOrDefault()?.Color ?? Surface.StrokeColor;
                Surface.StrokeWidth = primary.Strokes.FirstOrDefault()?.Width ?? 0;
            }
            _fillControl?.UpdateFromModel(Surface.FillColor, retarget);
            _strokeControl?.UpdateFromModel(Surface.StrokeColor, retarget);
            _strokeWidthControl?.UpdateFromModel(Surface.StrokeWidth, retarget);
            if (_opacityControl is not null)
            {
                var value = Numbers.Format((primary?.Opacity ?? 1) * 100) + "%";
                // ItemsSource-backed Items cannot be mutated. Keep at most one custom percentage.
                if (!_opacityControl.Items.Contains(value)) _opacityControl.ItemsSource = new[] { "100%", "75%", "50%", "25%", "10%", value };
                if (retarget) _opacityControl.IsDropDownOpen = false;
                if (!Equals(_opacityControl.SelectedItem, value)) _opacityControl.SelectedItem = value;
            }
        }
        finally { _syncingAppearance = false; }
    }

    private void RefreshLayers(bool force = false)
    {
        if (!IsPanelVisible("Layers")) { _uiDirty |= UiDirty.Layers | UiDirty.LayerSelection; return; }
        LayerPasses++;
        var filter = _search.Text.Trim();
        var desired = new List<LayerEntry>();
        var retained = new HashSet<string>(StringComparer.Ordinal);
        for (var i = Session.Page.Nodes.Count - 1; i >= 0; i--) Append(Session.Page.Nodes[i], 0);
        void Append(DesignNode node, int depth)
        {
            if (filter.Length == 0 || node.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                var rowDepth = filter.Length == 0 ? depth : 0;
                var glyph = Glyph(node.Kind);
                var component = node.Kind is NodeKind.Component or NodeKind.Instance;
                if (!_layerEntries.TryGetValue(node.Id, out var entry) || entry.Name != node.Name || entry.Glyph != glyph ||
                    entry.Depth != rowDepth || entry.Visible != node.Visible || entry.Locked != node.Locked ||
                    entry.Expanded != node.Expanded || entry.HasChildren != (node.Children.Count > 0) || entry.IsComponent != component)
                {
                    var id = node.Id;
                    entry = new LayerEntry
                    {
                        Id = id, Name = node.Name, Glyph = glyph, Depth = rowDepth, Visible = node.Visible, Locked = node.Locked,
                        Expanded = node.Expanded, HasChildren = node.Children.Count > 0, IsComponent = component,
                        ToggleExpanded = () => { if (Session.Document.Find(id) is { } current) { current.Expanded = !current.Expanded; RequestUi(UiDirty.Layers | UiDirty.LayerSelection); } },
                        ToggleVisibility = () => Run(() => { if (Session.Document.Find(id) is { } current) Session.Edit("Toggle layer visibility", () => current.Visible = !current.Visible); }),
                        ToggleLocked = () => Run(() => { if (Session.Document.Find(id) is { } current) Session.Edit("Toggle layer lock", () => current.Locked = !current.Locked); }),
                        Rename = () => { if (Session.Document.Find(id) is { } current) RunAsync(() => RenameLayerAsync(current)); }
                    };
                    _layerEntries[id] = entry; LayerEntryBuilds++;
                }
                desired.Add(entry); retained.Add(node.Id);
            }
            if (filter.Length > 0 || node.Expanded) for (var i = node.Children.Count - 1; i >= 0; i--) Append(node.Children[i], depth + 1);
        }
        var wasRefreshing = _refreshing; _refreshing = true;
        try
        {
            _entries.Apply(desired);
            foreach (var id in _layerEntries.Keys.Where(id => !retained.Contains(id)).ToArray()) _layerEntries.Remove(id);
            SyncLayerSelection();
        }
        finally { _refreshing = wasRefreshing; }
    }

    private void SyncLayerSelection()
    {
        var wasRefreshing = _refreshing; _refreshing = true;
        try
        {
            var current = _layers.SelectedItems;
            for (var i = current.Count - 1; i >= 0; i--)
            {
                if (current[i] is LayerEntry entry && Session.SelectedIds.Contains(entry.Id) &&
                    _layerEntries.TryGetValue(entry.Id, out var retained) && ReferenceEquals(entry, retained)) continue;
                current.RemoveAt(i); LayerSelectionChanges++;
            }
            var selected = current.OfType<LayerEntry>().Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var id in Session.SelectedIds)
                if (!selected.Contains(id) && _layerEntries.TryGetValue(id, out var entry)) { current.Add(entry); LayerSelectionChanges++; }
        }
        finally { _refreshing = wasRefreshing; }
    }

    private void RefreshArtboards()
    {
        ArtboardRefreshes++;
        _newArtboardButton ??= new StudioButton("+ New artboard", () => Run(AddArtboard)) { HorizontalAlignment = HorizontalAlignment.Stretch, RestBackground = Studio.Field };
        var desired = new List<UIElement> { _newArtboardButton };
        var retained = new HashSet<string>(StringComparer.Ordinal);
        foreach (var board in Session.Page.Nodes.Where(n => n.IsFrame))
        {
            if (!_artboardButtons.TryGetValue(board.Id, out var button))
            {
                var id = board.Id;
                button = new StudioButton("", () => { if (Session.Document.Find(id) is { } current) { Session.Select(current); Surface.Fit(true); } })
                { HorizontalContentAlignment = HorizontalAlignment.Left, HorizontalAlignment = HorizontalAlignment.Stretch, Height = 33, Padding = new(8) };
                _artboardButtons[id] = button;
            }
            var label = board.Name + "   " + Numbers.Format(board.Width) + " × " + Numbers.Format(board.Height);
            if (!Equals(button.Content, label)) { button.Content = label; AutomationProperties.SetName(button, label); }
            desired.Add(button); retained.Add(board.Id);
        }
        ReconcileChildren(_artboards, desired);
        foreach (var id in _artboardButtons.Keys.Where(id => !retained.Contains(id)).ToArray()) _artboardButtons.Remove(id);
    }

    private void RefreshHistory()
    {
        HistoryRefreshes++;
        if (_historyUndo is null)
        {
            _historyUndo = new StudioButton("Undo", () => Run(Session.Undo));
            _historyRedo = new StudioButton("Redo", () => Run(Session.Redo));
            _historyPanel.Children.Add(Studio.Columns((_historyUndo, -1), (_historyRedo, -1)));
        }
        _historyUndo.IsEnabled = Session.CanUndo; _historyRedo!.IsEnabled = Session.CanRedo;
        var history = Session.History; var count = Math.Min(100, history.Count);
        for (var i = 0; i < count; i++)
        {
            if (i == _historyRows.Count) { var row = Studio.Text("", 11, Studio.Muted); _historyRows.Add(row); _historyPanel.Children.Add(row); }
            var label = history[history.Count - i - 1];
            if (_historyRows[i].Text != label) _historyRows[i].Text = label;
        }
        while (_historyRows.Count > count)
        {
            _historyPanel.Children.RemoveAt(_historyPanel.Children.Count - 1); _historyRows.RemoveAt(_historyRows.Count - 1);
        }
    }

    private static void ReconcileChildren(Panel panel, IReadOnlyList<UIElement> desired)
    {
        for (var i = 0; i < desired.Count; i++)
        {
            var view = desired[i];
            if (i < panel.Children.Count && ReferenceEquals(panel.Children[i], view)) continue;
            var old = panel.Children.IndexOf(view);
            if (old >= 0) panel.Children.RemoveAt(old);
            panel.Children.Insert(i, view);
        }
        while (panel.Children.Count > desired.Count) panel.Children.RemoveAt(panel.Children.Count - 1);
    }

    private void RefreshLeftContent()
    {
        if (!_assets)
        {
            if (!ReferenceEquals(_leftContent.Content, _layers)) _leftContent.Content = _layers;
            RequestUi(UiDirty.Layers | UiDirty.LayerSelection); return;
        }
        var components = Session.Document.AllNodes().Where(n => n.Kind == NodeKind.Component).ToArray();
        var signature = string.Join('|', components.Select(c => c.Id + ":" + c.Name));
        if (_assetView is null || signature != _assetSignature)
        {
            _assetSignature = signature;
            var root = new StackPanel { Margin = new(14, 8, 14, 20), Spacing = 10 }; root.Children.Add(Studio.Text("Local components", 12, Studio.Ink, true));
            if (components.Length == 0) root.Children.Add(Wrapped("Select a layer and create a component to reuse it throughout this document."));
            foreach (var component in components)
            {
                var id = component.Id;
                var tile = new StudioButton { Content = Studio.Columns((new IconView { Glyph = "component", Color = "#9747FF", VerticalAlignment = VerticalAlignment.Center }, 20), (Studio.Text(component.Name, 11, "#9747FF"), -1)), HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new(12), Height = 56, BorderBrush = Studio.Brush(Studio.Line), BorderThickness = new(1), CornerRadius = new(8) };
                AutomationProperties.SetName(tile, "Insert " + component.Name);
                tile.Click += (_, _) => Run(() =>
                {
                    if (Session.Document.Find(id) is not { } current) return;
                    var p = Session.Viewport.ScreenToWorld(new(Surface.ActualWidth / 2, Surface.ActualHeight / 2));
                    ComponentService.InsertInstance(Session, current, p - new Vec2(current.Width / 2, current.Height / 2));
                });
                root.Children.Add(tile);
            }
            _assetCreateButton = new StudioButton("Create component from selection", () => Run(() => ComponentService.MakeComponent(Session))) { RestBackground = Studio.Field, Background = Studio.Brush(Studio.Field) };
            root.Children.Add(_assetCreateButton); _assetView = Studio.Scroll(root);
        }
        _assetCreateButton!.IsEnabled = Session.SelectionRoots.Count == 1;
        if (!ReferenceEquals(_leftContent.Content, _assetView)) _leftContent.Content = _assetView;
    }
}
