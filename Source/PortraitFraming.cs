using System;

namespace SelectedPawnPortraitOverlay;

public readonly struct PortraitFrame
{
    public PortraitFrame(float zoom, float offsetX, float offsetY, float offsetZ)
    {
        Zoom = zoom;
        OffsetX = offsetX;
        OffsetY = offsetY;
        OffsetZ = offsetZ;
    }

    public float Zoom { get; }

    // Screen-horizontal camera shift in world units; positive moves the view right.
    public float OffsetX { get; }

    // Camera altitude shift. The portrait camera is orthographic, so this does not move the image.
    public float OffsetY { get; }

    // Screen-vertical camera shift in world units; positive moves the view up the pawn.
    public float OffsetZ { get; }
}

// Area a pawn actually covers when drawn, in pawn-local world units (x right, z up, origin at the pawn).
public readonly struct PortraitBounds
{
    public PortraitBounds(float minX, float maxX, float minZ, float maxZ)
    {
        MinX = minX;
        MaxX = maxX;
        MinZ = minZ;
        MaxZ = maxZ;
    }

    public float MinX { get; }
    public float MaxX { get; }
    public float MinZ { get; }
    public float MaxZ { get; }

    public float Width => MaxX - MinX;
    public float Height => MaxZ - MinZ;
    public float CenterX => (MinX + MaxX) / 2f;
    public float CenterZ => (MinZ + MaxZ) / 2f;

    // The height a frame of this aspect (width / height) needs to show the whole box.
    public float FitHeight(float aspect)
    {
        return Math.Max(Height, Width / aspect);
    }

    public PortraitBounds Union(PortraitBounds other)
    {
        return new PortraitBounds(
            Math.Min(MinX, other.MinX),
            Math.Max(MaxX, other.MaxX),
            Math.Min(MinZ, other.MinZ),
            Math.Max(MaxZ, other.MaxZ));
    }

    // West-facing graphics are east-facing ones drawn flipped.
    public PortraitBounds MirroredX()
    {
        return new PortraitBounds(-MaxX, -MinX, MinZ, MaxZ);
    }

    public static PortraitBounds Lerp(PortraitBounds from, PortraitBounds to, float t)
    {
        return new PortraitBounds(
            from.MinX + ((to.MinX - from.MinX) * t),
            from.MaxX + ((to.MaxX - from.MaxX) * t),
            from.MinZ + ((to.MinZ - from.MinZ) * t),
            from.MaxZ + ((to.MaxZ - from.MaxZ) * t));
    }

    // True when the change between two measurements is too small to be worth reframing for.
    public static bool NearlyEqual(PortraitBounds a, PortraitBounds b, float tolerance)
    {
        var size = Math.Max(Math.Max(a.Width, a.Height), 1e-4f);
        return Math.Abs(a.Width - b.Width) <= a.Width * tolerance
            && Math.Abs(a.Height - b.Height) <= a.Height * tolerance
            && Math.Abs(a.CenterX - b.CenterX) <= size * tolerance
            && Math.Abs(a.CenterZ - b.CenterZ) <= size * tolerance;
    }
}

// How the portrait frames one pawn relative to human framing: Scale magnifies (1 = human size), and
// ShiftX/ShiftZ move the view center in world units so the pawn's drawn area sits where a human's would.
public readonly struct PortraitFit
{
    public PortraitFit(float scale, float shiftX = 0f, float shiftZ = 0f)
    {
        Scale = scale;
        ShiftX = shiftX;
        ShiftZ = shiftZ;
    }

    public float Scale { get; }
    public float ShiftX { get; }
    public float ShiftZ { get; }
}

// A manual correction applied on top of the automatic framing: Zoom multiplies, offsets are world units.
public readonly struct PortraitAdjustment
{
    public static readonly PortraitAdjustment Identity = new(1f, 0f, 0f);

    public PortraitAdjustment(float zoom, float offsetX, float offsetZ)
    {
        Zoom = zoom;
        OffsetX = offsetX;
        OffsetZ = offsetZ;
    }

    public float Zoom { get; }
    public float OffsetX { get; }
    public float OffsetZ { get; }
}

