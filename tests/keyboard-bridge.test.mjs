import { test } from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import vm from 'node:vm';
const script = await fs.readFile(new URL('../src/ArtSpace.App/Platforms/WebAssembly/WasmScripts/Keyboard.js', import.meta.url), 'utf8');
function harness() {
  class Target {
    listeners = new Map();
    addEventListener(name, callback) { let set = this.listeners.get(name); if (!set) this.listeners.set(name, set = new Set()); set.add(callback); }
    removeEventListener(name, callback) { this.listeners.get(name)?.delete(callback); }
    fire(name, values = {}) {
      const event = { key: '', code: '', ctrlKey: false, altKey: false, metaKey: false, shiftKey: false, isComposing: false,
        prevented: false, stopped: false, preventDefault() { this.prevented = true; }, stopImmediatePropagation() { this.stopped = true; }, ...values };
      for (const callback of this.listeners.get(name) ?? []) callback(event);
      return event;
    }
  }
  class Element { constructor(editable = false) { this.isContentEditable = editable; } closest() { return this.isContentEditable ? this : null; } }
  const window = new Target(), document = new Target();
  const context = vm.createContext({ window, document, HTMLElement: Element }); vm.runInContext(script, context);
  return { window, document, Element, install: context.artSpaceKeyboard.install };
}
test('handled navigation consumes exactly its paired release across a focus/owner change', () => {
  const h = harness(), routes = [], modifiers = []; let menuOpen = true;
  h.install(key => { routes.push(key); const handled = menuOpen; menuOpen = false; return handled ? 1 : 0; }, value => modifiers.push(value));
  const press = h.window.fire('keydown', { key: 'Enter', code: 'Enter', target: new h.Element(true) });
  assert.equal(routes[0], 13 | 0x10000); assert.ok(press.prevented && press.stopped);
  const release = h.window.fire('keyup', { key: 'Enter', code: 'Enter', target: new h.Element(true) });
  assert.ok(release.prevented && release.stopped); assert.equal(routes.length, 1);
  assert.equal(h.window.fire('keyup', { key: 'Enter', code: 'Enter' }).prevented, false);
  assert.deepEqual(modifiers, [0]);
});
test('unowned native editing, IME, modifiers and unrelated shortcuts are not swallowed', () => {
  const h = harness(), routes = []; h.install(key => { routes.push(key); return 0; }, () => {});
  for (const [key, extra] of [['Enter', {}], ['Tab', {}], ['Escape', {}], ['s', { ctrlKey: true }]]) {
    const down = h.window.fire('keydown', { key, target: new h.Element(true), ...extra });
    const up = h.window.fire('keyup', { key }); assert.equal(down.prevented, false); assert.equal(up.prevented, false);
  }
  assert.deepEqual(routes, [13, 9, 27, 83].map(key => key | 0x10000));
  const count = routes.length;
  for (const extra of [{ isComposing: true }, { ctrlKey: true }, { altKey: true }, { metaKey: true }, { shiftKey: true }])
    assert.equal(h.window.fire('keydown', { key: 'Enter', ...extra }).prevented, false);
  assert.equal(routes.length, count);
});
test('global save, repeated presses and teardown use bounded ownership state', () => {
  const h = harness(), routes = []; h.install(key => { routes.push(key); return 1; }, () => {});
  for (const extra of [{ ctrlKey: true }, { metaKey: true }]) {
    assert.ok(h.window.fire('keydown', { key: 's', code: 'KeyS', ...extra }).prevented);
    assert.ok(h.window.fire('keyup', { key: 's', code: 'KeyS', ...extra }).prevented);
  }
  assert.deepEqual(routes, [83, 83]);
  h.window.fire('keydown', { key: 'Enter', code: 'Enter' }); h.window.fire('blur');
  assert.equal(h.window.fire('keyup', { key: 'Enter', code: 'Enter' }).prevented, false);
  h.window.fire('keydown', { key: 'Home', code: 'Home' }); h.document.hidden = true; h.document.fire('visibilitychange');
  assert.equal(h.window.fire('keyup', { key: 'Home', code: 'Home' }).prevented, false);
  let replacement = 0; h.install(() => { replacement++; return 0; }, () => {});
  const previous = routes.length; h.window.fire('keydown', { key: 'Enter' });
  assert.equal(routes.length, previous); assert.equal(replacement, 1);
});
