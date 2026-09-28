"""One-shot, base-guarded source integration. Removed from the materialized commit."""
from pathlib import Path
import subprocess

def load(path, sha=None):
    p = Path(path)
    if sha:
        actual = subprocess.check_output(['git', 'hash-object', str(p)], text=True).strip()
        assert actual == sha, (path, actual, sha)
    return p.read_text()

def save(path, text):
    Path(path).write_text(text)

def replace(text, before, after):
    assert text.count(before) == 1, (before[:180], text.count(before))
    return text.replace(before, after)

def block(text, begin, end, replacement):
    assert text.count(begin) == 1 and text.count(end) == 1, (begin, end)
    a, b = text.index(begin), text.index(end)
    assert a < b
    return text[:a] + replacement + text[b:]

p = 'src/ArtSpace.Workbench/StudioWorkbench.cs'
s = load(p, '68e21fe496342f2514a4a3b73b0349dc8410ed60')
s = replace(s, 'private readonly ObservableCollection<LayerEntry> _entries = [];', 'private readonly ReconciledCollection<LayerEntry> _entries = [];')
s = replace(s, '    private string _layerSignature = "";\n', '')
s = replace(s, '_search.TextChanged += (_, _) => RefreshLayers(true);', '_search.TextChanged += (_, _) => RequestUi(UiDirty.Layers | UiDirty.LayerSelection);')
s = block(s, '    private void OnSessionChanged(', '    private void RefreshTools()', '''    private void OnSessionChanged(object? sender, EditorChangedEventArgs e)
    {
        if (_disposed) return;
        if (e.Kind is EditorChangeKind.Document or EditorChangeKind.Selection)
        {
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
''')
s = block(s, '    private void RefreshLayers(', '    private void TogglePanels()', '')
s = replace(s, 'if (presenting) ShowStatus("Prototype preview · Click linked layers · Esc to return");', 'if (presenting) { _inspectorView?.SuspendEditing(); ShowStatus("Prototype preview · Click linked layers · Esc to return"); }\n            else RequestUi(UiDirty.All);')
s = replace(s, 'try { var json = DocumentJson.Save(Session.Document); await _storage.WriteAutosaveAsync(json); _status.Text = "All changes saved locally"; }', '''try
        {
            if (_disposed || Session.IsInteracting) { if (!_disposed) _autosaveTimer.Start(); return; }
            var revision = Session.DocumentRevision;
            var json = DocumentJson.Save(Session.Document);
            await _storage.WriteAutosaveAsync(json);
            if (_disposed) return;
            if (revision == Session.DocumentRevision) _status.Text = "All changes saved locally";
            else { _status.Text = "Saving locally…"; _autosaveTimer.Stop(); _autosaveTimer.Start(); }
        }''')
s = replace(s, '_autosaveTimer.Stop(); _toastTimer.Stop(); Surface.Dispose();', '_autosaveTimer.Stop(); _toastTimer.Stop(); _inspectorView?.Dispose(); _layerEntries.Clear(); _artboardButtons.Clear(); UiRefreshed = null; Surface.Dispose();')
save(p, s)

p = 'src/ArtSpace.Workbench/StudioWorkbench.Illustration.cs'
s = load(p, 'c403498364d6e8a7ef11f1c1dfc97c0152873c49')
s = replace(s, '        _illustrationReady = true;', '''        _illustrationReady = true;
        _illustrationDock.SelectionChanged += name =>
        {
            if (name != "Properties") _inspectorView?.SuspendEditing();
            RequestUi(name switch
            {
                "Properties" => UiDirty.Inspector,
                "Layers" => UiDirty.Layers | UiDirty.LayerSelection | UiDirty.Assets,
                "Artboards" => UiDirty.Artboards,
                "History" => UiDirty.History,
                _ => UiDirty.None
            });
        };''')
