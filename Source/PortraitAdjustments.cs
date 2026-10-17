using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using Verse;

namespace SelectedPawnPortraitOverlay;

// Where a manual portrait correction is stored, from broadest to narrowest. Each level applies on top of the
// ones before it. Pawn entries live in the save; the others live in mod settings and apply to every save.
public enum AdjustmentScope
{
    Global,
    Category,
    Race,
    Variant,
    Pawn
}

// Broad pawn groups for the Category scope.
public enum PawnCategory
{
    Humanlike,
    Animal,
    Mechanoid,
    Insect,
    Anomaly,
    Other
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
    // Scopes stored in mod settings by a string key, in the order they apply.
    private static readonly AdjustmentScope[] KeyedScopes = { AdjustmentScope.Category, AdjustmentScope.Race, AdjustmentScope.Variant };

    // Mechanoids and Anomaly entities first, and insects before animals, because insects also count as animals.
    public static PawnCategory GetCategory(Pawn pawn)
    {
        var race = pawn?.RaceProps;
        if (race == null)
        {
            return PawnCategory.Other;
        }

        if (race.IsMechanoid)
        {
            return PawnCategory.Mechanoid;
        }

        if (race.IsAnomalyEntity)
        {
            return PawnCategory.Anomaly;
        }

        if (race.Insect)
        {
            return PawnCategory.Insect;
        }

        if (race.Animal)
        {
            return PawnCategory.Animal;
        }

        return race.Humanlike ? PawnCategory.Humanlike : PawnCategory.Other;
    }

    public static string CategoryLabel(PawnCategory category)
    {
        return ("PortraitOverlay.Category." + category).Translate();
    }

    public static string PresetLabel(PortraitPreset preset)
    {
        return preset switch
        {
            PortraitPreset.UpperBody => "PortraitOverlay.Edit.PresetUpper".Translate(),
            PortraitPreset.Face => "PortraitOverlay.Edit.PresetFace".Translate(),
            _ => "PortraitOverlay.Edit.PresetFull".Translate()
        };
    }

    // The variant within a race: the xenotype for humanlike pawns, the growth stage for races with several.
    // Returns false when the pawn's race has no meaningful variant (most mechanoids and entities).
    public static bool TryGetVariant(Pawn pawn, out string key, out string label)
    {
        key = null;
        label = null;
        if (pawn?.def == null || pawn.RaceProps == null)
        {
            return false;
        }

        string variantKey = null;
        string variantLabel = null;
        if (pawn.RaceProps.Humanlike)
        {
            var genes = pawn.genes;
            if (genes?.CustomXenotype != null)
            {
                variantKey = "xenotype:custom:" + genes.xenotypeName;
                variantLabel = genes.XenotypeLabelCap;
            }
            else if (genes?.Xenotype != null)
            {
                variantKey = "xenotype:" + genes.Xenotype.defName;
                variantLabel = genes.Xenotype.LabelCap;
            }
        }
        else if (pawn.RaceProps.lifeStageAges?.Count > 1 && pawn.ageTracker?.CurLifeStage != null)
        {
            var stage = pawn.ageTracker.CurLifeStage;
            variantKey = "stage:" + stage.defName;
            variantLabel = stage.LabelCap;
        }

        if (variantKey == null)
        {
            return false;
        }

        key = pawn.def.defName + "|" + variantKey;
        label = pawn.def.LabelCap + " · " + variantLabel;
        return true;
    }

    // Storage key and list label for a settings-stored scope, or false when the scope does not apply to this pawn.
    public static bool TryGetKey(AdjustmentScope scope, Pawn pawn, out string key, out string label)
    {
        key = null;
        label = null;
        if (pawn?.def == null)
        {
            return false;
        }

        switch (scope)
        {
            case AdjustmentScope.Category:
                var category = GetCategory(pawn);
                key = category.ToString();
                label = CategoryLabel(category);
                return true;
            case AdjustmentScope.Race:
                key = pawn.def.defName;
                label = pawn.def.LabelCap;
                return true;
            case AdjustmentScope.Variant:
                return TryGetVariant(pawn, out key, out label);
            default:
                return false;
        }
    }

