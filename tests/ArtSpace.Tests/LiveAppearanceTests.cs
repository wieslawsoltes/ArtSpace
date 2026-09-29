using ArtSpace.Core;
using ArtSpace.Documents;
using ArtSpace.Editing;
using ArtSpace.Illustration;
using ArtSpace.Skia;
using SkiaSharp;

internal static class LiveAppearanceTests
{
    private static void Check(bool value, string message = "Appearance assertion failed")
    { if (!value) throw new InvalidOperationException(message); }
    private static void Throws(Action action)
    { try { action(); } catch (Exception ex) when (ex is InvalidDataException or ArgumentException or InvalidOperationException) { return; } throw new Exception("Expected a rejected operation."); }
    private static DesignNode Node() => new() { X = 48, Y = 48, Width = 80, Height = 70, Fill = "#E84020" };
    private static EditorSession Editor(params DesignNode[] nodes) => new(new() { Pages = [new() { Nodes = [.. nodes] }] });
    private static FillStyle Gradient() => new()
    {
        Kind = FillKind.LinearGradient, Start = new(0, 0), End = new(1, 0),
        Stops = [new() { Offset = 0, Color = "#FF0000" }, new() { Offset = 1, Color = "#0000FF" }]
    };
    internal static SKBitmap Pixels(SceneRenderer renderer, DesignPage page, bool retained = false, float zoom = 1, float panX = 0, float panY = 0)
    {
        var bitmap = new SKBitmap(new SKImageInfo(240, 200, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent); canvas.Translate(panX, panY); canvas.Scale(zoom);
        var view = new RectD(-panX / zoom, -panY / zoom, 240 / zoom, 200 / zoom);
        if (retained) renderer.DrawRetained(canvas, page, view); else renderer.Draw(canvas, page.Nodes, view);
        canvas.Flush(); return bitmap;
    }
    private static void Same(SKBitmap a, SKBitmap b, int tolerance = 0)
    {
        var x = a.Pixels; var y = b.Pixels;
        Check(x.Length == y.Length);
        for (var i = 0; i < x.Length; i++)
        {
            if (Math.Abs(x[i].Alpha - y[i].Alpha) > tolerance || Math.Abs(x[i].Red - y[i].Red) > tolerance
                || Math.Abs(x[i].Green - y[i].Green) > tolerance || Math.Abs(x[i].Blue - y[i].Blue) > tolerance)
                throw new InvalidOperationException($"Pixel {i}: {x[i]} != {y[i]}.");
        }
    }

    public static void Register(Action<string, Action> test)
    {
        test("live effects retain geometry and form atomic undo transactions", () =>
        {
            var n = Node(); var e = Editor(n); e.Select(n); var id = n.Id;
            AppearanceOperations.AddEffect(e, LiveEffectKind.GaussianBlur);
            Check(e.History.Count == 1 && n.Effects.Count == 1 && n.Kind == NodeKind.Rectangle && n.X == 48);
            e.Undo(); Check(e.Document.Find(id)!.Effects.Count == 0);
            e.Redo(); Check(e.Document.Find(id)!.Effects.Single().Kind == LiveEffectKind.GaussianBlur);
        });
        test("live-effect edits duplicate independently and reorder reversibly", () =>
        {
            var n = Node(); var e = Editor(n); e.Select(n);
            AppearanceOperations.AddEffect(e, LiveEffectKind.GaussianBlur);
            AppearanceOperations.DuplicateEffect(e, 0);
            AppearanceOperations.UpdateEffect(e, 1, "Radius", fx => fx.Radius = 17);
            Check(n.Effects[0].Radius == 6 && n.Effects[1].Radius == 17);
            AppearanceOperations.MoveEffect(e, 1, -1); Check(n.Effects[0].Radius == 17);
            AppearanceOperations.RemoveEffect(e, 0); Check(n.Effects.Single().Radius == 6);
            e.Undo(); Check(e.Primary!.Effects.Count == 2 && e.Primary.Effects[0].Radius == 17);
        });
        test("invalid live effects roll back instead of poisoning the document", () =>
        {
            var n = Node(); var e = Editor(n); e.Select(n); AppearanceOperations.AddEffect(e, LiveEffectKind.GaussianBlur);
            var history = e.History.Count;
            Throws(() => AppearanceOperations.UpdateEffect(e, 0, "Invalid", fx => fx.Radius = double.NaN));
            Check(e.History.Count == history && e.Primary!.Effects[0].Radius == 6 && !e.IsInteracting);
        });
        test("live-effect limits and enum validation reject malformed inputs", () =>
        {
            var n = Node(); var e = Editor(n); e.Select(n);
            Throws(() => AppearanceOperations.AddEffect(e, (LiveEffectKind)123));
            n.Effects = Enumerable.Range(0, DocumentJson.MaxEffectsPerNode + 1).Select(_ => new LiveEffect()).ToList();
            Throws(() => DocumentJson.Validate(e.Document));
            n.Effects = [new() { OffsetX = double.PositiveInfinity }]; Throws(() => DocumentJson.Validate(e.Document));
            n.Effects = [new() { Amount = 5 }]; Throws(() => DocumentJson.Validate(e.Document));
        });
        test("graphic styles deep copy paints dashes and effects without changing geometry", () =>
        {
            var source = Node(); source.Fills = [Gradient()]; source.Strokes = [new() { Dashes = [4, 2], Paint = Gradient() }];
            source.Effects = [new() { Kind = LiveEffectKind.DropShadow }];
            var target = Node(); target.X = 137; target.Kind = NodeKind.Ellipse;
            var e = Editor(source, target); e.Select(source); var id = AppearanceOperations.CaptureStyle(e, "Copper");
            e.Select(target); AppearanceOperations.ApplyStyle(e, id);
            Check(target.X == 137 && target.Kind == NodeKind.Ellipse && target.Effects.Count == 1);
            target.Fills[0].Stops[0].Color = "#00FF00"; target.Strokes[0].Dashes[0] = 99; target.Effects[0].Radius = 40;
            Check(source.Fills[0].Stops[0].Color == "#FF0000" && source.Strokes[0].Dashes[0] == 4);
            Check(e.Document.GraphicStyles[0].Effects[0].Radius == 6 && source.Effects[0].Radius == 6);
            Check(!ReferenceEquals(target.Strokes[0].Paint, e.Document.GraphicStyles[0].Strokes[0].Paint));
        });
        test("graphic styles preserve mask sources children and object identifiers", () =>
        {
            var group = new DesignNode { Kind = NodeKind.Group }; var child = group.Add(Node()); var mask = group.Add(Node());
            group.ClipPathId = mask.Id;
            var style = GraphicStyle.Capture(Node(), "Style"); var id = group.Id; style.ApplyTo(group);
            Check(group.Id == id && group.ClipPathId == mask.Id && ReferenceEquals(group.Children[0], child) && group.Children.Count == 2);
        });
        test("style creation rename deletion and application participate in undo", () =>
        {
            var n = Node(); var e = Editor(n); e.Select(n);
            var id = AppearanceOperations.CaptureStyle(e, "One"); AppearanceOperations.RenameStyle(e, id, "Two");
            Check(e.Document.GraphicStyles.Single().Name == "Two");
            AppearanceOperations.DeleteStyle(e, id); Check(e.Document.GraphicStyles.Count == 0);
            e.Undo(); Check(e.Document.GraphicStyles.Single().Name == "Two");
            e.Undo(); Check(e.Document.GraphicStyles.Single().Name == "One");
            e.Undo(); Check(e.Document.GraphicStyles.Count == 0);
        });
        test("locked artwork is not changed by style or effect commands", () =>
        {
            var n = Node(); var e = Editor(n); e.Select(n); var id = AppearanceOperations.CaptureStyle(e);
            n.Locked = true;
            AppearanceOperations.AddEffect(e, LiveEffectKind.GaussianBlur); AppearanceOperations.ApplyStyle(e, id);
            Check(n.Effects.Count == 0 && n.Locked);
        });
        test("schema four retains styles live effects gradient strokes and phase", () =>
        {
            var n = Node(); n.Effects = [new() { Kind = LiveEffectKind.OuterGlow, Radius = 14 }];
            n.Strokes = [new() { Paint = Gradient(), DashOffset = -3.5, Dashes = [4] }];
            var e = Editor(n); e.Select(n); AppearanceOperations.CaptureStyle(e, "Glow");
            var loaded = DocumentJson.Load(DocumentJson.Save(e.Document));
            Check(loaded.FormatVersion == 4 && loaded.GraphicStyles.Count == 1);
            Check(loaded.Pages[0].Nodes[0].Strokes[0].DashOffset == -3.5 && loaded.GraphicStyles[0].Effects[0].Radius == 14);
        });
        test("legacy schemas upgrade while future appearance schemas fail closed", () =>
        {
            var json = DocumentJson.Save(new());
            foreach (var version in new[] { 1, 2, 3 })
            {
                var legacy = DocumentJson.Load(json.Replace("\"formatVersion\":4", "\"formatVersion\":" + version));
                Check(DocumentJson.Load(DocumentJson.Save(legacy)).FormatVersion == 4);
            }
            Throws(() => DocumentJson.Load(json.Replace("\"formatVersion\":4", "\"formatVersion\":99")));
        });
        test("malformed graphic styles are validated even when unused", () =>
        {
            var doc = new DesignDocument(); var style = GraphicStyle.Capture(Node(), "Bad"); doc.GraphicStyles.Add(style);
            style.Effects.Add(new() { Radius = -1 }); Throws(() => DocumentJson.Validate(doc));
            style.Effects.Clear(); doc.GraphicStyles.Add(style); Throws(() => DocumentJson.Validate(doc));
        });
        test("symbol effect overrides survive source synchronization and reset", () =>
        {
            var component = new DesignNode { Kind = NodeKind.Component, Fills = [] }; component.Add(Node());
            var e = Editor(component); var instance = ComponentService.InsertInstance(e, component, new(140, 0));
            e.Select(instance); AppearanceOperations.AddEffect(e, LiveEffectKind.GaussianBlur);
            Check(instance.Effects.Count == 1);
            e.Edit("Source edit", () => component.Name = "Renamed"); Check(instance.Effects.Count == 1);
            ComponentService.ResetOverrides(e); Check(e.Primary!.Effects.Count == 0);
        });
        test("Gaussian blur is non-destructive and grows coverage", () =>
        {
            var n = Node(); n.Effects = [new() { Kind = LiveEffectKind.GaussianBlur, Radius = 5 }];
            using var r = new SceneRenderer(); using var p = Pixels(r, new() { Nodes = [n] });
            Check(p.GetPixel(43, 80).Alpha > 0 && p.GetPixel(90, 80).Alpha > 240 && n.Kind == NodeKind.Rectangle);
        });
        test("saturation preserves alpha and produces neutral grayscale", () =>
        {
            var n = Node(); n.Opacity = .5; n.Effects = [new() { Kind = LiveEffectKind.Saturation, Amount = 0 }];
            using var r = new SceneRenderer(); using var p = Pixels(r, new() { Nodes = [n] }); var color = p.GetPixel(90, 80);
            Check(Math.Abs(color.Red - color.Green) <= 1 && Math.Abs(color.Green - color.Blue) <= 1 && color.Alpha is >= 126 and <= 128);
        });
        test("warm live filters survive placement and invalidate exact parameter edits", () =>
        {
            var n = Node(); n.Effects = [new() { Kind = LiveEffectKind.OuterGlow }]; var page = new DesignPage { Nodes = [n] };
            using var r = new SceneRenderer(); using (Pixels(r, page)) { }
            var builds = r.EffectFilterBuilds; n.X += 4; using (Pixels(r, page)) { }
            Check(r.EffectFilterBuilds == builds && r.EffectFilterHits > 0);
            n.Effects[0].Radius += 1; using (Pixels(r, page)) { } Check(r.EffectFilterBuilds == builds + 1);
        });
        test("disabled effects produce the unfiltered reference pixels", () =>
        {
            var n = Node(); var page = new DesignPage { Nodes = [n] }; using var r = new SceneRenderer();
            using var a = Pixels(r, page); n.Effects = [new() { Enabled = false, Radius = 100 }];
            using var b = Pixels(r, page); Same(a, b);
        });
        test("effect-source culling keeps offscreen shadow contributions", () =>
        {
            var n = Node(); n.X = -100; n.Effects = [new() { Kind = LiveEffectKind.DropShadow, OffsetX = 100, Radius = 3, Opacity = 1 }];
            var page = new DesignPage { Nodes = [n] }; using var r = new SceneRenderer(); using var a = Pixels(r, page);
            r.EnableCulling = false; using var b = Pixels(r, page); Same(a, b); Check(a.GetPixel(30, 90).Alpha > 0);
        });
        test("gradient stroke renders endpoint colors with composed opacity", () =>
        {
            var n = Node(); n.Fills = []; n.Strokes = [new() { Width = 12, Opacity = .5, Paint = Gradient() }];
            n.Strokes[0].Paint!.Opacity = .5;
            using var r = new SceneRenderer(); using var p = Pixels(r, new() { Nodes = [n] });
            var left = p.GetPixel(53, 48); var right = p.GetPixel(122, 48);
            Check(left.Red > left.Blue && right.Blue > right.Red && left.Alpha is >= 62 and <= 65);
        });
        test("odd dash arrays repeat and dash offsets alter coverage", () =>
        {
            var n = Node(); n.Fills = []; var stroke = new StrokeStyle { Width = 5, Cap = StrokeCap.Butt, Dashes = [8] }; n.Strokes = [stroke];
            var page = new DesignPage { Nodes = [n] }; using var r = new SceneRenderer(); using var a = Pixels(r, page);
            stroke.Dashes = [8, 8]; using var b = Pixels(r, page); Same(a, b);
            stroke.DashOffset = 8; using var c = Pixels(r, page); Check(!a.Pixels.SequenceEqual(c.Pixels));
        });
        test("warm paints gradients and dashes are retained without native rebuilds", () =>
        {
            var n = Node(); n.Fills = [Gradient()]; n.Strokes = [new() { Width = 4, Dashes = [7, 3], Paint = Gradient() }];
            var page = new DesignPage { Nodes = [n] }; using var r = new SceneRenderer(); using (Pixels(r, page)) { }
            var paints = r.PaintBuilds; var gradients = r.GradientBuilds; var dashes = r.DashBuilds;
            for (var i = 0; i < 5; i++) { n.X++; using (Pixels(r, page)) { } }
            Check(r.PaintBuilds == paints && r.GradientBuilds == gradients && r.DashBuilds == dashes);
            n.Strokes[0].DashOffset++; using (Pixels(r, page)) { } Check(r.DashBuilds == dashes + 1);
        });
        test("retained native scene agrees with direct drawing and records only once", () =>
        {
            var n = Node(); n.Fills = [Gradient()]; n.Effects = [new() { Kind = LiveEffectKind.DropShadow }];
            var page = new DesignPage { Nodes = [n] }; using var r = new SceneRenderer(); using var direct = Pixels(r, page);
            using var retained = Pixels(r, page, true); Same(direct, retained);
            for (var i = 0; i < 5; i++) using (Pixels(r, page, true)) { }
            Check(r.SceneRecordings == 1 && r.SceneReplays == 6 && r.RetainedSceneBytes > 0);
        });
        test("retained scene handles pan zoom and out-of-coverage rerecording", () =>
        {
            var n = Node(); n.Effects = [new() { Kind = LiveEffectKind.GaussianBlur, Radius = 3 }];
            var page = new DesignPage { Nodes = [n] }; using var r = new SceneRenderer(); using (Pixels(r, page, true)) { }
            using var a = Pixels(r, page, false, 1.2f, 12, 10); using var b = Pixels(r, page, true, 1.2f, 12, 10); Same(a, b);
            Check(r.SceneRecordings == 1);
            using (Pixels(r, page, true, 1, 800, 0)) { } Check(r.SceneRecordings == 2);
        });
        test("retained scene invalidation reflects edits and replacement model instances", () =>
        {
            var n = Node(); var page = new DesignPage { Nodes = [n] }; using var r = new SceneRenderer(); using (Pixels(r, page, true)) { }
            n.Fill = "#00FF00"; r.InvalidateRetainedScene(); using var a = Pixels(r, page, true);
            Check(a.GetPixel(90, 80).Green > 240 && r.SceneRecordings == 2);
            var copy = DocumentJson.CloneNode(n); copy.Fill = "#0000FF"; page.Nodes = [copy];
            using var b = Pixels(r, page, true); Check(b.GetPixel(90, 80).Blue > 240 && r.SceneRecordings == 3);
        });
        test("retained scene budget fallback does not repeatedly record rejected content", () =>
        {
            var page = new DesignPage { Nodes = [Node()] }; using var r = new SceneRenderer { RetainedSceneBudgetBytes = 1 };
            using var a = Pixels(r, page, true); using var b = Pixels(r, page, true); Same(a, b);
            Check(r.SceneRecordings == 1 && r.SceneReplays == 0 && r.RetainedSceneBytes == 0);
        });
        test("native appearance caches and pictures release on pruning and clear", () =>
        {
            var n = Node(); n.Fills = [Gradient()]; n.Strokes = [new() { Paint = Gradient(), Dashes = [3] }]; n.Effects = [new()];
            var page = new DesignPage { Nodes = [n] }; using var r = new SceneRenderer(); using (Pixels(r, page, true)) { }
            r.PruneCache([]); Check(r.CachedPaintCount == 0 && r.CachedEffectFilterCount == 0 && r.RetainedSceneBytes == 0);
            using (Pixels(r, page, true)) { } r.ClearCache(); Check(r.CachedPaintCount == 0 && r.CachedEffectFilterCount == 0 && r.RetainedSceneBytes == 0);
        });
        test("SVG gradient strokes preserve phase paint and stop opacity", () =>
        {
            var imported = SvgFormat.Import("<svg xmlns='http://www.w3.org/2000/svg' width='200' height='150'><defs><linearGradient id='g'><stop stop-color='#FF0000' stop-opacity='.4'/><stop offset='1' stop-color='#0000FF'/></linearGradient></defs><rect x='20' y='20' width='100' height='60' fill='none' stroke='url(#g)' stroke-width='8' stroke-dasharray='5' stroke-dashoffset='-2'/></svg>").Document;
            var stroke = imported.AllNodes().First(n => n.Strokes.Count > 0).Strokes[0];
            Check(stroke.Paint?.Kind == FillKind.LinearGradient && stroke.Paint.Stops[0].Opacity == .4 && stroke.DashOffset == -2);
            var output = SvgFormat.Export(imported.Pages[0].Nodes, new(0, 0, 200, 150));
            var again = SvgFormat.Import(output).Document.AllNodes().First(n => n.Strokes.Count > 0).Strokes[0];
            Check(again.Paint?.Stops.Count == 2 && again.DashOffset == -2 && again.Dashes.Single() == 5);
        });
        test("SVG export rejects unsupported live effects instead of dropping appearance", () =>
        {
            var n = Node(); n.Effects = [new()]; Throws(() => SvgFormat.Export([n], new(0, 0, 240, 200)));
            n.Effects[0].Enabled = false; Check(SvgFormat.Export([n], new(0, 0, 240, 200)).Contains("svg"));
        });
        test("all added blend modes serialize and map to native compositing", () =>
        {
            foreach (var blend in Enum.GetValues<BlendKind>())
            {
                var n = Node(); n.Blend = blend; using var r = new SceneRenderer();
                using var image = Pixels(r, new() { Nodes = [new() { Width = 200, Height = 180, Fill = "#667799" }, n] });
                Check(image.GetPixel(80, 80).Alpha == 255);
                var e = Editor(n); Check(DocumentJson.Load(DocumentJson.Save(e.Document)).Pages[0].Nodes[0].Blend == blend);
            }
        });
    }
}