s = replace(s, '        _rightPanel.Visibility = showRight ? Visibility.Visible : Visibility.Collapsed;', '''        var wasVisible = _rightPanel.Visibility == Visibility.Visible;
        _rightPanel.Visibility = showRight ? Visibility.Visible : Visibility.Collapsed;
        if (showRight && !wasVisible) RequestUi(UiDirty.All);
        else if (!showRight) _inspectorView?.SuspendEditing();''')
s = block(s, '    private void RefreshIllustrationPanels()', '    private void AddArtboard()', '''    private void RefreshIllustrationPanels() => RequestUi(UiDirty.ControlBar | UiDirty.Artboards | UiDirty.History);

''')
s = block(s, '    private void AddIllustrationSections()', '    private IEnumerable<MenuCommand> IllustrationMenu(', '')
save(p, s)

p = 'src/ArtSpace.Editing/EditorSession.cs'
s = load(p, '8af1cc85b64199fba94afd1b722f12495729cf07')
s = replace(s, 'public sealed class EditorSession\n', 'public sealed partial class EditorSession\n')
s = replace(s, 'public IReadOnlyList<string> History => _undo.Select(e => e.Label).ToArray();', 'public IReadOnlyList<string> History => _historyCache ??= Array.AsReadOnly(_undo.Select(e => e.Label).ToArray());')
s = block(s, '    private IReadOnlyList<DesignNode>? _selectionCache, _rootsCache;', '    public EditorTool Tool', '')
s = block(s, '    public void Select(IEnumerable<string> ids, bool toggle = false)', '    public void SelectAll()', '')
s = replace(s, 'if (kind is EditorChangeKind.Document or EditorChangeKind.Selection) InvalidateSelection();', '''if (kind == EditorChangeKind.Document) { InvalidateSceneSelection(); _historyCache = null; DocumentRevision++; }
        else if (kind == EditorChangeKind.Selection) InvalidateSelection();''')
s = replace(s, 'InvalidateSelection(); _before = Capture();', 'InvalidateSceneSelection(); _before = Capture();')
s = replace(s, '(parent?.Children ?? Page.Nodes).Add(node); InvalidateSelection();', '(parent?.Children ?? Page.Nodes).Add(node); InvalidateSceneSelection();')
s = replace(s, '        InvalidateSelection();\n    }\n    public void DeleteSelection()', '        InvalidateSceneSelection();\n    }\n    public void DeleteSelection()')
s = replace(s, '    private Snapshot Capture() => new(DocumentJson.Save(Document), Page.Id, _selected.ToArray());', '''    private Snapshot Capture()
    {
        SnapshotCaptures++;
        return new(DocumentJson.Save(Document), Page.Id, _selected.ToArray());
    }''')
s = replace(s, '_selected.Clear(); _selected.UnionWith(state.Selection.Where(id => Page.AllNodes().Any(n => n.Id == id))); IsDirty = state.Json != _savedJson;', '''InvalidateSceneSelection();
        var existing = Page.AllNodes().Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
        _selected.Clear(); _selected.UnionWith(state.Selection.Where(existing.Contains)); IsDirty = state.Json != _savedJson;''')
s = replace(s, '_savedJson = json ?? DocumentJson.Save(Document); IsDirty = DocumentJson.Save(Document) != _savedJson; Notify(EditorChangeKind.Selection);', 'var current = DocumentJson.Save(Document); _savedJson = json ?? current; IsDirty = current != _savedJson; Notify(EditorChangeKind.Selection);')
save(p, s)

p = 'src/ArtSpace.Editor/DesignSurface.cs'
s = load(p, '0c9c25407199fbf48905c9bd7858a682c2956ccf')
s = replace(s, 'Gradient, AnchorMarquee }', 'Gradient, AnchorMarquee, PendingTransform, PendingVertex }')
s = replace(s, '    private readonly Dictionary<string, DesignNode> _originals = [];', '''    private readonly record struct TransformSnapshot(double X, double Y, double Width, double Height, double Rotation, Matrix2D LocalMatrix);
    private readonly Dictionary<string, TransformSnapshot> _originals = [];
    private Gesture _pendingTransform;
    private bool _pendingDuplicate;
    public long SnapIndexBuilds { get; private set; }''')
