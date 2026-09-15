using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Meidocho.CeleTechDuplicateEquippableFix;
using RimWorld;
using Verse;

namespace Meidocho.CeleTechEquippableHarness;

// Exercises the REAL compiled patch DLL (Fix_CeleTech_DuplicateEquippableComps.dll) against
// the REAL CeleTech.Base.CompLegendaryWeapons from the workshop DLL, without booting the game.
//
// Phase 1 (failing-before): unpatched Notify_Unequipped never calls Verb.Notify_EquipmentLost.
// Phase 2 (passing-after): after CeleTechEquippableFixMod.Apply(), the prefix fires
//     Notify_EquipmentLost once per verb, BEFORE the original ability removal (sequence proof).
// Phase 3 (scope): a def outside the scoped set gets no additional callback.
public static class Program
{
    private static int seq;

    private static readonly string[] ProbeDirs =
    {
        @"A:\SteamLibrary\steamapps\common\RimWorld\RimWorldWin64_Data\Managed",
        @"A:\SteamLibrary\steamapps\workshop\content\294100\2009463077\Current\Assemblies",
        @"A:\SteamLibrary\steamapps\workshop\content\294100\3446237098\1.6\Assemblies",
        @"A:\SteamLibrary\steamapps\common\RimWorld\Mods\Fix_CeleTech_DuplicateEquippableComps\1.6\Assemblies",
    };

    static Program()
    {
        AppDomain.CurrentDomain.AssemblyResolve += (_, e) =>
        {
            string name = new AssemblyName(e.Name).Name;
            foreach (string dir in ProbeDirs)
            {
                string path = Path.Combine(dir, name + ".dll");
                if (File.Exists(path))
                {
                    return Assembly.LoadFrom(path);
                }
            }
            return null;
        };
    }


    public class CountingVerb : Verb_Shoot
    {
        public static readonly List<int> VerbSeqs = new();
        public static readonly List<int> PostfixSeqs = new();

        public static void ClearRecords()
        {
            VerbSeqs.Clear();
            PostfixSeqs.Clear();
        }

        public override void Notify_EquipmentLost()
        {
            VerbSeqs.Add(++seq);
        }
    }

    public static int Main()
    {
        try
        {
            return Run();
        }
        catch (Exception ex)
        {
            Console.WriteLine("HARNESS CRASH: " + ex.GetType().FullName + ": " + ex.Message);
            Console.WriteLine(ex.StackTrace);
            return 2;
        }
    }

    private static int Run()
    {
        int failures = 0;
        void Check(bool ok, string what)
        {
            Console.WriteLine((ok ? "PASS" : "FAIL") + ": " + what);
            if (!ok) failures++;
        }

        // Silence Verse.Log (it would route into UnityEngine logging outside the game).
        using (Verse.Log.LockMessages())
        {
            // ThingDef/ThingWithComps cannot be constructed normally outside the game:
            // BuildableDef field initializers pull in Verse.BaseContent (Unity content
            // loading). Allocate ctor-less shells and set only the fields the real
            // patch path reads (def.defName, thing.def, def.verbs).
            var def = (ThingDef)System.Runtime.Serialization.FormatterServices
                .GetUninitializedObject(typeof(ThingDef));
            def.defName = "TangDaoPersona";
            typeof(ThingDef).GetField("verbs", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(def, new List<VerbProperties>
                {
                    new VerbProperties { verbClass = typeof(CountingVerb) },
                });
            var thing = (ThingWithComps)System.Runtime.Serialization.FormatterServices
                .GetUninitializedObject(typeof(ThingWithComps));
            thing.def = def;
            var props = new CeleTech.Base.CompProperties_LegendaryWeapons
            {
                AbilitieDefs = new List<AbilityDef>(),
            };

            // ---- Phase 1: failing-before (no Harmony patch applied yet) ----
            var compBefore = new CeleTech.Base.CompLegendaryWeapons { parent = thing, props = props };
            compBefore.Notify_Unequipped(null);
            Check(CountingVerb.VerbSeqs.Count == 0,
                "failing-before: unpatched CompLegendaryWeapons.Notify_Unequipped fires no Notify_EquipmentLost (defect reproduced)");

            // ---- Phase 2: passing-after (real patch source applied) ----
            CeleTechEquippableFixMod.Apply();

            var original = AccessTools.Method(AccessTools.TypeByName("CeleTech.Base.CompLegendaryWeapons"), "Notify_Unequipped");
            var owners = Harmony.GetPatchInfo(original)?.Owners;
            Check(owners != null && owners.Contains("meidocho.celetech.duplicateequippablecompsfix"),
                "Harmony patch registered on real CeleTech.Base.CompLegendaryWeapons.Notify_Unequipped (owners: " + string.Join(",", owners ?? (IEnumerable<string>)Array.Empty<string>()) + ")");
            // Ordering proof: postfix records its sequence after the original body would run;
            // the verb callback must carry a strictly smaller sequence.
            var harness = new Harmony("meidocho.celetech.harness.orderproof");
            var postfix = new HarmonyMethod(typeof(Program), nameof(RecordPostfixSeq));
            harness.Patch(original, postfix: postfix);

            var compAfter = new CeleTech.Base.CompLegendaryWeapons { parent = thing, props = props };
            compAfter.Notify_Unequipped(null);
            Check(CountingVerb.VerbSeqs.Count == 1,
                "passing-after: Notify_EquipmentLost fired exactly once per verb");
            Check(CountingVerb.PostfixSeqs.Count == 1
                && CountingVerb.VerbSeqs[0] < CountingVerb.PostfixSeqs[0],
                "ordering: verb callback ran before the original method body completed (prefix-before-original)");

            // ---- Phase 3: scope ----
            var otherDef = (ThingDef)System.Runtime.Serialization.FormatterServices
                .GetUninitializedObject(typeof(ThingDef));
            otherDef.defName = "SomeOtherWeapon";
            typeof(ThingDef).GetField("verbs", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(otherDef, new List<VerbProperties> { new VerbProperties { verbClass = typeof(CountingVerb) } });
            var otherThing = (ThingWithComps)System.Runtime.Serialization.FormatterServices
                .GetUninitializedObject(typeof(ThingWithComps));
            otherThing.def = otherDef;
            var otherComp = new CeleTech.Base.CompLegendaryWeapons { parent = otherThing, props = props };
            otherComp.Notify_Unequipped(null);
            Check(CountingVerb.VerbSeqs.Count == 1,
                "scope: non-scoped def got no additional Notify_EquipmentLost");
        }

        Console.WriteLine(failures == 0 ? "HARNESS RESULT: ALL CHECKS PASSED" : $"HARNESS RESULT: {failures} CHECK(S) FAILED");
        return failures == 0 ? 0 : 1;
    }

    private static void RecordPostfixSeq()
    {
        CountingVerb.PostfixSeqs.Add(++seq);
    }
}
