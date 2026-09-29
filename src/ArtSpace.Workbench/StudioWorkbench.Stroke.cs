using ArtSpace.Illustration;
using ArtSpace.Skia;

namespace ArtSpace.Workbench;

public sealed partial class StudioWorkbench
{
    private readonly StackPanel _strokePanelHost = new();
    private RetainedInspector? _strokeInspector;
    private DesignNode? _strokePanelNode;
    private DesignDocument? _strokePanelDocument;
    private readonly HashSet<string> _strokePanelSelection = [];
    private double _newWidthPosition = 50;
    public long StrokePanelBuilds => _strokeInspector?.SectionBuilds ?? 0;

    private void RefreshStrokePanel(UiDirty dirty)
    {
        if ((dirty & UiDirty.Stroke) == 0) return;
        if (!IsPanelVisible("Stroke")) { _strokeInspector?.SuspendEditing(); _uiDirty |= UiDirty.Stroke; return; }
        _strokeInspector ??= new(_strokePanelHost);
        var node = Session.Primary;
        var retarget = !ReferenceEquals(node, _strokePanelNode) || !ReferenceEquals(Session.Document, _strokePanelDocument)
            || !_strokePanelSelection.SetEquals(Session.SelectedIds);
        _strokeInspector.Begin(retarget); _activeInspector = _strokeInspector;
        try
        {
            if (node is null || !PathEditing.CanEdit(node))
                Inspect("Stroke", "empty", (body, _) => body.Children.Add(Wrapped("Select an unlocked vector to edit stroke weight, caps, joins, dashes, gradients and width profiles. Use Create Outlines for text.")));
            else
            {
                BuildStrokes(node); BuildAdvancedStrokes(node); BuildVariableStrokes(node);
            }
            _strokeInspector.End();
            _strokePanelNode = node; _strokePanelDocument = Session.Document;
            _strokePanelSelection.Clear(); _strokePanelSelection.UnionWith(Session.SelectedIds);
        }
        finally { _activeInspector = null; }
    }

    private void BuildVariableStrokes(DesignNode node)
    {
        if (!PathEditing.CanEdit(node)) return;
        for (var strokeIndex = 0; strokeIndex < node.Strokes.Count; strokeIndex++)
        {
            var index = strokeIndex;
            StrokeStyle Current() => InspectedNode.Strokes[index];
            Inspect("Width Profile " + (index + 1), null, (body, b) =>
            {
                var presets = Enum.GetValues<StrokeWidthPreset>();
                var names = presets.Select(StrokeProfiles.Name).Append("Custom").ToArray();
                var profiles = presets.Select(StrokeProfiles.Create).ToArray();
                string ProfileName()
                {
                    for (var p = 0; p < presets.Length; p++)
                    {
                        var preset = presets[p]; var candidate = profiles[p]; var current = Current().WidthProfile;
                        if (candidate.Count != current.Count) continue;
                        var same = true;
                        for (var i = 0; i < candidate.Count; i++)
                            if (candidate[i].Position != current[i].Position || candidate[i].Left != current[i].Left || candidate[i].Right != current[i].Right) { same = false; break; }
                        if (same) return StrokeProfiles.Name(preset);
                    }
                    return "Custom";
                }
                body.Children.Add(b.Choice(names, ProfileName, name =>
                {
                    if (name == "Custom") return;
                    Run(() => StrokeProfileOperations.SetPreset(Session, Surface.Renderer, index, presets.Single(p => StrokeProfiles.Name(p) == name)));
                }, "Width profile " + (index + 1)));
                body.Children.Add(b.Button(() => "Edit Width on Canvas · Shift W", () => Surface.ActivateWidthStroke(index)));
                body.Children.Add(Studio.Columns((b.Number("Position %", () => _newWidthPosition, value => _newWidthPosition = value, 0, 100), -1),
                    (b.Button(() => "Add Point", () => Run(() => StrokeProfileOperations.AddPoint(Session, Surface.Renderer, index, _newWidthPosition / 100))), -1)));
                body.Children.Add(Studio.Columns((b.Button(() => "Reverse", () => Run(() => StrokeProfileOperations.Reverse(Session, Surface.Renderer, index))), -1),
                    (b.Button(() => "Swap Sides", () => Run(() => StrokeProfileOperations.SwapSides(Session, Surface.Renderer, index))), -1)));
                body.Children.Add(b.Button(() => "Save as Graphic Style", () => Run(() => AppearanceOperations.CaptureStyle(Session))));
                body.Children.Add(Wrapped("Drag a side handle to change both sides; Alt-drag changes one side. Drag a diamond along the path; Alt-drag duplicates it. Delete removes a selected knot. Side values below use object-local units."));
            });
            for (var pointIndex = 0; pointIndex < node.Strokes[index].WidthProfile.Count; pointIndex++)
            {
                var point = pointIndex;
                Inspect($"Stroke {index + 1} · Width Point {point + 1}", null, (body, b) =>
                {
                    StrokeWidthPoint Knot() => Current().WidthProfile[point];
                    void Update(Action<StrokeWidthPoint> change) => Run(() => StrokeProfileOperations.UpdatePoint(Session, Surface.Renderer, index, point, change));
                    body.Children.Add(b.Number("Position %", () => Knot().Position * 100, value => Update(p => p.Position = value / 100), 0, 100));
                    body.Children.Add(Studio.Columns((b.Number("Left", () => Knot().Left * Current().Width, value => Update(p => p.Left = value / Math.Max(1e-9, Current().Width)), 0, 16_000_000), -1),
                        (b.Number("Right", () => Knot().Right * Current().Width, value => Update(p => p.Right = value / Math.Max(1e-9, Current().Width)), 0, 16_000_000), -1)));
                    body.Children.Add(b.Button(() => "Delete Point", () => Run(() => StrokeProfileOperations.RemovePoint(Session, Surface.Renderer, index, point))));
                });
            }
        }
    }
}
