using System.Runtime.CompilerServices;
using OpenTabletArtist.Domain;

namespace OpenTabletArtist.Services;

/// <summary>TASKS #63: the radial menu model is shared with plugins that have no log; the app wires it to its own.</summary>
internal static class RadialMenuLogHook
{
    [ModuleInitializer]
    internal static void Init() => RadialMenuJson.Log = AppLog.Info;
}
