using System.Globalization;
using Windows.Foundation;

namespace ArtSpace.Controls;

/// <summary>
/// Retains named inspector sections independently of the inspected object. Shape keys describe
/// control topology, not object identity or values. Only active sections read the current model.
/// Use on the UI thread; factories and readers must not capture an obsolete model instance.
/// </summary>
public sealed class RetainedInspector : IDisposable
{
    private sealed record Entry(string Key, object? Shape, FrameworkElement View, InspectorBindings Bindings);
    private readonly Panel _host;
    private readonly Dictionary<string, Entry> _cache = new(StringComparer.Ordinal);
    private readonly List<Entry> _active = [];
    private readonly List<Entry> _next = [];
    private bool _retarget;
    public long SectionBuilds { get; private set; }
    public long Refreshes { get; private set; }
    public int RetainedSections => _cache.Count;

    public RetainedInspector(Panel host) => _host = host ?? throw new ArgumentNullException(nameof(host));

    public void Begin(bool retarget)
    {
        _retarget = retarget;
        _next.Clear();
        if (retarget) foreach (var entry in _active) entry.Bindings.CancelEdits();
    }

    public void Section(string title, object? shape, Action<StackPanel, InspectorBindings> build,
        string? actionGlyph = null, Action? action = null)
    {
        Use(title, shape, () =>
        {
            var bindings = new InspectorBindings();
            var section = new InspectorSection(title, actionGlyph, () => { if (bindings.CanWrite) action?.Invoke(); });
            build(section.Body, bindings);
            return (section, bindings);
        });
    }

    public void Use(string key, object? shape, Func<(FrameworkElement View, InspectorBindings Bindings)> factory)
    {
        if (!_cache.TryGetValue(key, out var entry) || !Equals(entry.Shape, shape))
        {
            var expanded = (entry?.View as InspectorSection)?.IsExpanded ?? true;
            if (entry is not null) { entry.Bindings.CancelEdits(); entry.Bindings.IsActive = false; }
            var content = factory();
            if (content.View is InspectorSection section) section.IsExpanded = expanded;
            entry = new(key, shape, content.View, content.Bindings);
            _cache[key] = entry;
            SectionBuilds++;
        }
        _next.Add(entry);
    }

    public void End()
    {
        foreach (var entry in _active)
        {
            if (_next.Contains(entry)) continue;
            entry.Bindings.CancelEdits();
            entry.Bindings.IsActive = false;
        }
        // Do not clear the visual tree. Common sections retain focus, scroll position and state.
        for (var i = 0; i < _next.Count; i++)
        {
            var view = _next[i].View;
            if (i < _host.Children.Count && ReferenceEquals(_host.Children[i], view)) continue;
            var previous = _host.Children.IndexOf(view);
            if (previous >= 0) _host.Children.RemoveAt(previous);
            _host.Children.Insert(i, view);
        }
        while (_host.Children.Count > _next.Count) _host.Children.RemoveAt(_host.Children.Count - 1);
        _active.Clear();
        _active.AddRange(_next);
        foreach (var entry in _active)
        {
            entry.Bindings.IsActive = true;
            entry.Bindings.Refresh(_retarget);
        }
        Refreshes++;
    }

    /// <summary>Read-only, opt-in UI diagnostics. Values come from the controls, not the model.</summary>
    public IEnumerable<InspectorFieldState> DescribeFields()
    {
        foreach (var entry in _active)
        {
            if (entry.View is InspectorSection { IsExpanded: false }) continue;
            foreach (var field in entry.Bindings.Describe(entry.Key)) yield return field;
        }
    }

    public void SuspendEditing()
    {
        foreach (var entry in _active) { entry.Bindings.CancelEdits(); entry.Bindings.IsActive = false; }
    }

    public void Dispose()
    {
        SuspendEditing();
        _host.Children.Clear(); _active.Clear(); _next.Clear(); _cache.Clear();
    }
}

public readonly record struct InspectorFieldState(string Section, string Label, string Value,
    double X, double Y, double Width, double Height);

/// <summary>One-way model refresh plus explicitly committed edits, without reflection or binding-path parsing.</summary>
public sealed class InspectorBindings
{
    private readonly List<Action<bool>> _readers = [];
    private readonly List<Action> _cancel = [];
    private readonly List<(string Label, FrameworkElement Control, Func<string> Value)> _fields = [];
    private bool _updating;
    internal bool IsActive { get; set; }
    public bool CanWrite => IsActive && !_updating;

    public void Observe(Action<bool> refresh) => _readers.Add(refresh);

    internal void Refresh(bool retarget)
    {
        _updating = true;
        try { foreach (var reader in _readers) reader(retarget); }
        finally { _updating = false; }
    }

