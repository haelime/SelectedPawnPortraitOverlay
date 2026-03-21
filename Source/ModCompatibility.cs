using System;
using RimWorld;
using Verse;

namespace SelectedPawnPortraitOverlay;

public static class ModCompatibility
{
    public static T WithPortraitWeaponRenderMode<T>(Func<T> render)
    {
        if (render == null)
        {
            return default;
        }

        var originalMode = Prefs.ShowWeaponsUnderPortraitMode;
        if (originalMode == ShowWeaponsUnderPortraitMode.WhileDrafted)
        {
            return render();
        }

        Prefs.ShowWeaponsUnderPortraitMode = ShowWeaponsUnderPortraitMode.WhileDrafted;
        try
        {
            return render();
        }
        finally
        {
            Prefs.ShowWeaponsUnderPortraitMode = originalMode;
        }
    }

    public static T WithPortraitHeadgearPreference<T>(bool renderHeadgear, Func<T> render)
    {
        if (render == null)
        {
            return default;
        }

        var originalValue = Prefs.HatsOnlyOnMap;
        if (!renderHeadgear || !originalValue)
        {
            return render();
        }

        Prefs.HatsOnlyOnMap = false;
        try
        {
            return render();
        }
        finally
        {
            Prefs.HatsOnlyOnMap = originalValue;
        }
    }
}
