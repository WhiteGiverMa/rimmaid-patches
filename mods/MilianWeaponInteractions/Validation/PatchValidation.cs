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
using Verse.AI;
using Verse.Sound;

public static class PatchValidation
{
    private static Type interactions = null!;
    private static bool reachable = true;
    private static bool controlled = true;
    private static bool fogged;
    private static bool hit;
    private static int buttons;
    private static Job? ordered;
    private static Pawn? actor;
    private static string? rejection;
    private static bool acceptJob = true;

    public static void Run(Assembly patch)
    {
        interactions = patch.GetType("WhiteGiver.MilianWeaponInteractions.WeaponInteractions", true)!;
        var qa = new Harmony(Driver.Owner + ".validation");
        qa.Patch(AccessTools.DeclaredMethod(typeof(Translator), nameof(Translator.TryTranslate), new[] { typeof(string), typeof(TaggedString).MakeByRefType() }),
            prefix: Hook(nameof(NoTranslation)));
        qa.Patch(AccessTools.DeclaredMethod(typeof(FloatMenuOption), nameof(FloatMenuOption.SetSizeMode)), prefix: Hook(nameof(Skip)));
        qa.Patch(AccessTools.DeclaredMethod(typeof(ColoredText), nameof(ColoredText.Resolve), new[] { typeof(TaggedString) }), prefix: Hook(nameof(PlainText)));
        qa.Patch(AccessTools.PropertyGetter(typeof(CompOverseerSubject), nameof(CompOverseerSubject.State)), prefix: Hook(nameof(OverseerState)));
        qa.Patch(AccessTools.DeclaredMethod(typeof(FogGrid), nameof(FogGrid.IsFogged), new[] { typeof(IntVec3) }), prefix: Hook(nameof(Fog)));
        qa.Patch(AccessTools.DeclaredMethod(typeof(ReachabilityUtility), nameof(ReachabilityUtility.CanReach),
            new[] { typeof(Pawn), typeof(LocalTargetInfo), typeof(PathEndMode), typeof(Danger), typeof(bool), typeof(bool), typeof(TraverseMode) }), prefix: Hook(nameof(Reachable)));
        qa.Patch(AccessTools.DeclaredMethod(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.TryTakeOrderedJob),
            new[] { typeof(Job), typeof(JobTag?), typeof(bool) }), prefix: Hook(nameof(Order)));
        qa.Patch(AccessTools.DeclaredMethod(typeof(Messages), nameof(Messages.Message),
            new[] { typeof(string), typeof(LookTargets), typeof(MessageTypeDef), typeof(bool) }), prefix: Hook(nameof(Message)));
        qa.Patch(AccessTools.DeclaredMethod(typeof(SoundStarter), nameof(SoundStarter.PlayOneShotOnCamera)), prefix: Hook(nameof(Skip)));
        qa.Patch(AccessTools.DeclaredMethod(typeof(CompForbiddable), "UpdateOverlayHandle"), prefix: Hook(nameof(Skip)));

