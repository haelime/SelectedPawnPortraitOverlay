using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SelectedPawnPortraitOverlay;

public sealed class PortraitOverlaySettings : ModSettings
{
    public const float MinPanelX = -64f;
    // Numeric input limits; the settings sliders only cover the recommended range.
    public const float MinCameraZoom = PortraitFraming.MinZoom;
    public const float MaxCameraZoom = 5f;
    public const float MaxFaceEmphasis = 3f;
    public const float MaxCutoff = 0.9f;
    public const float MaxCombinedVerticalCutoff = 0.9f;
    public const float MinRaceZoom = 0.1f;
    public const float MaxRaceZoom = 5f;

    // Bump when a setting needs migrating. Files written before versioning load as version 0.
    private const int CurrentSettingsVersion = 1;

    private const bool DefaultEnabled = true;
    private const bool DefaultShowBackground = true;
    private const bool DefaultShowName = true;
    private const bool DefaultKeepLastPortrait = false;
    private const bool DefaultRenderHeadgear = true;
    private const bool DefaultRenderApparel = true;
    private const bool DefaultLivePortrait = false;
    private const bool DefaultOnlyPlayerControlledPawns = false;
    private const bool DefaultAllowDragging = true;
    private const bool DefaultShowAnimals = true;
    private const bool DefaultShowMechanoids = true;
    private const bool DefaultShowAnomalyEntities = true;
    private const bool DefaultShowPrisoners = true;
    private const bool DefaultShowSlaves = true;
    private const float DefaultPanelX = 24f;
    private const float DefaultPanelY = 24f;
    private const float DefaultPanelWidth = 240f;
    private const float DefaultPanelHeight = 360f;
    private const float DefaultBackgroundAlpha = 0.45f;
    private const float DefaultCameraZoom = 1.02f;
    private const float DefaultFaceEmphasis = 0.2f;
    private const float DefaultTopCutoff = 0f;
    private const float DefaultBottomCutoff = 0f;
    private const bool DefaultAutoFitPortrait = true;

    public bool Enabled = DefaultEnabled;
    public bool ShowBackground = DefaultShowBackground;
    public bool ShowName = DefaultShowName;
    public bool KeepLastPortrait = DefaultKeepLastPortrait;
    public bool RenderHeadgear = DefaultRenderHeadgear;
    public bool RenderApparel = DefaultRenderApparel;
    public bool LivePortrait = DefaultLivePortrait;
    public bool OnlyPlayerControlledPawns = DefaultOnlyPlayerControlledPawns;
    public bool AllowDragging = DefaultAllowDragging;
    public bool ShowAnimals = DefaultShowAnimals;
    public bool ShowMechanoids = DefaultShowMechanoids;
    public bool ShowAnomalyEntities = DefaultShowAnomalyEntities;
    public bool ShowPrisoners = DefaultShowPrisoners;
    public bool ShowSlaves = DefaultShowSlaves;

    public float PanelX = DefaultPanelX;
    public float PanelY = DefaultPanelY;
    public float PanelWidth = DefaultPanelWidth;
    public float PanelHeight = DefaultPanelHeight;
    public float BackgroundAlpha = DefaultBackgroundAlpha;
    public float CameraZoom = DefaultCameraZoom;
    public float FaceEmphasis = DefaultFaceEmphasis;
    public float TopCutoff = DefaultTopCutoff;
    public float BottomCutoff = DefaultBottomCutoff;
    public bool AutoFitPortrait = DefaultAutoFitPortrait;

    // Race defName -> zoom scale relative to human framing. Keyed by name so races from removed mods are kept harmlessly.
    public Dictionary<string, float> RaceZoomOverrides = new();

    private int settingsVersion = CurrentSettingsVersion;

    public Vector2 PanelSize => new(PanelWidth, PanelHeight);

    public override void ExposeData()
    {
        Scribe_Values.Look(ref Enabled, nameof(Enabled), DefaultEnabled);
        Scribe_Values.Look(ref ShowBackground, nameof(ShowBackground), DefaultShowBackground);
        Scribe_Values.Look(ref ShowName, nameof(ShowName), DefaultShowName);
        Scribe_Values.Look(ref KeepLastPortrait, nameof(KeepLastPortrait), DefaultKeepLastPortrait);
        Scribe_Values.Look(ref RenderHeadgear, nameof(RenderHeadgear), DefaultRenderHeadgear);
        Scribe_Values.Look(ref RenderApparel, nameof(RenderApparel), DefaultRenderApparel);
        Scribe_Values.Look(ref LivePortrait, nameof(LivePortrait), DefaultLivePortrait);
        Scribe_Values.Look(ref OnlyPlayerControlledPawns, nameof(OnlyPlayerControlledPawns), DefaultOnlyPlayerControlledPawns);
        Scribe_Values.Look(ref AllowDragging, nameof(AllowDragging), DefaultAllowDragging);
        Scribe_Values.Look(ref ShowAnimals, nameof(ShowAnimals), DefaultShowAnimals);
        Scribe_Values.Look(ref ShowMechanoids, nameof(ShowMechanoids), DefaultShowMechanoids);
        Scribe_Values.Look(ref ShowAnomalyEntities, nameof(ShowAnomalyEntities), DefaultShowAnomalyEntities);
        Scribe_Values.Look(ref ShowPrisoners, nameof(ShowPrisoners), DefaultShowPrisoners);
        Scribe_Values.Look(ref ShowSlaves, nameof(ShowSlaves), DefaultShowSlaves);
        Scribe_Values.Look(ref PanelX, nameof(PanelX), DefaultPanelX);
        Scribe_Values.Look(ref PanelY, nameof(PanelY), DefaultPanelY);
        Scribe_Values.Look(ref PanelWidth, nameof(PanelWidth), DefaultPanelWidth);
        Scribe_Values.Look(ref PanelHeight, nameof(PanelHeight), DefaultPanelHeight);
        Scribe_Values.Look(ref BackgroundAlpha, nameof(BackgroundAlpha), DefaultBackgroundAlpha);
        Scribe_Values.Look(ref CameraZoom, nameof(CameraZoom), DefaultCameraZoom);
        Scribe_Values.Look(ref FaceEmphasis, nameof(FaceEmphasis), DefaultFaceEmphasis);
        Scribe_Values.Look(ref TopCutoff, nameof(TopCutoff), DefaultTopCutoff);
        Scribe_Values.Look(ref BottomCutoff, nameof(BottomCutoff), DefaultBottomCutoff);
        Scribe_Values.Look(ref AutoFitPortrait, nameof(AutoFitPortrait), DefaultAutoFitPortrait);
        Scribe_Collections.Look(ref RaceZoomOverrides, nameof(RaceZoomOverrides), LookMode.Value, LookMode.Value);
        Scribe_Values.Look(ref settingsVersion, "SettingsVersion", 0);

        if (Scribe.mode == LoadSaveMode.LoadingVars)
        {
            MigrateLoadedSettings();
        }

        RaceZoomOverrides ??= new Dictionary<string, float>();
        ClampRaceZoomOverrides();
        ClampValues();
    }

