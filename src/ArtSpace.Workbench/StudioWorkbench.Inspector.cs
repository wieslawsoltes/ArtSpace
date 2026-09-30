using ArtSpace.Layout;
using ArtSpace.Skia;

namespace ArtSpace.Workbench;

public sealed partial class StudioWorkbench
{
    private double _exportScale = 1;
    private RetainedInspector? _inspectorView;
    private DesignDocument? _inspectorDocument;
    private DesignNode? _inspectorPrimary;
    private readonly HashSet<string> _inspectorSelection = [];
    private DesignNode InspectedNode => Session.Primary ?? throw new InvalidOperationException("No inspected artwork.");
    public string? InspectorTargetId { get; private set; }
    public string? InspectorTargetName { get; private set; }
    public int InspectorSelectionCount { get; private set; }
    public long InspectorBuilds => _inspectorView?.SectionBuilds ?? 0;
    public long InspectorRefreshes => _inspectorView?.Refreshes ?? 0;
    public IEnumerable<InspectorFieldState> InspectorFields => _inspectorView is not null && IsPanelVisible("Properties") ? _inspectorView.DescribeFields() : [];

    private void Change(string label, Action<DesignNode> change) => Run(() => Session.UpdateSelection(label, change));
    private void Inspect(string title, object? shape, Action<StackPanel, InspectorBindings> build, string? glyph = null, Action? action = null)
        => (_activeInspector ?? _inspectorView)!.Section(title, shape, build, glyph, action);

