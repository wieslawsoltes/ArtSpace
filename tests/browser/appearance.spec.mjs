import { test, expect } from '@playwright/test';
import fs from 'node:fs/promises';
const state = page => page.evaluate(() => globalThis.__artSpaceState);
async function ready(page) {
  await page.goto('?test=1');
  await page.waitForFunction(() => globalThis.__artSpaceState?.ready, null, { timeout: 150_000 });
  await expect.poll(async () => (await state(page)).canvasWidth).toBeGreaterThan(100);
  await fs.mkdir('artifacts/screenshots', { recursive: true });
  await page.waitForTimeout(1200); await page.mouse.click(500, 300);
}
async function importSvg(page, name, svg) {
  const before = await state(page); const chooser = page.waitForEvent('filechooser');
  await page.keyboard.press('Control+o');
  await (await chooser).setFiles({ name, mimeType: 'image/svg+xml', buffer: Buffer.from(svg) });
  await expect.poll(async () => (await state(page)).roots).toBe(before.roots + 1);
  await expect.poll(async () => (await state(page)).kind).toBe('Frame');
  await page.waitForTimeout(350); return state(page);
}
async function objectCommand(page, fromEnd) {
  await page.mouse.click(148, 15); await page.keyboard.press('End');
  for (let i = 0; i < fromEnd; i++) await page.keyboard.press('ArrowUp');
  await page.keyboard.press('Enter');
}
async function saved(page) {
  const result = page.waitForEvent('download'); await page.keyboard.press('Control+s');
  return JSON.parse(await fs.readFile(await (await result).path(), 'utf8'));
}
const at = (s, x, y) => [s.canvasX + s.panX + x * s.zoom, s.canvasY + s.panY + y * s.zoom];
const gradientFixture = `<svg xmlns="http://www.w3.org/2000/svg" width="240" height="200"><defs><linearGradient id="fade"><stop offset="0" stop-color="#FFFFFF"/><stop offset="1" stop-color="#000000"/></linearGradient></defs><rect width="240" height="200" fill="#E6AA67"/><rect x="20" y="20" width="200" height="160" fill="url(#fade)"/></svg>`;

test('opacity mask commands preserve editable source, inversion, undo and schema-three recovery', async ({ page }) => {
  const errors = []; page.on('pageerror', error => errors.push(error.message));
  await ready(page); const frame = await importSvg(page, 'opacity-fixture.svg', gradientFixture);
  await objectCommand(page, 10); // Make Opacity Mask, ahead of existing clipping commands.
  await expect.poll(async () => (await state(page)).opacityMasks).toBe(1);
  expect((await state(page)).opacityMaskMode).toBe('Luminance');
  const id = (await state(page)).opacityMaskId;
  let document = await saved(page); expect(document.formatVersion).toBe(4);
  let owner = document.pages[0].nodes.find(n => n.name === 'opacity-fixture');
  expect(owner.opacityMaskId).toBe(id); expect(owner.children).toHaveLength(2);
  expect(owner.children.find(n => n.id === id).fills[0].kind).toBe('LinearGradient');
  await page.screenshot({ path: 'artifacts/screenshots/opacity-mask.png' });
  await objectCommand(page, 6); // Invert.
  await expect.poll(async () => (await state(page)).opacityMaskInverted).toBe(true);
  await page.keyboard.press('Control+z');
  await expect.poll(async () => (await state(page)).opacityMaskInverted).toBe(false);
  await objectCommand(page, 5); // Disable and restore.
  await expect.poll(async () => (await state(page)).opacityMaskEnabled).toBe(false);
  await page.keyboard.press('Control+z');
  await objectCommand(page, 8); // Explicit source editing, not normal coverage picking.
  await expect.poll(async () => (await state(page)).kind).toBe('Rectangle');
  const source = await state(page);
  const center = at(frame, frame.x + source.x + source.width / 2, frame.y + source.y + source.height / 2);
  await page.mouse.move(...center); await page.mouse.down();
  await page.mouse.move(center[0] + 30, center[1] + 12, { steps: 8 }); await page.mouse.up();
  await expect.poll(async () => (await state(page)).x).not.toBe(source.x);
  document = await saved(page); owner = document.pages[0].nodes.find(n => n.name === 'opacity-fixture');
  expect(owner.opacityMaskId).toBe(id); expect(owner.children.find(n => n.id === id).x).not.toBe(source.x);
  await page.keyboard.press('Control+z');
  await objectCommand(page, 9); // Release while selected inside the source.
  await expect.poll(async () => (await state(page)).opacityMasks).toBe(0);
  await page.keyboard.press('Control+z');
  await expect.poll(async () => (await state(page)).opacityMasks).toBe(1);
  await page.waitForTimeout(1600); await page.reload();
  await page.waitForFunction(() => globalThis.__artSpaceState?.ready, null, { timeout: 150_000 });
  await expect.poll(async () => (await state(page)).opacityMasks).toBe(1);
  expect(errors).toEqual([]);
});

