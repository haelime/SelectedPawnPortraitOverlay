using System.Globalization;
using UnityEngine;
using Verse;

namespace SelectedPawnPortraitOverlay;

// Exact numeric entry for one scope's correction, opened by double-clicking the edit mode readout.
public sealed class Dialog_PortraitAdjustment : Window
{
    private const float RowHeight = 30f;
    private const float FieldWidth = 110f;

    private readonly AdjustmentScope scope;
    private readonly Pawn pawn;
    private readonly System.Action beforeChange;
    private float zoom;
    private float offsetX;
    private float offsetZ;
    private string zoomBuffer;
    private string offsetXBuffer;
    private string offsetZBuffer;

    public Dialog_PortraitAdjustment(AdjustmentScope scope, Pawn pawn, System.Action beforeChange)
    {
        this.scope = scope;
        this.pawn = pawn;
        this.beforeChange = beforeChange;
        doCloseX = true;
        doCloseButton = false;
        absorbInputAroundWindow = true;
        closeOnClickedOutside = true;
        forcePause = false;

        var settings = PortraitOverlayMod.Settings;
        var saved = PortraitAdjustments.Get(scope, pawn, settings);
        zoom = saved?.Zoom ?? 1f;
        offsetX = saved?.OffsetX ?? 0f;
        offsetZ = saved?.OffsetZ ?? 0f;

        zoomBuffer = zoom.ToString("F2", CultureInfo.InvariantCulture);
        offsetXBuffer = offsetX.ToString("F2", CultureInfo.InvariantCulture);
        offsetZBuffer = offsetZ.ToString("F2", CultureInfo.InvariantCulture);
    }

    public override Vector2 InitialSize => new(380f, 230f);

    public override void DoWindowContents(Rect inRect)
    {
        var settings = PortraitOverlayMod.Settings;
        var scopeLabel = scope == AdjustmentScope.Pawn
            ? pawn.LabelShortCap.ToString()
            : PortraitAdjustments.ScopeLabel(scope, pawn);

        Text.Font = GameFont.Medium;
        Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 34f), "PortraitOverlay.Adjust.Title".Translate(scopeLabel));
        Text.Font = GameFont.Small;

        var y = inRect.y + 42f;
        DrawField(ref y, inRect, "PortraitOverlay.Adjust.Zoom".Translate(), ref zoom, ref zoomBuffer, SavedAdjustment.MinZoom, SavedAdjustment.MaxZoom);
        DrawField(ref y, inRect, "PortraitOverlay.Adjust.OffsetX".Translate(), ref offsetX, ref offsetXBuffer, -10f, 10f);
        DrawField(ref y, inRect, "PortraitOverlay.Adjust.OffsetZ".Translate(), ref offsetZ, ref offsetZBuffer, -10f, 10f);

        var buttonRect = new Rect(inRect.xMax - 120f, inRect.yMax - 34f, 120f, 34f);
        if (Widgets.ButtonText(buttonRect, "PortraitOverlay.Adjust.Apply".Translate()))
        {
            beforeChange?.Invoke();
            PortraitAdjustments.Set(scope, pawn, settings, new PortraitAdjustment(zoom, offsetX, offsetZ));

            if (scope != AdjustmentScope.Pawn)
            {
                PortraitOverlayMod.SaveSettings();
            }

            Close();
        }
    }

    private static void DrawField(ref float y, Rect inRect, string label, ref float value, ref string buffer, float min, float max)
    {
        var row = new Rect(inRect.x, y, inRect.width, RowHeight);
        Widgets.Label(row.LeftPartPixels(row.width - FieldWidth - 8f), label);
        Widgets.TextFieldNumeric(row.RightPartPixels(FieldWidth), ref value, ref buffer, min, max);
        y += RowHeight + 4f;
    }
}
