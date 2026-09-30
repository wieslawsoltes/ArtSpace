import { test } from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import vm from 'node:vm';

const script = await fs.readFile(new URL('../src/ArtSpace.App/Platforms/WebAssembly/WasmScripts/Storage.js', import.meta.url), 'utf8');
function harness(search = '?test=1') {
  const context = vm.createContext({ location: { search }, URLSearchParams, document: { addEventListener() {} } });
  vm.runInContext(script, context);
  return { context, storage: context.artSpaceStorage };
}
test('menu diagnostics never advertise readiness and remain disabled in production', () => {
  for (const search of ['', '?test=0', '?test=1']) {
    const { context, storage } = harness(search);
    storage.publishMenuDiagnostics('File', 'New…');
    assert.equal(context.__artSpaceState, undefined);
    storage.publishDiagnostics(JSON.stringify({ ready: true, openMenu: null, activeMenuCommand: null }));
    storage.publishMenuDiagnostics('File', 'Open…');
    if (search === '?test=1') assert.equal(context.__artSpaceState.activeMenuCommand, 'Open…');
    else assert.equal(context.__artSpaceState, undefined);
  }
});
test('menu updates preserve frozen scene state and reuse identical observations', () => {
  const { context, storage } = harness();
  storage.publishDiagnostics(JSON.stringify({ ready: true, history: 7, sceneRecordings: 2, inspectorFields: [{ value: '42' }], openMenu: null, activeMenuCommand: null }));
  const before = context.__artSpaceState;
  storage.publishMenuDiagnostics('File', 'Export Editable SVG…');
  const after = context.__artSpaceState;
  assert.ok(Object.isFrozen(after)); assert.notEqual(before, after);
  assert.equal(before.openMenu, null); assert.equal(after.openMenu, 'File');
  assert.equal(after.inspectorFields, before.inspectorFields);
  assert.equal(after.history, 7); assert.equal(after.sceneRecordings, 2); assert.equal(after.ready, true);
  storage.publishMenuDiagnostics('File', 'Export Editable SVG…');
  assert.equal(context.__artSpaceState, after);
  storage.publishMenuDiagnostics(null, null);
  assert.equal(context.__artSpaceState.openMenu, null); assert.equal(context.__artSpaceState.activeMenuCommand, null);
});
test('scalar frame and menu reports preserve each other and later authoritative snapshots', () => {
  const { context, storage } = harness();
  storage.publishDiagnostics(JSON.stringify({ ready: true, openMenu: 'File', activeMenuCommand: 'New…', history: 0 }));
  storage.publishMenuDiagnostics('File', 'Open…');
  storage.publishFrameDiagnostics(5, 8, 12, 20, 0, 0, 0, 0, 0);
  assert.equal(context.__artSpaceState.activeMenuCommand, 'Open…');
  assert.equal(context.__artSpaceState.sceneReplays, 8);
  storage.publishMenuDiagnostics('File', 'Save a Copy…');
  assert.equal(context.__artSpaceState.sceneReplays, 8); assert.equal(context.__artSpaceState.history, 0);
  storage.publishDiagnostics(JSON.stringify({ ready: true, openMenu: null, activeMenuCommand: null, history: 1 }));
  assert.equal(context.__artSpaceState.openMenu, null); assert.equal(context.__artSpaceState.history, 1);
});
