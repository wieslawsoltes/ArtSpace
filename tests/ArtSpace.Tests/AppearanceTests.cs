using ArtSpace.Core;
using ArtSpace.Documents;
using ArtSpace.Editing;
using ArtSpace.Illustration;
using ArtSpace.Skia;
using SkiaSharp;

internal static class AppearanceTests
{
    private static void Check(bool value) { if (!value) throw new Exception("Appearance assertion failed."); }
    private static void Near(double actual, double expected, double tolerance = 1e-5) { if (Math.Abs(actual - expected) > tolerance) throw new Exception($"Expected {expected}, got {actual}."); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or ArgumentException) { return; }
        throw new Exception("Expected invalid input to be rejected.");
    }
    private static DesignNode Rect(string color = "#FF4000", double x = 0, double y = 0, double w = 100, double h = 100) => new() { X = x, Y = y, Width = w, Height = h, Fill = color };
    private static EditorSession Editor(params DesignNode[] roots) => new(new() { Pages = [new() { Nodes = [.. roots] }] });
    private static EditorSession Mask(string color, double opacity = 1, OpacityMaskMode mode = OpacityMaskMode.Luminance)
    {
        var source = Rect(color, 20, 20, 60, 60); source.Opacity = opacity;
        var editor = Editor(Rect(), source); editor.SelectAll(); OpacityMaskOperations.Make(editor, mode); return editor;
    }
    private static SKBitmap Render(SceneRenderer renderer, IEnumerable<DesignNode> roots) => SKBitmap.Decode(renderer.ExportPng(roots, new(0, 0, 160, 160)));
    private static byte Alpha(EditorSession editor, int x = 50, int y = 50)
    {
        using var renderer = new SceneRenderer(); using var bitmap = Render(renderer, editor.Page.Nodes); return bitmap.GetPixel(x, y).Alpha;
    }
    private static void PixelEquivalent(IEnumerable<DesignNode> a, IEnumerable<DesignNode> b, int tolerance = 2)
    {
        using var renderer = new SceneRenderer(); using var x = Render(renderer, a); using var y = Render(renderer, b);
        for (var j = 0; j < x.Height; j++) for (var i = 0; i < x.Width; i++)
        {
            var p = x.GetPixel(i, j); var q = y.GetPixel(i, j);
            if (Math.Abs(p.Alpha - q.Alpha) > tolerance || (p.Alpha > 10 && (Math.Abs(p.Red - q.Red) > tolerance || Math.Abs(p.Green - q.Green) > tolerance || Math.Abs(p.Blue - q.Blue) > tolerance)))
                throw new Exception($"Pixels differ at {i},{j}: {p} versus {q}.");
        }
    }
    public static void Register(Action<string, Action> test)
    {
        test("alpha masks use source alpha rather than black color", () => { var e = Mask("#000000", .5, OpacityMaskMode.Alpha); Near(Alpha(e), 127, 1); Check(Alpha(e, 5, 5) == 0); });
        test("luminance masks multiply source alpha exactly once", () => { var e = Mask("#808080", .5); Near(Alpha(e), 64, 1); });
        test("opaque black luminance mask conceals artwork", () => Check(Alpha(Mask("#000000")) == 0));
        test("opaque white luminance mask reveals artwork", () => Check(Alpha(Mask("#FFFFFF")) == 255));
        test("colored luminance follows sRGB luma coefficients", () => { Near(Alpha(Mask("#FF0000")), 54, 1); Near(Alpha(Mask("#00FF00")), 182, 1); });
        test("transparent alpha source clears entire isolated artwork", () => { var e = Mask("#FFFFFF", 0, OpacityMaskMode.Alpha); Check(Alpha(e) == 0 && Alpha(e, 5, 5) == 0); });
        test("inverted masks complement coverage including empty source", () => { var e = Mask("#808080", .5); OpacityMaskOperations.Invert(e); Near(Alpha(e), 191, 1); Check(Alpha(e, 5, 5) == 255); });
        test("mask regions constrain normal and inverted output", () => { var e = Mask("#FFFFFF"); e.Primary!.OpacityMaskRegion = new(30, 30, 40, 40); Check(Alpha(e, 25, 25) == 0 && Alpha(e) == 255); OpacityMaskOperations.Invert(e); Check(Alpha(e, 5, 5) == 0); });
        test("disabled masks hide source artwork but reveal unclipped content", () => { var e = Mask("#000000"); OpacityMaskOperations.ToggleEnabled(e); Check(Alpha(e, 5, 5) == 255 && Alpha(e) == 255); using var r = new SceneRenderer(); using var b = Render(r, e.Page.Nodes); Check(b.GetPixel(50, 50).Red == 255); });
        test("mask source is composed before luminance conversion", () =>
        {
            var source = new DesignNode { Kind = NodeKind.Group, Fills = [] };
            source.Add(Rect("#FFFFFF")); var black = source.Add(Rect("#000000")); black.Opacity = .5;
            var e = Editor(Rect(), source); e.SelectAll(); OpacityMaskOperations.Make(e); Near(Alpha(e), 128, 1);
        });
        test("nested alpha masks multiply without leaking siblings", () =>
        {
            var inner = Mask("#FFFFFF", .5, OpacityMaskMode.Alpha).Primary!;
            var source = Rect("#FFFFFF"); source.Opacity = .5; var e = Editor(inner, source); e.SelectAll(); OpacityMaskOperations.Make(e, OpacityMaskMode.Alpha);
            Near(Alpha(e), 63, 2); Check(Alpha(e, 5, 5) == 0);
        });
        test("mask owner opacity is applied once after mask composition", () => { var e = Mask("#FFFFFF", .5, OpacityMaskMode.Alpha); e.Primary!.Opacity = .5; Near(Alpha(e), 63, 2); });
        test("mask commands preserve source identity and appearance with undo", () =>
        {
            var source = Rect("#123456"); source.Opacity = .4; var id = source.Id;
            var e = Editor(Rect(), source); e.SelectAll(); OpacityMaskOperations.Make(e); Check(e.Primary!.OpacityMask == source && source.Fill == "#123456");
            e.Undo(); Check(e.Page.Nodes[1].Id == id && e.Page.Nodes[1].Opacity == .4); e.Redo(); Check(e.Primary!.OpacityMaskId == id);
        });
        test("mask creation keeps rotated parent coordinates", () =>
        {
            var parent = new DesignNode { Kind = NodeKind.Group, X = 30, Y = 50, Rotation = 31, Fills = [] };
            var a = parent.Add(Rect()); var mask = parent.Add(Rect("#FFFFFF", 20, 20, 60, 60)); var matrix = a.WorldMatrix;
            var e = Editor(parent); e.Select([a.Id, mask.Id]); OpacityMaskOperations.Make(e); Near(a.WorldMatrix.DX, matrix.DX); Near(a.WorldMatrix.DY, matrix.DY);
        });
        test("release retains former source and group with reversible reference", () => { var e = Mask("#FFFFFF"); var g = e.Primary!; var id = g.OpacityMaskId; OpacityMaskOperations.Release(e); Check(g.Children.Count == 2 && g.OpacityMaskId is null); e.Undo(); Check(e.Primary!.OpacityMaskId == id); });
        test("mask edit selects source or artwork independently", () => { var e = Mask("#FFFFFF"); var g = e.Primary!; OpacityMaskOperations.EditMask(e); Check(e.Primary == g.OpacityMask); OpacityMaskOperations.EditContents(e); Check(e.Primary == g.Children[0]); });
        test("schema 4 mask settings survive save load", () => { var e = Mask("#808080"); OpacityMaskOperations.Invert(e); var d = DocumentJson.Load(DocumentJson.Save(e.Document)); Check(d.FormatVersion == DocumentJson.CurrentFormatVersion && d.Pages[0].Nodes[0].OpacityMaskInverted && d.Pages[0].Nodes[0].OpacityMask is not null); });
        test("mask source identifiers regenerate with clipboard and cloning", () => { var g = Mask("#FFFFFF").Primary!; var a = DocumentJson.CloneNode(g, true); var b = DocumentJson.LoadNodes(DocumentJson.SaveNodes([g]))[0]; Check(a.OpacityMask is not null && b.OpacityMask is not null && a.OpacityMaskId != g.OpacityMaskId && b.OpacityMaskId != g.OpacityMaskId); });
        test("deleting source releases mask and undo restores it", () => { var e = Mask("#FFFFFF"); e.Select(e.Primary!.OpacityMask); e.DeleteSelection(); Check(e.Page.Nodes[0].OpacityMaskId is null); e.Undo(); Check(e.Page.Nodes[0].OpacityMask is not null); });
        test("invalid dangling mask references fail validation", () => { var e = Mask("#FFFFFF"); e.Primary!.OpacityMaskId = "missing"; Reject(() => DocumentJson.Validate(e.Document)); });
        test("clip and opacity masks cannot share a source", () => { var e = Mask("#FFFFFF"); e.Primary!.ClipPathId = e.Primary.OpacityMaskId; Reject(() => DocumentJson.Validate(e.Document)); });
        test("mask creation rejects locked or active sources", () => { var a = Rect(); a.Locked = true; var e = Editor(a, Rect()); e.SelectAll(); Reject(() => OpacityMaskOperations.Make(e)); var masked = Mask("#FFFFFF"); masked.Select(masked.Primary!.Children.Select(c => c.Id)); Reject(() => OpacityMaskOperations.Make(masked)); });
        test("opacity mask source survives component synchronization", () => { var e = Mask("#FFFFFF"); ComponentService.MakeComponent(e); var def = e.Primary!; var instance = ComponentService.InsertInstance(e, def, new(200, 0)); Check(instance.OpacityMask is not null && instance.OpacityMaskId != def.OpacityMaskId); e.Edit("modify", () => def.Children[0].Fill = "#123456"); Check(instance.OpacityMask is not null); });
        test("mask picking rejects zero coverage and accepts partial coverage", () => { var e = Mask("#000000"); var g = e.Primary!; using var r = new SceneRenderer(); Check(r.HitTest(e.Page.Nodes, new(50,50), true) is null); g.OpacityMask!.Fill = "#808080"; Check(r.HitTest(e.Page.Nodes, new(50,50), true) == g.Children[0]); Check(r.HitTest(e.Page.Nodes, new(5,5), true) is null); });
        test("mask probing retains per-frame diagnostics", () => { var g = Mask("#FFFFFF").Primary!; using var r = new SceneRenderer(); using var s = SKSurface.Create(new SKImageInfo(160,160)); r.Draw(s.Canvas,[g]); var count = r.RenderedNodes; for (var i=0;i<20;i++) Check(r.MaskCoverageAt(g,new(50,50)) > .99); Check(r.RenderedNodes == count && r.MaskProbes == 20); });
        test("gradient fill opacity and stop opacity multiply only once", () =>
        {
            var n = Rect(); n.Fills = [new() { Kind = FillKind.LinearGradient, Color = "#001122", Opacity = .5, Stops = [new() { Offset=0,Color="#FFFFFF",Opacity=.5 },new() { Offset=1,Color="#FFFFFF",Opacity=.5 }] }];
            using var r=new SceneRenderer(); using var b=Render(r,[n]); Near(b.GetPixel(50,50).Alpha,64,1);
        });
        test("warm gradients reuse shaders across opacity and placement changes", () =>
        {
            var n = Rect(); n.Fills[0].Kind=FillKind.LinearGradient; using var r=new SceneRenderer(); using var s=SKSurface.Create(new SKImageInfo(160,160)); r.Draw(s.Canvas,[n]);
            for(var i=0;i<100;i++){n.X=i%10; n.Fills[0].Opacity=.5; r.Draw(s.Canvas,[n]);} Check(r.GradientBuilds==1 && r.GradientCacheHits>=100);
            n.Fills[0].Stops[0].Opacity=.25; r.Draw(s.Canvas,[n]); Check(r.GradientBuilds==2); r.PruneCache([]); Check(r.CachedGradientCount==0);
        });
        test("gradient shader cache detects direct stop and transform mutations", () => { var n=Rect(); n.Fills[0].Kind=FillKind.LinearGradient; using var r=new SceneRenderer(); using var s=SKSurface.Create(new SKImageInfo(160,160)); r.Draw(s.Canvas,[n]); n.Fills[0].Stops[0].Color="#ABCDEF"; r.Draw(s.Canvas,[n]); n.Fills[0].GradientTransform=Matrix2D.Translation(10,0); r.Draw(s.Canvas,[n]); n.Width=80; r.Draw(s.Canvas,[n]); Check(r.GradientBuilds==4); });
        test("empty gradients render transparent and one-stop gradients render that stop", () => { var n=Rect(); n.Fills[0].Kind=FillKind.LinearGradient; n.Fills[0].Stops=[]; using var r=new SceneRenderer(); using(var b=Render(r,[n])) Check(b.GetPixel(50,50).Alpha==0); n.Fills[0].Stops.Add(new(){Color="#FFFFFF",Opacity=.5}); using var c=Render(r,[n]); Near(c.GetPixel(50,50).Alpha,128,1); });
        test("SVG linear gradient imports percentages inheritance and stop opacity", () =>
        {
            var d=SvgFormat.Import("<svg width='100' height='100'><defs><linearGradient id='a'><stop offset='0%' stop-color='#FFFFFF' stop-opacity='.5'/><stop offset='100%' stop-color='#000000'/></linearGradient><linearGradient id='b' href='#a' x2='50%'/></defs><rect width='100' height='100' fill='url(#b)'/></svg>");
            Check(d.Warnings.Count==0); var f=d.Document.Pages[0].Nodes[0].Children[0].Fills[0]; Check(f.Kind==FillKind.LinearGradient && f.End.X==.5 && f.Stops[0].Opacity==.5);
        });
        test("SVG radial gradient retains center focus radius and reflect spread", () => { var d=SvgFormat.Import("<svg width='100' height='100'><defs><radialGradient id='a' cx='.3' cy='.4' r='.2' fx='.32' fy='.41' spreadMethod='reflect'><stop stop-color='#FFFFFF'/><stop offset='1' stop-color='#000000'/></radialGradient></defs><rect width='100' height='100' fill='url(#a)'/></svg>"); var f=d.Document.AllNodes().Single(n=>n.Kind==NodeKind.Rectangle).Fills[0]; Check(f.Start==new Vec2(.3,.4) && f.GradientRadius==.2 && f.GradientFocus==new Vec2(.32,.41) && f.GradientSpread==GradientSpread.Reflect); });
        test("SVG gradient cycles and external references fail closed", () => { Reject(()=>SvgFormat.Import("<svg><defs><linearGradient id='a' href='#b'/><linearGradient id='b' href='#a'/></defs><rect fill='url(#a)'/></svg>")); Reject(()=>SvgFormat.Import("<svg><rect fill='url(https://example.org/paint)'/></svg>")); });
        test("SVG gradient stop offsets are clamped and monotonically nondecreasing", () => { var d=SvgFormat.Import("<svg><defs><linearGradient id='a'><stop offset='-20%'/><stop offset='80%'/><stop offset='20%'/><stop offset='150%'/></linearGradient></defs><rect fill='url(#a)'/></svg>"); Check(d.Document.AllNodes().Single(n=>n.Kind==NodeKind.Rectangle).Fills[0].Stops.Select(s=>s.Offset).SequenceEqual(new[]{0d,.8,.8,1})); });
        test("SVG local gradient coordinates compensate translated primitives", () => { var d=SvgFormat.Import("<svg width='160' height='160'><defs><linearGradient id='a' gradientUnits='userSpaceOnUse' x1='20' x2='120'><stop stop-color='#FFFFFF'/><stop offset='1' stop-color='#000000'/></linearGradient></defs><rect x='20' y='20' width='100' height='100' fill='url(#a)'/></svg>"); using var r=new SceneRenderer(); using var b=Render(r,d.Document.Pages[0].Nodes); Near(b.GetPixel(70,50).Red,126,2); });
        test("SVG native linear and radial paints roundtrip without default radial placement", () =>
        {
            foreach(var kind in new[]{FillKind.LinearGradient,FillKind.RadialGradient})
            {var n=Rect(); n.Fills[0].Kind=kind; n.Fills[0].Start=new(.3,.4); n.Fills[0].End=new(.8,.6); n.Fills[0].Opacity=.65; n.Fills[0].Stops[0].Opacity=.6;
            var svg=SvgFormat.Export([n],new(0,0,160,160)); var d=SvgFormat.Import(svg).Document; PixelEquivalent([n],d.Pages[0].Nodes);}
        });
        test("SVG masks import alpha and luminance with multiple source children", () =>
        {
            foreach(var mode in new[]{"alpha","luminance"})
            {var d=SvgFormat.Import($"<svg width='160' height='160'><defs><mask id='m' maskUnits='userSpaceOnUse' x='0' y='0' width='100' height='100' style='mask-type:{mode}'><rect x='20' y='20' width='60' height='60' fill='#808080' opacity='.5'/></mask></defs><rect width='100' height='100' fill='#FF4000' mask='url(#m)'/></svg>").Document;
            using var r=new SceneRenderer(); using var b=Render(r,d.Pages[0].Nodes); Near(b.GetPixel(50,50).Alpha,mode=="alpha"?127:64,1); Check(b.GetPixel(5,5).Alpha==0);}
        });
        test("SVG opacity masks export and reimport source compositing", () => { var e=Mask("#808080",.5); e.Primary!.OpacityMaskRegion=new(0,0,120,120); var d=SvgFormat.Import(SvgFormat.Export(e.Page.Nodes,new(0,0,160,160))).Document; PixelEquivalent(e.Page.Nodes,d.Pages[0].Nodes); });
        test("SVG opacity mask recursion and missing references fail closed", () => { Reject(()=>SvgFormat.Import("<svg><defs><mask id='m' maskUnits='userSpaceOnUse'><rect mask='url(#m)'/></mask></defs><rect mask='url(#m)'/></svg>")); Reject(()=>SvgFormat.Import("<svg><rect mask='url(#missing)'/></svg>")); });
        test("SVG unsupported mask units and active source content are rejected", () => { Reject(()=>SvgFormat.Import("<svg><defs><mask id='m'><rect/></mask></defs><rect mask='url(#m)'/></svg>")); Reject(()=>SvgFormat.Import("<svg><defs><mask id='m' maskUnits='userSpaceOnUse'><image href='https://example.org/a.png'/></mask></defs><rect mask='url(#m)'/></svg>")); });
        test("affine group scale affects descendant geometry rather than nominal bounds only", () => { var d=SvgFormat.Import("<svg width='160' height='160'><g transform='scale(2)'><rect x='10' y='10' width='10' height='10'/></g></svg>").Document; using var r=new SceneRenderer(); using var b=Render(r,d.Pages[0].Nodes); Check(b.GetPixel(25,25).Alpha==255 && b.GetPixel(15,15).Alpha==0); });
        test("affine skew and reflection survive native save and SVG roundtrip", () => { var group=new DesignNode{Kind=NodeKind.Group,Fills=[]}; group.Add(Rect("#FF4000",10,20,40,30)); NodeGeometry.SetExactMatrix(group,new(1,.2,.3,1,5,7)); var editor=Editor(group); var loaded=DocumentJson.Load(DocumentJson.Save(editor.Document)); PixelEquivalent([group],loaded.Pages[0].Nodes); PixelEquivalent([group],SvgFormat.Import(SvgFormat.Export([group],new(0,0,160,160))).Document.Pages[0].Nodes); });
        test("affine reparenting preserves group world mapping", () => { var parent=new DesignNode{Kind=NodeKind.Group}; NodeGeometry.SetExactMatrix(parent,new(2,.2,.4,1.5,5,6)); var child=parent.Add(new(){Kind=NodeKind.Group,Width=30,Height=40,X=7,Y=8}); var nested=child.Add(Rect()); var p=nested.WorldMatrix.Map(new Vec2(3,4)); NodeGeometry.SetLocalMatrix(child,child.WorldMatrix); child.Parent=null; var q=nested.WorldMatrix.Map(new Vec2(3,4)); Near(p.X,q.X); Near(p.Y,q.Y); });
        test("nonfinite affine and gradient data is rejected", () => { var e=Editor(Rect()); e.Page.Nodes[0].AffineTransform=new(double.NaN,0,0,1,0,0); Reject(()=>DocumentJson.Validate(e.Document)); e.Page.Nodes[0].AffineTransform=null; e.Page.Nodes[0].Fills[0].Stops[0].Opacity=double.NaN; Reject(()=>DocumentJson.Validate(e.Document)); });
    }
}
