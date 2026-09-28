using System.Collections.Specialized;
using ArtSpace.Core;
using ArtSpace.Editing;

internal static class SelectionPerformanceTests
{
    public static void Register(Action<string, Action> test)
    {
        test("selection no-op preserves materialization and publishes no redundant event", () =>
        {
            var a = new DesignNode(); var editor = Editor(a); editor.Select(a);
            var selection = editor.Selection; var notifications = 0;
            editor.Changed += (_, _) => notifications++;
            editor.Select(a); editor.Select([a.Id, a.Id]);
            Check(notifications == 0 && ReferenceEquals(selection, editor.Selection));
        });
        test("selection materializes lazy SelectedIds before replacing its source", () =>
        {
            var a = new DesignNode(); var b = new DesignNode(); var editor = Editor(a, b);
            editor.Select([a.Id, b.Id]); editor.Select(editor.SelectedIds.Where(id => id == b.Id));
            Check(editor.Selection.Count == 1 && ReferenceEquals(editor.Primary, b));
        });
        test("indexed selection retains scene traversal order and nested selection roots", () =>
        {
            var group = new DesignNode { Kind = NodeKind.Group }; var child = group.Add(new()); var b = new DesignNode();
            var editor = Editor(group, b); editor.Select([b.Id, child.Id, group.Id]);
            Check(editor.Selection.SequenceEqual(new[] { group, child, b }));
            Check(editor.SelectionRoots.SequenceEqual(new[] { group, b }) && ReferenceEquals(editor.Primary, b));
        });
        test("indexed selection filters foreign identifiers and preserves toggle parity", () =>
        {
            var a = new DesignNode(); var b = new DesignNode(); var editor = Editor(a, b);
            editor.Select([a.Id, "missing"]); editor.Select([a.Id, b.Id, b.Id], true);
            Check(editor.Selection.Count == 0); editor.Select([b.Id]); editor.Select([], true);
            Check(ReferenceEquals(editor.Primary, b));
        });
        test("empty selection does not build a full page index", () =>
        {
            var editor = Editor(Enumerable.Range(0, 1000).Select(_ => new DesignNode()).ToArray());
            editor.Select([]); Check(editor.Selection.Count == 0 && editor.SelectionIndexBuilds == 0);
        });
        test("repeated selection reuses the page index and never captures history", () =>
        {
            var nodes = Enumerable.Range(0, 2000).Select(i => new DesignNode { X = i }).ToArray(); var editor = Editor(nodes);
            editor.Select(nodes[0]); _ = editor.Primary; var builds = editor.SelectionIndexBuilds; var captures = editor.SnapshotCaptures;
            for (var i = 0; i < 1000; i++) { editor.Select(nodes[i % nodes.Length]); Check(ReferenceEquals(editor.Primary, nodes[i % nodes.Length])); }
            Check(editor.SelectionIndexBuilds == builds && editor.SnapshotCaptures == captures && !editor.CanUndo);
        });
        test("selection index sees in-transaction additions and removal", () =>
        {
            var a = new DesignNode(); var b = new DesignNode(); var editor = Editor(a); editor.Select(a); _ = editor.Selection;
            editor.BeginInteraction("Insert"); editor.AddNode(b); editor.Select(b); Check(ReferenceEquals(editor.Primary, b));
            editor.RemoveNode(b); Check(editor.Selection.Count == 0); editor.CommitInteraction();
        });
        test("selection requests observe direct collection replacement inside transactions", () =>
        {
            var a = new DesignNode(); var editor = Editor(a); editor.Select(a); _ = editor.Selection;
            editor.BeginInteraction("Replace"); _ = editor.Selection;
            var replacement = new DesignNode { Id = a.Id, Name = "replacement" }; editor.Page.Nodes[0] = replacement;
            editor.Select(a.Id.Yield()); Check(ReferenceEquals(editor.Primary, replacement)); editor.CommitInteraction();
        });
        test("selection index invalidates after undo and redo replace model instances", () =>
        {
            var a = new DesignNode(); var editor = Editor(a); editor.Select(a); editor.MoveSelection(4, 0);
            editor.Undo(); Check(editor.Primary!.X == 0 && !ReferenceEquals(editor.Primary, a));
            var restored = editor.Primary; editor.Redo(); Check(editor.Primary!.X == 4 && !ReferenceEquals(editor.Primary, restored));
        });
        test("indexed primary order follows reorder commits", () =>
        {
            var a = new DesignNode(); var b = new DesignNode(); var editor = Editor(a, b); editor.Select(a); editor.Reorder(1, true);
            editor.Select([a.Id, b.Id]); Check(ReferenceEquals(editor.Primary, a));
        });
        test("selection index resets on page switch and document replacement", () =>
        {
            var a = new DesignNode(); var editor = Editor(a); editor.Select(a); _ = editor.Primary;
            var first = editor.Page.Id; editor.AddPage(); editor.Select(a); Check(editor.Selection.Count == 0);
            editor.SetPage(first); editor.Select(a); Check(ReferenceEquals(editor.Primary, a));
            editor.Load(new() { Pages = [new() { Nodes = [new DesignNode { Id = a.Id, Name = "loaded" }] }] });
            editor.Select(a); Check(editor.Primary!.Name == "loaded" && !ReferenceEquals(editor.Primary, a));
        });
        test("history projection is retained until document change", () =>
        {
            var a = new DesignNode(); var editor = Editor(a); var empty = editor.History;
            editor.Select(a); Check(ReferenceEquals(empty, editor.History));
            editor.MoveSelection(2, 0); var history = editor.History; Check(history.Count == 1 && ReferenceEquals(history, editor.History));
            editor.Undo(); Check(editor.History.Count == 0); editor.Redo(); Check(editor.History.Count == 1);
        });
        test("reconciled rows publish no notifications for equal projection", () =>
        {
            var items = new[] { new object(), new object() }; var rows = new ReconciledCollection<object>(); rows.Apply(items);
            var notifications = 0; rows.CollectionChanged += (_, _) => notifications++; rows.Apply(items.ToArray()); Check(notifications == 0);
        });
        test("reconciled rows replace only changed metadata", () =>
        {
            var items = new[] { new object(), new object(), new object() }; var rows = new ReconciledCollection<object>(); rows.Apply(items);
            var notifications = new List<NotifyCollectionChangedEventArgs>(); rows.CollectionChanged += (_, e) => notifications.Add(e);
            var middle = new object(); rows.Apply([items[0], middle, items[2]]);
            Check(notifications.Count == 1 && notifications[0].Action == NotifyCollectionChangedAction.Replace && notifications[0].NewStartingIndex == 1);
            Check(ReferenceEquals(rows[0], items[0]) && ReferenceEquals(rows[2], items[2]));
        });
        test("reconciled large imports publish one reset", () =>
        {
            var rows = new ReconciledCollection<object>(); var notifications = 0; rows.CollectionChanged += (_, _) => notifications++;
            rows.Apply(Enumerable.Range(0, 10000).Select(_ => new object()).ToArray());
            Check(rows.Count == 10000 && notifications == 1 && rows.ResetCount == 1);
        });
        test("reconciled projections agree with randomized reference lists", () =>
        {
            var random = new Random(723); var pool = Enumerable.Range(0, 300).Select(_ => new object()).ToArray();
            var rows = new ReconciledCollection<object>();
            for (var iteration = 0; iteration < 200; iteration++)
            {
                var desired = Enumerable.Range(0, random.Next(301)).Select(_ => pool[random.Next(pool.Length)]).ToArray();
                rows.Apply(desired); Check(rows.SequenceEqual(desired));
            }
        });
    }
    private static EditorSession Editor(params DesignNode[] nodes) => new(new() { Pages = [new() { Nodes = nodes.ToList() }] });
    private static IEnumerable<T> Yield<T>(this T item) { yield return item; }
    private static void Check(bool condition) { if (!condition) throw new InvalidOperationException("Selection/collection responsiveness invariant failed."); }
}
