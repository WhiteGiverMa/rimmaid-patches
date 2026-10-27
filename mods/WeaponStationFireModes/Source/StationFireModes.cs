using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using VFM_VanillaFireModes.Settings;
using VFM_VanillaFireModes.Utilities;
using WeaponStation;

namespace WhiteGiver.WeaponStationFireModes;

public sealed class StationFireModesMod : Mod
{
    public const string HarmonyId = "whitegiver.weaponstation.firemodes";
    internal static bool RuntimeEnabled { get; private set; }

    public StationFireModesMod(ModContentPack content) : base(content)
    {
        if (RuntimeEnabled) return;
        if (ModsConfig.IsActive("CETeam.CombatExtended"))
        {
            Log.Warning("[Weapon Station Fire Modes] Combat Extended is active; vanilla adapter disabled.");
            return;
        }
        var harmony = new Harmony(HarmonyId);
        try
        {
            StationPatches.Apply(harmony);
            RuntimeEnabled = true;
            Log.Message("[Weapon Station Fire Modes] Unmanned station adapter registered; manned mounts unchanged.");
        }
        catch (Exception exception)
        {
            harmony.UnpatchAll(HarmonyId);
            Log.Error("[Weapon Station Fire Modes] Adapter not applied: " + exception);
        }
    }
}

public sealed class CompProperties_StationFireModes : CompProperties
{
    public CompProperties_StationFireModes() => compClass = typeof(CompStationFireModes);
}

public sealed class CompStationFireModes : ThingComp
{
    private VFM_FireMode mode;
    private bool automatic;
    private ThingDef emptyGunDef = null!;

    public VFM_FireMode Mode { get => mode; set => mode = value; }
    public bool AutoSelection { get => automatic; set => automatic = value; }
    internal Building_WeaponStation Station => (Building_WeaponStation)parent;
    internal Thing Gun => Station.InstalledGun;
    internal bool Eligible => StationFireModesMod.RuntimeEnabled
        && parent.def.defName == "VortexWS_WeaponStation"
        && parent.GetComp<CompMannable>() == null
        && Gun is { Destroyed: false } gun && gun.def != emptyGunDef;
    internal bool ModeEffectsEnabled => parent.Faction == Faction.OfPlayer || FireModeDB.Settings.enableFireModeForNPC;

    internal static CompStationFireModes? For(Thing? thing)
    {
        if (thing is not Building_WeaponStation station) return null;
        CompStationFireModes? comp = station.GetComp<CompStationFireModes>();
        return comp is { Eligible: true } ? comp : null;
    }

    public override void Initialize(CompProperties properties)
    {
        base.Initialize(properties);
        // StatsReport temporarily replaces turretGunDef with the mounted gun's Def.
        emptyGunDef = parent.def.building.turretGunDef;
        automatic = FireModeDB.Settings.autoModeDefaultOn;
    }

    public override void PostExposeData()
    {
        Scribe_Values.Look(ref mode, "wsfm_mode", VFM_FireMode.Default, forceSave: true);
        Scribe_Values.Look(ref automatic, "wsfm_auto", FireModeDB.Settings.autoModeDefaultOn, forceSave: true);
    }

    public override float GetStatOffset(StatDef stat) => Eligible && stat != StatDefOf.ShootingAccuracyPawn
        ? StatWorker.StatOffsetFromGear(Gun, stat) : 0f;

    public override float GetStatFactor(StatDef stat)
    {
        if (!Eligible || !ModeEffectsEnabled) return 1f;
        if (stat == StatDefOf.AimingDelayFactor) return FireModeDB.GetWarmup(mode, Gun.def.defName);
        return stat == StatDefOf.RangedCooldownFactor ? FireModeDB.GetCooldown(mode, Gun.def.defName) : 1f;
    }

    internal void SelectFor(LocalTargetInfo target)
    {
        if (!target.IsValid || !ModeEffectsEnabled) return;
        if (parent.Faction == Faction.OfPlayer && (!automatic || !FireModeDB.Settings.enableAutoSelectionForPlayer)) return;
        float distance = parent.Position.DistanceTo(target.Cell);
        mode = distance >= FireModeDB.Settings.precisionMinDistance ? VFM_FireMode.Precision
            : distance >= FireModeDB.Settings.burstMinDistance ? VFM_FireMode.Burst : VFM_FireMode.Suppression;
    }

    internal float Accuracy(float baseline)
    {
        float offset = StatWorker.StatOffsetFromGear(Gun, StatDefOf.ShootingAccuracyPawn);
        float factor = ModeEffectsEnabled ? FireModeDB.GetAccuracy(mode, Gun.def.defName) : 1f;
        if (offset == 0f && factor == 1f) return baseline;
        SimpleCurve curve = StatDefOf.ShootingAccuracyPawn.postProcessCurve;
        return curve.Evaluate((curve.EvaluateInverted(baseline) + offset) * factor);
    }