    internal void CancelEdits()
    {
        _updating = true;
        try { foreach (var cancel in _cancel) cancel(); }
        finally { _updating = false; }
    }

    public NumericField Number(string label, Func<double> read, Action<double> write,
        double minimum = -1_000_000, double maximum = 1_000_000)
    {
        var field = new NumericField(label, read(), value => { if (CanWrite) write(value); })
        { Minimum = minimum, Maximum = maximum };
        Observe(retarget => field.UpdateFromModel(read(), retarget));
        _cancel.Add(field.CancelEdit);
        _fields.Add((label, field, () => field.Value.ToString("R", CultureInfo.InvariantCulture)));
        return field;
    }

    public ColorField Color(Func<string> read, Action<string> write, string label = "Hex color")
    {
        var field = new ColorField(read(), value => { if (CanWrite) write(value); });
        Observe(retarget => field.UpdateFromModel(read(), retarget));
        _cancel.Add(field.CancelEdit);
        _fields.Add((label, field, () => field.Value));
        return field;
    }

    public ComboBox Choice(IEnumerable<string> values, Func<string> read, Action<string> write, string label)
    {
        var choices = values.ToArray();
        var field = Studio.Choice(choices, read(), value => { if (CanWrite) write(value); }, label);
        Observe(retarget =>
        {
            if (retarget) field.IsDropDownOpen = false;
            var value = read();
            if (!Equals(field.SelectedItem, value)) field.SelectedItem = value;
        });
        _cancel.Add(() => field.IsDropDownOpen = false);
        _fields.Add((label, field, () => field.SelectedItem?.ToString() ?? ""));
        return field;
    }

    public CheckBox Check(string label, Func<bool> read, Action<bool> write)
    {
        var field = new CheckBox { Content = label, IsChecked = read(), FontFamily = Studio.Font,
            FontSize = 11, MinWidth = 0, MinHeight = 28, Padding = new(0), VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(field, label);
        field.Checked += (_, _) => { if (CanWrite) write(true); };
        field.Unchecked += (_, _) => { if (CanWrite) write(false); };
        Observe(_ => { var value = read(); if (field.IsChecked != value) field.IsChecked = value; });
        _fields.Add((label, field, () => (field.IsChecked == true).ToString()));
        return field;
    }

    public TextBlock Text(Func<string> read, double size = 11, string color = Studio.Ink, bool bold = false)
    {
        var field = Studio.Text(read(), size, color, bold);
        Observe(_ => { var value = read(); if (field.Text != value) field.Text = value; });
        return field;
    }

    public TextBox Input(string label, Func<string> read, Action<string> write)
    {
        var modelValue = read();
        var field = Studio.Input(modelValue, label);
        var dirty = false;
        var writing = false;
        void Display(string value)
        {
            writing = true;
            try { if (field.Text != value) field.Text = value; dirty = false; }
            finally { writing = false; }
        }
        field.TextChanged += (_, _) => { if (!writing && !_updating) dirty = true; };
        field.LostFocus += (_, _) =>
        {
            if (!CanWrite || !dirty) return;
            var value = field.Text;
            dirty = false;
            if (value != read()) write(value);
        };
        Observe(retarget => { modelValue = read(); if (retarget || !dirty || field.FocusState == FocusState.Unfocused) Display(modelValue); });
        _cancel.Add(() => Display(modelValue));
        _fields.Add((label, field, () => field.Text));
        return field;
    }

    public StudioButton Button(Func<string> read, Action write)
    {
        var field = new StudioButton(read(), () => { if (CanWrite) write(); });
        _fields.Add((read(), field, () => field.Content?.ToString() ?? ""));
        Observe(_ =>
        {
            var value = read();
            if (Equals(field.Content, value)) return;
            field.Content = value; AutomationProperties.SetName(field, value);
        });
        return field;
    }

    public IconButton Icon(Func<string> read, string label, Action write)
    {
        var field = new IconButton(read(), label, () => { if (CanWrite) write(); });
        _fields.Add((label, field, () => field.Glyph));
        Observe(_ => { var value = read(); if (field.Glyph != value) field.Glyph = value; });
        return field;
    }

    internal IEnumerable<InspectorFieldState> Describe(string section)
    {
        foreach (var field in _fields)
        {
            if (!field.Control.IsLoaded) continue;
            var bounds = field.Control.TransformToVisual(null).TransformBounds(new Rect(0, 0,
                field.Control.ActualWidth, field.Control.ActualHeight));
            yield return new(section, field.Label, field.Value(), bounds.X, bounds.Y, bounds.Width, bounds.Height);
        }
    }
}
