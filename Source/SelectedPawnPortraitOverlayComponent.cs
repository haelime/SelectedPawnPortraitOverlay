using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace SelectedPawnPortraitOverlay;

public sealed class SelectedPawnPortraitOverlayComponent : GameComponent
{
    private const int OverlayWindowId = 18467231;
    private const int UnsupportedVehiclePawnWarningId = 18467232;
    private const int ToolbarWindowId = 18467234;
    private const float DragStartThreshold = 5f;
    private const int OverlayGuiDepth = 100;
    private const float ContentPadding = 8f;
    private const float NameRowHeight = 24f;
    private const float NameRowGap = 6f;
    private const float ToolbarHeight = 22f;
    private const float ToolbarInset = 4f;
    private const float ScopeBarHeight = 22f;
    private const float ReadoutHeight = 18f;
    private const float WheelZoomStep = 1.05f;
    private const float FineWheelZoomStep = 1.01f;

    private static readonly Color EditAccent = new(0.94f, 0.62f, 0.15f);
    private static readonly Color BarBackground = new(0f, 0f, 0f, 0.6f);

    // Per-pawn manual corrections for this save, keyed by Thing.thingIDNumber.
    public Dictionary<int, SavedAdjustment> PawnAdjustments = new();

    private bool isDragging;
    private bool dragArmed;
    private Vector2 dragStartMousePosition;
    private Pawn lastDisplayedPawn;
    private Pawn pinnedPawn;

    private bool editing;
    private Pawn editPawn;
    private AdjustmentScope editScope = AdjustmentScope.Pawn;
    private bool panning;
    private bool settingsChangedInEdit;

