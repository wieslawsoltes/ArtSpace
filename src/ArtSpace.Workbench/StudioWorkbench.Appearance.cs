using System.Globalization;
using ArtSpace.Illustration;

namespace ArtSpace.Workbench;

public sealed partial class StudioWorkbench
{
    private readonly StackPanel _appearanceHost = new();
    private readonly StackPanel _graphicStylesHost = new();
    private RetainedInspector? _appearanceView, _graphicStylesView, _activeInspector;
    private DesignNode? _appearancePrimary;
    private DesignDocument? _appearanceDocument, _stylesDocument;
    private readonly HashSet<string> _appearanceSelection = [];
    private LiveEffectKind _newEffectKind;
    private int _stylePage;
    private const int StylesPerPage = 12;
    private void ChangeAppearance(string label, Action<DesignNode> update) => Run(() =>
        Session.UpdateSelection(label, node => { update(node); ComponentService.SetAppearanceOverride(node); }));
    public long AppearancePanelBuilds => (_appearanceView?.SectionBuilds ?? 0) + (_graphicStylesView?.SectionBuilds ?? 0);
    public IEnumerable<InspectorFieldState> AppearanceFields => ActivePanel switch
    {
        "Appearance" when _appearanceView is not null => _appearanceView.DescribeFields(),
        "Graphic Styles" when _graphicStylesView is not null => _graphicStylesView.DescribeFields(),
        _ => []
    };

    private void ConfigureAppearancePanels()
    {
        _illustrationDock.Add("Appearance", Studio.Scroll(_appearanceHost));
        _illustrationDock.Add("Graphic Styles", Studio.Scroll(_graphicStylesHost));
    }

    private void SuspendAppearanceEditing()
    {
        _appearanceView?.SuspendEditing(); _graphicStylesView?.SuspendEditing();
    }

    private void RefreshAppearancePanels(UiDirty dirty)
    {
        if ((dirty & UiDirty.Appearance) != 0)
        {
            if (IsPanelVisible("Appearance")) RefreshAppearance();
            else { _appearanceView?.SuspendEditing(); _uiDirty |= UiDirty.Appearance; }
        }
        if ((dirty & UiDirty.GraphicStyles) != 0)
        {
            if (IsPanelVisible("Graphic Styles")) RefreshGraphicStyles();
            else { _graphicStylesView?.SuspendEditing(); _uiDirty |= UiDirty.GraphicStyles; }
        }
    }

    private void AddLiveEffect(LiveEffectKind kind)
    {
        AppearanceOperations.AddEffect(Session, kind);
        _uiVisible = true; ResizeIllustrationWorkspace(); _illustrationDock.Select("Appearance");
    }

    private void RefreshAppearance()
    {
        _appearanceView ??= new(_appearanceHost);
        var node = Session.Primary;
        var retarget = !ReferenceEquals(_appearancePrimary, node) || !ReferenceEquals(_appearanceDocument, Session.Document)
            || !_appearanceSelection.SetEquals(Session.SelectedIds);
        _appearanceView.Begin(retarget); _activeInspector = _appearanceView;
        try
        {
            if (node is null)
                Inspect("Appearance", "empty", (body, _) => body.Children.Add(Wrapped("Select artwork to edit its appearance. Effects operate on the composed object without changing its editable paths.")));
            else
            {
                Inspect("Object appearance", null, (body, b) =>
                {
                    body.Children.Add(b.Text(() => InspectedNode.Name, 12, Studio.Ink, true));
                    body.Children.Add(Studio.Columns((b.Number("Opacity", () => InspectedNode.Opacity * 100,
                        value => ChangeAppearance("Object opacity", n => n.Opacity = value / 100), 0, 100), -1),
                        (b.Choice(Enum.GetNames<BlendKind>(), () => InspectedNode.Blend.ToString(),
                        value => ChangeAppearance("Blend mode", n => n.Blend = Enum.Parse<BlendKind>(value)), "Blend mode"), -1)));
                });
                BuildLiveEffects(node);
                BuildFills(node); BuildStrokes(node); BuildAdvancedStrokes(node); BuildEffects(node);
                BuildPaintOrder(node);
                Inspect("Appearance actions", null, (body, b) =>
                {
                    body.Children.Add(b.Button(() => "New Graphic Style", () => Run(() => AppearanceOperations.CaptureStyle(Session))));
                    body.Children.Add(Studio.Columns((b.Button(() => "Clear Appearance", () => Run(() => AppearanceOperations.Clear(Session))), -1),
                        (b.Button(() => "Reduce to Basic", () => Run(() => AppearanceOperations.Clear(Session, true))), -1)));
                });
            }
            _appearanceView.End();
            _appearancePrimary = node; _appearanceDocument = Session.Document;
            _appearanceSelection.Clear(); _appearanceSelection.UnionWith(Session.SelectedIds);
        }
        finally { _activeInspector = null; }
    }

