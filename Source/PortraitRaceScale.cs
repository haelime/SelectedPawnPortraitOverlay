using System;
using Verse;

namespace SelectedPawnPortraitOverlay;

public static class PortraitRaceScale
{
    // Auto-fit scales the pawn by its measured drawn area. Without auto-fit every pawn is framed like a human,
    // as before auto-fit existed. Manual corrections (PortraitAdjustments) are applied on top of this.
    // aspect is the portrait rect's width / height.
    public static PortraitFit For(Pawn pawn, float aspect, PortraitOverlaySettings settings)
    {
        if (!settings.AutoFitPortrait)
        {
            return new PortraitFit(1f);
        }

        var humanlike = pawn.RaceProps?.Humanlike == true;
        var graphicData = humanlike ? null : GetBodyGraphicData(pawn);
        var texelDensity = GetTexelDensity(graphicData);
        var bounds = PortraitFitCache.GetBounds(pawn, settings);
        if (bounds.HasValue)
        {
            return PortraitFraming.Fit(bounds.Value, aspect, humanlike, texelDensity);
        }

        // Measurement unavailable: fall back to the graphic's drawSize for non-humanlike pawns.
        return new PortraitFit(graphicData != null
            ? PortraitFraming.AutoFitScale(Math.Max(graphicData.drawSize.x, graphicData.drawSize.y), texelDensity)
            : 1f);
    }

    // Body texture pixels per world unit for a non-humanlike pawn, or null when unknown.
    public static float? TexelDensity(Pawn pawn)
    {
        return GetTexelDensity(GetBodyGraphicData(pawn));
    }

    private static GraphicData GetBodyGraphicData(Pawn pawn)
    {
        var lifeStage = pawn.ageTracker?.CurKindLifeStage;
        return pawn.gender == Gender.Female && lifeStage?.femaleGraphicData != null
            ? lifeStage.femaleGraphicData
            : lifeStage?.bodyGraphicData;
    }

    // Body texture pixels per world unit, or null when the graphic or its texture is unavailable.
    private static float? GetTexelDensity(GraphicData graphicData)
    {
        if (graphicData == null || !(graphicData.drawSize.y > 0f))
        {
            return null;
        }

        try
        {
            var texture = graphicData.Graphic?.MatSouth?.mainTexture;
            return texture != null && texture.height > 0 ? texture.height / graphicData.drawSize.y : null;
        }
        catch (Exception)
        {
            // Some modded graphic classes fail outside their own draw path; the conservative cap applies.
            return null;
        }
    }
}
