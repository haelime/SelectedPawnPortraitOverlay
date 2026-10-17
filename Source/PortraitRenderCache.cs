using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace SelectedPawnPortraitOverlay;

public static class PortraitRenderCache
{
    private const int PortraitRenderWarningId = 18467233;

    public static Texture GetPortrait(Pawn pawn, Vector2 size, PortraitOverlaySettings settings)
    {
        if (pawn == null)
        {
            return BaseContent.BadTex;
        }

        try
        {
            return ModCompatibility.WithPortraitPreferences(
                pawn,
                settings.RenderHeadgear,
                ShowWeaponsUnderPortraitMode.WhileDrafted,
                () =>
                {
                    // A dirty cache entry is re-rendered into its existing texture, so the
                    // returned texture stays alive. Destroying it here would hand back a dead
                    // texture: RimWorld 1.7 destroys render textures immediately.
                    InvalidatePortraitState(pawn);
                    return PawnFullBodyPortraitRenderer.Render(pawn, size, settings);
                });
        }
        catch (Exception exception)
        {
            var warningKey = PortraitRenderWarningId ^ (pawn.GetType().FullName?.GetHashCode() ?? 0);
            Log.WarningOnce(
                $"Selected Pawn Portrait Overlay skipped portrait rendering for unsupported pawn type '{pawn.GetType().FullName}'. {exception.GetType().Name}: {exception.Message}",
                warningKey);
            return BaseContent.BadTex;
        }
        finally
        {
            InvalidatePortraitState(pawn);
        }
    }

    private static void InvalidatePortraitState(Pawn pawn)
    {
        if (pawn == null)
        {
            return;
        }

        PortraitsCache.SetDirty(pawn);
    }
}