    // Whether a scope can be edited for this pawn (only Variant can be missing).
    public static bool IsAvailable(AdjustmentScope scope, Pawn pawn)
    {
        return scope != AdjustmentScope.Variant || TryGetVariant(pawn, out _, out _);
    }

    // Short name of a scope for this pawn, such as "Race: Thrumbo".
    public static string ScopeLabel(AdjustmentScope scope, Pawn pawn)
    {
        switch (scope)
        {
            case AdjustmentScope.Global:
                return "PortraitOverlay.Edit.ScopeGlobal".Translate();
            case AdjustmentScope.Category:
                return "PortraitOverlay.Edit.ScopeCategory".Translate(CategoryLabel(GetCategory(pawn)));
            case AdjustmentScope.Race:
                return "PortraitOverlay.Edit.ScopeRace".Translate(pawn.def.LabelCap);
            case AdjustmentScope.Variant:
                return "PortraitOverlay.Edit.ScopeVariant".Translate(TryGetVariant(pawn, out _, out var variantLabel) ? variantLabel : "-");
            default:
                return "PortraitOverlay.Edit.ScopePawn".Translate();
        }
    }

    public static Dictionary<string, SavedAdjustment> SettingsStore(AdjustmentScope scope, PortraitOverlaySettings settings)
    {
        return scope switch
        {
            AdjustmentScope.Category => settings.CategoryAdjustments,
            AdjustmentScope.Race => settings.RaceAdjustments,
            AdjustmentScope.Variant => settings.VariantAdjustments,
            _ => null
        };
    }

    // Per-pawn entries of the current game, or null when no game is loaded.
    public static Dictionary<int, SavedAdjustment> PawnStore =>
        Current.Game?.GetComponent<SelectedPawnPortraitOverlayComponent>()?.PawnAdjustments;

    // The saved entry of a scope, or null when it holds no correction.
    public static SavedAdjustment Get(AdjustmentScope scope, Pawn pawn, PortraitOverlaySettings settings)
    {
        if (scope == AdjustmentScope.Global)
        {
            return settings.GlobalAdjustment != null && !settings.GlobalAdjustment.IsIdentity ? settings.GlobalAdjustment : null;
        }

        if (pawn == null)
        {
            return null;
        }

        if (scope == AdjustmentScope.Pawn)
        {
            var pawnStore = PawnStore;
            return pawnStore != null && pawnStore.TryGetValue(pawn.thingIDNumber, out var own) ? own : null;
        }

        var store = SettingsStore(scope, settings);
        return store != null && TryGetKey(scope, pawn, out var key, out _) && store.TryGetValue(key, out var entry) ? entry : null;
    }

    public static bool Has(AdjustmentScope scope, Pawn pawn, PortraitOverlaySettings settings)
    {
        return scope == AdjustmentScope.Global
            ? settings.UseGlobalPreset || Get(scope, pawn, settings) != null
            : Get(scope, pawn, settings) != null;
    }

    // All corrections combined from the broadest scope to the narrowest: zoom multiplies, offsets add. Each level
    // is a difference on top of the ones before it, so changing a broad scope later keeps narrower intent intact.
    public static PortraitAdjustment Resolve(Pawn pawn, PortraitOverlaySettings settings)
    {
        return ResolveExcluding(pawn, settings, null);
    }

    // The combined correction without one scope's own entry, which is what that scope's entry is applied on top of.
    public static PortraitAdjustment ResolveExcluding(Pawn pawn, PortraitOverlaySettings settings, AdjustmentScope? excluded)
    {
        var zoom = 1f;
        var offsetX = 0f;
        var offsetZ = 0f;
        foreach (var entry in Entries(pawn, settings, excluded))
        {
            zoom *= entry.Zoom;
            offsetX += entry.OffsetX;
            offsetZ += entry.OffsetZ;
        }

        return new PortraitAdjustment(zoom, offsetX, offsetZ);
    }

    // Combined panel size multiplier of every scope's entry (1, 1 when none changes the panel).
    public static Vector2 ResolvePanelScale(Pawn pawn, PortraitOverlaySettings settings, AdjustmentScope? excluded = null)
    {
        var scale = Vector2.one;
        foreach (var entry in Entries(pawn, settings, excluded))
        {
            scale.x *= entry.PanelWidthScale;
            scale.y *= entry.PanelHeightScale;
        }

        return scale;
    }

