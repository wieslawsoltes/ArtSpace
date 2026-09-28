namespace ArtSpace.Controls;

/// <summary>A compact dock group with accessible tabs. The hosting application decides what panels contain.</summary>
public sealed class PanelDock : UserControl
{
    private readonly Grid _tabs = new() { Background = Studio.Brush("#292929") };
    private readonly ContentControl _body = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
    private readonly List<(string Name, UIElement Content, StudioButton Button)> _panels = [];
    public string SelectedName { get; private set; } = "";
    public event Action<string>? SelectionChanged;
    public PanelDock()
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        var grid = new Grid();
        grid.RowDefinitions.Add(new() { Height = new(31) });
        grid.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        grid.Children.Add(_tabs); Grid.SetRow(_body, 1); grid.Children.Add(_body); Content = grid;
    }
    public void Add(string name, UIElement content)
    {
        if (_panels.Any(p => p.Name == name)) throw new ArgumentException("Panel names must be unique.", nameof(name));
        var button = new StudioButton(name, () => Select(name)) { FontSize = 10, Padding = new(5, 3), Height = 31, CornerRadius = new(0), HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(button, name + " panel");
        _tabs.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        Grid.SetColumn(button, _panels.Count); _tabs.Children.Add(button); _panels.Add((name, content, button));
        if (_panels.Count == 1) Select(name);
    }
    public void Select(string name)
    {
        var index = _panels.FindIndex(p => p.Name == name);
        if (index < 0 || SelectedName == name) return;
        SelectedName = name; _body.Content = _panels[index].Content;
        foreach (var panel in _panels)
        {
            panel.Button.RestBackground = panel.Name == name ? "#3D3D3D" : "#292929";
            panel.Button.Background = Studio.Brush(panel.Button.RestBackground);
            panel.Button.BorderThickness = new(0, 0, 0, panel.Name == name ? 2 : 0);
            panel.Button.BorderBrush = Studio.Brush(Studio.Accent);
        }
        SelectionChanged?.Invoke(name);
    }
}

/// <summary>Pointer-captured horizontal resizing, with no dependency on a particular docking layout.</summary>
public sealed class DockResizeGrip : Border
{
    private bool _dragging;
    private double _lastX;
    public event Action<double>? DragDelta;
    public DockResizeGrip()
    {
        Width = 5; HorizontalAlignment = HorizontalAlignment.Left;
        Background = Studio.Brush("#252525");
        AutomationProperties.SetName(this, "Resize panels");
        PointerPressed += (_, e) => { _dragging = true; _lastX = e.GetCurrentPoint(null).Position.X; CapturePointer(e.Pointer); e.Handled = true; };
        PointerMoved += (_, e) =>
        {
            if (!_dragging) return;
            var x = e.GetCurrentPoint(null).Position.X; DragDelta?.Invoke(x - _lastX); _lastX = x; e.Handled = true;
        };
        PointerReleased += (_, e) => { _dragging = false; ReleasePointerCapture(e.Pointer); e.Handled = true; };
        PointerCanceled += (_, _) => _dragging = false;
        PointerCaptureLost += (_, _) => _dragging = false;
    }
}
