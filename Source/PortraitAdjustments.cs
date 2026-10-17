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
        var zoom = 1f;
        var offsetX = settings.GlobalOffsetX;
        var offsetZ = settings.GlobalOffsetZ;

        var race = GetRace(pawn, settings);
        if (race != null)
        {
            zoom *= race.Zoom;
            offsetX += race.OffsetX;
            offsetZ += race.OffsetZ;
        }

        var own = GetPawn(pawn);
        if (own != null)
        {
            zoom *= own.Zoom;
            offsetX += own.OffsetX;
            offsetZ += own.OffsetZ;
        }

        return new PortraitAdjustment(zoom, offsetX, offsetZ);
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
