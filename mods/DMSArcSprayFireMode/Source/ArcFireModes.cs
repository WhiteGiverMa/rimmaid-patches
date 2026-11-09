using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using Verse;

namespace WhiteGiver.DMSArcSprayFireMode;

public sealed class ArcFireModesMod : Mod
{
    public const string HarmonyId = "whitegiver.dms.arcsprayfiremode";
    public ArcFireModesMod(ModContentPack content) : base(content) => ArcFireModes.Apply();
}

public enum FireMode { Automatic, Direct, Spray }

public static class ArcFireModes
{
    private sealed class Choice { internal FireMode Mode; }
    private static readonly ConditionalWeakTable<ThingWithComps, Choice> Choices = new();
    private static Type? arcType;
    public static bool Enabled { get; private set; }

    public static void Apply()
    {
        if (Enabled) return;
        if (ModsConfig.IsActive("ceteam.combatextended"))
        {
            Log.Message("[DMS Arc Fire Mode] Combat Extended active; no patches or controls installed.");
            return;
        }
        var harmony = new Harmony(ArcFireModesMod.HarmonyId);
        try
        {
            arcType = AccessTools.TypeByName("Fortified.Verb_ArcSprayProjectile")
                ?? throw new TypeLoadException("Fortified.Verb_ArcSprayProjectile is missing.");
            if (arcType.BaseType != typeof(Verb_ArcSpray) || typeof(Verb_ArcSpray).BaseType != typeof(Verb_Spray)
                || typeof(Verb_Spray).BaseType != typeof(Verb))
                throw new InvalidOperationException("FFF / vanilla spray inheritance changed.");
            RequireProtectedVirtual(typeof(Verb_Spray), "TryCastShot", typeof(bool));
            RequireProtectedVirtual(arcType, "PreparePath", typeof(void));
            RequireProtectedVirtual(arcType, "HitCell", typeof(void), typeof(IntVec3));
            Require(typeof(Verb_Spray), "get_AimAngleOverride", typeof(float?), false);
            Require(typeof(Verb_Spray), "WarmupComplete", typeof(void), false);
            Require(typeof(Verb), "Reset", typeof(void), false);
            Require(typeof(ThingWithComps), "GetGizmos", typeof(IEnumerable<Gizmo>), false);
            Require(typeof(ThingWithComps), "ExposeData", typeof(void), false);
            DirectShot.Install(harmony);
            Patch(harmony, typeof(Verb_Spray), "TryCastShot", nameof(ShotPrefix), true);
            Patch(harmony, typeof(Verb_Spray), "get_AimAngleOverride", nameof(AimPrefix), true);
            Patch(harmony, typeof(Verb_Spray), "WarmupComplete", nameof(WarmupPrefix), true);
            Patch(harmony, typeof(Verb), "Reset", nameof(ResetPostfix), false);
            Patch(harmony, typeof(ThingWithComps), "GetGizmos", nameof(GizmosPostfix), false);
            Patch(harmony, typeof(ThingWithComps), "ExposeData", nameof(ScribePostfix), false);
            Enabled = true;
            Log.Message("[DMS Arc Fire Mode] Installed player-only direct / original DMS spray controls; vanilla shot IL verified.");
        }
        catch (Exception exception)
        {
            harmony.UnpatchAll(ArcFireModesMod.HarmonyId);
            Enabled = false;
            Log.Error("[DMS Arc Fire Mode] Interface/IL mismatch: refused the entire patch group. " + exception);
        }
    }

    internal static MethodInfo Require(Type type, string name, Type returns, bool isStatic, params Type[] args)
    {
        MethodInfo method = AccessTools.DeclaredMethod(type, name, args)
            ?? throw new MissingMethodException(type.FullName, name);
        if (method.ReturnType != returns || method.IsStatic != isStatic)
            throw new InvalidOperationException($"Signature changed: {type.FullName}.{name}");
        return method;
    }

    private static void RequireProtectedVirtual(Type type, string name, Type returns, params Type[] args)
    {
        MethodInfo method = Require(type, name, returns, false, args);
        if (!method.IsFamily || !method.IsVirtual)
            throw new InvalidOperationException($"Protected virtual contract changed: {type.FullName}.{name}");
    }

    private static void Patch(Harmony harmony, Type type, string name, string handler, bool prefix)
    {
        MethodInfo target = AccessTools.DeclaredMethod(type, name, Type.EmptyTypes)
            ?? throw new MissingMethodException(type.FullName, name);
        var hook = new HarmonyMethod(typeof(ArcFireModes), handler);
        harmony.Patch(target, prefix: prefix ? hook : null, postfix: prefix ? null : hook);
    }

