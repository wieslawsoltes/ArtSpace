using ArtSpace.Core;
using SkiaSharp;

namespace ArtSpace.Skia;

public sealed partial class SceneRenderer
{
    private SKPicture? _scenePicture;
    private DesignPage? _scenePage;
    private List<DesignNode>? _sceneNodes;
    private RectD _sceneCoverage;
    private bool _sceneOutlines, _sceneRejected;
    private bool _enableRetainedScene = true;
    private bool _enableSceneSpatialIndex = true;
    private int _retainedSceneBudgetBytes = 32 * 1024 * 1024;

    /// <summary>Disabling retention immediately releases recorded native commands.</summary>
    public bool EnableRetainedScene
    {
        get => _enableRetainedScene;
        set
        {
            if (_enableRetainedScene == value) return;
            _enableRetainedScene = value;
            InvalidateRetainedScene();
        }
    }

    /// <summary>
    /// Builds Skia's native R-tree for clip-aware command playback. This indexes drawing operations,
    /// not mutable document objects. It changes recording cost and native memory, not visual semantics.
    /// </summary>
    public bool EnableSceneSpatialIndex
    {
        get => _enableSceneSpatialIndex;
        set
        {
            if (_enableSceneSpatialIndex == value) return;
            _enableSceneSpatialIndex = value;
            InvalidateRetainedScene();
        }
    }

    /// <summary>
    /// Approximate native command storage budget, not a GPU memory or transient-allocation limit.
    /// Values at or below zero disable recording. Changing the budget retries a previously rejected scene.
    /// </summary>
    public int RetainedSceneBudgetBytes
    {
        get => _retainedSceneBudgetBytes;
        set
        {
            if (_retainedSceneBudgetBytes == value) return;
            _retainedSceneBudgetBytes = value;
            InvalidateRetainedScene();
        }
    }

    public long SceneRecordings { get; private set; }
    public long SceneReplays { get; private set; }
    public int RetainedSceneBytes => _scenePicture?.ApproximateBytesUsed ?? 0;

    /// <summary>
    /// Replays scene commands directly into the host's Skia canvas. Call InvalidateRetainedScene after
    /// any model mutation. Selection, hover and modest panning do not invalidate scene content.
    /// This is a vector display list, not a raster screenshot, CPU framebuffer, or GPU-compute engine.
    /// </summary>
    public void DrawRetained(SKCanvas canvas, DesignPage page, RectD worldViewport)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(page);
        if (worldViewport.IsEmpty || !double.IsFinite(worldViewport.X) || !double.IsFinite(worldViewport.Y)
            || !double.IsFinite(worldViewport.Right) || !double.IsFinite(worldViewport.Bottom))
            throw new ArgumentOutOfRangeException(nameof(worldViewport));
        if (!ReferenceEquals(_scenePage, page) || !ReferenceEquals(_sceneNodes, page.Nodes) || _sceneOutlines != Outlines)
        {
            InvalidateRetainedScene();
            _scenePage = page; _sceneNodes = page.Nodes; _sceneOutlines = Outlines;
        }
        if (!EnableRetainedScene || RetainedSceneBudgetBytes <= 0 || _sceneRejected)
        {
            Draw(canvas, page.Nodes, worldViewport);
            return;
        }
        if (_scenePicture is null || !_sceneCoverage.Contains(new(worldViewport.X, worldViewport.Y))
            || !_sceneCoverage.Contains(new(worldViewport.Right, worldViewport.Bottom)))
        {
            _scenePicture?.Dispose(); _scenePicture = null;
            // Overscan permits modest pan/zoom without rebuilding. The native R-tree tests destination
            // clipping during playback, rather than freezing scale-dependent managed culling decisions.
            _sceneCoverage = worldViewport.Inflate(Math.Max(worldViewport.Width, worldViewport.Height) * .25 + 4);
            using var recorder = new SKPictureRecorder();
            var recording = recorder.BeginRecording(Rect(_sceneCoverage), EnableSceneSpatialIndex);
            var culling = EnableCulling;
            try
            {
                EnableCulling = false;
                Draw(recording, page.Nodes, _sceneCoverage);
                _scenePicture = recorder.EndRecording()
                    ?? throw new InvalidOperationException("Cannot record the scene display list.");
                SceneRecordings++;
            }
            finally { EnableCulling = culling; }
            if (_scenePicture.ApproximateBytesUsed > RetainedSceneBudgetBytes)
            {
                _scenePicture.Dispose(); _scenePicture = null; _sceneRejected = true;
                Draw(canvas, page.Nodes, worldViewport);
                return;
            }
        }
        // Interactive overlays are drawn separately by the editor after replay.
        canvas.DrawPicture(_scenePicture);
        SceneReplays++;
    }

    /// <summary>Release retained commands before editing, model replacement, font replacement, or disposal.</summary>
    public void InvalidateRetainedScene()
    {
        _scenePicture?.Dispose(); _scenePicture = null;
        _scenePage = null; _sceneNodes = null; _sceneRejected = false;
    }
}
