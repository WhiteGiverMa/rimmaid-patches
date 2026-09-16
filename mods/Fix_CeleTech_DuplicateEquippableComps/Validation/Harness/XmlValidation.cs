using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using Verse;

namespace Meidocho.CeleTechEquippableHarness;

// Uses the installed game's parser, patch workers and inheritance resolver, not an emulator.
internal static class XmlValidation
{
    public static int Run(string patchPath)
    {
        // The standalone process has no game PrefsData for the optional profiler.
        DeepProfiler.enabled = false;
        const string weapons = @"A:\SteamLibrary\steamapps\workshop\content\294100\3446237098\1.6\Defs\Weapons\";
        string[] files =
        {
            @"A:\SteamLibrary\steamapps\common\RimWorld\Data\Core\Defs\ThingDefs_Misc\Weapons\BaseWeapons.xml",
            weapons + "WeaponBase.xml", weapons + @"ChargedPower\OICW.xml",
            weapons + @"ChargedSmart\Smart_Basic.xml", weapons + @"Melee\TangDao.xml",
            weapons + @"Melee\EMPSword.xml",
        };
        string[] targets = { "QBZ71_QS", "QTS9_OICW", "W85XWJ", "QBZS85XWJ", "QBZ_eightfiveAR", "TangDaoPersona", "CMC_EMPsword_sevenstars" };
        var doc = new XmlDocument();
        doc.LoadXml("<Defs/>");
        foreach (string file in files)
        {
            var input = new XmlDocument();
            input.Load(file);
            foreach (XmlElement node in input.DocumentElement.ChildNodes.OfType<XmlElement>())
                doc.DocumentElement.AppendChild(doc.ImportNode(node, true));
        }

        var patch = new XmlDocument();
        patch.Load(patchPath);
        int failures = 0;
        void Check(bool ok, string message)
        {
            Console.WriteLine((ok ? "PASS: " : "FAIL: ") + message);
            if (!ok) failures++;
        }

        using (Log.LockMessages())
        {
            foreach (XmlNode node in patch.SelectNodes("/Patch/Operation"))
            {
                var operation = DirectXmlToObject.ObjectFromXml<PatchOperation>(node, false);
                try
                {
                    Check(operation.Apply(doc), operation.GetType().Name + " applied by real game worker");
                }
                catch (Exception ex)
                {
                    Check(false, operation.GetType().Name + " threw " + ex.GetType().Name + ": " + ex.Message);
                }
            }
            foreach (string name in targets.Skip(5))
                Check(doc.SelectSingleNode($"Defs/*[defName='{name}']/comps/@Inherit")?.Value == "False",
                    name + " comps has Inherit=False after real XML deserialization");

            XmlInheritance.Clear();
            var registered = new HashSet<XmlNode>();
            void Register(XmlNode node)
            {
                if (!registered.Add(node)) return;
                string parent = node.Attributes["ParentName"]?.Value;
                if (parent != null) Register(doc.SelectSingleNode($"Defs/*[@Name='{parent}']"));
                XmlInheritance.TryRegister(node, null);
            }
            foreach (string name in targets) Register(doc.SelectSingleNode($"Defs/*[defName='{name}']"));
            XmlInheritance.Resolve();

            foreach (string name in targets)
            {
                XmlNode resolved = XmlInheritance.GetResolvedNodeFor(doc.SelectSingleNode($"Defs/*[defName='{name}']"));
                XmlNode[] comps = resolved.SelectNodes("comps/li").Cast<XmlNode>().ToArray();
                int equippable = comps.Count(n => n.SelectSingleNode("compClass")?.InnerText == "CompEquippable"
                    || n.Attributes["Class"]?.Value == "CeleTech.Base.CompProperties_PawnEquipmentGizmo"
                    || n.Attributes["Class"]?.Value == "CeleTech.Base.CompProperties_LegendaryWeapons");
                Check(equippable == 1, $"{name}: equippable={equippable} after real inheritance");
                if (!targets.Skip(5).Contains(name)) continue;
                foreach (string comp in new[] { "CompProperties_Forbiddable", "CompProperties_Styleable", "CompProperties_Art" })
                    Check(comps.Count(n => n.Attributes["Class"]?.Value == comp) == 1, name + ": exactly one " + comp);
                Check(comps.Count(n => n.SelectSingleNode("compClass")?.InnerText == "CompQuality") == 1, name + ": exactly one CompQuality");
                Check(resolved.SelectSingleNode("comps/li[@Class='CompProperties_Art']/nameMaker")?.InnerText == "NamerArtWeaponGun",
                    name + ": active Gun art grammar preserved");
            }
            XmlInheritance.Clear();
        }
        Console.WriteLine($"REAL XML HARNESS: {failures} failure(s)");
        return failures == 0 ? 0 : 1;
    }
}