    public static bool Supports(Verb verb) => arcType?.IsInstanceOfType(verb) == true;
    public static bool Supports(ThingWithComps weapon) => arcType != null
        && weapon.def.Verbs.Any(properties => arcType.IsAssignableFrom(properties.verbClass));

    public static FireMode GetChoice(ThingWithComps weapon) => Choices.GetOrCreateValue(weapon).Mode;
    public static void SetChoice(ThingWithComps weapon, FireMode mode)
    {
        if (mode is < FireMode.Automatic or > FireMode.Spray) throw new ArgumentOutOfRangeException(nameof(mode));
        Choices.GetOrCreateValue(weapon).Mode = mode;
    }

    public static bool PlayerShooter(Thing? caster)
    {
        // Never let a player-selected enemy, a captured gun or a hostile operator opt into direct fire.
        if (caster == null || Faction.OfPlayer == null || caster.Faction != Faction.OfPlayer) return false;
        Pawn? operatorPawn = caster.TryGetComp<CompMannable>()?.ManningPawn;
        return operatorPawn == null || operatorPawn.Faction == Faction.OfPlayer;
    }

    public static bool IsDirect(Verb verb) => Enabled && Supports(verb) && PlayerShooter(verb.caster)
        && (verb.EquipmentSource is not ThingWithComps gun || GetChoice(gun) != FireMode.Spray);

    private static void WarmupPrefix(Verb_Spray __instance)
    {
        if (Supports(__instance)) DirectShot.ResetCache(__instance);
        // Always keep FFF PreparePath, including in direct mode: switching mid-burst is immediately safe.
    }

    private static void ResetPostfix(Verb __instance)
    {
        if (Supports(__instance)) DirectShot.ResetCache(__instance);
    }

    private static bool AimPrefix(Verb_Spray __instance, ref float? __result)
    {
        if (!IsDirect(__instance)) return true;
        __result = null;
        return false;
    }

    private static bool ShotPrefix(Verb_Spray __instance, ref bool __result)
    {
        if (!IsDirect(__instance)) return true;
        __result = DirectShot.Fire(__instance);
        if (__result)
        {
            // Verb_Spray consumes a charge on the LAST shot, not on every projectile.
            if (DirectShot.ShotsLeft(__instance) <= 1 && __instance.EquipmentSource is ThingWithComps gun)
            {
                gun.GetComp<CompChangeableProjectile>()?.Notify_ProjectileLaunched();
                gun.GetComp<CompApparelReloadable>()?.UsedOnce();
            }
            __instance.verbProps.sprayEffecterDef?.Spawn(__instance.caster.Position, __instance.CurrentTarget.Cell, __instance.caster.Map);
        }
        return false;
    }

    private static ThingWithComps? WeaponFor(ThingWithComps host) => host switch
    {
        Pawn pawn => pawn.equipment?.Primary,
        Building_TurretGun turret => turret.gun as ThingWithComps,
        _ => null
    };

    public static IEnumerable<Gizmo> Controls(ThingWithComps host)
    {
        if (!Enabled || !PlayerShooter(host)) yield break;
        ThingWithComps? gun = WeaponFor(host);
        if (gun == null || !Supports(gun)) yield break;
        bool direct = GetChoice(gun) != FireMode.Spray;
        yield return new Command_Action
        {
            defaultLabel = (direct ? "DMSAF_Direct" : "DMSAF_Spray").Translate(),
            defaultDesc = "DMSAF_Description".Translate((direct ? "DMSAF_Direct" : "DMSAF_Spray").Translate()),
            icon = direct ? TexCommand.Attack : TexCommand.FireAtWill,
            groupKey = direct ? 173802701 : 173802702,
            Order = 25f,
            action = () =>
            {
                // Captured target state makes a grouped click unify modes instead of toggling twice.
                if (Enabled && PlayerShooter(host) && ReferenceEquals(WeaponFor(host), gun))
                    SetChoice(gun, direct ? FireMode.Spray : FireMode.Direct);
            }
        };
    }

    private static void GizmosPostfix(ThingWithComps __instance, ref IEnumerable<Gizmo> __result)
        => __result = __result.Concat(Controls(__instance));

    private static void ScribePostfix(ThingWithComps __instance)
    {
        if (!Enabled || !Supports(__instance)) return;
        int mode = (int)GetChoice(__instance);
        Scribe_Values.Look(ref mode, "whitegiverDmsArcFireMode", 0);
        SetChoice(__instance, mode is >= 0 and <= 2 ? (FireMode)mode : FireMode.Automatic);
    }
}
