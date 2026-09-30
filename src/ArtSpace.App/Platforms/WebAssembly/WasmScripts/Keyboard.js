"use strict";
// Browser capability adapter only: shared C# controls decide command/input ownership.
(() => {
  let dispose;
  const keys = Object.freeze({ Tab: 9, Enter: 13, Escape: 27, ArrowUp: 38, ArrowDown: 40, Home: 36, End: 35 });
  const nativeTextFlag = 0x10000;
  globalThis.artSpaceKeyboard = Object.freeze({
    install(route, modifiersChanged) {
      dispose?.();
      const removers = [], ownedPresses = new Set();
      const listen = (target, name, callback) => {
        target.addEventListener(name, callback, true);
        removers.push(() => target.removeEventListener(name, callback, true));
      };
      let last = -1;
      const publish = value => { if (value !== last) { last = value; modifiersChanged(value); } };
      const update = event => publish((event.ctrlKey ? 1 : 0) | (event.altKey ? 2 : 0) |
        (event.shiftKey ? 4 : 0) | (event.metaKey ? 8 : 0));
      const identity = event => event.code || event.key;
      const consume = event => { event.preventDefault(); event.stopImmediatePropagation(); };
      listen(window, 'keydown', event => {
        update(event);
        if (event.isComposing) return;
        const save = (event.ctrlKey || event.metaKey) && !event.altKey && !event.shiftKey && event.key.toLowerCase() === 's';
        if (!save && (event.ctrlKey || event.altKey || event.metaKey || event.shiftKey)) return;
        const key = save ? 83 : keys[event.key];
        if (!key) return;
        const target = event.target;
        const text = target instanceof HTMLElement && (target.isContentEditable || target.closest('input, textarea, select, [contenteditable="true"]'));
        // Do not infer XAML menu ownership from the HTML overlay's asynchronous focus state.
        if (route(key | (text ? nativeTextFlag : 0)) !== 1) return;
        ownedPresses.add(identity(event)); consume(event);
      });
      listen(window, 'keyup', event => {
        update(event);
        // A consumed press owns its release even if the command closed a menu/opened a dialog.
        // Unpaired keyups can otherwise activate a different, newly focused native button.
        if (ownedPresses.delete(identity(event))) consume(event);
      });
      for (const name of ['pointerdown', 'pointermove', 'pointerup']) listen(window, name, update);
      const reset = () => { ownedPresses.clear(); publish(0); };
      listen(window, 'blur', reset);
      listen(document, 'visibilitychange', () => { if (document.hidden) reset(); });
      publish(0);
      dispose = () => { for (const remove of removers) remove(); ownedPresses.clear(); };
    }
  });
})();