test('SVG alpha masks, radial coordinates and affine transforms remain in native documents', async ({ page }) => {
  await ready(page);
  await importSvg(page, 'svg-appearance.svg', `<svg xmlns="http://www.w3.org/2000/svg" width="240" height="200"><defs><radialGradient id="paint" cx=".35" cy=".4" r=".6" fx=".3" fy=".35" spreadMethod="reflect"><stop stop-color="#F8D997"/><stop offset="1" stop-color="#254B67" stop-opacity=".4"/></radialGradient><mask id="mask" maskUnits="userSpaceOnUse" x="0" y="0" width="240" height="200" style="mask-type:alpha"><rect x="10" y="10" width="190" height="150" fill="#000000" opacity=".5"/></mask></defs><g transform="translate(5,6) skewX(8)"><rect width="200" height="160" fill="url(#paint)" mask="url(#mask)"/></g></svg>`);
  expect((await state(page)).opacityMasks).toBe(1);
  const document = await saved(page);
  function* nodes(root) { yield root; for (const child of root.children) yield* nodes(child); }
  const frame = document.pages[0].nodes.find(n => n.name === 'svg-appearance');
  const all = [...nodes(frame)]; const owner = all.find(n => n.opacityMaskId);
  expect(owner.opacityMaskMode).toBe('Alpha');
  const paint = all.flatMap(n => n.fills).find(f => f.kind === 'RadialGradient');
  expect(paint.gradientSpace).toBe('ObjectBoundingBox'); expect(paint.gradientRadius).toBe(.6);
  expect(paint.gradientFocus).toEqual({ x: .3, y: .35 }); expect(paint.stops[1].opacity).toBe(.4);
  expect(all.some(n => n.affineTransform && n.affineTransform.m21 > .1)).toBe(true);
  await page.screenshot({ path: 'artifacts/screenshots/svg-alpha-radial-affine.png' });
});

test('on-canvas editing respects imported gradient units and warm shaders survive movement', async ({ page }) => {
  await ready(page); const frame = await importSvg(page, 'gradient-edit.svg', gradientFixture);
  await page.keyboard.press('a'); await page.mouse.click(...at(frame, frame.x + 120, frame.y + 100));
  await expect.poll(async () => (await state(page)).kind).toBe('Rectangle');
  const from = at(frame, frame.x + 40, frame.y + 60); const to = at(frame, frame.x + 190, frame.y + 140);
  await page.keyboard.press('g'); await page.mouse.move(...from); await page.mouse.down();
  await page.mouse.move(...to, { steps: 10 }); await page.mouse.up();
  const document = await saved(page);
  const root = document.pages[0].nodes.find(n => n.name === 'gradient-edit');
  const fill = root.children[1].fills[0];
  expect(fill.gradientSpace).toBe('ObjectBoundingBox');
  expect(fill.start.x).toBeCloseTo(.1, 2); expect(fill.end.x).toBeCloseTo(.85, 2);
  await page.keyboard.press('v'); await page.keyboard.press('ArrowRight'); await page.waitForTimeout(200);
  const before = await state(page);
  for (let i = 0; i < 6; i++) await page.keyboard.press('ArrowRight');
  await page.waitForTimeout(200); await page.keyboard.press('Control+1');
  expect((await state(page)).gradientBuilds - before.gradientBuilds).toBeLessThanOrEqual(1);
  await page.screenshot({ path: 'artifacts/screenshots/imported-gradient-edit.png' });
});