    private static IEnumerable<SavedAdjustment> Entries(Pawn pawn, PortraitOverlaySettings settings, AdjustmentScope? excluded)
    {
        var global = excluded == AdjustmentScope.Global ? null : Get(AdjustmentScope.Global, pawn, settings);
        if (global != null)
        {
            yield return global;
        }

        if (pawn == null)
        {
            yield break;
        }

        foreach (var scope in KeyedScopes)
        {
            var entry = scope == excluded ? null : Get(scope, pawn, settings);
            if (entry != null)
            {
                yield return entry;
            }
        }

        var own = excluded == AdjustmentScope.Pawn ? null : Get(AdjustmentScope.Pawn, pawn, settings);
        if (own != null)
        {
            yield return own;
        }
    }

    // Applies a zoom factor and offset delta to the given scope, creating or removing the stored entry as needed.
    public static void Apply(AdjustmentScope scope, Pawn pawn, PortraitOverlaySettings settings, float zoomFactor, float deltaX, float deltaZ)
    {
        if (scope == AdjustmentScope.Global)
        {
            var global = settings.GlobalAdjustment ?? new SavedAdjustment();
            global.Zoom *= zoomFactor;
            global.OffsetX += deltaX;
            global.OffsetZ += deltaZ;
            global.Clamp();
            settings.GlobalAdjustment = global;
            return;
        }

        if (!IsAvailable(scope, pawn))
        {
            return;
        }

        var entry = Get(scope, pawn, settings)?.Clone() ?? new SavedAdjustment();
        entry.Zoom *= zoomFactor;
        entry.OffsetX += deltaX;
        entry.OffsetZ += deltaZ;
        Put(scope, pawn, settings, entry);
    }

    // Sets a scope's entry outright (an identity value removes it). The entry's panel size is kept unless a new
    // one is given.
    public static void Set(AdjustmentScope scope, Pawn pawn, PortraitOverlaySettings settings, PortraitAdjustment value, Vector2? panelScale = null)
    {
        var existing = Get(scope, pawn, settings);
        var scale = panelScale ?? (existing != null ? new Vector2(existing.PanelWidthScale, existing.PanelHeightScale) : Vector2.one);
        if (scope == AdjustmentScope.Global)
        {
            settings.GlobalAdjustment = new SavedAdjustment
            {
                Zoom = value.Zoom,
                OffsetX = value.OffsetX,
                OffsetZ = value.OffsetZ,
                PanelWidthScale = scale.x,
                PanelHeightScale = scale.y
            };
            settings.GlobalAdjustment.Clamp();
            return;
        }

        if (pawn == null || !IsAvailable(scope, pawn))
        {
            return;
        }

        Put(scope, pawn, settings, new SavedAdjustment
        {
            Zoom = value.Zoom,
            OffsetX = value.OffsetX,
            OffsetZ = value.OffsetZ,
            PanelWidthScale = scale.x,
            PanelHeightScale = scale.y
        });
    }

    public static void Clear(AdjustmentScope scope, Pawn pawn, PortraitOverlaySettings settings)
    {
        if (scope == AdjustmentScope.Global)
        {
            settings.GlobalAdjustment = new SavedAdjustment();
            settings.UseGlobalPreset = false;
            return;
        }

        if (pawn == null)
        {
            return;
        }

        if (scope == AdjustmentScope.Pawn)
        {
            PawnStore?.Remove(pawn.thingIDNumber);
        }
        else if (TryGetKey(scope, pawn, out var key, out _))
        {
            SettingsStore(scope, settings)?.Remove(key);
        }
    }

