import { test, expect } from '@playwright/test';
import fs from 'node:fs/promises';
const state = page => page.evaluate(() => globalThis.__artSpaceState);
const frames = page => page.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))));
const at = (s, x, y) => [s.canvasX + s.panX + x * s.zoom, s.canvasY + s.panY + y * s.zoom];
const field = (s, section, label) => s.inspectorFields?.find(f => f.section === section && f.label === label);
const findNode = (document, id) => {
  function visit(nodes) { for (const node of nodes) { if (node.id === id) return node; const child = visit(node.children ?? []); if (child) return child; } }
  return document.pages.map(p => visit(p.nodes)).find(Boolean);
};
async function ready(page) {
  await page.setViewportSize({ width: 1600, height: 1450 });
  await page.goto('?test=1');
  await page.waitForFunction(() => globalThis.__artSpaceState?.ready, null, { timeout: 150_000 });
  await expect.poll(async () => (await state(page)).canvasWidth).toBeGreaterThan(100);
  await page.mouse.click(500, 300);
  const previous = await state(page); const chooser = page.waitForEvent('filechooser');
  await page.keyboard.press('Control+o');
  await (await chooser).setFiles({ name: 'type-path-fixture.svg', mimeType: 'image/svg+xml',
    buffer: Buffer.from('<svg xmlns="http://www.w3.org/2000/svg" width="500" height="260"><path d="M40 190C40 60 460 60 460 190" fill="none" stroke="#1C4864" stroke-width="3"/></svg>') });
  await expect.poll(async () => (await state(page)).roots).toBe(previous.roots + 1);
  await expect.poll(async () => (await state(page)).kind).toBe('Frame'); await frames(page);
  const root = await state(page);
  await page.keyboard.down('Control'); await page.mouse.click(...at(root, root.x + 250, root.y + 92.5)); await page.keyboard.up('Control');
  await expect.poll(async () => (await state(page)).kind).toBe('Path'); await frames(page);
  return state(page);
}
async function create(page, text = 'ALPINE ECHOES — TYPE ON A PATH') {
  await ready(page);
  // Activate the real custom Type menu and its first command.
  await page.mouse.click(200, 15); await page.keyboard.press('Home'); await page.keyboard.press('Enter');
  await page.waitForTimeout(350);
  await page.keyboard.press('Control+a'); await page.keyboard.type(text); await page.keyboard.press('Enter');
  await page.waitForFunction(() => globalThis.__artSpaceState?.pathText && !globalThis.__artSpaceState.uiPending);
  await expect.poll(async () => (await state(page)).kind).toBe('Text'); await frames(page);
  return state(page);
}
async function visibleField(page, section, label) {
  await expect.poll(async () => field(await state(page), section, label)?.width ?? 0).toBeGreaterThan(0);
  for (let i = 0; i < 8; i++) {
    const f = field(await state(page), section, label); const height = page.viewportSize().height;
    if (f.y > 145 && f.y + f.height < height - 30) return f;
    await page.mouse.move(f.x + Math.max(20, f.width / 2), Math.min(height - 80, Math.max(170, f.y)));
    await page.mouse.wheel(0, f.y <= 145 ? -350 : 350); await frames(page);
  }
  throw new Error(`Inspector field is not visible: ${section}/${label}`);
}
async function number(page, label, value) {
  const f = await visibleField(page, 'Type on a Path', label);
  await page.mouse.click(f.x + f.width * .8, f.y + f.height / 2);
  await page.keyboard.press('Control+a'); await page.keyboard.type(String(value)); await page.keyboard.press('Enter'); await frames(page);
}
async function button(page, label) {
  const f = await visibleField(page, 'Type on a Path', label);
  await page.mouse.click(f.x + f.width / 2, f.y + f.height / 2); await frames(page);
}
async function focusBrackets(page) {
  await page.keyboard.press('v'); await frames(page);
  const h = (await state(page)).typePathHandles?.[0]; expect(h).toBeTruthy();
  await page.mouse.click(h.x, h.y); await frames(page);
}
async function saved(page) {
  const event = page.waitForEvent('download'); await page.keyboard.press('Control+s');
  return JSON.parse(await fs.readFile(await (await event).path(), 'utf8'));
}

test('Type menu creates editable path text and retained inspector options persist through undo and recovery', async ({ page }) => {
  const errors = []; page.on('pageerror', e => errors.push(e.message));
  const initial = await create(page); expect(initial.pathText.error).toBeNull();
  expect(initial.typePathHandles).toHaveLength(3);
  await number(page, 'Start %', 20);
  await expect.poll(async () => (await state(page)).pathText.start).toBe(.2);
  await expect.poll(async () => field(await state(page), 'Type on a Path', 'Start %')?.value).toBe('20');
  const builds = (await state(page)).inspectorBuilds;
  await number(page, 'Baseline shift', 9);
  await expect.poll(async () => (await state(page)).pathText.baselineShift).toBe(9);
  expect((await state(page)).inspectorBuilds).toBe(builds);
  await focusBrackets(page); await page.keyboard.press('Control+z');
  await expect.poll(async () => (await state(page)).pathText.baselineShift).toBe(0);
  await page.keyboard.press('Control+Shift+z');
  await expect.poll(async () => (await state(page)).pathText.baselineShift).toBe(9);
  const document = await saved(page); expect(document.formatVersion).toBe(5);
  expect(findNode(document, initial.id)).toMatchObject({ kind: 'Text', text: 'ALPINE ECHOES — TYPE ON A PATH', textPath: { start: .2, baselineShift: 9 } });
  await fs.mkdir('artifacts/screenshots', { recursive: true }); await page.screenshot({ path: 'artifacts/screenshots/type-on-path-inspector.png' });
  await page.waitForTimeout(1500); await page.reload();
  await page.waitForFunction(() => globalThis.__artSpaceState?.ready, null, { timeout: 150_000 });
  const recovered = await saved(page); expect(findNode(recovered, initial.id).textPath).toMatchObject({ start: .2, baselineShift: 9 });
  expect(errors).toEqual([]);
});

