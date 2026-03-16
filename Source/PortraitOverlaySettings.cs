using UnityEngine;
using Verse;

namespace SelectedPawnPortraitOverlay;

public sealed class PortraitOverlaySettings : ModSettings
{
    public bool Enabled = true;
    public bool ShowBackground = true;
    public bool ShowName = true;
    public bool KeepLastPortrait = false;
    public bool RenderHeadgear = true;
    public bool RenderApparel = true;
    public bool AllowDragging = true;
    public bool ShowAnimals = true;
    public bool ShowMechanoids = true;
    public bool ShowAnomalyEntities = true;

    public float PanelX = 24f;
    public float PanelY = 24f;
    public float PanelWidth = 240f;
    public float PanelHeight = 360f;
    public float BackgroundAlpha = 0.45f;
    public float CameraZoom = 1.02f;
    public float FaceEmphasis = 0.2f;

    public Vector2 PanelSize => new(PanelWidth, PanelHeight);

    public override void ExposeData()
    {
        Scribe_Values.Look(ref Enabled, nameof(Enabled), true);
        Scribe_Values.Look(ref ShowBackground, nameof(ShowBackground), true);
        Scribe_Values.Look(ref ShowName, nameof(ShowName), true);
        Scribe_Values.Look(ref KeepLastPortrait, nameof(KeepLastPortrait), false);
        Scribe_Values.Look(ref RenderHeadgear, nameof(RenderHeadgear), true);
        Scribe_Values.Look(ref RenderApparel, nameof(RenderApparel), true);
        Scribe_Values.Look(ref AllowDragging, nameof(AllowDragging), true);
        Scribe_Values.Look(ref ShowAnimals, nameof(ShowAnimals), true);
        Scribe_Values.Look(ref ShowMechanoids, nameof(ShowMechanoids), true);
        Scribe_Values.Look(ref ShowAnomalyEntities, nameof(ShowAnomalyEntities), true);
        Scribe_Values.Look(ref PanelX, nameof(PanelX), 24f);
        Scribe_Values.Look(ref PanelY, nameof(PanelY), 24f);
        Scribe_Values.Look(ref PanelWidth, nameof(PanelWidth), 240f);
        Scribe_Values.Look(ref PanelHeight, nameof(PanelHeight), 360f);
        Scribe_Values.Look(ref BackgroundAlpha, nameof(BackgroundAlpha), 0.45f);
        Scribe_Values.Look(ref CameraZoom, nameof(CameraZoom), 1.02f);
        Scribe_Values.Look(ref FaceEmphasis, nameof(FaceEmphasis), 0.2f);
        ClampValues();
    }

    public void ClampValues()
    {
        PanelX = Mathf.Clamp(PanelX, 0f, 5000f);
        PanelY = Mathf.Clamp(PanelY, 0f, 5000f);
        PanelWidth = Mathf.Clamp(PanelWidth, 180f, 420f);
        PanelHeight = Mathf.Clamp(PanelHeight, 240f, 520f);
        BackgroundAlpha = Mathf.Clamp01(BackgroundAlpha);
        CameraZoom = Mathf.Clamp(CameraZoom, 0.75f, 1.45f);
        FaceEmphasis = Mathf.Clamp01(FaceEmphasis);
    }

    public void ResetPosition()
    {
        PanelX = 24f;
        PanelY = 24f;
    }
}
