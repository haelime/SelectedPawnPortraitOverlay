using UnityEngine;
using Verse;

namespace SelectedPawnPortraitOverlay;

public sealed class SelectedPawnPortraitOverlayComponent : GameComponent
{
    private bool isDragging;
    private Vector2 dragOffset;

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
            return;
        }

        var pawn = GetSelectedPawn();
        if (pawn == null)
        {
            isDragging = false;
            return;
        }

        settings.ClampValues();
        var rect = BuildPanelRect(settings);
        HandleDragging(rect, settings);
        DrawPanel(rect, pawn, settings);
    }

    private static Pawn GetSelectedPawn()
    {
        if (Find.Selector == null)
        {
            return null;
        }

        var pawn = Find.Selector.SingleSelectedThing as Pawn;
        return ShouldDisplayPawn(pawn) ? pawn : null;
    }

    private static bool ShouldDisplayPawn(Pawn pawn)
    {
        if (pawn == null || PortraitOverlayMod.Settings == null)
        {
            return false;
        }

        var settings = PortraitOverlayMod.Settings;
        if (!settings.ShowAnimals && pawn.RaceProps?.Animal == true)
        {
            return false;
        }

        if (!settings.ShowMechanoids && pawn.RaceProps?.IsMechanoid == true)
        {
            return false;
        }

        return true;
    }

    private static Rect BuildPanelRect(PortraitOverlaySettings settings)
    {
        var width = Mathf.Min(settings.PanelWidth, UI.screenWidth - 8f);
        var height = Mathf.Min(settings.PanelHeight, UI.screenHeight - 8f);
        var x = Mathf.Clamp(settings.PanelX, 0f, Mathf.Max(0f, UI.screenWidth - width));
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
            }

            return;
        }

        switch (currentEvent.rawType)
        {
            case EventType.MouseDown when currentEvent.button == 0 && rect.Contains(currentEvent.mousePosition):
                isDragging = true;
                dragOffset = currentEvent.mousePosition - rect.position;
                currentEvent.Use();
                break;
            case EventType.MouseDrag when isDragging:
                settings.PanelX = currentEvent.mousePosition.x - dragOffset.x;
                settings.PanelY = currentEvent.mousePosition.y - dragOffset.y;
                settings.ClampValues();
                currentEvent.Use();
                break;
            case EventType.MouseUp:
                if (isDragging)
                {
                    PortraitOverlayMod.SaveSettings();
                }

                isDragging = false;
                break;
        }
    }

    private static void DrawPanel(Rect rect, Pawn pawn, PortraitOverlaySettings settings)
    {
        if (settings.ShowBackground)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.08f, 0.08f, 0.08f, settings.BackgroundAlpha));
        }

        GenUI.AbsorbClicksInRect(rect);
        TooltipHandler.TipRegion(rect, "PortraitOverlay.Overlay.Tooltip".Translate());

        var contentRect = rect.ContractedBy(8f);
        var titleRect = new Rect(contentRect.x, contentRect.y, contentRect.width, 24f);
        var portraitRect = new Rect(contentRect.x, titleRect.yMax + 6f, contentRect.width, contentRect.height - 30f);

        var previousAnchor = Text.Anchor;
        var previousFont = Text.Font;
        Text.Font = GameFont.Small;
        Text.Anchor = TextAnchor.UpperLeft;
        Widgets.Label(titleRect, "PortraitOverlay.Overlay.Title".Translate(pawn.Name?.ToStringShort ?? pawn.LabelShortCap));
        Text.Anchor = previousAnchor;
        Text.Font = previousFont;

        var portrait = PortraitRenderCache.GetPortrait(pawn, portraitRect.size, settings);
        if (portrait == null || portrait == BaseContent.BadTex)
        {
            DrawFallbackLabel(portraitRect);
            return;
        }

        GUI.DrawTexture(portraitRect, portrait, ScaleMode.ScaleToFit, true);
    }

    private static void DrawFallbackLabel(Rect rect)
    {
        var previousAnchor = Text.Anchor;
        Text.Anchor = TextAnchor.MiddleCenter;
        Widgets.Label(rect, "PortraitOverlay.Overlay.NoPortrait".Translate());
        Text.Anchor = previousAnchor;
    }
}
