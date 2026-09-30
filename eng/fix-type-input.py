from pathlib import Path
r=Path('.')
def patch(p,a,b):
 f=r/p; s=f.read_text(); assert a in s,(p,a[:120]); f.write_text(s.replace(a,b))
patch('src/ArtSpace.Workbench/StudioWorkbench.Illustration.cs','var menu = new CommandMenuBar();','var menu = _applicationMenu = new CommandMenuBar();')
patch('src/ArtSpace.Workbench/StudioWorkbench.cs','''        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == VirtualKey.Tab && !Keyboard.IsTextInput(e.OriginalSource as DependencyObject))
            {
                TogglePanels(); e.Handled = true;
            }
        };''','''        PreviewKeyDown += HandleApplicationPreviewKey;
        Loaded += (_, _) => DispatcherQueue.TryEnqueue(() =>
        {
            // Do not steal a user's focus if an input/menu already acquired it during startup.
            if (!_disposed && XamlRoot is { } root && FocusManager.GetFocusedElement(root) is null)
                Surface.FocusCanvas();
        });''')
patch('src/ArtSpace.Workbench/StudioWorkbench.Commands.cs','''        if (Keyboard.IsTextInput(e.OriginalSource as DependencyObject)) return;
        try''','''        if (Keyboard.IsTextInput(e.OriginalSource as DependencyObject)) return;
        // Escape ends an in-flight gesture first. A second Escape can deselect the artwork.
        if (!control && e.Key == VirtualKey.Escape && Surface.TryCancelGesture())
        { e.Handled = true; return; }
        try''')
patch('src/ArtSpace.Editor/DesignSurface.cs','''    public void CancelGesture()
    {''','''    /// <summary>Cancel pointer work without also clearing the restored object selection.</summary>
    public bool TryCancelGesture()
    {
        if (_gesture == Gesture.None && Session?.IsInteracting != true) return false;
        CancelGesture(); return true;
    }
    public void CancelGesture()
    {''')
patch('src/ArtSpace.Editor/DesignSurface.cs','        ResetPathGesture(); Session?.CancelInteraction(); _canvas.Invalidate();','        ResetPathGesture(); Session?.CancelInteraction(); _canvas.ReleasePointerCaptures(); _canvas.Invalidate();')
patch('src/ArtSpace.Workbench/StudioWorkbench.Commands.cs','''    private ContentDialog Dialog(string title, UIElement content, string primary = "", string close = "Close") => new()
    {
        Title = title, Content = content, PrimaryButtonText = primary, CloseButtonText = close, XamlRoot = XamlRoot,
        FontFamily = Studio.Font, RequestedTheme = ElementTheme.Dark, DefaultButton = string.IsNullOrEmpty(primary) ? ContentDialogButton.Close : ContentDialogButton.Primary,
        MinWidth = 320, MaxWidth = 560
    };''','''    private ContentDialog Dialog(string title, UIElement content, string primary = "", string close = "Close")
    {
        var dialog = new ContentDialog
        {
            Title = title, Content = content, PrimaryButtonText = primary, CloseButtonText = close, XamlRoot = XamlRoot,
            FontFamily = Studio.Font, RequestedTheme = ElementTheme.Dark,
            DefaultButton = string.IsNullOrEmpty(primary) ? ContentDialogButton.Close : ContentDialogButton.Primary,
            MinWidth = 320, MaxWidth = 560
        };
        dialog.Opened += (_, _) => { _activeDialog = dialog; UiRefreshed?.Invoke(); };
        dialog.Closed += (_, _) =>
        {
            if (ReferenceEquals(_activeDialog, dialog)) _activeDialog = null;
            UiRefreshed?.Invoke();
        };
        return dialog;
    }''')
