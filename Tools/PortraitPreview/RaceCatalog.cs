using System.Globalization;
using System.Xml.Linq;

namespace PortraitPreview;

public sealed record RaceSample(
    string DefName,
    string Label,
    string Category,
    string Source,
    bool Humanlike,
    string TexPath,
    string GraphicClass,
    float DrawSizeX,
    float DrawSizeY)
{
    public float BodyDrawSize => Math.Max(DrawSizeX, DrawSizeY);
}

// Reads race and pawn kind defs from the game's Data folders, resolving Name/ParentName inheritance.
public static class RaceCatalog
{
    public static List<RaceSample> Load(string gameDir)
    {
        var things = new DefTable("ThingDef");
        var kinds = new DefTable("PawnKindDef");
        foreach (var source in Directory.GetDirectories(Path.Combine(gameDir, "Data")).OrderBy(SourceOrder))
        {
            var defsDir = Path.Combine(source, "Defs");
            if (!Directory.Exists(defsDir))
            {
                continue;
            }

            var sourceName = Path.GetFileName(source);
            foreach (var file in Directory.EnumerateFiles(defsDir, "*.xml", SearchOption.AllDirectories))
            {
                XElement root;
                try
                {
                    root = XDocument.Load(file).Root;
                }
                catch (Exception)
                {
                    continue;
                }

                foreach (var element in root?.Elements() ?? Enumerable.Empty<XElement>())
                {
                    things.Add(element, sourceName);
                    kinds.Add(element, sourceName);
                }
            }
        }

        var samples = new List<RaceSample>();
        var seenRaces = new HashSet<string>();
        foreach (var (kind, _) in kinds.Concrete())
        {
            var raceName = kinds.Find(kind, "race")?.Value.Trim();
            if (raceName == null || seenRaces.Contains(raceName) || !things.TryGet(raceName, out var race, out var raceSource))
            {
                continue;
            }

            var lastStage = kinds.Find(kind, "lifeStages")?.Elements("li").LastOrDefault();
            var graphic = lastStage?.Element("bodyGraphicData");
            var texPath = graphic?.Element("texPath")?.Value.Trim();
            if (texPath == null)
            {
                continue;
            }

            var (drawX, drawY) = ParseDrawSize(graphic.Element("drawSize")?.Value);
            var intelligence = things.Find(race, "race/intelligence")?.Value.Trim() ?? "Animal";
            var fleshType = things.Find(race, "race/fleshType")?.Value.Trim() ?? "Normal";
            seenRaces.Add(raceName);
            samples.Add(new RaceSample(
                raceName,
                things.Find(race, "label")?.Value.Trim() ?? raceName,
                fleshType != "Normal" ? fleshType : intelligence,
                raceSource,
                intelligence == "Humanlike",
                texPath,
                graphic.Element("graphicClass")?.Value.Trim() ?? "Graphic_Multi",
                drawX,
                drawY));
        }

        return samples;
    }

    private static int SourceOrder(string path)
    {
        return Path.GetFileName(path) == "Core" ? 0 : 1;
    }

    private static (float, float) ParseDrawSize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return (1f, 1f);
        }

        var parts = text.Trim('(', ')', ' ').Split(',');
        var x = float.Parse(parts[0], CultureInfo.InvariantCulture);
        var y = parts.Length > 1 ? float.Parse(parts[1], CultureInfo.InvariantCulture) : x;
        return (x, y);
    }

    private sealed class DefTable
    {
        private readonly string tag;
        private readonly Dictionary<string, XElement> named = new();
        private readonly Dictionary<string, (XElement Element, string Source)> byDefName = new();
        private readonly List<(XElement Element, string Source)> concrete = new();

        public DefTable(string tag)
        {
            this.tag = tag;
        }

        public void Add(XElement element, string source)
        {
            if (element.Name.LocalName != tag)
            {
                return;
            }

            var name = (string)element.Attribute("Name");
            if (name != null)
            {
                named[name] = element;
            }

            var defName = element.Element("defName")?.Value.Trim();
            if (defName != null && (string)element.Attribute("Abstract") != "True")
            {
                byDefName[defName] = (element, source);
                concrete.Add((element, source));
            }
        }

        public IEnumerable<(XElement Element, string Source)> Concrete() => concrete;

        public bool TryGet(string defName, out XElement element, out string source)
        {
            var found = byDefName.TryGetValue(defName, out var entry);
            element = entry.Element;
            source = entry.Source;
            return found;
        }

        // Nearest value for a path, walking up the ParentName chain like RimWorld's XML inheritance.
        public XElement Find(XElement element, string path)
        {
            for (var depth = 0; element != null && depth < 32; depth++)
            {
                var current = element;
                foreach (var step in path.Split('/'))
                {
                    current = current?.Element(step);
                }

                if (current != null)
                {
                    return current;
                }

                var parent = (string)element.Attribute("ParentName");
                element = parent != null && named.TryGetValue(parent, out var parentElement) ? parentElement : null;
            }

            return null;
        }
    }
}
