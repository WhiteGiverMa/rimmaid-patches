using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace WhiteGiver.EquippableAbilityPsycastLoadFix;

public static class EquippableAbilityPsycastLoadFix
{
    public const string HarmonyId = "whitegiver.equippableability.psycastloadfix";
    private const string LogPrefix = "[EquippableAbility Psycast Load Fix]";

    private const BindingFlags CtorBindings =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static readonly Type[] PawnSignature = { typeof(Pawn) };
    private static readonly Type[] PawnDefSignature = { typeof(Pawn), typeof(AbilityDef) };
    private static readonly Type[] PawnPreceptDefSignature =
        { typeof(Pawn), typeof(Precept), typeof(AbilityDef) };

    private static readonly FieldInfo AbilityField =
        AccessTools.Field(typeof(CompEquippableAbility), "ability");

    public static void Apply()
    {
        MethodInfo target = AccessTools.Method(typeof(CompEquippableAbility), nameof(CompEquippableAbility.PostExposeData));
        if (target == null || AbilityField == null)
        {
            Log.Error(LogPrefix + " expected members not found; patch skipped. The game may have changed CompEquippableAbility.");
            return;
        }

        Harmony harmony = new(HarmonyId);
        harmony.Patch(target, prefix: new HarmonyMethod(typeof(EquippableAbilityPsycastLoadFix), nameof(Prefix)));
        Log.Message(LogPrefix + " active: equippable abilities whose class needs constructor arguments can load from saves again.");
    }

    // CompEquippableAbility.PostExposeData deep-saves its ability without ctorArgs. Vanilla ability
    // classes have a parameterless constructor, so that works for them; Psycast only has (Pawn) and
    // (Pawn, AbilityDef), so loading a save logs "Constructor on type 'RimWorld.Psycast' not found"
    // and the ability is rebuilt empty. Re-run the same body with the matching constructor args.
    // ponytail: full body replacement; if vanilla changes PostExposeData, diff against it again.
    public static bool Prefix(CompEquippableAbility __instance)
    {
        if (Scribe.mode == LoadSaveMode.Saving)
        {
            return true;
        }

        // ponytail: mirrors CompEquippable.PostExposeData; ThingComp base is empty; re-diff on game updates.
        Scribe_Deep.Look(ref __instance.verbTracker, "verbTracker", __instance);

        if (Scribe.mode == LoadSaveMode.LoadingVars)
        {
            Ability? ability = (Ability?)AbilityField.GetValue(__instance);
            Scribe_Deep.Look(ref ability, "ability", BuildCtorArgs(__instance));
            AbilityField.SetValue(__instance, ability);
        }
        else if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            Pawn? holder = GetHolder(__instance);
            Ability? ability = __instance.AbilityForReading;
            if (holder != null && ability != null)
            {
                ability.pawn = holder;
                ability.verb.caster = holder;
            }
        }

        return false;
    }

    private static object[] BuildCtorArgs(CompEquippableAbility comp)
    {
        AbilityDef? def = (comp.props as CompProperties_EquippableAbility)?.abilityDef;
        return BuildCtorArgsFor(def?.abilityClass, GetHolder(comp), def);
    }

    internal static object[] BuildCtorArgsFor(Type? abilityClass, Pawn? holder, AbilityDef? def)
    {
        if (abilityClass == null)
        {
            return Array.Empty<object>();
        }

        // Keep the original no-arg path whenever it exists, so vanilla abilities are untouched.
        if (HasCtor(abilityClass, Type.EmptyTypes))
        {
            return Array.Empty<object>();
        }

        if (HasCtor(abilityClass, PawnSignature))
        {
            return new object[] { holder! };
        }
        if (HasCtor(abilityClass, PawnDefSignature))
        {
            return new object[] { holder!, def! };
        }
        if (HasCtor(abilityClass, PawnPreceptDefSignature))
        {
            return new object[] { holder!, null!, def! };
        }

        return Array.Empty<object>();
    }

    private static bool HasCtor(Type type, Type[] signature)
    {
        return type.GetConstructor(CtorBindings, null, signature, null) != null;
    }

    private static Pawn? GetHolder(CompEquippableAbility comp)
    {
        // Same lookup as CompEquippable.Holder, which is protected.
        return (comp.parent?.ParentHolder as Pawn_EquipmentTracker)?.pawn;
    }
}

public sealed class EquippableAbilityPsycastLoadFixMod : Mod
{
    public EquippableAbilityPsycastLoadFixMod(ModContentPack content) : base(content)
    {
        EquippableAbilityPsycastLoadFix.Apply();
    }
}
