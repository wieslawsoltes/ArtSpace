import { test, expect } from '@playwright/test';
import fs from 'node:fs/promises';
const state = page => page.evaluate(() => globalThis.__artSpaceState);
const frames = page => page.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))));
const field = (s, label) => s.inspectorFields?.find(f => f.section === 'Type on a Path' && f.label === label);
const fixture = `<svg xmlns="http://www.w3.org/2000/svg" width="500" height="240"><defs><path id="b" d="M20 150H480"/></defs><text id="Editable SVG label" font-family="Inter" font-size="32" fill="#254F70" text-anchor="middle"><textPath href="#b" startOffset="50%">EDITABLE SVG TEXT</textPath></text></svg>`;
async function ready(page) {
  await page.setViewportSize({ width: 1600, height: 1450 });
  await page.goto('?test=1');
  await page.waitForFunction(() => globalThis.__artSpaceState?.ready, null, { timeout: 150_000 });
  await page.mouse.click(500, 300); const before = await state(page);
  const chooser = page.waitForEvent('filechooser'); await page.keyboard.press('Control+o');
  await (await chooser).setFiles({ name: 'editable-path-text.svg', mimeType: 'image/svg+xml', buffer: Buffer.from(fixture) });
  await expect.poll(async () => (await state(page)).roots).toBe(before.roots + 1);
  await expect.poll(async () => (await state(page)).kind).toBe('Frame');
  // Real ungroup command selects the only child, without a test-only selection/mutation bridge.
  await page.keyboard.press('Control+Shift+g');
  await expect.poll(async () => (await state(page)).kind).toBe('Text');
  await page.waitForFunction(() => !globalThis.__artSpaceState.uiPending && !!globalThis.__artSpaceState.inspectorFields?.find(f => f.label === 'SVG offset'));
  await frames(page); return state(page);
}
async function control(page, label) {
  for (let i = 0; i < 12; i++) {
    const f = field(await state(page), label);
    if (f?.width > 0 && f.y > 145 && f.y + f.height < 1390) return f;
    await page.mouse.move(1450, 1050); await page.mouse.wheel(0, !f || f.y >= 1390 ? 300 : -300); await frames(page);
  }
  throw new Error('Control not visible: ' + label);
}
async function save(page) {
  const promise = page.waitForEvent('download'); await page.keyboard.press('Control+s');
  return JSON.parse(await fs.readFile(await (await promise).path(), 'utf8'));
}
function nodes(document) {
  const all = []; const visit = list => { for (const n of list) { all.push(n); visit(n.children ?? []); } };
  document.pages.forEach(p => visit(p.nodes)); return all;
}

test('editable SVG path text imports with retained offset controls and survives native recovery', async ({ page }) => {
  const errors = []; page.on('pageerror', e => errors.push(e.message));
  const initial = await ready(page); expect(initial.svgTextPath).toMatchObject({ offset: 50, percentage: true });
  expect(initial.pathText.error).toBeNull(); expect(initial.typePathHandles).toHaveLength(1);
  const f = await control(page, 'SVG offset'); const builds = (await state(page)).inspectorBuilds;
  await page.mouse.click(f.x + f.width * .8, f.y + f.height / 2);
  await page.keyboard.press('Control+a'); await page.keyboard.type('35'); await page.keyboard.press('Enter');
  await expect.poll(async () => (await state(page)).svgTextPath.offset).toBe(35);
  expect((await state(page)).inspectorBuilds).toBe(builds);
  const document = await save(page); expect(document.formatVersion).toBe(6);
  expect(nodes(document).find(n => n.id === initial.id)).toMatchObject({ text: 'EDITABLE SVG TEXT', textPath: { svgPosition: { offset: 35, percentage: true } } });
  await fs.mkdir('artifacts/screenshots', { recursive: true });
  await page.screenshot({ path: 'artifacts/screenshots/svg-text-path-inspector.png' });
  await page.waitForTimeout(1500); await page.reload();
  await page.waitForFunction(() => globalThis.__artSpaceState?.ready, null, { timeout: 150_000 });
  const recovered = await save(page); expect(nodes(recovered).find(n => n.id === initial.id).textPath.svgPosition.offset).toBe(35);
  expect(errors).toEqual([]);
});

test('SVG anchor dragging is lazy cancellable and does not remeasure baseline', async ({ page }) => {
  const initial = await ready(page); const handle = initial.typePathHandles[0]; expect(handle.kind).toBe(3);
  await page.mouse.click(handle.x, handle.y); await frames(page); const before = await state(page);
  expect(before.snapshots).toBe(initial.snapshots);
  await page.mouse.move(handle.x, handle.y); await page.mouse.down(); await page.mouse.move(handle.x + 55, handle.y, { steps: 10 }); await page.mouse.up();
  await expect.poll(async () => (await state(page)).history).toBe(before.history + 1);
  expect((await state(page)).svgTextPath.offset).toBeGreaterThan(50);
  expect((await state(page)).textBaselineBuilds).toBe(before.textBaselineBuilds);
  await page.keyboard.press('Control+z'); await expect.poll(async () => (await state(page)).svgTextPath.offset).toBe(50);
  await frames(page); const h = (await state(page)).typePathHandles[0];
  await page.mouse.move(h.x, h.y); await page.mouse.down(); await page.mouse.move(h.x - 45, h.y, { steps: 8 }); await page.keyboard.press('Escape'); await page.mouse.up();
  await expect.poll(async () => (await state(page)).svgTextPath.offset).toBe(50);
  expect((await state(page)).id).toBe(initial.id); expect((await state(page)).uiFailures).toBe(0);
});

test('File menu exports genuine editable textPath without mutating native text', async ({ page }) => {
  const original = await ready(page); const download = page.waitForEvent('download');
  await page.mouse.click(61, 15); await page.waitForFunction(() => globalThis.__artSpaceState?.openMenu === 'File');
  await page.keyboard.press('Home');
  for (let i = 0; i < 16 && (await state(page)).activeMenuCommand !== 'Export Editable SVG…'; i++) await page.keyboard.press('ArrowDown');
  expect((await state(page)).activeMenuCommand).toBe('Export Editable SVG…'); await page.keyboard.press('Enter');
  const file = await download; const svg = await fs.readFile(await file.path(), 'utf8');
  expect(svg).toContain('<textPath'); expect(svg).toContain('startOffset="50%"'); expect(svg).toContain('EDITABLE SVG TEXT');
  expect((await state(page)).history).toBe(original.history); expect((await state(page)).kind).toBe('Text');
  const before = await state(page); const chooser = page.waitForEvent('filechooser'); await page.keyboard.press('Control+o');
  await (await chooser).setFiles({ name: 'roundtrip.svg', mimeType: 'image/svg+xml', buffer: Buffer.from(svg) });
  await expect.poll(async () => (await state(page)).roots).toBe(before.roots + 1);
  const document = await save(page); expect(nodes(document).filter(n => n.text === 'EDITABLE SVG TEXT' && n.textPath?.svgPosition)).toHaveLength(2);
});
