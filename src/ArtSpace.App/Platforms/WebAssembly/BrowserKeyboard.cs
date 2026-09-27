using System.Runtime.InteropServices.JavaScript;
using ArtSpace.Workbench;
using Windows.System;

namespace ArtSpace.App;

/// <summary>Platform input adapter, active in production as well as tests. It contains no editor or geometry logic.</summary>
internal static partial class BrowserKeyboard
{
    [JSImport("globalThis.artSpaceKeyboard.install")]
    private static partial void Install([JSMarshalAs<JSType.Function<JSType.Number, JSType.Number>>] Func<int, int> route);

    public static void Attach(StudioWorkbench workbench)
    {
        Install(key => workbench.HandleHostNavigation((VirtualKey)key) ? 1 : 0);
    }
}
