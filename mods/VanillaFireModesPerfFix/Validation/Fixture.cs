using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using VFM_VanillaFireModes;
using VFM_VanillaFireModes.Comps;
using VFM_VanillaFireModes.Settings;
using Verse;

namespace VanillaFireModesPerfFix.Validation;

/// <summary>
/// Offline RimWorld 1.7 fixture built from the real game and the real Vanilla Fire Modes assembly: real
/// <see cref="Pawn"/>, <see cref="ThingWithComps"/>, <see cref="CompEquippable"/>, <see cref="Verb_Shoot"/>,
/// <see cref="Verb_MeleeAttackDamage"/> and <see cref="VFM_PawnCompFireMode"/> instances, a real loaded
/// <see cref="VanillaFireModesModSetting"/>, and a capture hook on the private
/// <c>Verb.TryCastNextBurstShot</c> so a burst can be started without a map, projectiles or a battle log.
/// </summary>
internal static class Fixture
{
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

    private static AccessTools.FieldRef<Verb, int> burstShotsLeft = null!;
    private static int nextId = 100;

    internal static void Setup()
    {
        Current.ProgramState = ProgramState.Entry;
        Current.Game = (Game)RuntimeHelpers.GetUninitializedObject(typeof(Game));
        Set(typeof(Game), "maps", Current.Game, new List<Map>());
        Set(typeof(Game), "tickManager", Current.Game, new TickManager());
        Set(typeof(Game), "uniqueIDsManager", Current.Game, new UniqueIDsManager());
        Set(typeof(Game), "battleLog", Current.Game, new BattleLog());

        // No ModsConfig.xml is read: the bound Assembly-CSharp has Verse.ModsConfig..cctor emptied, and the
        // active-mod set below is process-local.
        Set(typeof(ModsConfig), "activeModsHashSet", null, new HashSet<string>
        {
            "brrainz.harmony", "aliza.vanillafiremodes", "whitegiver.vfm.perffix",
        });

        // Prefs.data backs Log.Warning's Prefs.OpenLogOnWarnings read.
        Set(typeof(Prefs), "data", null, RuntimeHelpers.GetUninitializedObject(typeof(PrefsData)));

        VanillaFireModes.settings = new VanillaFireModesModSetting();
        burstShotsLeft = AccessTools.FieldRefAccess<Verb, int>("burstShotsLeft");
    }

    /// <summary>Captures the burst length the vanilla warm-up just assigned and stops before real projectile work.</summary>
    internal static void InstallBurstCapture()
    {
        Harmony harmony = new("whitegiver.vfm.perffix.validation.capture");
        harmony.Patch(
            AccessTools.Method(typeof(Verb), "TryCastNextBurstShot"),
            prefix: new HarmonyMethod(typeof(Fixture), nameof(CaptureBurst)));
        harmony.Patch(
            AccessTools.Method(typeof(BattleLog), nameof(BattleLog.Add)),
            prefix: new HarmonyMethod(typeof(Fixture), nameof(SkipBattleLog)));
    }

    private static bool SkipBattleLog() => false;

    internal static int CapturedBurstShotsLeft { get; private set; }

    internal static int ShotsLeft(Verb verb) => burstShotsLeft(verb);

    private static bool CaptureBurst(Verb __instance)
    {
        CapturedBurstShotsLeft = burstShotsLeft(__instance);
        return false;
    }

    internal static void ConfigureBurstOptions(BurstShotOption option)
    {
        VanillaFireModesModSetting settings = VanillaFireModes.settings;
        foreach (string prefix in new[] { "default", "precision", "burst", "suppression" })
        {
            SetField(settings, prefix + "BurstOption", option);
            SetField(settings, prefix + "BurstLinearMultiplier", 1.75f);
            SetField(settings, prefix + "BurstAdditiveBonus", 2);
            SetField(settings, prefix + "BurstTentMaxMultiplier", 2.5f);
            SetField(settings, prefix + "BurstTentSlopeK", 0.05f);
            SetField(settings, prefix + "BurstTentPeakOffset", 4);
            SetField(settings, prefix + "BurstAdaptiveBonus", 3);
            SetField(settings, prefix + "BurstAdaptivePeakOffset", 3);
        }
    }

    internal static void SetMode(Pawn pawn, VFM_FireMode mode)
    {
        VFM_PawnCompFireMode? comp = TryGetFireModeComp(pawn);
        if (comp != null)
        {
            comp.curMode = mode;
        }
    }

    /// <summary>
    /// ThingWithComps.GetComp&lt;T&gt; indexes comps[0] whenever the list is non-null, so the fixture must leave the
    /// field null (never an empty list) for comp-less subjects — exactly like a real def without comps.
    /// </summary>
    internal static VFM_PawnCompFireMode? TryGetFireModeComp(Pawn pawn)
    {
        return ((ThingWithComps)pawn).GetComp<VFM_PawnCompFireMode>();
    }

    // ------------------------------------------------------------------ factories

    internal static Pawn NewPawn(string name, VFM_FireMode mode, bool withFireModeComp = true)
    {
        Pawn pawn = (Pawn)RuntimeHelpers.GetUninitializedObject(typeof(Pawn));
        pawn.def = new ThingDef
        {
            defName = name,
            label = name,
            category = ThingCategory.Pawn,
            race = new RaceProperties { intelligence = Intelligence.ToolUser },
        };
        pawn.thingIDNumber = nextId++;
        Set(typeof(Thing), "mapIndexOrState", pawn, (sbyte)(-1));

        List<ThingComp> comps = new();
        if (withFireModeComp)
        {
            VFM_PawnCompFireMode comp = new() { parent = pawn };
            comp.curMode = mode;
            comps.Add(comp);
        }

        if (comps.Count > 0)
        {
            Set(typeof(ThingWithComps), "comps", pawn, comps);
        }

        return pawn;
    }