    private void RefreshInspector()
    {
        _inspectorView ??= new(_inspector);
        var node = Session.Primary;
        var retarget = !ReferenceEquals(_inspectorDocument, Session.Document) || !ReferenceEquals(_inspectorPrimary, node)
            || !_inspectorSelection.SetEquals(Session.SelectedIds);
        _inspectorView.Begin(retarget);
        if (_prototype) BuildPrototypeInspector();
        else if (node is null)
        {
            Inspect("Document", null, (body, b) =>
            {
                body.Children.Add(Studio.Text("RGB artwork · pixels", 11));
                body.Children.Add(Wrapped("Use the tools at left to draw. Select artwork to edit appearance and transform. A edits anchors; Alt-drag breaks tangent symmetry."));
                body.Children.Add(b.Button(() => "New artboard", () => Run(AddArtboard)));
            });
            AddIllustrationSections(); AddExportSection();
        }
        else
        {
            // Prioritize the selected object's identity, geometry and paints above auxiliary tools.
            _inspectorView.Use("selection-summary", null, () =>
            {
                var b = new InspectorBindings();
                var summary = new StackPanel { Spacing = 5, Margin = new(16, 14, 16, 14) };
                var icon = new IconView { Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center };
                b.Observe(_ => { icon.Glyph = Glyph(InspectedNode.Kind); icon.Color = InspectedNode.Kind is NodeKind.Component or NodeKind.Instance ? "#9747FF" : Studio.Ink; });
                summary.Children.Add(Studio.Columns((icon, 16), (b.Text(() => Session.Selection.Count == 1 ? InspectedNode.Name : Session.Selection.Count + " layers selected", 12, Studio.Ink, true), -1), (b.Icon(() => "more", "Layer actions", () => ShowCanvasMenu(new Point(20, 20))), 24)));
                summary.Children.Add(b.Text(() => InspectedNode.Kind + (InspectedNode.Parent is null ? " · Canvas" : " · " + InspectedNode.Parent.Name), 10, Studio.Muted));
                return (summary, b);
            });
            BuildTransform(); BuildLayout(node);
            if (node.Parent is not null) BuildConstraints(node.Parent.Layout.Direction != LayoutDirection.None);
            Inspect("Appearance", node.Kind is NodeKind.Star ? "star" : node.Kind is NodeKind.Polygon ? "polygon" : "normal", (body, b) =>
            {
                body.Children.Add(Studio.Columns((b.Number("%", () => InspectedNode.Opacity * 100, v => ChangeAppearance("Opacity", n => n.Opacity = v / 100), 0, 100), -1), (b.Number("R", () => InspectedNode.CornerRadius, v => Change("Corner radius", n => n.CornerRadius = v), 0), -1)));
                body.Children.Add(b.Choice(Enum.GetNames<BlendKind>(), () => InspectedNode.Blend.ToString(), value => ChangeAppearance("Blend mode", n => n.Blend = Enum.Parse<BlendKind>(value)), "Blend mode"));
                if (InspectedNode.Kind is NodeKind.Polygon or NodeKind.Star) body.Children.Add(b.Number("N", () => InspectedNode.Sides, v => Change("Polygon sides", n => n.Sides = (int)v), 3, 128));
                if (InspectedNode.Kind == NodeKind.Star) body.Children.Add(b.Number("Star ratio", () => InspectedNode.StarRatio * 100, v => Change("Star ratio", n => n.StarRatio = v / 100), 1, 100));
            });
            if (node.Kind == NodeKind.Text) BuildTypography();
            if (node.TextPath is not null) BuildTypeOnPath();
            BuildFills(node); BuildStrokes(node); BuildEffects(node);
            AddIllustrationSections();
            if (Session.SelectionRoots.Count >= 2)
            {
                Inspect("Combine shapes", null, (body, b) =>
                {
                    var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 24 };
                    foreach (var op in Enum.GetValues<BooleanOperation>()) row.Children.Add(b.Icon(() => op.ToString().ToLowerInvariant(), op.ToString(), () => Run(() => BooleanOperations.Apply(Session, Surface.Renderer, op))));
                    body.Children.Add(row);
                    body.Children.Add(Studio.Columns((b.Button(() => "Distribute H", () => Run(() => Session.Distribute(true))), -1), (b.Button(() => "Distribute V", () => Run(() => Session.Distribute(false))), -1)));
                });
            }
            if (node.Kind is NodeKind.Instance or NodeKind.Component)
            {
                Inspect(node.Kind == NodeKind.Component ? "Component" : "Instance", null, (body, b) =>
                {
                    if (InspectedNode.Kind == NodeKind.Instance)
                    {
                        body.Children.Add(b.Button(() => "Reset overrides", () => Run(() => ComponentService.ResetOverrides(Session))));
                        body.Children.Add(b.Button(() => "Detach instance", () => Run(() => ComponentService.Detach(Session))));
                    }
                    else body.Children.Add(b.Button(() => "Update all instances", () => Run(() => Session.Edit("Synchronize components", () => ComponentService.Synchronize(Session.Document)))));
                });
            }
            AddExportSection();
        }
        _inspectorView.End();
        _inspectorDocument = Session.Document; _inspectorPrimary = node;
        if (retarget) { _inspectorSelection.Clear(); _inspectorSelection.UnionWith(Session.SelectedIds); }
        InspectorTargetId = node?.Id; InspectorTargetName = node?.Name; InspectorSelectionCount = Session.Selection.Count;
    }

    private void BuildTransform()
    {
        Inspect("Transform", null, (body, b) =>
        {
            var align = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
            foreach (var direction in new[] { "left", "center", "right", "top", "middle", "bottom" }) align.Children.Add(new IconButton(direction, "Align " + direction, () => Run(() => Session.Align(direction))) { Width = 30, Height = 26 });
            body.Children.Add(align);
            body.Children.Add(Studio.Columns((b.Number("X", () => InspectedNode.X, v => Change("Change X", n => n.X = v)), -1), (b.Number("Y", () => InspectedNode.Y, v => Change("Change Y", n => n.Y = v)), -1)));
            var flip = b.Icon(() => "flip", "Flip horizontally", () => Change("Flip horizontal", n => n.FlipX = !n.FlipX));
            var flipY = b.Icon(() => "flip", "Flip vertically", () => Change("Flip vertical", n => n.FlipY = !n.FlipY));
            flipY.RenderTransform = new RotateTransform { Angle = 90 }; flipY.RenderTransformOrigin = new(.5, .5);
            body.Children.Add(Studio.Columns((b.Number("°", () => InspectedNode.Rotation, v => Change("Rotate", n => n.Rotation = v)), -1), (flip, 30), (flipY, 30)));
        });
    }

    private void BuildLayout(DesignNode node)
    {
        Inspect("Layout", (node.IsContainer, node.Layout.Direction != LayoutDirection.None), (body, b) =>
        {
            var chain = b.Icon(() => "link", "Lock aspect ratio", () => { _aspectLocked = !_aspectLocked; RequestUi(UiDirty.Inspector); }); chain.Width = 24;
            b.Observe(_ => chain.IsSelected = _aspectLocked);
            body.Children.Add(Studio.Columns((b.Number("W", () => InspectedNode.Width, v => Resize(v, null), 1), -1), (b.Number("H", () => InspectedNode.Height, v => Resize(null, v), 1), -1), (chain, 24)));
            if (!InspectedNode.IsContainer) return;
            body.Children.Add(b.Check("Clip content", () => InspectedNode.ClipContent, v => Change("Clip content", n => n.ClipContent = v)));
            body.Children.Add(b.Choice(Enum.GetNames<LayoutDirection>(), () => InspectedNode.Layout.Direction.ToString(), value => Change("Change auto layout", n => n.Layout.Direction = Enum.Parse<LayoutDirection>(value)), "Auto layout direction"));
            if (InspectedNode.Layout.Direction == LayoutDirection.None) return;
            body.Children.Add(Studio.Columns((b.Number("Gap", () => InspectedNode.Layout.Gap, v => Change("Item spacing", n => n.Layout.Gap = v), 0), -1), (b.Choice(Enum.GetNames<LayoutAlignment>(), () => InspectedNode.Layout.Alignment.ToString(), value => Change("Layout alignment", n => n.Layout.Alignment = Enum.Parse<LayoutAlignment>(value)), "Layout alignment"), -1)));
            body.Children.Add(Studio.Columns((b.Number("L", () => InspectedNode.Layout.PaddingLeft, v => Change("Left padding", n => n.Layout.PaddingLeft = v), 0), -1), (b.Number("R", () => InspectedNode.Layout.PaddingRight, v => Change("Right padding", n => n.Layout.PaddingRight = v), 0), -1)));
            body.Children.Add(Studio.Columns((b.Number("T", () => InspectedNode.Layout.PaddingTop, v => Change("Top padding", n => n.Layout.PaddingTop = v), 0), -1), (b.Number("B", () => InspectedNode.Layout.PaddingBottom, v => Change("Bottom padding", n => n.Layout.PaddingBottom = v), 0), -1)));
            body.Children.Add(Studio.Columns((b.Check("Hug width", () => InspectedNode.Layout.HugWidth, v => Change("Hug width", n => n.Layout.HugWidth = v)), -1), (b.Check("Hug height", () => InspectedNode.Layout.HugHeight, v => Change("Hug height", n => n.Layout.HugHeight = v)), -1)));
        }, node.IsContainer ? "plus" : null, () => Change("Add auto layout", n => { if (n.IsContainer) n.Layout.Direction = LayoutDirection.Horizontal; }));
    }

    private void BuildConstraints(bool autoLayout)
    {
        Inspect("Constraints", autoLayout, (body, b) =>
        {
            body.Children.Add(Studio.Columns((b.Choice(Enum.GetNames<AxisConstraint>(), () => InspectedNode.HorizontalConstraint.ToString(), value => Change("Horizontal constraint", n => n.HorizontalConstraint = Enum.Parse<AxisConstraint>(value)), "Horizontal constraint"), -1), (b.Choice(Enum.GetNames<AxisConstraint>(), () => InspectedNode.VerticalConstraint.ToString(), value => Change("Vertical constraint", n => n.VerticalConstraint = Enum.Parse<AxisConstraint>(value)), "Vertical constraint"), -1)));
            if (InspectedNode.Parent?.Layout.Direction != LayoutDirection.None) body.Children.Add(Studio.Columns((b.Check("Fill width", () => InspectedNode.FillWidth, v => Change("Fill width", n => n.FillWidth = v)), -1), (b.Check("Fill height", () => InspectedNode.FillHeight, v => Change("Fill height", n => n.FillHeight = v)), -1)));
        });
    }

    private void BuildTypography()
    {
        Inspect("Typography", null, (body, b) =>
        {
            body.Children.Add(b.Input("Font family", () => InspectedNode.FontFamily, value => { if (!string.IsNullOrWhiteSpace(value)) Change("Font family", n => n.FontFamily = value); }));
            string[] weights = ["Regular", "Medium", "Semibold", "Bold"]; int[] values = [400, 500, 600, 700];
            body.Children.Add(Studio.Columns((b.Choice(weights, () => weights[Math.Clamp((InspectedNode.FontWeight - 400) / 100, 0, 3)], value => Change("Font weight", n => n.FontWeight = values[Array.IndexOf(weights, value)]), "Font weight"), -1), (b.Number("T", () => InspectedNode.FontSize, v => Change("Font size", n => n.FontSize = v), 1, 4096), 92)));
            body.Children.Add(Studio.Columns((b.Number("↕", () => InspectedNode.LineHeight * 100, v => Change("Line height", n => n.LineHeight = v / 100), 20, 1000), -1), (b.Number("↔", () => InspectedNode.LetterSpacing, v => Change("Letter spacing", n => n.LetterSpacing = v), -100, 100), -1)));
            var align = new SegmentedControl(["Left", "Center", "Right"], (int)InspectedNode.TextAlign);
            align.SelectionChanged += index => { if (b.CanWrite) Change("Text alignment", n => n.TextAlign = (ArtSpace.Core.TextAlignment)index); };
            b.Observe(_ => { var value = (int)InspectedNode.TextAlign; if (align.SelectedIndex != value) align.SelectedIndex = value; }); body.Children.Add(align);
            var text = b.Input("Text content", () => InspectedNode.Text, value => Change("Change text", n => { n.Text = value; ComponentService.SetOverride(n, text: value); }));
            text.AcceptsReturn = true; text.TextWrapping = TextWrapping.Wrap; text.Height = 68; body.Children.Add(text);
            body.Children.Add(b.Button(() => "Edit on canvas", () => Surface.BeginTextEdit(InspectedNode)));
        });
    }

    private void BuildFills(DesignNode node)
    {
        // Exact topology key; no document serialization, geometry parsing or paint-value hashing.
        var shape = string.Join(';', node.Fills.Select(f => f.Kind == FillKind.Solid ? "solid" : "gradient:" + f.Stops.Count));
        Inspect("Fill", shape, (body, b) =>
        {
            for (var index = 0; index < InspectedNode.Fills.Count; index++)
            {
                var i = index;
                body.Children.Add(Studio.Columns((b.Color(() => InspectedNode.Fills[i].Color, c => ChangeAppearance("Fill color", n => { if (n.Fills.Count > i) { n.Fills[i].Color = c; ComponentService.SetOverride(n, fill: c); } }), "Fill " + i), -1), (b.Number("%", () => InspectedNode.Fills[i].Opacity * 100, v => ChangeAppearance("Fill opacity", n => { if (n.Fills.Count > i) n.Fills[i].Opacity = v / 100; }), 0, 100), 65), (b.Icon(() => InspectedNode.Fills[i].Visible ? "eye" : "eye-off", "Toggle fill", () => ChangeAppearance("Toggle fill", n => { if (n.Fills.Count > i) n.Fills[i].Visible = !n.Fills[i].Visible; })), 23), (b.Icon(() => "minus", "Remove fill", () => ChangeAppearance("Remove fill", n => { if (n.Fills.Count > i) n.Fills.RemoveAt(i); })), 23)));
                body.Children.Add(b.Choice(Enum.GetNames<FillKind>(), () => InspectedNode.Fills[i].Kind.ToString(), value => ChangeAppearance("Fill type", n => { if (n.Fills.Count > i) n.Fills[i].Kind = Enum.Parse<FillKind>(value); }), "Fill type"));
                if (InspectedNode.Fills[i].Kind == FillKind.Solid) continue;
                for (var stopIndex = 0; stopIndex < InspectedNode.Fills[i].Stops.Count; stopIndex++)
                {
                    var s = stopIndex;
                    body.Children.Add(Studio.Columns((b.Color(() => InspectedNode.Fills[i].Stops[s].Color, value => ChangeAppearance("Gradient stop color", n => { if (n.Fills.Count > i && n.Fills[i].Stops.Count > s) n.Fills[i].Stops[s].Color = value; }), "Stop " + s), -1), (b.Number("%", () => InspectedNode.Fills[i].Stops[s].Offset * 100, value => ChangeAppearance("Gradient stop position", n => { if (n.Fills.Count > i && n.Fills[i].Stops.Count > s) n.Fills[i].Stops[s].Offset = value / 100; }), 0, 100), 75)));
                }
                body.Children.Add(b.Button(() => "Add gradient stop", () => ChangeAppearance("Add gradient stop", n => { if (n.Fills.Count > i) n.Fills[i].Stops.Add(new() { Offset = .5, Color = "#FFFFFF" }); })));
                body.Children.Add(Studio.Columns((b.Number("X1", () => InspectedNode.Fills[i].Start.X, v => ChangeAppearance("Gradient start", n => { if (n.Fills.Count > i) n.Fills[i].Start = n.Fills[i].Start with { X = v }; })), -1), (b.Number("Y1", () => InspectedNode.Fills[i].Start.Y, v => ChangeAppearance("Gradient start", n => { if (n.Fills.Count > i) n.Fills[i].Start = n.Fills[i].Start with { Y = v }; })), -1)));
            }
        }, "plus", () => ChangeAppearance("Add fill", n => n.Fills.Add(new())));
    }

    private void BuildStrokes(DesignNode node)
    {
        Inspect("Stroke", node.Strokes.Count, (body, b) =>
        {
            for (var index = 0; index < InspectedNode.Strokes.Count; index++)
            {
                var i = index;
                body.Children.Add(Studio.Columns((b.Color(() => InspectedNode.Strokes[i].Color, color => ChangeAppearance("Stroke color", n => { if (n.Strokes.Count > i) { n.Strokes[i].Color = color; n.Strokes[i].Paint = null; } }), "Stroke " + i), -1), (b.Number("W", () => InspectedNode.Strokes[i].Width, v => ChangeAppearance("Stroke width", n => { if (n.Strokes.Count > i) n.Strokes[i].Width = v; }), 0, 1000), 68), (b.Icon(() => "minus", "Remove stroke", () => ChangeAppearance("Remove stroke", n => { if (n.Strokes.Count > i) n.Strokes.RemoveAt(i); })), 24)));
                body.Children.Add(Studio.Columns((b.Number("%", () => InspectedNode.Strokes[i].Opacity * 100, v => ChangeAppearance("Stroke opacity", n => { if (n.Strokes.Count > i) n.Strokes[i].Opacity = v / 100; }), 0, 100), -1), (b.Choice(["Solid", "Dashed", "Dotted"], () => InspectedNode.Strokes[i].Dashes.Count == 0 ? "Solid" : InspectedNode.Strokes[i].Dashes[0] == 1 ? "Dotted" : "Dashed", value => ChangeAppearance("Stroke dash", n => { if (n.Strokes.Count > i) n.Strokes[i].Dashes = value == "Dashed" ? [8, 6] : value == "Dotted" ? [1, 5] : []; }), "Stroke dash"), -1)));
            }
        }, "plus", () => ChangeAppearance("Add stroke", n => n.Strokes.Add(new())));
    }

    private void BuildEffects(DesignNode node)
    {
        Inspect("Effects", node.Shadows.Count > 0, (body, b) =>
        {
            if (InspectedNode.Shadows.Count == 0) return;
            body.Children.Add(Studio.Columns((Studio.Text("Drop shadow", 11), -1), (b.Icon(() => InspectedNode.Shadows[0].Visible ? "eye" : "eye-off", "Toggle shadow", () => ChangeAppearance("Toggle shadow", n => { if (n.Shadows.Count > 0) n.Shadows[0].Visible = !n.Shadows[0].Visible; })), 24), (b.Icon(() => "minus", "Remove shadow", () => ChangeAppearance("Remove shadow", n => n.Shadows.Clear())), 24)));
            body.Children.Add(Studio.Columns((b.Number("X", () => InspectedNode.Shadows[0].X, v => ChangeAppearance("Shadow X", n => { if (n.Shadows.Count > 0) n.Shadows[0].X = v; })), -1), (b.Number("Y", () => InspectedNode.Shadows[0].Y, v => ChangeAppearance("Shadow Y", n => { if (n.Shadows.Count > 0) n.Shadows[0].Y = v; })), -1)));
            body.Children.Add(Studio.Columns((b.Number("Blur", () => InspectedNode.Shadows[0].Blur, v => ChangeAppearance("Shadow blur", n => { if (n.Shadows.Count > 0) n.Shadows[0].Blur = v; }), 0, 512), -1), (b.Number("%", () => InspectedNode.Shadows[0].Opacity * 100, v => ChangeAppearance("Shadow opacity", n => { if (n.Shadows.Count > 0) n.Shadows[0].Opacity = v / 100; }), 0, 100), -1)));
            body.Children.Add(b.Color(() => InspectedNode.Shadows[0].Color, c => ChangeAppearance("Shadow color", n => { if (n.Shadows.Count > 0) n.Shadows[0].Color = c; })));
        }, "plus", () => ChangeAppearance("Add shadow", n => { if (n.Shadows.Count == 0) n.Shadows.Add(new()); }));
    }

    private void AddExportSection()
    {
        Inspect("Export", null, (body, b) =>
        {
            body.Children.Add(Studio.Columns((b.Choice(["1×", "2×", "3×", "4×"], () => ((int)_exportScale) + "×", value => _exportScale = value[0] - '0', "Export scale"), 70), (b.Button(() => "PNG", () => RunAsync(() => ExportAsync(false))), -1), (b.Button(() => "SVG", () => RunAsync(() => ExportAsync(true))), -1)));
            var note = b.Text(() => Session.Selection.Count > 0 ? "Exports selected layers at their actual document dimensions." : "Exports all visible layers on this page.", 10, Studio.Muted); note.TextWrapping = TextWrapping.Wrap; body.Children.Add(note);
        });
    }

    private void BuildPrototypeInspector()
    {
        Inspect("Prototype", Session.Primary is not null, (body, b) =>
        {
            body.Children.Add(Wrapped("Connect a layer to another frame. Present the design and click the layer to navigate."));
            if (Session.Primary is null) body.Children.Add(Wrapped("Select a layer to add an interaction."));
        });
        if (Session.Primary is not { } node) return;
        var frames = Session.Document.AllNodes().Where(n => n.IsFrame && n.Id != node.Id && !n.IsDescendantOf(node)).Select(n => (n.Id, n.Name)).ToArray();
        Inspect("Interactions", string.Join('|', frames.Select(f => f.Id + ":" + f.Name)), (body, b) =>
        {
            body.Children.Add(Studio.Text("On click → Navigate to", 11));
            var combo = new ComboBox { Style = (Style)StudioResources.Current["VS.ComboBox"], Height = 32, FontFamily = Studio.Font };
            combo.Items.Add(new ComboBoxItem { Content = "None", Tag = "" });
            foreach (var frame in frames) combo.Items.Add(new ComboBoxItem { Content = frame.Name, Tag = frame.Id });
            b.Observe(_ => combo.SelectedItem = combo.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == InspectedNode.PrototypeTargetId) ?? combo.Items[0]);
            combo.SelectionChanged += (_, _) => { if (b.CanWrite && combo.SelectedItem is ComboBoxItem item) Change("Prototype interaction", n => n.PrototypeTargetId = string.IsNullOrEmpty((string)item.Tag) ? null : (string)item.Tag); };
            AutomationProperties.SetName(combo, "Prototype destination"); body.Children.Add(combo);
            body.Children.Add(b.Button(() => "Present prototype", () => Run(Surface.Present)));
        });
        Inspect("Preview", null, (body, _) => body.Children.Add(Wrapped("Navigation is immediate. Press Escape to return to editing. Prototype links are stored in the document and work without a server.")));
    }

    private static NumericField Number(string label, double value, Action<double> changed, double minimum = -1_000_000, double maximum = 1_000_000) => new(label, value, changed) { Minimum = minimum, Maximum = maximum };
    private static CheckBox Check(string text, bool value, Action<bool> changed)
    {
        var box = new CheckBox { Content = text, IsChecked = value, FontFamily = Studio.Font, FontSize = 11, MinWidth = 0, MinHeight = 28, Padding = new(0), VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(box, text); box.Checked += (_, _) => changed(true); box.Unchecked += (_, _) => changed(false); return box;
    }
    private void Resize(double? width, double? height)
    {
        Change("Resize layers", node =>
        {
            var w = width ?? node.Width; var h = height ?? node.Height;
            if (_aspectLocked) { if (width.HasValue) h = node.Height * w / Math.Max(1, node.Width); else w = node.Width * h / Math.Max(1, node.Height); }
            node.Layout.HugWidth = node.Layout.HugHeight = false; LayoutEngine.Resize(node, w, h);
        });
    }
}
