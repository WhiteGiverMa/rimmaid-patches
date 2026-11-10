using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace WhiteGiver.PocketSandEquipReservationFix;

public sealed class PocketSandEquipReservationFixMod : Mod
{
    public PocketSandEquipReservationFixMod(ModContentPack content) : base(content)
    {
        EquipReservationFix.Apply();
    }
}

public static class EquipReservationFix
{
    public const string HarmonyId = "whitegiver.pocketsand.equipreservationfix";
    private const string LogPrefix = "[Pocket Sand Equip Reservation Fix]";
    private static bool applied;

    public static void Apply()
    {
        if (applied) return;

        Type? driver = AccessTools.TypeByName("PocketSand.JobDriver_Equip");
        MethodInfo? target = driver?.GetMethod(nameof(JobDriver.TryMakePreToilReservations),
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
            null, new[] { typeof(bool) }, null);
        if (driver == null || !typeof(JobDriver).IsAssignableFrom(driver)
            || target == null || target.ReturnType != typeof(bool))
        {
            Log.Warning($"{LogPrefix} Expected PocketSand.JobDriver_Equip.TryMakePreToilReservations(bool) not found; patch skipped.");
            return;
        }

        try
        {
            new Harmony(HarmonyId).Patch(target,
                postfix: new HarmonyMethod(typeof(EquipReservationFix), nameof(ReserveEquipment)));
            applied = true;
            Log.Message($"{LogPrefix} Active: ground weapons are reserved before walking; inventory swaps are unchanged.");
        }
        catch (Exception exception)
        {
            Log.Error($"{LogPrefix} Could not apply equipment reservation patch: {exception}");
        }
    }

    public static void ReserveEquipment(JobDriver __instance, bool errorOnFailed, ref bool __result)
    {
        if (!__result) return;

        Thing? equipment = __instance.job?.targetA.Thing;
        if (equipment == null) return;

        // Inventory swaps need no map reservation; outfit stands reserve their source like vanilla.
        Thing? target = equipment.Spawned ? equipment : equipment.ParentHolder as Building_OutfitStand;
        if (target != null)
            __result = __instance.pawn.Reserve(target, __instance.job, 1, -1, null, errorOnFailed);
    }
}
