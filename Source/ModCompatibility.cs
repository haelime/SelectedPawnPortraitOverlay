using System;
using System.Linq;
using Verse;

namespace SelectedPawnPortraitOverlay;

public static class ModCompatibility
{
    public static bool FacialAnimationActive { get; private set; }

    public static string FacialAnimationStatusLabel =>
        FacialAnimationActive ? "PortraitOverlay.Settings.FacialAnimationActive" : "PortraitOverlay.Settings.FacialAnimationInactive";

    public static void Initialize()
    {
        FacialAnimationActive = LoadedModManager.RunningModsListForReading.Any(IsFacialAnimationPack);
    }

    private static bool IsFacialAnimationPack(ModContentPack pack)
    {
        if (pack == null)
        {
            return false;
        }

        return Contains(pack.Name, "Facial Animation")
            || Contains(pack.PackageIdPlayerFacing, "facial");
    }

    private static bool Contains(string source, string value)
    {
        return !string.IsNullOrEmpty(source)
            && source.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
