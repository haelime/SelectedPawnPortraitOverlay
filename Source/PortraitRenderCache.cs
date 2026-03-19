using System.Reflection;
using RimWorld;
using UnityEngine;
using Verse;

namespace SelectedPawnPortraitOverlay;

public static class PortraitRenderCache
{
    private static readonly MethodInfo SetAnimatedPortraitsDirtyMethod =
        typeof(PortraitsCache).GetMethod("SetAnimatedPortraitsDirty", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

    public static Texture GetPortrait(Pawn pawn, Vector2 size, PortraitOverlaySettings settings)
    {
        if (pawn == null)
        {
            return BaseContent.BadTex;
        }

        if (settings.LivePortrait)
        {
            SetAnimatedPortraitsDirtyMethod?.Invoke(null, null);
            PortraitsCache.SetDirty(pawn);
        }

        return PawnFullBodyPortraitRenderer.Render(pawn, size, settings);
    }
}
