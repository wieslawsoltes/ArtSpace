import { test, expect } from '@playwright/test';
import fs from 'node:fs/promises';
const state = page => page.evaluate(() => globalThis.__artSpaceState);
const field = (s, section, label) => s.inspectorFields?.find(f => f.section === section && f.label === label);
const at = (s, x, y) => [s.canvasX + s.panX + x * s.zoom, s.canvasY + s.panY + y * s.zoom];
async function ready(page) {
  await page.goto('?test=1');
  await page.waitForFunction(() => globalThis.__artSpaceState?.ready, null, { timeout: 150_000 });
  await expect.poll(async () => (await state(page)).canvasWidth).toBeGreaterThan(100);
  await page.mouse.click(500, 300);
}
async function fixture(page, dense = false) {
  const extra = dense ? Array.from({ length: 2000 }, (_, i) => `<rect x="${900 + (i % 100) * 4}" y="${Math.floor(i / 100) * 4}" width="3" height="3" fill="#A0B0C0"/>`).join('') : '';
  const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="600" height="380"><rect x="50" y="50" width="120" height="70" fill="#A35F40"/><rect x="230" y="50" width="150" height="70" fill="#407DA3"/><rect x="420" y="50" width="120" height="70" fill="#40A379"/><ellipse cx="120" cy="220" rx="70" ry="45" fill="#DAA735"/><text x="240" y="240" font-size="32">Retained text</text>${extra}</svg>`;
  const previous = await state(page); const picker = page.waitForEvent('filechooser');
  await page.keyboard.press('Control+o');
  await (await picker).setFiles({ name: 'ui-fixture.svg', mimeType: 'image/svg+xml', buffer: Buffer.from(svg) });
  await expect.poll(async () => (await state(page)).roots).toBe(previous.roots + 1);
  await expect.poll(async () => (await state(page)).kind).toBe('Frame');
  await page.waitForTimeout(300);
  return state(page);
}
async function select(page, root, x, y = 85, shift = false) {
  await page.keyboard.press('v');
  await page.keyboard.down('Control');
  if (shift) await page.keyboard.down('Shift');
  await page.mouse.click(...at(root, root.x + x, root.y + y));
  if (shift) await page.keyboard.up('Shift');
  await page.keyboard.up('Control');
  await expect.poll(async () => (await state(page)).kind).toBe('Rectangle');
  return state(page);
}
async function settled(page) {
  await page.waitForFunction(() => {
    const s = globalThis.__artSpaceState;
    return s && !s.uiPending && s.inspectorTarget === s.id && s.inspectorSelection === s.selection;
  });
  expect((await state(page)).uiFailures).toBe(0);
  return state(page);
}
async function panel(page, index, name) {
  const s = await state(page);
  const width = 314;
  await page.mouse.click(s.canvasX + s.canvasWidth + width * (index + .5) / 4, s.canvasY + 15);
  await expect.poll(async () => (await state(page)).activePanel).toBe(name);
  await expect.poll(async () => (await state(page)).uiPending).toBe(false);
}
async function editNumber(page, section, label, text) {
  await expect.poll(async () => field(await state(page), section, label)?.width ?? 0).toBeGreaterThan(40);
  const f = field(await state(page), section, label);
  await page.mouse.click(f.x + f.width * .72, f.y + f.height / 2);
  await page.keyboard.press('Control+a'); await page.keyboard.type(text); await page.keyboard.press('Enter');
}

test('retained inspector updates actual controls without snapshots or rebuilding common sections', async ({ page }) => {
  const errors = []; page.on('pageerror', e => errors.push(e.message));
  await ready(page); const root = await fixture(page);
  await select(page, root, 105); await settled(page);
  await select(page, root, 290); const warm = await settled(page);
  for (let i = 0; i < 8; i++) {
    await select(page, root, i % 2 ? 290 : 105); const s = await settled(page);
    expect(Number(field(s, 'Transform', 'X').value)).toBe(i % 2 ? 230 : 50);
    expect(Number(field(s, 'Layout', 'W').value)).toBe(i % 2 ? 150 : 120);
  }
  const end = await settled(page);
  expect(end.inspectorBuilds).toBe(warm.inspectorBuilds);
  expect(end.snapshots).toBe(warm.snapshots);
  expect(end.snapIndexBuilds).toBe(warm.snapIndexBuilds);
  expect(end.layerPasses).toBe(warm.layerPasses);
  expect(end.artboardRefreshes).toBe(warm.artboardRefreshes);
  expect(end.historyRefreshes).toBe(warm.historyRefreshes);
  expect(end.selectionIndexBuilds).toBe(warm.selectionIndexBuilds);
  expect(errors).toEqual([]);
});

test('retained numeric edits keep focus and bind undo redo and subsequent selection correctly', async ({ page }) => {
  await ready(page); const root = await fixture(page); await select(page, root, 105);
  const before = await settled(page);
  await editNumber(page, 'Transform', 'X', '73');
  await expect.poll(async () => (await state(page)).x).toBe(73);
  await page.keyboard.press('ArrowUp');
  await expect.poll(async () => (await state(page)).x).toBe(74);
  const edited = await settled(page); expect(edited.inspectorBuilds).toBe(before.inspectorBuilds);
  expect(Number(field(edited, 'Transform', 'X').value)).toBe(74);
  // Real focus loss, retarget, and return: B must not receive A's buffered numeric value.
  await select(page, root, 290); const other = await settled(page);
  expect(other.x).toBe(230); expect(Number(field(other, 'Transform', 'X').value)).toBe(230);
  await page.keyboard.press('Control+z'); await expect.poll(async () => (await state(page)).x).toBe(73);
  await settled(page); expect(Number(field(await state(page), 'Transform', 'X').value)).toBe(73);
  await page.keyboard.press('Control+Shift+z'); await expect.poll(async () => (await state(page)).x).toBe(74);
  await settled(page);
  await select(page, root, 290); await select(page, root, 110, 85, true);
  const multi = await settled(page); expect(multi.selection).toBe(2); expect(multi.inspectorSelection).toBe(2);
  expect(multi.uiFailures).toBe(0);
});

test('hidden panels defer work and layer selection updates without resetting the collection', async ({ page }) => {
  await ready(page); const root = await fixture(page, true);
  await select(page, root, 105); await settled(page);
  await panel(page, 1, 'Layers'); const start = await state(page);
  for (let i = 0; i < 6; i++) {
    await select(page, root, i % 2 ? 290 : 105);
    await expect.poll(async () => (await state(page)).selectedLayerIds).toEqual([(await state(page)).id]);
  }
  const end = await state(page);
  expect(end.inspectorRefreshes).toBe(start.inspectorRefreshes);
  expect(end.layerPasses).toBe(start.layerPasses);
  expect(end.layerEntryBuilds).toBe(start.layerEntryBuilds);
  expect(end.layerResets).toBe(start.layerResets);
  await panel(page, 3, 'History'); const history = await state(page);
  await select(page, root, 105); await expect.poll(async () => (await state(page)).uiPending).toBe(false);
  expect((await state(page)).historyRefreshes).toBe(history.historyRefreshes);
  await panel(page, 0, 'Properties'); const latest = await settled(page);
  expect(Number(field(latest, 'Transform', 'X').value)).toBe(50);
  expect(latest.uiFailures).toBe(0);
});

test('anchor clicks defer transactions and cancelled drags remain reversible', async ({ page }) => {
  await ready(page); const root = await fixture(page); await select(page, root, 105); await settled(page);
  await page.keyboard.press('a');
  await expect.poll(async () => (await state(page)).anchors.length).toBeGreaterThan(0);
  const before = await state(page); const anchor = before.anchors[0];
  await page.mouse.click(anchor.x, anchor.y); await settled(page);
  expect((await state(page)).snapshots).toBe(before.snapshots);
  const history = (await state(page)).history;
  await page.mouse.move(anchor.x, anchor.y); await page.mouse.down();
  await page.mouse.move(anchor.x + 30, anchor.y + 20, { steps: 6 }); await page.keyboard.press('Escape'); await page.mouse.up();
  await expect.poll(async () => (await state(page)).history).toBe(history);
  expect((await state(page)).uiFailures).toBe(0);
});

test('selection latency report for a 2000-node document', async ({ page }) => {
  test.setTimeout(300_000);
  await ready(page); const root = await fixture(page, true);
  await select(page, root, 105); await select(page, root, 290);
  const baseline = process.env.ARTSPACE_BENCHMARK_BASELINE === '1';
  const samples = [];
  for (let i = 0; i < 12; i++) {
    const expectedX = i % 2 ? 230 : 50;
    await page.evaluate(expectedX => {
      globalThis.__selectionSample = null;
      document.addEventListener('pointerdown', () => {
        const start = performance.now(); let stable = 0;
        const sample = () => {
          const s = globalThis.__artSpaceState;
          const current = s?.kind === 'Rectangle' && s.x === expectedX;
          const ui = s?.inspectorTarget === undefined || (!s.uiPending && s.inspectorTarget === s.id && s.inspectorFields.some(f => f.section === 'Transform' && f.label === 'X' && Number(f.value) === expectedX));
          stable = current && ui ? stable + 1 : 0;
          if (stable >= 2) globalThis.__selectionSample = performance.now() - start;
          else requestAnimationFrame(sample);
        };
        requestAnimationFrame(sample);
      }, { once: true, capture: true });
    }, expectedX);
    await select(page, root, i % 2 ? 290 : 105);
    await page.waitForFunction(() => typeof globalThis.__selectionSample === 'number', null, { timeout: 45_000 });
    samples.push(await page.evaluate(() => globalThis.__selectionSample));
  }
  const sorted = [...samples].sort((a, b) => a - b); const final = await state(page);
  const report = { schema: 1, variant: baseline ? 'baseline' : 'optimized', nodes: final.nodes,
    note: 'Pointerdown to matching selection (and matching retained inspector fields when available) plus two animation-frame opportunities. Chromium/SwiftShader CI, not physical-GPU display latency or application FPS. Includes rendering and opt-in diagnostics.',
    medianMs: sorted[Math.floor(sorted.length / 2)], p95Ms: sorted[Math.ceil(sorted.length * .95) - 1], samplesMs: samples,
    inspectorBuilds: final.inspectorBuilds, uiFailures: final.uiFailures, snapshots: final.snapshots };
  await fs.mkdir('artifacts/ui-performance', { recursive: true });
  await fs.writeFile(`artifacts/ui-performance/${report.variant}.json`, JSON.stringify(report, null, 2));
  console.log('UI_SELECTION_LATENCY ' + JSON.stringify(report));
  if (!baseline) expect(final.uiFailures).toBe(0);
});