patch('src/ArtSpace.App/Platforms/WebAssembly/BrowserKeyboard.cs','Install(key => workbench.HandleHostNavigation((VirtualKey)key) ? 1 : 0,','Install(key => workbench.HandleHostNavigation((VirtualKey)(key & 0xffff), (key & 0x10000) != 0) ? 1 : 0,')
patch('src/ArtSpace.Controls/CommandMenuBar.cs','button.KeyDown += (_, e) => { if (HandleNavigationKey(e.Key)) e.Handled = true; };','button.PreviewKeyDown += (_, e) => { if (!e.Handled && HandleNavigationKey(e.Key)) e.Handled = true; };')
patch('src/ArtSpace.Controls/CommandMenuBar.cs','    public bool IsOpen => _popup?.IsOpen == true;','''    public bool IsOpen => _popup?.IsOpen == true;
    public string? OpenMenuName => IsOpen && _anchor is not null ? AutomationProperties.GetName(_anchor) : null;
    public string? ActiveCommandName => IsOpen && _activeIndex >= 0 && _activeIndex < _items.Count ? _items[_activeIndex].Command.Label : null;''')
patch('src/ArtSpace.App/Platforms/WebAssembly/BrowserWorkspaceStorage.cs','                json.WriteString("activePanel", workbench.ActivePanel);','''                json.WriteString("activePanel", workbench.ActivePanel);
                json.WriteString("openMenu", workbench.OpenMenuName);
                json.WriteString("activeMenuCommand", workbench.ActiveMenuCommandName);
                json.WriteString("dialogTitle", workbench.ActiveDialogTitle);
                json.WriteString("focusedControl", workbench.FocusedControlName);''')
patch('tests/browser/type-on-path.spec.mjs',"  await page.mouse.click(200, 15); await page.keyboard.press('Home'); await page.keyboard.press('Enter');\n  await page.waitForTimeout(350);", """  await page.mouse.click(200, 15);
  await page.waitForFunction(() => globalThis.__artSpaceState?.openMenu === 'Type');
  await page.keyboard.press('Home'); await page.keyboard.press('Enter');
  // Observe the actual opened/focused XAML input, not a guessed animation delay.
  await page.waitForFunction(() => globalThis.__artSpaceState?.dialogTitle === 'Type on a Path'
    && globalThis.__artSpaceState?.focusedControl === 'Type on a Path');""")
patch('tests/browser/type-on-path.spec.mjs',"  await expect.poll(async () => (await state(page)).pathText.end).toBe(.8);", "  await expect.poll(async () => (await state(page)).id).toBe(initial.id);\n  await expect.poll(async () => (await state(page)).pathText?.end).toBe(.8);")
p=r/'tests/browser/type-on-path.spec.mjs'
p.write_text(p.read_text()+"""

test('menu presses keep ownership across popup closure and a modal never edits the background', async ({ page }) => {
  const initial = await ready(page);
  for (let repeat = 0; repeat < 3; repeat++) {
    // Deliberately do not wait between pointer and navigation; the shared routed-preview
    // fallback must preserve event order when the browser bridge sees an unopened popup.
    await page.mouse.click(200, 15);
    await page.keyboard.press('Home');
    await page.keyboard.press('Enter');
    await page.waitForFunction(() => globalThis.__artSpaceState?.dialogTitle === 'Type on a Path'
      && globalThis.__artSpaceState?.focusedControl === 'Type on a Path');
    await page.keyboard.press('Control+a'); await page.keyboard.type('DO NOT COMMIT');
    expect((await state(page)).selection).toBe(initial.selection);
    await page.keyboard.press('Escape');
    await page.waitForFunction(() => globalThis.__artSpaceState?.dialogTitle == null);
    expect((await state(page)).id).toBe(initial.id);
    expect((await state(page)).history).toBe(initial.history);
  }
  expect((await state(page)).uiFailures).toBe(0);
});
""")
patch('.github/workflows/build.yml','          npm ci\n          npx playwright','          npm ci\n          node --test tests/keyboard-bridge.test.mjs\n          npx playwright')