s = replace(s, 'private int _resizeHandle, _vertexIndex;', 'private int _resizeHandle;')
s = replace(s, '        var editor = Session!; var roots = editor.SelectionRoots;', '        SnapIndexBuilds++;\n        var editor = Session!; var roots = editor.SelectionRoots;')
s = replace(s, '        if (e.Kind == EditorChangeKind.Tool && _penNode is not null) FinishPath(false);', '''        if (e.Kind == EditorChangeKind.Tool && _gesture is Gesture.PendingTransform or Gesture.PendingVertex) _gesture = Gesture.None;
        if (e.Kind == EditorChangeKind.Tool && _penNode is not null) FinishPath(false);''')
s = replace(s, '''                editor.BeginInteraction(i == 8 ? "Rotate layers" : "Resize layers"); CaptureOriginals(); _resizeHandle = i;
                _gesture = i == 8 ? Gesture.Rotate : Gesture.Resize;
                if (editor.SelectionRoots.Count == 1) _resizeMatrix = editor.SelectionRoots[0].WorldMatrix;''', '''                _resizeHandle = i; _pendingTransform = i == 8 ? Gesture.Rotate : Gesture.Resize;
                _pendingDuplicate = false; _gesture = Gesture.PendingTransform;''')
s = replace(s, '''            editor.BeginInteraction(alt ? "Duplicate layers" : "Move layers");
            if (alt) editor.DuplicateInTransaction(editor.SelectionRoots);
            CaptureOriginals(); _gesture = Gesture.Move;''', '''            _pendingDuplicate = alt; _pendingTransform = Gesture.Move; _gesture = Gesture.PendingTransform;''')
s = replace(s, 'foreach (var node in Session.SelectionRoots) _originals[node.Id] = DocumentJson.CloneNode(node);', 'foreach (var node in Session.SelectionRoots) _originals[node.Id] = new(node.X, node.Y, node.Width, node.Height, node.Rotation, node.LocalMatrix);')
s = replace(s, '        _snapIndex = BuildSnapIndex();\n    }', '        _snapIndex = null;\n    }')
s = replace(s, '        var shift = e.KeyModifiers.HasFlag(VirtualKeyModifiers.Shift);\n        switch (_gesture)', '''        if (_gesture == Gesture.PendingTransform)
        {
            if (screen.DistanceTo(_startScreen) < 3) return;
            if (editor.SelectionRoots.Count == 0 || editor.SelectionRoots.Any(n => n.IsEffectivelyLocked)) { _gesture = Gesture.None; return; }
            try
            {
                editor.BeginInteraction(_pendingTransform == Gesture.Move ? (_pendingDuplicate ? "Duplicate layers" : "Move layers") : _pendingTransform == Gesture.Resize ? "Resize layers" : "Rotate layers");
                if (_pendingDuplicate) editor.DuplicateInTransaction(editor.SelectionRoots);
                CaptureOriginals();
                if (_pendingTransform == Gesture.Resize && editor.SelectionRoots.Count == 1) _resizeMatrix = editor.SelectionRoots[0].WorldMatrix;
                _gesture = _pendingTransform;
            }
            catch (Exception ex) { CancelGesture(); StatusChanged?.Invoke(ex.Message); return; }
        }
        var shift = e.KeyModifiers.HasFlag(VirtualKeyModifiers.Shift);
        switch (_gesture)''')
s = replace(s, '            case Gesture.Vertex:\n                MovePathAnchor', '            case Gesture.PendingVertex:\n            case Gesture.Vertex:\n                MovePathAnchor')
s = replace(s, '        _touches.Remove(e.Pointer.PointerId);\n        var gesture', '''        if (_gesture is Gesture.PendingTransform or Gesture.Move or Gesture.Resize or Gesture.Rotate or Gesture.PendingVertex or Gesture.Vertex) Moved(sender, e);
        _touches.Remove(e.Pointer.PointerId);
        var gesture''')
