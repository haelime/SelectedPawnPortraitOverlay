using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using Verse;

namespace SelectedPawnPortraitOverlay;

// Where a manual portrait correction is stored. Pawn entries live in the save, race and global ones in mod settings.
public enum AdjustmentScope
{
    Pawn,
    Race,
    Global
}

// A saved manual correction (zoom multiplier plus world-unit camera offset) on top of the automatic framing.
public sealed class SavedAdjustment : IExposable
{
    public const float MinZoom = 0.1f;
    public const float MaxZoom = 10f;

    public float Zoom = 1f;
    public float OffsetX;
    public float OffsetZ;

    // Shown in the saved-adjustments list when the pawn or race can no longer be found.
    public string Label;

    public bool IsIdentity => Mathf.Approximately(Zoom, 1f) && Mathf.Approximately(OffsetX, 0f) && Mathf.Approximately(OffsetZ, 0f);

    public PortraitAdjustment ToAdjustment()
    {
        return new PortraitAdjustment(Zoom, OffsetX, OffsetZ);
    }

    public void Clamp()
    {
        Zoom = Mathf.Clamp(float.IsNaN(Zoom) || float.IsInfinity(Zoom) ? 1f : Zoom, MinZoom, MaxZoom);
        OffsetX = float.IsNaN(OffsetX) || float.IsInfinity(OffsetX) ? 0f : OffsetX;
        OffsetZ = float.IsNaN(OffsetZ) || float.IsInfinity(OffsetZ) ? 0f : OffsetZ;
    }

    public string Summary()
    {
        return "PortraitOverlay.Edit.Readout".Translate(
            Zoom.ToString("F2", CultureInfo.InvariantCulture),
            FormatOffset(OffsetX),
            FormatOffset(OffsetZ));
    }

    public static string FormatOffset(float value)
    {
        return (value >= 0f ? "+" : string.Empty) + value.ToString("F2", CultureInfo.InvariantCulture);
    }

    public void ExposeData()
    {
        Scribe_Values.Look(ref Zoom, "zoom", 1f);
        Scribe_Values.Look(ref OffsetX, "offsetX", 0f);
        Scribe_Values.Look(ref OffsetZ, "offsetZ", 0f);
        Scribe_Values.Look(ref Label, "label");
        if (Scribe.mode == LoadSaveMode.LoadingVars)
        {
            Clamp();
        }
    }
}

public static class PortraitAdjustments
{
    // Global, race, and pawn corrections combined: zoom multiplies, offsets add. Each level is a difference on top
    // of the one below, so changing the global zoom later keeps "this pawn a bit larger" intact.
    public static PortraitAdjustment Resolve(Pawn pawn, PortraitOverlaySettings settings)
    {
        return ResolveExcluding(pawn, settings, null);
    }

    // The combined correction without one scope's own entry, which is what that scope's entry is applied on top of.
    public static PortraitAdjustment ResolveExcluding(Pawn pawn, PortraitOverlaySettings settings, AdjustmentScope? excluded)
    {
        var zoom = 1f;
        var offsetX = excluded == AdjustmentScope.Global ? 0f : settings.GlobalOffsetX;
        var offsetZ = excluded == AdjustmentScope.Global ? 0f : settings.GlobalOffsetZ;

        var race = excluded == AdjustmentScope.Race ? null : GetRace(pawn, settings);
        if (race != null)
        {
            zoom *= race.Zoom;
            offsetX += race.OffsetX;
            offsetZ += race.OffsetZ;
        }

        var own = excluded == AdjustmentScope.Pawn ? null : GetPawn(pawn);
        if (own != null)
        {
            zoom *= own.Zoom;
            offsetX += own.OffsetX;
            offsetZ += own.OffsetZ;
        }

        return new PortraitAdjustment(zoom, offsetX, offsetZ);
    }

    // Sets a pawn or race scope's entry outright (an identity value removes it).
    public static void Set(AdjustmentScope scope, Pawn pawn, PortraitOverlaySettings settings, PortraitAdjustment value)
    {
        Clear(scope, pawn, settings);
        Apply(scope, pawn, settings, value.Zoom, value.OffsetX, value.OffsetZ);
    }

