using ArtSpace.Core;
using ArtSpace.Skia;
using SkiaSharp;

internal static class SceneSpatialIndexTests
{
    private static DesignNode Rectangle(double x, double y, string fill = "#E7A459") => new()
    { X = x, Y = y, Width = 60, Height = 50, Fill = fill };

    private static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }

    private static void Same(SKBitmap expected, SKBitmap actual)
    {
        var a = expected.Pixels; var b = actual.Pixels;
        Check(a.Length == b.Length, "Image dimensions differ.");
        for (var i = 0; i < a.Length; i++)
            if (a[i] != b[i]) throw new InvalidOperationException($"Indexed pixel {i}: {b[i]} != {a[i]}.");
    }

    private static void Equivalent(DesignPage page, float zoom = 1, float panX = 0, float panY = 0)
    {
        using var direct = new SceneRenderer();
        using var indexed = new SceneRenderer { EnableSceneSpatialIndex = true };
        using var linear = new SceneRenderer { EnableSceneSpatialIndex = false };
        using var a = LiveAppearanceTests.Pixels(direct, page, false, zoom, panX, panY);
        using var b = LiveAppearanceTests.Pixels(indexed, page, true, zoom, panX, panY);
        using var c = LiveAppearanceTests.Pixels(linear, page, true, zoom, panX, panY);
        Same(a, b); Same(a, c);
    }

    public static void Register(Action<string, Action> test)
    {
        test("indexed scene matches direct and linear replay with overflowing groups", () =>
        {
            var group = new DesignNode { Kind = NodeKind.Group, Width = 1, Height = 1, Fills = [] };
            for (var i = 0; i < 300; i++) group.Add(Rectangle((i % 30) * 45 - 30, (i / 30) * 42));
            group.Children[2].Rotation = 27;
            group.Children[2].Strokes = [new() { Color = "#365461", Width = 13, Dashes = [3, 5, 7], DashOffset = -4 }];
            Equivalent(new() { Nodes = [group] });
        });
        test("indexed scene retains offscreen input casting a visible live shadow", () =>
        {
            var source = Rectangle(-180, 60);
            source.Effects = [new() { Kind = LiveEffectKind.DropShadow, OffsetX = 230, OffsetY = 0, Radius = 9, Opacity = 1 }];
            var page = new DesignPage { Nodes = [source] };
            Equivalent(page);
            using var renderer = new SceneRenderer();
            using var image = LiveAppearanceTests.Pixels(renderer, page, true);
            Check(image.GetPixel(75, 85).Alpha > 0, "The offscreen source must produce visible shadow ink.");
        });
        test("indexed scene matches composed luminance and vector clipping", () =>
        {
            var owner = new DesignNode { Kind = NodeKind.Group, Width = 180, Height = 160, X = 20, Y = 15, Fills = [], Opacity = .7 };
            owner.Add(new() { Width = 220, Height = 170, Fill = "#BE5528" });
            var clip = owner.Add(new() { Kind = NodeKind.Ellipse, X = 4, Y = 7, Width = 155, Height = 135, Fills = [] });
            var mask = owner.Add(new() { Kind = NodeKind.Group, Fills = [] });
            mask.Add(Rectangle(0, 0, "#FFFFFF")); mask.Add(Rectangle(45, 35, "#7F7F7F"));
            owner.ClipPathId = clip.Id; owner.OpacityMaskId = mask.Id;
            owner.OpacityMaskMode = OpacityMaskMode.Luminance;
            Equivalent(new() { Nodes = [owner] });
            owner.OpacityMaskInverted = true;
            Equivalent(new() { Nodes = [owner] }, .8f, 8, 12);
        });
        test("indexed scene matches direct replay after zoom and clip changes", () =>
        {
            var page = new DesignPage { Nodes = [Rectangle(40, 35), Rectangle(130, 85, "#3494A9")] };
            page.Nodes[1].Effects = [new() { Kind = LiveEffectKind.GaussianBlur, Radius = 5 }];
            using var renderer = new SceneRenderer();
            using var initial = LiveAppearanceTests.Pixels(renderer, page, true);
            foreach (var zoom in new[] { .7f, 1.2f, 2f })
            {
                using var expected = LiveAppearanceTests.Pixels(renderer, page, false, zoom, -11, 4);
                using var actual = LiveAppearanceTests.Pixels(renderer, page, true, zoom, -11, 4);
                Same(expected, actual);
            }
        });
        test("raising retained budget retries rejected scene without a model mutation", () =>
        {
            var page = new DesignPage { Nodes = [Rectangle(10, 10)] };
            using var renderer = new SceneRenderer { RetainedSceneBudgetBytes = 1 };
            using var rejected = LiveAppearanceTests.Pixels(renderer, page, true);
            var recordings = renderer.SceneRecordings;
            Check(recordings == 1 && renderer.SceneReplays == 0 && renderer.RetainedSceneBytes == 0, "Budget rejection must fall back.");
            using var retrySuppressed = LiveAppearanceTests.Pixels(renderer, page, true);
            Check(renderer.SceneRecordings == recordings, "Unchanged rejected scenes must not repeatedly record.");
            renderer.RetainedSceneBudgetBytes = 1024 * 1024;
            using var accepted = LiveAppearanceTests.Pixels(renderer, page, true);
            Same(rejected, accepted);
            Check(renderer.SceneRecordings == recordings + 1 && renderer.SceneReplays == 1, "Raised budget must permit a retry.");
        });
        test("disabling retention releases native commands immediately", () =>
        {
            using var renderer = new SceneRenderer();
            var page = new DesignPage { Nodes = [Rectangle(10, 10)] };
            using var retained = LiveAppearanceTests.Pixels(renderer, page, true);
            Check(renderer.RetainedSceneBytes > 0, "Fixture must record commands.");
            renderer.EnableRetainedScene = false;
            Check(renderer.RetainedSceneBytes == 0, "Disabled scene commands must be released.");
            using var direct = LiveAppearanceTests.Pixels(renderer, page, true);
            Same(retained, direct);
            Check(renderer.SceneRecordings == 1, "Disabled retention must not record.");
        });
        test("spatial policy change rerecords once while equal settings reuse cache", () =>
        {
            using var renderer = new SceneRenderer();
            var page = new DesignPage { Nodes = [Rectangle(20, 30)] };
            using var first = LiveAppearanceTests.Pixels(renderer, page, true);
            renderer.EnableSceneSpatialIndex = true;
            renderer.EnableRetainedScene = true;
            renderer.RetainedSceneBudgetBytes = renderer.RetainedSceneBudgetBytes;
            using var equal = LiveAppearanceTests.Pixels(renderer, page, true);
            Check(renderer.SceneRecordings == 1, "Equal settings must not invalidate.");
            renderer.EnableSceneSpatialIndex = false;
            Check(renderer.RetainedSceneBytes == 0, "Policy changes must release incompatible commands.");
            using var linear = LiveAppearanceTests.Pixels(renderer, page, true);
            Same(first, linear);
            Check(renderer.SceneRecordings == 2, "Changed indexing mode must record exactly once.");
        });
        test("zero retained budget disables recording and can be reenabled", () =>
        {
            using var renderer = new SceneRenderer { RetainedSceneBudgetBytes = 0 };
            var page = new DesignPage { Nodes = [Rectangle(20, 30)] };
            using var initial = LiveAppearanceTests.Pixels(renderer, page, true);
            Check(renderer.SceneRecordings == 0, "Zero budget must bypass recording.");
            renderer.RetainedSceneBudgetBytes = 1024 * 1024;
            using var enabled = LiveAppearanceTests.Pixels(renderer, page, true);
            Same(initial, enabled);
            Check(renderer.SceneRecordings == 1 && renderer.RetainedSceneBytes > 0, "A restored budget must enable retention.");
        });
        test("indexed replay preserves caller canvas transform and save stack", () =>
        {
            using var renderer = new SceneRenderer();
            using var bitmap = new SKBitmap(240, 200);
            using var canvas = new SKCanvas(bitmap);
            canvas.Save(); canvas.Translate(15, 21); canvas.Scale(.75f);
            canvas.ClipRect(new(0, 0, 180, 160));
            var matrix = canvas.TotalMatrix; var count = canvas.SaveCount; var clip = canvas.DeviceClipBounds;
            var page = new DesignPage { Nodes = [Rectangle(20, 30)] };
            renderer.DrawRetained(canvas, page, new(-20, -28, 320, 267));
            renderer.DrawRetained(canvas, page, new(-20, -28, 320, 267));
            Check(canvas.TotalMatrix.Equals(matrix) && canvas.SaveCount == count && canvas.DeviceClipBounds == clip,
                "Recording and replay must preserve caller-owned canvas state.");
        });
    }
}