s = replace(s, 'and not Gesture.AnchorMarquee and not Gesture.PenControl) editor.CommitInteraction();', 'and not Gesture.AnchorMarquee and not Gesture.PenControl and not Gesture.PendingTransform and not Gesture.PendingVertex) editor.CommitInteraction();')
save(p, s)

p = 'src/ArtSpace.Editor/DesignSurface.Paths.cs'
s = load(p, '5b4691e8bf8b5fcbaeaddd4868891e92bfec5629')
s = replace(s, '    private string? _pathSignature;', '    private string? _pathSignature;\n    private SKPath? _pathGeometry;\n    public long EditablePathBuilds { get; private set; }')
s = replace(s, '''        var signature = VectorPath.Build(node) + $"|{node.Width:R}|{node.Height:R}|{node.PathWidth:R}|{node.PathHeight:R}|{node.FillRule}";
        if (ReferenceEquals(_pathNode, node) && _pathSignature == signature && _editablePath is not null) return true;
        var geometry = PathEditing.Read(node, Renderer);''', '''        var source = Renderer.Geometry(node);
        if (ReferenceEquals(_pathNode, node) && ReferenceEquals(_pathGeometry, source) && _pathSignature is not null && _editablePath is not null) return true;
        var geometry = PathEditing.Read(source);
        EditablePathBuilds++;
        _pathGeometry = source;''')
s = replace(s, '_pathNode = node; _editablePath = geometry; _pathSignature = signature;', '_pathNode = node; _editablePath = geometry; _pathSignature = "";')
s = block(s, '    private void BeginPathDrag(', '    private void MovePathAnchor(', '''    private void BeginPathDrag(DesignNode node, Vec2 world, Address address, int handle)
    {
        _pathDragBasis = null; _pathDragOriginal = null; _pathDragWorld = node.WorldMatrix;
        _pathDragStart = _pathDragWorld.Inverse.Map(world); _pathDragMoved = false;
        _pathDragAnchor = address; _pathDragHandle = handle; _gesture = Gesture.PendingVertex;
    }

''')
s = replace(s, '        if (Session is not { } editor || _pathNode is not { } node || _pathDragOriginal is not { } original || _pathDragBasis is null) return;', '''        if (Session is not { } editor || _pathNode is not { } node) return;
        if (_gesture == Gesture.PendingVertex)
        {
            if (screenDelta(world).DistanceTo(Vec2.Zero) < 3) return;
            try
            {
                editor.BeginInteraction(_pathDragHandle == 0 ? "Move path anchors" : "Move direction handle");
                _pathDragBasis = DocumentJson.CloneNode(node); _pathDragOriginal = _editablePath!.Clone(); _gesture = Gesture.Vertex;
            }
            catch (Exception ex) { CancelGesture(); StatusChanged?.Invoke(ex.Message); return; }
        }
        if (_pathDragOriginal is not { } original || _pathDragBasis is null) return;''')
save(p, s)

