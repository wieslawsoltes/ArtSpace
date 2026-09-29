import { test, expect } from '@playwright/test';
import fs from 'node:fs/promises';
const state = page => page.evaluate(() => globalThis.__artSpaceState);
const frames = page => page.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))));
const at = (s, x, y) => [s.canvasX + s.panX + x * s.zoom, s.canvasY + s.panY + y * s.zoom];
const field = (s, section, label) => s.inspectorFields?.find(f => f.section === section && f.label === label);
const fixture = `<svg xmlns="http://www.w3.org/2000/svg" width="520" height="320"><defs><linearGradient id="ink"><stop offset="0" stop-color="#E84927"/><stop offset="1" stop-color="#2978D2"/></linearGradient></defs><path id="first-ribbon" d="M40 100H450" fill="none" stroke="url(#ink)" stroke-width="20"/><path id="second-ribbon" d="M40 220H450" fill="none" stroke="#396B85" stroke-width="20"/></svg>`;
async function ready(page) {
  await page.goto('?test=1'); await page.waitForFunction(() => globalThis.__artSpaceState?.ready, null, { timeout: 150_000 });
  await expect.poll(async () => (await state(page)).canvasWidth).toBeGreaterThan(100);
  await page.mouse.click(500, 300); const before = await state(page);
  const chooser = page.waitForEvent('filechooser'); await page.keyboard.press('Control+o');
  await (await chooser).setFiles({ name: 'width-fixture.svg', mimeType: 'image/svg+xml', buffer: Buffer.from(fixture) });
  await expect.poll(async () => (await state(page)).roots).toBe(before.roots + 1);
  await expect.poll(async () => (await state(page)).kind).toBe('Frame');
  await frames(page); return state(page);
}
async function select(page, root, second = false) {
  await page.keyboard.press('v'); await page.keyboard.down('Control');
  await page.mouse.click(...at(root, root.x + 245, root.y + (second ? 220 : 100))); await page.keyboard.up('Control');
  await expect.poll(async () => (await state(page)).name).toBe(second ? 'second-ribbon' : 'first-ribbon');
  await frames(page); return state(page);
}
async function drawWidth(page, root) {
  const selected = await select(page, root); await page.keyboard.press('Shift+w');
  await expect.poll(async () => (await state(page)).tool).toBe('Width');
  const start = at(root, root.x + 245, root.y + 100);
  await page.mouse.move(...start); await page.mouse.down(); await page.mouse.move(start[0], start[1] + 30, { steps: 8 }); await page.mouse.up();
  await expect.poll(async () => (await state(page)).widthPoints).toBe(3);
  await expect.poll(async () => (await state(page)).history).toBe(selected.history + 1);
  await frames(page); return state(page);
}
async function saved(page) {
  const pending = page.waitForEvent('download'); await page.keyboard.press('Control+s');
  const download = await pending; return JSON.parse(await fs.readFile(await download.path(), 'utf8'));
}
function findNode(document, id) {
  function find(nodes) { for (const node of nodes) { if (node.id === id) return node; const child = find(node.children ?? []); if (child) return child; } }
  return document.pages.map(p => find(p.nodes)).find(Boolean);
}
async function strokePanel(page) {
  const s = await state(page);
  // Real custom dock buttons use the same four-column layout as the other panel tests.
  await page.mouse.click(s.canvasX + s.canvasWidth + 314 * 2.5 / 4, s.canvasY + 31 + 15);
  await page.waitForFunction(() => globalThis.__artSpaceState?.activePanel === 'Stroke' && !globalThis.__artSpaceState.uiPending);
  await frames(page);
}
async function visibleField(page, section, label) {
  for (let attempt = 0; attempt < 20; attempt++) {
    const s = await state(page); const f = field(s, section, label);
    if (f && f.width > 20 && f.y >= s.canvasY + 65 && f.y + f.height < page.viewportSize().height - 35) return f;
    await page.mouse.move(page.viewportSize().width - 80, page.viewportSize().height / 2);
    await page.mouse.wheel(0, f && f.y < s.canvasY + 65 ? -260 : 260); await page.waitForTimeout(100); await frames(page);
  }
  throw new Error(`Cannot scroll to ${section} / ${label}: ${JSON.stringify(field(await state(page), section, label))}`);
}
async function number(page, section, label, value) {
  const f = await visibleField(page, section, label);
  await page.mouse.click(f.x + f.width * .8, f.y + f.height / 2);
  await page.keyboard.press('Control+a'); await page.keyboard.type(String(value)); await page.keyboard.press('Enter'); await frames(page);
}
async function command(page, text) {
  // Actual command palette text input; diagnostics do not expose commands or model mutation.
  await page.keyboard.press('Control+k'); await page.keyboard.type(text); await page.keyboard.press('Enter'); await frames(page);
}

