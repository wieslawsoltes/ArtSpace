#!/usr/bin/env python3
"""Idempotent finishing fixes; earlier source migrations are already committed."""
from pathlib import Path

def patch(path, old, new):
    p=Path(path)
    if p.exists():
        s=p.read_text()
        if old in s: p.write_text(s.replace(old,new))

patch('src/ArtSpace.Workbench/StudioWorkbench.cs', 'KeyDown += OnKeyDown; KeyUp +=', '''PreviewKeyDown += (_, e) =>
        {
            if (e.Key == VirtualKey.Tab && !Keyboard.IsTextInput(e.OriginalSource as DependencyObject))
            {
                TogglePanels(); e.Handled = true;
            }
        };
        KeyDown += OnKeyDown; KeyUp +=''')
patch('src/ArtSpace.Workbench/StudioWorkbench.Commands.cs', '                VirtualKey.Tab => TogglePanels,\n', '')
patch('src/ArtSpace.Workbench/StudioWorkbench.Illustration.cs', '{ Height = 23, Padding = new(0), CornerRadius = new(0), RestBackground = color', '{ Height = 23, HorizontalAlignment = HorizontalAlignment.Stretch, Padding = new(0), CornerRadius = new(0), RestBackground = color')
patch('src/ArtSpace.App/App.xaml.cs', 'Studio.Brush("#F5F5F5")', 'Studio.Brush("#292929")')
patch('tests/browser/illustration.spec.mjs', "  await draw(page, 'Shift+o', [830, 200], [1000, 350]);\n  await expect.poll(async () => (await state(page)).roots).toBe(initial.roots + 1);", "  const rootsBeforeArtboard = (await state(page)).roots;\n  await draw(page, 'Shift+o', [830, 200], [1000, 350]);\n  await expect.poll(async () => (await state(page)).roots).toBe(rootsBeforeArtboard + 1);")
print('Applied final workspace fixes.')
