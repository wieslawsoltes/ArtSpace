using ArtSpace.Documents;
using ArtSpace.Skia;
using SkiaSharp;
using Address = ArtSpace.Core.EditablePath.Address;

namespace ArtSpace.Editor;

public sealed partial class DesignSurface
{
    /// <summary>Optional host snapshot for modifiers omitted from some pointer backends.</summary>
    public VirtualKeyModifiers? HostModifiers { get; set; }
    private bool AltPressed(PointerRoutedEventArgs e) => HostModifiers?.HasFlag(VirtualKeyModifiers.Menu)
        ?? (e.KeyModifiers.HasFlag(VirtualKeyModifiers.Menu) || Keyboard.Alt);

    private DesignNode? _pathNode, _pathDragBasis;
    private EditablePath? _editablePath, _pathDragOriginal;
    private string? _pathSignature;
    private readonly HashSet<Address> _selectedAnchors = [];
    private Address _pathDragAnchor;
    private int _pathDragHandle;
    private bool _pathDragMoved;
    private Matrix2D _pathDragWorld;
    private Vec2 _pathDragStart;
    private HashSet<Address> _marqueeOriginalAnchors = [];

    public readonly record struct AnchorInfo(int Contour, int Index, Vec2 Position, Vec2? ControlIn, Vec2? ControlOut, bool Selected);
    public int SelectedAnchorCount => IsPathTool ? _selectedAnchors.Count : 0;
    private bool IsPathTool => Session?.Tool is EditorTool.DirectSelect or EditorTool.AddAnchor or EditorTool.DeleteAnchor or EditorTool.AnchorPoint;

    /// <summary>Read-only positions in surface screen coordinates for hosts, automation and accessibility adapters.</summary>
    public IReadOnlyList<AnchorInfo> GetPathAnchors()
    {
        if (!IsPathTool || Session?.Primary is not { } node || !EnsureEditablePath(node)) return [];
        var matrix = _pathDragOriginal is not null && _gesture == Gesture.Vertex ? _pathDragWorld : node.WorldMatrix;
        Vec2 Screen(Vec2 p) => Session.Viewport.WorldToScreen(matrix.Map(p));
        return _editablePath!.Addresses.Select(address =>
        {
            var p = _editablePath[address];
            return new AnchorInfo(address.ContourIndex, address.PointIndex, Screen(p.Position),
                p.ControlIn.HasValue ? Screen(p.ControlIn.Value) : null,
                p.ControlOut.HasValue ? Screen(p.ControlOut.Value) : null, _selectedAnchors.Contains(address));
        }).ToArray();
    }

    public void EnterPathEditing()
    {
        if (Session?.Primary is not { } node || !PathEditing.CanEdit(node))
            throw new InvalidOperationException("Select a vector object to edit its anchors.");
        FinishTextEdit(true); FinishPath(false);
        Session.Tool = EditorTool.DirectSelect;
        EnsureEditablePath(node); _vectorNode = null; FocusCanvas(); Invalidate();
    }

    private bool EnsureEditablePath(DesignNode node)
    {
        if (!PathEditing.CanEdit(node)) return false;
        if (_gesture == Gesture.Vertex && _pathDragOriginal is not null && ReferenceEquals(_pathNode, node)) return true;
        var signature = VectorPath.Build(node) + $"|{node.Width:R}|{node.Height:R}|{node.PathWidth:R}|{node.PathHeight:R}|{node.FillRule}";
        if (ReferenceEquals(_pathNode, node) && _pathSignature == signature && _editablePath is not null) return true;
        var geometry = PathEditing.Read(node, Renderer);
        if (_pathNode?.Id != node.Id) _selectedAnchors.Clear();
        _pathNode = node; _editablePath = geometry; _pathSignature = signature;
        var valid = geometry.Addresses.ToHashSet(); _selectedAnchors.IntersectWith(valid);
        return true;
    }

    private void ResetPathGesture()
    {
        _pathDragOriginal = null; _pathDragBasis = null; _pathSignature = null;
        _marqueeOriginalAnchors.Clear();
        if (Session?.Primary?.Id != _pathNode?.Id) _selectedAnchors.Clear();
    }

