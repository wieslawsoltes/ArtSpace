from pathlib import Path

def patch(path, old, new):
    p=Path(path); text=p.read_text()
    if old not in text: raise RuntimeError('Missing patch anchor: '+path+': '+old[:80])
    p.write_text(text.replace(old,new))

patch('src/ArtSpace.Documents/SvgFormat.cs', '        var defs = new XElement(Ns + "defs");', '''        if (!double.IsFinite(bounds.X) || !double.IsFinite(bounds.Y) || !double.IsFinite(bounds.Right) || !double.IsFinite(bounds.Bottom) || bounds.IsEmpty) throw new ArgumentException("SVG export needs finite nonempty bounds.", nameof(bounds));
        var defs = new XElement(Ns + "defs");
        defs.AddAnnotation(new ExportViewport(bounds));''')
patch('src/ArtSpace.Documents/SvgFormat.Appearance.cs', '    private static XElement ExportOpacityMask(DesignNode owner, XElement defs)', '    private sealed record ExportViewport(RectD Bounds);\n\n    private static XElement ExportOpacityMask(DesignNode owner, XElement defs)')
patch('src/ArtSpace.Documents/SvgFormat.Appearance.cs', 'var region = owner.OpacityMaskRegion ?? new RectD(-1e9, -1e9, 2e9, 2e9);', '''var viewport = defs.Annotation<ExportViewport>()?.Bounds ?? owner.WorldBounds;
        var region = owner.OpacityMaskRegion ?? owner.WorldMatrix.Inverse.Map(viewport);
        if (region.IsEmpty || !double.IsFinite(region.Right) || !double.IsFinite(region.Bottom)) throw new InvalidOperationException("Invalid SVG mask export bounds.");''')
patch('src/ArtSpace.Documents/SvgFormat.cs', 'number.ToString("0.######", CultureInfo.InvariantCulture)', 'number.ToString("G17", CultureInfo.InvariantCulture)')
# Reject unsupported transform syntax rather than silently returning an identity matrix.
p=Path('src/ArtSpace.Documents/SvgFormat.cs'); text=p.read_text(); a=text.index('    public static Matrix2D ParseTransform('); b=text.index('    private static string Transform(',a)
text=text[:a]+'''    public static Matrix2D ParseTransform(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var result = Matrix2D.Identity; var end = 0;
        foreach (Match match in TransformRegex().Matches(text))
        {
            if (text[end..match.Index].Any(c => !char.IsWhiteSpace(c) && c != ',')) throw new InvalidDataException("Unsupported SVG transform syntax.");
            var arguments = match.Groups[2].Value;
            if (NumberRegex().Replace(arguments, "").Any(c => !char.IsWhiteSpace(c) && c != ',')) throw new InvalidDataException("Invalid SVG transform arguments.");
            var v = Values(arguments);
            var matrix = match.Groups[1].Value switch
            {
                "matrix" when v.Length == 6 => new Matrix2D(v[0], v[1], v[2], v[3], v[4], v[5]),
                "translate" when v.Length is 1 or 2 => Matrix2D.Translation(v[0], v.Length == 2 ? v[1] : 0),
                "scale" when v.Length is 1 or 2 => Matrix2D.Scale(v[0], v.Length == 2 ? v[1] : v[0]),
                "rotate" when v.Length == 1 => Matrix2D.Rotation(v[0]),
                "rotate" when v.Length == 3 => Matrix2D.Translation(-v[1], -v[2]) * Matrix2D.Rotation(v[0]) * Matrix2D.Translation(v[1], v[2]),
                "skewX" when v.Length == 1 => new Matrix2D(1, 0, Math.Tan(v[0] * Math.PI / 180), 1, 0, 0),
                "skewY" when v.Length == 1 => new Matrix2D(1, Math.Tan(v[0] * Math.PI / 180), 0, 1, 0, 0),
                _ => throw new InvalidDataException("Unsupported SVG transform or invalid argument count.")
            };
            result = matrix * result; end = match.Index + match.Length;
            if (!AffineGeometry.IsInvertible(result)) throw new InvalidDataException("Singular or nonfinite SVG transforms are not supported.");
        }
        if (text[end..].Any(c => !char.IsWhiteSpace(c) && c != ',')) throw new InvalidDataException("Unsupported SVG transform syntax.");
        return result;
    }
'''+text[b:]; p.write_text(text)
# Directly selected mask artwork remains movable even where it contributes zero visible coverage.
patch('src/ArtSpace.Editor/DesignSurface.cs', '''        if (Session is null) return null;
        foreach (var node in Session.Page.Nodes.Where''', '''        if (Session is null) return null;
        foreach (var selected in Session.SelectionRoots)
        {
            var source = selected;
            while (source.Parent is { } parent)
            {
                if (parent.OpacityMaskId == source.Id)
                {
                    if (!selected.IsEffectivelyLocked && selected.WorldBounds.Contains(world)) return selected;
                    break;
                }
                source = parent;
            }
        }
        foreach (var node in Session.Page.Nodes.Where''')
