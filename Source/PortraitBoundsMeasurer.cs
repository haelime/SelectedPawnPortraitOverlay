using System;
using System.Reflection;
using RimWorld;
using UnityEngine;
using Verse;

namespace SelectedPawnPortraitOverlay;

// Renders a pawn once into a private texture and reads back the area it actually covers, so framing
// follows the drawn pawn (empty space in its graphic, hats, wings, modded races) rather than drawSize.
// Holds Unity texture fields, which RimWorld expects to be initialized on the main thread at startup.
[StaticConstructorOnStartup]
public static class PortraitBoundsMeasurer
{
    private const int MeasureFailureWarningId = 18467234;
    private const int TextureSize = PortraitFraming.MeasureTextureSize;

    // Vanilla portraits draw downed pawns lying down: PortraitsCache.PortraitParams.RenderPortrait.
    private const float DownedAngle = 85f;
    private const float DownedPositionOffset = -0.18f;

    // One-time check of the readback row order: moving the camera this far up moves the pawn down ~38 rows.
    private const float RowOrderProbeShift = 0.5f;
    private const int RowOrderProbeMinRows = 10;

    private static readonly MethodInfo RenderPawnMethod = ResolveRenderPawnMethod();
    private static RenderTexture renderTexture;
    private static Texture2D readbackTexture;
    private static bool disabled;

    // Unity returns readback rows bottom-up. RimWorld 1.7 emulates Unity on its own engine, so the order
    // is probed once per session instead of assumed.
    private static bool? rowsBottomUp;

    private readonly struct PixelBox
    {
        public PixelBox(int minColumn, int maxColumn, int minRow, int maxRow)
        {
            MinColumn = minColumn;
            MaxColumn = maxColumn;
            MinRow = minRow;
            MaxRow = maxRow;
        }

        public int MinColumn { get; }
        public int MaxColumn { get; }

        // Rows in readback order, which is bottom-up or top-down depending on rowsBottomUp.
        public int MinRow { get; }
        public int MaxRow { get; }
    }

    // Null when the pawn cannot be measured; callers then fall back to drawSize framing.
    public static PortraitBounds? Measure(Pawn pawn, Rot4 rotation, PortraitOverlaySettings settings)
    {
        if (disabled || RenderPawnMethod == null || pawn == null || settings == null)
        {
            return null;
        }

        try
        {
            // Weapons stick far out of the body and only show while drafted, so they never count.
            return ModCompatibility.WithPortraitPreferences(
                pawn,
                settings.RenderHeadgear,
                ShowWeaponsUnderPortraitMode.Never,
                () => MeasureNow(pawn, rotation, settings));
        }
        catch (Exception exception)
        {
            // A broken readback would fail the same way every time; stop trying for this session.
            disabled = true;
            Log.WarningOnce(
                $"Selected Pawn Portrait Overlay could not measure portraits and will auto-fit by graphic size instead. {exception.GetType().Name}: {exception.Message}",
                MeasureFailureWarningId);
            return null;
        }
    }

    private static PortraitBounds? MeasureNow(Pawn pawn, Rot4 rotation, PortraitOverlaySettings settings)
    {
        EnsureTextures();
        var box = RenderAndScan(pawn, rotation, settings, Vector3.zero);
        if (!box.HasValue)
        {
            return null;
        }

        rowsBottomUp ??= ProbeRowOrder(pawn, rotation, settings, box.Value);
        var minRowFromTop = rowsBottomUp.Value ? TextureSize - 1 - box.Value.MaxRow : box.Value.MinRow;
        var maxRowFromTop = rowsBottomUp.Value ? TextureSize - 1 - box.Value.MinRow : box.Value.MaxRow;
        return PortraitFraming.BoundsFromPixels(
            box.Value.MinColumn,
            box.Value.MaxColumn,
            minRowFromTop,
            maxRowFromTop,
            TextureSize,
            TextureSize,
            PortraitFraming.MeasureZoom);
    }

    // Raising the camera moves the pawn down the image; bottom-up rows then get smaller.
    private static bool ProbeRowOrder(Pawn pawn, Rot4 rotation, PortraitOverlaySettings settings, PixelBox centered)
    {
        var raised = RenderAndScan(pawn, rotation, settings, new Vector3(0f, 0f, RowOrderProbeShift));
        if (!raised.HasValue)
        {
            return true;
        }

        var rowShift = (raised.Value.MinRow + raised.Value.MaxRow) - (centered.MinRow + centered.MaxRow);
        return Math.Abs(rowShift) < RowOrderProbeMinRows * 2 || rowShift < 0;
    }