    // Frames the pawn to a preset by writing the needed correction into a pawn or race scope.
    // portraitSize is the portrait area of the panel, which the preset is fitted to.
    public static void ApplyPreset(PortraitPreset preset, AdjustmentScope scope, Pawn pawn, Vector2 portraitSize, PortraitOverlaySettings settings)
    {
        if (scope == AdjustmentScope.Global || pawn == null)
        {
            return;
        }

        var humanlike = pawn.RaceProps?.Humanlike == true;
        var bounds = PortraitFitCache.GetBounds(pawn, settings) ?? FallbackBounds(pawn, humanlike);
        var headZ = PawnFullBodyPortraitRenderer.GetHumanlikeHeadZ(pawn, PawnFullBodyPortraitRenderer.GetRotation(pawn, settings));
        var aspect = portraitSize.y > 0f ? portraitSize.x / portraitSize.y : 1f;
        var target = PortraitFraming.PresetFrame(preset, bounds, headZ, aspect);
        var withoutScope = PawnFullBodyPortraitRenderer.ComputeFrame(
            pawn,
            portraitSize,
            settings,
            ResolveExcluding(pawn, settings, scope));
        Set(scope, pawn, settings, PortraitFraming.AdjustmentToReach(withoutScope, target));
    }

    private static PortraitBounds FallbackBounds(Pawn pawn, bool humanlike)
    {
        var graphic = humanlike ? null : pawn.ageTracker?.CurKindLifeStage?.bodyGraphicData;
        return PortraitFraming.FallbackBounds(humanlike, graphic?.drawSize.x ?? 0f, graphic?.drawSize.y ?? 0f);
    }

    public static SavedAdjustment GetRace(Pawn pawn, PortraitOverlaySettings settings)
    {
        var defName = pawn?.def?.defName;
        return defName != null && settings.RaceAdjustments.TryGetValue(defName, out var adjustment) ? adjustment : null;
    }

    public static SavedAdjustment GetPawn(Pawn pawn)
    {
        var store = PawnStore;
        return pawn != null && store != null && store.TryGetValue(pawn.thingIDNumber, out var adjustment) ? adjustment : null;
    }

    public static bool Has(AdjustmentScope scope, Pawn pawn, PortraitOverlaySettings settings)
    {
        return scope switch
        {
            AdjustmentScope.Pawn => GetPawn(pawn) != null,
            AdjustmentScope.Race => GetRace(pawn, settings) != null,
            _ => !Mathf.Approximately(settings.GlobalOffsetX, 0f) || !Mathf.Approximately(settings.GlobalOffsetZ, 0f)
        };
    }

    // Applies a zoom factor and offset delta to the given scope, creating or removing the stored entry as needed.
    public static void Apply(AdjustmentScope scope, Pawn pawn, PortraitOverlaySettings settings, float zoomFactor, float deltaX, float deltaZ)
    {
        switch (scope)
        {
            case AdjustmentScope.Global:
                settings.CameraZoom = Mathf.Clamp(
                    settings.CameraZoom * zoomFactor,
                    PortraitOverlaySettings.MinCameraZoom,
                    PortraitOverlaySettings.MaxCameraZoom);
                settings.GlobalOffsetX += deltaX;
                settings.GlobalOffsetZ += deltaZ;
                return;
            case AdjustmentScope.Race:
                ApplyTo(settings.RaceAdjustments, pawn?.def?.defName, pawn?.def?.LabelCap.ToString(), zoomFactor, deltaX, deltaZ);
                return;
            case AdjustmentScope.Pawn:
                if (pawn != null && PawnStore != null)
                {
                    ApplyTo(PawnStore, pawn.thingIDNumber, pawn.LabelShortCap, zoomFactor, deltaX, deltaZ);
                }

                return;
        }
    }

