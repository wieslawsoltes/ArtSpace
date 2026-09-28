namespace ArtSpace.Controls;

public sealed class LayerEntry
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Glyph { get; init; } = "rectangle";
    public int Depth { get; init; }
    public bool HasChildren { get; init; }
    public bool Expanded { get; init; }
    public bool Visible { get; init; } = true;
    public bool Locked { get; init; }
    public bool IsComponent { get; init; }
    public Action? ToggleExpanded { get; init; }
    public Action? ToggleVisibility { get; init; }
    public Action? ToggleLocked { get; init; }
    public Action? Rename { get; init; }
}

/// <summary>A virtualized row whose visual tree survives recycling and metadata changes.</summary>
public sealed class LayerRow : UserControl
{
    private readonly IconButton _expander, _visibility, _locked;
    private readonly IconView _icon;
    private readonly TextBlock _title;
    private readonly Grid _grid;
    private LayerEntry? _entry;
    private bool _hover;

    public LayerRow()
    {
        _expander = new IconButton("chevron-right", "Expand layer", () => _entry?.ToggleExpanded?.Invoke()) { Width = 20, Height = 28, Padding = new(3), IsTabStop = false };
        _icon = new IconView { Width = 14, Height = 14, VerticalAlignment = VerticalAlignment.Center };
        _title = Studio.Text("", 11);
        _visibility = new IconButton("eye", "Toggle layer visibility", () => _entry?.ToggleVisibility?.Invoke()) { Width = 22, Height = 28, Padding = new(4), IsTabStop = false };
        _locked = new IconButton("unlock", "Toggle layer lock", () => _entry?.ToggleLocked?.Invoke()) { Width = 22, Height = 28, Padding = new(4), IsTabStop = false };
        _grid = Studio.Columns((_expander, 20), (_icon, 14), (_title, -1), (_locked, 22), (_visibility, 22));
        _grid.ColumnSpacing = 5; _grid.Height = 30; Content = _grid;
        _grid.PointerEntered += (_, _) => { _hover = true; UpdateActions(); };
        _grid.PointerExited += (_, _) => { _hover = false; UpdateActions(); };
        DataContextChanged += (_, _) => Refresh();
        DoubleTapped += (_, e) => { if (_entry is not null) { _entry.Rename?.Invoke(); e.Handled = true; } };
    }

    private void Refresh()
    {
        var entry = DataContext as LayerEntry;
        if (ReferenceEquals(_entry, entry)) return;
        _entry = entry;
        if (entry is null) { _title.Text = ""; IsHitTestVisible = false; return; }
        IsHitTestVisible = true;
        _expander.Glyph = entry.Expanded ? "chevron-down" : "chevron-right";
        _expander.Opacity = entry.HasChildren ? 1 : 0; _expander.IsHitTestVisible = entry.HasChildren;
        _icon.Glyph = entry.Glyph; _icon.Color = entry.IsComponent ? "#9747FF" : "#A7A7A7";
        if (_title.Text != entry.Name) _title.Text = entry.Name;
        _title.Foreground = Studio.Brush(entry.IsComponent ? "#9747FF" : Studio.Ink); _title.Opacity = entry.Visible ? 1 : .4;
        _visibility.Glyph = entry.Visible ? "eye" : "eye-off"; _locked.Glyph = entry.Locked ? "lock" : "unlock";
        _grid.Margin = new(Math.Min(96, entry.Depth * 14), 0, 4, 0);
        AutomationProperties.SetName(this, entry.Name);
        AutomationProperties.SetName(_expander, "Expand " + entry.Name);
        AutomationProperties.SetName(_visibility, (entry.Visible ? "Hide " : "Show ") + entry.Name);
        AutomationProperties.SetName(_locked, (entry.Locked ? "Unlock " : "Lock ") + entry.Name);
        ToolTipService.SetToolTip(_expander, "Expand " + entry.Name);
        ToolTipService.SetToolTip(_visibility, (entry.Visible ? "Hide " : "Show ") + entry.Name);
        ToolTipService.SetToolTip(_locked, (entry.Locked ? "Unlock " : "Lock ") + entry.Name);
        UpdateActions();
    }
    private void UpdateActions()
    {
        _visibility.Opacity = _hover ? .65 : _entry?.Visible == false ? .7 : 0;
        _locked.Opacity = _hover ? .65 : _entry?.Locked == true ? .7 : 0;
    }
}
