"""Apply reviewed, source-guarded responsiveness fixes; removed immediately after integration."""
from pathlib import Path
import subprocess

def edit(path, sha, before, after):
    file = Path(path)
    assert subprocess.check_output(['git', 'hash-object', str(file)], text=True).strip() == sha, path
    source = file.read_text()
    assert source.count(before) == 1, path
    file.write_text(source.replace(before, after))

edit('src/ArtSpace.Editor/DesignSurface.cs', 'cc59595bf4e09c3f628bea5bf70eb705741a9c22',
'''            if (Session is null || IsPresenting) return;
            if (_penNode is not null)''',
'''            if (Session is null || IsPresenting) return;
            // An explicit contour tool owns anchor clicks, including a rapid second press.
            // Generic artwork picking deliberately excludes mask sources, so allowing it here
            // can switch an anchor drag to the underlying masked artwork.
            if (Session.Tool is EditorTool.Vertex or EditorTool.AddAnchor or EditorTool.DeleteAnchor or EditorTool.ConvertAnchor)
            { e.Handled = true; return; }
            if (_penNode is not null)''')
edit('tests/browser/clipping.spec.mjs', 'e155ccff7933ff7f7a0dc8a22d595186f67a4933',
'''  await page.keyboard.press('ArrowRight');
  const baseline = await state(page);''',
'''  const beforeWarm = await state(page);
  await page.keyboard.press('ArrowRight');
  await expect.poll(async () => (await state(page)).history).toBe(beforeWarm.history + 1);
  await expect.poll(async () => (await state(page)).x).toBeCloseTo(beforeWarm.x + 1, 3);
  const baseline = await state(page);''')
file = Path('README.md')
source = file.read_text()
assert '195 registered engine cases' in source and '13 browser scenarios' in source
source = source.replace('195 registered engine cases', '211 registered engine cases').replace('13 browser scenarios', '18 browser scenarios')
source = source.replace('## New in 0.4\n', '''## New in 0.4.1

**Responsive selection and panels:** retained inspector sections and values, visible-panel-only refreshes, incremental layer rows, indexed selection and lazy drag transactions. Plain clicks no longer serialize the document or construct a snapping index. Focused edits and section expansion survive ordinary updates. [Implementation and reproducible measurements](docs/ui-performance.md).

## New in 0.4
''')
file.write_text(source)
print('Applied contour-input ownership fix and exact asynchronous test barriers.')
