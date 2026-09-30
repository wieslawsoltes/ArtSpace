namespace ArtSpace.Workbench;

public sealed partial class StudioWorkbench
{
    private CommandMenuBar? _applicationMenu;
    private ContentDialog? _activeDialog;
    public string? OpenMenuName => _applicationMenu?.OpenMenuName;
    public string? ActiveMenuCommandName => _applicationMenu?.ActiveCommandName;
    public string? ActiveDialogTitle => _activeDialog?.Title?.ToString();
    public string? FocusedControlName => XamlRoot is { } root && FocusManager.GetFocusedElement(root) is DependencyObject focused
        ? AutomationProperties.GetName(focused) : null;

    private bool HasOpenPopup => XamlRoot is { } root && VisualTreeHelper.GetOpenPopupsForXamlRoot(root).Any(p => p.IsOpen);

    /// <summary>
    /// Shared input ownership for host adapters and routed preview input. A menu owns its
    /// navigation even when a stale browser text overlay has not yet relinquished DOM focus.
    /// Other native text inputs and modal dialogs retain normal editing/focus traversal.
    /// </summary>
    public bool HandleHostNavigation(VirtualKey key, bool nativeTextInput = false)
    {
        if (_disposed || XamlRoot is null || _activeDialog is not null) return false;
        if (_applicationMenu?.IsOpen == true) return _applicationMenu.HandleNavigationKey(key);
        if (HasOpenPopup) return false;
        if (key == VirtualKey.S && (Surface.HostModifiers & (VirtualKeyModifiers.Control | VirtualKeyModifiers.Windows)) != 0)
        {
            // A browser root may not have a focused XAML control immediately after recovery.
            // Save still belongs to the application; consume it once before the native pipeline.
            RunAsync(SaveAsync); return true;
        }
        if (key != VirtualKey.Tab || nativeTextInput || Surface.IsTextEditing || Surface.IsPresenting) return false;
        if (Keyboard.IsTextInput(FocusManager.GetFocusedElement(XamlRoot) as DependencyObject)) return false;
        TogglePanels(); return true;
    }

    private void HandleApplicationPreviewKey(object sender, KeyRoutedEventArgs e)
    {
        if (e.Handled || _disposed || _activeDialog is not null) return;
        // The browser adapter can run before Uno processes a just-queued pointer click.
        // Recheck ownership when Uno delivers the routed key, before Button/focus defaults.
        if (_applicationMenu?.IsOpen == true && _applicationMenu.HandleNavigationKey(e.Key))
        { e.Handled = true; return; }
        if (Keyboard.Control && e.Key == VirtualKey.S && !HasOpenPopup)
        { RunAsync(SaveAsync); e.Handled = true; return; }
        if (e.Key == VirtualKey.Tab && !Surface.IsTextEditing && !Surface.IsPresenting && !HasOpenPopup
            && !Keyboard.IsTextInput(e.OriginalSource as DependencyObject))
        { TogglePanels(); e.Handled = true; }
    }
}
