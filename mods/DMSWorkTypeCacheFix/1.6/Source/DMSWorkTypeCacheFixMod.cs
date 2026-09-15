using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Meidocho.DMSWorkTypeCacheFix;

public sealed class DMSWorkTypeCacheFixMod : Mod
{
    private const string HarmonyId = "meidocho.dms.worktypecachefix";

    public DMSWorkTypeCacheFixMod(ModContentPack content) : base(content)
    {
        Harmony harmony = new(HarmonyId);
        MethodInfo invalidate = AccessTools.Method(
            typeof(DMSWorkTypeCacheFixMod),
            nameof(Invalidate));
        MethodInfo setFaction = AccessTools.Method(
            typeof(Thing),
            nameof(Thing.SetFaction),
            new[] { typeof(Faction), typeof(Pawn) });
        MethodInfo setFactionDirect = AccessTools.Method(
            typeof(Thing),
            nameof(Thing.SetFactionDirect),
            new[] { typeof(Faction) });

        if (invalidate == null || setFaction == null || setFactionDirect == null)
        {
            Log.Error("[DMS Work Cache Fix] Could not resolve faction hooks; patch not applied.");
            return;
        }

        harmony.Patch(
            setFaction,
            postfix: new HarmonyMethod(invalidate) { priority = Priority.First });
        harmony.Patch(
            setFactionDirect,
            postfix: new HarmonyMethod(invalidate) { priority = Priority.First });
        Log.Message("[DMS Work Cache Fix] Mech faction work-cache invalidation applied.");
    }

    public static void Invalidate(Thing __instance)
    {
        if (!ModsConfig.BiotechActive || __instance is not Pawn pawn || !pawn.RaceProps.IsMechanoid)
        {
            return;
        }

        // ponytail: PawnGenerator sets faction before creating trackers; notify only initialized pawns, even off-map.
        if (pawn.health == null || pawn.mindState == null)
        {
            return;
        }

        pawn.Notify_DisabledWorkTypesChanged();
    }
}
