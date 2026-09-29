import { test, expect } from '@playwright/test';
import fs from 'node:fs/promises';
import vm from 'node:vm';
const state = page => page.evaluate(() => globalThis.__artSpaceState);
const field = (s, section, label) => s.inspectorFields?.find(f => f.section === section && f.label === label);
const frames = page => page.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))));
const at = (s, x, y) => [s.canvasX + s.panX + x * s.zoom, s.canvasY + s.panY + y * s.zoom];
async function ready(page) {
  await page.goto('?test=1');
  await page.waitForFunction(() => globalThis.__artSpaceState?.ready, null, { timeout: 150_000 });
  await expect.poll(async () => (await state(page)).canvasWidth).toBeGreaterThan(100);
  await page.mouse.click(500, 300);
  const previous = await state(page); const chooser = page.waitForEvent('filechooser');
  await page.keyboard.press('Control+o');
  await (await chooser).setFiles({ name: 'appearance-fixture.svg', mimeType: 'image/svg+xml', buffer: Buffer.from(`<svg xmlns="http://www.w3.org/2000/svg" width="520" height="300"><rect x="50" y="50" width="120" height="70" fill="#D95732"/><rect x="250" y="50" width="150" height="70" fill="#326FA3"/></svg>`) });
  await expect.poll(async () => (await state(page)).roots).toBe(previous.roots + 1);
  await expect.poll(async () => (await state(page)).kind).toBe('Frame');
  await frames(page); return state(page);
}
async function select(page, root, second = false) {
  await page.keyboard.press('v'); await page.keyboard.down('Control');
  await page.mouse.click(...at(root, root.x + (second ? 300 : 105), root.y + 85));
  await page.keyboard.up('Control');
  await page.waitForFunction(width => {
    const s = globalThis.__artSpaceState;
    return s?.kind === 'Rectangle' && s.width === width && !s.uiPending;
  }, second ? 150 : 120);
  await frames(page); return state(page);
}
async function panel(page, index, name) {
  const s = await state(page);
  await page.mouse.click(s.canvasX + s.canvasWidth + 314 * ((index % 4) + .5) / 4, s.canvasY + Math.floor(index / 4) * 31 + 15);
  await page.waitForFunction(name => globalThis.__artSpaceState?.activePanel === name && !globalThis.__artSpaceState.uiPending, name);
  await frames(page);
}
async function clickField(page, section, label) {
  await expect.poll(async () => field(await state(page), section, label)?.width ?? 0).toBeGreaterThan(0);
  const f = field(await state(page), section, label);
  expect(f.y).toBeGreaterThan(0); expect(f.y + f.height).toBeLessThan(page.viewportSize().height);
  await page.mouse.click(f.x + f.width / 2, f.y + f.height / 2);
  await frames(page);
}
async function number(page, section, label, value) {
  const f = field(await state(page), section, label); expect(f?.width).toBeGreaterThan(40);
  await page.mouse.click(f.x + f.width * .75, f.y + f.height / 2);
  await page.keyboard.press('Control+a'); await page.keyboard.type(String(value)); await page.keyboard.press('Enter');
  await frames(page);
}
async function saved(page) {
  const event = page.waitForEvent('download'); await page.keyboard.press('Control+s');
  return JSON.parse(await fs.readFile(await (await event).path(), 'utf8'));
}
function findNode(document, id) {
  function find(nodes) { for (const n of nodes) { if (n.id === id) return n; const child = find(n.children ?? []); if (child) return child; } }
  return document.pages.map(p => find(p.nodes)).find(Boolean);
}

test('Appearance panel edits non-destructive effects with undo and schema-four persistence', async ({ page }) => {
  const errors = []; page.on('pageerror', e => errors.push(e.message));
  const root = await ready(page); const selected = await select(page, root);
  await panel(page, 4, 'Appearance');
  await clickField(page, 'Live Effects', 'Add live effect');
  await expect.poll(async () => (await state(page)).effects).toBe(1);
  await expect.poll(async () => field(await state(page), 'Effect 1', 'Radius')?.value).toBe('6');
  await number(page, 'Effect 1', 'Radius', 19);
  await expect.poll(async () => (await state(page)).effectRadius).toBe(19);
  const before = await state(page);
  await select(page, root); await page.keyboard.press('Control+z');
  await expect.poll(async () => (await state(page)).effectRadius).toBe(6);
  await page.keyboard.press('Control+Shift+z');
  await expect.poll(async () => (await state(page)).effectRadius).toBe(19);
  expect((await state(page)).kind).toBe('Rectangle');
  const document = await saved(page); expect(document.formatVersion).toBe(4);
  expect(findNode(document, selected.id).effects[0]).toMatchObject({ kind: 'GaussianBlur', radius: 19, enabled: true });
  expect((await state(page)).uiFailures).toBe(0); expect(before.effects).toBe(1);
  await fs.mkdir('artifacts/screenshots', { recursive: true });
  await page.screenshot({ path: 'artifacts/screenshots/live-appearance-panel.png' });
  expect(errors).toEqual([]);
});