    private bool PathPressed(Vec2 world, Vec2 screen, PointerRoutedEventArgs e)
    {
        if (Session is not { } editor || !IsPathTool) return false;
        _vectorNode = null;
        try
        {
            var shift = e.KeyModifiers.HasFlag(VirtualKeyModifiers.Shift);
            if (editor.Primary is { } primary && EnsureEditablePath(primary) && HitAnchor(primary, screen) is { } anchor)
            {
                if (editor.Tool == EditorTool.DeleteAnchor)
                {
                    _selectedAnchors.Clear(); _selectedAnchors.Add(anchor.Address); RemoveSelectedAnchors(false); return true;
                }
                if (editor.Tool == EditorTool.AnchorPoint)
                {
                    _selectedAnchors.Clear(); _selectedAnchors.Add(anchor.Address);
                    var point = _editablePath![anchor.Address];
                    if (point.ControlIn.HasValue || point.ControlOut.HasValue)
                    {
                        EditSelectedAnchors("Convert to corner", (path, addresses) => path.Smooth(addresses, false));
                    }
                    else BeginPathDrag(primary, world, anchor.Address, 3);
                    return true;
                }
                if (shift && anchor.Handle == 0)
                {
                    if (!_selectedAnchors.Add(anchor.Address)) { _selectedAnchors.Remove(anchor.Address); PublishAnchorSelection(); return true; }
                }
                else if (!_selectedAnchors.Contains(anchor.Address)) { _selectedAnchors.Clear(); _selectedAnchors.Add(anchor.Address); }
                BeginPathDrag(primary, world, anchor.Address, anchor.Handle); PublishAnchorSelection(); return true;
            }

            var hit = Renderer.HitTest(editor.Page.Nodes, world, true, 6 / editor.Viewport.Zoom);
            // A selected contour can be edited where its fill is absent or its handles extend beyond its bounds.
            if (editor.Primary is { } selected && EnsureEditablePath(selected) && TrySegment(selected, world) is { } near)
            {
                if (editor.Tool == EditorTool.AddAnchor || (editor.Tool == EditorTool.DirectSelect && AltPressed(e)))
                {
                    InsertAnchor(selected, near); return true;
                }
                if (editor.Tool == EditorTool.DirectSelect)
                {
                    if (!shift) _selectedAnchors.Clear();
                    var contour = _editablePath!.Contours[near.Start.ContourIndex];
                    _selectedAnchors.Add(near.Start);
                    _selectedAnchors.Add(new(near.Start.ContourIndex, (near.Start.PointIndex + 1) % contour.Points.Count));
                    BeginPathDrag(selected, world, near.Start, 0); PublishAnchorSelection(); return true;
                }
            }
            if (hit is not null && PathEditing.CanEdit(hit))
            {
                if (editor.Primary?.Id != hit.Id) editor.Select(hit);
                if (!EnsureEditablePath(hit)) return true;
                if (editor.Tool == EditorTool.AddAnchor && TrySegment(hit, world) is { } segment) { InsertAnchor(hit, segment); return true; }
                if (editor.Tool != EditorTool.DirectSelect) { Invalidate(); return true; }
                _selectedAnchors.Clear(); _selectedAnchors.UnionWith(_editablePath!.Addresses);
                if (_editablePath.AnchorCount > 0) BeginPathDrag(hit, world, _editablePath.Addresses.First(), 0);
                PublishAnchorSelection(); return true;
            }
            if (editor.Tool == EditorTool.DirectSelect && editor.Primary is { } active && EnsureEditablePath(active))
            {
                _marqueeOriginalAnchors = shift ? _selectedAnchors.ToHashSet() : [];
                if (!shift) _selectedAnchors.Clear();
                _gesture = Gesture.AnchorMarquee; _marquee = new(world.X, world.Y, 0, 0);
                PublishAnchorSelection(); return true;
            }
            if (!shift) editor.Select((DesignNode?)null);
        }
        catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or ArgumentException)
        {
            CancelGesture(); StatusChanged?.Invoke(ex.Message);
        }
        return true;
    }

    private (Address Address, int Handle)? HitAnchor(DesignNode node, Vec2 screen)
    {
        if (Session is null || _editablePath is null) return null;
        Vec2 Screen(Vec2 p) => Session.Viewport.WorldToScreen(node.WorldMatrix.Map(p));
        foreach (var address in _editablePath.Addresses)
            if (Screen(_editablePath[address].Position).DistanceTo(screen) <= 7) return (address, 0);
        foreach (var address in _editablePath.Addresses)
        {
            if (_selectedAnchors.Count > 0 && !_selectedAnchors.Contains(address)) continue;
            var point = _editablePath[address];
            if (point.ControlIn.HasValue && Screen(point.ControlIn.Value).DistanceTo(screen) <= 7) return (address, 1);
            if (point.ControlOut.HasValue && Screen(point.ControlOut.Value).DistanceTo(screen) <= 7) return (address, 2);
        }
        return null;
    }

    private EditablePath.SegmentHit? TrySegment(DesignNode node, Vec2 world) =>
        _editablePath?.HitSegment(node.WorldMatrix.Inverse.Map(world), 6 / Session!.Viewport.Zoom);

    private void InsertAnchor(DesignNode node, EditablePath.SegmentHit hit)
    {
        if (hit.Parameter <= .001 || hit.Parameter >= .999) return;
        var geometry = _editablePath!.Clone();
        var inserted = geometry.Split(hit.Start, hit.Parameter);
        Session!.Edit("Insert anchor point", () => PathEditing.Write(node, geometry));
        _selectedAnchors.Clear(); _selectedAnchors.Add(inserted); _pathSignature = null;
        PublishAnchorSelection();
    }

    private void BeginPathDrag(DesignNode node, Vec2 world, Address address, int handle)
    {
        Session!.BeginInteraction(handle == 0 ? "Move path anchors" : "Move direction handle");
        _pathDragBasis = DocumentJson.CloneNode(node); _pathDragWorld = node.WorldMatrix;
        _pathDragOriginal = _editablePath!.Clone(); _pathDragStart = _pathDragWorld.Inverse.Map(world);
        _pathDragMoved = false;
        _pathDragAnchor = address; _pathDragHandle = handle; _gesture = Gesture.Vertex;
    }

    private void MovePathAnchor(Vec2 world, bool independent)
    {
        independent = HostModifiers?.HasFlag(VirtualKeyModifiers.Menu) ?? (independent || Keyboard.Alt);
        if (Session is not { } editor || _pathNode is not { } node || _pathDragOriginal is not { } original || _pathDragBasis is null) return;
        if (!_pathDragMoved && screenDelta(world).DistanceTo(Vec2.Zero) < .25) return;
        _pathDragMoved = true;
        var delta = _pathDragWorld.Inverse.Map(world) - _pathDragStart;
        if (Keyboard.Shift && _pathDragHandle == 0) delta = Math.Abs(delta.X) > Math.Abs(delta.Y) ? new(delta.X, 0) : new(0, delta.Y);
        if (_pathDragHandle == 0)
        {
            foreach (var address in _selectedAnchors)
            {
                var a = original[address]; var b = _editablePath![address];
                b.Position = a.Position + delta; b.ControlIn = a.ControlIn + delta; b.ControlOut = a.ControlOut + delta;
            }
        }
        else
        {
            var a = original[_pathDragAnchor]; var b = _editablePath![_pathDragAnchor];
            b.ControlIn = a.ControlIn; b.ControlOut = a.ControlOut;
            if (_pathDragHandle == 3)
            {
                b.ControlOut = a.Position + delta; b.ControlIn = a.Position - delta;
            }
            else
            {
                var initial = _pathDragHandle == 1 ? a.ControlIn ?? a.Position : a.ControlOut ?? a.Position;
                _editablePath.MoveControl(_pathDragAnchor, _pathDragHandle == 1, initial + delta, independent);
            }
        }
        try { PathEditing.Write(node, _editablePath!, _pathDragBasis); editor.Preview(); }
        catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or ArgumentException)
        { CancelGesture(); StatusChanged?.Invoke(ex.Message); }
        Vec2 screenDelta(Vec2 p) => editor.Viewport.WorldToScreen(p) - _startScreen;
    }

    private void UpdateAnchorMarquee(Vec2 world)
    {
        if (Session is null || _pathNode is null || _editablePath is null) return;
        _marquee = RectD.FromPoints(_startWorld, world);
        _selectedAnchors.Clear(); _selectedAnchors.UnionWith(_marqueeOriginalAnchors);
        foreach (var address in _editablePath.Addresses)
            if (_marquee.Value.Contains(_pathNode.WorldMatrix.Map(_editablePath[address].Position))) _selectedAnchors.Add(address);
        PublishAnchorSelection();
    }

    public bool EditSelectedAnchors(string label, Action<EditablePath, Address[]> edit)
    {
        if (!IsPathTool || Session?.Primary is not { } node || !EnsureEditablePath(node) || _selectedAnchors.Count == 0) return false;
        var geometry = _editablePath!.Clone(); edit(geometry, _selectedAnchors.ToArray());
        Session.Edit(label, () =>
        {
            if (geometry.AnchorCount == 0) { Session.RemoveNode(node); Session.Select((DesignNode?)null); }
            else PathEditing.Write(node, geometry);
        });
        _pathSignature = null; return true;
    }

    public bool RemoveSelectedAnchors(bool cut)
    {
        var edited = EditSelectedAnchors(cut ? "Cut selected anchors" : "Remove selected anchors", (path, addresses) =>
        {
            if (cut) path.Cut(addresses); else path.Remove(addresses);
        });
        if (edited) { _selectedAnchors.Clear(); PublishAnchorSelection(); }
        return edited;
    }

    public bool HandlePathKey(VirtualKey key, bool control, bool shift, bool alt)
    {
        if (!IsPathTool || Session?.Primary is not { } node || !EnsureEditablePath(node)) return false;
        if (control && key == VirtualKey.A)
        {
            _selectedAnchors.Clear(); _selectedAnchors.UnionWith(_editablePath!.Addresses); PublishAnchorSelection(); return true;
        }
        if (!control && key == VirtualKey.Escape && _selectedAnchors.Count > 0)
        {
            if (Session.IsInteracting) CancelGesture();
            _selectedAnchors.Clear(); PublishAnchorSelection(); return true;
        }
        if (_selectedAnchors.Count == 0 || control) return false;
        if (key is VirtualKey.Delete or VirtualKey.Back) return RemoveSelectedAnchors(!alt);
        var step = shift ? 10d : 1d;
        var worldDelta = key switch
        {
            VirtualKey.Left => new Vec2(-step, 0), VirtualKey.Right => new Vec2(step, 0),
            VirtualKey.Up => new Vec2(0, -step), VirtualKey.Down => new Vec2(0, step), _ => Vec2.Zero
        };
        if (worldDelta == Vec2.Zero) return false;
        var inverse = node.WorldMatrix.Inverse;
        var delta = inverse.Map(worldDelta) - inverse.Map(Vec2.Zero);
        return EditSelectedAnchors("Nudge path anchors", (path, addresses) => path.Translate(addresses, delta));
    }

    private void PublishAnchorSelection() { Session?.Notify(EditorChangeKind.Selection, "Anchor selection"); Invalidate(); }

    private void DrawEditableHandles(SKCanvas canvas)
    {
        if (!IsPathTool || Session?.Primary is not { } node) return;
        IReadOnlyList<AnchorInfo> anchors;
        try { anchors = GetPathAnchors(); }
        catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or ArgumentException) { return; }
        using var line = new SKPaint { IsAntialias = true, Color = new SKColor(92, 152, 255), StrokeWidth = 1, Style = SKPaintStyle.Stroke };
        using var fill = new SKPaint { IsAntialias = true, Color = SKColors.White };
        using var selected = new SKPaint { IsAntialias = true, Color = line.Color };
        canvas.Save();
        canvas.Translate((float)Session!.Viewport.Pan.X, (float)Session.Viewport.Pan.Y);
        canvas.Scale((float)Session.Viewport.Zoom); canvas.Concat(SceneRenderer.Matrix(node.WorldMatrix));
        line.StrokeWidth = (float)(1 / Session.Viewport.Zoom); canvas.DrawPath(Renderer.Geometry(node), line); canvas.Restore(); line.StrokeWidth = 1;
        foreach (var anchor in anchors)
        {
            if (anchor.Selected || SelectedAnchorCount == 0)
            {
                Handle(anchor.ControlIn); Handle(anchor.ControlOut);
                void Handle(Vec2? handle)
                {
                    if (handle is not { } p) return;
                    canvas.DrawLine(P(anchor.Position), P(p), line); canvas.DrawCircle(P(p), 3, fill); canvas.DrawCircle(P(p), 3, line);
                }
            }
            var box = new SKRect((float)anchor.Position.X - 3, (float)anchor.Position.Y - 3, (float)anchor.Position.X + 3, (float)anchor.Position.Y + 3);
            canvas.DrawRect(box, anchor.Selected ? selected : fill); canvas.DrawRect(box, line);
        }
    }
}