// Portrait camera framing. Kept free of RimWorld and Unity types so Tools/PortraitPreview compiles
// this same file and previews exactly what the mod renders.
public static class PortraitFraming
{
    public const float MinZoom = 0.1f;

    // Humanlike body graphics are drawn 1.5 world units wide; the drawSize fallback scales other races against that.
    public const float ReferenceDrawSize = 1.5f;
    public const float MinAutoFitScale = 0.25f;
    public const float MaxAutoFitScale = 2f;

    // Measurement pass: a square render at this zoom shows 6.67 world units, enough for the largest vanilla pawns.
    public const float MeasureZoom = 0.3f;
    public const int MeasureTextureSize = 512;
    // Pixels at or below this alpha (0-255) count as empty. Some vanilla textures (the cowboy hat, for one)
    // carry faint compression noise up to about alpha 32 far outside the visible art; real edges lose under a pixel.
    public const byte MeasureAlphaThreshold = 48;

    // Drawn area of the reference human (naked male body, Male_Average_Normal head at z = 0.34), measured
    // by Tools/PortraitPreview with the same measurement settings.
    public const float ReferenceFitWidth = 0.625f;
    public const float ReferenceFitHeight = 1.158854f;
    public const float ReferenceFitCenterZ = 0.05859375f;

    // Humanlike pawns only shrink to fit, and only once they are this much larger than the reference, so
    // ordinary hair and clothes keep today's colonist framing while big hats and wings stop being cut off.
    public const float HumanlikeFitTolerance = 1.2f;

    // Texture pixels per world unit of the human body graphic (128 px drawn 1.5 units tall).
    public const float ReferenceTexelDensity = 128f / 1.5f;

    // Auto-fit never magnifies a pawn's texels more than this many times as much as a human's, so small
    // animals with low-resolution graphics (64 px hares, chinchillas, bluebirds) do not turn to mush.
    public const float MaxTexelMagnification = 1.5f;

    // Magnification cap when a pawn's texture size is unknown.
    public const float UnknownTexelDensityMaxScale = 1.25f;

    private const float FaceEmphasisVerticalOffset = 0.3f;
    private const float FaceEmphasisDepthOffset = 0.7f;
    private const float FaceEmphasisZoomFactor = 0.25f;

    // Head room kept visible above the head center when the camera is held back at the head.
    private const float HeadFrameMargin = 0.2f;

    // Fallback when a pawn could not be measured. Races larger than a human shrink all the way to human
    // size, since anything bigger overflows the panel. Smaller races grow only by the square root.
    public static float AutoFitScale(float bodyDrawSize)
    {
        if (!(bodyDrawSize > 0f))
        {
            return 1f;
        }

        var ratio = ReferenceDrawSize / bodyDrawSize;
        var scale = ratio < 1f ? ratio : (float)Math.Sqrt(ratio);
        return Clamp(scale, MinAutoFitScale, MaxAutoFitScale);
    }

    // Largest auto-fit scale before a pawn's texels look coarser than MaxTexelMagnification times a human's.
    // texelDensity is body texture height in pixels / body drawSize.y, or null when unknown. Never below 1:
    // the cap only stops extra magnification, it does not shrink pawns that already look coarse at human framing.
    public static float MaxScaleForTexelDensity(float? texelDensity)
    {
        return texelDensity > 0f
            ? Math.Max(1f, MaxTexelMagnification * texelDensity.Value / ReferenceTexelDensity)
            : UnknownTexelDensityMaxScale;
    }

    // drawSize fallback for a non-humanlike pawn that could not be measured, with the same texel cap.
    public static float AutoFitScale(float bodyDrawSize, float? texelDensity)
    {
        return Math.Min(AutoFitScale(bodyDrawSize), MaxScaleForTexelDensity(texelDensity));
    }

    // Converts the inclusive pixel box of a measurement render (rows counted from the top) to world units.
    public static PortraitBounds BoundsFromPixels(
        int minColumn,
        int maxColumn,
        int minRowFromTop,
        int maxRowFromTop,
        int textureWidth,
        int textureHeight,
        float zoom)
    {
        var halfHeight = 1f / zoom;
        var halfWidth = halfHeight * textureWidth / textureHeight;
        var unitsPerColumn = 2f * halfWidth / textureWidth;
        var unitsPerRow = 2f * halfHeight / textureHeight;
        return new PortraitBounds(
            -halfWidth + (minColumn * unitsPerColumn),
            -halfWidth + ((maxColumn + 1) * unitsPerColumn),
            halfHeight - ((maxRowFromTop + 1) * unitsPerRow),
            halfHeight - (minRowFromTop * unitsPerRow));
    }

