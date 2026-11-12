using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using WhiteGiver.Wolfein.RestoreWorkPrioritiesFix;

internal static class Tests
{
    public static int Run()
    {
        int red = 0;
        void Check(string label, bool ok)
        {
            Console.WriteLine((ok ? "GREEN " : "RED   ") + label);
            if (!ok) red++;
        }

        var add = AccessTools.Method("Wolfein.HediffComp_RestoreWorkPriorities:CompPostPostAdd");
        var removed = AccessTools.Method("Wolfein.HediffComp_RestoreWorkPriorities:CompPostPostRemoved");
        Check("CompPostPostAdd resolved", add != null);
        Check("CompPostPostRemoved resolved", removed != null);
        if (add == null || removed == null) return 1;

        var savedField = add.DeclaringType!.GetField("savedPriorities", BindingFlags.NonPublic | BindingFlags.Instance)!;

        var race = new RaceProperties { intelligence = Intelligence.Humanlike };
        var pawn = new Pawn { def = new ThingDef { race = race } };
        var hediff = new HediffWithComps { pawn = pawn, def = new HediffDef() };
        pawn.workSettings = new Pawn_WorkSettings(pawn);

        // Red: before patching, the original comp runs and allocates its save dictionary.
        var beforePatch = Activator.CreateInstance(add.DeclaringType!)!;
        typeof(HediffComp).GetField("parent")!.SetValue(beforePatch, hediff);
        add.Invoke(beforePatch, new object?[] { null });
        Check("unpatched original runs", savedField.GetValue(beforePatch) != null);

        RestoreWorkPrioritiesFix.Apply();

        var addInfo = Harmony.GetPatchInfo(add);
        var removedInfo = Harmony.GetPatchInfo(removed);
        Check("add: one prefix", addInfo?.Prefixes?.Count == 1);
        Check("removed: one prefix", removedInfo?.Prefixes?.Count == 1);
        Check("prefix owner", addInfo?.Prefixes?[0].owner == RestoreWorkPrioritiesFix.HarmonyId);
        Check("prefix method", addInfo?.Prefixes?[0].PatchMethod == typeof(RestoreWorkPrioritiesFix).GetMethod(nameof(RestoreWorkPrioritiesFix.Prefix)));

        var comp = new HediffComp { parent = hediff };
        Check("uninitialized + humanlike -> skip", RestoreWorkPrioritiesFix.Prefix(comp) == false);

        var prioritiesField = typeof(Pawn_WorkSettings).GetField("priorities", BindingFlags.NonPublic | BindingFlags.Instance)!;
        prioritiesField.SetValue(pawn.workSettings, System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(DefMap<WorkTypeDef, int>)));
        Check("initialized + humanlike -> run original", RestoreWorkPrioritiesFix.Prefix(comp) == true);

        race.intelligence = Intelligence.Animal;
        Check("non-humanlike -> run original", RestoreWorkPrioritiesFix.Prefix(comp) == true);

        race.intelligence = Intelligence.Humanlike;
        pawn.workSettings = null!;
        Check("null workSettings -> run original", RestoreWorkPrioritiesFix.Prefix(comp) == true);

        // Green: after patching, the original comp is skipped for the uninitialized pawn.
        pawn.workSettings = new Pawn_WorkSettings(pawn);
        var afterPatch = Activator.CreateInstance(add.DeclaringType!)!;
        typeof(HediffComp).GetField("parent")!.SetValue(afterPatch, hediff);
        add.Invoke(afterPatch, new object?[] { null });
        Check("patched original skipped", savedField.GetValue(afterPatch) == null);
        Check("patched original leaves work settings uninitialized", !pawn.workSettings.Initialized);

        Console.WriteLine(red == 0 ? "ALL GREEN" : red + " RED");
        return red == 0 ? 0 : 1;
    }
}