p = 'src/ArtSpace.Controls/ColorField.cs'
s = load(p, 'd311d409cd939ea39d270a2ea660a40f8c8f3452')
s = block(s, 'public sealed class ColorField : UserControl', '/// <summary>Native Skia HSV picker;', '''public sealed class ColorField : UserControl
{
    private readonly TextBox _text;
    private readonly StudioButton _swatch;
    private string _value = "";
    private bool _writing, _dirty;
    private Flyout? _flyout;
    private long _editGeneration;
    public event Action<string>? ColorCommitted;
    public string Value
    {
        get => _value;
        set
        {
            var changed = _value != value;
            _value = value;
            _writing = true;
            try
            {
                var text = value.TrimStart('#').ToUpperInvariant();
                if (_text.Text != text) _text.Text = text;
                _dirty = false;
            }
            finally { _writing = false; }
            if (changed) { _swatch.RestBackground = value; _swatch.Background = Studio.Brush(value); }
        }
    }
    public ColorField(string color, Action<string> commit)
    {
        _text = Studio.Input(color.TrimStart('#'), "Hex color");
        _swatch = new StudioButton { Width = 24, Height = 24, Padding = new(0), CornerRadius = new(4), BorderThickness = new(1), BorderBrush = Studio.Brush("#22000000") };
        AutomationProperties.SetName(_swatch, "Choose color");
        Content = Studio.Columns((_swatch, 24), (_text, -1)); Value = color; ColorCommitted += commit;
        _text.TextChanged += (_, _) => { if (!_writing) _dirty = true; };
        _text.LostFocus += (_, _) => CommitText();
        _text.KeyDown += (_, e) =>
        {
            if (e.Key == VirtualKey.Enter) { CommitText(); e.Handled = true; }
            else if (e.Key == VirtualKey.Escape) { CancelEdit(); e.Handled = true; }
        };
        _swatch.Click += (_, _) => OpenPicker();
    }
    public void UpdateFromModel(string value, bool retarget = false)
    {
        if (retarget) CancelEdit();
        else if (_dirty && _text.FocusState != FocusState.Unfocused) return;
        Value = value;
    }
    public void CancelEdit()
    {
        _editGeneration++;
        _flyout?.Hide(); _flyout = null;
        if (_dirty) Value = _value;
    }
    private void OpenPicker()
    {
        CancelEdit();
        var generation = _editGeneration;
        var spectrum = new ColorSpectrum { Width = 248, Height = 166 }; spectrum.SetColor(Value);
        var flyout = new Flyout(); _flyout = flyout;
        var root = new StackPanel { Spacing = 12 };
        root.Children.Add(Studio.Text("Custom color", 12, Studio.Ink, true)); root.Children.Add(spectrum);
        var palette = new Grid { ColumnSpacing = 6 };
        string[] colors = ["#FFFFFF", "#1E1E1E", "#477BDA", "#7B61FF", "#F24822", "#FFCD29", "#14AE5C", "#FFA6D5"];
        for (var i = 0; i < colors.Length; i++)
        {
            var color = colors[i];
            var button = new StudioButton("", () => { if (generation == _editGeneration) Set(color); flyout.Hide(); })
                { Width = 25, Height = 25, RestBackground = color, Background = Studio.Brush(color), BorderThickness = new(1), BorderBrush = Studio.Brush("#22000000") };
            AutomationProperties.SetName(button, color); palette.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); Grid.SetColumn(button, i); palette.Children.Add(button);
        }
        root.Children.Add(palette);
        spectrum.ColorCommitted += color => { if (generation == _editGeneration) Set(color); };
        flyout.Content = root; flyout.ShowAt(_swatch);
    }
    private void Set(string color) { var changed = _value != color; Value = color; if (changed) ColorCommitted?.Invoke(color); }
    private void CommitText()
    {
        if (!_dirty) return;
        var candidate = "#" + _text.Text.Trim().TrimStart('#');
        if (candidate.Length is 7 or 9 && SKColor.TryParse(candidate, out _)) Set(candidate.ToUpperInvariant()); else Value = _value;
    }
}

''')
save(p, s)

