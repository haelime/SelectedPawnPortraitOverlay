using System;
using System.Reflection;
using RimWorld;
using Verse;

namespace SelectedPawnPortraitOverlay;

public static class ModCompatibility
{
    // Prefs' setters call Prefs.Apply(), which in 1.7 also re-applies the window mode, resolution, and audio.
    // Portraits are rendered every frame, so the preferences are swapped on the backing PrefsData instead.
    private static readonly FieldInfo PrefsDataField = typeof(Prefs).GetField("data", BindingFlags.NonPublic | BindingFlags.Static);

    // Renders a portrait with the overlay's own headgear and weapon choices in place of the player's preferences,
    // restoring them even when rendering throws.
    public static T WithPortraitPreferences<T>(Pawn pawn, bool renderHeadgear, ShowWeaponsUnderPortraitMode weaponMode, Func<T> render)
    {
        if (render == null)
        {
            return default;
        }

        var data = PrefsDataField?.GetValue(null) as PrefsData;
        var originalHatsOnlyOnMap = Prefs.HatsOnlyOnMap;
        var originalWeaponMode = Prefs.ShowWeaponsUnderPortraitMode;
        var hatsOnlyOnMap = originalHatsOnlyOnMap && !renderHeadgear;
        var changed = hatsOnlyOnMap != originalHatsOnlyOnMap || weaponMode != originalWeaponMode;
        if (changed)
        {
            SetPreferences(data, hatsOnlyOnMap, weaponMode);

            // The pawn's render tree keeps the list of parts to draw until its draw parameters change, and the
            // colonist bar portrait uses the same parameters. Without a rebuild the overlay would reuse a list made
            // under the player's preferences (hats hidden), and the colonist bar would then reuse the overlay's.
            RequestDrawRecache(pawn);
        }

        try
        {
            return render();
        }
        finally
        {
            if (changed)
            {
                SetPreferences(data, originalHatsOnlyOnMap, originalWeaponMode);
                RequestDrawRecache(pawn);
            }
        }
    }

    private static void SetPreferences(PrefsData data, bool hatsOnlyOnMap, ShowWeaponsUnderPortraitMode weaponMode)
    {
        if (data != null)
        {
            data.hatsOnlyOnMap = hatsOnlyOnMap;
            data.showWeaponsUnderPortraitMode = weaponMode;
            return;
        }

        Prefs.HatsOnlyOnMap = hatsOnlyOnMap;
        Prefs.ShowWeaponsUnderPortraitMode = weaponMode;
    }

    private static void RequestDrawRecache(Pawn pawn)
    {
        var root = pawn?.Drawer?.renderer?.renderTree?.rootNode;
        if (root != null)
        {
            root.requestRecache = true;
        }
    }
}
