using System;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace SelectedPawnPortraitOverlay;

public sealed class SelectedPawnPortraitOverlayComponent : GameComponent
{
    private const int OverlayWindowId = 18467231;
    private const int UnsupportedVehiclePawnWarningId = 18467232;
    private const float DragStartThreshold = 5f;
    private const int OverlayGuiDepth = 100;

    private bool isDragging;
    private bool dragArmed;
    private Vector2 dragStartMousePosition;
    private Pawn lastDisplayedPawn;

    public SelectedPawnPortraitOverlayComponent(Game game)
    {
    }

    public override void GameComponentOnGUI()
    {
        base.GameComponentOnGUI();

        if (Current.ProgramState != ProgramState.Playing || PortraitOverlayMod.Settings == null)
        {
            return;
        }

        var settings = PortraitOverlayMod.Settings;
        if (!settings.Enabled)
        {
            isDragging = false;
            dragArmed = false;
            return;
        }

        if (WorldRendererUtility.WorldRendered)
        {
            isDragging = false;
            dragArmed = false;
            return;
        }

        var pawn = GetPawnToDisplay(settings);
        if (pawn == null)
        {
            isDragging = false;
            dragArmed = false;
            return;
        }

        settings.ClampValues();
        var rect = BuildPanelRect(settings);
        if (settings.AllowDragging)
        {
            Find.WindowStack.ImmediateWindow(
                OverlayWindowId,
                rect,
                WindowLayer.GameUI,
                delegate
                {
                    var localRect = new Rect(0f, 0f, rect.width, rect.height);
                    HandleDragging(localRect, settings);
                    DrawPanel(localRect, pawn, settings);
                },
                doBackground: false,
                absorbInputAroundWindow: false,
                0f);
            return;
        }

        isDragging = false;
        dragArmed = false;
        var previousDepth = GUI.depth;
        GUI.depth = OverlayGuiDepth;
        DrawPanel(rect, pawn, settings);
        GUI.depth = previousDepth;
    }

    private Pawn GetPawnToDisplay(PortraitOverlaySettings settings)
    {
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

    private void HandleDragging(Rect rect, PortraitOverlaySettings settings)
    {
        var currentEvent = Event.current;
        if (currentEvent == null || !settings.AllowDragging)
        {
            if (currentEvent != null && currentEvent.rawType == EventType.MouseUp)
            {
                isDragging = false;
                dragArmed = false;
            }

            return;
        }

        switch (currentEvent.rawType)
        {
            case EventType.MouseDown when currentEvent.button == 0 && rect.Contains(currentEvent.mousePosition):
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

    private static void DrawPanel(Rect rect, Pawn pawn, PortraitOverlaySettings settings)
    {
        var topCutPixels = rect.height * Mathf.Clamp01(settings.TopCutoff);
        var bottomCutPixels = rect.height * Mathf.Clamp01(settings.BottomCutoff);
        var visiblePanelRect = new Rect(
            rect.x,
            rect.y + topCutPixels,
            rect.width,
            Mathf.Max(0f, rect.height - topCutPixels - bottomCutPixels));
        var contentRect = rect.ContractedBy(8f);
        var titleRect = Rect.zero;
        var portraitRect = contentRect;

        if (settings.ShowName)
        {
            titleRect = new Rect(contentRect.x, contentRect.y, contentRect.width, 24f);
            portraitRect = new Rect(contentRect.x, titleRect.yMax + 6f, contentRect.width, contentRect.height - 30f);
        }

        if (settings.ShowName)
        {
            titleRect.y += topCutPixels;
        }

        portraitRect.y += topCutPixels;

        TooltipHandler.TipRegion(rect, "PortraitOverlay.Overlay.Tooltip".Translate());

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
            ? new Rect(titleRect.x - visiblePanelRect.x, titleRect.y - visiblePanelRect.y, titleRect.width, titleRect.height)
            : Rect.zero;
        var localPortraitRect = new Rect(
            portraitRect.x - visiblePanelRect.x,
            portraitRect.y - visiblePanelRect.y,
            portraitRect.width,
            portraitRect.height);

        if (settings.ShowName)
        {
            var previousAnchor = Text.Anchor;
            var previousFont = Text.Font;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            Widgets.Label(localTitleRect, "PortraitOverlay.Overlay.Title".Translate(pawn.Name?.ToStringShort ?? pawn.LabelShortCap));
            Text.Anchor = previousAnchor;
            Text.Font = previousFont;
        }

        var portrait = PortraitRenderCache.GetPortrait(pawn, portraitRect.size, settings);
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