    public SelectedPawnPortraitOverlayComponent(Game game)
    {
        // Pawns from a previous game are gone; their measurements would only take cache slots.
        PortraitFitCache.Clear();
    }

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Collections.Look(ref PawnAdjustments, "pawnAdjustments", LookMode.Value, LookMode.Deep);
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            PawnAdjustments ??= new Dictionary<int, SavedAdjustment>();
        }
    }

    // Width / height of the portrait area in the current panel, which framing fits pawns to.
    public static float GetPortraitAspect(PortraitOverlaySettings settings)
    {
        var rect = BuildPanelRect(settings);
        var width = rect.width - (2f * ContentPadding);
        var height = rect.height - (2f * ContentPadding) - (settings.ShowName ? NameRowHeight + NameRowGap : 0f);
        return width > 0f && height > 0f ? width / height : 1f;
    }

    // Finds a pawn by id among living map and world pawns, for labelling saved per-pawn corrections.
    public static Pawn FindPawn(int thingId)
    {
        foreach (var map in Find.Maps)
        {
            foreach (var pawn in map.mapPawns.AllPawns)
            {
                if (pawn.thingIDNumber == thingId)
                {
                    return pawn;
                }
            }
        }

        foreach (var pawn in Find.WorldPawns.AllPawnsAlive)
        {
            if (pawn.thingIDNumber == thingId)
            {
                return pawn;
            }
        }

        return null;
    }

    public override void GameComponentOnGUI()
    {
        base.GameComponentOnGUI();

        if (Current.ProgramState != ProgramState.Playing || PortraitOverlayMod.Settings == null)
        {
            return;
        }

        var settings = PortraitOverlayMod.Settings;

        // WorldRendered is also true on orbit maps, which draw the world as a background.
        var pawn = settings.Enabled && !WorldRendererUtility.WorldSelected ? GetPawnToDisplay(settings) : null;
        if (pawn == null)
        {
            ResetPointerState();
            ExitEditMode();
            return;
        }

        if (editing && pawn != editPawn)
        {
            ExitEditMode();
        }

        if (editing && Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
        {
            ExitEditMode();
            Event.current.Use();
        }

        settings.ClampValues();
        var rect = BuildPanelRect(settings);
        // Mouse.IsOver reports false under a window, and the panel itself is a window when dragging is allowed.
        var showToolbar = settings.ShowHoverToolbar && (editing || rect.Contains(Event.current.mousePosition));

        if (settings.AllowDragging || editing)
        {
            Find.WindowStack.ImmediateWindow(
                OverlayWindowId,
                rect,
                WindowLayer.GameUI,
                delegate
                {
                    var localRect = new Rect(0f, 0f, rect.width, rect.height);
                    var layout = ComputeLayout(localRect, settings);
                    if (editing)
                    {
                        HandleEditInput(layout, pawn, settings);
                        HandleDragging(ScopeBarRect(layout), true);
                    }
                    else
                    {
                        HandleDragging(localRect, settings.AllowDragging);
                    }

                    DrawPanel(localRect, pawn, settings, editing);
                    if (editing)
                    {
                        DrawEditOverlay(localRect, layout, pawn, settings);
                    }

                    if (showToolbar)
                    {
                        DrawToolbar(ToolbarRect(layout), pawn, settings);
                    }
                },
                doBackground: false,
                absorbInputAroundWindow: false,
                0f);
            return;
        }

        ResetPointerState();
        var previousDepth = GUI.depth;
        GUI.depth = OverlayGuiDepth;
        DrawPanel(rect, pawn, settings, false);
        GUI.depth = previousDepth;

        if (showToolbar)
        {
            // Only the toolbar captures clicks; the rest of the panel still lets clicks through to the map.
            var toolbarRect = ToolbarRect(ComputeLayout(rect, settings));
            Find.WindowStack.ImmediateWindow(
                ToolbarWindowId,
                toolbarRect,
                WindowLayer.GameUI,
                delegate { DrawToolbar(new Rect(0f, 0f, toolbarRect.width, toolbarRect.height), pawn, settings); },
                doBackground: false,
                absorbInputAroundWindow: false,
                0f);
        }
    }

    private Pawn GetPawnToDisplay(PortraitOverlaySettings settings)
    {
        if (pinnedPawn != null)
        {
            if (!pinnedPawn.Destroyed && ShouldDisplayPawn(pinnedPawn))
            {
                return pinnedPawn;
            }

            pinnedPawn = null;
        }

        var selectedPawn = Find.Selector?.SingleSelectedThing as Pawn;
        if (selectedPawn == null)
        {
            return settings.KeepLastPortrait ? GetCachedPawn() : ClearCachedPawn();
        }

        if (!ShouldDisplayPawn(selectedPawn))
        {
            return ClearCachedPawn();
        }

        lastDisplayedPawn = selectedPawn;
        return selectedPawn;
    }

    private Pawn GetCachedPawn()
    {
        return ShouldDisplayPawn(lastDisplayedPawn) ? lastDisplayedPawn : ClearCachedPawn();
    }

    private Pawn ClearCachedPawn()
    {
        lastDisplayedPawn = null;
        return null;
    }

    private static bool ShouldDisplayPawn(Pawn pawn)
    {
        if (pawn == null || PortraitOverlayMod.Settings == null)
        {
            return false;
        }

        if (IsVehiclePawn(pawn))
        {
            Log.WarningOnce(
                "Selected Pawn Portrait Overlay skipped an unsupported vehicle pawn. Vehicle pawns are not supported by this mod.",
                UnsupportedVehiclePawnWarningId);
            return false;
        }

        var settings = PortraitOverlayMod.Settings;
        if (settings.OnlyPlayerControlledPawns && !pawn.IsPlayerControlled)
        {
            return false;
        }

        if (!settings.ShowAnimals && pawn.RaceProps?.Animal == true)
        {
            return false;
        }

        if (!settings.ShowMechanoids && pawn.RaceProps?.IsMechanoid == true)
        {
            return false;
        }

        if (!settings.ShowAnomalyEntities && pawn.RaceProps?.IsAnomalyEntity == true)
        {
            return false;
        }

        if (!settings.ShowPrisoners && pawn.IsPrisonerOfColony)
        {
            return false;
        }

        if (!settings.ShowSlaves && pawn.IsSlaveOfColony)
        {
            return false;
        }

        return true;
    }

    private static bool IsVehiclePawn(Pawn pawn)
    {
        for (var type = pawn?.GetType(); type != null; type = type.BaseType)
        {
            if (string.Equals(type.FullName, "Vehicles.VehiclePawn", StringComparison.Ordinal)
                || string.Equals(type.Name, "VehiclePawn", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static Rect BuildPanelRect(PortraitOverlaySettings settings)
    {
        var width = Mathf.Min(settings.PanelWidth, UI.screenWidth - 8f);
        var height = Mathf.Min(settings.PanelHeight, UI.screenHeight - 8f);
        var maxX = Mathf.Max(PortraitOverlaySettings.MinPanelX, UI.screenWidth - width);
        var x = Mathf.Clamp(settings.PanelX, PortraitOverlaySettings.MinPanelX, maxX);
        var y = Mathf.Clamp(settings.PanelY, 0f, Mathf.Max(0f, UI.screenHeight - height));
        return new Rect(x, y, width, height);
    }

    // Panel regions in the coordinate space of the given panel rect.
    private readonly struct PanelLayout
    {
        public PanelLayout(Rect visible, Rect title, Rect portrait)
        {
            Visible = visible;
            Title = title;
            Portrait = portrait;
        }

        // The part of the panel left after the top and bottom cutoffs.
        public Rect Visible { get; }
        public Rect Title { get; }
        public Rect Portrait { get; }
    }

    private static PanelLayout ComputeLayout(Rect rect, PortraitOverlaySettings settings)
    {
        var topCutPixels = rect.height * Mathf.Clamp01(settings.TopCutoff);
        var bottomCutPixels = rect.height * Mathf.Clamp01(settings.BottomCutoff);
        var visible = new Rect(
            rect.x,
            rect.y + topCutPixels,
            rect.width,
            Mathf.Max(0f, rect.height - topCutPixels - bottomCutPixels));
        var content = rect.ContractedBy(ContentPadding);
        var title = Rect.zero;
        var portrait = content;
        if (settings.ShowName)
        {
            title = new Rect(content.x, content.y + topCutPixels, content.width, NameRowHeight);
            portrait = new Rect(content.x, content.y + NameRowHeight + NameRowGap, content.width, content.height - NameRowHeight - NameRowGap);
        }

        portrait.y += topCutPixels;
        return new PanelLayout(visible, title, portrait);
    }

    private static Rect ToolbarRect(PanelLayout layout)
    {
        var visible = layout.Visible;
        return new Rect(
            visible.x + ToolbarInset,
            visible.yMax - ToolbarHeight - ToolbarInset,
            Mathf.Max(0f, visible.width - (2f * ToolbarInset)),
            ToolbarHeight);
    }

    private static Rect ScopeBarRect(PanelLayout layout)
    {
        return new Rect(layout.Visible.x, layout.Visible.y, layout.Visible.width, ScopeBarHeight);
    }

    private void ResetPointerState()
    {
        isDragging = false;
        dragArmed = false;
        panning = false;
    }

    private void HandleDragging(Rect dragArea, bool enabled)
    {
        var currentEvent = Event.current;
        if (currentEvent == null || !enabled)
        {
            if (currentEvent != null && currentEvent.rawType == EventType.MouseUp)
            {
                isDragging = false;
                dragArmed = false;
            }

            return;
        }

        var settings = PortraitOverlayMod.Settings;
        switch (currentEvent.rawType)
        {
            case EventType.MouseDown when currentEvent.button == 0 && dragArea.Contains(currentEvent.mousePosition):
                dragArmed = true;
                dragStartMousePosition = currentEvent.mousePosition;
                break;
            case EventType.MouseDrag when dragArmed && !isDragging:
                if ((currentEvent.mousePosition - dragStartMousePosition).sqrMagnitude < DragStartThreshold * DragStartThreshold)
                {
                    break;
                }

                isDragging = true;
                currentEvent.Use();
                break;
            case EventType.MouseDrag when isDragging:
                settings.PanelX += currentEvent.delta.x;
                settings.PanelY += currentEvent.delta.y;
                settings.ClampValues();
                currentEvent.Use();
                break;
            case EventType.MouseUp:
                if (isDragging)
                {
                    PortraitOverlayMod.SaveSettings();
                    currentEvent.Use();
                }

                isDragging = false;
                dragArmed = false;
                break;
        }
    }

    private void EnterEditMode(Pawn pawn)
    {
        editing = true;
        editPawn = pawn;
        editScope = AdjustmentScope.Pawn;
        panning = false;
        settingsChangedInEdit = false;
    }

    private void ExitEditMode()
    {
        if (!editing)
        {
            return;
        }

        editing = false;
        editPawn = null;
        panning = false;
        if (settingsChangedInEdit)
        {
            PortraitOverlayMod.SaveSettings();
            settingsChangedInEdit = false;
        }
    }

    // Edit mode input over the portrait: drag pans, the wheel zooms, right click opens the menu. Events are used
    // so they do not reach the map; the scope bar and toolbar handle their own clicks.
    private void HandleEditInput(PanelLayout layout, Pawn pawn, PortraitOverlaySettings settings)
    {
        var currentEvent = Event.current;
        var mouse = currentEvent.mousePosition;
        var overPortrait = layout.Visible.Contains(mouse)
            && !ScopeBarRect(layout).Contains(mouse)
            && !(settings.ShowHoverToolbar && ToolbarRect(layout).Contains(mouse));

        switch (currentEvent.type)
        {
            case EventType.MouseDown when currentEvent.button == 0 && overPortrait:
                panning = true;
                currentEvent.Use();
                break;
            case EventType.MouseDrag when panning:
                var portraitHeight = Mathf.Max(1f, layout.Portrait.height);
                var worldPerPixel = 2f / (Mathf.Max(PortraitFraming.MinZoom, PawnFullBodyPortraitRenderer.LastZoom) * portraitHeight);

                // Dragging right moves the pawn right, which means moving the camera left; screen y grows downward.
                ApplyEdit(pawn, settings, 1f, -currentEvent.delta.x * worldPerPixel, currentEvent.delta.y * worldPerPixel);
                currentEvent.Use();
                break;
            case EventType.MouseUp when panning:
                panning = false;
                currentEvent.Use();
                break;
            case EventType.ScrollWheel when overPortrait:
                var step = currentEvent.shift ? FineWheelZoomStep : WheelZoomStep;
                ApplyEdit(pawn, settings, currentEvent.delta.y < 0f ? step : 1f / step, 0f, 0f);
                currentEvent.Use();
                break;
            case EventType.MouseDown when currentEvent.button == 1 && layout.Visible.Contains(mouse):
                Find.WindowStack.Add(new FloatMenu(BuildEditMenu(pawn, settings)));
                currentEvent.Use();
                break;
        }
    }

    private void ApplyEdit(Pawn pawn, PortraitOverlaySettings settings, float zoomFactor, float deltaX, float deltaZ)
    {
        PortraitAdjustments.Apply(editScope, pawn, settings, zoomFactor, deltaX, deltaZ);
        if (editScope != AdjustmentScope.Pawn)
        {
            settingsChangedInEdit = true;
        }
    }

    private List<FloatMenuOption> BuildEditMenu(Pawn pawn, PortraitOverlaySettings settings)
    {
        var options = new List<FloatMenuOption>();
        var hasPawn = PortraitAdjustments.Has(AdjustmentScope.Pawn, pawn, settings);
        var hasRace = PortraitAdjustments.Has(AdjustmentScope.Race, pawn, settings);
        if (hasPawn)
        {
            options.Add(new FloatMenuOption("PortraitOverlay.Menu.ClearPawn".Translate(), () => ClearScope(AdjustmentScope.Pawn, pawn, settings)));
        }

        if (hasRace)
        {
            options.Add(new FloatMenuOption("PortraitOverlay.Menu.ClearRace".Translate(), () => ClearScope(AdjustmentScope.Race, pawn, settings)));
        }

        if (hasPawn && hasRace)
        {
            options.Add(new FloatMenuOption("PortraitOverlay.Menu.ClearBoth".Translate(), () =>
            {
                ClearScope(AdjustmentScope.Pawn, pawn, settings);
                ClearScope(AdjustmentScope.Race, pawn, settings);
            }));
        }

        options.Add(new FloatMenuOption("PortraitOverlay.Menu.ResetAll".Translate(), () =>
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                "PortraitOverlay.Menu.ResetAllConfirm".Translate(),
                () => PortraitOverlayMod.ResetAllSettings(),
                true))));
        options.Add(new FloatMenuOption("PortraitOverlay.Menu.OpenSettings".Translate(), OpenSettings));
        options.Add(new FloatMenuOption("PortraitOverlay.Menu.ExitEdit".Translate(), ExitEditMode));
        return options;
    }

    private void ClearScope(AdjustmentScope scope, Pawn pawn, PortraitOverlaySettings settings)
    {
        PortraitAdjustments.Clear(scope, pawn, settings);
        if (scope != AdjustmentScope.Pawn)
        {
            PortraitOverlayMod.SaveSettings();
        }
    }

    private static void OpenSettings()
    {
        if (PortraitOverlayMod.Instance != null)
        {
            Find.WindowStack.Add(new Dialog_ModSettings(PortraitOverlayMod.Instance));
        }
    }

    private void DrawToolbar(Rect rect, Pawn pawn, PortraitOverlaySettings settings)
    {
        if (rect.width <= 0f || rect.height <= 0f)
        {
            return;
        }

        Widgets.DrawBoxSolid(rect, BarBackground);
        var previousFont = Text.Font;
        Text.Font = GameFont.Tiny;
        var buttonWidth = rect.width / 4f;
        var hasPawnAdjustment = PortraitAdjustments.Has(AdjustmentScope.Pawn, pawn, settings);
        var pinned = pinnedPawn == pawn;

        var editRect = new Rect(rect.x, rect.y, buttonWidth, rect.height).ContractedBy(1f);
        if (Widgets.ButtonText(editRect, (editing ? "PortraitOverlay.Toolbar.Done" : "PortraitOverlay.Toolbar.Edit").Translate(), true, true, true))
        {
            if (editing)
            {
                ExitEditMode();
            }
            else
            {
                EnterEditMode(pawn);
            }
        }

        TooltipHandler.TipRegion(editRect, "PortraitOverlay.Toolbar.EditTooltip".Translate());

        var resetRect = new Rect(rect.x + buttonWidth, rect.y, buttonWidth, rect.height).ContractedBy(1f);
        if (Widgets.ButtonText(resetRect, "PortraitOverlay.Toolbar.Reset".Translate(), true, true, hasPawnAdjustment) && hasPawnAdjustment)
        {
            PortraitAdjustments.Clear(AdjustmentScope.Pawn, pawn, settings);
        }

        TooltipHandler.TipRegion(resetRect, "PortraitOverlay.Toolbar.ResetTooltip".Translate());

        var pinRect = new Rect(rect.x + (2f * buttonWidth), rect.y, buttonWidth, rect.height).ContractedBy(1f);
        if (Widgets.ButtonText(pinRect, (pinned ? "PortraitOverlay.Toolbar.Unpin" : "PortraitOverlay.Toolbar.Pin").Translate(), true, true, true))
        {
            pinnedPawn = pinned ? null : pawn;
        }

        TooltipHandler.TipRegion(pinRect, "PortraitOverlay.Toolbar.PinTooltip".Translate());

        var settingsRect = new Rect(rect.x + (3f * buttonWidth), rect.y, buttonWidth, rect.height).ContractedBy(1f);
        if (Widgets.ButtonText(settingsRect, "PortraitOverlay.Toolbar.Settings".Translate(), true, true, true))
        {
            OpenSettings();
        }

        Text.Font = previousFont;
    }

    private void DrawEditOverlay(Rect rect, PanelLayout layout, Pawn pawn, PortraitOverlaySettings settings)
    {
        var previousColor = GUI.color;
        GUI.color = EditAccent;
        Widgets.DrawBox(layout.Visible, 2);
        GUI.color = previousColor;

        var previousFont = Text.Font;
        var previousAnchor = Text.Anchor;
        Text.Font = GameFont.Tiny;

        var bar = ScopeBarRect(layout);
        Widgets.DrawBoxSolid(bar, BarBackground);
        var x = bar.x + 4f;
        x = DrawScopeChip(x, bar, AdjustmentScope.Pawn, "PortraitOverlay.Edit.ScopePawn".Translate(), pawn, settings);
        x = DrawScopeChip(x, bar, AdjustmentScope.Race, "PortraitOverlay.Edit.ScopeRace".Translate(pawn.def.LabelCap), pawn, settings);
        DrawScopeChip(x, bar, AdjustmentScope.Global, "PortraitOverlay.Edit.ScopeGlobal".Translate(), pawn, settings);

        var readoutBottom = settings.ShowHoverToolbar ? ToolbarRect(layout).y - 2f : layout.Visible.yMax - ToolbarInset;
        var readout = new Rect(layout.Visible.x + ToolbarInset, readoutBottom - ReadoutHeight, layout.Visible.width - (2f * ToolbarInset), ReadoutHeight);
        Widgets.DrawBoxSolid(readout, BarBackground);
        Text.Anchor = TextAnchor.MiddleCenter;
        Widgets.Label(readout, ScopeReadout(pawn, settings));

        // The hint would cover the right-click menu, which opens right where the mouse is.
        if (Find.WindowStack.FloatMenu == null)
        {
            var portraitArea = new Rect(layout.Visible.x, bar.yMax, layout.Visible.width, Mathf.Max(0f, readout.y - bar.yMax));
            TooltipHandler.TipRegion(portraitArea, "PortraitOverlay.Edit.Hint".Translate());
        }

        Text.Anchor = previousAnchor;
        Text.Font = previousFont;
    }

    // Draws one scope chip and returns the x where the next one starts. A dot marks a scope with a saved
    // correction; hovering such a chip shows an x that clears it (the global scope is reset from the settings).
    private float DrawScopeChip(float x, Rect bar, AdjustmentScope scope, string label, Pawn pawn, PortraitOverlaySettings settings)
    {
        var hasAdjustment = PortraitAdjustments.Has(scope, pawn, settings);
        var text = hasAdjustment ? "• " + label : label;
        var clearable = hasAdjustment && scope != AdjustmentScope.Global;
        var clearWidth = clearable ? 14f : 0f;
        var width = Mathf.Min(Text.CalcSize(text).x + 10f + clearWidth, Mathf.Max(0f, bar.xMax - x - 2f));
        if (width <= 0f)
        {
            return x;
        }

        var chip = new Rect(x, bar.y + 3f, width, bar.height - 6f);
        var selected = editScope == scope;
        var previousColor = GUI.color;
        Widgets.DrawBoxSolid(chip, selected ? EditAccent : new Color(1f, 1f, 1f, 0.12f));
        GUI.color = selected ? new Color(0.15f, 0.08f, 0f) : Color.white;
        Text.Anchor = TextAnchor.MiddleCenter;
        Widgets.Label(new Rect(chip.x, chip.y, chip.width - clearWidth, chip.height), text);
        GUI.color = previousColor;

        var clearRect = new Rect(chip.xMax - clearWidth, chip.y, clearWidth, chip.height);
        if (clearable && Mouse.IsOver(chip))
        {
            Widgets.Label(clearRect, "×");
            TooltipHandler.TipRegion(clearRect, "PortraitOverlay.Edit.ClearScopeTooltip".Translate());
            if (Widgets.ButtonInvisible(clearRect))
            {
                ClearScope(scope, pawn, settings);
                return chip.xMax + 4f;
            }
        }

        if (Widgets.ButtonInvisible(new Rect(chip.x, chip.y, chip.width - clearWidth, chip.height)))
        {
            editScope = scope;
        }

        return chip.xMax + 4f;
    }

    private string ScopeReadout(Pawn pawn, PortraitOverlaySettings settings)
    {
        float zoom;
        float offsetX;
        float offsetZ;
        switch (editScope)
        {
            case AdjustmentScope.Global:
                zoom = settings.CameraZoom;
                offsetX = settings.GlobalOffsetX;
                offsetZ = settings.GlobalOffsetZ;
                break;
            default:
                var saved = editScope == AdjustmentScope.Pawn
                    ? PortraitAdjustments.GetPawn(pawn)
                    : PortraitAdjustments.GetRace(pawn, settings);
                zoom = saved?.Zoom ?? 1f;
                offsetX = saved?.OffsetX ?? 0f;
                offsetZ = saved?.OffsetZ ?? 0f;
                break;
        }

        return "PortraitOverlay.Edit.Readout".Translate(
            zoom.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
            SavedAdjustment.FormatOffset(offsetX),
            SavedAdjustment.FormatOffset(offsetZ));
    }

    private static void DrawPanel(Rect rect, Pawn pawn, PortraitOverlaySettings settings, bool editing)
    {
        var layout = ComputeLayout(rect, settings);
        var visiblePanelRect = layout.Visible;

        if (!editing)
        {
            TooltipHandler.TipRegion(rect, "PortraitOverlay.Overlay.Tooltip".Translate());
        }

        if (visiblePanelRect.height <= 0f)
        {
            return;
        }

        if (settings.ShowBackground)
        {
            DrawBackgroundSection(visiblePanelRect, settings.BackgroundAlpha);
        }

        GUI.BeginGroup(visiblePanelRect);
        var localTitleRect = settings.ShowName
            ? new Rect(layout.Title.x - visiblePanelRect.x, layout.Title.y - visiblePanelRect.y, layout.Title.width, layout.Title.height)
            : Rect.zero;
        var localPortraitRect = new Rect(
            layout.Portrait.x - visiblePanelRect.x,
            layout.Portrait.y - visiblePanelRect.y,
            layout.Portrait.width,
            layout.Portrait.height);

        if (settings.ShowName && !editing)
        {
            var previousAnchor = Text.Anchor;
            var previousFont = Text.Font;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            Widgets.Label(localTitleRect, "PortraitOverlay.Overlay.Title".Translate(pawn.Name?.ToStringShort ?? pawn.LabelShortCap));
            Text.Anchor = previousAnchor;
            Text.Font = previousFont;
        }

        // Textures are only drawn on repaint; rendering on layout and input events too would repeat the
        // portrait render (and any measurement) several times a frame for nothing.
        if (Event.current.type != EventType.Repaint)
        {
            GUI.EndGroup();
            return;
        }

        var portrait = PortraitRenderCache.GetPortrait(pawn, layout.Portrait.size, settings);
        if (portrait == null || portrait == BaseContent.BadTex)
        {
            DrawFallbackLabel(localPortraitRect);
            GUI.EndGroup();
            return;
        }

        GUI.DrawTexture(localPortraitRect, portrait, ScaleMode.ScaleToFit, true);
        GUI.EndGroup();
    }

    private static void DrawBackgroundSection(Rect rect, float alpha)
    {
        if (rect.width <= 0f || rect.height <= 0f)
        {
            return;
        }

        Widgets.DrawBoxSolid(rect, new Color(0.08f, 0.08f, 0.08f, alpha));
    }

    private static void DrawFallbackLabel(Rect rect)
    {
        var previousAnchor = Text.Anchor;
        Text.Anchor = TextAnchor.MiddleCenter;
        Widgets.Label(rect, "PortraitOverlay.Overlay.NoPortrait".Translate());
        Text.Anchor = previousAnchor;
    }
}
