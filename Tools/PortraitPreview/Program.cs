using System.Globalization;
using System.Net;
using System.Text;
using PortraitPreview;
using SelectedPawnPortraitOverlay;

// Previews the overlay's portrait framing for every vanilla non-humanlike race, plus dressed human samples,
// without running the game.
// Framing comes from the mod's own PortraitFraming.cs; textures come from the game's Assets.zip files.
// Output contains game textures, so keep it local.
var options = ParseArgs(args);
var gameDir = options.GetValueOrDefault("game", @"C:\Program Files (x86)\Steam\steamapps\common\RimWorld");
var outDir = Path.GetFullPath(options.GetValueOrDefault("out", Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "out")));
var cameraZoom = GetFloat(options, "zoom", 1.02f);
var faceEmphasis = GetFloat(options, "face", 0.2f);
var panel = new PanelOptions(
    (int)GetFloat(options, "width", 240f),
    (int)GetFloat(options, "height", 360f),
    !options.ContainsKey("no-name"),
    GetFloat(options, "bg-alpha", 0.45f));

// Matches the vanilla adult body type (BodyTypeDef.headOffset) for the human reference card.
const float HumanHeadZ = 0.34f;

using var textures = new TextureLibrary(gameDir);
if (options.TryGetValue("list", out var listPrefix))
{
    // Lists texture paths, for picking the clothing and hair used by the humanlike sample cards.
    foreach (var path in textures.PathsStartingWith(listPrefix))
    {
        Console.WriteLine(path);
    }

    return;
}

Directory.CreateDirectory(outDir);

var entries = new List<PreviewEntry>();
var human = BuildHumanReference(textures);
if (human != null)
{
    entries.Add(human);
    entries.AddRange(BuildHumanlikeSamples(textures));
}

var missing = new List<string>();
foreach (var race in RaceCatalog.Load(gameDir).Where(race => !race.Humanlike).OrderBy(race => race.BodyDrawSize).ThenBy(race => race.DefName))
{
    var image = textures.LoadSouthFacing(race.TexPath, race.GraphicClass);
    if (image == null)
    {
        missing.Add($"{race.DefName} ({race.TexPath})");
        continue;
    }

    entries.Add(new PreviewEntry(race, new[] { new Sprite(image, 0f, 0f, race.DrawSizeX, race.DrawSizeY) }, null)
    {
        TexelDensity = image.Height / race.DrawSizeY
    });
}

var aspect = PreviewRenderer.PortraitAspect(panel);
var offImages = new List<RgbaImage>();
var onImages = new List<RgbaImage>();
foreach (var entry in entries)
{
    // Auto-fit off is what existing users keep after the update; auto-fit on is the default for new installs.
    // The mod falls back to the drawSize rule when a pawn cannot be measured, so the preview does the same.
    entry.Bounds = PreviewRenderer.MeasureBounds(entry.Sprites);
    entry.Fit = entry.Bounds.HasValue
        ? PortraitFraming.Fit(entry.Bounds.Value, aspect, entry.Race.Humanlike, entry.TexelDensity)
        : new PortraitFit(entry.Race.Humanlike ? 1f : PortraitFraming.AutoFitScale(entry.Race.BodyDrawSize, entry.TexelDensity));
    var off = PreviewRenderer.RenderPanel(entry.Sprites, PortraitFraming.Compute(cameraZoom, faceEmphasis, 1f, entry.HeadZ), panel);
    var on = PreviewRenderer.RenderPanel(
        entry.Sprites,
        PortraitFraming.Compute(cameraZoom, faceEmphasis, entry.Fit, entry.HeadZ, PortraitAdjustment.Identity),
        panel);
    // Half-size images match how large the report and sheets show them and keep the report small.
    var offHalf = Half(off);
    var onHalf = Half(on);
    entry.OffPng = PngWriter.Encode(offHalf);
    entry.OnPng = PngWriter.Encode(onHalf);
    offImages.Add(offHalf);
    onImages.Add(onHalf);
}

