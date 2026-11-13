using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Verse;

namespace DMSSRLegionCustomizationCompat.Validation;

internal sealed class LegionMechEntry
{
    internal LegionMechEntry(string defName, string bodyDefName, string sourceFile)
    {
        DefName = defName;
        BodyDefName = bodyDefName;
        SourceFile = sourceFile;
    }

    internal string DefName { get; }

    internal string BodyDefName { get; }

    internal string SourceFile { get; }
}

/// <summary>
/// Minimal, deterministic reader for the exact XML files RimWorld would load. It reads real
/// BodyPartDef tags, real BodyDef trees and the real DMSL_Mech_* race list from the installed
/// Workshop copies; no def values are invented except for the two control defs used by the tests.
/// </summary>
internal sealed class DefBank
{
    internal readonly Dictionary<string, BodyPartDef> BodyParts = new(StringComparer.Ordinal);
    internal readonly Dictionary<string, BodyDef> Bodies = new(StringComparer.Ordinal);
    internal readonly Dictionary<string, BodyPartTagDef> Tags = new(StringComparer.Ordinal);
    internal readonly Dictionary<string, BodyPartGroupDef> Groups = new(StringComparer.Ordinal);

    private readonly HashSet<string> bodyPartsInDatabase = new(StringComparer.Ordinal);
    private readonly HashSet<string> bodiesInDatabase = new(StringComparer.Ordinal);

    internal BodyPartDef GetOrCreateBodyPart(string defName)
    {
        if (!BodyParts.TryGetValue(defName, out BodyPartDef? def))
        {
            def = new BodyPartDef
            {
                defName = defName,
                label = defName,
                tags = new List<BodyPartTagDef>(),
            };
            BodyParts.Add(defName, def);
        }

        return def;
    }

    internal BodyPartTagDef GetOrCreateTag(string defName)
    {
        if (!Tags.TryGetValue(defName, out BodyPartTagDef? def))
        {
            def = new BodyPartTagDef { defName = defName, label = defName };
            Tags.Add(defName, def);
        }

        return def;
    }

    internal BodyPartGroupDef GetOrCreateGroup(string defName)
    {
        if (!Groups.TryGetValue(defName, out BodyPartGroupDef? def))
        {
            def = new BodyPartGroupDef { defName = defName, label = defName };
            Groups.Add(defName, def);
        }

        return def;
    }

    internal void AddBodyPartFile(string path)
    {
        XDocument document = XDocument.Load(path);
        foreach (XElement element in document.Descendants("BodyPartDef"))
        {
            string? defName = element.Element("defName")?.Value.Trim();
            if (string.IsNullOrEmpty(defName))
            {
                continue;
            }

            BodyPartDef def = GetOrCreateBodyPart(defName!);
            def.label = element.Element("label")?.Value.Trim() ?? defName!;
            List<BodyPartTagDef>? tags = element.Element("tags")?
                .Elements("li")
                .Select(li => GetOrCreateTag(li.Value.Trim()))
                .ToList();
            if (tags != null)
            {
                def.tags = tags;
            }

            if (bodyPartsInDatabase.Add(def.defName))
            {
                DefDatabase<BodyPartDef>.Add(def);
            }
        }
    }

    internal void AddBodyDefFile(string path)
    {
        XDocument document = XDocument.Load(path);
        foreach (XElement element in document.Descendants("BodyDef"))
        {
            string? defName = element.Element("defName")?.Value.Trim();
            XElement? corePart = element.Element("corePart");
            if (string.IsNullOrEmpty(defName) || corePart == null)
            {
                continue;
            }

            BodyDef body = new()
            {
                defName = defName!,
                label = element.Element("label")?.Value.Trim() ?? defName!,
                corePart = ParsePart(corePart),
            };
            body.ResolveReferences();
            Bodies[defName!] = body;

            if (bodiesInDatabase.Add(body.defName))
            {
                DefDatabase<BodyDef>.Add(body);
            }
        }
    }

    private BodyPartRecord ParsePart(XElement element)
    {
        string defName = element.Element("def")?.Value.Trim()
            ?? throw new InvalidDataException("BodyPartRecord without <def>.");

        BodyPartRecord record = new()
        {
            def = GetOrCreateBodyPart(defName),
            customLabel = element.Element("customLabel")?.Value.Trim(),
            coverage = ParseFloat(element.Element("coverage"), 1f),
            height = ParseEnum(element.Element("height"), BodyPartHeight.Undefined),
            depth = ParseEnum(element.Element("depth"), BodyPartDepth.Undefined),
            groups = element.Element("groups")?
                .Elements("li")
                .Select(li => GetOrCreateGroup(li.Value.Trim()))
                .ToList() ?? new List<BodyPartGroupDef>(),
        };

        XElement? parts = element.Element("parts");
        if (parts != null)
        {
            foreach (XElement child in parts.Elements("li"))
            {
                record.parts.Add(ParsePart(child));
            }
        }

        return record;
    }

