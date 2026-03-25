using UnityEngine;
using Verse;

namespace SelectedPawnPortraitOverlay;

public sealed class PortraitOverlaySettings : ModSettings
{
    public const float MinPanelX = -64f;

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
    private const float MaximumCutoff = 0.35f;
    private const float MaximumCombinedVerticalCutoff = 0.8f;

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
        ClampValues();
    }

    public void ClampValues()
    {
        ApplyFilterDependencies();
        PanelX = EnsureFinite(PanelX, DefaultPanelX);
        PanelY = EnsureFinite(PanelY, DefaultPanelY);
        PanelWidth = Mathf.Max(1f, EnsureFinite(PanelWidth, DefaultPanelWidth));
        PanelHeight = Mathf.Max(1f, EnsureFinite(PanelHeight, DefaultPanelHeight));
        BackgroundAlpha = Mathf.Clamp01(BackgroundAlpha);
        CameraZoom = Mathf.Clamp(CameraZoom, 0.75f, 1.45f);
        FaceEmphasis = Mathf.Clamp01(FaceEmphasis);
        TopCutoff = Mathf.Clamp(TopCutoff, 0f, MaximumCutoff);
        BottomCutoff = Mathf.Clamp(BottomCutoff, 0f, MaximumCutoff);

        var totalVerticalCutoff = TopCutoff + BottomCutoff;
        if (totalVerticalCutoff > MaximumCombinedVerticalCutoff)
        {
            var scale = MaximumCombinedVerticalCutoff / totalVerticalCutoff;
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
