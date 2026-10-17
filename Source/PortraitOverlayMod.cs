using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RimWorld;
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
    private const float SavedRowHeight = 28f;
    private const float SavedSummaryWidth = 190f;

    private enum SettingsTab
    {
        General,
        Portrait,
        Saved
    }

    private static PortraitOverlayMod instance;
    private SettingsTab selectedSettingsTab = SettingsTab.General;
    private Vector2 generalScrollPosition;
    private Vector2 portraitScrollPosition;
    private Vector2 savedScrollPosition;
    private bool showAdvanced;
    private string backgroundAlphaBuffer;
    private string cameraZoomBuffer;
    private string faceEmphasisBuffer;
    private string topCutoffBuffer;
    private string bottomCutoffBuffer;
    private string panelWidthBuffer;
    private string panelHeightBuffer;
    private string panelXBuffer;
    private string panelYBuffer;

    public static PortraitOverlaySettings Settings { get; private set; }

    public static PortraitOverlayMod Instance => instance;

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
                DrawScrollableListing(contentRect, ref portraitScrollPosition, GetPortraitTabHeight(), DrawPortraitTab);
                break;
            case SettingsTab.Saved:
                DrawScrollableListing(contentRect, ref savedScrollPosition, GetSavedTabHeight(settings), DrawSavedTab);
                break;
        }

        settings.ClampValues();
    }

    private void DrawTabs(Rect rect)
    {
        var tabs = new List<TabRecord>
        {
            new("PortraitOverlay.Settings.TabGeneral".Translate(), () => selectedSettingsTab = SettingsTab.General, selectedSettingsTab == SettingsTab.General),
            new("PortraitOverlay.Settings.TabPortrait".Translate(), () => selectedSettingsTab = SettingsTab.Portrait, selectedSettingsTab == SettingsTab.Portrait),
            new("PortraitOverlay.Settings.TabSaved".Translate(), () => selectedSettingsTab = SettingsTab.Saved, selectedSettingsTab == SettingsTab.Saved)
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
        listing.CheckboxLabeled(
            "PortraitOverlay.Settings.ShowHoverToolbar".Translate(),
            ref settings.ShowHoverToolbar,
            "PortraitOverlay.Settings.ShowHoverToolbarTooltip".Translate());
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
        listing.CheckboxLabeled(
            "PortraitOverlay.Settings.AutoFitPortrait".Translate(),
            ref settings.AutoFitPortrait,
            "PortraitOverlay.Settings.AutoFitPortraitTooltip".Translate());

        listing.GapLine();
        DrawSliderWithNumericField(
            listing,
            "PortraitOverlay.Settings.BackgroundAlpha".Translate(FormatPercent(settings.BackgroundAlpha)),
            ref settings.BackgroundAlpha,
            ref backgroundAlphaBuffer,
            0f,
            0.95f,
            "F2");

        listing.GapLine();

        // Numeric framing and layout controls are folded away; edit mode on the panel is the usual way to adjust.
        var toggleLabel = (showAdvanced ? "▼ " : "▶ ") + "PortraitOverlay.Settings.Advanced".Translate();
        if (listing.ButtonText(toggleLabel))
        {
            showAdvanced = !showAdvanced;
        }

        if (showAdvanced)
        {
            DrawInfoLabel(listing, "PortraitOverlay.Settings.PanelNumericInputHint".Translate());
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

        }

        listing.GapLine();
        if (listing.ButtonText("PortraitOverlay.Settings.ResetWindowSettings".Translate()))
        {
            settings.ResetWindowSettings();
            SyncNumericBuffers(settings);
            SaveSettings();
        }

        if (listing.ButtonText("PortraitOverlay.Settings.ResetAllSettings".Translate()))
        {
            ResetAllSettings();
        }
    }

    private void DrawSavedTab(Listing_Standard listing, PortraitOverlaySettings settings)
    {
        DrawInfoLabel(listing, "PortraitOverlay.Saved.Hint".Translate());

        listing.Label("PortraitOverlay.Saved.PawnsHeader".Translate());
        var pawnStore = PortraitAdjustments.PawnStore;
        if (pawnStore == null)
        {
            DrawMutedLine(listing, "PortraitOverlay.Saved.NoGame".Translate());
        }
        else if (pawnStore.Count == 0)
        {
            DrawMutedLine(listing, "PortraitOverlay.Saved.Empty".Translate());
        }
        else
        {
            int? removedId = null;
            var missingIds = new List<int>();
            foreach (var pair in pawnStore.OrderBy(pair => pair.Value.Label ?? string.Empty).ToList())
            {
                var pawn = SelectedPawnPortraitOverlayComponent.FindPawn(pair.Key);
                if (pawn == null)
                {
                    missingIds.Add(pair.Key);
                }

                var name = pawn != null
                    ? pawn.LabelShortCap.ToString()
                    : "PortraitOverlay.Saved.MissingPawn".Translate(pair.Value.Label ?? pair.Key.ToString()).ToString();
                if (DrawSavedRow(listing, name, pair.Value))
                {
                    removedId = pair.Key;
                }
            }

            if (removedId.HasValue)
            {
                pawnStore.Remove(removedId.Value);
            }

            var buttons = listing.GetRect(SavedRowHeight);
            if (Widgets.ButtonText(buttons.LeftHalf().ContractedBy(2f), "PortraitOverlay.Saved.DeleteAll".Translate()))
            {
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                    "PortraitOverlay.Saved.DeleteAllPawnsConfirm".Translate(pawnStore.Count.ToString()),
                    pawnStore.Clear,
                    true));
            }

            if (missingIds.Count > 0
                && Widgets.ButtonText(buttons.RightHalf().ContractedBy(2f), "PortraitOverlay.Saved.PruneMissing".Translate(missingIds.Count.ToString())))
            {
                foreach (var id in missingIds)
                {
                    pawnStore.Remove(id);
                }
            }
        }

        listing.GapLine();
        listing.Label("PortraitOverlay.Saved.RacesHeader".Translate());
        if (settings.RaceAdjustments.Count == 0)
        {
            DrawMutedLine(listing, "PortraitOverlay.Saved.Empty".Translate());
            return;
        }

        string removedRace = null;
        foreach (var pair in settings.RaceAdjustments.OrderBy(pair => GetRaceLabel(pair.Key)).ToList())
        {
            if (DrawSavedRow(listing, GetRaceLabel(pair.Key), pair.Value))
            {
                removedRace = pair.Key;
            }
        }

        if (removedRace != null)
        {
            settings.RaceAdjustments.Remove(removedRace);
            SaveSettings();
        }

        var raceButtons = listing.GetRect(SavedRowHeight);
        if (Widgets.ButtonText(raceButtons.LeftHalf().ContractedBy(2f), "PortraitOverlay.Saved.DeleteAll".Translate()))
        {
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                "PortraitOverlay.Saved.DeleteAllRacesConfirm".Translate(settings.RaceAdjustments.Count.ToString()),
                () =>
                {
                    settings.RaceAdjustments.Clear();
                    SaveSettings();
                },
                true));
        }
    }

    // One saved correction: name, summary, and a delete button. Returns true when delete was clicked.
    private static bool DrawSavedRow(Listing_Standard listing, string name, SavedAdjustment adjustment)
    {
        var row = listing.GetRect(SavedRowHeight);
        Widgets.DrawHighlightIfMouseover(row);
        var previousAnchor = Text.Anchor;
        Text.Anchor = TextAnchor.MiddleLeft;
        var nameWidth = Mathf.Max(0f, row.width - SavedSummaryWidth - RemoveButtonWidth - (2f * SliderFieldGap));
        Widgets.Label(new Rect(row.x, row.y, nameWidth, row.height), name);
        Widgets.Label(new Rect(row.x + nameWidth + SliderFieldGap, row.y, SavedSummaryWidth, row.height), adjustment.Summary());
        Text.Anchor = previousAnchor;
        return Widgets.ButtonText(row.RightPartPixels(RemoveButtonWidth).ContractedBy(2f), "PortraitOverlay.Saved.Delete".Translate());
    }

    private static void DrawMutedLine(Listing_Standard listing, string text)
    {
        var rect = listing.GetRect(SavedRowHeight);
        var previousColor = GUI.color;
        GUI.color = new Color(0.72f, 0.72f, 0.72f);
        Widgets.Label(rect, text);
        GUI.color = previousColor;
    }

    // Resets every mod setting. Per-pawn corrections live in the save, so clearing them is asked separately.
    public static void ResetAllSettings()
    {
        if (Settings == null)
        {
            return;
        }

        Settings.ResetAll();
        instance?.SyncNumericBuffers(Settings);
        SaveSettings();

        var pawnStore = PortraitAdjustments.PawnStore;
        if (pawnStore != null && pawnStore.Count > 0)
        {
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                "PortraitOverlay.Settings.ResetPawnAdjustmentsConfirm".Translate(pawnStore.Count.ToString()),
                pawnStore.Clear,
                true));
        }
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
        return PortraitOverlaySettings.CutoffMax(otherCutoff);
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
        const int checkboxCount = 10;
        return (checkboxCount * checkboxHeight) + spacing;
    }

    private float GetPortraitTabHeight()
    {
        const float checkboxHeight = 32f;
        const float sliderBlockHeight = SettingLabelHeight + SettingControlHeight + 4f;
        const float buttonHeight = 36f;
        const float spacing = 128f;
        const float infoLabelBlockHeight = InfoLabelHeight + 4f;
        const int checkboxCount = 6;
        const int advancedSliderCount = 8;
        const int buttonCount = 3;

        return (checkboxCount * checkboxHeight)
            + sliderBlockHeight
            + (showAdvanced ? (advancedSliderCount * sliderBlockHeight) + infoLabelBlockHeight + 16f : 0f)
            + (buttonCount * buttonHeight)
            + spacing
            + 16f;
    }

    private static float GetSavedTabHeight(PortraitOverlaySettings settings)
    {
        const float headerHeight = SettingLabelHeight + 8f;
        var pawnRows = PortraitAdjustments.PawnStore?.Count ?? 0;
        var raceRows = settings.RaceAdjustments.Count;

        // Each list shows its rows plus a button row, or a single placeholder line when empty.
        var pawnBlock = pawnRows > 0 ? (pawnRows + 1) * SavedRowHeight : SavedRowHeight;
        var raceBlock = raceRows > 0 ? (raceRows + 1) * SavedRowHeight : SavedRowHeight;
        return InfoLabelHeight + 8f + (2f * headerHeight) + pawnBlock + raceBlock + 64f;
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
