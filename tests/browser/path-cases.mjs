import { test, expect } from '@playwright/test';
import fs from 'node:fs/promises';
const state = page => page.evaluate(() => globalThis.__artSpaceState);
async function ready(page) {
  await page.goto('?test=1');
  await page.waitForFunction(() => globalThis.__artSpaceState?.ready, null, { timeout: 150_000 });
  await expect.poll(async () => (await state(page)).canvasWidth).toBeGreaterThan(100);
  await fs.mkdir('artifacts/screenshots', { recursive: true });
  await page.waitForTimeout(1200);
}
async function draw(page, key, a, b) {
  await page.keyboard.press(key);
  await page.mouse.move(...a); await page.mouse.down();
  await page.mouse.move(...b, { steps: 12 }); await page.mouse.up();
}
async function importSvg(page, name, content) {
  const before = await state(page);
  const chooser = page.waitForEvent('filechooser');
  await page.keyboard.press('Control+o');
  await (await chooser).setFiles({ name, mimeType: 'image/svg+xml', buffer: Buffer.from(content) });
  await expect.poll(async () => (await state(page)).roots).toBe(before.roots + 1);
  await expect.poll(async () => (await state(page)).kind).toBe('Frame');
  await page.waitForTimeout(350);
  return state(page);
}
const at = (s, x, y) => [s.canvasX + s.panX + x * s.zoom, s.canvasY + s.panY + y * s.zoom];

test('imported compound SVG supports direct anchors, insertion, persistence and undo', async ({ page }) => {
  await ready(page); await page.mouse.click(500, 300);
  const imported = await importSvg(page, 'anchor-fixture.svg', `<svg xmlns="http://www.w3.org/2000/svg" width="240" height="200"><path fill="#D29251" fill-rule="evenodd" d="M20 20H220V180H20Z M75 65H165V135H75Z"/></svg>`);
  await page.keyboard.press('a');
  await page.mouse.click(...at(imported, imported.x + 35, imported.y + 35));
  await expect.poll(async () => (await state(page)).anchors.length).toBe(8);
  expect((await state(page)).fillRule).toBe('EvenOdd');
  const selected = await state(page);
  const first = selected.anchors[0];
  await page.keyboard.press('Escape');
  await page.mouse.click(first.x, first.y);
  await expect.poll(async () => (await state(page)).anchors.filter(a => a.selected).length).toBe(1);
  const beforeDrag = await state(page);
  await page.mouse.move(first.x, first.y); await page.mouse.down();
  await page.mouse.move(first.x - 25, first.y + 18, { steps: 10 }); await page.mouse.up();
  await expect.poll(async () => (await state(page)).history).toBe(beforeDrag.history + 1);
  expect((await state(page)).anchors[0].x).toBeCloseTo(first.x - 25, 1);
  expect((await state(page)).anchors[1].x).toBeCloseTo(selected.anchors[1].x, 1);
  await page.keyboard.press('Control+z');
  await expect.poll(async () => (await state(page)).anchors[0].x).toBeCloseTo(first.x, 1);
  const points = (await state(page)).anchors;
  const middle = [(points[0].x + points[1].x) / 2, (points[0].y + points[1].y) / 2];
  await page.keyboard.down('Alt'); await page.mouse.click(...middle); await page.keyboard.up('Alt');
  await expect.poll(async () => (await state(page)).anchors.length).toBe(9);
  await page.screenshot({ path: 'artifacts/screenshots/compound-path-anchors.png' });
  const download = page.waitForEvent('download'); await page.keyboard.press('Control+s');
  const json = JSON.parse(await fs.readFile(await (await download).path(), 'utf8'));
  const path = json.pages[0].nodes.find(n => n.name === 'anchor-fixture').children[0];
  expect(path.fillRule).toBe('EvenOdd'); expect(path.pathData.match(/M/g)).toHaveLength(2);
  await page.keyboard.press('Control+z');
  await expect.poll(async () => (await state(page)).anchors.length).toBe(8);
  await page.keyboard.press('Control+Shift+z');
  await expect.poll(async () => (await state(page)).anchors.length).toBe(9);
});

test('text Create Outlines keeps editable glyph contours and undo restores text', async ({ page }) => {
  await ready(page); await page.mouse.click(500, 300);
  const imported = await importSvg(page, 'type-fixture.svg', `<svg xmlns="http://www.w3.org/2000/svg" width="240" height="160"><text x="40" y="85" font-family="Inter" font-size="64" fill="#E6AA67">O8</text></svg>`);
  await page.keyboard.press('v'); await page.mouse.click(...at(imported, imported.x + 50, imported.y + 50));
  await expect.poll(async () => (await state(page)).kind).toBe('Text');
  const before = await state(page);
  await page.keyboard.press('Control+Shift+o');
  await expect.poll(async () => (await state(page)).kind).toBe('Path');
  await expect.poll(async () => (await state(page)).history).toBe(before.history + 1);
  await page.keyboard.press('a');
  await expect.poll(async () => (await state(page)).anchors.length).toBeGreaterThan(8);
  expect(new Set((await state(page)).anchors.map(a => a.contour)).size).toBeGreaterThan(2);
  await page.screenshot({ path: 'artifacts/screenshots/text-outlines.png' });
  await page.keyboard.press('Control+z');
  await expect.poll(async () => (await state(page)).kind).toBe('Text');
  await page.keyboard.press('Control+Shift+z');
  await expect.poll(async () => (await state(page)).kind).toBe('Path');
});

test('anchor marquee, multi-anchor nudge and cancelled drag preserve transaction boundaries', async ({ page }) => {
  await ready(page); await page.mouse.click(540, 330);
  await draw(page, 'm', [520, 420], [700, 550]);
  const drawn = await state(page);
  await page.keyboard.press('a');
  await expect.poll(async () => (await state(page)).anchors.length).toBe(4);
  expect((await state(page)).history).toBe(drawn.history);
  const [a, b] = (await state(page)).anchors;
  await page.mouse.move(a.x - 14, a.y - 14); await page.mouse.down();
  await page.mouse.move(b.x + 14, b.y + 14, { steps: 10 }); await page.mouse.up();
  await expect.poll(async () => (await state(page)).anchors.filter(a => a.selected).length).toBe(2);
  await page.keyboard.press('Shift+ArrowRight');
  await expect.poll(async () => (await state(page)).history).toBe(drawn.history + 1);
  expect((await state(page)).anchors[0].x).toBeCloseTo(a.x + drawn.zoom * 10, 1);
  await page.keyboard.press('Control+z');
  await expect.poll(async () => (await state(page)).kind).toBe('Rectangle');
  const origin = (await state(page)).anchors[0];
  await page.mouse.move(origin.x, origin.y); await page.mouse.down();
  await page.mouse.move(origin.x - 40, origin.y - 20, { steps: 8 });
  await page.keyboard.press('Escape'); await page.mouse.up();
  await expect.poll(async () => (await state(page)).kind).toBe('Rectangle');
  expect((await state(page)).history).toBe(drawn.history);
});
