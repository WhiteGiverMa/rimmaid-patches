using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml;
using Meidocho.WolfeinWeaponBoxCE;
using RimWorld;
using Verse;

public static class WeaponBoxValidation
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("FAIL: " + message);
        Console.WriteLine("PASS: " + message);
    }

    public static void Run(string wolfeinXml, string ceXml)
    {
        string savedSettings = Path.Combine(Path.GetTempPath(), "WolfeinWeaponBoxCE-" + Guid.NewGuid() + ".xml");
        using (Log.LockMessages())
        {
            DeepProfiler.enabled = false;
            try
            {
                RunChecks(wolfeinXml, ceXml, savedSettings);
            }
            finally
            {
                Scribe.mode = LoadSaveMode.Inactive;
                if (File.Exists(savedSettings)) File.Delete(savedSettings);
            }
        }
    }

    private static void RunChecks(string wolfeinXml, string ceXml, string savedSettings)
    {
        var xml = new XmlDocument();
        xml.Load(wolfeinXml);
        var node = xml.SelectSingleNode("/Defs/ThingDef[defName='Wolfein_WeaponCase']");
        Check(node != null, "installed Wolfein target exists");
        Check(node["equippedStatOffsets"]["CarryingCapacity"].InnerText == "350", "upstream vanilla bonus remains 350");
        Check(node["comps"].GetAttribute("Inherit") == "false" && !node["comps"].InnerXml.Contains("CompQuality"),
            "current weapon case has no quality component");

        // Shells bypass Unity content loading only; patch methods, Scribe and gear calculations are real.
        var box = (ThingDef)RuntimeHelpers.GetUninitializedObject(typeof(ThingDef));
        box.defName = node["defName"].InnerText;
        box.thingClass = typeof(Apparel);
        box.category = ThingCategory.Item;
        box.equippedStatOffsets = new List<StatModifier>();
        foreach (XmlNode offset in node["equippedStatOffsets"].ChildNodes)
        {
            var stat = new StatDef { defName = offset.Name };
            DefDatabase<StatDef>.Add(stat);
            box.equippedStatOffsets.Add(new StatModifier
            {
                stat = stat,
                value = float.Parse(offset.InnerText, System.Globalization.CultureInfo.InvariantCulture)
            });
        }
        var originalOffsets = new List<StatModifier>(box.equippedStatOffsets);
        DefDatabase<ThingDef>.Add(box);
        xml.Load(ceXml);
        foreach (string name in new[] { "CarryBulk", "CarryWeight" })
            DefDatabase<StatDef>.Add(DirectXmlToObject.ObjectFromXml<StatDef>(
                xml.SelectSingleNode("/Defs/StatDef[defName='" + name + "']"), true));
        var bulk = DefDatabase<StatDef>.GetNamedSilentFail("CarryBulk");
        var weight = DefDatabase<StatDef>.GetNamedSilentFail("CarryWeight");
        var gear = (Apparel)RuntimeHelpers.GetUninitializedObject(typeof(Apparel));
        gear.def = box;

        var settings = new WeaponBoxSettings();
        var mod = (WeaponBoxMod)RuntimeHelpers.GetUninitializedObject(typeof(WeaponBoxMod));
        const BindingFlags instanceFlags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(WeaponBoxMod).GetField("settings", instanceFlags).SetValue(mod, settings);
        var apply = typeof(WeaponBoxMod).GetMethod("ApplySettings", instanceFlags);
        apply.Invoke(mod, null);
        Check(box.equippedStatOffsets.Count == originalOffsets.Count, "inactive/uninitialized patch makes no changes");
        // ModsConfig's static constructor requires a live Unity filesystem/mod registry.
        // Supply the resolved defs rather than pretending to test that startup integration.
        typeof(WeaponBoxMod).GetField("weaponBox", instanceFlags).SetValue(mod, box);
        typeof(WeaponBoxMod).GetField("carryBulk", instanceFlags).SetValue(mod, bulk);
        typeof(WeaponBoxMod).GetField("carryWeight", instanceFlags).SetValue(mod, weight);
        apply.Invoke(mod, null);
        Check(StatWorker.StatOffsetFromGear(gear, bulk) == 350, "default gear bonus +350 bulk through real CE StatParts");
        Check(StatWorker.StatOffsetFromGear(gear, weight) == 0, "default gear bonus +0 weight");

        foreach (var values in new[] { new[] { 500, 125 }, new[] { 0, 0 }, new[] { 1000, 1000 }, new[] { 350, 0 } })
        {
            settings.BulkBonus = values[0];
            settings.WeightBonus = values[1];
            apply.Invoke(mod, null);
            Check(StatWorker.StatOffsetFromGear(gear, bulk) == values[0], "same existing gear: bulk updates to " + values[0]);
            Check(StatWorker.StatOffsetFromGear(gear, weight) == values[1], "same existing gear: weight updates to " + values[1]);
            Check(box.equippedStatOffsets.Count == originalOffsets.Count + 2, "reapplying does not accumulate offsets");
        }
        foreach (var offset in originalOffsets)
            Check(box.equippedStatOffsets.Contains(offset), "original offset preserved: " + offset.stat.defName + "=" + offset.value);

        settings.BulkBonus = 612;
        settings.WeightBonus = 87;
        Scribe.saver.InitSaving(savedSettings, "Settings");
        settings.ExposeData();
        Scribe.saver.FinalizeSaving();
        xml.Load(savedSettings);
        var loaded = new WeaponBoxSettings();
        Scribe.mode = LoadSaveMode.LoadingVars;
        Scribe.loader.curXmlParent = xml.DocumentElement;
        loaded.ExposeData();
        Check(loaded.BulkBonus == 612 && loaded.WeightBonus == 87, "real Scribe preserves both settings on save/read");
        xml.LoadXml("<Settings />");
        Scribe.loader.curXmlParent = xml.DocumentElement;
        loaded.ExposeData();
        Check(loaded.BulkBonus == 350 && loaded.WeightBonus == 0, "missing saved keys restore requested defaults");
        xml.LoadXml("<Settings><bulkBonus>2000</bulkBonus><weightBonus>-10</weightBonus></Settings>");
        Scribe.loader.curXmlParent = xml.DocumentElement;
        loaded.ExposeData();
        Check(loaded.BulkBonus == 1000 && loaded.WeightBonus == 0, "external settings clamp to slider bounds");
        Console.WriteLine("REAL-ASSEMBLY VALIDATION PASSED. Unity UI, startup/CE activation and full pawn/game/mod-list integration are not exercised.");
    }
}
