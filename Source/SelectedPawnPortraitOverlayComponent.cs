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
    private const int HintWindowId = 18467235;
    private const float HandleMargin = 10f;
    private const float CropHandleWidth = 34f;
    private const float CropHandleThickness = 7f;
    private const float ResizeHandleSize = 10f;
    private const float PresetRowHeight = 20f;
    private const float EditControlsGap = 3f;
    private const float MinEditControlsWidth = 260f;
    private const float MinPanelWidth = 80f;
    private const float MinPanelHeight = 100f;
    private const float HintWidth = 240f;
    private const float HintHeight = 82f;
    private const int MaxUndoSteps = 50;
    private const float WheelUndoGroupSeconds = 0.6f;
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
    // Scope menu order: category down to this pawn, with All last because it is rarely the right choice.
    private static readonly AdjustmentScope[] ScopeOrder =
    {
        AdjustmentScope.Category,
        AdjustmentScope.Race,
        AdjustmentScope.Variant,
        AdjustmentScope.Pawn,
        AdjustmentScope.Global
    };

    // Delete entries in the right-click menu, narrowest first.
    private static readonly AdjustmentScope[] ClearOrder =
    {
        AdjustmentScope.Pawn,
        AdjustmentScope.Variant,
        AdjustmentScope.Race,
        AdjustmentScope.Category,
        AdjustmentScope.Global
    };
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
    private AdjustmentScope editScope = AdjustmentScope.Category;
    private DragMode dragMode;
    private bool settingsChangedInEdit;
    private readonly List<AdjustmentSnapshot> undoStack = new();
    private float lastWheelTime = -10f;
    private static KeyBindingDef toggleEditKey;
    private static bool toggleEditKeyResolved;

    private enum DragMode
    {
        None,
        Pan,
        MovePanel,
        CropTop,
        CropBottom,
        Resize
    }

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

    // Portrait area inside a panel of the given size, matching the layout DrawPanel uses.
    public static Vector2 PortraitSizeFor(Vector2 panelSize, PortraitOverlaySettings settings)
    {
        return new Vector2(
            Mathf.Max(1f, panelSize.x - (2f * ContentPadding)),
            Mathf.Max(1f, panelSize.y - (2f * ContentPadding) - (settings.ShowName ? NameRowHeight + NameRowGap : 0f)));
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

        HandleKeys(pawn, settings);

        settings.ClampValues();
        var rect = BuildPanelRect(settings, pawn);
        if (editing && dragMode != DragMode.None)
        {
            ContinueDrag(rect, pawn, settings);
        }
        // Mouse.IsOver reports false under a window, and the panel itself is a window when dragging is allowed.
        var showToolbar = settings.ShowHoverToolbar && (editing || rect.Contains(Event.current.mousePosition));
        if (settings.ShowHoverToolbar && !settings.HoverToolbarHintShown && !editing)
        {
            DrawFirstRunHint(rect, settings);
        }

        if (editing)
        {
            // The window reaches a little past the panel so the crop and resize handles on its edges stay clickable,
            // and below it for the edit controls, which sit outside so they never cover a small panel's portrait.
            var outer = rect.ExpandedBy(HandleMargin);
            outer.width = Mathf.Max(outer.width, MinEditControlsWidth + (2f * HandleMargin));
            outer.height += EditControlsHeight;
            Find.WindowStack.ImmediateWindow(
                OverlayWindowId,
                outer,
                WindowLayer.GameUI,
                delegate
                {
                    var localRect = new Rect(HandleMargin, HandleMargin, rect.width, rect.height);
                    var layout = ComputeLayout(localRect, settings);
                    HandleEditInput(localRect, layout, pawn, settings);
                    DrawPanel(localRect, pawn, settings, true);
                    DrawEditOverlay(layout, pawn, settings);
                    DrawToolbar(EditToolbarRect(layout), pawn, settings);
                },
                doBackground: false,
                absorbInputAroundWindow: false,
                0f);
            return;
        }

        if (settings.AllowDragging)
        {
            Find.WindowStack.ImmediateWindow(
                OverlayWindowId,
                rect,
                WindowLayer.GameUI,
                delegate
                {
                    var localRect = new Rect(0f, 0f, rect.width, rect.height);
                    var layout = ComputeLayout(localRect, settings);
                    HandleDragging(localRect, true);
                    DrawPanel(localRect, pawn, settings, false);
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

    // The panel for a pawn: the base size from settings times any panel size its race or pawn entry sets.
    private static Rect BuildPanelRect(PortraitOverlaySettings settings, Pawn pawn)
    {
        var scale = PortraitAdjustments.ResolvePanelScale(pawn, settings);
        var width = Mathf.Min(settings.PanelWidth * scale.x, UI.screenWidth - 8f);
        var height = Mathf.Min(settings.PanelHeight * scale.y, UI.screenHeight - 8f);
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
        dragMode = DragMode.None;
    }

    // Escape leaves edit mode, Ctrl+Z undoes the last edit, and the optional key binding toggles edit mode.
    private void HandleKeys(Pawn pawn, PortraitOverlaySettings settings)
    {
        var currentEvent = Event.current;
        if (currentEvent.type != EventType.KeyDown)
        {
            return;
        }

        if (editing && currentEvent.keyCode == KeyCode.Escape)
        {
            ExitEditMode();
            currentEvent.Use();
            return;
        }

        if (editing && currentEvent.keyCode == KeyCode.Z && (currentEvent.control || currentEvent.command))
        {
            Undo(settings);
            currentEvent.Use();
            return;
        }

        if (!toggleEditKeyResolved)
        {
            toggleEditKey = DefDatabase<KeyBindingDef>.GetNamedSilentFail("PortraitOverlay_ToggleEditMode");
            toggleEditKeyResolved = true;
        }

        if (toggleEditKey != null && toggleEditKey.KeyDownEvent)
        {
            if (editing)
            {
                ExitEditMode();
            }
            else
            {
                EnterEditMode(pawn, settings);
            }

            currentEvent.Use();
        }
    }

    private void PushUndo(Pawn pawn, PortraitOverlaySettings settings)
    {
        undoStack.Add(AdjustmentSnapshot.Capture(pawn, settings));
        if (undoStack.Count > MaxUndoSteps)
        {
            undoStack.RemoveAt(0);
        }
    }

    private void Undo(PortraitOverlaySettings settings)
    {
        if (undoStack.Count == 0)
        {
            return;
        }

        var snapshot = undoStack[undoStack.Count - 1];
        undoStack.RemoveAt(undoStack.Count - 1);
        snapshot.Restore(settings);
        settingsChangedInEdit = true;
    }

    private void DrawFirstRunHint(Rect panel, PortraitOverlaySettings settings)
    {
        var x = panel.xMax + 8f + HintWidth <= UI.screenWidth ? panel.xMax + 8f : panel.x - 8f - HintWidth;
        var hintRect = new Rect(Mathf.Max(0f, x), panel.y, HintWidth, HintHeight);
        Find.WindowStack.ImmediateWindow(
            HintWindowId,
            hintRect,
            WindowLayer.GameUI,
            delegate
            {
                var local = new Rect(0f, 0f, hintRect.width, hintRect.height);
                Widgets.DrawBoxSolid(local, BarBackground);
                var previousColor = GUI.color;
                GUI.color = EditAccent;
                Widgets.DrawBox(local);
                GUI.color = previousColor;
                var previousFont = Text.Font;
                Text.Font = GameFont.Small;
                Widgets.Label(new Rect(8f, 6f, local.width - 16f, local.height - 34f), "PortraitOverlay.Hint.FirstRun".Translate());
                Text.Font = previousFont;
                if (Widgets.ButtonText(new Rect(local.width - 78f, local.height - 26f, 70f, 20f), "PortraitOverlay.Hint.Close".Translate()))
                {
                    settings.HoverToolbarHintShown = true;
                    PortraitOverlayMod.SaveSettings();
                }
            },
            doBackground: false,
            absorbInputAroundWindow: false,
            0f);
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

    private void EnterEditMode(Pawn pawn, PortraitOverlaySettings settings)
    {
        editing = true;
        editPawn = pawn;

        // Category first: one edit then covers every similar pawn, which is the usual intent.
        editScope = AdjustmentScope.Category;
        dragMode = DragMode.None;
        settingsChangedInEdit = false;
        if (!settings.HoverToolbarHintShown)
        {
            settings.HoverToolbarHintShown = true;
            settingsChangedInEdit = true;
        }
    }

    private void ExitEditMode()
    {
        if (!editing)
        {
            return;
        }

        editing = false;
        editPawn = null;
        dragMode = DragMode.None;
        if (settingsChangedInEdit)
        {
            PortraitOverlayMod.SaveSettings();
            settingsChangedInEdit = false;
        }
    }

    // Edit mode input. Drag on the portrait pans, the wheel zooms, the scope bar moves the panel, edge handles crop,
    // the corner handle resizes, double-clicking the readout opens exact entry, and right click opens the menu.
    // Handled events are used so they do not reach the map; buttons drawn later handle their own clicks.
    private void HandleEditInput(Rect panel, PanelLayout layout, Pawn pawn, PortraitOverlaySettings settings)
    {
        var currentEvent = Event.current;
        var mouse = currentEvent.mousePosition;
        var overButtons = EditToolbarRect(layout).Contains(mouse) || PresetRowRect(layout).Contains(mouse);
        var overScopeChips = ScopeBarRect(layout).Contains(mouse) && mouse.x < ScopeButtonEnd;
        var overPortrait = layout.Visible.Contains(mouse) && !ScopeBarRect(layout).Contains(mouse) && !overButtons;

        switch (currentEvent.type)
        {
            case EventType.MouseDown when currentEvent.button == 0 && currentEvent.clickCount == 2 && ReadoutRect(layout).Contains(mouse):
                Find.WindowStack.Add(new Dialog_PortraitAdjustment(editScope, pawn, () => PushUndo(pawn, settings)));
                currentEvent.Use();
                break;
            case EventType.MouseDown when currentEvent.button == 0:
                var mode = DragModeAt(mouse, layout, overPortrait, overScopeChips);
                if (mode == DragMode.None)
                {
                    break;
                }

                if (mode != DragMode.MovePanel)
                {
                    PushUndo(pawn, settings);
                }

                dragMode = mode;
                currentEvent.Use();
                break;
            case EventType.ScrollWheel when overPortrait:
                // A burst of wheel ticks is one undo step.
                if (Time.realtimeSinceStartup - lastWheelTime > WheelUndoGroupSeconds)
                {
                    PushUndo(pawn, settings);
                }

                lastWheelTime = Time.realtimeSinceStartup;
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

    // Drags are followed here rather than inside the panel window, which stops getting events once the mouse
    // leaves it (resizing outward always does). rawType still reports events another window already used.
    private void ContinueDrag(Rect panel, Pawn pawn, PortraitOverlaySettings settings)
    {
        var currentEvent = Event.current;
        switch (currentEvent.rawType)
        {
            case EventType.MouseDrag:
                ApplyDrag(currentEvent.delta, panel, ComputeLayout(panel, settings), pawn, settings);
                currentEvent.Use();
                break;
            case EventType.MouseUp:
                if (dragMode != DragMode.Pan || editScope != AdjustmentScope.Pawn)
                {
                    PortraitOverlayMod.SaveSettings();
                }

                dragMode = DragMode.None;
                currentEvent.Use();
                break;
        }
    }

    // Where the mouse is decides what a drag does; handles win over the portrait under them.
    private static DragMode DragModeAt(Vector2 mouse, PanelLayout layout, bool overPortrait, bool overScopeChips)
    {
        if (TopCropHandle(layout).Contains(mouse))
        {
            return DragMode.CropTop;
        }

        if (BottomCropHandle(layout).Contains(mouse))
        {
            return DragMode.CropBottom;
        }

        if (ResizeHandle(layout).Contains(mouse))
        {
            return DragMode.Resize;
        }

        if (ScopeBarRect(layout).Contains(mouse) && !overScopeChips)
        {
            return DragMode.MovePanel;
        }

        return overPortrait ? DragMode.Pan : DragMode.None;
    }

    private void ApplyDrag(Vector2 delta, Rect panel, PanelLayout layout, Pawn pawn, PortraitOverlaySettings settings)
    {
        switch (dragMode)
        {
            case DragMode.Pan:
                var portraitHeight = Mathf.Max(1f, layout.Portrait.height);
                var worldPerPixel = 2f / (Mathf.Max(PortraitFraming.MinZoom, PawnFullBodyPortraitRenderer.LastZoom) * portraitHeight);

                // Dragging right moves the pawn right, which means moving the camera left; screen y grows downward.
                ApplyEdit(pawn, settings, 1f, -delta.x * worldPerPixel, delta.y * worldPerPixel);
                break;
            case DragMode.MovePanel:
                settings.PanelX += delta.x;
                settings.PanelY += delta.y;
                break;
            case DragMode.CropTop:
                settings.TopCutoff = Mathf.Clamp(
                    settings.TopCutoff + (delta.y / Mathf.Max(1f, panel.height)),
                    0f,
                    PortraitOverlaySettings.CutoffMax(settings.BottomCutoff));
                break;
            case DragMode.CropBottom:
                settings.BottomCutoff = Mathf.Clamp(
                    settings.BottomCutoff - (delta.y / Mathf.Max(1f, panel.height)),
                    0f,
                    PortraitOverlaySettings.CutoffMax(settings.TopCutoff));
                break;
            case DragMode.Resize:
                // The handle moves the scaled panel's corner; the base size changes by the unscaled amount.
                var scale = PortraitAdjustments.ResolvePanelScale(pawn, settings);
                settings.PanelWidth = Mathf.Max(MinPanelWidth, settings.PanelWidth + (delta.x / Mathf.Max(0.01f, scale.x)));
                settings.PanelHeight = Mathf.Max(MinPanelHeight, settings.PanelHeight + (delta.y / Mathf.Max(0.01f, scale.y)));
                break;
        }

        settings.ClampValues();
    }

    private static Rect TopCropHandle(PanelLayout layout)
    {
        var visible = layout.Visible;
        return new Rect(visible.center.x - (CropHandleWidth / 2f), visible.y - CropHandleThickness, CropHandleWidth, CropHandleThickness);
    }

    private static Rect BottomCropHandle(PanelLayout layout)
    {
        var visible = layout.Visible;
        return new Rect(visible.center.x - (CropHandleWidth / 2f), visible.yMax, CropHandleWidth, CropHandleThickness);
    }

    private static Rect ResizeHandle(PanelLayout layout)
    {
        var visible = layout.Visible;
        return new Rect(visible.xMax - (ResizeHandleSize / 2f), visible.yMax - (ResizeHandleSize / 2f), ResizeHandleSize, ResizeHandleSize);
    }

    // Edit controls stack below the panel: readout, presets, then the toolbar.
    private static float EditControlsHeight => CropHandleThickness + EditControlsGap + ReadoutHeight + 2f + PresetRowHeight + 2f + ToolbarHeight;

    private static Rect ReadoutRect(PanelLayout layout)
    {
        var top = layout.Visible.yMax + CropHandleThickness + EditControlsGap;
        return new Rect(layout.Visible.x, top, Mathf.Max(layout.Visible.width, MinEditControlsWidth), ReadoutHeight);
    }

    private static Rect PresetRowRect(PanelLayout layout)
    {
        var readout = ReadoutRect(layout);
        return new Rect(readout.x, readout.yMax + 2f, readout.width, PresetRowHeight);
    }

    private static Rect EditToolbarRect(PanelLayout layout)
    {
        var presets = PresetRowRect(layout);
        return new Rect(presets.x, presets.yMax + 2f, presets.width, ToolbarHeight);
    }

    // Right edge of the scope button, so the rest of the scope bar can serve as a drag handle.
    private static float ScopeButtonEnd;

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
        if (undoStack.Count > 0)
        {
            options.Add(new FloatMenuOption("PortraitOverlay.Menu.Undo".Translate(), () => Undo(settings)));
        }

        // One delete entry per scope that holds a correction for this pawn, narrowest first. "Delete all" leaves the
        // All scope alone, since it frames every pawn, not just this one.
        var saved = new List<AdjustmentScope>();
        foreach (var scope in ClearOrder)
        {
            if (PortraitAdjustments.Has(scope, pawn, settings))
            {
                if (scope != AdjustmentScope.Global)
                {
                    saved.Add(scope);
                }

                options.Add(new FloatMenuOption(
                    "PortraitOverlay.Menu.ClearScope".Translate(PortraitAdjustments.ScopeLabel(scope, pawn)),
                    () => ClearScope(scope, pawn, settings)));
            }
        }

        if (saved.Count > 1)
        {
            options.Add(new FloatMenuOption("PortraitOverlay.Menu.ClearAllScopes".Translate(), () =>
            {
                PushUndo(pawn, settings);
                foreach (var scope in saved)
                {
                    PortraitAdjustments.Clear(scope, pawn, settings);
                }

                PortraitOverlayMod.SaveSettings();
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

    // The scope menu: every level from broadest to narrowest with the pawn's own category, race, and variant
    // names. A dot marks levels that hold a correction; levels that do not apply to this pawn are greyed out.
    private List<FloatMenuOption> BuildScopeMenu(Pawn pawn, PortraitOverlaySettings settings)
    {
        var options = new List<FloatMenuOption>();
        foreach (var scope in ScopeOrder)
        {
            var label = PortraitAdjustments.ScopeLabel(scope, pawn);
            if (PortraitAdjustments.Has(scope, pawn, settings))
            {
                label = "• " + label;
            }

            if (scope == editScope)
            {
                label = "✓ " + label;
            }

            var target = scope;
            var available = PortraitAdjustments.IsAvailable(scope, pawn);
            var option = available
                ? new FloatMenuOption(label, () => editScope = target)
                : new FloatMenuOption(label, null);
            option.tooltip = new TipSignal(ScopeHelp(scope, available));
            options.Add(option);
        }

        return options;
    }

    // Guidance shown when hovering a scope in the scope menu.
    private static string ScopeHelp(AdjustmentScope scope, bool available)
    {
        if (!available)
        {
            return "PortraitOverlay.ScopeHelp.VariantUnavailable".Translate();
        }

        return ("PortraitOverlay.ScopeHelp." + scope).Translate();
    }

    private void ClearScope(AdjustmentScope scope, Pawn pawn, PortraitOverlaySettings settings)
    {
        PushUndo(pawn, settings);
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
                EnterEditMode(pawn, settings);
            }
        }

        TooltipHandler.TipRegion(editRect, "PortraitOverlay.Toolbar.EditTooltip".Translate());

        var resetRect = new Rect(rect.x + buttonWidth, rect.y, buttonWidth, rect.height).ContractedBy(1f);
        if (Widgets.ButtonText(resetRect, "PortraitOverlay.Toolbar.Reset".Translate(), true, true, hasPawnAdjustment) && hasPawnAdjustment)
        {
            ClearScope(AdjustmentScope.Pawn, pawn, settings);
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

    private void DrawEditOverlay(PanelLayout layout, Pawn pawn, PortraitOverlaySettings settings)
    {
        var previousColor = GUI.color;
        GUI.color = EditAccent;
        Widgets.DrawBox(layout.Visible, 2);
        Widgets.DrawBoxSolid(TopCropHandle(layout), EditAccent);
        Widgets.DrawBoxSolid(BottomCropHandle(layout), EditAccent);
        Widgets.DrawBoxSolid(ResizeHandle(layout), EditAccent);
        GUI.color = previousColor;
        TooltipHandler.TipRegion(TopCropHandle(layout), "PortraitOverlay.Edit.CropTooltip".Translate());
        TooltipHandler.TipRegion(BottomCropHandle(layout), "PortraitOverlay.Edit.CropTooltip".Translate());
        TooltipHandler.TipRegion(ResizeHandle(layout), "PortraitOverlay.Edit.ResizeTooltip".Translate());

        var previousFont = Text.Font;
        var previousAnchor = Text.Anchor;
        Text.Font = GameFont.Tiny;

        var bar = ScopeBarRect(layout);
        Widgets.DrawBoxSolid(bar, BarBackground);
        ScopeButtonEnd = DrawScopeButton(bar, pawn, settings);

        var readout = ReadoutRect(layout);
        Widgets.DrawBoxSolid(new Rect(readout.x, readout.y, readout.width, EditToolbarRect(layout).yMax - readout.y), BarBackground);
        DrawPresetRow(PresetRowRect(layout), pawn, settings);
        Text.Anchor = TextAnchor.MiddleCenter;
        Widgets.Label(readout, ScopeReadout(pawn, settings));
        TooltipHandler.TipRegion(readout, "PortraitOverlay.Edit.ReadoutTooltip".Translate());

        // The hint would cover the right-click menu, which opens right where the mouse is.
        if (Find.WindowStack.FloatMenu == null)
        {
            var portraitArea = new Rect(layout.Visible.x, bar.yMax, layout.Visible.width, Mathf.Max(0f, layout.Visible.yMax - bar.yMax));
            TooltipHandler.TipRegion(portraitArea, "PortraitOverlay.Edit.Hint".Translate());
        }

        Text.Anchor = previousAnchor;
        Text.Font = previousFont;
    }

    private void DrawPresetRow(Rect row, Pawn pawn, PortraitOverlaySettings settings)
    {
        if (row.width <= 0f)
        {
            return;
        }

        var usable = PortraitAdjustments.IsAvailable(editScope, pawn);
        var width = row.width / 3f;
        DrawPresetButton(new Rect(row.x, row.y, width, row.height), PortraitPreset.FullBody, "PortraitOverlay.Edit.PresetFull", usable, pawn, settings);
        DrawPresetButton(new Rect(row.x + width, row.y, width, row.height), PortraitPreset.UpperBody, "PortraitOverlay.Edit.PresetUpper", usable, pawn, settings);
        DrawPresetButton(new Rect(row.x + (2f * width), row.y, width, row.height), PortraitPreset.Face, "PortraitOverlay.Edit.PresetFace", usable, pawn, settings);
    }

    private void DrawPresetButton(Rect rect, PortraitPreset preset, string labelKey, bool usable, Pawn pawn, PortraitOverlaySettings settings)
    {
        var button = rect.ContractedBy(1f);
        if (Widgets.ButtonText(button, labelKey.Translate(), true, true, usable) && usable)
        {
            PushUndo(pawn, settings);
            PortraitAdjustments.ApplyPreset(preset, editScope, pawn, settings);
            if (editScope == AdjustmentScope.Race)
            {
                PortraitOverlayMod.SaveSettings();
            }
        }

        TooltipHandler.TipRegion(button, (editScope == AdjustmentScope.Global ? "PortraitOverlay.Edit.PresetGlobalTooltip" : "PortraitOverlay.Edit.PresetTooltip").Translate());
    }

    // The scope button: shows where edits are stored and opens the scope menu. Returns its right edge.
    private float DrawScopeButton(Rect bar, Pawn pawn, PortraitOverlaySettings settings)
    {
        if (!PortraitAdjustments.IsAvailable(editScope, pawn))
        {
            editScope = AdjustmentScope.Category;
        }

        var label = "PortraitOverlay.Edit.ScopeButton".Translate(PortraitAdjustments.ScopeLabel(editScope, pawn)).ToString();
        if (PortraitAdjustments.Has(editScope, pawn, settings))
        {
            label = "• " + label;
        }

        label += " ▾";
        var width = Mathf.Min(Text.CalcSize(label).x + 12f, Mathf.Max(0f, bar.width - 8f));
        var button = new Rect(bar.x + 4f, bar.y + 3f, width, bar.height - 6f);
        var previousColor = GUI.color;
        Widgets.DrawBoxSolid(button, EditAccent);
        GUI.color = new Color(0.15f, 0.08f, 0f);
        Text.Anchor = TextAnchor.MiddleCenter;
        Widgets.Label(button, label);
        GUI.color = previousColor;
        TooltipHandler.TipRegion(button, "PortraitOverlay.Edit.ScopeTooltip".Translate());
        if (Widgets.ButtonInvisible(button))
        {
            Find.WindowStack.Add(new FloatMenu(BuildScopeMenu(pawn, settings)));
        }

        return button.xMax + 4f;
    }

    private string ScopeReadout(Pawn pawn, PortraitOverlaySettings settings)
    {
        var summary = PortraitAdjustments.Get(editScope, pawn, settings)?.Summary() ?? SavedAdjustment.Format(1f, 0f, 0f, 1f, 1f);
        return editScope == AdjustmentScope.Global && settings.UseGlobalPreset
            ? "PortraitOverlay.Edit.ReadoutPreset".Translate(PortraitAdjustments.PresetLabel(settings.GlobalPreset), summary)
            : summary;
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
