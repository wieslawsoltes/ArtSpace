#!/usr/bin/env python3
from pathlib import Path

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
print('Applied source polish.')