    private static float ParseFloat(XElement? element, float fallback)
    {
        return element != null
            && float.TryParse(element.Value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
            ? value
            : fallback;
    }

    private static T ParseEnum<T>(XElement? element, T fallback) where T : struct
    {
        return element != null && Enum.TryParse(element.Value.Trim(), ignoreCase: true, out T value)
            ? value
            : fallback;
    }
}

internal static class XmlDefLoader
{
    internal static DefBank Load(string gameRoot, string legionRoot, string dmsCoreRoot, string ancientCorpsRoot)
    {
        DefBank bank = new();

        string vanillaBodies = Path.Combine(gameRoot, "Data", "Core", "Defs", "Bodies");
        foreach (string file in Directory.GetFiles(vanillaBodies, "BodyParts_*.xml").OrderBy(path => path, StringComparer.Ordinal))
        {
            bank.AddBodyPartFile(file);
        }

        string coreRaceDefs = Path.Combine(dmsCoreRoot, "1.7", "Defs", "Things_Race");
        foreach (string file in Directory.GetFiles(coreRaceDefs, "*.xml").OrderBy(path => path, StringComparer.Ordinal))
        {
            bank.AddBodyPartFile(file);
        }

        foreach (string directory in LegionRaceDirectories(legionRoot))
        {
            foreach (string file in Directory.GetFiles(directory, "*.xml").OrderBy(path => path, StringComparer.Ordinal))
            {
                bank.AddBodyPartFile(file);
            }
        }

        foreach (string file in Directory.GetFiles(coreRaceDefs, "BodyPartsDef*.xml").OrderBy(path => path, StringComparer.Ordinal))
        {
            bank.AddBodyDefFile(file);
        }

        foreach (string directory in LegionRaceDirectories(legionRoot))
        {
            string file = Path.Combine(directory, "BodyPartsDef.xml");
            if (File.Exists(file))
            {
                bank.AddBodyDefFile(file);
            }
        }

        string ancientCorpsBodyDefs = Path.Combine(ancientCorpsRoot, "1.7", "Defs", "BodyDef");
        if (Directory.Exists(ancientCorpsBodyDefs))
        {
            foreach (string file in Directory.GetFiles(ancientCorpsBodyDefs, "*.xml").OrderBy(path => path, StringComparer.Ordinal))
            {
                bank.AddBodyDefFile(file);
            }
        }

        return bank;
    }

    internal static List<LegionMechEntry> LoadLegionMechs(string legionRoot)
    {
        List<LegionMechEntry> result = new();
        foreach (string directory in LegionRaceDirectories(legionRoot))
        {
            foreach (string file in Directory.GetFiles(directory, "*.xml").OrderBy(path => path, StringComparer.Ordinal))
            {
                XDocument document = XDocument.Load(file);
                foreach (XElement thing in document.Descendants("ThingDef"))
                {
                    string? defName = thing.Element("defName")?.Value.Trim();
                    if (string.IsNullOrEmpty(defName) || !defName!.StartsWith("DMSL_Mech_", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    string? body = thing.Element("race")?.Element("body")?.Value.Trim();
                    if (string.IsNullOrEmpty(body))
                    {
                        throw new InvalidDataException(defName + " has no <race><body> in " + file);
                    }

                    result.Add(new LegionMechEntry(defName!, body!, file));
                }
            }
        }

        return result
            .GroupBy(entry => entry.DefName, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(entry => entry.DefName, StringComparer.Ordinal)
            .ToList();
    }

    internal static string? LoadDroneDefName(string legionRoot)
    {
        foreach (string directory in LegionRaceDirectories(legionRoot))
        {
            string file = Path.Combine(directory, "Races_CommandingBird.xml");
            if (!File.Exists(file))
            {
                continue;
            }

            XDocument document = XDocument.Load(file);
            return document.Descendants("ThingDef")
                .Select(thing => thing.Element("defName")?.Value.Trim())
                .FirstOrDefault(name => name != null && name.StartsWith("DMSL_Drone_", StringComparison.Ordinal));
        }

        return null;
    }

    private static IEnumerable<string> LegionRaceDirectories(string legionRoot)
    {
        yield return Path.Combine(legionRoot, "Defs", "Defs", "ThingDefs_Races");
        yield return Path.Combine(legionRoot, "1.7", "AncientCorps", "Defs", "ThingDefs_Races");
    }
}