# Keep user-space/imported gradient positions through geometry normalization.
patch('src/ArtSpace.Skia/PathEditing.cs', '        var gradients = basis.Fills.Select(f => (f.Start, f.End)).ToArray();', '''        var gradients = basis.Fills.Select(f => (f.Start, f.End, f.GradientSpace, f.GradientTransform, f.GradientFocus, f.GradientRadius)).ToArray();
        var oldBounds = basis.LocalBounds;
        if (gradients.Any(f => f.GradientSpace == GradientSpace.ObjectBoundingBox))
        {
            using var oldPath = SKPath.ParseSvgPathData(VectorPath.Build(basis)) ?? new SKPath();
            if (basis.Kind == NodeKind.Path && basis.PathWidth > 0 && basis.PathHeight > 0)
                oldPath.Transform(SKMatrix.CreateScale((float)(oldWidth / basis.PathWidth), (float)(oldHeight / basis.PathHeight)));
            var old = oldPath.TightBounds; oldBounds = new(old.Left, old.Top, old.Width, old.Height);
        }''')
patch('src/ArtSpace.Skia/PathEditing.cs', '''            node.Fills[i].Start = new((old.Start.X * oldWidth - bounds.Left) / width, (old.Start.Y * oldHeight - bounds.Top) / height);
            node.Fills[i].End = new((old.End.X * oldWidth - bounds.Left) / width, (old.End.Y * oldHeight - bounds.Top) / height);''', '''            var fill = node.Fills[i];
            if (old.GradientSpace == GradientSpace.Legacy && old.GradientTransform == Matrix2D.Identity)
            {
                fill.Start = new((old.Start.X * oldWidth - bounds.Left) / width, (old.Start.Y * oldHeight - bounds.Top) / height);
                fill.End = new((old.End.X * oldWidth - bounds.Left) / width, (old.End.Y * oldHeight - bounds.Top) / height);
            }
            else
            {
                fill.Start = old.Start; fill.End = old.End; fill.GradientFocus = old.GradientFocus; fill.GradientRadius = old.GradientRadius;
                var transform = old.GradientTransform;
                if (old.GradientSpace == GradientSpace.ObjectBoundingBox)
                    transform *= Matrix2D.Scale(Math.Max(.000001, oldBounds.Width), Math.Max(.000001, oldBounds.Height)) * Matrix2D.Translation(oldBounds.X, oldBounds.Y);
                else if (old.GradientSpace == GradientSpace.Legacy)
                {
                    fill.Start = new(old.Start.X * oldWidth, old.Start.Y * oldHeight);
                    fill.End = new(old.End.X * oldWidth, old.End.Y * oldHeight);
                    fill.GradientFocus = fill.Start; fill.GradientRadius = Math.Max(1, fill.Start.DistanceTo(fill.End));
                }
                fill.GradientSpace = GradientSpace.UserSpaceOnUse;
                fill.GradientTransform = transform * Matrix2D.Translation(-bounds.Left, -bounds.Top) * Matrix2D.Scale(node.Width / width, node.Height / height);
            }''')
patch('tests/ArtSpace.Tests/Program.cs','AppearanceTests.Register(Test);','AppearanceTests.Register(Test);\nAppearanceRegressionTests.Register(Test);')
patch('tests/ArtSpace.Tests/PerformanceBenchmarks.cs','        Console.WriteLine(JsonSerializer.Serialize(new', '        var appearance = AppearanceBenchmarks.Run();\n        Console.WriteLine(JsonSerializer.Serialize(new')
patch('tests/ArtSpace.Tests/PerformanceBenchmarks.cs','            schema = 1, framework', '            appearance,\n            schema = 1, framework')
print('Applied mask bounds, coordinate editing, affine parsing and benchmark integration.')
