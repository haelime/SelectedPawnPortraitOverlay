using System;
using Verse;

namespace SelectedPawnPortraitOverlay;

public static class PortraitRaceScale
{
    // A per-race override wins on scale; otherwise auto-fit scales the pawn by its measured drawn area.
    // Without auto-fit every pawn is framed like a human, as before auto-fit existed. aspect is the
    // portrait rect's width / height.
    public static PortraitFit For(Pawn pawn, float aspect, PortraitOverlaySettings settings)
    {
        float? overrideScale = null;
        var defName = pawn.def?.defName;
        if (defName != null && settings.RaceZoomOverrides.TryGetValue(defName, out var raceZoom))
        {
            overrideScale = raceZoom;
        }

        if (!settings.AutoFitPortrait)
        {
            return new PortraitFit(overrideScale ?? 1f);
        }

        var humanlike = pawn.RaceProps?.Humanlike == true;
        var graphicData = humanlike ? null : GetBodyGraphicData(pawn);
        var texelDensity = GetTexelDensity(graphicData);
        var bounds = PortraitFitCache.GetBounds(pawn, settings);
        if (bounds.HasValue)
        {
            return PortraitFraming.Fit(bounds.Value, aspect, humanlike, texelDensity, overrideScale);
        }

        if (overrideScale.HasValue)
        {
            return new PortraitFit(overrideScale.Value);
        }

        // Measurement unavailable: fall back to the graphic's drawSize for non-humanlike pawns.
        return new PortraitFit(graphicData != null
            ? PortraitFraming.AutoFitScale(Math.Max(graphicData.drawSize.x, graphicData.drawSize.y), texelDensity)
            : 1f);
    }

    // Scale a race renders with before it has an override, for seeding a new override entry.
    public static float DefaultFor(ThingDef race, float aspect, PortraitOverlaySettings settings)
    {
        if (!settings.AutoFitPortrait || race.race == null)
        {
            return 1f;
        }

        if (race.race.Humanlike)
        {
            var humanBounds = PortraitFitCache.FindBoundsForRace(race);
            return humanBounds.HasValue ? PortraitFraming.FitScale(humanBounds.Value, aspect, true, null) : 1f;
        }

        PawnKindDef kind = null;
        foreach (var candidate in DefDatabase<PawnKindDef>.AllDefsListForReading)
        {
            if (candidate.race == race)
            {
                kind = candidate;
                break;
            }
        }

        var graphicData = kind?.lifeStages?.Count > 0 ? kind.lifeStages[kind.lifeStages.Count - 1].bodyGraphicData : null;
        var texelDensity = GetTexelDensity(graphicData);
        var bounds = PortraitFitCache.FindBoundsForRace(race);
        if (bounds.HasValue)
        {
            return PortraitFraming.FitScale(bounds.Value, aspect, false, texelDensity);
        }

        return graphicData != null
            ? PortraitFraming.AutoFitScale(Math.Max(graphicData.drawSize.x, graphicData.drawSize.y), texelDensity)
            : 1f;
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
