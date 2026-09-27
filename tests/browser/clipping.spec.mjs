import { test, expect } from '@playwright/test';
import fs from 'node:fs/promises';
const state = page => page.evaluate(() => globalThis.__artSpaceState);
async function ready(page) {
  await page.goto('?test=1');
  await page.waitForFunction(() => globalThis.__artSpaceState?.ready, null, { timeout: 150_000 });
  await expect.poll(async () => (await state(page)).canvasWidth).toBeGreaterThan(100);
  await fs.mkdir('artifacts/screenshots', { recursive: true });
  await page.waitForTimeout(1200);
  await page.mouse.click(500, 300);
}
async function importSvg(page, name, text) {
  const before = await state(page);
  const chooser = page.waitForEvent('filechooser');
  await page.keyboard.press('Control+o');
  await (await chooser).setFiles({ name, mimeType: 'image/svg+xml', buffer: Buffer.from(text) });
  await expect.poll(async () => (await state(page)).roots).toBe(before.roots + 1);
  await expect.poll(async () => (await state(page)).kind).toBe('Frame');
  await page.waitForTimeout(350);
  return state(page);
}
const at = (s, x, y) => [s.canvasX + s.panX + x * s.zoom, s.canvasY + s.panY + y * s.zoom];
async function savedDocument(page) {
  const promise = page.waitForEvent('download'); await page.keyboard.press('Control+s');
  const download = await promise;
  return JSON.parse(await fs.readFile(await download.path(), 'utf8'));
}

test('clipping masks retain editable anchors, native references, undo and local recovery', async ({ page }) => {
  const errors = []; page.on('pageerror', error => errors.push(error.message));
  await ready(page);
  await importSvg(page, 'clipping-fixture.svg', `<svg xmlns="http://www.w3.org/2000/svg" width="240" height="200"><rect x="20" y="20" width="200" height="160" fill="#E8AF6A"/><ellipse cx="120" cy="100" rx="75" ry="60" fill="#203F49"/></svg>`);
  const before = await state(page);
  await page.keyboard.press('Control+7');
  await expect.poll(async () => (await state(page)).clipGroups).toBe(1);
  await expect.poll(async () => (await state(page)).history).toBe(before.history + 1);
  const clipId = (await state(page)).clipPathId;
  expect(clipId).toBeTruthy();
  const document = await savedDocument(page);
  const group = document.pages[0].nodes.find(n => n.name === 'clipping-fixture');
  expect(group.clipPathId).toBe(clipId); expect(group.children).toHaveLength(2);
  expect(group.children.find(n => n.id === clipId).fills).toHaveLength(0);
  await page.screenshot({ path: 'artifacts/screenshots/clipping-mask.png' });

  // Choose the real Object menu's third-from-last command: Edit Clipping Path.
  await page.mouse.click(148, 15); await page.keyboard.press('End');
  await page.keyboard.press('ArrowUp'); await page.keyboard.press('ArrowUp'); await page.keyboard.press('Enter');
  await expect.poll(async () => (await state(page)).anchors.length).toBeGreaterThanOrEqual(4);
  // Entering from the menu exposes anchors without selecting them. Exercise selection and
  // anchor-only Escape clearing explicitly; Escape with no anchor selection deselects the object.
  await page.keyboard.press('Control+a');
  await expect.poll(async () => (await state(page)).anchors.filter(a => a.selected).length).toBeGreaterThanOrEqual(4);
  await page.keyboard.press('Escape');
  await expect.poll(async () => (await state(page)).anchors.length).toBeGreaterThanOrEqual(4);
  await expect.poll(async () => (await state(page)).anchors.filter(a => a.selected).length).toBe(0);
  const first = (await state(page)).anchors[0];
  await page.mouse.click(first.x, first.y);
  await expect.poll(async () => (await state(page)).anchors.filter(a => a.selected).length).toBe(1);
  const beforeDrag = await state(page);
  await page.mouse.move(first.x, first.y); await page.mouse.down();
  await page.mouse.move(first.x + 28, first.y + 16, { steps: 10 }); await page.mouse.up();
  await expect.poll(async () => (await state(page)).history).toBe(beforeDrag.history + 1);
  expect((await state(page)).anchors[0].x).toBeCloseTo(first.x + 28, 1);
  expect((await state(page)).clipGroups).toBe(1);
  await page.screenshot({ path: 'artifacts/screenshots/clipping-mask-anchors.png' });
  await page.keyboard.press('Control+z');
  await expect.poll(async () => (await state(page)).anchors[0].x).toBeCloseTo(first.x, 1);

  await page.keyboard.press('Control+Alt+7');
  await expect.poll(async () => (await state(page)).clipGroups).toBe(0);
  await page.keyboard.press('Control+z');
  await expect.poll(async () => (await state(page)).clipGroups).toBe(1);
  await page.waitForTimeout(1600); await page.reload();
  await page.waitForFunction(() => globalThis.__artSpaceState?.ready, null, { timeout: 150_000 });
  await expect.poll(async () => (await state(page)).clipGroups).toBe(1);
  expect(errors).toEqual([]);
});

test('imported SVG clipping holes prevent selection of hidden artwork', async ({ page }) => {
  await ready(page);
  const imported = await importSvg(page, 'clip-hole-fixture.svg', `<svg xmlns="http://www.w3.org/2000/svg" width="240" height="200"><defs><clipPath id="hole" clipPathUnits="userSpaceOnUse"><path clip-rule="evenodd" d="M20 20H220V180H20Z M80 65H160V135H80Z"/></clipPath></defs><rect id="clipped-artwork" x="0" y="0" width="240" height="200" fill="#E6AA67" clip-path="url(#hole)"/></svg>`);
  expect(imported.clipGroups).toBe(1);
  await page.keyboard.press('a');
  await page.mouse.click(...at(imported, imported.x + 45, imported.y + 45));
  await expect.poll(async () => (await state(page)).kind).toBe('Rectangle');
  await page.keyboard.press('v');
  await page.mouse.click(...at(imported, imported.x + 120, imported.y + 100));
  await expect.poll(async () => (await state(page)).kind).not.toBe('Rectangle');
  await page.screenshot({ path: 'artifacts/screenshots/clipping-compound-hole.png' });
  const saved = await savedDocument(page);
  const frame = saved.pages[0].nodes.find(n => n.name === 'clip-hole-fixture');
  const clipped = frame.children[0];
  expect(clipped.children.find(n => n.id === clipped.clipPathId).fillRule).toBe('EvenOdd');
});

test('warm geometry caches survive repeated movement without rebuilding the scene', async ({ page }) => {
  await ready(page);
  await page.keyboard.press('m');
  await page.mouse.move(550, 370); await page.mouse.down(); await page.mouse.move(700, 490, { steps: 10 }); await page.mouse.up();
  await expect.poll(async () => (await state(page)).kind).toBe('Rectangle');
  await page.waitForTimeout(250);
  await page.keyboard.press('ArrowRight');
  const baseline = await state(page);
  for (let i = 0; i < 12; i++) await page.keyboard.press('ArrowRight');
  await expect.poll(async () => (await state(page)).x).toBeCloseTo(baseline.x + 12, 3);
  await page.waitForTimeout(250);
  // A viewport command publishes counters after pending drawing has completed, without changing geometry.
  await page.keyboard.press('Control+1');
  const after = await state(page);
  expect(after.geometryBuilds - baseline.geometryBuilds).toBeLessThanOrEqual(2);
  expect(after.history).toBe(baseline.history + 12);
});
