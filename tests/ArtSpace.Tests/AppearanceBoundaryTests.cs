using ArtSpace.Core;
using ArtSpace.Documents;
using ArtSpace.Editing;
using ArtSpace.Illustration;
using ArtSpace.Skia;
using SkiaSharp;

internal static class AppearanceBoundaryTests
{
    private static void Check(bool value, string message = "Appearance boundary assertion failed")
    { if (!value) throw new InvalidOperationException(message); }
    private static DesignNode Shape(string color = "#D95732") => new() { X = 40, Y = 40, Width = 100, Height = 80, Fill = color };
    private static void Same(SKBitmap first, SKBitmap second) => Check(first.Pixels.SequenceEqual(second.Pixels), "Direct and retained pixels differ.");
    private static EditorSession Editor(params DesignNode[] nodes) => new(new() { Pages = [new() { Nodes = [.. nodes] }] });

    public static void Register(Action<string, Action> test)
    {
        test("resetting full symbol appearance restores source opacity and blend reversibly", () =>
        {
            var source = Shape(); source.Kind = NodeKind.Component; source.Opacity = .8; source.Blend = BlendKind.Multiply;
            var styled = Shape(); styled.Opacity = .3; styled.Blend = BlendKind.Screen; styled.Effects = [new() { Kind = LiveEffectKind.OuterGlow }];
            var editor = Editor(source, styled); editor.Select(styled);
            var style = AppearanceOperations.CaptureStyle(editor);
            var instance = ComponentService.InsertInstance(editor, source, new(150, 0));
            AppearanceOperations.ApplyStyle(editor, style);
            Check(instance.Opacity == .3 && instance.Blend == BlendKind.Screen && instance.Effects.Count == 1);
            ComponentService.ResetOverrides(editor);
            Check(instance.Opacity == .8 && instance.Blend == BlendKind.Multiply && instance.Effects.Count == 0);
            editor.Undo(); Check(editor.Primary!.Opacity == .3 && editor.Primary.Blend == BlendKind.Screen && editor.Primary.Effects.Count == 1);
            editor.Redo(); Check(editor.Primary!.Opacity == .8 && editor.Primary.Blend == BlendKind.Multiply);
        });
        test("locked symbol reset does not discard stored appearance overrides", () =>
        {
            var source = Shape(); source.Kind = NodeKind.Component; var editor = Editor(source);
            var instance = ComponentService.InsertInstance(editor, source, new(150, 0));
            AppearanceOperations.AddEffect(editor, LiveEffectKind.GaussianBlur);
            editor.Edit("Lock", () => instance.Locked = true);
            var history = editor.History.Count; ComponentService.ResetOverrides(editor);
            Check(instance.Effects.Count == 1 && instance.Overrides.Count == 1 && editor.History.Count == history);
        });
        test("child appearance overrides remain isolated from symbol siblings", () =>
        {
            var source = new DesignNode { Kind = NodeKind.Component, Fills = [] };
            source.Add(Shape()); source.Add(Shape("#3366FF"));
            var editor = Editor(source); var instance = ComponentService.InsertInstance(editor, source, new(150, 0));
            var child = instance.Children[0]; var id = child.Id; editor.Select(child);
            AppearanceOperations.AddEffect(editor, LiveEffectKind.GaussianBlur);
            Check(editor.Document.Find(id)!.Effects.Count == 1 && instance.Children[1].Effects.Count == 0);
            editor.Edit("Change source", () => source.Children[0].Width = 121);
            Check(editor.Document.Find(id)!.Effects.Count == 1 && editor.Document.Find(id)!.Width == 121);
            editor.Select(instance); ComponentService.ResetOverrides(editor);
            Check(instance.Children.All(n => n.Effects.Count == 0));
        });
        test("retained scenes preserve alpha luminance inverted and nested mask pixels", () =>
        {
            foreach (var mode in Enum.GetValues<OpacityMaskMode>())
            foreach (var inverted in new[] { false, true })
            {
                var owner = new DesignNode { Kind = NodeKind.Group, Fills = [], Opacity = .7, OpacityMaskMode = mode,
                    OpacityMaskInverted = inverted, OpacityMaskRegion = new(0, 0, 200, 180), Effects = [new() { Kind = LiveEffectKind.GaussianBlur, Radius = 3 }] };
                owner.Add(Shape());
                var mask = new DesignNode { Kind = NodeKind.Group, Fills = [], OpacityMaskMode = OpacityMaskMode.Alpha };
                mask.Add(new() { X = 25, Y = 25, Width = 130, Height = 110, Fill = "#B0B0B0" });
                var nested = mask.Add(new() { Kind = NodeKind.Ellipse, X = 50, Y = 45, Width = 70, Height = 60, Fill = "#FFFFFF", Opacity = .6 });
                mask.OpacityMaskId = nested.Id; owner.Add(mask); owner.OpacityMaskId = mask.Id;
                var page = new DesignPage { Nodes = [owner] }; using var renderer = new SceneRenderer();
                using var direct = LiveAppearanceTests.Pixels(renderer, page);
                using var retained = LiveAppearanceTests.Pixels(renderer, page, true); Same(direct, retained);
                using var zoomed = LiveAppearanceTests.Pixels(renderer, page, false, 1.15f, 7, 3);
                using var replayed = LiveAppearanceTests.Pixels(renderer, page, true, 1.15f, 7, 3); Same(zoomed, replayed);
                Check(renderer.SceneRecordings == 1);
            }
        });
        test("retained clipping holes survive affine transforms and outline-mode switches", () =>
        {
            var owner = new DesignNode { Kind = NodeKind.Group, Fills = [], X = 20, Y = 10,
                AffineTransform = new Matrix2D(1, .1, .2, 1, 0, 0) };
            owner.Add(Shape());
            var clip = owner.Add(new() { Kind = NodeKind.Path, PathData = "M30 30H150V140H30Z M60 60H100V100H60Z",
                PathWidth = 180, PathHeight = 160, Width = 180, Height = 160, FillRule = PathFillRule.EvenOdd, Fills = [] });
            owner.ClipPathId = clip.Id;
            var page = new DesignPage { Nodes = [owner] }; using var renderer = new SceneRenderer();
            using var a = LiveAppearanceTests.Pixels(renderer, page); using var b = LiveAppearanceTests.Pixels(renderer, page, true); Same(a, b);
            renderer.Outlines = true;
            using var c = LiveAppearanceTests.Pixels(renderer, page); using var d = LiveAppearanceTests.Pixels(renderer, page, true); Same(c, d);
            Check(renderer.SceneRecordings == 2);
            renderer.Outlines = false;
            using var e = LiveAppearanceTests.Pixels(renderer, page, true); Same(a, e); Check(renderer.SceneRecordings == 3);
        });
        test("effect ordering changes pixels and direct edits invalidate exact native graphs", () =>
        {
            var shape = Shape(); shape.Effects = [new() { Kind = LiveEffectKind.DropShadow, OffsetX = 20, Color = "#00FF00", Opacity = 1 },
                new() { Kind = LiveEffectKind.Saturation, Amount = 0 }];
            var page = new DesignPage { Nodes = [shape] }; using var renderer = new SceneRenderer();
            using var first = LiveAppearanceTests.Pixels(renderer, page); var builds = renderer.EffectFilterBuilds;
            shape.Effects.Reverse(); using var second = LiveAppearanceTests.Pixels(renderer, page);
            Check(!first.Pixels.SequenceEqual(second.Pixels) && renderer.EffectFilterBuilds == builds + 1);
            shape.Effects[1].Enabled = false; using (LiveAppearanceTests.Pixels(renderer, page)) { }
            Check(renderer.EffectFilterBuilds == builds + 2);
        });
        test("gradient stroke expansion preserves interior paint and object effect ownership", () =>
        {
            var node = Shape(); node.Fills = []; node.Effects = [new() { Kind = LiveEffectKind.Saturation, Amount = .5 }];
            node.Strokes = [new() { Width = 12, Paint = new() { Kind = FillKind.LinearGradient,
                GradientSpace = GradientSpace.ObjectBoundingBox, Start = new(0, 0), End = new(1, 0),
                Stops = [new() { Offset = 0, Color = "#FF0000" }, new() { Offset = 1, Color = "#0000FF" }] } }];
            var editor = Editor(node); editor.Select(node); using var renderer = new SceneRenderer();
            using var before = LiveAppearanceTests.Pixels(renderer, editor.Page);
            IllustrationOperations.OutlineStrokes(editor, renderer);
            var group = editor.Primary!;
            Check(group.Kind == NodeKind.Group && group.Effects.Count == 1 && group.Children.All(n => n.Effects.Count == 0));
            using var after = LiveAppearanceTests.Pixels(renderer, editor.Page);
            foreach (var x in new[] { 50, 80, 110, 130 })
            {
                var a = before.GetPixel(x, 40); var b = after.GetPixel(x, 40);
                Check(a.Alpha == b.Alpha && Math.Abs(a.Red - b.Red) <= 1 && Math.Abs(a.Blue - b.Blue) <= 1 && Math.Abs(a.Green - b.Green) <= 1,
                    $"Expanded gradient pixel {x}: {a} versus {b}.");
            }
            editor.Undo(); Check(editor.Primary!.Kind == NodeKind.Rectangle && editor.Primary.Strokes[0].Paint is not null);
        });
        test("appearance caches observe same-id replacement effects and gradient stops", () =>
        {
            var original = Shape(); original.Effects = [new() { Kind = LiveEffectKind.OuterGlow, Color = "#FF0000" }];
            original.Strokes = [new() { Width = 10, Paint = new() { Kind = FillKind.LinearGradient } }];
            var page = new DesignPage { Nodes = [original] }; using var renderer = new SceneRenderer();
            using var before = LiveAppearanceTests.Pixels(renderer, page, true); var builds = renderer.EffectFilterBuilds;
            var replacement = DocumentJson.CloneNode(original); replacement.Effects[0].Color = "#00FF00";
            replacement.Strokes[0].Paint!.Stops[0].Color = "#FFFF00";
            page.Nodes = [replacement]; renderer.PruneCache(page.Nodes);
            using var after = LiveAppearanceTests.Pixels(renderer, page, true);
            Check(renderer.EffectFilterBuilds == builds + 1 && !before.Pixels.SequenceEqual(after.Pixels));
        });
    }
}
