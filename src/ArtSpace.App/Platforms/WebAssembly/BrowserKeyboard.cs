using System.Runtime.InteropServices.JavaScript;
using ArtSpace.Workbench;
using Windows.System;

namespace ArtSpace.App;

/// <summary>Browser input adapter. Document and gesture decisions remain in the shared C# workbench.</summary>
internal static partial class BrowserKeyboard
{
    [JSImport("globalThis.artSpaceKeyboard.install")]
    private static partial void Install(
        [JSMarshalAs<JSType.Function<JSType.Number, JSType.Number>>] Func<int, int> route,
        [JSMarshalAs<JSType.Function<JSType.Number>>] Action<int> modifiersChanged);

    public static void Attach(StudioWorkbench workbench)
    {
        Install(key => workbench.HandleHostNavigation((VirtualKey)key) ? 1 : 0,
            modifiers => workbench.Surface.HostModifiers = (VirtualKeyModifiers)modifiers);
    }
}
