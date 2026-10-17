using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using Verse;

namespace SelectedPawnPortraitOverlay;

public sealed class PortraitOverlayMod : Mod
{
    private const float SettingsTitleReservedHeight = 40f;
    private const float TabHeight = 32f;
    private const float HeaderSpacing = 8f;
    private const float SettingsScrollBarWidth = 16f;
    private const float SettingsContentPadding = 12f;
    private const float TabMaxWidth = 220f;
    private const float NumericFieldWidth = 88f;
    private const float SliderFieldGap = 12f;
    private const float SettingLabelHeight = 24f;
    private const float SettingControlHeight = 28f;
    private const float InfoLabelHeight = 34f;
    private const float RemoveButtonWidth = 90f;

    private enum SettingsTab
    {
        General,
        Portrait
    }

    private static PortraitOverlayMod instance;
    private SettingsTab selectedSettingsTab = SettingsTab.General;
    private Vector2 generalScrollPosition;
    private Vector2 portraitScrollPosition;
    private string backgroundAlphaBuffer;
    private string cameraZoomBuffer;
    private string faceEmphasisBuffer;
    private string topCutoffBuffer;
    private string bottomCutoffBuffer;
    private string panelWidthBuffer;
    private string panelHeightBuffer;
    private string panelXBuffer;
    private string panelYBuffer;
    private readonly Dictionary<string, string> raceZoomBuffers = new();

    public static PortraitOverlaySettings Settings { get; private set; }

    public PortraitOverlayMod(ModContentPack content) : base(content)
    {
        instance = this;
        Settings = GetSettings<PortraitOverlaySettings>();
        Settings.ClampValues();
    }

    public override string SettingsCategory()
    {
        return "PortraitOverlay.Settings.Category".Translate();
    }

    public override void DoSettingsWindowContents(Rect inRect)
    {
        var settings = Settings;
        settings.ClampValues();

        var tabRect = new Rect(inRect.x, inRect.y + SettingsTitleReservedHeight, inRect.width, TabHeight);
        DrawTabs(tabRect);

        var contentRect = new Rect(
            inRect.x,
            tabRect.yMax + HeaderSpacing,
            inRect.width,
            inRect.height - SettingsTitleReservedHeight - TabHeight - HeaderSpacing);

        switch (selectedSettingsTab)
        {
            case SettingsTab.General:
                DrawScrollableListing(contentRect, ref generalScrollPosition, GetGeneralTabHeight(), DrawGeneralTab);
                break;
            case SettingsTab.Portrait:
                DrawScrollableListing(contentRect, ref portraitScrollPosition, GetPortraitTabHeight(settings), DrawPortraitTab);
                break;
        }

        settings.ClampValues();
    }

    private void DrawTabs(Rect rect)
    {
        var tabs = new List<TabRecord>
        {
            new("PortraitOverlay.Settings.TabGeneral".Translate(), () => selectedSettingsTab = SettingsTab.General, selectedSettingsTab == SettingsTab.General),
            new("PortraitOverlay.Settings.TabPortrait".Translate(), () => selectedSettingsTab = SettingsTab.Portrait, selectedSettingsTab == SettingsTab.Portrait)
        };

        TabDrawer.DrawTabs(rect, tabs, TabMaxWidth);
    }

    private void DrawScrollableListing(
        Rect inRect,
        ref Vector2 scrollPosition,
        float contentHeight,
        Action<Listing_Standard, PortraitOverlaySettings> drawContents)
    {
        var viewRect = new Rect(0f, 0f, inRect.width - SettingsScrollBarWidth, contentHeight);
        var listingRect = new Rect(0f, 0f, viewRect.width - SettingsContentPadding, contentHeight);
        Widgets.BeginScrollView(inRect, ref scrollPosition, viewRect);

        var listing = new Listing_Standard();
        listing.Begin(listingRect);
        drawContents(listing, Settings);
        listing.End();

        Widgets.EndScrollView();
    }

    private void DrawGeneralTab(Listing_Standard listing, PortraitOverlaySettings settings)
    {
        listing.CheckboxLabeled("PortraitOverlay.Settings.Enabled".Translate(), ref settings.Enabled);
        listing.CheckboxLabeled("PortraitOverlay.Settings.KeepLastPortrait".Translate(), ref settings.KeepLastPortrait);

        var previousOnlyPlayerControlledPawns = settings.OnlyPlayerControlledPawns;
        listing.CheckboxLabeled("PortraitOverlay.Settings.OnlyPlayerControlledPawns".Translate(), ref settings.OnlyPlayerControlledPawns);
        if (settings.OnlyPlayerControlledPawns != previousOnlyPlayerControlledPawns)
        {
            settings.ApplyFilterDependencies();
        }

        listing.CheckboxLabeled("PortraitOverlay.Settings.AllowDragging".Translate(), ref settings.AllowDragging);
        listing.GapLine();
        listing.CheckboxLabeled("PortraitOverlay.Settings.ShowAnimals".Translate(), ref settings.ShowAnimals);
        listing.CheckboxLabeled("PortraitOverlay.Settings.ShowMechanoids".Translate(), ref settings.ShowMechanoids);
        DrawCheckbox(listing, "PortraitOverlay.Settings.ShowAnomalyEntities", ref settings.ShowAnomalyEntities, !settings.OnlyPlayerControlledPawns);
        DrawCheckbox(listing, "PortraitOverlay.Settings.ShowPrisoners", ref settings.ShowPrisoners, !settings.OnlyPlayerControlledPawns);
        DrawCheckbox(listing, "PortraitOverlay.Settings.ShowSlaves", ref settings.ShowSlaves, !settings.OnlyPlayerControlledPawns);
    }

