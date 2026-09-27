import { test, expect } from '@playwright/test';
import fs from 'node:fs/promises';
const state = page => page.evaluate(() => globalThis.__artSpaceState);

async function ready(page) {
  await page.goto('?test=1');
  await page.waitForFunction(() => globalThis.__artSpaceState?.ready, null, { timeout: 150_000 });
  await expect.poll(async () => (await state(page)).canvasWidth).toBeGreaterThan(100);
  await page.waitForTimeout(1200);
}
async function draw(page, key, a = [560, 340], b = [730, 460]) {
  await page.keyboard.press(key);
  await page.mouse.move(...a); await page.mouse.down();
  await page.mouse.move(...b, { steps: 12 }); await page.mouse.up();
}

test('Uno illustration workbench: draw, transform, undo, redo, download and recover', async ({ page }) => {
  const errors = []; page.on('pageerror', e => errors.push(e.message));
  await ready(page);
  await fs.mkdir('artifacts/screenshots', { recursive: true });
  await page.screenshot({ path: 'artifacts/screenshots/workbench-desktop.png' });
  const initial = await state(page);
  expect(initial.nodes).toBeGreaterThan(60); expect(initial.roots).toBe(3); expect(initial.pages).toBe(1);
  await page.mouse.click(570, 310);
  await draw(page, 'm');
  await expect.poll(async () => (await state(page)).nodes).toBe(initial.nodes + 1);
  const rectangle = await state(page);
  expect(rectangle.kind).toBe('Rectangle'); expect(rectangle.width).toBeGreaterThan(50);
  await page.keyboard.press('ArrowRight');
  await expect.poll(async () => (await state(page)).x).toBeCloseTo(rectangle.x + 1, 3);
  await page.keyboard.press('Control+z');
  await expect.poll(async () => (await state(page)).x).toBeCloseTo(rectangle.x, 3);
  await page.keyboard.press('Control+Shift+z');
  await expect.poll(async () => (await state(page)).x).toBeCloseTo(rectangle.x + 1, 3);
  const download = page.waitForEvent('download'); await page.keyboard.press('Control+s');
  const file = await download; expect(file.suggestedFilename()).toMatch(/\.artspace$/);
  const saved = JSON.parse(await fs.readFile(await file.path(), 'utf8'));
  expect(saved.name).toBe('Alpine Echoes');
  await page.waitForTimeout(1500); await page.reload();
  await page.waitForFunction(() => globalThis.__artSpaceState?.ready, null, { timeout: 150_000 });
  await expect.poll(async () => (await state(page)).nodes).toBe(initial.nodes + 1);
  expect(errors).toEqual([]);
});

test('tool conventions, pen path, gradient, zoom and artboard creation', async ({ page }) => {
  await ready(page); await page.mouse.click(550, 310);
  const initial = await state(page);
  await draw(page, 'l', [480, 370], [610, 460]);
  await expect.poll(async () => (await state(page)).kind).toBe('Ellipse');
  await page.keyboard.press('g');
  await expect.poll(async () => (await state(page)).tool).toBe('Gradient');
  await page.mouse.move(490, 380); await page.mouse.down(); await page.mouse.move(605, 450, { steps: 8 }); await page.mouse.up();
  await page.keyboard.press('p');
  for (const [x, y] of [[650, 450], [720, 370], [810, 440]]) await page.mouse.click(x, y);
  await page.keyboard.press('Enter');
  await expect.poll(async () => (await state(page)).kind).toBe('Path');
  const zoom = (await state(page)).zoom;
  await page.keyboard.press('z'); await page.mouse.click(850, 520);
  await expect.poll(async () => (await state(page)).zoom).toBeGreaterThan(zoom);
  await page.keyboard.press('Control+0');
  await draw(page, 'Shift+o', [830, 200], [1000, 350]);
  await expect.poll(async () => (await state(page)).roots).toBe(initial.roots + 1);
  expect((await state(page)).kind).toBe('Frame');
  await page.keyboard.press('a');
  await expect.poll(async () => (await state(page)).tool).toBe('DirectSelect');
});

test('panels hide without losing document and compact workspace remains usable', async ({ page }) => {
  await ready(page); await page.mouse.click(540, 300);
  const before = await state(page);
  await page.keyboard.press('Tab');
  await expect.poll(async () => (await state(page)).canvasWidth).toBeGreaterThan(before.canvasWidth);
  await page.keyboard.press('Tab');
  await expect.poll(async () => (await state(page)).canvasWidth).toBeCloseTo(before.canvasWidth, 0);
  await page.setViewportSize({ width: 720, height: 900 });
  await page.waitForTimeout(600);
  await page.keyboard.press('Control+0');
  await fs.mkdir('artifacts/screenshots', { recursive: true });
  await page.screenshot({ path: 'artifacts/screenshots/workbench-compact.png' });
  expect((await state(page)).nodes).toBe(before.nodes);
  await draw(page, 'm', [290, 300], [410, 390]);
  await expect.poll(async () => (await state(page)).nodes).toBe(before.nodes + 1);
});
