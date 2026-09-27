"use strict";
// Forward only navigation keys that browser/native focus routing can consume before Uno's routed events.
// The workbench makes every decision; ordinary typing, shortcuts, browser controls and dialogs remain native.
(() => {
  let listener;
  const keys = Object.freeze({ Tab: 9, Enter: 13, Escape: 27, ArrowUp: 38, ArrowDown: 40, Home: 36, End: 35 });
  globalThis.artSpaceKeyboard = Object.freeze({
    install(route) {
      if (listener) window.removeEventListener('keydown', listener, true);
      listener = event => {
        if (event.ctrlKey || event.altKey || event.metaKey || event.shiftKey || event.isComposing) return;
        const key = keys[event.key];
        if (!key) return;
        const target = event.target;
        if (target instanceof HTMLElement && (target.isContentEditable || target.closest('input, textarea, select, [contenteditable="true"]'))) return;
        if (route(key) !== 1) return;
        event.preventDefault(); event.stopImmediatePropagation();
      };
      window.addEventListener('keydown', listener, true);
    }
  });
})();