    private void DrawPortraitTab(Listing_Standard listing, PortraitOverlaySettings settings)
    {
        listing.CheckboxLabeled("PortraitOverlay.Settings.ShowBackground".Translate(), ref settings.ShowBackground);
        listing.CheckboxLabeled("PortraitOverlay.Settings.ShowName".Translate(), ref settings.ShowName);
        listing.CheckboxLabeled("PortraitOverlay.Settings.RenderHeadgear".Translate(), ref settings.RenderHeadgear);
        listing.CheckboxLabeled("PortraitOverlay.Settings.RenderApparel".Translate(), ref settings.RenderApparel);
        listing.CheckboxLabeled("PortraitOverlay.Settings.LivePortrait".Translate(), ref settings.LivePortrait);

        listing.GapLine();
        DrawInfoLabel(listing, "PortraitOverlay.Settings.PanelNumericInputHint".Translate());
        DrawSliderWithNumericField(
            listing,
            "PortraitOverlay.Settings.BackgroundAlpha".Translate(FormatPercent(settings.BackgroundAlpha)),
            ref settings.BackgroundAlpha,
            ref backgroundAlphaBuffer,
            0f,
            0.95f,
            "F2");
        DrawSliderWithNumericField(
            listing,
            "PortraitOverlay.Settings.Zoom".Translate(settings.CameraZoom.ToString("F2")),
            ref settings.CameraZoom,
            ref cameraZoomBuffer,
            0.75f,
            1.45f,
            "F2",
            numericMin: PortraitOverlaySettings.MinCameraZoom,
            numericMax: PortraitOverlaySettings.MaxCameraZoom);
        DrawSliderWithNumericField(
            listing,
            "PortraitOverlay.Settings.FaceEmphasis".Translate(FormatPercent(settings.FaceEmphasis)),
            ref settings.FaceEmphasis,
            ref faceEmphasisBuffer,
            0f,
            1f,
            "F2",
            numericMax: PortraitOverlaySettings.MaxFaceEmphasis);
        DrawSliderWithNumericField(
            listing,
            "PortraitOverlay.Settings.TopCutoff".Translate(FormatPercent(settings.TopCutoff)),
            ref settings.TopCutoff,
            ref topCutoffBuffer,
            0f,
            0.35f,
            "F2",
            numericMax: GetCutoffNumericMax(settings.BottomCutoff));
        DrawSliderWithNumericField(
            listing,
            "PortraitOverlay.Settings.BottomCutoff".Translate(FormatPercent(settings.BottomCutoff)),
            ref settings.BottomCutoff,
            ref bottomCutoffBuffer,
            0f,
            0.35f,
            "F2",
            numericMax: GetCutoffNumericMax(settings.TopCutoff));

        listing.GapLine();
        DrawRaceZoomSection(listing, settings);

        listing.GapLine();
        DrawSliderWithNumericField(
            listing,
            "PortraitOverlay.Settings.PanelWidth".Translate(settings.PanelWidth.ToString("F0")),
            ref settings.PanelWidth,
            ref panelWidthBuffer,
            180f,
            420f,
            "F0",
            numericMin: 1f,
            numericMax: 1000000f);
        DrawSliderWithNumericField(
            listing,
            "PortraitOverlay.Settings.PanelHeight".Translate(settings.PanelHeight.ToString("F0")),
            ref settings.PanelHeight,
            ref panelHeightBuffer,
            240f,
            520f,
            "F0",
            numericMin: 1f,
            numericMax: 1000000f);
        DrawSliderWithNumericField(
            listing,
            "PortraitOverlay.Settings.PositionX".Translate(settings.PanelX.ToString("F0")),
            ref settings.PanelX,
            ref panelXBuffer,
            PortraitOverlaySettings.MinPanelX,
            1200f,
            "F0",
            numericMin: -1000000f,
            numericMax: 1000000f);
        DrawSliderWithNumericField(
            listing,
            "PortraitOverlay.Settings.PositionY".Translate(settings.PanelY.ToString("F0")),
            ref settings.PanelY,
            ref panelYBuffer,
            0f,
            700f,
            "F0",
            numericMin: -1000000f,
            numericMax: 1000000f);

        listing.GapLine();
        if (listing.ButtonText("PortraitOverlay.Settings.ResetWindowSettings".Translate()))
        {
            settings.ResetWindowSettings();
            SyncNumericBuffers(settings);
            SaveSettings();
        }

        if (listing.ButtonText("PortraitOverlay.Settings.ResetAllSettings".Translate()))
        {
            settings.ResetAll();
            SyncNumericBuffers(settings);
            SaveSettings();
        }
    }

