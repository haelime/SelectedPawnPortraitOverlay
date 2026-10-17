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
    public const float MinPanelScale = 0.2f;
    public const float MaxPanelScale = 3f;

    public float Zoom = 1f;
    public float OffsetX;
    public float OffsetZ;

    // Panel size relative to the base size in settings, so a close-up preset can use a smaller panel.
    public float PanelWidthScale = 1f;
    public float PanelHeightScale = 1f;

    // Shown in the saved-adjustments list when the pawn or race can no longer be found.
    public string Label;

    public bool IsIdentity => Mathf.Approximately(Zoom, 1f)
        && Mathf.Approximately(OffsetX, 0f)
        && Mathf.Approximately(OffsetZ, 0f)
        && !HasPanelScale;

    public bool HasPanelScale => !Mathf.Approximately(PanelWidthScale, 1f) || !Mathf.Approximately(PanelHeightScale, 1f);

    public SavedAdjustment Clone()
    {
        return (SavedAdjustment)MemberwiseClone();
    }

    public PortraitAdjustment ToAdjustment()
    {
        return new PortraitAdjustment(Zoom, OffsetX, OffsetZ);
    }

    public void Clamp()
    {
        Zoom = Mathf.Clamp(float.IsNaN(Zoom) || float.IsInfinity(Zoom) ? 1f : Zoom, MinZoom, MaxZoom);
        OffsetX = float.IsNaN(OffsetX) || float.IsInfinity(OffsetX) ? 0f : OffsetX;
        OffsetZ = float.IsNaN(OffsetZ) || float.IsInfinity(OffsetZ) ? 0f : OffsetZ;
        PanelWidthScale = Mathf.Clamp(float.IsNaN(PanelWidthScale) ? 1f : PanelWidthScale, MinPanelScale, MaxPanelScale);
        PanelHeightScale = Mathf.Clamp(float.IsNaN(PanelHeightScale) ? 1f : PanelHeightScale, MinPanelScale, MaxPanelScale);
    }

    public string Summary()
    {
        return Format(Zoom, OffsetX, OffsetZ, PanelWidthScale, PanelHeightScale);
    }

    public static string Format(float zoom, float offsetX, float offsetZ, float panelWidthScale, float panelHeightScale)
    {
        var zoomText = zoom.ToString("F2", CultureInfo.InvariantCulture);
        var hasPanelScale = !Mathf.Approximately(panelWidthScale, 1f) || !Mathf.Approximately(panelHeightScale, 1f);
        return hasPanelScale
            ? "PortraitOverlay.Edit.ReadoutWithPanel".Translate(
                zoomText,
                FormatOffset(offsetX),
                FormatOffset(offsetZ),
                Mathf.RoundToInt(panelWidthScale * 100f).ToString(),
                Mathf.RoundToInt(panelHeightScale * 100f).ToString())
            : "PortraitOverlay.Edit.Readout".Translate(zoomText, FormatOffset(offsetX), FormatOffset(offsetZ));
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
        Scribe_Values.Look(ref PanelWidthScale, "panelWidthScale", 1f);
        Scribe_Values.Look(ref PanelHeightScale, "panelHeightScale", 1f);
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

    // Combined panel size multiplier from the race and pawn entries (1, 1 when neither changes the panel).
    public static Vector2 ResolvePanelScale(Pawn pawn, PortraitOverlaySettings settings, AdjustmentScope? excluded = null)
    {
        var scale = Vector2.one;
        var race = excluded == AdjustmentScope.Race ? null : GetRace(pawn, settings);
        if (race != null)
        {
            scale.x *= race.PanelWidthScale;
            scale.y *= race.PanelHeightScale;
        }

        var own = excluded == AdjustmentScope.Pawn ? null : GetPawn(pawn);
        if (own != null)
        {
            scale.x *= own.PanelWidthScale;
            scale.y *= own.PanelHeightScale;
        }

        return scale;
    }

    // Sets a pawn or race scope's entry outright (an identity value removes it). The entry's panel size is kept
    // unless a new one is given.
    public static void Set(AdjustmentScope scope, Pawn pawn, PortraitOverlaySettings settings, PortraitAdjustment value, Vector2? panelScale = null)
    {
        if (scope == AdjustmentScope.Global || pawn == null)
        {
            return;
        }

        var existing = scope == AdjustmentScope.Pawn ? GetPawn(pawn) : GetRace(pawn, settings);
        var scale = panelScale ?? (existing != null ? new Vector2(existing.PanelWidthScale, existing.PanelHeightScale) : Vector2.one);
        var entry = new SavedAdjustment
        {
            Zoom = value.Zoom,
            OffsetX = value.OffsetX,
            OffsetZ = value.OffsetZ,
            PanelWidthScale = scale.x,
            PanelHeightScale = scale.y
        };
        entry.Clamp();
        if (scope == AdjustmentScope.Pawn)
        {
            entry.Label = pawn.LabelShortCap;
            Store(PawnStore, pawn.thingIDNumber, entry);
        }
        else
        {
            entry.Label = pawn.def.LabelCap.ToString();
            Store(settings.RaceAdjustments, pawn.def.defName, entry);
        }
    }

    // Frames the pawn to a preset by writing the needed correction and panel size into a pawn or race scope.
    // The panel size is set so the final panel is the preset's share of the base size, whatever other scopes hold.
    public static void ApplyPreset(PortraitPreset preset, AdjustmentScope scope, Pawn pawn, PortraitOverlaySettings settings)
    {
        if (scope == AdjustmentScope.Global || pawn == null)
        {
            return;
        }

        var (widthScale, heightScale) = PortraitFraming.PresetPanelScale(preset);
        var otherScale = ResolvePanelScale(pawn, settings, scope);
        var scopeScale = new Vector2(widthScale / Mathf.Max(0.01f, otherScale.x), heightScale / Mathf.Max(0.01f, otherScale.y));
        var portraitSize = SelectedPawnPortraitOverlayComponent.PortraitSizeFor(
            new Vector2(settings.PanelWidth * widthScale, settings.PanelHeight * heightScale),
            settings);

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
        Set(scope, pawn, settings, PortraitFraming.AdjustmentToReach(withoutScope, target), scopeScale);
    }

    private static void Store<TKey>(Dictionary<TKey, SavedAdjustment> store, TKey key, SavedAdjustment entry)
    {
        if (store == null)
        {
            return;
        }

        if (entry.IsIdentity)
        {
            store.Remove(key);
        }
        else
        {
            store[key] = entry;
        }
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
        return source?.Clone();
    }
}
