using System.Collections.Generic;
using System.Drawing;
using System.IO;

namespace RoadGen;

/// <summary>
/// RoadGen features that go BEYOND stock Hammer — "non-native" helpers. Anything here is a
/// deliberate, opt-in extension that Hammer does NOT do, so it stays labelled as such (in code
/// docs and, where user-visible, in the UI) so texture/preview output is never mistaken for
/// what Hammer or the game would show.
///
/// Current non-native feature: auto-mounting a decompiled map's own content folder when a
/// layout .vmf is imported. Hammer only mounts gameinfo content + the game/mod dirs
/// (hammer_launcher/main.cpp -&gt; FileSystem_LoadSearchPaths; TextureSystem.cpp adds the game
/// dir to "GAME"), so the map-local loose materials a Crowbar/BSPSource decompile produces are
/// missing in Hammer — and would be missing here too without this extension.
/// </summary>
public static class NonNativeHelpers
{
    /// <summary>Tooltip text shown in the texture status when the non-native layout mount is
    /// active, so users know the visible textures are a RoadGen extra, not Hammer output.</summary>
    public const string LayoutExtraMountNote =
        "Note: the imported layout's map-content folder is mounted — a RoadGen extra. " +
        "Hammer does not auto-mount map folders, so these textures would be missing in " +
        "Hammer/game output.";

    /// <summary>NON-NATIVE (not Hammer): returns the content folders that should be mounted for
    /// an imported layout .vmf so its decompiled map-local materials resolve. Crowbar/BSPSource
    /// decompiles put a map's loose custom materials in a sibling folder named after the .vmf
    /// (tf\maps\&lt;map&gt;.vmf next to tf\maps\&lt;map&gt;\materials), which Hammer never mounts.
    /// Candidates are returned most-specific first; empty/duplicate/non-mountable ones are
    /// filtered by the mount layer that consumes them.</summary>
    public static IReadOnlyList<string> LayoutMaterialContentRoots(string vmfPath)
    {
        var roots = new List<string>();
        if (string.IsNullOrWhiteSpace(vmfPath))
        {
            return roots;
        }

        string dir = Path.GetDirectoryName(vmfPath);
        if (string.IsNullOrEmpty(dir))
        {
            return roots;
        }

        // <maps>\<map>.vmf -> <maps>\<map>\materials (decompile convention).
        string baseName = Path.GetFileNameWithoutExtension(vmfPath);
        if (!string.IsNullOrEmpty(baseName))
        {
            roots.Add(Path.Combine(dir, baseName));
        }

        // The .vmf's own folder, for layouts that keep materials directly beside it.
        roots.Add(dir);

        return roots;
    }

    /// <summary>NON-NATIVE water representation: RoadGen cannot run Hammer's engine Water
    /// shader (that needs the game's GPU material system + render targets), so faces whose
    /// material is water are filled with this flat colour instead. Hammer would show real
    /// (refracting/reflecting) shader water. Detection is Hammer-faithful (%compilewater /
    /// $surfaceprop "water", see CMaterial::IsWater) — only the fill is an approximation.</summary>
    public static readonly Color WaterSurfaceColor = Color.FromArgb(255, 64, 118, 176);

    /// <summary>Suffix text used by the status/report when water faces are shown as the flat
    /// approximation, so it is clear this is a RoadGen extra, not Hammer output.</summary>
    public const string WaterApproxNote =
        "water material(s) shown as a flat colour — a RoadGen approximation " +
        "(Hammer renders shader water; water .vmts have no plain $basetexture texture to decode)";
}
