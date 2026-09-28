using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using ArtSpace.Documents;
using ArtSpace.Editing;
using ArtSpace.Workbench;

namespace ArtSpace.App;

internal sealed class BrowserWorkspaceStorage : IWorkspaceStorage
{
    public async Task<string?> ReadAutosaveAsync(CancellationToken cancellationToken = default) { cancellationToken.ThrowIfCancellationRequested(); return await BrowserFiles.Load(); }
    public async Task WriteAutosaveAsync(string document, CancellationToken cancellationToken = default) { cancellationToken.ThrowIfCancellationRequested(); await BrowserFiles.Save(document); }
    public async Task<(string Name, string Text)?> OpenAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested(); var result = await BrowserFiles.Open();
        if (string.IsNullOrEmpty(result)) return null;
        using var data = JsonDocument.Parse(result); return (data.RootElement.GetProperty("name").GetString()!, data.RootElement.GetProperty("text").GetString()!);
    }
    public async Task SaveAsync(string name, byte[] bytes, string contentType, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested(); await BrowserFiles.Download(name, Convert.ToBase64String(bytes), contentType);
    }
}
internal static partial class BrowserFiles
{
    [JSImport("globalThis.artSpaceStorage.load")]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    internal static partial Task<string> Load();
    [JSImport("globalThis.artSpaceStorage.save")]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    internal static partial Task<string> Save(string document);
    [JSImport("globalThis.artSpaceStorage.open")]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    internal static partial Task<string> Open();
    [JSImport("globalThis.artSpaceStorage.download")]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    internal static partial Task<string> Download(string name, string base64, string contentType);
    [JSImport("globalThis.artSpaceStorage.isTestMode")]
    internal static partial bool IsTestMode();
    [JSImport("globalThis.artSpaceStorage.publishDiagnostics")]
    internal static partial void PublishDiagnostics(string json);
}
internal static class BrowserDiagnostics
{
    // Read-only diagnostics are opt-in and never expose a document mutation API.
    public static void Attach(EditorSession session, StudioWorkbench workbench)
    {
        if (!BrowserFiles.IsTestMode()) return;
        void Publish()
        {
            var primary = session.Primary;
            using var stream = new MemoryStream();
            using (var json = new Utf8JsonWriter(stream))
            {
                json.WriteStartObject(); json.WriteBoolean("ready", true); json.WriteString("tool", session.Tool.ToString());
                json.WriteNumber("nodes", session.Page.AllNodes().Count()); json.WriteNumber("roots", session.Page.Nodes.Count);
                json.WriteNumber("pages", session.Document.Pages.Count); json.WriteNumber("selection", session.Selection.Count);
                json.WriteNumber("history", session.History.Count); json.WriteNumber("zoom", session.Viewport.Zoom);
                json.WriteNumber("panX", session.Viewport.Pan.X); json.WriteNumber("panY", session.Viewport.Pan.Y);
                json.WriteNumber("canvasWidth", workbench.Surface.ActualWidth); json.WriteNumber("canvasHeight", workbench.Surface.ActualHeight);
                json.WriteString("page", session.Page.Name); json.WriteString("name", primary?.Name);
                json.WriteString("kind", primary?.Kind.ToString()); json.WriteBoolean("visible", primary?.Visible ?? false); json.WriteBoolean("locked", primary?.Locked ?? false); json.WriteNumber("x", primary?.X ?? 0); json.WriteNumber("y", primary?.Y ?? 0);
                json.WriteNumber("width", primary?.Width ?? 0); json.WriteNumber("height", primary?.Height ?? 0);
                json.WriteBoolean("canUndo", session.CanUndo); json.WriteBoolean("canRedo", session.CanRedo);
                var origin = workbench.Surface.TransformToVisual(null).TransformPoint(new Windows.Foundation.Point(0, 0));
                json.WriteNumber("canvasX", origin.X); json.WriteNumber("canvasY", origin.Y);
                json.WriteString("fillRule", primary?.FillRule.ToString());
                json.WriteString("clipPathId", primary?.ClipPathId);
                json.WriteString("opacityMaskId", primary?.OpacityMaskId);
                json.WriteString("opacityMaskMode", primary?.OpacityMaskMode.ToString());
                json.WriteBoolean("opacityMaskInverted", primary?.OpacityMaskInverted ?? false);
                json.WriteBoolean("opacityMaskEnabled", primary?.OpacityMaskEnabled ?? false);
                json.WriteNumber("opacityMasks", session.Page.AllNodes().Count(n => n.OpacityMaskId is not null));
                json.WriteNumber("gradientBuilds", workbench.Surface.Renderer.GradientBuilds);
                json.WriteNumber("clipGroups", session.Page.AllNodes().Count(n => n.ClipPathId is not null));
                json.WriteNumber("geometryBuilds", workbench.Surface.Renderer.GeometryBuilds);
                json.WriteNumber("culledNodes", workbench.Surface.Renderer.CulledNodes);
                json.WriteStartArray("anchors");
                foreach (var anchor in workbench.Surface.GetPathAnchors())
                {
                    json.WriteStartObject(); json.WriteNumber("contour", anchor.Contour); json.WriteNumber("index", anchor.Index);
                    json.WriteNumber("x", anchor.Position.X + origin.X); json.WriteNumber("y", anchor.Position.Y + origin.Y);
                    json.WriteBoolean("selected", anchor.Selected);
                    if (anchor.ControlIn.HasValue) { json.WriteNumber("inX", anchor.ControlIn.Value.X + origin.X); json.WriteNumber("inY", anchor.ControlIn.Value.Y + origin.Y); }
                    if (anchor.ControlOut.HasValue) { json.WriteNumber("outX", anchor.ControlOut.Value.X + origin.X); json.WriteNumber("outY", anchor.ControlOut.Value.Y + origin.Y); }
                    json.WriteEndObject();
                }
                json.WriteEndArray();
                json.WriteBoolean("presenting", workbench.Surface.IsPresenting); json.WriteEndObject();
            }
            BrowserFiles.PublishDiagnostics(System.Text.Encoding.UTF8.GetString(stream.ToArray()));
        }
        session.Changed += (_, _) => Publish(); workbench.Surface.SizeChanged += (_, _) => Publish(); workbench.Surface.PresentationChanged += _ => Publish(); Publish();
    }
}
