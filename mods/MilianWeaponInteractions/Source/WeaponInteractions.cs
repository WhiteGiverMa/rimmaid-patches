using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace WhiteGiver.MilianWeaponInteractions;

public sealed class MilianWeaponInteractionsMod : Mod
{
    public MilianWeaponInteractionsMod(ModContentPack content) : base(content) => WeaponInteractions.Apply();
}

public static class WeaponInteractions
{
    public const string HarmonyId = "whitegiver.milian.weaponinteractions";
    private const string LogPrefix = "[Milian Weapon Interactions]";
    private static readonly MethodInfo Button = AccessTools.DeclaredMethod(typeof(Widgets), nameof(Widgets.ButtonInvisible),
        new[] { typeof(Rect), typeof(bool) });
    private static Type? weaponCompType;
    private static Type? equipDriverType;
    private static MethodInfo? available;
    private static Func<Pawn, bool>? isMilian;
    public static bool Ready { get; private set; }

    public static void Apply()
    {
        if (Ready) return;
        var harmony = new Harmony(HarmonyId);
        MethodInfo? cell = null;
        try
        {
            Type? column = AccessTools.TypeByName("Milira.PawnColumnWorker_EquipmentMilian");
            weaponCompType = AccessTools.TypeByName("Milira.CompTargetableWeapon");
            Type? utility = AccessTools.TypeByName("Milira.MilianUtility");
            equipDriverType = AccessTools.TypeByName("Milira.JobDriver_EquipMilian");
            if (column == null || weaponCompType == null || utility == null || equipDriverType == null)
            {
                Log.Error($"{LogPrefix} Milira equipment types are unavailable; both new interactions are disabled.");
                return;
            }

            cell = AccessTools.DeclaredMethod(column, "DoCell", new[] { typeof(Rect), typeof(Pawn), typeof(PawnTable) });
            MethodInfo? menu = AccessTools.DeclaredMethod(column, "GetEquipmentFloatMenu", new[] { typeof(Pawn) });
            available = AccessTools.DeclaredMethod(weaponCompType, "Available", new[] { typeof(Thing), typeof(Pawn) });
            MethodInfo? identity = AccessTools.DeclaredMethod(utility, "IsMilian", new[] { typeof(Pawn) });
            if (cell == null || cell.IsStatic || cell.ReturnType != typeof(void)
                || menu == null || menu.IsStatic || menu.ReturnType != typeof(List<FloatMenuOption>)
                || available == null || available.IsStatic || available.ReturnType != typeof(bool)
                || identity == null || !identity.IsStatic || identity.ReturnType != typeof(bool)
                || !typeof(ThingComp).IsAssignableFrom(weaponCompType) || !typeof(JobDriver).IsAssignableFrom(equipDriverType))
                throw new MissingMethodException("Milira weapon-column, identity, eligibility or equip-job contract changed.");

            ValidateButtonCall(PatchProcessor.GetOriginalInstructions(cell));
            isMilian = identity.CreateDelegate<Func<Pawn, bool>>();
            if (Harmony.GetPatchInfo(cell)?.Owners.Contains(HarmonyId) != true)
                harmony.Patch(cell, transpiler: new HarmonyMethod(typeof(WeaponInteractions), nameof(Transpiler)));
            Ready = true;
            Log.Message($"{LogPrefix} Weapon-cell picker and selected-Milian ground-weapon menu enabled.");
        }
        catch (Exception exception)
        {
            if (cell != null) harmony.Unpatch(cell, HarmonyPatchType.Transpiler, HarmonyId);
            Ready = false;
            Log.Error($"{LogPrefix} Could not apply weapon interactions; both new entries are disabled: {exception}");
        }
    }