    // Scale that gives the measured pawn the frame room the reference human gets. aspect is the portrait
    // rect's width / height; the result does not depend on the camera zoom setting. texelDensity caps how
    // far a non-humanlike pawn is magnified (see MaxScaleForTexelDensity); humanlike pawns only shrink.
    public static float FitScale(PortraitBounds measured, float aspect, bool humanlike, float? texelDensity)
    {
        if (!(aspect > 0f))
        {
            return 1f;
        }

        var reference = Math.Max(ReferenceFitHeight, ReferenceFitWidth / aspect);
        var needed = measured.FitHeight(aspect);
        if (!(needed > 0f))
        {
            return 1f;
        }

        var scale = reference / needed;
        return humanlike
            ? Math.Min(1f, scale * HumanlikeFitTolerance)
            : Math.Min(Clamp(scale, MinAutoFitScale, MaxAutoFitScale), MaxScaleForTexelDensity(texelDensity));
    }

    // Framing for a measured pawn. scaleOverride (a per-race setting) replaces the fitted scale and is not
    // texel-capped, so users can still zoom further on purpose. Non-humanlike pawns are also re-centered on
    // their drawn area; humanlike pawns keep the colonist framing.
    public static PortraitFit Fit(
        PortraitBounds measured,
        float aspect,
        bool humanlike,
        float? texelDensity,
        float? scaleOverride = null)
    {
        var scale = scaleOverride ?? FitScale(measured, aspect, humanlike, texelDensity);
        if (!(scale > 0f))
        {
            scale = 1f;
        }

        return humanlike
            ? new PortraitFit(scale)
            : new PortraitFit(scale, measured.CenterX, measured.CenterZ - (ReferenceFitCenterZ / scale));
    }

    // raceScale magnifies the pawn relative to human framing (1 = human). headZ is the humanlike head
    // center height in world units, or null for pawns without a humanlike head.
    public static PortraitFrame Compute(float cameraZoom, float faceEmphasis, float raceScale, float? headZ)
    {
        return Compute(cameraZoom, faceEmphasis, new PortraitFit(raceScale), headZ, PortraitAdjustment.Identity);
    }

    public static PortraitFrame Compute(
        float cameraZoom,
        float faceEmphasis,
        PortraitFit fit,
        float? headZ,
        PortraitAdjustment adjustment)
    {
        var raceScale = fit.Scale > 0f ? fit.Scale : 1f;
        var emphasis = (float)Math.Sqrt(Math.Max(0f, faceEmphasis));

        // Offsets are world distances; dividing by raceScale makes a scaled pawn frame like a human does.
        var offsetY = emphasis * FaceEmphasisVerticalOffset / raceScale;
        var offsetZ = emphasis * FaceEmphasisDepthOffset / raceScale;
        var zoom = Math.Max((cameraZoom + (emphasis * FaceEmphasisZoomFactor)) * raceScale, MinZoom);

        if (headZ.HasValue)
        {
            // The orthographic half-height is 1 / zoom, but the face emphasis offset is a fixed distance.
            // Past the recommended zoom range it would carry the view above the head, so cap it there.
            // Within the recommended range the cap never applies to adults.
            var maxOffsetZ = headZ.Value + Math.Max(0f, (1f / zoom) - (HeadFrameMargin / raceScale));
            offsetZ = Math.Min(offsetZ, maxOffsetZ);
        }

        var offsetX = fit.ShiftX;
        offsetZ += fit.ShiftZ;

        // The manual adjustment is applied last, so it is never undone by the head cap.
        if (adjustment.Zoom > 0f)
        {
            zoom = Math.Max(zoom * adjustment.Zoom, MinZoom);
        }

        offsetX += adjustment.OffsetX;
        offsetZ += adjustment.OffsetZ;
        return new PortraitFrame(zoom, offsetX, offsetY, offsetZ);
    }