test('path text bracket clicks are snapshot-free and dragging is one cancellable transaction', async ({ page }) => {
  await create(page, 'BRACKET EDITING'); await number(page, 'Start %', 20); await number(page, 'End %', 80);
  await focusBrackets(page); const initial = await state(page); const start = initial.typePathHandles[0];
  await page.mouse.click(start.x, start.y); await frames(page);
  expect((await state(page)).snapshots).toBe(initial.snapshots); expect((await state(page)).history).toBe(initial.history);
  await page.mouse.move(start.x, start.y); await page.mouse.down(); await page.mouse.move(start.x + 50, start.y, { steps: 12 }); await page.mouse.up();
  await expect.poll(async () => (await state(page)).history).toBe(initial.history + 1);
  expect((await state(page)).pathText.start).toBeGreaterThan(.2);
  expect((await state(page)).textBaselineBuilds).toBe(initial.textBaselineBuilds);
  await page.keyboard.press('Control+z'); await expect.poll(async () => (await state(page)).pathText.start).toBe(.2);
  await frames(page); const restored = await state(page); const end = restored.typePathHandles[1];
  await page.mouse.move(end.x, end.y); await page.mouse.down(); await page.mouse.move(end.x - 45, end.y - 5, { steps: 8 });
  await page.keyboard.press('Escape'); await page.mouse.up();
  await expect.poll(async () => (await state(page)).pathText.end).toBe(.8);
  expect((await state(page)).history).toBe(initial.history);
  expect((await state(page)).uiFailures).toBe(0);
});

test('baseline anchor edits preserve text and Create Outlines remains reversible', async ({ page }) => {
  const original = await create(page, 'CURVE EDITING');
  await button(page, 'Edit baseline anchors');
  await expect.poll(async () => (await state(page)).anchors.length).toBe(2);
  let current = await state(page); const anchor = current.anchors[0];
  await page.mouse.click(anchor.x, anchor.y); await frames(page); const before = await state(page);
  await page.mouse.move(anchor.x, anchor.y); await page.mouse.down(); await page.mouse.move(anchor.x + 25, anchor.y + 15, { steps: 10 }); await page.mouse.up();
  await expect.poll(async () => (await state(page)).history).toBe(before.history + 1);
  expect((await state(page)).kind).toBe('Text'); expect((await state(page)).pathText.error).toBeNull();
  const edited = await saved(page); expect(findNode(edited, original.id).text).toBe('CURVE EDITING');
  await button(page, 'Create Outlines'); await expect.poll(async () => (await state(page)).kind).toBe('Path');
  const outlined = await saved(page); expect(findNode(outlined, original.id).textPath).toBeUndefined();
  await page.keyboard.press('Control+z'); await expect.poll(async () => (await state(page)).kind).toBe('Text');
  expect((await state(page)).pathText.error).toBeNull();
  await fs.mkdir('artifacts/screenshots', { recursive: true }); await page.screenshot({ path: 'artifacts/screenshots/type-on-path-baseline-editing.png' });
});

test('rapid single-character inspector commits are synchronous, idempotent and cancellable', async ({ page }) => {
  await create(page, 'FAST INPUT');
  const builds = (await state(page)).inspectorBuilds;
  for (const value of [9, 4, -3, 0, 7]) {
    const before = await state(page);
    await number(page, 'Baseline shift', value);
    await expect.poll(async () => (await state(page)).pathText.baselineShift).toBe(value);
    await expect.poll(async () => (await state(page)).history).toBe(before.history + 1);
    await page.keyboard.press('Enter'); await frames(page);
    expect((await state(page)).history).toBe(before.history + 1);
  }
  const before = await state(page);
  const f = await visibleField(page, 'Type on a Path', 'Baseline shift');
  await page.mouse.click(f.x + f.width * .8, f.y + f.height / 2);
  await page.keyboard.press('Control+a'); await page.keyboard.type('5'); await page.keyboard.press('Escape');
  await page.keyboard.press('Tab'); await frames(page);
  expect((await state(page)).pathText.baselineShift).toBe(7);
  expect((await state(page)).history).toBe(before.history);
  expect((await state(page)).inspectorBuilds).toBe(builds);
  expect((await state(page)).uiFailures).toBe(0);
});