    private void DrawRaceZoomSection(Listing_Standard listing, PortraitOverlaySettings settings)
    {
        listing.CheckboxLabeled(
            "PortraitOverlay.Settings.AutoFitPortrait".Translate(),
            ref settings.AutoFitPortrait,
            "PortraitOverlay.Settings.AutoFitPortraitTooltip".Translate());
        DrawInfoLabel(listing, "PortraitOverlay.Settings.RaceZoomHint".Translate());

        string removedDefName = null;
        foreach (var defName in settings.RaceZoomOverrides.Keys.OrderBy(GetRaceLabel).ToList())
        {
            var value = settings.RaceZoomOverrides[defName];
            raceZoomBuffers.TryGetValue(defName, out var buffer);

            var labelRect = listing.GetRect(SettingLabelHeight);
            Widgets.Label(
                labelRect.LeftPartPixels(labelRect.width - RemoveButtonWidth - SliderFieldGap),
                "PortraitOverlay.Settings.RaceZoom".Translate(GetRaceLabel(defName), value.ToString("F2")));
            if (Widgets.ButtonText(labelRect.RightPartPixels(RemoveButtonWidth), "PortraitOverlay.Settings.RemoveRace".Translate()))
            {
                removedDefName = defName;
            }

            DrawSliderRow(
                listing,
                ref value,
                ref buffer,
                0.25f,
                3f,
                "F2",
                PortraitOverlaySettings.MinRaceZoom,
                PortraitOverlaySettings.MaxRaceZoom);
            settings.RaceZoomOverrides[defName] = value;
            raceZoomBuffers[defName] = buffer;
        }

        if (removedDefName != null)
        {
            settings.RaceZoomOverrides.Remove(removedDefName);
            raceZoomBuffers.Remove(removedDefName);
        }

        var selectedPawn = Current.ProgramState == ProgramState.Playing
            ? Find.Selector?.SingleSelectedThing as Pawn
            : null;
        var canAddSelected = selectedPawn?.def != null && !settings.RaceZoomOverrides.ContainsKey(selectedPawn.def.defName);
        var previousColor = GUI.color;
        if (!canAddSelected)
        {
            GUI.color = Color.gray;
        }

        if (listing.ButtonText("PortraitOverlay.Settings.AddSelectedRace".Translate()) && canAddSelected)
        {
            // Start from the scale the pawn renders with now, so adding an entry does not change the portrait.
            settings.RaceZoomOverrides[selectedPawn.def.defName] = PortraitRaceScale
                .For(selectedPawn, SelectedPawnPortraitOverlayComponent.GetPortraitAspect(settings), settings)
                .Scale;
        }

        GUI.color = previousColor;
        if (listing.ButtonText("PortraitOverlay.Settings.AddRace".Translate()))
        {
            Find.WindowStack.Add(new FloatMenu(BuildAddRaceOptions(settings)));
        }
    }

    private static List<FloatMenuOption> BuildAddRaceOptions(PortraitOverlaySettings settings)
    {
        return DefDatabase<ThingDef>.AllDefsListForReading
            .Where(def => def.race != null && !settings.RaceZoomOverrides.ContainsKey(def.defName))
            .OrderBy(def => def.label)
            .Select(def => new FloatMenuOption(
                $"{def.LabelCap} ({def.defName})",
                () => settings.RaceZoomOverrides[def.defName] = PortraitRaceScale.DefaultFor(
                    def,
                    SelectedPawnPortraitOverlayComponent.GetPortraitAspect(settings),
                    settings)))
            .ToList();
    }

    private static string GetRaceLabel(string defName)
    {
        var def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
        return def != null
            ? def.LabelCap.ToString()
            : "PortraitOverlay.Settings.RaceNotLoaded".Translate(defName).ToString();
    }

    // Cap each cutoff by what the other leaves, so a typed value is never rescaled out from under its field.
    private static float GetCutoffNumericMax(float otherCutoff)
    {
        return Mathf.Clamp(
            PortraitOverlaySettings.MaxCombinedVerticalCutoff - otherCutoff,
            0f,
            PortraitOverlaySettings.MaxCutoff);
    }