    internal static string ModeLabel(VFM_FireMode value) => (value switch
    {
        VFM_FireMode.Precision => "VFM_PrecisionMode",
        VFM_FireMode.Burst => "VFM_ShortBurstMode",
        VFM_FireMode.Suppression => "VFM_SuppressionMode",
        _ => "VFM_DefaultMode"
    }).Translate();

    public override IEnumerable<Gizmo> CompGetGizmosExtra()
    {
        if (!Eligible || parent.Faction != Faction.OfPlayer) yield break;
        if (!automatic || !FireModeDB.Settings.enableAutoSelectionForPlayer)
            yield return new Command_Action
            {
                icon = mode switch
                {
                    VFM_FireMode.Precision => VFM_IconTexture.VFM_Precision_Icon,
                    VFM_FireMode.Burst => VFM_IconTexture.VFM_Burst_Icon,
                    VFM_FireMode.Suppression => VFM_IconTexture.VFM_Suppression_Icon,
                    _ => VFM_IconTexture.VFM_Default_Icon
                },
                defaultLabel = ModeLabel(mode),
                defaultDesc = "WSFM_StationModeDescription".Translate(),
                action = () => mode = (VFM_FireMode)(((int)mode + 1) % 4)
            };
        if (FireModeDB.Settings.enableAutoSelectionForPlayer)
            yield return new Command_Toggle
            {
                icon = VFM_IconTexture.VFM_Auto_Icon,
                defaultLabel = "VFM_AutoSelection".Translate(),
                defaultDesc = "VFM_AutoSelection_Desc".Translate(),
                isActive = () => automatic,
                toggleAction = () => { automatic = !automatic; mode = VFM_FireMode.Default; }
            };
    }

    public override string? CompInspectStringExtra() => !Eligible ? null : "WSFM_Inspect".Translate(
        ModeLabel(mode), parent.GetStatValue(StatDefOf.ShootingAccuracyTurret).ToStringPercent("F2"),
        Station.WeaponWarmupTime.ToString("F2"),
        (Station.AttackVerb.verbProps.AdjustedCooldown(Station.AttackVerb, null)
            * parent.GetStatValue(StatDefOf.RangedCooldownFactor)).ToString("F2"));
}

[StaticConstructorOnStartup]
public static class StationStats
{
    static StationStats() => Apply();

    public static void Apply()
    {
        if (!StationFireModesMod.RuntimeEnabled) return;
        StatDef accuracy = StatDefOf.ShootingAccuracyTurret;
        accuracy.parts ??= new List<StatPart>();
        if (!accuracy.parts.Any(p => p is StationAccuracyPart))
            accuracy.parts.Add(new StationAccuracyPart { parentStat = accuracy });
        foreach (StatDef stat in new[] { accuracy, StatDefOf.AimingDelayFactor, StatDefOf.RangedCooldownFactor })
        {
            stat.immutable = stat.IsImmutable();
            stat.Worker.SetCacheability(stat.immutable);
        }
    }
}

public sealed class StationAccuracyPart : StatPart
{
    public override void TransformValue(StatRequest req, ref float val)
    {
        if (CompStationFireModes.For(req.Thing) is { } comp) val = comp.Accuracy(val);
    }

    public override string? ExplanationPart(StatRequest req)
    {
        if (CompStationFireModes.For(req.Thing) is not { } comp) return null;
        float baseline = parentStat.Worker.GetValueUnfinalized(req);
        float factor = comp.ModeEffectsEnabled ? FireModeDB.GetAccuracy(comp.Mode, comp.Gun.def.defName) : 1f;
        return "WSFM_AccuracyExplanation".Translate(comp.Gun.LabelCap,
            StatDefOf.ShootingAccuracyPawn.postProcessCurve.EvaluateInverted(baseline).ToString("F2"),
            StatWorker.StatOffsetFromGear(comp.Gun, StatDefOf.ShootingAccuracyPawn).ToString("+0.##;-0.##;0"),
            CompStationFireModes.ModeLabel(comp.Mode), factor.ToStringPercent(), comp.Accuracy(baseline).ToStringPercent("F2"));
    }
}

internal static class StationPatches
{
    private sealed class BurstState
    {
        internal Thing? Gun;
        internal int Count;
        internal bool Valid;
    }

    private static readonly ConditionalWeakTable<Verb, BurstState> Bursts = new();
    private static readonly FieldInfo WarmupField = AccessTools.Field(typeof(BuildingProperties), "turretBurstWarmupTime");

