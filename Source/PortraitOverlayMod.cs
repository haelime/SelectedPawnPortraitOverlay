using UnityEngine;
using Verse;

namespace SelectedPawnPortraitOverlay;

public sealed class PortraitOverlayMod : Mod
{
    private const float SettingsContentHeight = 760f;

    private static PortraitOverlayMod instance;
    private Vector2 settingsScrollPosition;

    public static PortraitOverlaySettings Settings { get; private set; }

    public PortraitOverlayMod(ModContentPack content) : base(content)
    {
        instance = this;
        Settings = GetSettings<PortraitOverlaySettings>();
        Settings.ClampValues();
        ModCompatibility.Initialize();
    }

    public override string SettingsCategory()
    {
        return "PortraitOverlay.Settings.Category".Translate();
    }

    public override void DoSettingsWindowContents(Rect inRect)
    {
        var settings = Settings;
        settings.ClampValues();

        var viewRect = new Rect(0f, 0f, inRect.width - 16f, SettingsContentHeight);
        Widgets.BeginScrollView(inRect, ref settingsScrollPosition, viewRect);

        var listing = new Listing_Standard();
        listing.Begin(viewRect);

        listing.CheckboxLabeled("PortraitOverlay.Settings.Enabled".Translate(), ref settings.Enabled);
        listing.CheckboxLabeled("PortraitOverlay.Settings.ShowBackground".Translate(), ref settings.ShowBackground);
        listing.CheckboxLabeled("PortraitOverlay.Settings.ShowName".Translate(), ref settings.ShowName);
        listing.CheckboxLabeled("PortraitOverlay.Settings.KeepLastPortrait".Translate(), ref settings.KeepLastPortrait);
        listing.CheckboxLabeled("PortraitOverlay.Settings.RenderHeadgear".Translate(), ref settings.RenderHeadgear);
        listing.CheckboxLabeled("PortraitOverlay.Settings.RenderApparel".Translate(), ref settings.RenderApparel);
        listing.CheckboxLabeled("PortraitOverlay.Settings.AllowDragging".Translate(), ref settings.AllowDragging);
        listing.CheckboxLabeled("PortraitOverlay.Settings.ShowAnimals".Translate(), ref settings.ShowAnimals);
        listing.CheckboxLabeled("PortraitOverlay.Settings.ShowMechanoids".Translate(), ref settings.ShowMechanoids);
        listing.CheckboxLabeled("PortraitOverlay.Settings.ShowAnomalyEntities".Translate(), ref settings.ShowAnomalyEntities);
        listing.GapLine();

        listing.Label("PortraitOverlay.Settings.PanelWidth".Translate(settings.PanelWidth.ToString("F0")));
        settings.PanelWidth = listing.Slider(settings.PanelWidth, 180f, 420f);

        listing.Label("PortraitOverlay.Settings.PanelHeight".Translate(settings.PanelHeight.ToString("F0")));
        settings.PanelHeight = listing.Slider(settings.PanelHeight, 240f, 520f);

        listing.Label("PortraitOverlay.Settings.PositionX".Translate(settings.PanelX.ToString("F0")));
        settings.PanelX = listing.Slider(settings.PanelX, 0f, 1200f);

        listing.Label("PortraitOverlay.Settings.PositionY".Translate(settings.PanelY.ToString("F0")));
        settings.PanelY = listing.Slider(settings.PanelY, 0f, 700f);

        listing.Label("PortraitOverlay.Settings.BackgroundAlpha".Translate(FormatPercent(settings.BackgroundAlpha)));
        settings.BackgroundAlpha = listing.Slider(settings.BackgroundAlpha, 0f, 0.95f);

        listing.Label("PortraitOverlay.Settings.Zoom".Translate(settings.CameraZoom.ToString("F2")));
        settings.CameraZoom = listing.Slider(settings.CameraZoom, 0.75f, 1.45f);

        listing.Label("PortraitOverlay.Settings.FaceEmphasis".Translate(FormatPercent(settings.FaceEmphasis)));
        settings.FaceEmphasis = listing.Slider(settings.FaceEmphasis, 0f, 1f);

        if (listing.ButtonText("PortraitOverlay.Settings.ResetPosition".Translate()))
        {
            settings.ResetPosition();
            SaveSettings();
        }

        listing.GapLine();
        listing.Label("PortraitOverlay.Settings.FacialAnimationStatus".Translate(ModCompatibility.FacialAnimationStatusLabel.Translate()));

        listing.End();
        Widgets.EndScrollView();
        settings.ClampValues();
    }

    private static string FormatPercent(float value)
    {
        return Mathf.RoundToInt(value * 100f) + "%";
    }

    public static void SaveSettings()
    {
        if (Settings == null)
        {
            return;
        }

        Settings.ClampValues();
        instance?.WriteSettings();
    }
}
