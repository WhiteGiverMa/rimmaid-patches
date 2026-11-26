using System.IO.Compression;
using System.Xml.Linq;

internal static class PackageChecks
{
    internal const string DefName = "WhiteGiver_MechanitorBandwidthRelay";
    internal const string DefFile = "Defs/ThingDefs_Buildings/BandwidthRelay.xml";

    internal static void Run(string directory)
    {
        XDocument mod = XDocument.Load(Path.Combine(directory, DefFile));
        string biotech = Path.Combine(Program.GameRoot, "Data", "Biotech");
        XElement vanilla = XDocument.Load(Path.Combine(biotech, "Defs", "ThingDefs_Buildings", "Buildings_Misc.xml"))
            .Root!.Elements("ThingDef").Single(def => (string?)def.Element("defName") == "BandNode");
        XElement relay = mod.Root!.Elements("ThingDef").Single();
        Require((string?)relay.Element("defName") == DefName && (string?)relay.Attribute("ParentName") == "BuildingBase" &&
            string.Equals((string?)relay.Attribute("MayRequire"), "Ludeon.RimWorld.Biotech", StringComparison.OrdinalIgnoreCase), "DLC-gated new building def");
        Require(XNode.DeepEquals(vanilla.Element("graphicData"), relay.Element("graphicData")) &&
            XNode.DeepEquals(vanilla.Element("size"), relay.Element("size")) && XNode.DeepEquals(vanilla.Element("rotatable"), relay.Element("rotatable")),
            "building graphic, shadow, size and rotation match vanilla");
        using var assets = ZipFile.OpenRead(Path.Combine(biotech, "Assets.zip"));
        Require(assets.GetEntry($"Textures/{(string?)relay.Element("graphicData")?.Element("texPath")}.png.dds.split") != null &&
            !Directory.Exists(Path.Combine(directory, "Textures")), "real Biotech asset referenced; no redistributed texture");
        XDocument about = XDocument.Load(Path.Combine(directory, "About", "About.xml"));
        HashSet<string?> dependencies = about.Root!.Element("modDependencies")!.Elements("li")
            .Select(item => (string?)item.Element("packageId")).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Require(dependencies.SetEquals(["Ludeon.RimWorld.Biotech", "brrainz.harmony"]), "only Biotech and Harmony are required");

        string[] keyed = ["English", "ChineseSimplified"];
        string[] Keys(string language) => XDocument.Load(Path.Combine(directory, "Languages", language, "Keyed", "BandwidthRelay.xml"))
            .Root!.Elements().Select(node => node.Name.LocalName).Order().ToArray();
        Require(Keys(keyed[0]).SequenceEqual(Keys(keyed[1])), "English and Simplified Chinese keys agree");
        Require(XDocument.Load(Path.Combine(directory, "Languages", "ChineseSimplified", "DefInjected", "ThingDef", "BandwidthRelay.xml"))
            .Root!.Element($"{DefName}.label")?.Value == "带宽转接基站", "Simplified Chinese building name");
        Console.WriteLine("PASS: released package dependency, translations and original packed Biotech artwork reference.");
    }

    private static void Require(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
    }
}