    internal static void Apply(Harmony harmony)
    {
        MethodInfo shoot = AccessTools.DeclaredMethod(typeof(Building_TurretGun), "TryStartShootSomething");
        if (PatchProcessor.GetOriginalInstructions(shoot).Count(IsWarmupRead) != 1)
            throw new InvalidOperationException("Expected one turret warmup field read; automatic selection not applied.");
        harmony.Patch(shoot, transpiler: Patch(nameof(PrepareTranspiler)));
        harmony.Patch(AccessTools.DeclaredPropertyGetter(typeof(Building_WeaponStation), "WeaponWarmupTime"), postfix: Patch(nameof(Warmup)));
        harmony.Patch(AccessTools.DeclaredMethod(typeof(Building_WeaponStation), "BurstCooldownTime"), postfix: Patch(nameof(Cooldown)));
        harmony.Patch(AccessTools.DeclaredMethod(typeof(Building_WeaponStation), "GetInspectString"), postfix: Patch(nameof(Inspect)));
        harmony.Patch(AccessTools.DeclaredMethod(typeof(Verb), "WarmupComplete"), prefix: Patch(nameof(LockBurst)));
        harmony.Patch(AccessTools.DeclaredPropertyGetter(typeof(Verb), "BurstShotCount"), postfix: Patch(nameof(BurstCount)));
        harmony.Patch(AccessTools.DeclaredMethod(typeof(Verb), "VerbTick"), postfix: Patch(nameof(CleanupBurst)));
    }

    private static HarmonyMethod Patch(string name) => new(typeof(StationPatches), name);

    private static IEnumerable<CodeInstruction> PrepareTranspiler(IEnumerable<CodeInstruction> source)
    {
        var instructions = source.ToList();
        if (instructions.Count(IsWarmupRead) != 1)
            throw new InvalidOperationException("Turret warmup field drifted; adapter refused.");
        foreach (CodeInstruction instruction in instructions)
        {
            if (IsWarmupRead(instruction))
            {
                var load = new CodeInstruction(OpCodes.Ldarg_0);
                load.MoveLabelsFrom(instruction);
                load.MoveBlocksFrom(instruction);
                yield return load;
                yield return new CodeInstruction(OpCodes.Call, AccessTools.DeclaredMethod(typeof(StationPatches), nameof(PrepareAttack)));
            }
            yield return instruction;
        }
    }

    private static bool IsWarmupRead(CodeInstruction instruction) => instruction.opcode == OpCodes.Ldflda
        && Equals(instruction.operand, WarmupField);

    private static void PrepareAttack(Building_TurretGun station) => CompStationFireModes.For(station)?.SelectFor(station.CurrentTarget);

    private static void Warmup(Building_WeaponStation __instance, ref float __result)
    {
        if (CompStationFireModes.For(__instance) != null) __result *= __instance.GetStatValue(StatDefOf.AimingDelayFactor);
    }

    private static void Cooldown(Building_WeaponStation __instance, ref float __result)
    {
        if (CompStationFireModes.For(__instance) != null) __result *= __instance.GetStatValue(StatDefOf.RangedCooldownFactor);
    }

    private static void Inspect(Building_WeaponStation __instance, ref string __result)
    {
        if (CompStationFireModes.For(__instance) is { } comp)
            __result = (__result + "\n" + comp.CompInspectStringExtra()).Trim();
    }

    private static CompStationFireModes? ForVerb(Verb verb)
    {
        CompStationFireModes? comp = CompStationFireModes.For(verb.Caster);
        return comp != null && comp.ModeEffectsEnabled && !verb.verbProps.IsMeleeAttack
            && verb is not Verb_ShootOneUse && ReferenceEquals(verb.EquipmentSource, comp.Gun)
            && comp.Gun.def.IsRangedWeapon ? comp : null;
    }

    private static void LockBurst(Verb __instance)
    {
        if (Bursts.TryGetValue(__instance, out BurstState? old)) old.Valid = false;
        if (ForVerb(__instance) is not { } comp) return;
        int count = FireModeDB.GetBurstCount(comp.Mode, __instance.BurstShotCount, comp.Gun.def.defName);
        BurstState state = Bursts.GetOrCreateValue(__instance);
        state.Gun = comp.Gun;
        state.Count = Math.Max(1, count);
        state.Valid = true;
    }

    private static void BurstCount(Verb __instance, ref int __result)
    {
        if (Bursts.TryGetValue(__instance, out BurstState? state) && state.Valid
            && ForVerb(__instance) is { } comp && ReferenceEquals(state.Gun, comp.Gun)) __result = state.Count;
    }

    private static void CleanupBurst(Verb __instance)
    {
        if (__instance.state != VerbState.Bursting && Bursts.TryGetValue(__instance, out BurstState? state))
            state.Valid = false;
    }
}