    private void BuildLiveEffects(DesignNode node)
    {
        Inspect("Live Effects", null, (body, b) =>
        {
            var kinds = Enum.GetValues<LiveEffectKind>();
            body.Children.Add(b.Choice(kinds.Select(LiveEffect.Name), () => LiveEffect.Name(_newEffectKind),
                value => _newEffectKind = kinds.Single(k => LiveEffect.Name(k) == value), "New effect"));
            body.Children.Add(b.Button(() => "Add live effect", () => Run(() => AddLiveEffect(_newEffectKind))));
        });
        for (var index = 0; index < node.Effects.Count; index++)
        {
            var i = index;
            Inspect("Effect " + (i + 1), node.Effects[i].Kind, (body, b) =>
            {
                LiveEffect Current() => InspectedNode.Effects[i];
                void Update(string label, Action<LiveEffect> edit) => Run(() => AppearanceOperations.UpdateEffect(Session, i, label, edit));
                body.Children.Add(b.Text(() => LiveEffect.Name(Current().Kind), 12, Studio.Ink, true));
                body.Children.Add(b.Check("Enabled", () => Current().Enabled, value => Update("Toggle live effect", e => e.Enabled = value)));
                if (Current().Kind == LiveEffectKind.Saturation)
                    body.Children.Add(b.Number("Saturation", () => Current().Amount * 100, value => Update("Effect saturation", e => e.Amount = value / 100), 0, 400));
                else
                {
                    body.Children.Add(b.Number("Radius", () => Current().Radius, value => Update("Effect radius", e => e.Radius = value), 0, 256));
                    if (Current().Kind == LiveEffectKind.DropShadow)
                        body.Children.Add(Studio.Columns((b.Number("X", () => Current().OffsetX, value => Update("Shadow X", e => e.OffsetX = value), -4096, 4096), -1),
                            (b.Number("Y", () => Current().OffsetY, value => Update("Shadow Y", e => e.OffsetY = value), -4096, 4096), -1)));
                    if (Current().Kind is LiveEffectKind.DropShadow or LiveEffectKind.OuterGlow)
                    {
                        body.Children.Add(b.Color(() => Current().Color, value => Update("Effect color", e => e.Color = value), "Effect color"));
                        body.Children.Add(b.Number("Opacity", () => Current().Opacity * 100, value => Update("Effect opacity", e => e.Opacity = value / 100), 0, 100));
                    }
                }
                body.Children.Add(Studio.Columns(
                    (b.Icon(() => "top", "Move effect up", () => Run(() => AppearanceOperations.MoveEffect(Session, i, -1))), 28),
                    (b.Icon(() => "bottom", "Move effect down", () => Run(() => AppearanceOperations.MoveEffect(Session, i, 1))), 28),
                    (b.Button(() => "Duplicate", () => Run(() => AppearanceOperations.DuplicateEffect(Session, i))), -1),
                    (b.Button(() => "Delete", () => Run(() => AppearanceOperations.RemoveEffect(Session, i))), -1)));
            });
        }
    }

