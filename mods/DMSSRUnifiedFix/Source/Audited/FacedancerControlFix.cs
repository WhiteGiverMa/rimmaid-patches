using System;
using System.Reflection;
using DMSSR.Hediffs;
using DMSSR.Patches;
using HarmonyLib;
using RimWorld;
using Verse;

namespace WhiteGiver.DMSSRUnifiedFix.Audited;

/// <summary>
/// DMSSR's four facechange control postfixes only ask "does this pawn have any active facechange".
/// That also matches Hydra encounter infiltrators and non-player-owned disguises, letting a hostile
/// pawn take player orders, receive a drafter and draft colony mechs. Re-apply the otherwise
/// identical gate using the comp's own <c>PlayerCanRevert</c> ownership test.
/// </summary>
internal static class FacedancerControlFix
{
    private static Func<HediffComp_FacedancerFacechange, bool> playerCanRevert = null!;

    internal static bool Installed { get; private set; }

    internal static void Apply(Harmony harmony)
    {
        PropertyInfo? property = AccessTools.Property(
            typeof(HediffComp_FacedancerFacechange), "PlayerCanRevert");
        playerCanRevert = property?.GetGetMethod(true)?.CreateDelegate<Func<HediffComp_FacedancerFacechange, bool>>()
            ?? throw new MissingMethodException("HediffComp_FacedancerFacechange.PlayerCanRevert");

        PatchProperty(harmony, typeof(Pawn), nameof(Pawn.IsColonistPlayerControlled), nameof(IsColonistPostfix));
        PatchProperty(harmony, typeof(Pawn), nameof(Pawn.IsColonyMechPlayerControlled), nameof(IsMechPostfix));
        PatchProperty(harmony, typeof(Pawn_DraftController), nameof(Pawn_DraftController.ShowDraftGizmo), nameof(DraftGizmoPostfix));
        PatchMethod(harmony, typeof(MechanitorUtility), nameof(MechanitorUtility.CanDraftMech), nameof(CanDraftMechPostfix));

        MethodInfo? ensureControllable = AccessTools.Method(typeof(HediffComp_FacedancerFacechange), "EnsureControllable");
        if (ensureControllable != null)
        {
            harmony.Patch(
                ensureControllable,
                prefix: new HarmonyMethod(AccessTools.Method(typeof(FacedancerControlFix), nameof(EnsureControllablePrefix))));
        }

        Installed = true;
    }

    private static bool EnsureControllablePrefix(HediffComp_FacedancerFacechange __instance)
    {
        return __instance != null && playerCanRevert(__instance);
    }

    private static void PatchProperty(Harmony harmony, Type type, string propertyName, string postfixName)
    {
        MethodInfo original = AccessTools.PropertyGetter(type, propertyName)
            ?? throw new MissingMethodException(type.FullName, propertyName);
        PatchMethod(harmony, original, postfixName);
    }

    private static void PatchMethod(Harmony harmony, Type type, string methodName, string postfixName)
    {
        MethodInfo original = AccessTools.Method(type, methodName)
            ?? throw new MissingMethodException(type.FullName, methodName);
        PatchMethod(harmony, original, postfixName);
    }

    private static void PatchMethod(Harmony harmony, MethodInfo original, string postfixName)
    {
        MethodInfo postfix = AccessTools.Method(typeof(FacedancerControlFix), postfixName)
            ?? throw new MissingMethodException("FacedancerControlFix." + postfixName);
        harmony.Patch(original, postfix: new HarmonyMethod(postfix) { priority = Priority.Last });
    }

    private static void IsColonistPostfix(Pawn __instance, ref bool __result)
    {
        if (__result && HasFacechange(__instance) && !IsPlayerOwnedFacechange(__instance))
        {
            __result = false;
        }
    }

    private static void IsMechPostfix(Pawn __instance, ref bool __result)
    {
        if (__result && HasFacechange(__instance) && !IsPlayerOwnedFacechange(__instance))
        {
            __result = false;
        }
    }

    private static void DraftGizmoPostfix(Pawn_DraftController __instance, ref bool __result)
    {
        if (__result && HasFacechange(__instance?.pawn) && !IsPlayerOwnedFacechange(__instance?.pawn))
        {
            __result = false;
        }
    }

    private static void CanDraftMechPostfix(Pawn mech, ref AcceptanceReport __result)
    {
        if (__result.Accepted && HasFacechange(mech) && !IsPlayerOwnedFacechange(mech))
        {
            __result = false;
        }
    }

    private static bool HasFacechange(Pawn? pawn)
    {
        return pawn != null && HediffComp_FacedancerFacechange.HasFacechange(pawn);
    }

    private static bool IsPlayerOwnedFacechange(Pawn? pawn)
    {
        if (pawn == null || !HediffComp_FacedancerFacechange.TryGetFacechangeComp(pawn, out HediffComp_FacedancerFacechange comp))
        {
            return false;
        }
        return playerCanRevert(comp);
    }
}
