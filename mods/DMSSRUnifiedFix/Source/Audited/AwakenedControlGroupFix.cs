using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using DMSSR.Abilities;
using Fortified;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Grammar;

namespace WhiteGiver.DMSSRUnifiedFix.Audited;

/// <summary>Keep awakening independent of the native mechanitor relation and work-mode routing.</summary>
internal static class AwakenedControlGroupFix
{
    private static readonly AccessTools.FieldRef<StatWorker, StatDef> Stat =
        AccessTools.FieldRefAccess<StatWorker, StatDef>("stat");

    [ThreadStatic] private static int suppressZeroBandwidthDepth;

    internal static bool Installed { get; private set; }

    internal static void Apply(Harmony harmony)
    {
        if (Installed || StatDefOf.BandwidthCost == null) return;
        MethodInfo trigger = Require(typeof(CompDeadManSwitch), "TryTriggerDMS");
        // Preflight before changing any method or Def. Future upstream drift disables only this subsystem.
        _ = KeepAwakening(PatchProcessor.GetOriginalInstructions(trigger)).ToList();
        var patches = new List<(MethodInfo Target, string? Prefix, string? Postfix, string? Transpiler, string? Finalizer)>
        {
            (trigger, null, null, nameof(KeepAwakening), null),
            (Require(typeof(CompDeadManSwitch), nameof(CompDeadManSwitch.Wake)), nameof(Wake), null, null, null),
            (Require(typeof(CompDeadManSwitch), nameof(CompDeadManSwitch.Notify_Killed)), null, nameof(Killed), null, null),
            (Require(typeof(StatWorker), nameof(StatWorker.FinalizeValue)), nameof(WastepackScope), null, null, nameof(EndBandwidthScope)),
            (Require(typeof(StatWorker), nameof(StatWorker.GetValue), typeof(Thing), typeof(bool), typeof(int)), nameof(UncachedBandwidth), null, null, null),
            (Require(typeof(CompAbilityEffect_LocalMesh), "GetBandwidthMultiplier", typeof(Pawn)), nameof(LocalMeshScope), null, null, nameof(EndBandwidthScope)),
            (Require(typeof(ThinkNode_Condition_Wake), "Satisfied"), null, nameof(WakeCondition), null, null),
            (Require(typeof(JobGiver_GetEnergy), nameof(JobGiver_GetEnergy.GetMinAutorechargeThreshold)), nameof(RechargeThreshold), null, null, null),
            (Require(typeof(OutgoingMech), nameof(OutgoingMech.Trigger)), nameof(OutgoingTrigger), null, null, null),
        };
        Type? storage = AccessTools.TypeByName("WhiteGiver.DMSMechStorage.MechStorage");
        if (storage != null)
            patches.Add((Require(storage, "RequiresEnrollment", typeof(Pawn)), null, nameof(StorageEnrollment), null, null));

        try
        {
            foreach (var patch in patches)
                harmony.Patch(patch.Target, prefix: Hook(patch.Prefix), postfix: Hook(patch.Postfix),
                    transpiler: Hook(patch.Transpiler), finalizer: Hook(patch.Finalizer));

            StatDef bandwidth = StatDefOf.BandwidthCost;
            bandwidth.parts ??= new List<StatPart>();
            if (!bandwidth.parts.Any(part => part is StatPart_AwakenedBandwidth))
                bandwidth.parts.Add(new StatPart_AwakenedBandwidth { parentStat = bandwidth });
            // Static startup can run after SetImmutability. A newly stateful stat must not retain
            // the immutable cache determined before this StatPart existed.
            bandwidth.immutable = false;
            bandwidth.Worker.TryClearCache();
            bandwidth.Worker.SetCacheability(false);
            Installed = true;
        }
        catch
        {
            foreach (var patch in patches)
                foreach (string? name in new[] { patch.Prefix, patch.Postfix, patch.Transpiler, patch.Finalizer })
                    if (name != null) harmony.Unpatch(patch.Target, Require(typeof(AwakenedControlGroupFix), name));
            throw;
        }
    }

    private static MethodInfo Require(Type type, string name, params Type[] parameters) =>
        AccessTools.Method(type, name, parameters.Length == 0 ? null : parameters)
        ?? throw new MissingMethodException(type.FullName, name);