test('Graphic Styles captures appearance and applies it without replacing target geometry', async ({ page }) => {
  const root = await ready(page); await select(page, root);
  await panel(page, 4, 'Appearance'); await clickField(page, 'Live Effects', 'Add live effect');
  await expect.poll(async () => (await state(page)).effects).toBe(1);
  await panel(page, 5, 'Graphic Styles'); await clickField(page, 'Graphic Styles', 'New Graphic Style');
  await expect.poll(async () => (await state(page)).graphicStyles).toBe(1);
  const target = await select(page, root, true); expect(target.effects).toBe(0);
  await clickField(page, 'Graphic style 1', 'Apply style');
  await expect.poll(async () => (await state(page)).effects).toBe(1);
  expect((await state(page)).width).toBe(150); expect((await state(page)).id).toBe(target.id);
  await select(page, root, true);
  const document = await saved(page);
  expect(document.graphicStyles).toHaveLength(1);
  expect(findNode(document, target.id).effects[0].kind).toBe('GaussianBlur');
  await page.keyboard.press('Control+z'); await expect.poll(async () => (await state(page)).effects).toBe(0);
  await page.keyboard.press('Control+Shift+z'); await expect.poll(async () => (await state(page)).effects).toBe(1);
  expect((await state(page)).uiFailures).toBe(0);
  await fs.mkdir('artifacts/screenshots', { recursive: true });
  await page.screenshot({ path: 'artifacts/screenshots/graphic-styles-panel.png' });
});

test('unchanged scene is replayed during selection without rerecording paints or geometry', async ({ page }) => {
  // Test the read-only host adapter as well: frame patches cannot replace UI observations.
  const script = await fs.readFile('src/ArtSpace.App/Platforms/WebAssembly/WasmScripts/Storage.js', 'utf8');
  for (const search of ['', '?test=1']) {
    const context = vm.createContext({ URLSearchParams, location: { search }, document: { addEventListener() {} } });
    vm.runInContext(script, context);
    const adapter = context.artSpaceStorage;
    adapter.publishFrameDiagnostics(1, 2, 3, 4, 5, 6, 7, 8, 9);
    expect(context.__artSpaceState).toBeUndefined();
    adapter.publishDiagnostics(JSON.stringify({ ready: true, id: 'selected', inspectorFields: [{ value: '19' }] }));
    if (!search) {
      adapter.publishFrameDiagnostics(1, 2, 3, 4, 5, 6, 7, 8, 9);
      expect(context.__artSpaceState).toBeUndefined();
      continue;
    }
    const previous = context.__artSpaceState;
    adapter.publishFrameDiagnostics(1, 2, 3, 4, 5, 6, 7, 8, 9);
    expect(context.__artSpaceState.id).toBe('selected');
    expect(context.__artSpaceState.inspectorFields).toBe(previous.inspectorFields);
    expect(context.__artSpaceState.sceneReplays).toBe(2);
    expect(previous.sceneReplays).toBeUndefined();
    expect(Object.isFrozen(context.__artSpaceState)).toBe(true);
  }
  const root = await ready(page); await select(page, root); await select(page, root, true);
  await page.waitForFunction(() => globalThis.__artSpaceState?.sceneReplays > 0);
  const warm = await state(page);
  for (let i = 0; i < 8; i++) await select(page, root, i % 2 === 1);
  const end = await state(page);
  expect(end.sceneRecordings).toBe(warm.sceneRecordings);
  expect(end.sceneReplays).toBeGreaterThan(warm.sceneReplays);
  expect(end.paintBuilds).toBe(warm.paintBuilds);
  expect(warm.diagnosticSceneScans).toBeGreaterThan(0);
  expect(end.diagnosticSceneScans).toBe(warm.diagnosticSceneScans);
  expect(end.snapshots).toBe(warm.snapshots);
  expect(end.uiFailures).toBe(0);
});

test.afterEach(async ({ page }, info) => {
  if (info.status !== info.expectedStatus && !page.isClosed())
    console.log('FAILED_APPEARANCE_OBSERVATION', JSON.stringify(await state(page).catch(() => null)));
});