p = 'src/ArtSpace.Controls/PanelDock.cs'
s = load(p, '3b9d709ceda1155a559c9969ecb489abb54fe247')
s = replace(s, '        if (index < 0) return;', '        if (index < 0 || SelectedName == name) return;')
s = replace(s, 'SelectedName = name; _body.Content = null; _body.Content = _panels[index].Content;', 'SelectedName = name; _body.Content = _panels[index].Content;')
save(p, s)
p = 'src/ArtSpace.Controls/StudioButton.cs'
s = load(p, 'f8527ccf38f5275a6ec8cad6afffbebe9c108464')
s = replace(s, 'set { _selected = value; Refresh(); }', 'set { if (_selected == value) return; _selected = value; Refresh(); }')
s = replace(s, 'set { _primary = value; Refresh(); }', 'set { if (_primary == value) return; _primary = value; Refresh(); }')
save(p, s)
p = 'src/ArtSpace.Controls/IconView.cs'
s = load(p, 'c17f57b059b3ad916d1f3fa4ad13977cf9c65f75')
s = replace(s, 'set => SetValue(GlyphProperty, value);', 'set { if (Glyph != value) SetValue(GlyphProperty, value); }')
s = replace(s, 'set => SetValue(ColorProperty, value);', 'set { if (Color != value) SetValue(ColorProperty, value); }')
s = block(s, '    public IconView() {', '    public static readonly IReadOnlyDictionary<string, string> Paths', '''    private SKPath? _path;
    private SKPaint? _paint;
    private string? _pathData, _paintColor;
    public IconView()
    {
        Width = Height = 18; IsHitTestVisible = false;
        Loaded += (_, _) => Invalidate();
        Unloaded += (_, _) => { _path?.Dispose(); _path = null; _paint?.Dispose(); _paint = null; _pathData = _paintColor = null; };
    }
    private static void Changed(DependencyObject sender, DependencyPropertyChangedEventArgs args) => ((IconView)sender).Invalidate();
    protected override void RenderOverride(SKCanvas canvas, Size area)
    {
        var data = Paths.GetValueOrDefault(Glyph) ?? Paths["rectangle"];
        if (_path is null || _pathData != data) { _path?.Dispose(); _path = SKPath.ParseSvgPathData(data); _pathData = data; }
        _paint ??= new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.6f, StrokeJoin = SKStrokeJoin.Round, StrokeCap = SKStrokeCap.Round };
        if (_paintColor != Color) { _paint.Color = SKColor.TryParse(Color, out var color) ? color : SKColors.Black; _paintColor = Color; }
        canvas.Save();
        try { canvas.Scale((float)(area.Width / 24), (float)(area.Height / 24)); canvas.DrawPath(_path, _paint); }
        finally { canvas.Restore(); }
    }
''')
save(p, s)
p = 'src/ArtSpace.Controls/NumericField.cs'
s = load(p)
s = replace(s, 'if (_scrubbing) { _scrubbing = false; _value = _startValue; _prefix.ReleasePointerCaptures(); }', 'if (_scrubbing) { _scrubbing = false; _value = _startValue; _prefix.ReleasePointerCaptures(); Display(); }')
save(p, s)
p = 'src/ArtSpace.Controls/RetainedInspector.cs'
s = load(p)
s = replace(s, '        foreach (var field in _fields)\n        {', '        foreach (var field in _fields)\n        {\n            if (!field.Control.IsLoaded) continue;')
save(p, s)

p = 'src/ArtSpace.App/Platforms/WebAssembly/BrowserWorkspaceStorage.cs'
s = load(p, 'e2d279b80fe2c99d867d5b14ca99db492eda9ad9')
s = replace(s, '            var primary = session.Primary;', '            if (workbench.IsDisposed) return;\n            var primary = session.Primary;')
s = replace(s, '                json.WriteStartArray("anchors");', '''                json.WriteString("id", primary?.Id);
                json.WriteString("inspectorTarget", workbench.InspectorTargetId);
                json.WriteString("inspectorName", workbench.InspectorTargetName);
                json.WriteNumber("inspectorSelection", workbench.InspectorSelectionCount);
                json.WriteString("activePanel", workbench.ActivePanel);
                json.WriteBoolean("uiPending", workbench.UiPending);
                json.WriteNumber("uiFlushes", workbench.UiFlushes);
                json.WriteNumber("uiFailures", workbench.UiRefreshFailures);
                json.WriteNumber("uiMs", workbench.LastUiRefreshMs);
                json.WriteNumber("inspectorBuilds", workbench.InspectorBuilds);
                json.WriteNumber("inspectorRefreshes", workbench.InspectorRefreshes);
                json.WriteNumber("layerPasses", workbench.LayerPasses);
                json.WriteNumber("layerEntryBuilds", workbench.LayerEntryBuilds);
                json.WriteNumber("layerSelectionChanges", workbench.LayerSelectionChanges);
                json.WriteNumber("layerResets", workbench.LayerCollectionResets);
                json.WriteNumber("artboardRefreshes", workbench.ArtboardRefreshes);
                json.WriteNumber("historyRefreshes", workbench.HistoryRefreshes);
                json.WriteNumber("selectionIndexBuilds", session.SelectionIndexBuilds);
                json.WriteNumber("snapshots", session.SnapshotCaptures);
                json.WriteNumber("snapIndexBuilds", workbench.Surface.SnapIndexBuilds);
                json.WriteNumber("editablePathBuilds", workbench.Surface.EditablePathBuilds);
                json.WriteStartArray("selectedLayerIds");
                foreach (var id in workbench.SelectedLayerIds) json.WriteStringValue(id);
                json.WriteEndArray();
                json.WriteStartArray("inspectorFields");
                foreach (var field in workbench.InspectorFields)
                {
                    json.WriteStartObject(); json.WriteString("section", field.Section); json.WriteString("label", field.Label); json.WriteString("value", field.Value);
                    json.WriteNumber("x", field.X); json.WriteNumber("y", field.Y); json.WriteNumber("width", field.Width); json.WriteNumber("height", field.Height); json.WriteEndObject();
                }
                json.WriteEndArray();
                json.WriteStartArray("anchors");''')