    private static HarmonyMethod? Hook(string? name) => name == null ? null :
        new HarmonyMethod(Require(typeof(AwakenedControlGroupFix), name));

    internal static bool ZeroBandwidth(StatRequest req) => suppressZeroBandwidthDepth == 0
        && req.HasThing && req.Thing is Pawn pawn && pawn.TryGetComp<CompDeadManSwitch>()?.woken == true;

    private static bool Wake(CompDeadManSwitch __instance)
    {
        __instance.woken_Lurk = false;
        if (!__instance.woken)
        {
            __instance.woken = true;
            Pawn pawn = (Pawn)__instance.parent;
            RefreshBandwidth(pawn);
            pawn.Name = new NameSingle(NameGenerator.GenerateName(__instance.Props.nameRule ?? RulePackDefOf.NamerTraderGeneral));
            Find.LetterStack.ReceiveLetter("FFF.MechWake".Translate(pawn.Label),
                "FFF.MechWakeDesc".Translate(pawn.Label), LetterDefOf.PositiveEvent, pawn);
            pawn.interactions = new Pawn_InteractionsTracker(pawn);
        }
        return false;
    }

    private static void Killed(CompDeadManSwitch __instance)
    {
        if (__instance.parent is Pawn pawn) RefreshBandwidth(pawn);
    }

    private static void RefreshBandwidth(Pawn pawn)
    {
        StatDefOf.BandwidthCost?.Worker.ClearCacheForThing(pawn);
        StatDefOf.WastepacksPerRecharge?.Worker.ClearCacheForThing(pawn);
        pawn.GetOverseer()?.mechanitor?.Notify_BandwidthChanged();
    }

    internal static IEnumerable<CodeInstruction> KeepAwakening(IEnumerable<CodeInstruction> instructions)
    {
        List<CodeInstruction> result = new(instructions);
        FieldInfo woken = AccessTools.Field(typeof(CompDeadManSwitch), nameof(CompDeadManSwitch.woken));
        FieldInfo lurk = AccessTools.Field(typeof(CompDeadManSwitch), nameof(CompDeadManSwitch.woken_Lurk));
        var matches = new List<int>();
        for (int i = 3; i + 2 < result.Count; i++)
            if (result[i - 3].opcode == OpCodes.Ldarg_0 && result[i - 2].opcode == OpCodes.Ldc_I4_0
                && result[i - 1].StoresField(lurk) && result[i].opcode == OpCodes.Ldarg_0
                && result[i + 1].opcode == OpCodes.Ldc_I4_0 && result[i + 2].StoresField(woken))
                matches.Add(i);
        if (matches.Count != 1)
            throw new InvalidOperationException("CompDeadManSwitch.TryTriggerDMS: expected exactly one woken_Lurk=false; woken=false block, found " + matches.Count + ".");
        // Nops preserve every label and exception boundary while dropping only the assignment.
        for (int i = matches[0]; i < matches[0] + 3; i++)
        {
            result[i].opcode = OpCodes.Nop;
            result[i].operand = null;
        }
        return result;
    }

    private static void WastepackScope(StatWorker __instance, ref bool __state)
    {
        if (StatDefOf.WastepacksPerRecharge != null && Stat(__instance) == StatDefOf.WastepacksPerRecharge)
            LocalMeshScope(ref __state);
    }

    private static void LocalMeshScope(ref bool __state)
    {
        suppressZeroBandwidthDepth++;
        __state = true;
    }

    private static void EndBandwidthScope(bool __state)
    {
        if (__state) suppressZeroBandwidthDepth--;
    }

    private static bool UncachedBandwidth(StatWorker __instance, Thing thing, bool applyPostProcess, ref float __result)
    {
        if (suppressZeroBandwidthDepth == 0 || Stat(__instance) != StatDefOf.BandwidthCost) return true;
        // Suppressed reads must neither reuse the cached zero nor publish a physical cost into it.
        __result = __instance.GetValue(StatRequest.For(thing), applyPostProcess);
        return false;
    }

    private static void WakeCondition(Pawn pawn, ref bool __result)
    {
        if (__result && pawn?.GetOverseer() != null) __result = false;
    }

