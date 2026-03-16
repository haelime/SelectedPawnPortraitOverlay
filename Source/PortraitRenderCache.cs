using UnityEngine;
using Verse;

namespace SelectedPawnPortraitOverlay;

public static class PortraitRenderCache
{
    public static Texture GetPortrait(Pawn pawn, Vector2 size, PortraitOverlaySettings settings)
    {
        return PawnFullBodyPortraitRenderer.Render(pawn, size, settings);
    }
}
