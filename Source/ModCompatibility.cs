using System;
using RimWorld;
using Verse;

namespace SelectedPawnPortraitOverlay;

public static class ModCompatibility
{
    public static T WithPortraitWeaponRenderMode<T>(Func<T> render)
    {
        return WithPortraitWeaponMode(ShowWeaponsUnderPortraitMode.WhileDrafted, render);
    }

    public static T WithPortraitWeaponMode<T>(ShowWeaponsUnderPortraitMode mode, Func<T> render)
    {
        if (render == null)
        {
            return default;
        }

        // These are global game preferences; restore them even when portrait rendering throws.
        var originalMode = Prefs.ShowWeaponsUnderPortraitMode;
        if (originalMode == mode)
        {
            return render();
        }

        Prefs.ShowWeaponsUnderPortraitMode = mode;
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

        // Temporarily override the map-only hat preference for this portrait render.
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
