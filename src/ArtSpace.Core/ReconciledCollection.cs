using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace ArtSpace.Core;

/// <summary>
/// Applies an immutable row projection by reference identity. Unchanged prefixes/suffixes keep
/// their containers; large changes publish one Reset instead of thousands of notifications.
/// </summary>
public sealed class ReconciledCollection<T> : ObservableCollection<T> where T : class
{
    public long ResetCount { get; private set; }
    public void Apply(IReadOnlyList<T> desired)
    {
        ArgumentNullException.ThrowIfNull(desired);
        if (ReferenceEquals(this, desired)) return;
        var prefix = 0;
        while (prefix < Count && prefix < desired.Count && ReferenceEquals(this[prefix], desired[prefix])) prefix++;
        if (prefix == Count && prefix == desired.Count) return;
        var suffix = 0;
        while (suffix < Count - prefix && suffix < desired.Count - prefix && ReferenceEquals(this[Count - suffix - 1], desired[desired.Count - suffix - 1])) suffix++;
        var removed = Count - prefix - suffix;
        var added = desired.Count - prefix - suffix;
        if (removed + added > 128)
        {
            CheckReentrancy();
            // Materialize before clearing, so views over this collection are also safe.
            var snapshot = desired.ToArray();
            Items.Clear();
            foreach (var item in snapshot) Items.Add(item);
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
            OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
            ResetCount++;
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
            return;
        }
        if (removed == added)
        {
            for (var i = 0; i < added; i++) if (!ReferenceEquals(this[prefix + i], desired[prefix + i])) this[prefix + i] = desired[prefix + i];
        }
        else
        {
            for (var i = removed - 1; i >= 0; i--) RemoveAt(prefix + i);
            for (var i = 0; i < added; i++) Insert(prefix + i, desired[prefix + i]);
        }
    }
}
