"use strict";
// Browser capability adapter only: the shared C# controls own commands and geometry.
(() => {
  let dispose;
  const keys = Object.freeze({ Tab: 9, Enter: 13, Escape: 27, ArrowUp: 38, ArrowDown: 40, Home: 36, End: 35 });
  globalThis.artSpaceKeyboard = Object.freeze({
    install(route, modifiersChanged) {
      dispose?.();
      const removers = [];
      const listen = (target, name, callback) => {
        target.addEventListener(name, callback, true);
        removers.push(() => target.removeEventListener(name, callback, true));
      };
      let last = -1;
      const publish = value => { if (value !== last) { last = value; modifiersChanged(value); } };
      // Windows.System.VirtualKeyModifiers: Control=1, Menu(Alt)=2, Shift=4, Windows=8.
      const update = event => publish((event.ctrlKey ? 1 : 0) | (event.altKey ? 2 : 0) |
        (event.shiftKey ? 4 : 0) | (event.metaKey ? 8 : 0));
      listen(window, 'keydown', event => {
        update(event);
        if (event.ctrlKey || event.altKey || event.metaKey || event.shiftKey || event.isComposing) return;
        const key = keys[event.key];
        if (!key) return;
        const target = event.target;
        if (target instanceof HTMLElement && (target.isContentEditable || target.closest('input, textarea, select, [contenteditable="true"]'))) return;
        if (route(key) !== 1) return;
        event.preventDefault(); event.stopImmediatePropagation();
      });
      listen(window, 'keyup', update);
      // Capture modifier state before Uno consumes the pointer event; no bridge call while unchanged.
      for (const name of ['pointerdown', 'pointermove', 'pointerup']) listen(window, name, update);
      listen(window, 'blur', () => publish(0));
      listen(document, 'visibilitychange', () => { if (document.hidden) publish(0); });
      publish(0);
      dispose = () => { for (const remove of removers) remove(); };
    }
  });
})();
