using Microsoft.UI.Xaml.Controls.Primitives;

namespace ArtSpace.Controls;

public sealed record MenuCommand(string Label, Action? Execute = null, string Shortcut = "", bool Enabled = true);

/// <summary>Compact custom-rendered application menu. Popup placement and input are shared on desktop and WebAssembly.</summary>
public sealed class CommandMenuBar : UserControl
{
    private readonly StackPanel _bar = new() { Orientation = Orientation.Horizontal, Spacing = 0 };
    private Popup? _popup;
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
        _bar.Children.Add(button);
    }
    public void Close()
    {
        if (_popup is not null) _popup.IsOpen = false;
        _popup = null;
    }
    private void Open(FrameworkElement anchor, IEnumerable<MenuCommand> commands)
    {
        Close();
        var items = new StackPanel { Spacing = 1, Padding = new(4) };
        var buttons = new List<StudioButton>();
        foreach (var command in commands)
        {
            if (command.Label.Length == 0) { items.Children.Add(new Border { Height = 1, Margin = new(4, 4, 4, 4), Background = Studio.Brush(Studio.Line) }); continue; }
            var button = new StudioButton
            {
                Height = 29, Padding = new(10, 3), CornerRadius = new(1), IsEnabled = command.Enabled,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Content = Studio.Columns((Studio.Text(command.Label, 11), -1), (Studio.Text(command.Shortcut, 10, Studio.Muted), 100))
            };
            AutomationProperties.SetName(button, command.Label);
            button.Click += (_, _) => { Close(); command.Execute?.Invoke(); };
            button.KeyDown += (_, e) =>
            {
                if (e.Key == VirtualKey.Escape) { Close(); (anchor as Control)?.Focus(FocusState.Programmatic); e.Handled = true; }
                if (e.Key is VirtualKey.Down or VirtualKey.Up)
                {
                    var index = buttons.IndexOf(button); var direction = e.Key == VirtualKey.Down ? 1 : -1;
                    for (var count = 0; count < buttons.Count; count++)
                    {
                        index = (index + direction + buttons.Count) % buttons.Count;
                        if (!buttons[index].IsEnabled) continue;
                        buttons[index].Focus(FocusState.Keyboard); break;
                    }
                    e.Handled = true;
                }
            };
            items.Children.Add(button); buttons.Add(button);
        }
        var position = anchor.TransformToVisual(null).TransformPoint(new(0, anchor.ActualHeight));
        var scroll = Studio.Scroll(items); scroll.MaxHeight = Math.Max(100, (XamlRoot?.Size.Height ?? 800) - position.Y - 8);
        _popup = new Popup
        {
            XamlRoot = XamlRoot, IsLightDismissEnabled = true,
            HorizontalOffset = Math.Max(0, Math.Min(position.X, (XamlRoot?.Size.Width ?? 1000) - 300)), VerticalOffset = position.Y,
            Child = new Border { Width = 300, Background = Studio.Brush("#303030"), BorderBrush = Studio.Brush("#171717"), BorderThickness = new(1), Padding = new(1), Child = scroll }
        };
        _popup.IsOpen = true;
        buttons.FirstOrDefault(b => b.IsEnabled)?.Focus(FocusState.Keyboard);
    }
}