PngWriter.Write(Path.Combine(outDir, "sheet_off.png"), ContactSheet(offImages, 14));
PngWriter.Write(Path.Combine(outDir, "sheet_on.png"), ContactSheet(onImages, 14));
File.WriteAllText(Path.Combine(outDir, "index.html"), BuildHtml(entries, missing, cameraZoom, faceEmphasis, panel), Encoding.UTF8);

Console.WriteLine($"Rendered {entries.Count} portraits ({missing.Count} races without a texture) to {outDir}");
if (human?.Bounds is { } reference)
{
    // The values PortraitFraming.ReferenceFit* are set from.
    Console.WriteLine(Invariant($"Reference human box: width {reference.Width:R}, height {reference.Height:R}, center x {reference.CenterX:R}, center z {reference.CenterZ:R}"));
}

foreach (var entry in entries.Where(entry => entry.Race.Humanlike))
{
    Console.WriteLine(Invariant($"  {entry.Race.Label}: box {entry.Bounds?.Width:0.###} x {entry.Bounds?.Height:0.###}, needs {entry.Bounds?.FitHeight(aspect):0.###}, scale {entry.Fit.Scale:0.###}"));
}

foreach (var name in missing)
{
    Console.WriteLine("  no texture: " + name);
}

PreviewEntry BuildHumanReference(TextureLibrary library)
{
    var body = library.Load("Things/Pawn/Humanlike/Bodies/Naked_Male_south");
    var head = library.Load("Things/Pawn/Humanlike/Heads/Male/Male_Average_Normal_south");
    if (body == null || head == null)
    {
        return null;
    }

    var race = new RaceSample("Human_Reference", "human (naked male, no hair)", "Humanlike", "Core", true, "", "", 1.5f, 1.5f);
    return new PreviewEntry(
        race,
        new[] { new Sprite(body, 0f, 0f, 1.5f, 1.5f), new Sprite(head, 0f, HumanHeadZ, 1.5f, 1.5f) },
        HumanHeadZ);
}

// Dressed colonists, to check that ordinary hair and clothes keep the reference framing and that only
// large headgear makes a humanlike portrait zoom out.
IEnumerable<PreviewEntry> BuildHumanlikeSamples(TextureLibrary library)
{
    const string Apparel = "Things/Pawn/Humanlike/Apparel/";
    const string Hair = "Things/Pawn/Humanlike/Hairs/";
    var samples = new (string Name, string Label, string[] BodyLayer, string[] HeadLayer)[]
    {
        ("Sample_Jacket", "human: hair, jacket", new[] { Apparel + "Jacket/Jacket_Male_south" }, new[] { Hair + "Decent_south" }),
        ("Sample_Parka", "human: afro, parka", new[] { Apparel + "Parka/Parka_Male_south" }, new[] { Hair + "Afro_south" }),
        ("Sample_Cowboy", "human: cowboy hat, duster", new[] { Apparel + "Duster/Duster_Male_south" }, new[] { Hair + "Decent_south", Apparel + "CowboyHat/CowboyHat_south" }),
        ("Sample_Marine", "human: marine armor, helmet", new[] { Apparel + "PowerArmor/PowerArmor_Male_south" }, new[] { Apparel + "PowerArmorHelmet/PowerArmorHelmet_south" }),
        ("Sample_Cataphract", "human: cataphract armor, helmet", new[] { Apparel + "CataphractArmor/CataphractArmor_Male_south" }, new[] { Apparel + "CataphractArmorHelmet/CataphractHelmet_south" }),
        ("Sample_Crown", "human: crown, cape", new[] { Apparel + "Cape/Cape_Male_south" }, new[] { Hair + "Decent_south", Apparel + "Crown/Crown_south" }),
        ("Sample_Dreadcap", "human: ceremonial dreadcap", new[] { Apparel + "EltexRobe/EltexRobe_Male_south" }, new[] { Apparel + "CeremonialDreadcap/CeremonialDreadcap_south" }),
    };

    var body = library.Load("Things/Pawn/Humanlike/Bodies/Naked_Male_south");
    var head = library.Load("Things/Pawn/Humanlike/Heads/Male/Male_Average_Normal_south");
    foreach (var sample in samples)
    {
        var sprites = new List<Sprite> { new(body, 0f, 0f, 1.5f, 1.5f) };
        sprites.AddRange(sample.BodyLayer.Select(library.Load).Where(image => image != null).Select(image => new Sprite(image, 0f, 0f, 1.5f, 1.5f)));
        sprites.Add(new Sprite(head, 0f, HumanHeadZ, 1.5f, 1.5f));
        sprites.AddRange(sample.HeadLayer.Select(library.Load).Where(image => image != null).Select(image => new Sprite(image, 0f, HumanHeadZ, 1.5f, 1.5f)));
        var race = new RaceSample(sample.Name, sample.Label, "Humanlike", "Core", true, "", "", 1.5f, 1.5f);
        yield return new PreviewEntry(race, sprites, HumanHeadZ);
    }
}

