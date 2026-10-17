using SelectedPawnPortraitOverlay;

namespace PortraitPreview;

// A textured quad in pawn-local world units: center (X, Z) and size, like a RimWorld graphic mesh.
public sealed record Sprite(RgbaImage Image, float CenterX, float CenterZ, float SizeX, float SizeZ);

public sealed record PanelOptions(int Width, int Height, bool ShowName, float BackgroundAlpha);

public static class PreviewRenderer
{
    // Mirrors SelectedPawnPortraitOverlayComponent.DrawPanel: 8px padding, a 24px name row plus 6px gap.
    private const int Padding = 8;
    private const int NameRowHeight = 30;

    // Approximates the map under the panel so the translucent background reads like it does in game.
    private static readonly byte[] MapColor = { 128, 112, 84 };
    private static readonly byte[] PanelColor = { 20, 20, 20 };

    // Width / height of the portrait rect inside the panel, which is also the portrait texture's aspect.
    public static float PortraitAspect(PanelOptions panel)
    {
        var portraitWidth = panel.Width - (2 * Padding);
        var portraitHeight = panel.Height - (2 * Padding) - (panel.ShowName ? NameRowHeight : 0);
        return portraitHeight > 0 ? (float)portraitWidth / portraitHeight : 1f;
    }

    // Mirrors the mod's measurement pass: composite the sprites into a square texture at the measurement
    // zoom, find the box of pixels above the alpha threshold, and convert it with the mod's own function.
    public static PortraitBounds? MeasureBounds(IReadOnlyList<Sprite> sprites)
    {
        const int size = PortraitFraming.MeasureTextureSize;
        var half = 1f / PortraitFraming.MeasureZoom;
        int minColumn = size, maxColumn = -1, minRow = size, maxRow = -1;
        Span<float> color = stackalloc float[4];
        for (var row = 0; row < size; row++)
        {
            var worldZ = half - ((row + 0.5f) / size * 2f * half);
            for (var column = 0; column < size; column++)
            {
                var worldX = -half + ((column + 0.5f) / size * 2f * half);
                var transparency = 1f;
                foreach (var sprite in sprites)
                {
                    if (TrySample(sprite, worldX, worldZ, color))
                    {
                        transparency *= 1f - (color[3] / 255f);
                    }
                }

                if ((1f - transparency) * 255f <= PortraitFraming.MeasureAlphaThreshold)
                {
                    continue;
                }

                minColumn = Math.Min(minColumn, column);
                maxColumn = Math.Max(maxColumn, column);
                minRow = Math.Min(minRow, row);
                maxRow = Math.Max(maxRow, row);
            }
        }

        return maxColumn < 0
            ? null
            : PortraitFraming.BoundsFromPixels(minColumn, maxColumn, minRow, maxRow, size, size, PortraitFraming.MeasureZoom);
    }

    public static RgbaImage RenderPanel(IReadOnlyList<Sprite> sprites, PortraitFrame frame, PanelOptions panel)
    {
        var pixels = new byte[panel.Width * panel.Height * 4];
        for (var i = 0; i < panel.Width * panel.Height; i++)
        {
            for (var c = 0; c < 3; c++)
            {
                pixels[(i * 4) + c] = (byte)Lerp(MapColor[c], PanelColor[c], panel.BackgroundAlpha);
            }

            pixels[(i * 4) + 3] = 255;
        }

        var portraitX = Padding;
        var portraitY = Padding + (panel.ShowName ? NameRowHeight : 0);
        var portraitWidth = panel.Width - (2 * Padding);
        var portraitHeight = panel.Height - (2 * Padding) - (panel.ShowName ? NameRowHeight : 0);
        if (portraitWidth <= 0 || portraitHeight <= 0)
        {
            return new RgbaImage(panel.Width, panel.Height, pixels);
        }

        // The portrait camera is orthographic with half-height 1 / zoom, shifted by the camera offset.
        // The render texture has the portrait rect's aspect, so the rect is filled without letterboxing.
        var halfHeight = 1f / frame.Zoom;
        var halfWidth = halfHeight * portraitWidth / portraitHeight;
        for (var py = 0; py < portraitHeight; py++)
        {
            var worldZ = frame.OffsetZ + halfHeight - ((py + 0.5f) / portraitHeight * 2f * halfHeight);
            for (var px = 0; px < portraitWidth; px++)
            {
                var worldX = frame.OffsetX - halfWidth + ((px + 0.5f) / portraitWidth * 2f * halfWidth);
                var index = (((portraitY + py) * panel.Width) + portraitX + px) * 4;
                foreach (var sprite in sprites)
                {
                    BlendSample(sprite, worldX, worldZ, pixels, index);
                }
            }
        }

        return new RgbaImage(panel.Width, panel.Height, pixels);
    }

    private static void BlendSample(Sprite sprite, float worldX, float worldZ, byte[] target, int index)
    {
        Span<float> color = stackalloc float[4];
        if (!TrySample(sprite, worldX, worldZ, color))
        {
            return;
        }

        var alpha = color[3] / 255f;
        for (var c = 0; c < 3; c++)
        {
            target[index + c] = (byte)Lerp(target[index + c], color[c], alpha);
        }
    }

    private static bool TrySample(Sprite sprite, float worldX, float worldZ, Span<float> color)
    {
        var u = (worldX - (sprite.CenterX - (sprite.SizeX / 2f))) / sprite.SizeX;
        var v = ((sprite.CenterZ + (sprite.SizeZ / 2f)) - worldZ) / sprite.SizeZ;
        if (u < 0f || u >= 1f || v < 0f || v >= 1f)
        {
            return false;
        }

        SampleBilinear(sprite.Image, (u * sprite.Image.Width) - 0.5f, (v * sprite.Image.Height) - 0.5f, color);
        return true;
    }

    private static void SampleBilinear(RgbaImage image, float x, float y, Span<float> color)
    {
        var x0 = (int)Math.Floor(x);
        var y0 = (int)Math.Floor(y);
        var fx = x - x0;
        var fy = y - y0;
        color.Clear();
        AddTexel(image, x0, y0, (1 - fx) * (1 - fy), color);
        AddTexel(image, x0 + 1, y0, fx * (1 - fy), color);
        AddTexel(image, x0, y0 + 1, (1 - fx) * fy, color);
        AddTexel(image, x0 + 1, y0 + 1, fx * fy, color);

        // Average color over opaque texels only, so transparent borders do not darken edges.
        if (color[3] > 0f)
        {
            for (var c = 0; c < 3; c++)
            {
                color[c] /= color[3] / 255f;
            }
        }
    }

    private static void AddTexel(RgbaImage image, int x, int y, float weight, Span<float> color)
    {
        x = Math.Clamp(x, 0, image.Width - 1);
        y = Math.Clamp(y, 0, image.Height - 1);
        var i = ((y * image.Width) + x) * 4;
        var alpha = image.Pixels[i + 3] / 255f;
        for (var c = 0; c < 3; c++)
        {
            color[c] += image.Pixels[i + c] * weight * alpha;
        }

        color[3] += image.Pixels[i + 3] * weight;
    }

    private static float Lerp(float a, float b, float t)
    {
        return a + ((b - a) * t);
    }
}
