using AssetRipper.TextureDecoder.Bc;
using AssetRipper.TextureDecoder.Rgb.Formats;

namespace PortraitPreview;

public sealed class RgbaImage
{
    public RgbaImage(int width, int height, byte[] pixels)
    {
        Width = width;
        Height = height;
        Pixels = pixels;
    }

    public int Width { get; }
    public int Height { get; }

    // Row-major RGBA, top row first.
    public byte[] Pixels { get; }
}

// Texture lookup across the game's Assets.zip archives (base game plus each DLC), decoding DDS on demand.
public sealed class TextureLibrary : IDisposable
{
    private readonly List<ZipIndex> archives = new();
    private readonly Dictionary<string, ZipIndex.Entry> entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RgbaImage> cache = new(StringComparer.OrdinalIgnoreCase);

    public TextureLibrary(string gameDir)
    {
        var zips = new List<string> { Path.Combine(gameDir, "Assets.zip") };
        zips.AddRange(Directory.GetFiles(Path.Combine(gameDir, "Data"), "Assets.zip", SearchOption.AllDirectories));
        foreach (var zip in zips.Where(File.Exists))
        {
            var archive = new ZipIndex(zip);
            archives.Add(archive);
            foreach (var entry in archive.Entries)
            {
                var key = ToTexPath(entry.Name);
                if (key != null)
                {
                    entries.TryAdd(key, entry);
                }
            }
        }
    }

    // "Resources/Textures/Things/Pawn/Animal/Muffalo/Muffalo_south.psd.dds" -> "Things/Pawn/Animal/Muffalo/Muffalo_south"
    private static string ToTexPath(string entryName)
    {
        if (!entryName.EndsWith(".dds", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var start = entryName.IndexOf("Textures/", StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return null;
        }

        var path = entryName.Substring(start + "Textures/".Length);
        path = path.Substring(0, path.Length - ".dds".Length);
        var extension = Path.GetExtension(path);
        return extension is ".png" or ".psd" ? path.Substring(0, path.Length - extension.Length) : path;
    }

    // The south-facing frame a portrait uses, following how RimWorld graphic classes name their files.
    public RgbaImage LoadSouthFacing(string texPath, string graphicClass)
    {
        var candidates = graphicClass switch
        {
            "Graphic_Single" => new[] { texPath },
            "Graphic_Random" => RandomVariants(texPath),
            _ => new[] { texPath + "_south", texPath + "_east", texPath }
        };

        foreach (var candidate in candidates)
        {
            var image = Load(candidate);
            if (image != null)
            {
                return image;
            }
        }

        return null;
    }

    private string[] RandomVariants(string folder)
    {
        var prefix = folder.TrimEnd('/') + "/";
        var inFolder = entries.Keys.Where(key => key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).OrderBy(key => key).ToList();
        var south = inFolder.Where(key => key.EndsWith("_south", StringComparison.OrdinalIgnoreCase)).ToList();
        return (south.Count > 0 ? south : inFolder).ToArray();
    }

    public IEnumerable<string> PathsStartingWith(string prefix)
    {
        return entries.Keys.Where(key => key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).OrderBy(key => key);
    }

    public RgbaImage Load(string texPath)
    {
        if (cache.TryGetValue(texPath, out var cached))
        {
            return cached;
        }

        RgbaImage image = null;
        if (entries.TryGetValue(texPath, out var entry))
        {
            image = DecodeDds(entry.Archive.Read(entry));
        }

        cache[texPath] = image;
        return image;
    }

    private static RgbaImage DecodeDds(byte[] data)
    {
        if (data.Length < 128 || data[0] != 'D' || data[1] != 'D' || data[2] != 'S')
        {
            return null;
        }

        var height = BitConverter.ToInt32(data, 12);
        var width = BitConverter.ToInt32(data, 16);
        var fourCc = System.Text.Encoding.ASCII.GetString(data, 84, 4);
        var offset = 128;
        var dxgiFormat = 0;
        if (fourCc == "DX10")
        {
            dxgiFormat = BitConverter.ToInt32(data, 128);
            offset = 148;
        }

        var input = new ReadOnlySpan<byte>(data, offset, data.Length - offset);
        byte[] rgba;
        switch (fourCc, dxgiFormat)
        {
            case ("DX10", 98):
            case ("DX10", 99):
                Bc7.Decompress<ColorRGBA<byte>, byte>(input, width, height, out rgba);
                break;
            case ("DX10", 80):
                Bc4.Decompress<ColorRGBA<byte>, byte>(input, width, height, out rgba);
                break;
            case ("DX10", 77):
            case ("DXT5", _):
                Bc3.Decompress<ColorRGBA<byte>, byte>(input, width, height, out rgba);
                break;
            case ("DX10", 71):
            case ("DXT1", _):
                Bc1.Decompress<ColorRGBA<byte>, byte>(input, width, height, out rgba);
                break;
            case ("DX10", 28):
                rgba = input.Slice(0, width * height * 4).ToArray();
                break;
            default:
                return null;
        }

        return new RgbaImage(width, height, rgba);
    }

    public void Dispose()
    {
        foreach (var archive in archives)
        {
            archive.Dispose();
        }
    }
}