    private static void ValidateButtonCall(IEnumerable<CodeInstruction> instructions)
    {
        List<CodeInstruction> codes = instructions.ToList();
        int[] matches = codes.Select((code, index) => (code, index)).Where(item => item.code.Calls(Button)).Select(item => item.index).ToArray();
        if (matches.Length != 1 || matches[0] < 2 || !codes[matches[0] - 2].IsLdarg(1)
            || !codes[matches[0] - 1].LoadsConstant(1L))
            throw new InvalidOperationException("Expected exactly one ButtonInvisible(Rect, true) call using the weapon-cell rect.");
    }

    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        List<CodeInstruction> codes = instructions.ToList();
        ValidateButtonCall(codes);
        MethodInfo replacement = AccessTools.DeclaredMethod(typeof(WeaponInteractions), nameof(SlotButton));
        foreach (CodeInstruction code in codes)
        {
            if (code.Calls(Button))
            {
                yield return new CodeInstruction(OpCodes.Ldarg_2).MoveLabelsFrom(code).MoveBlocksFrom(code);
                code.opcode = OpCodes.Call;
                code.operand = replacement;
            }
            yield return code;
        }
    }

    public static bool SlotButton(Rect rect, bool doMouseoverSound, Pawn pawn)
    {
        TooltipHandler.TipRegion(rect, "MWI_WeaponSlotTip".Translate());
        int button = Event.current.button;
        if (!Widgets.ButtonInvisible(rect, doMouseoverSound)) return false;
        // ponytail: 原按钮负责按下/抬起与事件消费，右键只回到原菜单，不重写绘制或取消选项。
        if (button == 1) return true;
        if (button != 0) return false;
        BeginSelection(pawn);
        return false;
    }

    public static void BeginSelection(Pawn pawn)
    {
        if (!CanCommand(pawn))
        {
            Reject("MWI_NotControlled".Translate(), pawn);
            return;
        }
        Find.MainTabsRoot.SetCurrentTab(null);
        Find.DesignatorManager.Deselect();
        Find.Targeter.BeginTargeting(TargetParameters(pawn), target => TryOrder(pawn, target.Thing),
            caster: pawn, requiresCastedSelected: false);
    }

    public static TargetingParameters TargetParameters(Pawn pawn) => new()
    {
        canTargetLocations = false,
        canTargetPawns = false,
        canTargetBuildings = false,
        canTargetItems = true,
        canTargetSelf = false,
        mapObjectTargetsMustBeAutoAttackable = false,
        thingCategory = ThingCategory.Item,
        validator = target => CanEquip(pawn, target.Thing, out _)
    };

    public static bool CanCommand(Pawn pawn) => Ready && pawn.Spawned && !pawn.Dead && !pawn.Downed
        && pawn.equipment != null && pawn.jobs != null && pawn.carryTracker != null && pawn.pather != null
        && pawn.IsColonyMechPlayerControlled && isMilian!(pawn) && WeaponComp(pawn) != null
        && FloatMenuMakerMap.ShouldGenerateFloatMenuForPawn(pawn).Accepted;

    private static ThingComp? WeaponComp(Pawn pawn) => pawn.AllComps.FirstOrDefault(comp => weaponCompType!.IsInstanceOfType(comp));

    public static bool CanEquip(Pawn pawn, Thing? thing, out string reason)
    {
        reason = "";
        if (!CanCommand(pawn))
            reason = "MWI_NotControlled".Translate();
        else if (thing is not ThingWithComps || !thing.def.IsWeapon || !thing.HasComp<CompEquippable>()
                 || !thing.Spawned || thing.Map != pawn.Map || thing.Position.Fogged(pawn.Map))
            reason = "MWI_TargetUnavailable".Translate();
        else if (thing.stackCount != 1)
            reason = "MWI_StackedWeapon".Translate();
        else if (!(bool)available!.Invoke(WeaponComp(pawn), new object[] { thing, pawn })!)
            reason = "MWI_WeaponNotSupported".Translate();
        else if (!EquipmentUtility.CanEquip(thing, pawn, out reason))
            return false;
        else if (!pawn.CanReach(thing, PathEndMode.Touch, Danger.Deadly))
            reason = "NoPath".Translate();
        else if (!pawn.CanReserve(pawn) || !pawn.CanReserve(thing))
            reason = "Reserved".Translate();
        return reason.NullOrEmpty();
    }

    public static bool TryOrder(Pawn pawn, Thing? weapon)
    {
        if (!CanEquip(pawn, weapon, out string reason))
        {
            Reject(reason, pawn);
            return false;
        }
        JobDef? jobDef = DefDatabase<JobDef>.GetNamedSilentFail("Milira_EquipMilian");
        if (jobDef == null || jobDef.driverClass != equipDriverType)
        {
            Log.Error($"{LogPrefix} Milira_EquipMilian or its driver changed; no equip order was issued.");
            Reject("MWI_JobUnavailable".Translate(), pawn);
            return false;
        }
        weapon!.SetForbidden(false);
        Job job = JobMaker.MakeJob(jobDef, pawn, weapon);
        job.count = 1;
        bool accepted = pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
        if (!accepted) Reject("MWI_OrderFailed".Translate(), pawn);
        return accepted;
    }

    private static void Reject(string reason, Pawn pawn) => Messages.Message(reason, pawn, MessageTypeDefOf.RejectInput, false);
}

public sealed class FloatMenuOptionProvider_MilianWeapons : FloatMenuOptionProvider
{
    protected override bool Drafted => true;
    protected override bool Undrafted => true;
    protected override bool Multiselect => false;
    protected override bool MechanoidCanDo => true;

    public override bool SelectedPawnValid(Pawn pawn, FloatMenuContext context) =>
        base.SelectedPawnValid(pawn, context) && WeaponInteractions.CanCommand(pawn);

    protected override bool AppliesInt(FloatMenuContext context) =>
        context.FirstSelectedPawn is Pawn pawn && WeaponInteractions.CanCommand(pawn);

    protected override FloatMenuOption? GetSingleOptionFor(Thing clickedThing, FloatMenuContext context)
    {
        Pawn? pawn = context.FirstSelectedPawn;
        if (pawn == null || clickedThing is not ThingWithComps || !clickedThing.def.IsWeapon) return null;
        bool allowed = WeaponInteractions.CanEquip(pawn, clickedThing, out string reason);
        string label = allowed ? "Equip".Translate(clickedThing.LabelShort)
            : "CannotEquip".Translate(clickedThing.LabelShort) + ": " + reason;
        return new FloatMenuOption(label, allowed ? () => WeaponInteractions.TryOrder(pawn, clickedThing) : null)
        {
            revalidateClickTarget = clickedThing
        };
    }
}
