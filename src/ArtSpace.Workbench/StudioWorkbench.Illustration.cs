using ArtSpace.Illustration;
using ArtSpace.Skia;

namespace ArtSpace.Workbench;

public sealed partial class StudioWorkbench
{
    private readonly PanelDock _illustrationDock = new();
    private readonly StackPanel _artboards = new() { Spacing = 3, Margin = new(10) };
    private readonly StackPanel _historyPanel = new() { Spacing = 2, Margin = new(10) };
    private readonly StackPanel _controlBar = new() { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new(12, 3, 8, 3) };
    private readonly TextBlock _selectionLabel = Studio.Text("No Selection", 11);
    private double _dockWidth = 314;
    private bool _illustrationReady;
    private ColorField? _fillControl, _strokeControl;
    private NumericField? _strokeWidthControl;
    private ComboBox? _opacityControl;
    private bool _syncingAppearance;

    private static void Detach(FrameworkElement element)
    {
        switch (element.Parent)
        {
            case Panel panel: panel.Children.Remove(element); break;
            case Border border: border.Child = null; break;
            case ContentControl content: content.Content = null; break;
        }
    }

    private void ConfigureIllustrationWorkspace()
    {
        foreach (var element in new FrameworkElement[] { Surface, _palette, _toastBorder, _title, _zoom, _status, _leftContent, _inspector, _pages, _search }) Detach(element);
        _root.Children.Clear(); _root.RowDefinitions.Clear();
        foreach (var height in new[] { 30d, 38d, 28d, -1d, 24d })
            _root.RowDefinitions.Add(new() { Height = height < 0 ? new(1, GridUnitType.Star) : new(height) });
        _root.Background = Studio.Brush("#292929");
        _leftColumn.Width = new(64); _rightColumn.Width = new(_dockWidth);
        _leftPanel.Margin = _rightPanel.Margin = new(0);
        _leftPanel.CornerRadius = _rightPanel.CornerRadius = new(0);
        _leftPanel.BorderThickness = new(0, 0, 1, 0); _rightPanel.BorderThickness = new(1, 0, 0, 0);
        _leftPanel.Background = Studio.Brush("#343434"); _rightPanel.Background = Studio.Brush("#383838");

        var menu = _applicationMenu = new CommandMenuBar();
        foreach (var name in new[] { "File", "Edit", "Object", "Type", "Select", "Effect", "View", "Window", "Help" })
            menu.Add(name, () => IllustrationMenu(name));
        var mark = Studio.Text("As", 17, "#FFAF4A", true); mark.Margin = new(12, 0, 8, 0);
        var menuRow = Studio.Columns((mark, 40), (menu, -1), (Studio.Text("ArtSpace", 10, Studio.Muted), 68), (new StudioButton("Essentials Classic", () => { _uiVisible = true; ResizeIllustrationWorkspace(); _illustrationDock.Select("Properties"); }) { FontSize = 10, Padding = new(5, 2) }, 112));
        menuRow.ColumnSpacing = 2; Put(menuRow, 0, 0, 3);
        BuildControlBar();
        var controlScroll = new ScrollViewer { Content = _controlBar, HorizontalScrollMode = ScrollMode.Enabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollMode = ScrollMode.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Put(new Border { Background = Studio.Brush("#3B3B3B"), BorderBrush = Studio.Brush("#212121"), BorderThickness = new(0, 1, 0, 1), Child = controlScroll }, 1, 0, 3);

        _title.HorizontalContentAlignment = HorizontalAlignment.Left; _title.Padding = new(13, 3); _title.Height = 28;
        _title.CornerRadius = new(0); _title.RestBackground = "#454545"; _title.Background = Studio.Brush("#454545");
        var documentRow = Studio.Columns((_title, -1), (Studio.Text("RGB / Preview", 10, Studio.Muted), 104));
        documentRow.MaxWidth = 500; documentRow.HorizontalAlignment = HorizontalAlignment.Left;
        Put(documentRow, 2, 1);
        Put(new StudioButton("Tools", () => RunAsync(ShowHelpAsync)) { FontSize = 9, Padding = new(2), Height = 28 }, 2, 0);
        Put(new StudioButton("Panels", () => _illustrationDock.Select("Properties")) { FontSize = 10, Padding = new(6), Height = 28, HorizontalContentAlignment = HorizontalAlignment.Left }, 2, 2);
        var canvas = new Grid(); canvas.Children.Add(Surface); canvas.Children.Add(_toastBorder); Put(canvas, 3, 1);

        _palette.Child = BuildIllustrationTools(); _palette.Margin = new(0); _palette.CornerRadius = new(0);
        _palette.HorizontalAlignment = HorizontalAlignment.Stretch; _palette.VerticalAlignment = VerticalAlignment.Top;
        _palette.BorderThickness = new(0); _palette.Background = Studio.Brush("#343434");
        _leftPanel.Child = Studio.Scroll(_palette); Put(_leftPanel, 3, 0);

        var layers = new Grid(); layers.RowDefinitions.Add(new() { Height = GridLength.Auto }); layers.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        _search.PlaceholderText = "Filter layers"; _search.Visibility = Visibility.Visible; _search.Margin = new(8); layers.Children.Add(_search);
        Grid.SetRow(_leftContent, 1); layers.Children.Add(_leftContent);
        _illustrationDock.Add("Properties", Studio.Scroll(_inspector));
        _illustrationDock.Add("Layers", layers);
        _illustrationDock.Add("Artboards", Studio.Scroll(_artboards));
        _illustrationDock.Add("History", Studio.Scroll(_historyPanel));
        ConfigureAppearancePanels();
        var dockRoot = new Grid(); dockRoot.Children.Add(_illustrationDock);
        var grip = new DockResizeGrip(); grip.DragDelta += delta => { _dockWidth = Math.Clamp(_dockWidth - delta, 268, 510); ResizeIllustrationWorkspace(); };
        dockRoot.Children.Add(grip); _rightPanel.Child = dockRoot; Put(_rightPanel, 3, 2);

        _zoom.Height = 24; _zoom.Padding = new(6, 2); _zoom.FontSize = 10;
        var footer = Studio.Columns((_zoom, 76), (Studio.Text("px", 10, Studio.Muted), 22), (new IconButton("frame", "Fit artboard (Ctrl 0)", () => Surface.Fit(firstFrame: true)) { Width = 24, Height = 24, Padding = new(5) }, 24), (_status, -1), (Studio.Text("Uno · Skia", 10, Studio.Muted), 72));
        footer.Margin = new(5, 0, 8, 0); Put(footer, 4, 0, 3);
        Session.RulersVisible = true;
        _illustrationReady = true;
        _illustrationDock.SelectionChanged += name =>
        {
            if (name != "Properties") _inspectorView?.SuspendEditing();
            SuspendAppearanceEditing();
            RequestUi(name switch
            {
                "Properties" => UiDirty.Inspector,
                "Layers" => UiDirty.Layers | UiDirty.LayerSelection | UiDirty.Assets,
                "Artboards" => UiDirty.Artboards,
                "History" => UiDirty.History,
                "Appearance" => UiDirty.Appearance,
                "Graphic Styles" => UiDirty.GraphicStyles,
                _ => UiDirty.None
            });
        };
        SizeChanged += (_, _) => ResizeIllustrationWorkspace();
        RefreshIllustrationPanels(); ResizeIllustrationWorkspace();

        void Put(FrameworkElement element, int row, int column, int span = 1)
        {
            Grid.SetRow(element, row); Grid.SetColumn(element, column); Grid.SetColumnSpan(element, span); _root.Children.Add(element);
        }
    }

    private void ResizeIllustrationWorkspace()
    {
        _leftColumn.Width = new(_uiVisible ? 64 : 0);
        var showRight = _uiVisible && (ActualWidth == 0 || ActualWidth >= 760);
        _rightColumn.Width = new(showRight ? _dockWidth : 0);
        _leftPanel.Visibility = _palette.Visibility = _uiVisible ? Visibility.Visible : Visibility.Collapsed;
        var wasVisible = _rightPanel.Visibility == Visibility.Visible;
        _rightPanel.Visibility = showRight ? Visibility.Visible : Visibility.Collapsed;
        if (showRight && !wasVisible) RequestUi(UiDirty.All);
        else if (!showRight) { _inspectorView?.SuspendEditing(); SuspendAppearanceEditing(); }
    }

    private UIElement BuildIllustrationTools()
    {
        _toolButtons.Clear();
        var root = new StackPanel { Margin = new(3, 4, 3, 8), Spacing = 6 };
        var tools = new (EditorTool Tool, string Glyph, string Label)[]
        {
            (EditorTool.Move,"move","Selection (V)"), (EditorTool.DirectSelect,"directselect","Direct Selection (A)"),
            (EditorTool.Pen,"pen","Pen (P)"), (EditorTool.Pencil,"pencil","Pencil (N)"),
            (EditorTool.AddAnchor,"plus","Add Anchor Point (+)"), (EditorTool.DeleteAnchor,"minus","Remove Anchor Point (-)"),
            (EditorTool.Text,"text","Type (T)"), (EditorTool.Line,"line","Line Segment (\\)"),
            (EditorTool.Rectangle,"rectangle","Rectangle (M)"), (EditorTool.Ellipse,"ellipse","Ellipse (L)"),
            (EditorTool.Polygon,"polygon","Polygon"), (EditorTool.Star,"star","Star"),
            (EditorTool.Brush,"brush","Paintbrush (B)"), (EditorTool.Scale,"scale","Scale (S)"),
            (EditorTool.Gradient,"gradient","Gradient (G)"), (EditorTool.Eyedropper,"eyedropper","Eyedropper (I)"),
            (EditorTool.Frame,"frame","Artboard (Shift O)"), (EditorTool.Slice,"slice","Slice"),
            (EditorTool.Hand,"hand","Hand (H / Space)"), (EditorTool.Zoom,"search","Zoom (Z)")
        };
        var grid = new Grid { RowSpacing = 2, ColumnSpacing = 1 };
        grid.ColumnDefinitions.Add(new() { Width = new(28) }); grid.ColumnDefinitions.Add(new() { Width = new(28) });
        for (var i = 0; i < tools.Length; i++)
        {
            var item = tools[i]; if (i % 2 == 0) grid.RowDefinitions.Add(new() { Height = new(29) });
            var button = new IconButton(item.Glyph, item.Label, () => { Surface.FinishTextEdit(true); Surface.FinishPath(false); Session.Tool = item.Tool; Surface.FocusCanvas(); }) { Width = 28, Height = 29, Padding = new(5), CornerRadius = new(1) };
            Grid.SetRow(button, i / 2); Grid.SetColumn(button, i % 2); grid.Children.Add(button); _toolButtons[item.Tool] = button;
        }
        root.Children.Add(grid); root.Children.Add(Studio.Rule());
        var fillStroke = new Grid { Height = 53 };
        var stroke = new StudioButton("", () => ChangeAppearance("Swap fill and stroke", n => { var color = n.Fill; n.Fill = n.Strokes.FirstOrDefault()?.Color ?? "#161616"; n.Strokes = [new() { Color = color, Width = Math.Max(1, Surface.StrokeWidth) }]; })) { Width = 31, Height = 31, Margin = new(18, 14, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, BorderThickness = new(5), BorderBrush = Studio.Brush("#151515"), RestBackground = "#EEEEEE", Background = Studio.Brush("#EEEEEE"), CornerRadius = new(0) };
        AutomationProperties.SetName(stroke, "Swap fill and stroke");
        var fill = new StudioButton("", () => _illustrationDock.Select("Properties")) { Width = 31, Height = 31, Margin = new(4, 1, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, BorderThickness = new(1), BorderBrush = Studio.Brush("#111111"), RestBackground = "#F0B562", Background = Studio.Brush("#F0B562"), CornerRadius = new(0) };
        AutomationProperties.SetName(fill, "Fill and appearance properties"); fillStroke.Children.Add(stroke); fillStroke.Children.Add(fill); root.Children.Add(fillStroke);
        root.Children.Add(new StudioButton("None", () => ChangeAppearance("Remove fill", n => n.Fills.Clear())) { FontSize = 9, Height = 22, Padding = new(0) });
        root.Children.Add(Studio.Rule());
        root.Children.Add(new IconButton("more", "Quick actions (Ctrl K)", () => RunAsync(ShowQuickActionsAsync)) { HorizontalAlignment = HorizontalAlignment.Center });
        return root;
    }

    private void BuildControlBar()
    {
        _selectionLabel.Width = 95; _controlBar.Children.Add(_selectionLabel);
        _controlBar.Children.Add(Studio.Text("Fill", 10, Studio.Muted));
        _controlBar.Children.Add(_fillControl = new ColorField(Surface.FillColor, color => { Surface.FillColor = color; ChangeAppearance("Fill color", n => n.Fill = color); }) { Width = 108 });
        _controlBar.Children.Add(Studio.Text("Stroke", 10, Studio.Muted));
        _controlBar.Children.Add(_strokeControl = new ColorField(Surface.StrokeColor, color => { Surface.StrokeColor = color; ChangeAppearance("Stroke color", n => { if (n.Strokes.Count == 0) n.Strokes.Add(new()); n.Strokes[0].Color = color; n.Strokes[0].Paint = null; }); }) { Width = 108 });
        _controlBar.Children.Add(_strokeWidthControl = Number("pt", Surface.StrokeWidth, width => { Surface.StrokeWidth = Math.Clamp(width, 0, 1000); ChangeAppearance("Stroke width", n => { if (n.Strokes.Count == 0) n.Strokes.Add(new()); n.Strokes[0].Width = Surface.StrokeWidth; }); }, 0, 1000));
        _controlBar.Children.Add(Studio.Text("Opacity", 10, Studio.Muted));
        var opacity = Studio.Choice(new[] { "100%", "75%", "50%", "25%", "10%" }, "100%", value => { if (!_syncingAppearance) ChangeAppearance("Opacity", n => n.Opacity = double.Parse(value.TrimEnd('%'), System.Globalization.CultureInfo.InvariantCulture) / 100); }, "Object opacity"); opacity.Width = 78; _opacityControl = opacity; _controlBar.Children.Add(opacity);
        foreach (var direction in new[] { "left", "center", "right", "top", "middle", "bottom" })
            _controlBar.Children.Add(new IconButton(direction, "Align " + direction, () => Run(() => Session.Align(direction))) { Width = 27, Height = 29, Padding = new(5) });
    }

    private void RefreshIllustrationPanels() => RequestUi(UiDirty.ControlBar | UiDirty.Artboards | UiDirty.History);

    private void AddArtboard()
    {
        var right = Session.Page.Nodes.Count == 0 ? 0 : Session.Page.Nodes.Max(n => n.WorldBounds.Right) + 80;
        var board = new DesignNode { Kind = NodeKind.Frame, Name = "Artboard " + (Session.Page.Nodes.Count(n => n.IsFrame) + 1), X = right, Width = 640, Height = 800, Fill = "#FFFFFF", ClipContent = true };
        Session.Edit("Add artboard", () => { Session.AddNode(board); Session.Select(board); }); Surface.Fit(true);
    }

    private IEnumerable<MenuCommand> IllustrationMenu(string menu)
    {
        var selected = Session.Selection.Count > 0;
        MenuCommand Item(string label, Action action, string shortcut = "", bool enabled = true) => new(label, () => Run(action), shortcut, enabled);
        MenuCommand Async(string label, Func<Task> action, string shortcut = "") => new(label, () => RunAsync(action), shortcut);
        var separator = new MenuCommand("");
        switch (menu)
        {
            case "File":
                yield return Async("New…", NewDocumentAsync, "Ctrl N"); yield return Async("Open…", OpenAsync, "Ctrl O");
                yield return Async("Save a Copy…", SaveAsync, "Ctrl S"); yield return separator;
                yield return Async("Export SVG…", () => ExportAsync(true)); yield return Async("Export PNG…", () => ExportAsync(false));
                yield return Async("Export Editable SVG…", () => ExportAsync(true, true));
                yield return Item("New Artboard", AddArtboard, "Shift O");
                yield return Async("Open Alpine sample", async () => { if (Session.IsDirty && !await ConfirmAsync("Replace current artwork?", "Save a local copy first to keep the current document.")) return; Session.Load(IllustrationSample.Create()); Surface.Fit(firstFrame: true); });
                break;
            case "Edit":
                yield return Item("Undo " + Session.UndoLabel, Session.Undo, "Ctrl Z", Session.CanUndo);
                yield return Item("Redo " + Session.RedoLabel, Session.Redo, "Ctrl Shift Z", Session.CanRedo); yield return separator;
                yield return Async("Cut", () => CopyAsync(true), "Ctrl X"); yield return Async("Copy", () => CopyAsync(false), "Ctrl C"); yield return Async("Paste", PasteAsync, "Ctrl V");
                yield return Item("Duplicate", () => Session.DuplicateSelection(), "Ctrl D", selected);
                yield return Item("Delete", Session.DeleteSelection, "Delete", selected); break;
            case "Object":
                yield return Item("Group", () => Session.GroupSelection(), "Ctrl G", selected); yield return Item("Ungroup", Session.UngroupSelection, "Ctrl Shift G", selected);
                yield return Item("Bring to Front", () => Session.Reorder(1, true), "Ctrl Shift ]", selected); yield return Item("Send to Back", () => Session.Reorder(-1, true), "Ctrl Shift [", selected); yield return separator;
                yield return Item("Expand Shape", () => IllustrationOperations.ExpandShapes(Session, Surface.Renderer), enabled: selected);
                yield return Item("Outline Stroke", () => IllustrationOperations.OutlineStrokes(Session, Surface.Renderer), enabled: selected);
                yield return Async("Offset Path…", () => NumberOperation("Offset path in pixels", "12", value => IllustrationOperations.OffsetPaths(Session, Surface.Renderer, value)));
                yield return Async("Blend…", () => NumberOperation("Intermediate blend steps (1–256)", "8", value => IllustrationOperations.Blend(Session, checked((int)value))));
                yield return Async("Radial Repeat…", () => NumberOperation("Radial copies (2–128)", "8", value => IllustrationOperations.RadialRepeat(Session, checked((int)value))));
                yield return separator;
                yield return Item("Edit Anchors", Surface.EnterPathEditing, "A", PathEditing.CanEdit(Session.Primary));
                yield return Item("Anchor Point Tool", () => Session.Tool = EditorTool.AnchorPoint, "Shift C");
                yield return Item("Make Compound Path", () => PathOperations.MakeCompound(Session, Surface.Renderer), enabled: Session.SelectionRoots.Count > 1);
                yield return Item("Release Compound Path", () => PathOperations.ReleaseCompound(Session, Surface.Renderer), enabled: selected);
                yield return separator;
                yield return Item("Add Anchor Points", () => PathOperations.AddAnchors(Session, Surface.Renderer), enabled: selected);
                yield return Item("Smooth Anchors", () => SmoothPathAnchors(true), enabled: selected);
                yield return Item("Corner Anchors", () => SmoothPathAnchors(false), enabled: selected);
                yield return Item("Reverse Path Direction", () => PathOperations.Reverse(Session, Surface.Renderer), enabled: selected);
                yield return Item("Make Opacity Mask", () => OpacityMaskOperations.Make(Session), enabled: selected);
                yield return Item("Release Opacity Mask", () => OpacityMaskOperations.Release(Session), enabled: selected);
                yield return Item("Edit Opacity Mask", () => OpacityMaskOperations.EditMask(Session), enabled: selected);
                yield return Item("Edit Masked Artwork", () => OpacityMaskOperations.EditContents(Session), enabled: selected);
                yield return Item("Invert Opacity Mask", () => OpacityMaskOperations.Invert(Session), enabled: selected);
                yield return Item("Enable / Disable Opacity Mask", () => OpacityMaskOperations.ToggleEnabled(Session), enabled: selected);
                yield return Item("Make Clipping Mask", () => ClippingOperations.Make(Session), "Ctrl 7", selected);
                yield return Item("Release Clipping Mask", () => ClippingOperations.Release(Session), "Ctrl Alt 7", selected);
                yield return Item("Edit Clipping Path", () => { ClippingOperations.EditMask(Session); Surface.EnterPathEditing(); }, enabled: selected);
                yield return Item("Edit Clipped Contents", () => ClippingOperations.EditContents(Session), enabled: selected);
                yield return Item("Close Path", () => PathOperations.Close(Session, Surface.Renderer), enabled: selected);
                break;
            case "Type":
                yield return Async("Type on a Path…", CreateTypeOnPathAsync);
                yield return Item("Edit Path Baseline", Surface.EnterPathEditing, enabled: Session.Primary?.TextPath is not null);
                yield return Item("Flip Path Text", () => TypeOnPathOperations.Update(Session, "Flip path text", o => o.Flip = !o.Flip), enabled: Session.Primary?.TextPath is not null);
                yield return Item("Create Outlines", () => PathOperations.CreateOutlines(Session, Surface.Renderer), "Ctrl Shift O", Session.SelectionRoots.Any(n => n.DescendantsAndSelf().Any(c => c.Kind == NodeKind.Text && !c.IsEffectivelyLocked)));
                yield return Item("Type Tool", () => Session.Tool = EditorTool.Text, "T");
                yield return Item("Edit Text", () => { if (Session.Primary?.Kind == NodeKind.Text) Surface.BeginTextEdit(Session.Primary); }, "Enter", Session.Primary?.Kind == NodeKind.Text);
                foreach (var weight in new[] { 400, 500, 600, 700, 900 }) yield return Item("Weight " + weight, () => Session.UpdateSelection("Font weight", n => n.FontWeight = weight), enabled: selected);
                yield return Item("Align Left", () => Session.UpdateSelection("Text alignment", n => n.TextAlign = ArtSpace.Core.TextAlignment.Left), enabled: selected);
                yield return Item("Align Center", () => Session.UpdateSelection("Text alignment", n => n.TextAlign = ArtSpace.Core.TextAlignment.Center), enabled: selected);
                break;
            case "Select":
                yield return Item("All", Session.SelectAll, "Ctrl A"); yield return Item("Deselect", () => Session.Select((DesignNode?)null), "Escape");
                yield return Item("Inverse", () => Session.Select(Session.Page.AllNodes().Where(n => !n.IsEffectivelyLocked && !Session.SelectedIds.Contains(n.Id)).Select(n => n.Id).ToArray()));
                yield return Item("Same Fill Color", () => { var fill = Session.Primary?.Fill; Session.Select(Session.Page.AllNodes().Where(n => !n.IsEffectivelyLocked && n.Fill == fill).Select(n => n.Id).ToArray()); }, enabled: selected);
                yield return Item("Same Object Type", () => { var kind = Session.Primary?.Kind; Session.Select(Session.Page.AllNodes().Where(n => !n.IsEffectivelyLocked && n.Kind == kind).Select(n => n.Id).ToArray()); }, enabled: selected); break;
            case "Effect":
                foreach (var kind in Enum.GetValues<LiveEffectKind>()) yield return Item(LiveEffect.Name(kind), () => AddLiveEffect(kind), enabled: selected);
                yield return Item("Remove Shadows", () => ChangeAppearance("Remove shadows", n => n.Shadows.Clear()), enabled: selected);
                yield return Item("Linear Gradient", () => SetGradient(false), enabled: selected); yield return Item("Radial Gradient", () => SetGradient(true), enabled: selected); break;
            case "View":
                yield return Item("Retained Scene Rendering", () => { Surface.Renderer.EnableRetainedScene = !Surface.Renderer.EnableRetainedScene; Surface.Renderer.InvalidateRetainedScene(); Surface.Invalidate(); });
                yield return Item("Outline / Preview", () => { Session.OutlinesVisible = !Session.OutlinesVisible; Surface.Invalidate(); }, "Ctrl Y");
                yield return Item("Fit Artboard in Window", () => Surface.Fit(firstFrame: true), "Ctrl 0"); yield return Item("Actual Size", () => Surface.ZoomTo(1), "Ctrl 1");
                yield return Item("Fit Selection", () => Surface.Fit(true), "Shift 2"); yield return Item("Fit All Artboards", () => Surface.Fit(), "Shift 1"); yield return separator;
                yield return Item("Show / Hide Rulers", () => { Session.RulersVisible = !Session.RulersVisible; Surface.Invalidate(); }, "Ctrl R");
                yield return Item("Show / Hide Grid", () => { Session.GridVisible = !Session.GridVisible; Surface.Invalidate(); }, "Ctrl '");
                yield return Item("Smart Guides / Snapping", () => Session.SnapEnabled = !Session.SnapEnabled);
                yield return Item("Clear Guides", () => Session.Edit("Clear guides", () => Session.Page.Guides.Clear())); break;
            case "Window":
                foreach (var name in new[] { "Properties", "Layers", "Artboards", "History", "Appearance", "Graphic Styles" }) yield return Item(name, () => { _uiVisible = true; ResizeIllustrationWorkspace(); _illustrationDock.Select(name); });
                yield return Item("Symbols", () => { _assets = true; RefreshLeftContent(); _illustrationDock.Select("Layers"); });
                yield return Item("Show Layers", () => { _assets = false; RefreshLeftContent(); _illustrationDock.Select("Layers"); });
                yield return Item("Make Symbol", () => ComponentService.MakeComponent(Session), enabled: selected);
                yield return Item("Hide / Show Panels", TogglePanels, "Tab"); break;
            default:
                yield return Async("Tools, Shortcuts & Help", ShowHelpAsync, "?"); yield return Async("Find a Command…", ShowQuickActionsAsync, "Ctrl K"); break;
        }
    }

    private void SmoothPathAnchors(bool smooth)
    {
        if (!Surface.EditSelectedAnchors(smooth ? "Smooth selected anchors" : "Corner selected anchors", (path, anchors) => path.Smooth(anchors, smooth)))
            PathOperations.Smooth(Session, Surface.Renderer, smooth);
    }

    private void SetGradient(bool radial)
    {
        ChangeAppearance("Apply gradient", n => n.Fills = [new() { Kind = radial ? FillKind.RadialGradient : FillKind.LinearGradient, Start = new(0, 0), End = new(1, 1), Stops = [new() { Offset = 0, Color = n.Fill }, new() { Offset = 1, Color = "#263F53" }] }]);
    }
    private async Task NumberOperation(string title, string initial, Action<double> action)
    {
        var text = await PromptAsync(title, initial);
        if (text is null) return;
        if (!double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value))
            throw new InvalidOperationException("Enter a finite numeric value.");
        action(value);
    }
}
