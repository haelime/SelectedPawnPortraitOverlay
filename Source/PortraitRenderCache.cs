using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace SelectedPawnPortraitOverlay;

public static class PortraitRenderCache
{
    private const int PortraitRenderWarningId = 18467233;

    // A still portrait is redrawn this often to pick up changes the game does not report (some mods' faces, for
    // one). Changes the game does report, such as apparel, mark the portrait dirty and redraw it right away.
    private const float StillRefreshSeconds = 0.25f;

    private static Pawn lastRefreshedPawn;
    private static float lastRefreshTime = float.NegativeInfinity;

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
                    // A live portrait follows the pawn's pose, so it is redrawn every frame. A dirty cache entry
                    // is re-rendered into its existing texture, so the returned texture stays alive; destroying it
                    // instead would hand back a dead texture, since RimWorld 1.7 destroys render textures at once.
                    var now = Time.realtimeSinceStartup;
                    if (settings.LivePortrait || pawn != lastRefreshedPawn || now - lastRefreshTime >= StillRefreshSeconds)
                    {
                        PortraitsCache.SetDirty(pawn);
                        lastRefreshedPawn = pawn;
                        lastRefreshTime = now;
                    }

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
    }
}
