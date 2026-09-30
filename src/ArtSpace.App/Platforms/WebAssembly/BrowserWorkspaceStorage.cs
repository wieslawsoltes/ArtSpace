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
    [JSImport("globalThis.artSpaceStorage.publishMenuDiagnostics")]
    internal static partial void PublishMenuDiagnostics(string? openMenu, string? activeMenuCommand);
    [JSImport("globalThis.artSpaceStorage.publishFrameDiagnostics")]
    internal static partial void PublishFrameDiagnostics(double sceneRecordings, double sceneReplays,
        double sceneBytes, double paintBuilds, double dashBuilds, double effectFilterBuilds,
        double gradientBuilds, double geometryBuilds, double culledNodes);
}
internal static class BrowserDiagnostics
{
    // Read-only diagnostics are opt-in and never expose a document mutation API.
    public static void Attach(EditorSession session, StudioWorkbench workbench)
    {
        if (!BrowserFiles.IsTestMode()) return;
        ArtSpace.Core.DesignPage? countedPage = null;
        var countsDirty = true;
        var nodeCount = 0; var maskCount = 0; var clipCount = 0;
        long sceneScans = 0, publishes = 0;
        void RefreshCounts()
        {
            if (!countsDirty && ReferenceEquals(countedPage, session.Page)) return;
            countedPage = session.Page;
            nodeCount = maskCount = clipCount = 0;
            foreach (var node in countedPage.AllNodes())
            {
                nodeCount++;
                if (node.OpacityMaskId is not null) maskCount++;
                if (node.ClipPathId is not null) clipCount++;
            }
            countsDirty = false; sceneScans++;
        }
        void Publish()
        {
            if (workbench.IsDisposed) return;
            RefreshCounts(); publishes++;
            var primary = session.Primary;
            using var stream = new MemoryStream();
            using (var json = new Utf8JsonWriter(stream))
            {
                json.WriteStartObject(); json.WriteBoolean("ready", true); json.WriteString("tool", session.Tool.ToString());
                json.WriteNumber("nodes", nodeCount); json.WriteNumber("roots", session.Page.Nodes.Count);
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
                json.WriteNumber("opacityMasks", maskCount);
                json.WriteNumber("gradientBuilds", workbench.Surface.Renderer.GradientBuilds);
                json.WriteNumber("effects", primary?.Effects.Count ?? 0);
                json.WriteNumber("effectRadius", primary?.Effects.FirstOrDefault()?.Radius ?? 0);
                json.WriteNumber("graphicStyles", session.Document.GraphicStyles.Count);
                json.WriteNumber("pathTextLayoutBuilds", workbench.Surface.Renderer.PathTextLayoutBuilds);
                json.WriteNumber("textBaselineBuilds", workbench.Surface.Renderer.TextBaselineBuilds);
                if (primary?.TextPath is { } pathText)
                {
                    var status = workbench.Surface.Renderer.GetTypeOnPathStatus(primary);
                    json.WriteStartObject("pathText"); json.WriteNumber("start", pathText.Start); json.WriteNumber("end", pathText.End);
                    json.WriteBoolean("flip", pathText.Flip); json.WriteNumber("baselineShift", pathText.BaselineShift);
                    json.WriteNumber("length", status.PathLength); json.WriteBoolean("overflow", status.Overflow);
                    json.WriteNumber("visibleGlyphs", status.VisibleGlyphs); json.WriteString("error", status.Error); json.WriteEndObject();
                }
                json.WriteStartObject("svgTextPath");
                if (primary?.TextPath?.SvgPosition is { } svgPosition)
                {
                    json.WriteNumber("offset", svgPosition.Offset); json.WriteBoolean("percentage", svgPosition.Percentage);
                }
                json.WriteEndObject();
                json.WriteStartArray("typePathHandles");
                foreach (var handle in workbench.Surface.GetTypeOnPathHandles())
                {
                    json.WriteStartObject(); json.WriteNumber("kind", handle.Kind);
                    json.WriteNumber("x", handle.Position.X + origin.X); json.WriteNumber("y", handle.Position.Y + origin.Y);
                    json.WriteNumber("baseX", handle.BaselinePosition.X + origin.X); json.WriteNumber("baseY", handle.BaselinePosition.Y + origin.Y);
                    json.WriteEndObject();
                }
                json.WriteEndArray();
                json.WriteNumber("appearanceBuilds", workbench.AppearancePanelBuilds);
                json.WriteNumber("sceneRecordings", workbench.Surface.Renderer.SceneRecordings);
                json.WriteNumber("sceneReplays", workbench.Surface.Renderer.SceneReplays);
                json.WriteNumber("sceneBytes", workbench.Surface.Renderer.RetainedSceneBytes);
                json.WriteNumber("paintBuilds", workbench.Surface.Renderer.PaintBuilds);
                json.WriteNumber("dashBuilds", workbench.Surface.Renderer.DashBuilds);
                json.WriteNumber("effectFilterBuilds", workbench.Surface.Renderer.EffectFilterBuilds);
                json.WriteNumber("clipGroups", clipCount);
                json.WriteNumber("diagnosticSceneScans", sceneScans);
                json.WriteNumber("diagnosticPublishes", publishes);
                json.WriteNumber("geometryBuilds", workbench.Surface.Renderer.GeometryBuilds);
                json.WriteNumber("culledNodes", workbench.Surface.Renderer.CulledNodes);
                json.WriteString("id", primary?.Id);
                json.WriteString("inspectorTarget", workbench.InspectorTargetId);
                json.WriteString("inspectorName", workbench.InspectorTargetName);
                json.WriteNumber("inspectorSelection", workbench.InspectorSelectionCount);
                json.WriteString("activePanel", workbench.ActivePanel);
                json.WriteString("openMenu", workbench.OpenMenuName);
                json.WriteString("activeMenuCommand", workbench.ActiveMenuCommandName);
                json.WriteString("dialogTitle", workbench.ActiveDialogTitle);
                json.WriteString("focusedControl", workbench.FocusedControlName);
                json.WriteBoolean("uiPending", workbench.UiPending);
                json.WriteNumber("uiFlushes", workbench.UiFlushes);
                json.WriteNumber("uiFailures", workbench.UiRefreshFailures);
                json.WriteNumber("uiMs", workbench.LastUiRefreshMs);
                json.WriteNumber("inspectorBuilds", workbench.InspectorBuilds);
                json.WriteNumber("inspectorRefreshes", workbench.InspectorRefreshes);
                json.WriteNumber("layerPasses", workbench.LayerPasses);
                json.WriteNumber("layerEntryBuilds", workbench.LayerEntryBuilds);
                json.WriteNumber("layerSelectionChanges", workbench.LayerSelectionChanges);
                json.WriteNumber("layerResets", workbench.LayerCollectionResets);
                json.WriteNumber("artboardRefreshes", workbench.ArtboardRefreshes);
                json.WriteNumber("historyRefreshes", workbench.HistoryRefreshes);
                json.WriteNumber("selectionIndexBuilds", session.SelectionIndexBuilds);
                json.WriteNumber("snapshots", session.SnapshotCaptures);
                json.WriteNumber("snapIndexBuilds", workbench.Surface.SnapIndexBuilds);
                json.WriteNumber("editablePathBuilds", workbench.Surface.EditablePathBuilds);
                json.WriteStartArray("selectedLayerIds");
                foreach (var id in workbench.SelectedLayerIds) json.WriteStringValue(id);
                json.WriteEndArray();
                json.WriteStartArray("inspectorFields");
                foreach (var field in workbench.ActivePanel == "Properties" ? workbench.InspectorFields : workbench.AppearanceFields)
                {
                    json.WriteStartObject(); json.WriteString("section", field.Section); json.WriteString("label", field.Label); json.WriteString("value", field.Value);
                    json.WriteNumber("x", field.X); json.WriteNumber("y", field.Y); json.WriteNumber("width", field.Width); json.WriteNumber("height", field.Height); json.WriteEndObject();
                }
                json.WriteEndArray();
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
        var queued = false;
        void QueuePublish()
        {
            if (queued || workbench.IsDisposed) return;
            queued = true;
            if (!workbench.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => { queued = false; Publish(); })) queued = false;
        }
        session.Changed += (_, change) =>
        {
            // Previews can add/remove nodes inside a transaction before DocumentRevision changes.
            if (change.Kind is EditorChangeKind.Document or EditorChangeKind.Preview) countsDirty = true;
            QueuePublish();
        };
        workbench.UiRefreshed += QueuePublish;
        workbench.MenuNavigationChanged += () =>
        {
            if (workbench.IsDisposed) return;
            // Navigation changes selection paint/focus without requiring layout. Publish only the
            // two authoritative scalars instead of traversing the document/inspector for every key.
            BrowserFiles.PublishMenuDiagnostics(workbench.OpenMenuName, workbench.ActiveMenuCommandName);
        };
        workbench.Surface.FrameRendered += () =>
        {
            if (workbench.IsDisposed) return;
            var renderer = workbench.Surface.Renderer;
            // Keep real post-render counters without another traversal, control walk or JSON snapshot.
            // Session/layout/UI events still publish the complete actual-control state separately.
            BrowserFiles.PublishFrameDiagnostics(renderer.SceneRecordings, renderer.SceneReplays,
                renderer.RetainedSceneBytes, renderer.PaintBuilds, renderer.DashBuilds,
                renderer.EffectFilterBuilds, renderer.GradientBuilds, renderer.GeometryBuilds, renderer.CulledNodes);
        };
        workbench.LayoutUpdated += (_, _) => QueuePublish();
        workbench.Surface.SizeChanged += (_, _) => QueuePublish();
        workbench.Surface.PresentationChanged += _ => QueuePublish();
        QueuePublish();
    }
}