    // Users who configured the mod before auto-fit existed keep the framing they tuned; new installs get it on.
    private void MigrateLoadedSettings()
    {
        if (settingsVersion < 1)
        {
            AutoFitPortrait = false;
        }

        settingsVersion = CurrentSettingsVersion;
    }

    private void ClampRaceZoomOverrides()
    {
        foreach (var defName in RaceZoomOverrides.Keys.ToList())
        {
            RaceZoomOverrides[defName] = Mathf.Clamp(EnsureFinite(RaceZoomOverrides[defName], 1f), MinRaceZoom, MaxRaceZoom);
        }
    }

    public void ClampValues()
    {
        ApplyFilterDependencies();
        PanelX = EnsureFinite(PanelX, DefaultPanelX);
        PanelY = EnsureFinite(PanelY, DefaultPanelY);
        PanelWidth = Mathf.Max(1f, EnsureFinite(PanelWidth, DefaultPanelWidth));
        PanelHeight = Mathf.Max(1f, EnsureFinite(PanelHeight, DefaultPanelHeight));
        BackgroundAlpha = Mathf.Clamp01(BackgroundAlpha);
        CameraZoom = Mathf.Clamp(EnsureFinite(CameraZoom, DefaultCameraZoom), MinCameraZoom, MaxCameraZoom);
        FaceEmphasis = Mathf.Clamp(EnsureFinite(FaceEmphasis, DefaultFaceEmphasis), 0f, MaxFaceEmphasis);
        TopCutoff = Mathf.Clamp(EnsureFinite(TopCutoff, DefaultTopCutoff), 0f, MaxCutoff);
        BottomCutoff = Mathf.Clamp(EnsureFinite(BottomCutoff, DefaultBottomCutoff), 0f, MaxCutoff);

        var totalVerticalCutoff = TopCutoff + BottomCutoff;
        if (totalVerticalCutoff > MaxCombinedVerticalCutoff)
        {
            var scale = MaxCombinedVerticalCutoff / totalVerticalCutoff;
            TopCutoff *= scale;
            BottomCutoff *= scale;
        }
    }

    public void ResetWindowSettings()
    {
        PanelX = DefaultPanelX;
        PanelY = DefaultPanelY;
        PanelWidth = DefaultPanelWidth;
        PanelHeight = DefaultPanelHeight;
        ClampValues();
    }

    public void ResetAll()
    {
        Enabled = DefaultEnabled;
        ShowBackground = DefaultShowBackground;
        ShowName = DefaultShowName;
        KeepLastPortrait = DefaultKeepLastPortrait;
        RenderHeadgear = DefaultRenderHeadgear;
        RenderApparel = DefaultRenderApparel;
        LivePortrait = DefaultLivePortrait;
        OnlyPlayerControlledPawns = DefaultOnlyPlayerControlledPawns;
        AllowDragging = DefaultAllowDragging;
        ShowAnimals = DefaultShowAnimals;
        ShowMechanoids = DefaultShowMechanoids;
        ShowAnomalyEntities = DefaultShowAnomalyEntities;
        ShowPrisoners = DefaultShowPrisoners;
        ShowSlaves = DefaultShowSlaves;
        PanelX = DefaultPanelX;
        PanelY = DefaultPanelY;
        PanelWidth = DefaultPanelWidth;
        PanelHeight = DefaultPanelHeight;
        BackgroundAlpha = DefaultBackgroundAlpha;
        CameraZoom = DefaultCameraZoom;
        FaceEmphasis = DefaultFaceEmphasis;
        TopCutoff = DefaultTopCutoff;
        BottomCutoff = DefaultBottomCutoff;
        AutoFitPortrait = DefaultAutoFitPortrait;
        RaceZoomOverrides.Clear();
        ClampValues();
    }

    public void ApplyFilterDependencies()
    {
        if (!OnlyPlayerControlledPawns)
        {
            return;
        }

        ShowAnomalyEntities = false;
        ShowPrisoners = false;
        ShowSlaves = false;
    }

    private static float EnsureFinite(float value, float fallback)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value) ? value : fallback;
    }
}
