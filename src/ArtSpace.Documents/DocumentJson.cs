using System.Text.Json;
using System.Text.Json.Serialization;
using ArtSpace.Core;

namespace ArtSpace.Documents;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = false, UseStringEnumConverter = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(DesignDocument))]
[JsonSerializable(typeof(DesignNode))]
[JsonSerializable(typeof(List<DesignNode>))]
public partial class ArtSpaceJsonContext : JsonSerializerContext;

public static partial class DocumentJson
{
    public const int CurrentFormatVersion = 5;
    public const int MaxDocumentCharacters = 32 * 1024 * 1024;
    public const int MaxNodes = 100_000;
    /// <summary>Save using schema 5. Older documents upgrade so earlier readers cannot silently discard live appearance semantics.</summary>
    public static string Save(DesignDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.FormatVersion is < 1 or > CurrentFormatVersion) throw new InvalidDataException($"Unsupported ArtSpace format version {document.FormatVersion}.");
        document.FormatVersion = CurrentFormatVersion;
        return JsonSerializer.Serialize(document, ArtSpaceJsonContext.Default.DesignDocument);
    }
    public static DesignDocument Load(string json)
    {
        if (json.Length > MaxDocumentCharacters) throw new InvalidDataException("The document exceeds the 32 MiB text limit.");
        var document = JsonSerializer.Deserialize(json, ArtSpaceJsonContext.Default.DesignDocument) ?? throw new InvalidDataException("The file does not contain an ArtSpace document.");
        Validate(document); document.RebuildParents(); return document;
    }
    public static DesignNode CloneNode(DesignNode node, bool newIds = false)
    {
        var clone = JsonSerializer.Deserialize(JsonSerializer.Serialize(node, ArtSpaceJsonContext.Default.DesignNode), ArtSpaceJsonContext.Default.DesignNode)!;
        Attach(clone, null);
        if (newIds) RegenerateIds([clone]);
        return clone;
        static void Attach(DesignNode n, DesignNode? parent) { n.Parent = parent; foreach (var c in n.Children) Attach(c, n); }
    }
    public static string SaveNodes(IEnumerable<DesignNode> nodes) => JsonSerializer.Serialize(nodes.ToList(), ArtSpaceJsonContext.Default.ListDesignNode);
    public static List<DesignNode> LoadNodes(string json)
    {
        if (json.Length > MaxDocumentCharacters) throw new InvalidDataException("Clipboard content is too large.");
        var nodes = JsonSerializer.Deserialize(json, ArtSpaceJsonContext.Default.ListDesignNode) ?? [];
        var doc = new DesignDocument { Pages = [new() { Nodes = nodes }] }; Validate(doc); doc.RebuildParents(); RegenerateIds(nodes); return nodes;
    }
    public static void RegenerateIds(IEnumerable<DesignNode> roots)
    {
        var nodes = roots.SelectMany(n => n.DescendantsAndSelf()).ToArray();
        var ids = nodes.ToDictionary(n => n.Id, _ => Guid.NewGuid().ToString("N"));
        foreach (var node in nodes)
        {
            node.Id = ids[node.Id];
            if (node.OpacityMaskId is { } opacity && ids.TryGetValue(opacity, out var opacityReplacement)) node.OpacityMaskId = opacityReplacement;
            if (node.ClipPathId is { } clip && ids.TryGetValue(clip, out var clipReplacement)) node.ClipPathId = clipReplacement;
            if (node.PrototypeTargetId is { } target && ids.TryGetValue(target, out var replacement)) node.PrototypeTargetId = replacement;
            if (node.ComponentId is { } component && ids.TryGetValue(component, out replacement)) node.ComponentId = replacement;
        }
    }
    public static void Validate(DesignDocument document)
    {
        if (document.FormatVersion is < 1 or > CurrentFormatVersion) throw new InvalidDataException($"Unsupported ArtSpace format version {document.FormatVersion}.");
        if (document.Pages is null || document.Pages.Count is < 1 or > 1000) throw new InvalidDataException("A document must have between 1 and 1000 pages.");
        ValidateGraphicStyles(document);
        var ids = new HashSet<string>(StringComparer.Ordinal); var count = 0;
        foreach (var page in document.Pages)
        {
            if (string.IsNullOrWhiteSpace(page.Id) || !ids.Add(page.Id) || page.Nodes is null) throw new InvalidDataException("Invalid or duplicate page identifier.");
            foreach (var node in page.Nodes) Check(node, 0);
        }
        void Check(DesignNode n, int depth)
        {
            if (++count > MaxNodes || depth > 60) throw new InvalidDataException("Document node count or nesting limit exceeded.");
            if (n is null || string.IsNullOrWhiteSpace(n.Id) || !ids.Add(n.Id)) throw new InvalidDataException("Invalid or duplicate layer identifier.");
            if (!double.IsFinite(n.X) || !double.IsFinite(n.Y) || !double.IsFinite(n.Width) || !double.IsFinite(n.Height) || !double.IsFinite(n.Rotation) || n.Width < 0 || n.Height < 0 || n.Width > 1e7 || n.Height > 1e7 || Math.Abs(n.X) > 1e9 || Math.Abs(n.Y) > 1e9) throw new InvalidDataException("A layer has invalid geometry.");
            if (n.Children is null || n.Fills is null || n.Strokes is null || n.Shadows is null || n.Layout is null || n.Points is null || n.Overrides is null) throw new InvalidDataException("A layer is missing required data.");
            ValidateLiveAppearance(n);
            if (n.ClipPathId is { } clip)
            {
                var mask = n.Children.Find(child => child?.Id == clip);
                if (!n.IsContainer || mask is null || mask.IsContainer || mask.Children is null || mask.Children.Count != 0 || mask.Kind is NodeKind.Text or NodeKind.Slice)
                    throw new InvalidDataException("A clipping path must reference a direct vector child of its container.");
            }
            if (n.AffineTransform is { } affine && !AffineGeometry.IsInvertible(affine)) throw new InvalidDataException("Invalid affine transform.");
            if (!Enum.IsDefined(n.OpacityMaskMode)) throw new InvalidDataException("Invalid opacity mask mode.");
            if (n.OpacityMaskId is { } opacityId)
            {
                var source = n.Children.Find(c => c?.Id == opacityId);
                if (!n.IsContainer || source is null || source.Kind == NodeKind.Slice || opacityId == n.ClipPathId)
                    throw new InvalidDataException("An opacity mask must reference a distinct renderable direct child.");
            }
            if (n.OpacityMaskRegion is { } region && (!double.IsFinite(region.X) || !double.IsFinite(region.Y) || !double.IsFinite(region.Right) || !double.IsFinite(region.Bottom) || region.Width <= 0 || region.Height <= 0))
                throw new InvalidDataException("Invalid opacity mask region.");
            foreach (var fill in n.Fills)
            {
                if (fill is not null) ValidatePaint(fill);
                if (fill is null || fill.Stops is null || fill.Stops.Count > 4096 || !double.IsFinite(fill.Opacity) || !fill.Start.IsFinite || !fill.End.IsFinite || !double.IsFinite(fill.GradientRadius) || fill.GradientRadius < 0 || !Enum.IsDefined(fill.GradientSpace) || !Enum.IsDefined(fill.GradientSpread) || !AffineGeometry.IsInvertible(fill.GradientTransform) || (fill.GradientFocus.HasValue && !fill.GradientFocus.Value.IsFinite) || fill.Stops.Any(s => s is null || !double.IsFinite(s.Offset) || !double.IsFinite(s.Opacity)))
                    throw new InvalidDataException("Invalid gradient appearance.");
            }
            if (!Enum.IsDefined(n.FillRule)) throw new InvalidDataException("Invalid path fill rule.");
            n.Opacity = Numbers.Clamp(n.Opacity, 0, 1); n.FontSize = Numbers.Clamp(n.FontSize, 1, 4096);
            n.CornerRadius = Numbers.Clamp(n.CornerRadius, 0, 1e6); n.Sides = Math.Clamp(n.Sides, 3, 128);
            n.StarRatio = Numbers.Clamp(n.StarRatio, .01, 1); n.LineHeight = Numbers.Clamp(n.LineHeight, .2, 10);
            if (n.Points.Any(p => !p.Position.IsFinite || (p.ControlIn.HasValue && !p.ControlIn.Value.IsFinite) || (p.ControlOut.HasValue && !p.ControlOut.Value.IsFinite))) throw new InvalidDataException("A path contains invalid points.");
            foreach (var stroke in n.Strokes)
            {
                if (stroke is null || !double.IsFinite(stroke.Opacity) || !double.IsFinite(stroke.Width) || stroke.Width < 0 || stroke.Width > 1e6 || !double.IsFinite(stroke.MiterLimit) || stroke.MiterLimit < 1 || stroke.MiterLimit > 1e6 || !Enum.IsDefined(stroke.Cap) || !Enum.IsDefined(stroke.Join) || stroke.Dashes is null || stroke.Dashes.Count > 4096 || stroke.Dashes.Any(d => !double.IsFinite(d) || d <= 0))
                    throw new InvalidDataException("Invalid stroke appearance.");
            }
            if (!double.IsFinite(n.PathWidth) || !double.IsFinite(n.PathHeight) || n.PathWidth < 0 || n.PathHeight < 0) throw new InvalidDataException("Invalid path dimensions.");
            foreach (var child in n.Children) Check(child, depth + 1);
        }
    }
}

public interface IWorkspaceStorage
{
    Task<string?> ReadAutosaveAsync(CancellationToken cancellationToken = default);
    Task WriteAutosaveAsync(string document, CancellationToken cancellationToken = default);
    Task<(string Name, string Text)?> OpenAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(string name, byte[] bytes, string contentType, CancellationToken cancellationToken = default);
}
