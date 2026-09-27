namespace ArtSpace.Workbench;

public sealed partial class StudioWorkbench
{
    /// <summary>
    /// Routes host-level navigation into the same C# controls used by the desktop app.
    /// Returns false for text inputs and modal dialogs so native editing and focus traversal are preserved.
    /// </summary>
    public bool HandleHostNavigation(VirtualKey key)
    {
        if (_disposed || XamlRoot is null) return false;
        var menu = FindMenu(this);
        if (menu?.IsOpen == true) return menu.HandleNavigationKey(key);
        if (key != VirtualKey.Tab || Surface.IsTextEditing || Surface.IsPresenting) return false;
        var focused = FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
        if (Keyboard.IsTextInput(focused)) return false;
        if (VisualTreeHelper.GetOpenPopupsForXamlRoot(XamlRoot).Any(p => p.IsOpen)) return false;
        TogglePanels(); return true;
    }

    private static CommandMenuBar? FindMenu(DependencyObject root)
    {
        if (root is CommandMenuBar menu) return menu;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            if (FindMenu(VisualTreeHelper.GetChild(root, i)) is { } child) return child;
        return null;
    }
}