    private static string FormatPercent(float value)
    {
        return Mathf.RoundToInt(value * 100f) + "%";
    }

    private static void DrawInfoLabel(Listing_Standard listing, string text)
    {
        var rect = listing.GetRect(InfoLabelHeight);
        var previousColor = GUI.color;
        var previousFont = Text.Font;
        GUI.color = new Color(0.72f, 0.72f, 0.72f);
        Text.Font = GameFont.Tiny;
        Widgets.Label(rect, text);
        Text.Font = previousFont;
        GUI.color = previousColor;
        listing.Gap(listing.verticalSpacing);
    }

    private void DrawSliderWithNumericField(
        Listing_Standard listing,
        string label,
        ref float value,
        ref string buffer,
        float sliderMin,
        float sliderMax,
        string format,
        float? numericMin = null,
        float? numericMax = null)
    {
        listing.Label(label);
        DrawSliderRow(listing, ref value, ref buffer, sliderMin, sliderMax, format, numericMin, numericMax);
    }

    private static void DrawSliderRow(
        Listing_Standard listing,
        ref float value,
        ref string buffer,
        float sliderMin,
        float sliderMax,
        string format,
        float? numericMin = null,
        float? numericMax = null)
    {
        var rowRect = listing.GetRect(SettingControlHeight);
        var sliderRect = new Rect(
            rowRect.x,
            rowRect.y,
            rowRect.width - NumericFieldWidth - SliderFieldGap,
            rowRect.height);
        var fieldRect = new Rect(
            sliderRect.xMax + SliderFieldGap,
            rowRect.y,
            NumericFieldWidth,
            rowRect.height);

        buffer ??= FormatFloat(value, format);

        var previousValue = value;
        value = Widgets.HorizontalSlider(sliderRect, value, sliderMin, sliderMax, true);
        if (!Mathf.Approximately(previousValue, value))
        {
            buffer = FormatFloat(value, format);
        }

        Widgets.TextFieldNumeric(
            fieldRect,
            ref value,
            ref buffer,
            numericMin ?? sliderMin,
            numericMax ?? sliderMax);

        if (string.IsNullOrWhiteSpace(buffer))
        {
            buffer = FormatFloat(value, format);
        }

        listing.Gap(listing.verticalSpacing);
    }

    private void SyncNumericBuffers(PortraitOverlaySettings settings)
    {
        backgroundAlphaBuffer = FormatFloat(settings.BackgroundAlpha, "F2");
        cameraZoomBuffer = FormatFloat(settings.CameraZoom, "F2");
        faceEmphasisBuffer = FormatFloat(settings.FaceEmphasis, "F2");
        topCutoffBuffer = FormatFloat(settings.TopCutoff, "F2");
        bottomCutoffBuffer = FormatFloat(settings.BottomCutoff, "F2");
        panelWidthBuffer = FormatFloat(settings.PanelWidth, "F0");
        panelHeightBuffer = FormatFloat(settings.PanelHeight, "F0");
        panelXBuffer = FormatFloat(settings.PanelX, "F0");
        panelYBuffer = FormatFloat(settings.PanelY, "F0");
        raceZoomBuffers.Clear();
    }

    private static string FormatFloat(float value, string format)
    {
        return value.ToString(format, CultureInfo.InvariantCulture);
    }

    private static void DrawCheckbox(Listing_Standard listing, string labelKey, ref bool value, bool enabled)
    {
        var previousEnabled = GUI.enabled;
        GUI.enabled = previousEnabled && enabled;
        listing.CheckboxLabeled(labelKey.Translate(), ref value);
        GUI.enabled = previousEnabled;
    }

    private static float GetGeneralTabHeight()
    {
        const float checkboxHeight = 32f;
        const float spacing = 96f;
        const int checkboxCount = 9;
        return (checkboxCount * checkboxHeight) + spacing;
    }

    private static float GetPortraitTabHeight(PortraitOverlaySettings settings)
    {
        const float checkboxHeight = 32f;
        const float sliderBlockHeight = SettingLabelHeight + SettingControlHeight + 4f;
        const float buttonHeight = 36f;
        const float spacing = 128f;
        const float infoLabelBlockHeight = InfoLabelHeight + 4f;
        const int checkboxCount = 6;
        const int sliderCount = 9;
        const int buttonCount = 4;
        const int infoLabelCount = 2;
        const float raceZoomSpacing = 16f;

        return (checkboxCount * checkboxHeight)
            + ((sliderCount + settings.RaceZoomOverrides.Count) * sliderBlockHeight)
            + (infoLabelCount * infoLabelBlockHeight)
            + (buttonCount * buttonHeight)
            + spacing
            + raceZoomSpacing;
    }

    public override void WriteSettings()
    {
        base.WriteSettings();

        // Measurements depend on the portrait settings; start fresh after they change.
        PortraitFitCache.Clear();
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