    // Stores an entry under the pawn's key for the scope, or removes it when it no longer changes anything.
    private static void Put(AdjustmentScope scope, Pawn pawn, PortraitOverlaySettings settings, SavedAdjustment entry)
    {
        if (pawn == null)
        {
            return;
        }

        entry.Clamp();
        if (scope == AdjustmentScope.Pawn)
        {
            entry.Label = pawn.LabelShortCap;
            Store(PawnStore, pawn.thingIDNumber, entry);
        }
        else if (TryGetKey(scope, pawn, out var key, out var label))
        {
            entry.Label = label;
            Store(SettingsStore(scope, settings), key, entry);
        }
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

    // Frames the pawn to a preset. In the All scope the preset itself is stored, so every pawn is framed to it by
    // its own shape. In narrower scopes the needed correction and panel size are stored, with the panel size set
    // so the final panel is the preset's share of the base size, whatever other scopes hold.
    public static void ApplyPreset(PortraitPreset preset, AdjustmentScope scope, Pawn pawn, PortraitOverlaySettings settings)
    {
        var (widthScale, heightScale) = PortraitFraming.PresetPanelScale(preset);
        if (scope == AdjustmentScope.Global)
        {
            settings.UseGlobalPreset = true;
            settings.GlobalPreset = preset;
            settings.GlobalAdjustment = new SavedAdjustment { PanelWidthScale = widthScale, PanelHeightScale = heightScale };
            return;
        }

        if (pawn == null || !IsAvailable(scope, pawn))
        {
            return;
        }

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

    public static PortraitBounds FallbackBounds(Pawn pawn, bool humanlike)
    {
        var graphic = humanlike ? null : pawn.ageTracker?.CurKindLifeStage?.bodyGraphicData;
        return PortraitFraming.FallbackBounds(humanlike, graphic?.drawSize.x ?? 0f, graphic?.drawSize.y ?? 0f);
    }
}

// Everything an edit can change for one pawn, kept so Ctrl+Z can put it back.
public sealed class AdjustmentSnapshot
{
    private static readonly AdjustmentScope[] KeyedScopes = { AdjustmentScope.Category, AdjustmentScope.Race, AdjustmentScope.Variant };

    private int pawnId;
    private SavedAdjustment pawnAdjustment;
    private readonly List<KeyedEntry> keyedEntries = new();
    private float cameraZoom;
    private SavedAdjustment globalAdjustment;
    private bool useGlobalPreset;
    private PortraitPreset globalPreset;
    private float topCutoff;
    private float bottomCutoff;
    private float panelWidth;
    private float panelHeight;

    private sealed class KeyedEntry
    {
        public AdjustmentScope Scope;
        public string Key;
        public SavedAdjustment Entry;
    }

    public static AdjustmentSnapshot Capture(Pawn pawn, PortraitOverlaySettings settings)
    {
        var snapshot = new AdjustmentSnapshot
        {
            pawnId = pawn?.thingIDNumber ?? -1,
            pawnAdjustment = PortraitAdjustments.Get(AdjustmentScope.Pawn, pawn, settings)?.Clone(),
            cameraZoom = settings.CameraZoom,
            globalAdjustment = settings.GlobalAdjustment?.Clone(),
            useGlobalPreset = settings.UseGlobalPreset,
            globalPreset = settings.GlobalPreset,
            topCutoff = settings.TopCutoff,
            bottomCutoff = settings.BottomCutoff,
            panelWidth = settings.PanelWidth,
            panelHeight = settings.PanelHeight
        };

        foreach (var scope in KeyedScopes)
        {
            if (PortraitAdjustments.TryGetKey(scope, pawn, out var key, out _))
            {
                snapshot.keyedEntries.Add(new KeyedEntry
                {
                    Scope = scope,
                    Key = key,
                    Entry = PortraitAdjustments.Get(scope, pawn, settings)?.Clone()
                });
            }
        }

        return snapshot;
    }

    public void Restore(PortraitOverlaySettings settings)
    {
        var pawnStore = PortraitAdjustments.PawnStore;
        if (pawnStore != null && pawnId >= 0)
        {
            Put(pawnStore, pawnId, pawnAdjustment);
        }

        foreach (var keyed in keyedEntries)
        {
            var store = PortraitAdjustments.SettingsStore(keyed.Scope, settings);
            if (store != null)
            {
                Put(store, keyed.Key, keyed.Entry);
            }
        }

        settings.CameraZoom = cameraZoom;
        settings.GlobalAdjustment = globalAdjustment?.Clone() ?? new SavedAdjustment();
        settings.UseGlobalPreset = useGlobalPreset;
        settings.GlobalPreset = globalPreset;
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
            store[key] = value.Clone();
        }
    }
}
