using Microsoft.UI.Xaml.Controls.Primitives;

namespace ArtSpace.Controls;

public sealed record MenuCommand(string Label, Action? Execute = null, string Shortcut = "", bool Enabled = true);

/// <summary>Custom application menus with explicit navigation state, independent of popup focus timing.</summary>
public sealed class CommandMenuBar : UserControl
{
    private readonly StackPanel _bar = new() { Orientation = Orientation.Horizontal };
    private readonly List<(StudioButton Button, MenuCommand Command)> _items = [];
    private Popup? _popup;
    private Control? _anchor;
    private int _activeIndex = -1;
    private bool _userNavigated;
    public bool IsOpen => _popup?.IsOpen == true;
    /// <summary>Raised after the open menu or active command changes; never requests layout or edits a document.</summary>
    public event Action? NavigationChanged;
    public string? OpenMenuName => IsOpen && _anchor is not null ? AutomationProperties.GetName(_anchor) : null;
    public string? ActiveCommandName => IsOpen && _activeIndex >= 0 && _activeIndex < _items.Count ? _items[_activeIndex].Command.Label : null;

    public CommandMenuBar()
    {
        Content = _bar;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        Unloaded += (_, _) => Close();
    }

    public void Add(string label, Func<IEnumerable<MenuCommand>> commands)
    {
        var button = new StudioButton(label, () => { }) { Height = 29, Padding = new(10, 3), CornerRadius = new(0) };
        button.Click += (_, _) => Open(button, commands());
        button.PreviewKeyDown += (_, e) => { if (!e.Handled && HandleNavigationKey(e.Key)) e.Handled = true; };
        _bar.Children.Add(button);
    }

    public void Close()
    {
        var popup = _popup; _popup = null;
        if (popup is not null) popup.IsOpen = false;
        _items.Clear(); _activeIndex = -1;
        if (popup is not null) NavigationChanged?.Invoke();
    }

    /// <summary>Host keyboard adapters can forward navigation when native/browser focus routing is unavailable.</summary>
    public bool HandleNavigationKey(VirtualKey key)
    {
        if (!IsOpen) return false;
        if (key is VirtualKey.Escape or VirtualKey.Tab)
        {
            var anchor = _anchor; Close(); anchor?.Focus(FocusState.Programmatic); return true;
        }
        if (key == VirtualKey.Enter || key == VirtualKey.Space)
        {
            _userNavigated = true;
            if (_activeIndex >= 0 && _activeIndex < _items.Count) Execute(_items[_activeIndex].Command);
            return true;
        }
        if (key is not VirtualKey.Down and not VirtualKey.Up and not VirtualKey.Home and not VirtualKey.End) return false;
        _userNavigated = true;
        var direction = key is VirtualKey.Up or VirtualKey.End ? -1 : 1;
        var index = key == VirtualKey.Home ? -1 : key == VirtualKey.End ? 0 : _activeIndex;
        for (var count = 0; count < _items.Count; count++)
        {
            index = (index + direction + _items.Count) % _items.Count;
            if (!_items[index].Command.Enabled) continue;
            Activate(index); break;
        }
        return true;
    }

    private void Activate(int index)
    {
        var changed = _activeIndex != index;
        var previous = _activeIndex; _activeIndex = index;
        if (changed)
        {
            if (previous >= 0 && previous < _items.Count) _items[previous].Button.IsSelected = false;
            if (index >= 0 && index < _items.Count) _items[index].Button.IsSelected = true;
        }
        // Re-focus even an unchanged initial index: popup loading can complete after its first activation.
        if (index >= 0 && index < _items.Count) _items[index].Button.Focus(FocusState.Keyboard);
        if (changed) NavigationChanged?.Invoke();
    }

    private void Execute(MenuCommand command)
    {
        if (!command.Enabled) return;
        var anchor = _anchor; Close(); anchor?.Focus(FocusState.Programmatic); command.Execute?.Invoke();
    }

    private void Open(Control anchor, IEnumerable<MenuCommand> commands)
    {
        Close(); _anchor = anchor; _userNavigated = false;
        var items = new StackPanel { Spacing = 1, Padding = new(4) };
        foreach (var command in commands)
        {
            if (command.Label.Length == 0)
            {
                items.Children.Add(new Border { Height = 1, Margin = new(4), Background = Studio.Brush(Studio.Line) }); continue;
            }
            var row = new Grid();
            row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            row.Children.Add(Studio.Text(command.Label, 11));
            var shortcut = Studio.Text(command.Shortcut, 10, Studio.Muted);
            Grid.SetColumn(shortcut, 1); row.Children.Add(shortcut);
            var button = new StudioButton
            {
                Height = 29, Padding = new(10, 3), CornerRadius = new(1), IsEnabled = command.Enabled,
                HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Content = row
            };
            AutomationProperties.SetName(button, command.Label);
            button.Click += (_, _) => Execute(command);
            button.PreviewKeyDown += (_, e) => { if (!e.Handled && HandleNavigationKey(e.Key)) e.Handled = true; };
            var index = _items.Count;
            button.PointerEntered += (_, _) => { if (command.Enabled) { _userNavigated = true; Activate(index); } };
            _items.Add((button, command)); items.Children.Add(button);
        }
        var position = anchor.TransformToVisual(null).TransformPoint(new(0, anchor.ActualHeight));
        var scroll = Studio.Scroll(items); scroll.MaxHeight = Math.Max(100, (XamlRoot?.Size.Height ?? 800) - position.Y - 8);
        var popup = new Popup
        {
            XamlRoot = XamlRoot, IsLightDismissEnabled = true,
            HorizontalOffset = Math.Max(0, Math.Min(position.X, (XamlRoot?.Size.Width ?? 1000) - 300)), VerticalOffset = position.Y,
            Child = new Border { Width = 300, Background = Studio.Brush("#303030"), BorderBrush = Studio.Brush("#171717"), BorderThickness = new(1), Padding = new(1), Child = scroll }
        };
        _popup = popup;
        popup.Closed += (_, _) => { if (ReferenceEquals(_popup, popup)) Close(); };
        void InitialFocus()
        {
            // Loading may finish after keyboard/pointer navigation. Never reset that explicit selection.
            if (!ReferenceEquals(_popup, popup) || _userNavigated) return;
            var first = _items.FindIndex(item => item.Command.Enabled); Activate(first);
        }
        items.Loaded += (_, _) => DispatcherQueue.TryEnqueue(InitialFocus);
        popup.IsOpen = true;
        InitialFocus();
        // Also publish menus containing no enabled command.
        if (_activeIndex < 0) NavigationChanged?.Invoke();
    }
}