    private void BuildAdvancedStrokes(DesignNode node)
    {
        for (var index = 0; index < node.Strokes.Count; index++)
        {
            var i = index; var source = node.Strokes[i].Paint;
            Inspect("Stroke options " + (i + 1), (source?.Kind, source?.Stops.Count ?? 0), (body, b) =>
            {
                StrokeStyle Current() => InspectedNode.Strokes[i];
                void Update(string label, Action<StrokeStyle> edit) => ChangeAppearance(label, n => { if (i < n.Strokes.Count) edit(n.Strokes[i]); });
                body.Children.Add(b.Input("Dash pattern", () => string.Join(" ", Current().Dashes.Select(Numbers.Format)), value =>
                {
                    try
                    {
                        var parts = value.Split(new[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length > 4096) throw new FormatException("Use at most 4096 dash intervals.");
                        var lengths = parts.Select(part => double.Parse(part, NumberStyles.Float, CultureInfo.InvariantCulture)).ToList();
                        if (lengths.Any(x => !double.IsFinite(x) || x <= 0 || !float.IsFinite((float)x) || (float)x <= 0))
                            throw new FormatException("Dash and gap lengths must be positive finite numbers.");
                        Update("Dash pattern", stroke => stroke.Dashes = [.. lengths]);
                    }
                    catch (Exception ex) when (ex is FormatException or OverflowException) { ShowStatus(ex.Message, true); RequestUi(UiDirty.All); }
                }));
                body.Children.Add(b.Number("Dash offset", () => Current().DashOffset, value => Update("Dash offset", stroke => stroke.DashOffset = value), -1e9, 1e9));
                body.Children.Add(b.Choice(Enum.GetNames<FillKind>(), () => (Current().Paint?.Kind ?? FillKind.Solid).ToString(), value =>
                    Update("Stroke paint", stroke =>
                    {
                        var kind = Enum.Parse<FillKind>(value);
                        if (kind == FillKind.Solid) { stroke.Paint = null; return; }
                        stroke.Paint ??= new() { Color = stroke.Color, Start = new(0, 0), End = new(1, 0),
                            Stops = [new() { Offset = 0, Color = stroke.Color }, new() { Offset = 1, Color = "#FFFFFF" }] };
                        stroke.Paint.Kind = kind;
                    }), "Stroke paint"));
                if (Current().Paint is not { } paint) return;
                body.Children.Add(b.Choice(Enum.GetNames<GradientSpread>(), () => Current().Paint!.GradientSpread.ToString(),
                    value => Update("Stroke gradient spread", stroke => { if (stroke.Paint is not null) stroke.Paint.GradientSpread = Enum.Parse<GradientSpread>(value); }), "Spread"));
                for (var stopIndex = 0; stopIndex < paint.Stops.Count; stopIndex++)
                {
                    var s = stopIndex;
                    void Stop(Action<ArtSpace.Core.GradientStop> edit) => Update("Stroke gradient stop", stroke => { if (stroke.Paint is { } p && s < p.Stops.Count) edit(p.Stops[s]); });
                    body.Children.Add(Studio.Columns((b.Color(() => Current().Paint!.Stops[s].Color, value => Stop(stop => stop.Color = value), "Stop " + s), -1),
                        (b.Number("Position " + s, () => Current().Paint!.Stops[s].Offset * 100, value => Stop(stop => stop.Offset = value / 100), 0, 100), 80)));
                    body.Children.Add(b.Number("Stop opacity " + s, () => Current().Paint!.Stops[s].Opacity * 100, value => Stop(stop => stop.Opacity = value / 100), 0, 100));
                }
            });
        }
    }

    private void BuildPaintOrder(DesignNode node)
    {
        Inspect("Paint stacking", (node.Fills.Count, node.Strokes.Count), (body, b) =>
        {
            body.Children.Add(Wrapped("Front to back. Strokes paint above fills; each list is independently ordered."));
            for (var index = InspectedNode.Strokes.Count - 1; index >= 0; index--)
            {
                var i = index;
                body.Children.Add(Studio.Columns((b.Text(() => "Stroke " + (i + 1)), -1),
                    (b.Icon(() => "top", "Raise stroke", () => ChangeAppearance("Raise stroke", n => Move(n.Strokes, i, 1))), 28),
                    (b.Icon(() => "bottom", "Lower stroke", () => ChangeAppearance("Lower stroke", n => Move(n.Strokes, i, -1))), 28),
                    (b.Button(() => "Duplicate", () => ChangeAppearance("Duplicate stroke", n => { if (i < n.Strokes.Count) n.Strokes.Insert(i + 1, GraphicStyle.CloneStroke(n.Strokes[i])); })), 80)));
            }
            for (var index = InspectedNode.Fills.Count - 1; index >= 0; index--)
            {
                var i = index;
                body.Children.Add(Studio.Columns((b.Text(() => "Fill " + (i + 1)), -1),
                    (b.Icon(() => "top", "Raise fill", () => ChangeAppearance("Raise fill", n => Move(n.Fills, i, 1))), 28),
                    (b.Icon(() => "bottom", "Lower fill", () => ChangeAppearance("Lower fill", n => Move(n.Fills, i, -1))), 28),
                    (b.Button(() => "Duplicate", () => ChangeAppearance("Duplicate fill", n => { if (i < n.Fills.Count) n.Fills.Insert(i + 1, GraphicStyle.CloneFill(n.Fills[i])); })), 80)));
            }
        });
        static void Move<T>(List<T> values, int index, int direction)
        {
            var target = index + direction;
            if (index < values.Count && target >= 0 && target < values.Count)
                (values[index], values[target]) = (values[target], values[index]);
        }
    }

    private void RefreshGraphicStyles()
    {
        _graphicStylesView ??= new(_graphicStylesHost);
        if (!ReferenceEquals(_stylesDocument, Session.Document)) _stylePage = 0;
        var pages = Math.Max(1, (Session.Document.GraphicStyles.Count + StylesPerPage - 1) / StylesPerPage);
        _stylePage = Math.Clamp(_stylePage, 0, pages - 1);
        _graphicStylesView.Begin(!ReferenceEquals(_stylesDocument, Session.Document)); _activeInspector = _graphicStylesView;
        try
        {
            Inspect("Graphic Styles", null, (body, b) =>
            {
                body.Children.Add(Wrapped("Save the selected object's fills, strokes, transparency and live effects. Applying a style leaves geometry and mask relationships intact."));
                var create = b.Button(() => "New Graphic Style", () => Run(() => AppearanceOperations.CaptureStyle(Session)));
                b.Observe(_ => create.IsEnabled = Session.Primary is not null); body.Children.Add(create);
                var previous = b.Button(() => "Previous", () => { _stylePage--; RequestUi(UiDirty.GraphicStyles); });
                var next = b.Button(() => "Next", () => { _stylePage++; RequestUi(UiDirty.GraphicStyles); });
                b.Observe(_ => { previous.IsEnabled = _stylePage > 0; next.IsEnabled = (_stylePage + 1) * StylesPerPage < Session.Document.GraphicStyles.Count; });
                body.Children.Add(Studio.Columns((previous, -1), (next, -1)));
                body.Children.Add(b.Text(() => "Page " + (_stylePage + 1) + " · " + Session.Document.GraphicStyles.Count + " styles"));
            });
            var first = _stylePage * StylesPerPage;
            for (var index = first; index < Math.Min(first + StylesPerPage, Session.Document.GraphicStyles.Count); index++)
            {
                var i = index; var id = Session.Document.GraphicStyles[i].Id;
                Inspect("Graphic style " + (i - first + 1), id, (body, b) =>
                {
                    GraphicStyle Current() => Session.Document.GraphicStyles.Find(s => s.Id == id)!;
                    var preview = new GraphicStylePreview { Height = 64, HorizontalAlignment = HorizontalAlignment.Stretch };
                    b.Observe(_ => preview.Style = Current()); body.Children.Add(preview);
                    body.Children.Add(b.Input("Style name", () => Current().Name, value => Run(() => AppearanceOperations.RenameStyle(Session, id, value))));
                    var apply = b.Button(() => "Apply style", () => Run(() => AppearanceOperations.ApplyStyle(Session, id)));
                    b.Observe(_ => apply.IsEnabled = Session.Primary is not null);
                    body.Children.Add(Studio.Columns((apply, -1), (b.Button(() => "Delete style", () => Run(() => AppearanceOperations.DeleteStyle(Session, id))), -1)));
                });
            }
            _graphicStylesView.End(); _stylesDocument = Session.Document;
        }
        finally { _activeInspector = null; }
    }
}