    /// <summary>A real ThingWithComps weapon whose only comp is the real CompEquippable (so EquipmentSource works).</summary>
    internal static ThingWithComps NewWeapon(string defName, int burstShotCount, Type verbClass)
    {
        VerbProperties props = new()
        {
            verbClass = verbClass,
            burstShotCount = burstShotCount,
            warmupTime = 1.5f,
            defaultProjectile = new ThingDef { defName = defName + "_Projectile", projectile = new ProjectileProperties() },
        };

        ThingDef def = new()
        {
            defName = defName,
            label = defName,
            category = ThingCategory.Item,
            thingClass = typeof(ThingWithComps),
            equipmentType = EquipmentType.Primary,
        };
        Set(typeof(ThingDef), "verbs", def, new List<VerbProperties> { props });

        ThingWithComps weapon = new() { def = def, thingIDNumber = nextId++ };
        CompEquippable equippable = new() { parent = weapon, props = new CompProperties(typeof(CompEquippable)) };
        Set(typeof(ThingWithComps), "comps", weapon, new List<ThingComp> { equippable });
        return weapon;
    }

    internal static ThingWithComps NewRangedWeapon(string defName, int burstShotCount)
        => NewWeapon(defName, burstShotCount, typeof(Verb_Shoot));

    internal static ThingWithComps NewMeleeWeapon(string defName)
        => NewWeapon(defName, 1, typeof(Verb_MeleeAttackDamage));

    internal static Verb_Shoot NewRangedVerb(Pawn pawn, ThingWithComps weapon)
    {
        return new Verb_Shoot
        {
            verbProps = weapon.def.Verbs[0],
            verbTracker = weapon.GetComp<CompEquippable>().verbTracker,
            caster = pawn,
        };
    }

    internal static Verb_MeleeAttackDamage NewMeleeVerb(Pawn pawn, ThingWithComps weapon)
    {
        return new Verb_MeleeAttackDamage
        {
            verbProps = weapon.def.Verbs[0],
            verbTracker = weapon.GetComp<CompEquippable>().verbTracker,
            caster = pawn,
        };
    }

    internal static Verb_ShootOneUse NewOneUseVerb(Pawn pawn, ThingWithComps weapon)
    {
        return new Verb_ShootOneUse
        {
            verbProps = weapon.def.Verbs[0],
            verbTracker = weapon.GetComp<CompEquippable>().verbTracker,
            caster = pawn,
        };
    }

    /// <summary>A non-pawn caster (turret-like): VFM must not touch its burst either.</summary>
    internal static ThingWithComps NewBuildingCaster(string defName)
    {
        ThingWithComps building = new()
        {
            def = new ThingDef { defName = defName, label = defName, category = ThingCategory.Building, thingClass = typeof(ThingWithComps) },
            thingIDNumber = nextId++,
        };
        Set(typeof(Thing), "mapIndexOrState", building, (sbyte)(-1));
        return building;
    }

    /// <summary>
    /// The reported colony shape: 1,200 idle verbs, most of them animal/melee verbs that still tick every tick.
    /// </summary>
    internal static List<Verb> NewIdleColony(int meleeVerbs, int rangedVerbs)
    {
        List<Verb> verbs = new(meleeVerbs + rangedVerbs);
        for (int i = 0; i < meleeVerbs; i++)
        {
            Pawn animal = NewPawn("ValidationAnimal" + i, VFM_FireMode.Default);
            ThingWithComps beak = NewMeleeWeapon("ValidationBeak" + i);
            verbs.Add(NewMeleeVerb(animal, beak));
        }

        for (int i = 0; i < rangedVerbs; i++)
        {
            Pawn shooter = NewPawn("ValidationShooter" + i, VFM_FireMode.Burst);
            ThingWithComps rifle = NewRangedWeapon("ValidationRifle" + i, 3);
            verbs.Add(NewRangedVerb(shooter, rifle));
        }

        return verbs;
    }

    /// <summary>
    /// Fires one burst per eligible verb so the burst table really holds entries. Without this the table is empty
    /// and a lookup is a one-compare miss, which would understate VFM's cost in a colony that has ever shot.
    /// </summary>
    internal static void PrimeBursts(List<Verb> verbs)
    {
        foreach (Verb verb in verbs)
        {
            if (verb.verbProps == null || verb.verbProps.IsMeleeAttack)
            {
                continue;
            }

            verb.Reset();
            verb.WarmupComplete();
            verb.Reset();
        }
    }

    // ------------------------------------------------------------------ helpers

    internal static void Set(Type type, string name, object? instance, object? value)
    {
        FieldInfo field = AccessTools.Field(type, name)
            ?? throw new MissingFieldException(type.FullName, name);
        field.SetValue(instance, value);
    }

    private static void SetField(object target, string name, object value)
    {
        FieldInfo field = AccessTools.Field(target.GetType(), name)
            ?? throw new MissingFieldException(target.GetType().FullName, name);
        field.SetValue(target, value);
    }
}
