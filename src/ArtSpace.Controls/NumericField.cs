using System.Globalization;

namespace ArtSpace.Controls;

/// <summary>A retained numeric editor with explicit commits and cancellable label-drag scrubbing.</summary>
public sealed class NumericField : UserControl
{
    private readonly TextBox _input;
    private readonly Border _prefix;
    private double _value, _startX, _startValue;
    private bool _scrubbing, _dirty, _writing;
    public event Action<double>? ValueCommitted;
    public double Minimum { get; set; } = -1_000_000;
    public double Maximum { get; set; } = 1_000_000;
    public double Step { get; set; } = 1;
    public double Value
    {
        get => _value;
        set { _value = double.IsFinite(value) ? value : 0; Display(); }
    }

    public NumericField(string label, double value, Action<double>? commit = null)
    {
        _input = Studio.Input(); _input.Background = Studio.Brush("#00FFFFFF"); _input.Padding = new(1, 4, 5, 4);
        AutomationProperties.SetName(_input, label); ToolTipService.SetToolTip(_input, label);
        _prefix = new Border { MinWidth = 28, MaxWidth = 120, Background = Studio.Brush("#00FFFFFF"), Child = Studio.Text(label, 10, Studio.Muted), Padding = new(8, 0, 6, 0) };
        ToolTipService.SetToolTip(_prefix, label);
        var grid = Studio.Columns((_prefix, 28), (_input, -1)); grid.ColumnSpacing = 0;
        grid.ColumnDefinitions[0].Width = GridLength.Auto;
        SizeChanged += (_, e) =>
        {
            // Fit descriptive labels without consuming the editable numeric area in compact rows.
            var width = Math.Max(28, Math.Min(120, e.NewSize.Width * .48));
            if (_prefix.MaxWidth != width) _prefix.MaxWidth = width;
        };
        Content = new Border { Background = Studio.Brush(Studio.Field), CornerRadius = new(5), Child = grid, Height = 30 };
        Value = value;
        if (commit is not null) ValueCommitted += commit;
        _input.TextChanged += (_, _) => { if (!_writing) _dirty = true; };
        _input.LostFocus += (_, _) => Commit();
        _input.KeyDown += (_, e) =>
        {
            if (e.Key == VirtualKey.Enter) { Commit(); e.Handled = true; }
            else if (e.Key == VirtualKey.Escape) { CancelEdit(); e.Handled = true; }
            else if (e.Key is VirtualKey.Up or VirtualKey.Down)
            {
                var previous = Value;
                Value = Math.Clamp(Parse(_input.Text, _value) + Step * (e.Key == VirtualKey.Up ? 1 : -1), Minimum, Maximum);
                if (Value != previous) ValueCommitted?.Invoke(Value);
                e.Handled = true;
            }
        };
        _prefix.PointerPressed += (_, e) =>
        {
            _scrubbing = true; _startX = e.GetCurrentPoint(_prefix).Position.X; _startValue = Value;
            _prefix.CapturePointer(e.Pointer); e.Handled = true;
        };
        _prefix.PointerMoved += (_, e) =>
        {
            if (_scrubbing) Value = Math.Clamp(_startValue + Math.Round(e.GetCurrentPoint(_prefix).Position.X - _startX) * Step, Minimum, Maximum);
        };
        _prefix.PointerReleased += (_, e) =>
        {
            if (!_scrubbing) return;
            _scrubbing = false; _prefix.ReleasePointerCapture(e.Pointer);
            if (Value != _startValue) ValueCommitted?.Invoke(Value);
            e.Handled = true;
        };
        _prefix.PointerCanceled += (_, _) => CancelScrub();
        _prefix.PointerCaptureLost += (_, _) => CancelScrub();
    }

    /// <summary>Refresh without resetting a user's uncommitted text or scrubbing on the same target.</summary>
    public void UpdateFromModel(double value, bool retarget = false)
    {
        if (retarget) CancelEdit();
        else if (_scrubbing || (_dirty && _input.FocusState != FocusState.Unfocused)) return;
        Value = value;
    }

    /// <summary>Discard pending input before recycling or retargeting the control.</summary>
    public void CancelEdit()
    {
        if (_scrubbing) { _scrubbing = false; _value = _startValue; _prefix.ReleasePointerCaptures(); Display(); }
        if (_dirty) Display();
    }

    private void CancelScrub()
    {
        if (!_scrubbing) return;
        _scrubbing = false; Value = _startValue;
    }

    private void Display()
    {
        var text = _value.ToString("0.##", CultureInfo.InvariantCulture);
        _writing = true;
        try { if (_input.Text != text) _input.Text = text; _dirty = false; }
        finally { _writing = false; }
    }

    private void Commit()
    {
        if (!_dirty) return;
        if (!double.TryParse(_input.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value)) { Display(); return; }
        value = Math.Clamp(value, Minimum, Maximum);
        var changed = value != _value;
        Value = value;
        if (changed) ValueCommitted?.Invoke(value);
    }
    private static double Parse(string text, double fallback) => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) ? value : fallback;
}