test('Width tool edits symmetric and asymmetric sides with cancellable single-transaction gestures', async ({ page }) => {
  const errors = []; page.on('pageerror', e => errors.push(e.message));
  const root = await ready(page); let current = await drawWidth(page, root); const id = current.id;
  let mid = current.widthHandles.find(h => h.index === 1); expect(mid).toBeTruthy();
  const beforeClick = current;
  await page.mouse.click(mid.x, mid.y); await frames(page);
  expect((await state(page)).snapshots).toBe(beforeClick.snapshots);
  expect((await state(page)).history).toBe(beforeClick.history);
  const fixedRight = mid.rightY;
  await page.keyboard.down('Alt'); await page.mouse.move(mid.leftX, mid.leftY); await page.mouse.down();
  await page.mouse.move(mid.leftX, mid.leftY + 24, { steps: 8 }); await page.mouse.up(); await page.keyboard.up('Alt');
  await expect.poll(async () => (await state(page)).history).toBe(beforeClick.history + 1);
  current = await state(page); mid = current.widthHandles.find(h => h.index === 1);
  expect(mid.rightY).toBeCloseTo(fixedRight, 2);
  expect(mid.leftY).toBeCloseTo(beforeClick.widthHandles[1].leftY + 24, 1);
  const document = await saved(page); expect(document.formatVersion).toBe(5);
  const node = findNode(document, id); expect(node.pathData).toBe('M40 100H450');
  expect(node.strokes[0].widthProfile).toHaveLength(3);
  expect(node.strokes[0].widthProfile[1].left).toBeGreaterThan(node.strokes[0].widthProfile[1].right);

  const beforeCancel = await state(page); const handle = beforeCancel.widthHandles[1];
  await page.mouse.move(handle.leftX, handle.leftY); await page.mouse.down(); await page.mouse.move(handle.leftX, handle.leftY + 22, { steps: 6 });
  await page.keyboard.press('Escape'); await page.mouse.up();
  await expect.poll(async () => (await state(page)).widthHandles[1]?.leftY).toBeCloseTo(handle.leftY, 2);
  expect((await state(page)).history).toBe(beforeCancel.history);
  await page.keyboard.press('Control+z'); await expect.poll(async () => (await state(page)).widthHandles[1]?.leftY).toBeCloseTo(beforeClick.widthHandles[1].leftY, 2);
  await page.keyboard.press('Control+Shift+z'); await expect.poll(async () => (await state(page)).widthHandles[1]?.leftY).toBeCloseTo(handle.leftY, 2);
  await page.mouse.click(handle.x, handle.y); await page.keyboard.press('Delete');
  await expect.poll(async () => (await state(page)).widthPoints).toBe(2); expect((await state(page)).id).toBe(id);
  await page.keyboard.press('Control+z'); await expect.poll(async () => (await state(page)).widthPoints).toBe(3);
  expect((await state(page)).renderError).toBeNull(); expect((await state(page)).uiFailures).toBe(0);
  await fs.mkdir('artifacts/screenshots', { recursive: true }); await page.screenshot({ path: 'artifacts/screenshots/variable-width-handles.png' });
  expect(errors).toEqual([]);
});

test('Stroke panel retains numeric fields and width profiles survive save and recovery', async ({ page }) => {
  const root = await ready(page); const initial = await drawWidth(page, root);
  await strokePanel(page); await number(page, 'Stroke 1 · Width Point 2', 'Left', 35);
  await expect.poll(async () => field(await state(page), 'Stroke 1 · Width Point 2', 'Left')?.value).toBe('35');
  const edited = await state(page); const builds = edited.strokePanelBuilds;
  await number(page, 'Stroke 1 · Width Point 2', 'Left', 28);
  expect((await state(page)).strokePanelBuilds).toBe(builds);
  // Leave the text field through the canvas before exercising the application undo command.
  await select(page, root); await page.keyboard.press('Control+z');
  await expect.poll(async () => field(await state(page), 'Stroke 1 · Width Point 2', 'Left')?.value).toBe('35');
  await page.keyboard.press('Control+Shift+z'); await expect.poll(async () => field(await state(page), 'Stroke 1 · Width Point 2', 'Left')?.value).toBe('28');
  const document = await saved(page); const node = findNode(document, initial.id);
  expect(node.strokes[0].widthProfile[1].left * node.strokes[0].width).toBeCloseTo(28, 5);
  expect((await state(page)).uiFailures).toBe(0); expect((await state(page)).renderError).toBeNull();
  await page.waitForTimeout(1800); await page.reload(); await page.waitForFunction(() => globalThis.__artSpaceState?.ready, null, { timeout: 150_000 });
  const recovered = await saved(page); expect(findNode(recovered, initial.id).strokes[0].widthProfile).toEqual(node.strokes[0].widthProfile);
  await fs.mkdir('artifacts/screenshots', { recursive: true }); await page.screenshot({ path: 'artifacts/screenshots/stroke-panel-recovery.png' });
});

test('width geometry is reused across selection and translation and exported as real SVG coverage', async ({ page }) => {
  const root = await ready(page); const selected = await drawWidth(page, root); const id = selected.id;
  await select(page, root, true); await select(page, root); await frames(page); const warm = await state(page);
  for (let i = 0; i < 6; i++) { await select(page, root, true); await select(page, root); }
  await page.keyboard.press('ArrowRight'); await frames(page);
  const moved = await state(page); expect(moved.strokeOutlineBuilds).toBe(warm.strokeOutlineBuilds);
  expect(moved.strokeCenterlineBuilds).toBe(warm.strokeCenterlineBuilds);
  const pending = page.waitForEvent('download'); await command(page, 'Export SVG');
  const download = await pending; const svg = await fs.readFile(await download.path(), 'utf8');
  expect(svg).toContain('data-artspace-expanded-stroke'); expect(svg).toContain('linearGradient'); expect(svg).toContain('fill-opacity');
  const native = await saved(page); expect(findNode(native, id).strokes[0].widthProfile).toHaveLength(3);
  expect((await state(page)).renderError).toBeNull(); expect((await state(page)).uiFailures).toBe(0);
});

test.afterEach(async ({ page }, info) => {
  if (info.status !== info.expectedStatus && !page.isClosed()) console.log('FAILED_WIDTH_OBSERVATION', JSON.stringify(await state(page).catch(() => null)));
});