static RgbaImage Half(RgbaImage source)
{
    var width = source.Width / 2;
    var height = source.Height / 2;
    var pixels = new byte[width * height * 4];
    for (var y = 0; y < height; y++)
    {
        for (var x = 0; x < width; x++)
        {
            for (var c = 0; c < 4; c++)
            {
                var sum = 0;
                for (var dy = 0; dy < 2; dy++)
                {
                    for (var dx = 0; dx < 2; dx++)
                    {
                        sum += source.Pixels[((((y * 2) + dy) * source.Width) + (x * 2) + dx) * 4 + c];
                    }
                }

                pixels[(((y * width) + x) * 4) + c] = (byte)(sum / 4);
            }
        }
    }

    return new RgbaImage(width, height, pixels);
}

static RgbaImage ContactSheet(IReadOnlyList<RgbaImage> images, int columns)
{
    var cellWidth = images[0].Width;
    var cellHeight = images[0].Height;
    var rows = (images.Count + columns - 1) / columns;
    var width = columns * cellWidth;
    var pixels = new byte[width * rows * cellHeight * 4];
    for (var n = 0; n < images.Count; n++)
    {
        var originX = (n % columns) * cellWidth;
        var originY = (n / columns) * cellHeight;
        for (var y = 0; y < cellHeight; y++)
        {
            Array.Copy(images[n].Pixels, y * cellWidth * 4, pixels, (((originY + y) * width) + originX) * 4, cellWidth * 4);
        }
    }

    return new RgbaImage(width, rows * cellHeight, pixels);
}

