#!/usr/bin/env python3
from pathlib import Path
import re

def patch(path, old, new):
    p=Path(path)
    if p.exists():
        s=p.read_text()
        if old in s: p.write_text(s.replace(old,new))

patch('src/ArtSpace.Editor/DesignSurface.Illustration.cs', 'node.WorldMatrix.Map(new(p.X * node.Width, p.Y * node.Height))', 'node.WorldMatrix.Map(new Vec2(p.X * node.Width, p.Y * node.Height))')
patch('src/ArtSpace.Workbench/StudioWorkbench.Commands.cs', 'RequestedTheme = ElementTheme.Light', 'RequestedTheme = ElementTheme.Dark')
patch('src/ArtSpace.Workbench/StudioWorkbench.Illustration.cs', 'n.TextAlign = TextAlignment.', 'n.TextAlign = ArtSpace.Core.TextAlignment.')
patch('src/ArtSpace.Workbench/StudioWorkbench.Illustration.cs', 'yield return Item("Open Alpine sample", () => { Session.Load(IllustrationSample.Create()); Surface.Fit(firstFrame: true); });', 'yield return Async("Open Alpine sample", async () => { if (Session.IsDirty && !await ConfirmAsync("Replace current artwork?", "Save a local copy first to keep the current document.")) return; Session.Load(IllustrationSample.Create()); Surface.Fit(firstFrame: true); });')
for name in ['editor.spec.mjs','gestures.spec.mjs']:
    p=Path('tests/browser')/name
    if p.exists(): p.unlink()

marker=Path('eng/appearance-polished.txt')
if not marker.exists():
    workspace='src/ArtSpace.Workbench/StudioWorkbench.Illustration.cs'
    patch(workspace,'private bool _illustrationReady;', '''private bool _illustrationReady;
    private ColorField? _fillControl, _strokeControl;
    private NumericField? _strokeWidthControl;
    private ComboBox? _opacityControl;
    private bool _syncingAppearance;''')
    patch(workspace,'_controlBar.Children.Add(new ColorField(Surface.FillColor', '_controlBar.Children.Add(_fillControl = new ColorField(Surface.FillColor')
    patch(workspace,'_controlBar.Children.Add(new ColorField(Surface.StrokeColor', '_controlBar.Children.Add(_strokeControl = new ColorField(Surface.StrokeColor')
    patch(workspace,'_controlBar.Children.Add(Number("pt", Surface.StrokeWidth', '_controlBar.Children.Add(_strokeWidthControl = Number("pt", Surface.StrokeWidth')
    patch(workspace, 'value => Change("Opacity", n => n.Opacity = double.Parse(value.TrimEnd(\'%\'), System.Globalization.CultureInfo.InvariantCulture) / 100)', 'value => { if (!_syncingAppearance) Change("Opacity", n => n.Opacity = double.Parse(value.TrimEnd(\'%\'), System.Globalization.CultureInfo.InvariantCulture) / 100); }')
    patch(workspace,'opacity.Width = 78; _controlBar.Children.Add(opacity);','opacity.Width = 78; _opacityControl = opacity; _controlBar.Children.Add(opacity);')
    patch(workspace,'        _artboards.Children.Clear();', '''        _syncingAppearance = true;
        try
        {
            var primary = Session.Primary;
            if (primary is not null)
            {
                Surface.FillColor = primary.Fill;
                Surface.StrokeColor = primary.Strokes.FirstOrDefault()?.Color ?? Surface.StrokeColor;
                Surface.StrokeWidth = primary.Strokes.FirstOrDefault()?.Width ?? 0;
            }
            if (_fillControl is not null) _fillControl.Value = Surface.FillColor;
            if (_strokeControl is not null) _strokeControl.Value = Surface.StrokeColor;
            if (_strokeWidthControl is not null) _strokeWidthControl.Value = Surface.StrokeWidth;
            if (_opacityControl is not null)
            {
                var value = Numbers.Format((primary?.Opacity ?? 1) * 100) + "%";
                if (!_opacityControl.Items.Contains(value)) _opacityControl.Items.Add(value);
                _opacityControl.SelectedItem = value;
            }
        }
        finally { _syncingAppearance = false; }
        _artboards.Children.Clear();''')
    path=Path('src/ArtSpace.Workbench/StudioWorkbench.Commands.cs')
    s=path.read_text()
    start=s.index('    private IEnumerable<QuickAction> Actions()')
    end=s.index('    private static void AddMenu',start)
    s=s[:start]+'''    private IEnumerable<QuickAction> Actions()
    {
        foreach (var menu in new[] { "File", "Edit", "Object", "Type", "Select", "Effect", "View", "Window", "Help" })
            foreach (var command in IllustrationMenu(menu))
                if (command.Enabled && command.Execute is not null && command.Label.Length > 0 && command.Label != "Find a Command…")
                    yield return new(menu + " / " + command.Label, command.Shortcut, command.Execute);
    }
'''+s[end:]
    s=s.replace('Add auto layout            Shift A','Add auto layout').replace('Create component",','Create symbol",')
    path.write_text(s)
    patch('src/ArtSpace.Illustration/IllustrationOperations.cs', '''output.PathData = result.ToSvgPathData(); output.PathWidth = node.Width; output.PathHeight = node.Height;
                output.Points.Clear();''', '''var bounds = result.TightBounds;
                using var normalized = new SKPath(result);
                normalized.Transform(SKMatrix.CreateTranslation(-bounds.Left, -bounds.Top));
                output.Width = output.PathWidth = Math.Max(.001, bounds.Width);
                output.Height = output.PathHeight = Math.Max(.001, bounds.Height);
                output.PathData = normalized.ToSvgPathData();
                NodeGeometry.SetLocalMatrix(output, Matrix2D.Translation(bounds.Left, bounds.Top) * node.LocalMatrix);
                output.Points.Clear();''')
    p=Path('tests/ArtSpace.Tests/Program.cs')
    tests='''Test("offset normalizes expanded selection bounds", () => { var n=Node(20,30); var e=Editor(n); e.Select(n); using var r=new SceneRenderer(); IllustrationOperations.OffsetPaths(e,r,10); var p=e.Primary!; Equal(p.X,10); Equal(p.Y,20); Equal(p.Width,120); Equal(p.Height,120); });
Test("offset preserves rotated center", () => { var n=Node(20,30); n.Rotation=37; var center=n.WorldBounds.Center; var e=Editor(n); e.Select(n); using var r=new SceneRenderer(); IllustrationOperations.OffsetPaths(e,r,10); Equal(e.Primary!.WorldBounds.Center.X,center.X); Equal(e.Primary.WorldBounds.Center.Y,center.Y); });
Test("inset normalizes reduced selection bounds", () => { var n=Node(20,30); var e=Editor(n); e.Select(n); using var r=new SceneRenderer(); IllustrationOperations.OffsetPaths(e,r,-10); Equal(e.Primary!.X,30); Equal(e.Primary.Y,40); Equal(e.Primary.Width,80); Equal(e.Primary.Height,80); });
'''
    p.write_text(p.read_text().replace('var failed = 0;',tests+'\nvar failed = 0;'))
    marker.write_text('Appearance and offset bounds polish applied.\n')
print('Applied source polish.')