s = replace(s, '        session.Changed += (_, _) => Publish(); workbench.Surface.SizeChanged += (_, _) => Publish(); workbench.Surface.PresentationChanged += _ => Publish(); Publish();', '''        var queued = false;
        void QueuePublish()
        {
            if (queued || workbench.IsDisposed) return;
            queued = true;
            if (!workbench.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => { queued = false; Publish(); })) queued = false;
        }
        session.Changed += (_, _) => QueuePublish();
        workbench.UiRefreshed += QueuePublish;
        workbench.LayoutUpdated += (_, _) => QueuePublish();
        workbench.Surface.SizeChanged += (_, _) => QueuePublish();
        workbench.Surface.PresentationChanged += _ => QueuePublish();
        QueuePublish();''')
save(p, s)

p = 'tests/ArtSpace.Tests/Program.cs'
s = load(p)
s = replace(s, 'Test("affine composition and inversion",', 'SelectionPerformanceTests.Register(Test);\n\nTest("affine composition and inversion",')
save(p, s)
p = 'Directory.Build.props'
s = load(p, '6ef9500f54266baf8882a47a7fd8bb928f1d2e79')
save(p, replace(s, '<Version>0.4.0-alpha.1</Version>', '<Version>0.4.1-alpha.1</Version>'))
for p in ['.github/workflows/build.yml', '.github/workflows/pages.yml']:
    s = load(p)
    s = replace(s, '            artifacts/browser-results.xml', '            artifacts/browser-results.xml\n            artifacts/ui-performance')
    save(p, s)
p = 'README.md'
s = load(p)
s = s.replace('0.4.0-alpha.1', '0.4.1-alpha.1')
s += '\n## Responsive selection and panels\n\nSelection and property-editing performance is described in [UI performance](docs/ui-performance.md), including retained controls, change routing, lazy drag transactions and reproducible browser measurements.\n'
save(p, s)
p = 'CHANGELOG.md'
s = load(p)
heading, rest = s.split('\n', 1)
save(p, heading + '\n\n## 0.4.1-alpha.1 — selection and panel responsiveness\n\n- Retain inspector sections and update values rather than recreating controls on every selection and edit.\n- Coalesce UI refreshes and defer hidden Layers, Artboards and History panels; preserve section expansion and input state.\n- Reconcile layer rows incrementally and batch large collection changes; reuse row visuals and icon geometry.\n- Start transform and anchor transactions only after a three-pixel drag threshold; avoid whole-document snapshots and snap-index builds for selection clicks.\n- Resolve selections through a revision-invalidated ID/order index, preserve scene ordering, and suppress no-op selection notifications.\n- Add UI input-ownership guards, arbitrary opacity synchronization, browser control-value regressions and selection-latency reports.\n\n' + rest)
print('Applied guarded UI responsiveness integration.')