static string BuildHtml(List<PreviewEntry> entries, List<string> missing, float zoom, float face, PanelOptions panel)
{
    var html = new StringBuilder();
    html.Append("<!doctype html><html lang=\"ko\"><head><meta charset=\"utf-8\"><title>Portrait framing preview</title><style>")
        .Append("body{font-family:system-ui,sans-serif;background:#1b1b1b;color:#ddd;margin:16px}")
        .Append("h1{font-size:20px}p{color:#aaa;max-width:900px}")
        .Append(".grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(270px,1fr));gap:12px}")
        .Append(".card{background:#262626;border-radius:6px;padding:8px}.card h2{font-size:14px;margin:0 0 2px}")
        .Append(".meta{font-size:12px;color:#999;margin-bottom:6px}.pair{display:flex;gap:6px}")
        .Append(".pair figure{margin:0;flex:1}.pair img{width:100%;image-rendering:auto;display:block}")
        .Append("figcaption{font-size:11px;color:#aaa;text-align:center}")
        .Append("</style></head><body>");
    html.Append("<h1>초상화 구도 미리보기</h1>");
    html.Append(Invariant($"<p>줌 {zoom:F2}, 얼굴 강조 {face * 100:F0}%, 패널 {panel.Width}×{panel.Height}. "))
        .Append("모드의 PortraitFraming.cs로 계산하고 게임 텍스처를 남쪽 방향으로 배치했습니다. ")
        .Append("사람 샘플 외에는 옷·머리카락·애니메이션 노드를 그리지 않으므로 실제 게임과 약간 다를 수 있습니다. ")
        .Append("왼쪽은 자동 맞춤 꺼짐(기존 사용자), 오른쪽은 켜짐(새 설치)입니다. ")
        .Append("켜짐 쪽은 모드와 같은 방식으로 그려진 영역(알파 경계 상자)을 재서 배율과 중심을 정합니다.</p>");
    if (missing.Count > 0)
    {
        html.Append("<p>텍스처를 찾지 못한 종족: ").Append(WebUtility.HtmlEncode(string.Join(", ", missing))).Append("</p>");
    }

    html.Append("<div class=\"grid\">");
    foreach (var entry in entries)
    {
        var race = entry.Race;
        html.Append("<div class=\"card\"><h2>").Append(WebUtility.HtmlEncode(race.Label)).Append("</h2>")
            .Append("<div class=\"meta\">").Append(WebUtility.HtmlEncode($"{race.DefName} · {race.Category} · {race.Source}"))
            .Append(Invariant($" · drawSize {race.BodyDrawSize:0.##}"))
            .Append(entry.Bounds is { } bounds ? Invariant($" · 실측 {bounds.Width:0.##}×{bounds.Height:0.##}") : " · 실측 실패")
            .Append(entry.TexelDensity is { } density ? Invariant($" · 텍셀 {density:0.#}/단위") : "")
            .Append(Invariant($" · 배율 {entry.Fit.Scale:0.00}"))
            .Append("</div><div class=\"pair\">")
            .Append($"<figure><img src=\"{DataUri(entry.OffPng)}\"><figcaption>자동 맞춤 끔</figcaption></figure>")
            .Append($"<figure><img src=\"{DataUri(entry.OnPng)}\"><figcaption>자동 맞춤 켬</figcaption></figure>")
            .Append("</div></div>");
    }

    html.Append("</div></body></html>");
    return html.ToString();
}

// Images are embedded so the report is one file that opens anywhere.
static string DataUri(byte[] png) => "data:image/png;base64," + Convert.ToBase64String(png);

static string Invariant(FormattableString value) => value.ToString(CultureInfo.InvariantCulture);

static Dictionary<string, string> ParseArgs(string[] arguments)
{
    var parsed = new Dictionary<string, string>();
    for (var i = 0; i < arguments.Length; i++)
    {
        if (!arguments[i].StartsWith("--"))
        {
            continue;
        }

        var key = arguments[i].Substring(2);
        var hasValue = i + 1 < arguments.Length && !arguments[i + 1].StartsWith("--");
        parsed[key] = hasValue ? arguments[++i] : "true";
    }

    return parsed;
}

static float GetFloat(Dictionary<string, string> parsed, string key, float fallback)
{
    return parsed.TryGetValue(key, out var text) ? float.Parse(text, CultureInfo.InvariantCulture) : fallback;
}

sealed class PreviewEntry
{
    public PreviewEntry(RaceSample race, IReadOnlyList<Sprite> sprites, float? headZ)
    {
        Race = race;
        Sprites = sprites;
        HeadZ = headZ;
    }

    public RaceSample Race { get; }
    public IReadOnlyList<Sprite> Sprites { get; }
    public float? HeadZ { get; }
    public PortraitBounds? Bounds { get; set; }
    public float? TexelDensity { get; init; }
    public PortraitFit Fit { get; set; } = new(1f);
    public byte[] OffPng { get; set; }
    public byte[] OnPng { get; set; }
}