    private static PixelBox? RenderAndScan(Pawn pawn, Rot4 rotation, PortraitOverlaySettings settings, Vector3 cameraOffset)
    {
        var previousActive = RenderTexture.active;
        try
        {
            RenderTexture.active = renderTexture;
            GL.Clear(true, true, Color.clear);
            RenderPawn(pawn, rotation, settings, cameraOffset);
            RenderTexture.active = renderTexture;
            readbackTexture.ReadPixels(new Rect(0f, 0f, TextureSize, TextureSize), 0, 0);
            readbackTexture.Apply(false);
        }
        finally
        {
            RenderTexture.active = previousActive;
        }

        return Scan(readbackTexture.GetPixels32());
    }

    private static PixelBox? Scan(Color32[] pixels)
    {
        if (pixels == null || pixels.Length < TextureSize * TextureSize)
        {
            return null;
        }

        int minColumn = TextureSize, maxColumn = -1, minRow = TextureSize, maxRow = -1;
        for (var row = 0; row < TextureSize; row++)
        {
            var rowStart = row * TextureSize;
            for (var column = 0; column < TextureSize; column++)
            {
                if (pixels[rowStart + column].a <= PortraitFraming.MeasureAlphaThreshold)
                {
                    continue;
                }

                minColumn = Math.Min(minColumn, column);
                maxColumn = Math.Max(maxColumn, column);
                minRow = Math.Min(minRow, row);
                maxRow = Math.Max(maxRow, row);
            }
        }

        // Nothing drawn means the readback did not work. Everything covered means something other than
        // the pawn filled the texture; neither describes the pawn.
        var empty = maxColumn < 0;
        var full = minColumn == 0 && minRow == 0 && maxColumn == TextureSize - 1 && maxRow == TextureSize - 1;
        return empty || full ? null : new PixelBox(minColumn, maxColumn, minRow, maxRow);
    }

    private static void EnsureTextures()
    {
        // Kept for the whole session and never destroyed: RimWorld 1.7 frees textures immediately.
        if (renderTexture == null)
        {
            renderTexture = new RenderTexture(TextureSize, TextureSize, 24)
            {
                name = "SelectedPawnPortraitOverlay_Measure"
            };
        }

        if (!renderTexture.IsCreated())
        {
            renderTexture.Create();
        }

        readbackTexture ??= new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false);
    }

    private static void RenderPawn(Pawn pawn, Rot4 rotation, PortraitOverlaySettings settings, Vector3 cameraOffset)
    {
        var downed = IsDrawnDowned(pawn);
        var parameters = RenderPawnMethod.GetParameters();
        var args = new object[parameters.Length];
        for (var i = 0; i < parameters.Length; i++)
        {
            var parameter = parameters[i];
            args[i] = parameter.Name switch
            {
                "pawn" => pawn,
                "renderTexture" => renderTexture,
                "cameraOffset" => cameraOffset,
                "cameraZoom" => PortraitFraming.MeasureZoom,
                "angle" => downed ? DownedAngle : 0f,
                "rotation" => rotation,
                "renderHead" => pawn.health?.hediffSet?.HasHead ?? true,
                "renderHeadgear" => settings.RenderHeadgear,
                "renderClothes" => settings.RenderApparel,
                "portrait" => true,
                "positionOffset" => downed ? new Vector3(DownedPositionOffset, 0f, DownedPositionOffset) : Vector3.zero,
                "stylingStation" => false,
                _ => DefaultArgument(parameter)
            };
        }

        RenderPawnMethod.Invoke(Find.PawnCacheRenderer, args);
    }

    private static bool IsDrawnDowned(Pawn pawn)
    {
        return pawn.health != null
            && pawn.health.State != PawnHealthState.Mobile
            && pawn.ageTracker?.CurLifeStage?.alwaysDowned != true;
    }

    private static object DefaultArgument(ParameterInfo parameter)
    {
        var type = parameter.ParameterType;
        if (parameter.HasDefaultValue && parameter.DefaultValue != null && parameter.DefaultValue != DBNull.Value)
        {
            return parameter.DefaultValue;
        }

        return type.IsValueType ? Activator.CreateInstance(type) : null;
    }

    private static MethodInfo ResolveRenderPawnMethod()
    {
        foreach (var method in typeof(PawnCacheRenderer).GetMethods(BindingFlags.Public | BindingFlags.Instance))
        {
            if (method.Name != "RenderPawn")
            {
                continue;
            }

            var parameters = method.GetParameters();
            if (parameters.Length >= 2
                && parameters[0].ParameterType == typeof(Pawn)
                && parameters[1].ParameterType == typeof(RenderTexture))
            {
                return method;
            }
        }

        return null;
    }
}