    private static bool RechargeThreshold(Pawn pawn, ref int __result)
    {
        if (pawn == null || pawn is IOverseerMech || pawn.GetMechControlGroup() != null
            || pawn.TryGetComp<CompDeadManSwitch>()?.woken != true) return true;
        __result = Mathf.RoundToInt(pawn.RaceProps.maxMechEnergy * MechanitorControlGroup.DefaultMechRechargeThresholds.min);
        return false;
    }

    private static void StorageEnrollment(Pawn mech, ref bool __result)
    {
        if (!__result && mech != null && mech.TryGetComp<CompOverseerSubject>() != null
            && mech is not IStateControllableMech && mech.TryGetComp<CompDeadManSwitch>()?.woken == true)
            __result = true;
    }

    private static bool OutgoingTrigger(OutgoingMech __instance)
    {
        __instance.timeToTrigger = Rand.Range(300000, 1800000);
        Pawn? mech = __instance.mech;
        CompDeadManSwitch? comp = mech?.TryGetComp<CompDeadManSwitch>();
        CompProperties_DeadManSwitch? props = comp?.Props;
        if (Rand.Chance(0.5f))
        {
            if (mech != null && props?.attackRule != null)
            {
                List<Settlement> settlements = Find.World.worldObjects.AllWorldObjects.OfType<Settlement>()
                    .Where(settlement => settlement.def == WorldObjectDefOf.Settlement
                        && settlement.Faction != null && !settlement.Faction.IsPlayer
                        && settlement.Faction.HostileTo(Faction.OfPlayer)).ToList();
                if (settlements.Count > 0)
                {
                    Settlement settlement = settlements.RandomElement();
                    GrammarRequest request = default;
                    request.Includes.Add(props.attackRule);
                    request.Rules.Add(new Rule_String("Faction", settlement.Faction.Name));
                    request.Rules.Add(new Rule_String("Settlement", settlement.Name));
                    AddMechRules(ref request, mech);
                    Find.LetterStack.ReceiveLetter("FFF.MechStory_Attack".Translate(mech.Label),
                        GrammarResolver.Resolve("_story", request), LetterDefOf.PositiveEvent, new LookTargets(settlement.Tile));
                    settlement.Destroy();
                }
            }
        }
        else if (Rand.Chance(0.5f))
        {
            if (mech != null && props?.lootRule != null && props.lootSetMaker?.root != null
                && Find.AnyPlayerHomeMap is Map map)
            {
                IntVec3 cell = DropCellFinder.TradeDropSpot(map);
                ActiveTransporterInfo pod = new();
                pod.innerContainer.TryAddRangeOrTransfer(props.lootSetMaker.root.Generate(), true, false);
                DropPodUtility.MakeDropPodAt(cell, map, pod);
                GrammarRequest request = default;
                request.Includes.Add(props.lootRule);
                AddMechRules(ref request, mech);
                Find.LetterStack.ReceiveLetter("FFF.MechStory_Loot".Translate(mech.Label),
                    GrammarResolver.Resolve("_story", request), LetterDefOf.PositiveEvent, new LookTargets(cell, map));
            }
        }
        GameComponent_DMS? component = Current.Game?.GetComponent<GameComponent_DMS>();
        if (component != null)
        {
            component.removedCache ??= new List<Pawn>();
            // The upstream pre-enumeration RemoveAll also matches null mechs. Queue that sentinel
            // rather than mutating OutgoingMeches if a missing pawn reaches Trigger during foreach.
            component.removedCache.Add(mech!);
        }
        return false;
    }

    private static void AddMechRules(ref GrammarRequest request, Pawn mech)
    {
        request.Rules.Add(new Rule_String("Mech", mech.Label));
        request.Rules.Add(new Rule_String("MechType", mech.kindDef.race.label));
    }
}

internal sealed class StatPart_AwakenedBandwidth : StatPart
{
    public override void TransformValue(StatRequest req, ref float val)
    {
        if (AwakenedControlGroupFix.ZeroBandwidth(req)) val = 0f;
    }

    public override string ExplanationPart(StatRequest req) => AwakenedControlGroupFix.ZeroBandwidth(req)
        ? "Awakened mech: bandwidth cost = 0" : string.Empty;
}
