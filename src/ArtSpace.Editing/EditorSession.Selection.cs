using ArtSpace.Core;

namespace ArtSpace.Editing;

public sealed partial class EditorSession
{
    private readonly record struct IndexedNode(DesignNode Node, int Order);
    private Dictionary<string, IndexedNode>? _selectionIndex;
    private IReadOnlyList<DesignNode>? _selectionCache, _rootsCache;
    private IReadOnlyList<string>? _historyCache;
    public long SelectionMaterializations { get; private set; }
    public long SelectionIndexBuilds { get; private set; }
    public long SnapshotCaptures { get; private set; }
    public long DocumentRevision { get; private set; }
    private void InvalidateSelection() { _selectionCache = null; _rootsCache = null; }
    private void InvalidateSceneSelection() { _selectionIndex = null; InvalidateSelection(); }

    private Dictionary<string, IndexedNode> SelectionIndex()
    {
        if (_selectionIndex is not null) return _selectionIndex;
        var index = new Dictionary<string, IndexedNode>(StringComparer.Ordinal);
        var order = 0;
        foreach (var node in Page.AllNodes()) index.Add(node.Id, new(node, order++));
        SelectionIndexBuilds++;
        return _selectionIndex = index;
    }

    public IReadOnlyList<DesignNode> Selection
    {
        get
        {
            if (_selectionCache is not null) return _selectionCache;
            SelectionMaterializations++;
            if (_selected.Count == 0) return _selectionCache = Array.Empty<DesignNode>();
            var index = SelectionIndex();
            var selected = new List<IndexedNode>(_selected.Count);
            foreach (var id in _selected) if (index.TryGetValue(id, out var entry)) selected.Add(entry);
            selected.Sort(static (a, b) => a.Order.CompareTo(b.Order));
            return _selectionCache = Array.AsReadOnly(selected.Select(x => x.Node).ToArray());
        }
    }
    public IReadOnlyList<DesignNode> SelectionRoots => _rootsCache ??= Array.AsReadOnly(Selection.Where(n => !Ancestors(n).Any(a => _selected.Contains(a.Id))).ToArray());
    public DesignNode? Primary => Selection.Count == 0 ? null : Selection[^1];

    public void Select(IEnumerable<string> ids, bool toggle = false)
    {
        ArgumentNullException.ThrowIfNull(ids);
        // Inputs may be a lazy projection of SelectedIds. Enumerate before modifying that set.
        var requested = ids.ToArray();
        if (requested.Length == 0)
        {
            if (toggle || _selected.Count == 0) return;
            _selected.Clear(); Notify(EditorChangeKind.Selection); return;
        }
        // Public document collections can change directly inside an edit transaction.
        // A selection request must see those additions/replacements, even before commit.
        if (IsInteracting) InvalidateSceneSelection();
        var index = SelectionIndex();
        var next = toggle ? new HashSet<string>(_selected, StringComparer.Ordinal) : new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in requested)
            if (index.ContainsKey(id) && (!toggle || !next.Remove(id))) next.Add(id);
        if (_selected.SetEquals(next)) return;
        _selected.Clear(); _selected.UnionWith(next); Notify(EditorChangeKind.Selection);
    }
    public void Select(DesignNode? node, bool toggle = false) => Select(node is null ? [] : [node.Id], toggle);
}