        Type mod = patch.GetType("WhiteGiver.MilianWeaponInteractions.MilianWeaponInteractionsMod", true)!;
        Activator.CreateInstance(mod, new object?[] { null });
        MethodInfo cell = AccessTools.DeclaredMethod(Driver.Milira.GetType("Milira.PawnColumnWorker_EquipmentMilian", true)!, "DoCell");
        MethodBase[] owned = Harmony.GetAllPatchedMethods().Where(method => Harmony.GetPatchInfo(method)?.Owners.Contains(Driver.Owner) == true).ToArray();
        Driver.Check(owned.Length == 1 && owned[0] == cell && (bool)interactions.GetProperty("Ready")!.GetValue(null)!, "Actual Mod did not install exactly the weapon-cell patch.");
        Activator.CreateInstance(mod, new object?[] { null });
        Driver.Check(Harmony.GetPatchInfo(cell)!.Transpilers.Count(item => item.owner == Driver.Owner) == 1, "Repeated construction duplicated the patch.");
        ValidateIl(cell);
        ValidateOrders(patch);
        ValidateApparelOrders(patch);
        ValidateButtons(qa);
        ValidateOriginalMenu();
        ValidateSling(qa);
        ValidateDressTransfer(qa, patch);
        Console.WriteLine("PASS: real Mod registration, drift rejection, original-menu preservation, shared job ordering and both interaction entry points.");
    }

    private static void ValidateIl(MethodInfo cell)
    {
        List<CodeInstruction> original = PatchProcessor.GetOriginalInstructions(cell);
        MethodInfo button = AccessTools.DeclaredMethod(typeof(Widgets), nameof(Widgets.ButtonInvisible), new[] { typeof(Rect), typeof(bool) });
        MethodInfo slot = interactions.GetMethod("SlotButton")!;
        MethodInfo transpiler = interactions.GetMethod("Transpiler")!;
        var changed = ((IEnumerable<CodeInstruction>)transpiler.Invoke(null, new object[] { original.Select(code => new CodeInstruction(code)).ToList() })!).ToList();
        Driver.Check(original.Count(code => code.Calls(button)) == 1 && changed.Count(code => code.Calls(slot)) == 1
            && !changed.Any(code => code.Calls(button)), "Button replacement count differs.");
        Driver.Check(changed.Where(code => code.operand is MethodInfo method && method.Name is "GetEquipmentFloatMenu" or "DoCell")
            .Select(code => code.operand).SequenceEqual(original.Where(code => code.operand is MethodInfo method && method.Name is "GetEquipmentFloatMenu" or "DoCell").Select(code => code.operand)),
            "Original menu/base rendering calls changed.");
        foreach (List<CodeInstruction> invalid in new[]
        {
            original.Where(code => !code.Calls(button)).Select(code => new CodeInstruction(code)).ToList(),
            original.Concat(new[] { new CodeInstruction(OpCodes.Call, button) }).Select(code => new CodeInstruction(code)).ToList()
        })
        {
            bool rejected = false;
            try { _ = ((IEnumerable<CodeInstruction>)transpiler.Invoke(null, new object[] { invalid })!).ToList(); }
            catch (TargetInvocationException exception) when (exception.InnerException is InvalidOperationException) { rejected = true; }
            catch (InvalidOperationException) { rejected = true; }
            Driver.Check(rejected, "Changed upstream IL was accepted.");
        }
        var final = PatchProcessor.GetCurrentInstructions(cell);
        Driver.Check(final.Count(code => code.Calls(slot)) == 1, "Real Harmony replacement is not present.");
    }

    private static void ValidateOrders(Assembly patch)
    {
        Pawn pawn = Driver.NewMilian();
        ThingWithComps gun = Driver.NewWeapon();
        ThingWithComps sword = Driver.NewWeapon(false);
        Driver.Check(CanEquip(pawn, gun), "Compatible ground gun rejected.");
        Driver.Check(!CanEquip(pawn, sword), "Wrong weapon class accepted.");
        gun.stackCount = 2;
        Driver.Check(!CanEquip(pawn, gun), "Stacked weapon accepted.");
        gun.stackCount = 1;
        reachable = false;
        Driver.Check(!CanEquip(pawn, gun), "Unreachable weapon accepted.");
        reachable = true;
        fogged = true;
        Driver.Check(!CanEquip(pawn, gun), "Fogged weapon accepted.");
        fogged = false;
        controlled = false;
        Driver.Check(!CanEquip(pawn, gun), "Uncontrolled pawn accepted.");
        controlled = true;
        Pawn foreign = Driver.NewMilian(bodyName: "OtherMechBody");
        Driver.Check(!CanEquip(foreign, gun), "Unrelated mech accepted.");
        pawn.health.hediffSet.hediffs.Add(new Hediff { def = Driver.Adaptive, pawn = pawn });
        Driver.Check(CanEquip(pawn, sword), "Adaptive arm no longer honored by new entry.");
        pawn.health.hediffSet.hediffs.Clear();
        Pawn reserver = Driver.NewMilian();
        Driver.Map.reservationManager.Reserve(reserver, JobMaker.MakeJob(Driver.EquipDef), gun);
        Driver.Check(!CanEquip(pawn, gun), "Reserved weapon accepted.");
        Driver.Map.reservationManager.ReleaseAllForTarget(gun);

        Type providerType = patch.GetType("WhiteGiver.MilianWeaponInteractions.FloatMenuOptionProvider_MilianWeapons", true)!;
        var provider = (FloatMenuOptionProvider)Activator.CreateInstance(providerType)!;
        var context = (FloatMenuContext)RuntimeHelpers.GetUninitializedObject(typeof(FloatMenuContext));
        context.allSelectedPawns = new List<Pawn> { pawn };
        context.map = Driver.Map;
        Driver.Set(typeof(FloatMenuContext), "cachedClickedCell", context, gun.Position);
        FloatMenuMakerMap.currentProvider = provider;
        Driver.Check(provider.SelectedPawnValid(pawn, context) && provider.Applies(context), "Single Milian provider not applicable.");
        context.allSelectedPawns.Add(reserver);
        Driver.Check(!provider.Applies(context), "Multi-selection accepted.");
        context.allSelectedPawns.Remove(reserver);
        FloatMenuOption option = provider.GetOptionsFor(gun, context).Single();
        Driver.Check(option.action != null && !option.Disabled, "Compatible equip option disabled.");
        FloatMenuMakerMap.currentProvider = new NeverSelectedProvider();
        ordered = null;
        option.action!();
        Driver.Check(ordered?.def == Driver.EquipDef && ordered.targetA.Thing == pawn && ordered.targetB.Thing == gun
            && ordered.count == 1 && actor == pawn,
            $"Provider action mismatch: job={ordered?.def?.defName}, A={ordered?.targetA.Thing == pawn}, B={ordered?.targetB.Thing == gun}, count={ordered?.count}, actor={actor == pawn}, rejection={rejection}.");
        gun.DeSpawn();
        ordered = null;
        option.action!();
        Driver.Check(ordered == null && rejection != null, "Stale menu target still issued a job.");
        sword.DeSpawn();
        FloatMenuMakerMap.currentProvider = null;
    }

    private static void ValidateButtons(Harmony qa)
    {
        qa.Patch(AccessTools.DeclaredMethod(typeof(Widgets), nameof(Widgets.ButtonInvisible), new[] { typeof(Rect), typeof(bool) }), prefix: Hook(nameof(ButtonHit)));
        qa.Patch(AccessTools.DeclaredMethod(typeof(TooltipHandler), nameof(TooltipHandler.TipRegion), new[] { typeof(Rect), typeof(TipSignal) }), prefix: Hook(nameof(Skip)));
        MethodInfo slot = interactions.GetMethod("SlotButton")!;
        Pawn pawn = Driver.NewMilian();
        buttons = 0;
        hit = false;
        Event.current = new Event { type = EventType.MouseDown, button = 0 };
        Driver.Check(!(bool)slot.Invoke(null, new object[] { new Rect(0, 0, 30, 30), true, pawn })!, "Non-click returned original-menu path.");
        hit = true;
        Event.current = new Event { type = EventType.MouseUp, button = 1 };
        Driver.Check((bool)slot.Invoke(null, new object[] { new Rect(0, 0, 30, 30), true, pawn })!, "Right-click did not preserve original menu branch.");
        controlled = false;
        Event.current = new Event { type = EventType.MouseUp, button = 1 };
        Driver.Check((bool)slot.Invoke(null, new object[] { new Rect(0, 0, 30, 30), true, pawn })!, "Right-click cancel menu was blocked by new eligibility.");
        controlled = true;
        var root = (Root_Play)RuntimeHelpers.GetUninitializedObject(typeof(Root_Play));
        var ui = (UIRoot_Play)RuntimeHelpers.GetUninitializedObject(typeof(UIRoot_Play));
        ui.windows = (WindowStack)RuntimeHelpers.GetUninitializedObject(typeof(WindowStack));
        var tab = (MainTabWindow)RuntimeHelpers.GetUninitializedObject(Driver.Milira.GetType("Milira.MainTabWindow_MilianConfig", true)!);
        tab.def = new MainButtonDef { defName = "Milian_Config" };
        Driver.Set(typeof(MainButtonDef), "tabWindowInt", tab.def, tab);
        Driver.Set(typeof(WindowStack), "windows", ui.windows, new List<Window> { tab });
        ui.mainButtonsRoot = (MainButtonsRoot)RuntimeHelpers.GetUninitializedObject(typeof(MainButtonsRoot));
        ui.mainButtonsRoot.tabs = new MainTabsRoot();
        ui.mapUI = (MapInterface)RuntimeHelpers.GetUninitializedObject(typeof(MapInterface));
        ui.mapUI.targeter = new Targeter();
        ui.mapUI.designatorManager = (DesignatorManager)RuntimeHelpers.GetUninitializedObject(typeof(DesignatorManager));
        root.uiRoot = ui;
        Driver.Set(typeof(Current), "rootInt", null, root);
        ThingWithComps gun = Driver.NewWeapon();
        Event.current = new Event { type = EventType.MouseUp, button = 0 };
        Driver.Check(!(bool)slot.Invoke(null, new object[] { new Rect(0, 0, 30, 30), true, pawn })!, "Left click fell through to original list.");
        Driver.Check(ui.windows.Count == 0 && ui.mapUI.targeter.IsTargeting, "Left click did not remove the actual main tab and start the real Targeter.");
        Targeter targeter = ui.mapUI.targeter;
        Driver.Check(!(bool)AccessTools.Field(typeof(Targeter), "requiresCastedSelected").GetValue(targeter)!, "Targeter requires preselected Milian.");
        AccessTools.DeclaredMethod(typeof(Targeter), "ConfirmStillValid").Invoke(targeter, null);
        Driver.Check(targeter.IsTargeting, "Unselected Milian immediately cancelled targeting.");
        var parameters = (TargetingParameters)AccessTools.Field(typeof(Targeter), "targetParams").GetValue(targeter)!;
        Driver.Check(parameters.canTargetItems && !parameters.canTargetPawns && !parameters.canTargetBuildings
            && !parameters.canTargetLocations && parameters.CanTarget(gun), "Targeter parameters rejected the ground weapon.");
        var action = (Action<LocalTargetInfo>)AccessTools.Field(typeof(Targeter), "action").GetValue(targeter)!;
        ordered = null;
        action(gun);
        Driver.Check(ordered?.targetA.Thing == pawn && ordered.targetB.Thing == gun && actor == pawn, "Picker did not order the original self-equip job.");
        Job captured = ordered!;
        targeter.StopTargeting();
        Driver.Check(!targeter.IsTargeting && ReferenceEquals(ordered, captured), "Cancelling target selection altered an equip job.");
        gun.DeSpawn();
        Driver.Check(buttons == 4, "Wrapper invoked native button more than once per entry.");
        Console.WriteLine("PASS: real main-tab removal and Targeter initialization/caster lifecycle; button hit-test only is substituted.");
    }

    private static void ValidateApparelOrders(Assembly patch)
    {
        Pawn pawn = Driver.NewMilian();
        Apparel apparel = Driver.NewApparel(pawn);
        Driver.Check(CanDress(pawn, apparel), "Compatible ground apparel rejected.");
        Apparel wrong = Driver.NewApparel(pawn, compatible: false);
        Driver.Check(!CanDress(pawn, wrong), "Wrong apparel model accepted.");
        Apparel ordinary = Driver.NewApparel(pawn, targetable: false);
        Driver.Check(!CanDress(pawn, ordinary), "Ordinary non-Milian apparel accepted.");
        Apparel noDress = Driver.NewApparel(pawn, dressable: false);
        Driver.Check(!CanDress(pawn, noDress), "Apparel without the native dress contract accepted.");
        Apparel fitting = Driver.NewApparel(pawn, hediffOnly: true);
        Driver.Check(!CanDress(pawn, fitting), "Fitting apparel accepted without its fitting.");
        pawn.health.hediffSet.hediffs.Add(new Hediff { def = Driver.Adaptive, pawn = pawn });
        Driver.Check(CanDress(pawn, fitting), "Native fitting extension rejected.");
        pawn.health.hediffSet.hediffs.Clear();
        apparel.stackCount = 2;
        Driver.Check(!CanDress(pawn, apparel), "Stacked apparel accepted.");
        apparel.stackCount = 1;
        reachable = false;
        Driver.Check(!CanDress(pawn, apparel), "Unreachable apparel accepted.");
        reachable = true;
        fogged = true;
        Driver.Check(!CanDress(pawn, apparel), "Fogged apparel accepted.");
        fogged = false;
        controlled = false;
        Driver.Check(!CanDress(pawn, apparel), "Uncontrolled pawn could dress.");
        controlled = true;
        Driver.Check(!CanDress(Driver.NewMilian(bodyName: "OtherMechBody"), apparel), "Unrelated mech could dress.");
        BodyPartGroupDef group = apparel.def.apparel.bodyPartGroups[0];
        apparel.def.apparel.bodyPartGroups[0] = new BodyPartGroupDef { defName = "MissingPart" };
        Driver.Check(!CanDress(pawn, apparel), "Apparel with no matching body part accepted.");
        apparel.def.apparel.bodyPartGroups[0] = group;
        var targetable = apparel.AllComps.OfType<CompTargetable>().Single();
        targetable.Props.cannotHaveHediff = Driver.Adaptive;
        pawn.health.hediffSet.hediffs.Add(new Hediff { def = Driver.Adaptive, pawn = pawn });
        Driver.Check(!CanDress(pawn, apparel), "Base target validation was bypassed.");
        pawn.health.hediffSet.hediffs.Clear();
        targetable.Props.cannotHaveHediff = null;
        Pawn reserver = Driver.NewMilian();
        Driver.Map.reservationManager.Reserve(reserver, JobMaker.MakeJob(Driver.DressDef), apparel);
        Driver.Check(!CanDress(pawn, apparel), "Reserved apparel accepted.");
        Driver.Map.reservationManager.ReleaseAllForTarget(apparel);
        Driver.Map.reservationManager.Reserve(reserver, JobMaker.MakeJob(Driver.DressDef), pawn);
        Driver.Check(!CanDress(pawn, apparel), "A different dress actor's Milian reservation was bypassed.");
        Driver.Map.reservationManager.ReleaseAllForTarget(pawn);
        var fire = new Fire { def = new ThingDef { defName = "ValidationFire" }, parent = null };
        Driver.Map.thingGrid.ThingsListAt(apparel.Position).Add(fire);
        Driver.Check(!CanDress(pawn, apparel), "Burning apparel accepted.");
        Driver.Map.thingGrid.ThingsListAt(apparel.Position).Remove(fire);

        var provider = (FloatMenuOptionProvider)Activator.CreateInstance(patch.GetType("WhiteGiver.MilianWeaponInteractions.FloatMenuOptionProvider_MilianWeapons", true)!)!;
        var context = (FloatMenuContext)RuntimeHelpers.GetUninitializedObject(typeof(FloatMenuContext));
        context.allSelectedPawns = new List<Pawn> { pawn };
        context.map = Driver.Map;
        Driver.Set(typeof(FloatMenuContext), "cachedClickedCell", context, apparel.Position);
        FloatMenuMakerMap.currentProvider = provider;
        Driver.Check(provider.SelectedPawnValid(pawn, context) && provider.Applies(context), "Apparel provider not applicable.");
        context.allSelectedPawns.Add(reserver);
        Driver.Check(!provider.Applies(context), "Apparel provider accepted multi-selection.");
        context.allSelectedPawns.Remove(reserver);
        Driver.Check(!provider.GetOptionsFor(ordinary, context).Any() && !provider.GetOptionsFor(noDress, context).Any(), "Unsupported apparel produced a menu entry.");
        Driver.Check(provider.GetOptionsFor(wrong, context).Single().Disabled, "Wrong-model apparel menu entry was enabled.");
        FloatMenuOption option = provider.GetOptionsFor(apparel, context).Single();
        Driver.Check(!option.Disabled && option.Label.StartsWith("ForceWear", StringComparison.Ordinal), "Compatible wear menu entry missing.");
        apparel.SetForbidden(true);
        FloatMenuMakerMap.currentProvider = new NeverSelectedProvider();
        ordered = null;
        rejection = null;
        option.action!();
        Driver.Check(ordered?.def == Driver.DressDef && ordered.targetA.Thing == pawn && ordered.targetB.Thing == apparel
            && ordered.count == 1 && actor == pawn && !apparel.GetComp<CompForbiddable>().Forbidden, "Apparel menu did not capture self, apparel, native job and unforbid.");
        acceptJob = false;
        Driver.Check(!(bool)interactions.GetMethod("TryDress")!.Invoke(null, new object[] { pawn, apparel })! && rejection != null, "Rejected dress order reported success.");
        acceptJob = true;
        Type originalDriver = Driver.DressDef.driverClass;
        Driver.DressDef.driverClass = Driver.EquipDef.driverClass;
        ordered = null;
        Driver.Check(!(bool)interactions.GetMethod("TryDress")!.Invoke(null, new object[] { pawn, apparel })! && ordered == null, "Changed dress driver accepted.");
        Driver.DressDef.driverClass = originalDriver;
        apparel.DeSpawn();
        ordered = null;
        rejection = null;
        option.action!();
        Driver.Check(ordered == null && rejection != null, "Stale apparel menu still issued a job.");
        wrong.DeSpawn(); ordinary.DeSpawn(); noDress.DeSpawn(); fitting.DeSpawn();
        FloatMenuMakerMap.currentProvider = null;
        Console.WriteLine("PASS: apparel context menu, model/fitting/base-target/body/fire/reach/reservation boundaries, stable captured actor, stale click and driver drift.");
    }

    private static void ValidateDressTransfer(Harmony qa, Assembly patch)
    {
        // The real apparel container notifications/unlock logic run; only rendering is isolated.
        qa.Patch(AccessTools.DeclaredMethod(typeof(Pawn_ApparelTracker), nameof(Pawn_ApparelTracker.Notify_ApparelChanged)), prefix: Hook(nameof(Skip)));
        Pawn pawn = Driver.NewMilian();
        Apparel old = Driver.NewApparel(pawn);
        old.DeSpawn();
        pawn.apparel.Wear(old, true, true);
        Driver.Check(pawn.apparel.IsLocked(old) && old.Wearer == pawn, "QA could not seed the native locked apparel state.");
        var replacements = new[] { Driver.NewApparel(pawn), Driver.NewApparel(pawn) };
        ApparelProperties.ResetStaticData();
        foreach (Apparel next in replacements)
        {
            Driver.Check(pawn.apparel.WouldReplaceLockedApparel(next), "Replacement did not actually conflict with locked apparel.");
            Driver.Check(CanDress(pawn, next), "Native locked apparel incorrectly blocked repeat changing.");
            var provider = (FloatMenuOptionProvider)Activator.CreateInstance(patch.GetType("WhiteGiver.MilianWeaponInteractions.FloatMenuOptionProvider_MilianWeapons", true)!)!;
            var context = (FloatMenuContext)RuntimeHelpers.GetUninitializedObject(typeof(FloatMenuContext));
            context.allSelectedPawns = new List<Pawn> { pawn };
            context.map = Driver.Map;
            FloatMenuMakerMap.currentProvider = provider;
            FloatMenuOption option = provider.GetOptionsFor(next, context).Single();
            ordered = null;
            option.action!();
            Driver.Check(ordered?.def == Driver.DressDef && actor == pawn, "Replacement menu did not issue native dress job.");
            Job job = ordered!;
            var driver = (JobDriver)Activator.CreateInstance(Driver.DressDef.driverClass)!;
            driver.pawn = pawn;
            driver.job = job;
            Driver.Check(driver.TryMakePreToilReservations(false), "Native self-dress pre-toil reservations failed.");
            next.DeSpawn();
            Driver.Check(pawn.carryTracker.innerContainer.TryAdd(next), "QA could not carry replacement apparel.");
            AccessTools.DeclaredMethod(Driver.DressDef.driverClass, "DressMilian").Invoke(driver, null);
            Driver.Check(next.Wearer == pawn && pawn.apparel.WornApparel.Count == 1 && pawn.apparel.IsLocked(next), "Native dress failed to wear/lock new apparel.");
            Driver.Check(old.Spawned && old.Wearer == null && !pawn.apparel.IsLocked(old), "Native dress failed to drop/unlock old apparel.");
            Driver.Check(!next.Spawned && pawn.carryTracker.CarriedThing == null, "Native dress duplicated apparel across carry/ground/worn containers.");
            Driver.Map.reservationManager.ReleaseAllForTarget(pawn);
            Driver.Map.reservationManager.ReleaseAllForTarget(next);
            old.DeSpawn();
            old = next;
        }
        FloatMenuMakerMap.currentProvider = null;
        Console.WriteLine("PASS: two real menu-to-DressMilian-to-Wear replacements; old locked apparel drops/unlocks, new apparel locks, no container duplicates.");
    }

    private static void ValidateOriginalMenu()
    {
        Pawn pawn = Driver.NewMilian();
        ThingWithComps gun = Driver.NewWeapon();
        ThingWithComps wrong = Driver.NewWeapon(false);
        ThingWithComps stack = Driver.NewWeapon();
        stack.stackCount = 2;
        Type column = Driver.Milira.GetType("Milira.PawnColumnWorker_EquipmentMilian", true)!;
        object worker = RuntimeHelpers.GetUninitializedObject(column);
        MethodInfo get = column.GetMethod("GetEquipmentFloatMenu")!;
        var options = (List<FloatMenuOption>)get.Invoke(worker, new object[] { pawn })!;
        Driver.Check(options.Count == 1 && !options[0].Disabled, "Original menu weapon filtering changed.");
        ordered = null;
        options[0].action();
        Driver.Check(ordered?.targetA.Thing == pawn && ordered.targetB.Thing == gun, "Original menu order no longer intact.");
        pawn.jobs.curJob = JobMaker.MakeJob(Driver.EquipDef, pawn, gun);
        options = (List<FloatMenuOption>)get.Invoke(worker, new object[] { pawn })!;
        Driver.Check(options.Count == 2 && options[0].Label.StartsWith("Milira.CancelEquipWith", StringComparison.Ordinal), "Original cancel option missing.");
        gun.DeSpawn(); wrong.DeSpawn(); stack.DeSpawn();
        Console.WriteLine("PASS: actual upstream right-click list still filters weapon types/stacks and retains cancellation.");
    }

    private static void ValidateSling(Harmony qa)
    {
        // Only external presentation/container notifications are substituted; transfers and Milira's equip body run unchanged.
        qa.Patch(AccessTools.DeclaredMethod(typeof(Pawn_EquipmentTracker), nameof(Pawn_EquipmentTracker.Notify_EquipmentAdded)), prefix: Hook(nameof(Skip)));
        qa.Patch(AccessTools.DeclaredMethod(typeof(Pawn_EquipmentTracker), nameof(Pawn_EquipmentTracker.Notify_EquipmentRemoved)), prefix: Hook(nameof(Skip)));
        qa.Patch(AccessTools.DeclaredMethod(typeof(ThingOwnerUtility), nameof(ThingOwnerUtility.ShouldAutoExtinguishInnerThings)), prefix: Hook(nameof(False)));
        qa.Patch(AccessTools.DeclaredMethod(typeof(ThingOwnerUtility), nameof(ThingOwnerUtility.ShouldRemoveDesignationsOnAddedThings)), prefix: Hook(nameof(False)));
        qa.Patch(AccessTools.DeclaredMethod(typeof(PawnRenderTree), nameof(PawnRenderTree.SetDirty)), prefix: Hook(nameof(Skip)));
        qa.Patch(AccessTools.DeclaredMethod(typeof(ResourceCounter), nameof(ResourceCounter.UpdateResourceCounts)), prefix: Hook(nameof(Skip)));

        Pawn pawn = Driver.NewMilian();
        var drawer = (Pawn_DrawTracker)RuntimeHelpers.GetUninitializedObject(typeof(Pawn_DrawTracker));
        drawer.renderer = (PawnRenderer)RuntimeHelpers.GetUninitializedObject(typeof(PawnRenderer));
        drawer.renderer.renderTree = (PawnRenderTree)RuntimeHelpers.GetUninitializedObject(typeof(PawnRenderTree));
        Driver.Set(typeof(Pawn), "drawer", pawn, drawer);
        Driver.Map.resourceCounter = (ResourceCounter)RuntimeHelpers.GetUninitializedObject(typeof(ResourceCounter));
        var sling = new HediffWithComps { def = Driver.Sling, pawn = pawn };
        Type alternateType = Driver.Ancot.GetType("AncotLibrary.HediffComp_AlternateWeapon", true)!;
        var alternate = (HediffComp)Activator.CreateInstance(alternateType)!;
        alternate.parent = sling;
        sling.comps.Add(alternate);
        pawn.health.hediffSet.hediffs.Add(sling);
        ThingWithComps old = Driver.NewWeapon();
        old.DeSpawn();
        pawn.equipment.AddEquipment(old);
        ThingWithComps next = Driver.NewWeapon();
        next.DeSpawn();
        Driver.Check(pawn.carryTracker.innerContainer.TryAdd(next), "QA could not place new weapon in actual carry container.");
        var jobDriver = (JobDriver)Activator.CreateInstance(Driver.EquipDef.driverClass)!;
        jobDriver.pawn = pawn;
        jobDriver.job = JobMaker.MakeJob(Driver.EquipDef, pawn, next);
        AccessTools.DeclaredMethod(Driver.EquipDef.driverClass, "EquipMilian").Invoke(jobDriver, null);
        Driver.Check(ReferenceEquals(pawn.equipment.Primary, next), "Actual Milira equip did not install carried weapon.");
        Driver.Check(ReferenceEquals(alternateType.GetProperty("ContainedThing")!.GetValue(alternate), old), "Actual Milira equip did not store old weapon in tactical sling.");
        Driver.Check(pawn.carryTracker.CarriedThing == null && !next.Spawned && !old.Spawned,
            "Actual equipment/carry/sling containers retained duplicate ground weapons.");
        Console.WriteLine("PASS: actual Milira EquipMilian moves carried gun into equipment and old gun into the tactical-sling container.");
    }

    private static bool CanEquip(Pawn pawn, Thing weapon)
    {
        object?[] arguments = { pawn, weapon, null };
        return (bool)interactions.GetMethod("CanEquip")!.Invoke(null, arguments)!;
    }
    private static bool CanDress(Pawn pawn, Apparel apparel)
    {
        object?[] arguments = { pawn, apparel, null };
        return (bool)interactions.GetMethod("CanDress")!.Invoke(null, arguments)!;
    }
    private static HarmonyMethod Hook(string name) => new(typeof(PatchValidation), name);
    public static bool NoTranslation(string key, out TaggedString result, ref bool __result) { result = key; __result = true; return false; }
    public static bool PlainText(TaggedString taggedStr, ref string __result) { __result = taggedStr.RawText; return false; }
    public static bool OverseerState(ref OverseerSubjectState __result) { __result = controlled ? OverseerSubjectState.Overseen : OverseerSubjectState.RequiresOverseer; return false; }
    public static bool Fog(ref bool __result) { __result = fogged; return false; }
    public static bool Reachable(ref bool __result) { __result = reachable; return false; }
    public static bool Order(Pawn ___pawn, Job job, ref bool __result) { actor = ___pawn; ordered = job; __result = acceptJob; return false; }
    public static bool Message(string text) { rejection = text; return false; }
    public static bool ButtonHit(ref bool __result) { buttons++; __result = hit; if (hit) Event.current.Use(); return false; }
    public static bool Skip() => false;
    public static bool False(ref bool __result) { __result = false; return false; }
}

public sealed class NeverSelectedProvider : FloatMenuOptionProvider
{
    protected override bool Drafted => true;
    protected override bool Undrafted => true;
    protected override bool Multiselect => false;
    public override bool SelectedPawnValid(Pawn pawn, FloatMenuContext context) => false;
}
