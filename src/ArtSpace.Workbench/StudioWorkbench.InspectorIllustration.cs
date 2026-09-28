using ArtSpace.Illustration;
using ArtSpace.Skia;

namespace ArtSpace.Workbench;

public sealed partial class StudioWorkbench
{
    private void AddIllustrationSections()
    {
        Inspect("Transparency", OpacityMaskOperations.FindOwner(Session.Primary) is not null, (body, b) =>
        {
            if (OpacityMaskOperations.FindOwner(Session.Primary) is not null)
            {
                body.Children.Add(b.Choice(Enum.GetNames<OpacityMaskMode>(), () => OpacityMaskOperations.FindOwner(Session.Primary)!.OpacityMaskMode.ToString(), value => Run(() => OpacityMaskOperations.SetMode(Session, Enum.Parse<OpacityMaskMode>(value))), "Opacity mask mode"));
                body.Children.Add(b.Button(() => "Edit Mask Artwork", () => Run(() => OpacityMaskOperations.EditMask(Session))));
                body.Children.Add(b.Button(() => "Edit Masked Artwork", () => Run(() => OpacityMaskOperations.EditContents(Session))));
                body.Children.Add(b.Button(() => OpacityMaskOperations.FindOwner(Session.Primary)!.OpacityMaskInverted ? "Invert Mask: On" : "Invert Mask: Off", () => Run(() => OpacityMaskOperations.Invert(Session))));
                body.Children.Add(b.Button(() => OpacityMaskOperations.FindOwner(Session.Primary)!.OpacityMaskEnabled ? "Disable Opacity Mask" : "Enable Opacity Mask", () => Run(() => OpacityMaskOperations.ToggleEnabled(Session))));
                body.Children.Add(b.Button(() => "Release Opacity Mask", () => Run(() => OpacityMaskOperations.Release(Session))));
            }
            else
            {
                var make = b.Button(() => "Make Opacity Mask", () => Run(() => OpacityMaskOperations.Make(Session)));
                b.Observe(_ => make.IsEnabled = Session.Selection.Count > 0); body.Children.Add(make);
            }
        });
        if (ClippingOperations.FindGroup(Session.Primary) is not null)
        {
            Inspect("Clipping Mask", null, (body, b) =>
            {
                body.Children.Add(b.Button(() => "Edit Clipping Path", () => Run(() => { ClippingOperations.EditMask(Session); Surface.EnterPathEditing(); })));
                body.Children.Add(b.Button(() => "Edit Contents", () => Run(() => ClippingOperations.EditContents(Session))));
                body.Children.Add(b.Button(() => "Release Mask", () => Run(() => ClippingOperations.Release(Session))));
            });
        }
        if (PathEditing.CanEdit(Session.Primary))
        {
            Inspect("Path", Surface.SelectedAnchorCount > 0, (body, b) =>
            {
                body.Children.Add(b.Button(() => "Edit anchors", () => Run(Surface.EnterPathEditing)));
                body.Children.Add(b.Choice(["Nonzero", "Even-odd"], () => InspectedNode.FillRule == PathFillRule.EvenOdd ? "Even-odd" : "Nonzero", value => Change("Fill rule", n => n.FillRule = value == "Even-odd" ? PathFillRule.EvenOdd : PathFillRule.NonZero), "Path fill rule"));
                if (Surface.SelectedAnchorCount == 0) return;
                body.Children.Add(b.Text(() => Surface.SelectedAnchorCount + " anchors selected", 10, Studio.Muted));
                body.Children.Add(Studio.Columns((b.Button(() => "Smooth", () => Run(() => SmoothPathAnchors(true))), -1), (b.Button(() => "Corner", () => Run(() => SmoothPathAnchors(false))), -1)));
                body.Children.Add(b.Button(() => "Remove selected anchors", () => Run(() => Surface.RemoveSelectedAnchors(false))));
            });
        }
        if (Session.Primary?.Kind == NodeKind.Text)
            Inspect("Type", null, (body, b) => body.Children.Add(b.Button(() => "Create outlines", () => Run(() => PathOperations.CreateOutlines(Session, Surface.Renderer)))));
        Inspect("Pathfinder", null, (body, b) =>
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            foreach (var (op, glyph) in new[] { (BooleanOperation.Union, "union"), (BooleanOperation.Subtract, "subtract"), (BooleanOperation.Intersect, "intersect"), (BooleanOperation.Exclude, "exclude") })
            {
                var button = b.Icon(() => glyph, "Pathfinder " + op, () => Run(() => BooleanOperations.Apply(Session, Surface.Renderer, op)));
                button.Width = 42; button.Height = 29; row.Children.Add(button);
            }
            body.Children.Add(row);
        });
        Inspect("Swatches", null, (body, b) =>
        {
            var palette = new Grid { RowSpacing = 4, ColumnSpacing = 4 };
            string[] colors = ["#FFFFFF", "#111111", "#F5E8D0", "#F1BB78", "#E47956", "#CF4640", "#AE3C69", "#7955A3", "#4059A9", "#477AD0", "#4489A0", "#327F79", "#365348", "#748A60", "#ADC58C", "#ECD875"];
            for (var c = 0; c < 8; c++) palette.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            for (var r = 0; r < 2; r++) palette.RowDefinitions.Add(new() { Height = new(23) });
            for (var i = 0; i < colors.Length; i++)
            {
                var color = colors[i]; var button = new StudioButton("", () => { if (!b.CanWrite) return; Surface.FillColor = color; Change("Apply swatch", n => n.Fill = color); })
                { Height = 23, HorizontalAlignment = HorizontalAlignment.Stretch, Padding = new(0), CornerRadius = new(0), RestBackground = color, Background = Studio.Brush(color), BorderThickness = new(1), BorderBrush = Studio.Brush("#252525") };
                AutomationProperties.SetName(button, "Swatch " + color); Grid.SetRow(button, i / 8); Grid.SetColumn(button, i % 8); palette.Children.Add(button);
            }
            body.Children.Add(palette);
        });
        if (Session.Primary is { Strokes.Count: > 0 })
        {
            Inspect("Stroke options", null, (body, b) =>
            {
                body.Children.Add(b.Choice(Enum.GetNames<StrokeCap>(), () => InspectedNode.Strokes[0].Cap.ToString(), value => Change("Stroke cap", n => { foreach (var stroke in n.Strokes) stroke.Cap = Enum.Parse<StrokeCap>(value); }), "Stroke cap"));
                body.Children.Add(b.Choice(Enum.GetNames<StrokeJoin>(), () => InspectedNode.Strokes[0].Join.ToString(), value => Change("Stroke join", n => { foreach (var stroke in n.Strokes) stroke.Join = Enum.Parse<StrokeJoin>(value); }), "Stroke join"));
            });
        }
    }
}
