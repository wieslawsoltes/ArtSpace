using System.Collections.ObjectModel;
using System.Text;
using ArtSpace.Skia;

namespace ArtSpace.Workbench;

/// <summary>A complete embeddable editor shell. Storage is injected; there is no static document singleton.</summary>
public sealed partial class StudioWorkbench : UserControl, IDisposable
{
    private readonly IWorkspaceStorage _storage;
    private readonly Grid _root = new();
    private readonly ColumnDefinition _leftColumn = new() { Width = new(248) };
    private readonly ColumnDefinition _rightColumn = new() { Width = new(288) };
    private readonly ListView _layers = new();
    private readonly ReconciledCollection<LayerEntry> _entries = [];
    private readonly StackPanel _pages = new() { Margin = new(8, 2, 8, 12), Spacing = 2 };
    private readonly StackPanel _inspector = new();
    private readonly ContentControl _leftContent = new();
    private readonly TextBox _search = Studio.Input("", "Search layers");
    private readonly StudioButton _title = new();
    private readonly StudioButton _zoom = new();
    private readonly TextBlock _status = Studio.Text("All changes saved locally", 10, Studio.Muted);
    private readonly TextBlock _toast = Studio.Text("", 11, "#FFFFFF");
    private readonly Border _toastBorder;
    private readonly Border _leftPanel;
    private readonly Border _rightPanel;
    private readonly Border _palette;
    private readonly Dictionary<EditorTool, IconButton> _toolButtons = [];
    private readonly DispatcherTimer _autosaveTimer = new() { Interval = TimeSpan.FromMilliseconds(900) };
    private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(4) };
    private readonly SemaphoreSlim _autosaveLock = new(1, 1);
    private bool _refreshing, _assets, _prototype, _uiVisible = true, _initialFit, _disposed;
    private bool _aspectLocked;
    public EditorSession Session { get; }
    public DesignSurface Surface { get; } = new();
    public StudioWorkbench(EditorSession session, IWorkspaceStorage storage)
    {
        Session = session ?? throw new ArgumentNullException(nameof(session)); _storage = storage ?? throw new ArgumentNullException(nameof(storage)); Surface.Session = session;
        HorizontalContentAlignment = HorizontalAlignment.Stretch; VerticalContentAlignment = VerticalAlignment.Stretch;
        _root.Background = Studio.Brush("#E5E5E5"); _root.ColumnDefinitions.Add(_leftColumn); _root.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); _root.ColumnDefinitions.Add(_rightColumn);
        var canvasArea = new Grid(); canvasArea.Children.Add(Surface); Grid.SetColumn(canvasArea, 1); _root.Children.Add(canvasArea);
        _palette = BuildPalette(); _palette.HorizontalAlignment = HorizontalAlignment.Center; _palette.VerticalAlignment = VerticalAlignment.Bottom; _palette.Margin = new(8, 0, 8, 24); canvasArea.Children.Add(_palette);
        _toastBorder = new() { Child = _toast, Background = Studio.Brush("#303030"), CornerRadius = new(8), Padding = new(14, 9), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new(12, 20, 12, 0), Visibility = Visibility.Collapsed, IsHitTestVisible = false };
        canvasArea.Children.Add(_toastBorder);
        _leftPanel = Studio.Surface(BuildLeftPanel()); _leftPanel.Margin = new(8, 8, 0, 8); _root.Children.Add(_leftPanel);
        _rightPanel = Studio.Surface(BuildRightPanel()); _rightPanel.Margin = new(0, 8, 8, 8); Grid.SetColumn(_rightPanel, 2); _root.Children.Add(_rightPanel);
        var reveal = new IconButton("sidebar", "Show or hide editor panels (Tab)", TogglePanels) { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new(12), RestBackground = "#FFFFFF", Background = Studio.Brush("#FFFFFF") };
        reveal.Visibility = Visibility.Collapsed; canvasArea.Children.Add(reveal);
        Content = _root;
        Session.Changed += OnSessionChanged;
        Surface.CommentRequested += (anchor, thread) => RunAsync(() => EditCommentAsync(anchor, thread));
        Surface.CanvasContextRequested += ShowCanvasMenu;
        Surface.StatusChanged += message => ShowStatus(message);
        Surface.PresentationChanged += presenting =>
        {
            _leftPanel.Visibility = _rightPanel.Visibility = _palette.Visibility = presenting ? Visibility.Collapsed : Visibility.Visible;
            _leftColumn.Width = presenting ? new(0) : new(248); _rightColumn.Width = presenting ? new(0) : new(288);
            reveal.Visibility = presenting ? Visibility.Collapsed : Visibility.Visible;
            if (presenting) { _inspectorView?.SuspendEditing(); ShowStatus("Prototype preview · Click linked layers · Esc to return"); }
            else RequestUi(UiDirty.All);
        };
        PreviewKeyDown += HandleApplicationPreviewKey;
        Loaded += (_, _) => DispatcherQueue.TryEnqueue(() =>
        {
            // Do not steal a user's focus if an input/menu already acquired it during startup.
            if (!_disposed && XamlRoot is { } root && FocusManager.GetFocusedElement(root) is null)
                Surface.FocusCanvas();
        });
        KeyDown += OnKeyDown; KeyUp += (_, e) => { if (e.Key == VirtualKey.Space) Surface.IsSpaceDown = false; };
        SizeChanged += (_, _) =>
        {
            if (!_uiVisible || Surface.IsPresenting) return;
            var compact = ActualWidth < 950; _leftColumn.Width = new(compact ? 216 : 248); _rightColumn.Width = new(compact ? 264 : 288);
            if (ActualWidth < 700) { _leftColumn.Width = new(0); _leftPanel.Visibility = Visibility.Collapsed; reveal.Visibility = Visibility.Visible; }
            else { _leftPanel.Visibility = Visibility.Visible; reveal.Visibility = Visibility.Collapsed; }
        };
        Surface.SizeChanged += (_, _) => { if (!_initialFit && Surface.ActualWidth > 200) { _initialFit = true; Surface.Fit(firstFrame: true); } };
        Loaded += (_, _) => { RefreshAll(); if (!_initialFit && Surface.ActualWidth > 200) { _initialFit = true; Surface.Fit(firstFrame: true); } };
        _autosaveTimer.Tick += async (_, _) => { _autosaveTimer.Stop(); await AutosaveAsync(); };
        _toastTimer.Tick += (_, _) => { _toastTimer.Stop(); _toastBorder.Visibility = Visibility.Collapsed; };
        ConfigureIllustrationWorkspace();
        RefreshAll();
    }
    private UIElement BuildLeftPanel()
    {
        var root = new Grid();
        root.RowDefinitions.Add(new() { Height = GridLength.Auto }); root.RowDefinitions.Add(new() { Height = GridLength.Auto }); root.RowDefinitions.Add(new() { Height = GridLength.Auto }); root.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) }); root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var header = new StackPanel();
        var menu = new IconButton("logo", "ArtSpace menu", () => { }); menu.Width = 36; menu.Height = 36; menu.Click += (_, _) => ShowFileMenu(menu);
        var wordmark = Studio.Text("ArtSpace", 12, Studio.Ink, true);
        var search = new IconButton("search", "Search layers", () => { _search.Visibility = _search.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible; if (_search.Visibility == Visibility.Visible) _search.Focus(FocusState.Programmatic); });
        var top = Studio.Columns((menu, 36), (wordmark, -1), (search, 30)); top.Margin = new(9, 8, 9, 3); top.ColumnSpacing = 4; header.Children.Add(top);
        _title.HorizontalContentAlignment = HorizontalAlignment.Left; _title.Padding = new(13, 2); _title.Height = 30; _title.FontWeight = new() { Weight = 600 }; _title.Click += (_, _) => RunAsync(RenameDocumentAsync); header.Children.Add(_title);
        var breadcrumb = Studio.Text("Drafts  /  Local file", 10, Studio.Muted); breadcrumb.Margin = new(22, 0, 16, 15); header.Children.Add(breadcrumb);
        var file = new StudioButton("File", () => { _assets = false; RefreshLeftContent(); }) { FontWeight = new() { Weight = 600 }, HorizontalAlignment = HorizontalAlignment.Left, Padding = new(10, 6) };
        var assets = new StudioButton("Assets", () => { _assets = true; RefreshLeftContent(); }) { HorizontalAlignment = HorizontalAlignment.Left };
        var tabs = Studio.Columns((file, 46), (assets, 60)); tabs.Margin = new(10, 0, 10, 8); header.Children.Add(tabs);
        _search.PlaceholderText = "Search all layers…"; _search.Margin = new(12, 0, 12, 12); _search.Visibility = Visibility.Collapsed; _search.TextChanged += (_, _) => RequestUi(UiDirty.Layers | UiDirty.LayerSelection); header.Children.Add(_search); header.Children.Add(Studio.Rule());
        root.Children.Add(header);
        var pageHeader = Studio.Columns((Studio.Text("Pages", 11, Studio.Ink, true), -1), (new IconButton("plus", "Add page", () => Run(Session.AddPage)), 26)); pageHeader.Margin = new(16, 8, 12, 0);
        var pageArea = new StackPanel(); pageArea.Children.Add(pageHeader); pageArea.Children.Add(_pages); pageArea.Children.Add(Studio.Rule()); Grid.SetRow(pageArea, 1); root.Children.Add(pageArea);
        var layerHeader = Studio.Columns((Studio.Text("Layers", 11, Studio.Ink, true), -1), (new IconButton("layers", "Collapse all layers", () => { foreach (var n in Session.Page.AllNodes()) n.Expanded = false; RefreshLayers(true); }), 26)); layerHeader.Margin = new(16, 8, 12, 6); Grid.SetRow(layerHeader, 2); root.Children.Add(layerHeader);
        _layers.ItemsSource = _entries; _layers.SelectionMode = ListViewSelectionMode.Extended; _layers.ItemTemplate = (DataTemplate)StudioResources.Current["VS.LayerRow"]; _layers.ItemContainerStyle = (Style)StudioResources.Current["VS.LayerContainer"]; _layers.Background = Studio.Brush("#00FFFFFF"); _layers.BorderThickness = new(0); _layers.Padding = new(0);
        _layers.SelectionChanged += (_, _) => { if (!_refreshing) Session.Select(_layers.SelectedItems.OfType<LayerEntry>().Select(x => x.Id).ToArray()); };
        _leftContent.HorizontalContentAlignment = HorizontalAlignment.Stretch; _leftContent.VerticalContentAlignment = VerticalAlignment.Stretch; _leftContent.Content = _layers; Grid.SetRow(_leftContent, 3); root.Children.Add(_leftContent);
        var footer = new StackPanel(); footer.Children.Add(Studio.Rule()); var statusRow = Studio.Columns((new IconView { Glyph = "check", Color = "#14AE5C", Width = 14, Height = 14, VerticalAlignment = VerticalAlignment.Center }, 14), (_status, -1), (new IconButton("help", "Keyboard shortcuts and about", () => RunAsync(ShowHelpAsync)), 26)); statusRow.Margin = new(12, 8, 9, 8); footer.Children.Add(statusRow); Grid.SetRow(footer, 4); root.Children.Add(footer); return root;
    }
    private UIElement BuildRightPanel()
    {
        var root = new Grid(); root.RowDefinitions.Add(new() { Height = GridLength.Auto }); root.RowDefinitions.Add(new() { Height = GridLength.Auto }); root.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        var avatar = new Border { Width = 28, Height = 28, CornerRadius = new(14), Background = Studio.Brush("#F5D7A6"), Child = Studio.Text("Y", 11, "#775719", true), Padding = new(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        ToolTipService.SetToolTip(avatar, "You · Local editing");
        var present = new IconButton("play", "Present prototype", () => Run(Surface.Present));
        var share = new StudioButton("Share", () => RunAsync(ShowShareAsync)) { IsPrimary = true, Height = 32, Padding = new(17, 6), FontWeight = new() { Weight = 600 } };
        var top = Studio.Columns((avatar, 28), (new Grid(), -1), (present, 30), (share, 70)); top.Margin = new(16, 12, 12, 12); root.Children.Add(top);
        var design = new StudioButton("Design", () => { _prototype = false; RefreshInspector(); }) { FontWeight = new() { Weight = 600 }, Padding = new(5, 6) };
        var prototype = new StudioButton("Prototype", () => { _prototype = true; RefreshInspector(); }) { Padding = new(8, 6) };
        _zoom.Padding = new(5, 6); _zoom.FontSize = 11; _zoom.Click += (_, _) => ShowZoomMenu(_zoom);
        var tabs = Studio.Columns((design, 51), (prototype, 73), (new Grid(), -1), (_zoom, 62)); tabs.ColumnSpacing = 2; tabs.Margin = new(11, 0, 10, 8); Grid.SetRow(tabs, 1); root.Children.Add(tabs);
        var scroll = Studio.Scroll(_inspector); Grid.SetRow(scroll, 2); root.Children.Add(scroll); return root;
    }
    private Border BuildPalette()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3, Margin = new(7) };
        AddToolGroup(EditorTool.Move, "move", "Move (V)", [(EditorTool.Move, "Move", "V"), (EditorTool.Scale, "Scale", "K")]);
        AddToolGroup(EditorTool.Frame, "frame", "Frame (F)", [(EditorTool.Frame, "Frame", "F"), (EditorTool.Section, "Section", "Shift S"), (EditorTool.Slice, "Slice", "S")]);
        AddToolGroup(EditorTool.Rectangle, "rectangle", "Rectangle (R)", [(EditorTool.Rectangle, "Rectangle", "R"), (EditorTool.Ellipse, "Ellipse", "O"), (EditorTool.Line, "Line", "L"), (EditorTool.Arrow, "Arrow", "Shift L"), (EditorTool.Polygon, "Polygon", ""), (EditorTool.Star, "Star", "")]);
        AddToolGroup(EditorTool.Pen, "pen", "Pen (P)", [(EditorTool.Pen, "Pen", "P"), (EditorTool.Pencil, "Pencil", "Shift P")]);
        AddTool(EditorTool.Text, "text", "Text (T)"); AddTool(EditorTool.Hand, "hand", "Hand (H / Space)"); AddTool(EditorTool.Comment, "comment", "Comment (C)");
        row.Children.Add(new Border { Width = 1, Height = 24, Background = Studio.Brush(Studio.Line), Margin = new(5, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(new IconButton("component", "Insert component", () => { _assets = true; RefreshLeftContent(); }) { Width = 36, Height = 36 });
        var more = new IconButton("more", "Quick actions", () => RunAsync(ShowQuickActionsAsync)) { Width = 30, Height = 36 }; row.Children.Add(more);
        return Studio.Surface(row, 12);
        void AddTool(EditorTool tool, string glyph, string label)
        {
            var button = new IconButton(glyph, label, () => { Surface.FinishTextEdit(true); Session.Tool = tool; Surface.FocusCanvas(); }) { Width = 36, Height = 36, Padding = new(9), CornerRadius = new(7) }; _toolButtons[tool] = button; row.Children.Add(button);
        }
        void AddToolGroup(EditorTool tool, string glyph, string label, (EditorTool Tool, string Name, string Shortcut)[] choices)
        {
            AddTool(tool, glyph, label);
            var dropdown = new IconButton("chevron-down", label + " alternatives", () => { }) { Width = 16, Height = 36, Padding = new(0) };
            dropdown.Click += (_, _) =>
            {
                var menu = new MenuFlyout();
                foreach (var choice in choices) { var item = new MenuFlyoutItem { Text = choice.Name + (choice.Shortcut.Length > 0 ? "    " + choice.Shortcut : ""), FontSize = 12 }; item.Click += (_, _) => { Session.Tool = choice.Tool; Surface.FocusCanvas(); }; menu.Items.Add(item); }
                menu.ShowAt(dropdown);
            };
            row.Children.Add(dropdown);
        }
    }
    private void OnSessionChanged(object? sender, EditorChangedEventArgs e)
    {
        if (_disposed) return;
        if (e.Kind is EditorChangeKind.Document or EditorChangeKind.Selection)
        {
            SuspendAppearanceEditing();
            // Invalidate input ownership synchronously; drawing and rebinding are coalesced.
            if (!ReferenceEquals(_inspectorDocument, Session.Document) || !ReferenceEquals(_inspectorPrimary, Session.Primary)
                || !_inspectorSelection.SetEquals(Session.SelectedIds)) _inspectorView?.SuspendEditing();
            if (!ReferenceEquals(_controlPrimary, Session.Primary) || !_controlSelection.SetEquals(Session.SelectedIds))
            { _fillControl?.CancelEdit(); _strokeControl?.CancelEdit(); _strokeWidthControl?.CancelEdit(); }
        }
        switch (e.Kind)
        {
            case EditorChangeKind.Document:
                RequestUi(UiDirty.All);
                if (e.Label != "Switch page") { _status.Text = "Saving locally…"; _autosaveTimer.Stop(); _autosaveTimer.Start(); }
                break;
            case EditorChangeKind.Selection:
                RequestUi(UiDirty.Inspector | UiDirty.LayerSelection | UiDirty.ControlBar);
                break;
            case EditorChangeKind.Preview:
                RequestUi(UiDirty.Inspector | UiDirty.ControlBar);
                break;
            case EditorChangeKind.Viewport:
                RequestUi(UiDirty.Header);
                break;
            case EditorChangeKind.Tool:
                RequestUi(UiDirty.Tools | UiDirty.Inspector);
                break;
        }
    }
    private void RefreshAll() => RequestUi(UiDirty.All);
    private void RefreshTools()
    {
        foreach (var (tool, button) in _toolButtons)
        {
            button.IsSelected = tool == Session.Tool;
        }
    }
    private void RefreshPages()
    {
        _pages.Children.Clear();
        foreach (var page in Session.Document.Pages)
        {
            var label = Studio.Text(page.Name, 11, Studio.Ink, page == Session.Page);
            var icon = new IconView { Glyph = page == Session.Page ? "check" : "page", Width = 14, Height = 14, Color = page == Session.Page ? Studio.Ink : "#AAAAAA", VerticalAlignment = VerticalAlignment.Center };
            var button = new StudioButton { Content = Studio.Columns((icon, 14), (label, -1)), HorizontalContentAlignment = HorizontalAlignment.Stretch, Height = 30, Padding = new(8, 3), RestBackground = page == Session.Page ? "#F3F3F3" : "#00FFFFFF", Background = Studio.Brush(page == Session.Page ? "#F3F3F3" : "#00FFFFFF") };
            AutomationProperties.SetName(button, "Page " + page.Name); button.Click += (_, _) => { Run(() => Session.SetPage(page.Id)); Surface.Fit(); };
            button.RightTapped += (_, e) => { var menu = new MenuFlyout(); AddMenu(menu, "Rename page", () => RunAsync(async () => { var text = await PromptAsync("Rename page", page.Name); if (!string.IsNullOrWhiteSpace(text)) Session.Edit("Rename page", () => page.Name = text); })); AddMenu(menu, "Delete page", () => Run(() => Session.DeletePage(page.Id)), Session.Document.Pages.Count > 1); menu.ShowAt(button); e.Handled = true; };
            _pages.Children.Add(button);
        }
    }
    private void TogglePanels()
    {
        _uiVisible = !_uiVisible; ResizeIllustrationWorkspace(); Surface.FocusCanvas();
    }
    public void ShowStatus(string message, bool error = false)
    {
        _toast.Text = message; _toastBorder.Background = Studio.Brush(error ? "#AA302C" : "#303030"); _toastBorder.Visibility = Visibility.Visible; _toastTimer.Stop(); _toastTimer.Start();
    }
    private async Task AutosaveAsync()
    {
        if (_disposed || Session.IsInteracting) { if (!_disposed) _autosaveTimer.Start(); return; }
        await _autosaveLock.WaitAsync();
        try
        {
            if (_disposed || Session.IsInteracting) { if (!_disposed) _autosaveTimer.Start(); return; }
            var revision = Session.DocumentRevision;
            var json = DocumentJson.Save(Session.Document);
            await _storage.WriteAutosaveAsync(json);
            if (_disposed) return;
            if (revision == Session.DocumentRevision) _status.Text = "All changes saved locally";
            else { _status.Text = "Saving locally…"; _autosaveTimer.Stop(); _autosaveTimer.Start(); }
        }
        catch (Exception ex) { _status.Text = "Local save failed"; ShowStatus("Autosave failed: " + ex.Message + ". Download a document copy.", true); }
        finally { _autosaveLock.Release(); }
    }
    private void Run(Action action)
    {
        try { Surface.FinishTextEdit(true); Surface.FinishPath(false); action(); }
        catch (Exception ex) { ShowStatus(ex.Message, true); }
    }
    private async void RunAsync(Func<Task> action)
    {
        try { Surface.FinishTextEdit(true); Surface.FinishPath(false); await action(); }
        catch (Exception ex) { ShowStatus(ex.Message, true); }
    }
    private static TextBlock Wrapped(string text, double size = 11, string color = Studio.Muted)
    {
        var block = Studio.Text(text, size, color); block.TextWrapping = TextWrapping.Wrap; block.TextTrimming = TextTrimming.None; block.LineHeight = size * 1.55; return block;
    }
    private static string Glyph(NodeKind kind) => kind == NodeKind.Path ? "pen" : kind.ToString().ToLowerInvariant();
    public new void Dispose()
    {
        if (_disposed) return; _disposed = true; Session.Changed -= OnSessionChanged; _autosaveTimer.Stop(); _toastTimer.Stop(); _inspectorView?.Dispose(); _appearanceView?.Dispose(); _graphicStylesView?.Dispose(); _layerEntries.Clear(); _artboardButtons.Clear(); UiRefreshed = null;
        if (_applicationMenu is not null) _applicationMenu.NavigationChanged -= OnMenuNavigationChanged;
        MenuNavigationChanged = null; Surface.Dispose();
    }
}