    // Camera framing a preset aims for, in the pawn's local world units: Zoom is the final camera zoom and
    // (CenterX, CenterZ) the view center. bounds is the pawn's drawn area; headZ is set for humanlike pawns.
    public static PortraitFrame PresetFrame(PortraitPreset preset, PortraitBounds bounds, float? headZ, float aspect)
    {
        if (!(aspect > 0f))
        {
            aspect = 1f;
        }

        float minZ;
        float maxZ;
        float width;
        switch (preset)
        {
            case PortraitPreset.UpperBody when headZ.HasValue:
                // RimWorld heads are about half a pawn's height, so a fraction of the body would be all head.
                // Frame from above the hair down to about the waist instead.
                maxZ = Math.Max(bounds.MaxZ, headZ.Value + HeadVisualHalfHeight);
                minZ = headZ.Value - UpperBodyBelowHead;
                width = bounds.Width;
                break;
            case PortraitPreset.UpperBody:
                maxZ = bounds.MaxZ;
                minZ = maxZ - (UpperBodyFraction * bounds.Height);
                width = bounds.Width;
                break;
            case PortraitPreset.Face when headZ.HasValue:
                // Head center a little above the middle, with about one and a half head heights of room.
                var viewHeight = 2f * HeadVisualHalfHeight * FaceViewHeads;
                return new PortraitFrame(2f / viewHeight, 0f, 0f, headZ.Value - (FaceHeadLift * viewHeight));
            case PortraitPreset.Face:
                minZ = bounds.MaxZ - (FaceFraction * bounds.Height);
                maxZ = bounds.MaxZ;
                width = Math.Min(bounds.Width, (maxZ - minZ) * 1.4f);
                break;
            default:
                minZ = bounds.MinZ;
                maxZ = bounds.MaxZ;
                width = bounds.Width;
                break;
        }

        var height = Math.Max(maxZ - minZ, width / aspect) * PresetMargin;
        if (!(height > 0f))
        {
            height = 2f;
        }

        return new PortraitFrame(Math.Max(2f / height, MinZoom), bounds.CenterX, 0f, (minZ + maxZ) / 2f);
    }

    // The correction that turns a frame computed without this scope's adjustment into the target frame.
    public static PortraitAdjustment AdjustmentToReach(PortraitFrame withoutScope, PortraitFrame target)
    {
        var zoom = withoutScope.Zoom > 0f ? target.Zoom / withoutScope.Zoom : 1f;
        return new PortraitAdjustment(zoom, target.OffsetX - withoutScope.OffsetX, target.OffsetZ - withoutScope.OffsetZ);
    }

    // Drawn area assumed when a pawn has no measurement: the reference human, or the graphic's drawSize.
    public static PortraitBounds FallbackBounds(bool humanlike, float drawSizeX, float drawSizeY)
    {
        if (humanlike || !(drawSizeX > 0f) || !(drawSizeY > 0f))
        {
            return new PortraitBounds(
                -ReferenceFitWidth / 2f,
                ReferenceFitWidth / 2f,
                ReferenceFitCenterZ - (ReferenceFitHeight / 2f),
                ReferenceFitCenterZ + (ReferenceFitHeight / 2f));
        }

        // Graphics leave transparent padding; most of the art sits in the middle 70% of the quad.
        var halfX = drawSizeX * 0.35f;
        var halfZ = drawSizeY * 0.35f;
        return new PortraitBounds(-halfX, halfX, -halfZ, halfZ);
    }

    private const float PresetMargin = 1.08f;
    private const float UpperBodyFraction = 0.6f;
    private const float FaceFraction = 0.35f;
    // A humanlike head with hair spans about 0.5 world units around its center.
    private const float HeadVisualHalfHeight = 0.25f;
    private const float UpperBodyBelowHead = 0.6f;
    private const float FaceViewHeads = 1.5f;
    private const float FaceHeadLift = 0.03f;

    private static float Clamp(float value, float min, float max)
    {
        return Math.Min(Math.Max(value, min), max);
    }
}

public enum PortraitPreset
{
    FullBody,
    UpperBody,
    Face
}