    public static void Clear(AdjustmentScope scope, Pawn pawn, PortraitOverlaySettings settings)
    {
        switch (scope)
        {
            case AdjustmentScope.Global:
                settings.GlobalOffsetX = 0f;
                settings.GlobalOffsetZ = 0f;
                return;
            case AdjustmentScope.Race:
                if (pawn?.def?.defName != null)
                {
                    settings.RaceAdjustments.Remove(pawn.def.defName);
                }

                return;
            case AdjustmentScope.Pawn:
                if (pawn != null)
                {
                    PawnStore?.Remove(pawn.thingIDNumber);
                }

                return;
        }
    }

    // Per-pawn entries of the current game, or null when no game is loaded.
    public static Dictionary<int, SavedAdjustment> PawnStore =>
        Current.Game?.GetComponent<SelectedPawnPortraitOverlayComponent>()?.PawnAdjustments;

    private static void ApplyTo<TKey>(
        Dictionary<TKey, SavedAdjustment> store,
        TKey key,
        string label,
        float zoomFactor,
        float deltaX,
        float deltaZ)
    {
        if (key == null)
        {
            return;
        }

        if (!store.TryGetValue(key, out var adjustment))
        {
            adjustment = new SavedAdjustment();
            store[key] = adjustment;
        }

        adjustment.Zoom *= zoomFactor;
        adjustment.OffsetX += deltaX;
        adjustment.OffsetZ += deltaZ;
        adjustment.Label = label ?? adjustment.Label;
        adjustment.Clamp();

        // An entry edited back to the identity is removed, so "has a correction" always means it changes something.
        if (adjustment.IsIdentity)
        {
            store.Remove(key);
        }
    }
}

// Everything an edit can change for one pawn, kept so Ctrl+Z can put it back.
public sealed class AdjustmentSnapshot
{
    private int pawnId;
    private string raceDefName;
    private SavedAdjustment pawnAdjustment;
    private SavedAdjustment raceAdjustment;
    private float cameraZoom;
    private float globalOffsetX;
    private float globalOffsetZ;
    private float topCutoff;
    private float bottomCutoff;
    private float panelWidth;
    private float panelHeight;

    public static AdjustmentSnapshot Capture(Pawn pawn, PortraitOverlaySettings settings)
    {
        return new AdjustmentSnapshot
        {
            pawnId = pawn?.thingIDNumber ?? -1,
            raceDefName = pawn?.def?.defName,
            pawnAdjustment = Copy(PortraitAdjustments.GetPawn(pawn)),
            raceAdjustment = Copy(PortraitAdjustments.GetRace(pawn, settings)),
            cameraZoom = settings.CameraZoom,
            globalOffsetX = settings.GlobalOffsetX,
            globalOffsetZ = settings.GlobalOffsetZ,
            topCutoff = settings.TopCutoff,
            bottomCutoff = settings.BottomCutoff,
            panelWidth = settings.PanelWidth,
            panelHeight = settings.PanelHeight
        };
    }

    public void Restore(PortraitOverlaySettings settings)
    {
        var pawnStore = PortraitAdjustments.PawnStore;
        if (pawnStore != null && pawnId >= 0)
        {
            Put(pawnStore, pawnId, pawnAdjustment);
        }

        if (raceDefName != null)
        {
            Put(settings.RaceAdjustments, raceDefName, raceAdjustment);
        }

        settings.CameraZoom = cameraZoom;
        settings.GlobalOffsetX = globalOffsetX;
        settings.GlobalOffsetZ = globalOffsetZ;
        settings.TopCutoff = topCutoff;
        settings.BottomCutoff = bottomCutoff;
        settings.PanelWidth = panelWidth;
        settings.PanelHeight = panelHeight;
        settings.ClampValues();
    }

    private static void Put<TKey>(Dictionary<TKey, SavedAdjustment> store, TKey key, SavedAdjustment value)
    {
        if (value == null)
        {
            store.Remove(key);
        }
        else
        {
            store[key] = Copy(value);
        }
    }

    private static SavedAdjustment Copy(SavedAdjustment source)
    {
        return source == null
            ? null
            : new SavedAdjustment { Zoom = source.Zoom, OffsetX = source.OffsetX, OffsetZ = source.OffsetZ, Label = source.Label };
    }
}
