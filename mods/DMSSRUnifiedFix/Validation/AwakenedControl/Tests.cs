using System.Reflection;
using System.Reflection.Emit;
using System.Xml.Linq;
using DMSSR.Abilities;
using DMSSR.Hediffs;
using Fortified;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;
using WhiteGiver.DMSSRUnifiedFix;
using WhiteGiver.DMSSRUnifiedFix.Audited;

namespace DMSSRUnifiedFix.Validation.AwakenedControl;

internal static class Tests
{
    private static readonly Harmony Harness = new(Program.ValidationOwner + ".awake");
    private static Fixture fx = null!;
    private static int assertions;
    private static int letters;
    private static bool hasHome = true;
    private static bool throwBandwidth;
    private static bool nestedScope;
    private static int drops;
    private static int destroyed;
    private static bool crossThreadScope;
    private static float crossThreadBandwidth = -1f;
    private static readonly List<float> NestedBandwidth = new();
    private static Type Storage => AccessTools.TypeByName("WhiteGiver.DMSMechStorage.MechStorage")!;

    internal static void Run(string mode)
    {
        fx = new Fixture();
        SetupEngineSeams();
        StatPathPerf.UpstreamPatches.EnsurePatched();
        foreach (string name in new[] { "Patch_JobGiver_GetEnergy_Min", "Patch_CompOverseerSubject_State",
            "Patch_MechanitorUtility_CanDraftMech", "Patch_Pawn_DraftController_ShowDraftGizmo", "Patch_InMechanitorCommandRange" })
            Harness.CreateClassProcessor(typeof(CompDeadManSwitch).Assembly.GetType("Fortified." + name, true)!).Patch();
        bool green = mode != "red";
        // Emulate a cache initialized before StaticConstructorOnStartup, not just uncached fixture reads.
        Pawn cached = fx.Mech();
        StatDefOf.BandwidthCost.immutable = true;
        StatDefOf.BandwidthCost.Worker.SetCacheability(true);
        Check(cached.GetStatValue(StatDefOf.BandwidthCost) == 8f, "real immutable bandwidth cache primed before installation");
        if (green) DMSSRUnifiedFixMod.ApplyPatches();
        Check(AwakenedControlGroupFix.Installed == green, "awakening subsystem installation state");
        if (green)
        {
            Check(!StatDefOf.BandwidthCost.immutable, "new stateful StatPart disables pre-startup immutable caching");
            Check(StatDefOf.BandwidthCost.parts.Count(p => p is StatPart_AwakenedBandwidth) == 1, "one inspectable awakened bandwidth StatPart");
        }
        // RED retains upstream immutable behavior for the first cache probe; remaining tests isolate
        // the gameplay defects from that independent lifecycle issue.
        cached.GetComp<CompDeadManSwitch>().woken = true;
        Check(cached.GetStatValue(StatDefOf.BandwidthCost) == (green ? 0f : 8f), "cached sleeping cost changes after awakening only with the fix");
        StatDefOf.BandwidthCost.immutable = false;
        StatDefOf.BandwidthCost.Worker.TryClearCache();
        StatDefOf.BandwidthCost.Worker.SetCacheability(false);

        WakeAndTick(green);
        DetachedRecontrol(green);
        PhysicalBandwidth(green);
        ThinkAndRecharge(green);
        StorageRoundTrip(green);
        Outgoing(green);
        PatternContract();
        if (green) ExceptionScopes();
        if (mode == "composition") RemoteComposition();
        Check(!typeof(DMSSRUnifiedFixMod).Assembly.GetReferencedAssemblies().Any(a => a.Name == "DMSMechStorage"), "runtime assembly has no hard storage reference");
        LogCapture.AssertClean();
        Console.WriteLine($"PASS AWAKE {mode.ToUpperInvariant()}: {assertions} assertions.");
    }

    private static void SetupEngineSeams()
    {
        MethodInfo receive = AccessTools.GetDeclaredMethods(typeof(LetterStack)).Single(m => m.Name == "ReceiveLetter" && m.GetParameters().Length == 10);
        Harness.Patch(receive, prefix: new HarmonyMethod(typeof(Tests), nameof(Letter)));
        Harness.Patch(AccessTools.PropertyGetter(typeof(Find), nameof(Find.AnyPlayerHomeMap)), prefix: new HarmonyMethod(typeof(Tests), nameof(Home)));
        Harness.Patch(AccessTools.Method(typeof(DropCellFinder), nameof(DropCellFinder.TradeDropSpot)), prefix: new HarmonyMethod(typeof(Tests), nameof(DropCell)));
        Harness.Patch(AccessTools.Method(typeof(DropPodUtility), nameof(DropPodUtility.MakeDropPodAt)), prefix: new HarmonyMethod(typeof(Tests), nameof(Drop)));
        Harness.Patch(AccessTools.Method(typeof(WorldObject), nameof(WorldObject.Destroy)), prefix: new HarmonyMethod(typeof(Tests), nameof(DestroySettlement)));
        Harness.Patch(AccessTools.Method(typeof(StatWorker), nameof(StatWorker.GetValueUnfinalized)), prefix: new HarmonyMethod(typeof(Tests), nameof(BandwidthProbe)));
        // Capsule tests retain CanActivate/Activate, container transfers and native relation creation;
        // only Unity map-placement, rendering/quest notifications and footprint collision are seams.
        Harness.Patch(AccessTools.Method(Storage, "ClearFootprint"), prefix: new HarmonyMethod(typeof(Tests), nameof(ClearFootprint)));
        Harness.Patch(AccessTools.Method(typeof(GenSpawn), nameof(GenSpawn.Spawn), new[] { typeof(Thing), typeof(IntVec3), typeof(Map), typeof(Rot4), typeof(WipeMode), typeof(bool), typeof(bool) }),
            prefix: new HarmonyMethod(typeof(Tests), nameof(Spawn)));
        Harness.Patch(AccessTools.Method(typeof(Pawn), nameof(Pawn.Notify_DisabledWorkTypesChanged)), prefix: new HarmonyMethod(typeof(Tests), nameof(Skip)));
        Harness.Patch(AccessTools.Method(typeof(Thing), nameof(Thing.Destroy)), prefix: new HarmonyMethod(typeof(Tests), nameof(DestroyThing)));
        Harness.Patch(AccessTools.Method(typeof(Building), nameof(Building.Destroy)), prefix: new HarmonyMethod(typeof(Tests), nameof(DestroyThing)));
        Harness.Patch(AccessTools.Method(typeof(Messages), nameof(Messages.Message), new[] { typeof(string), typeof(LookTargets), typeof(MessageTypeDef), typeof(bool) }),
            prefix: new HarmonyMethod(typeof(Tests), nameof(Skip)));
    }

    private static void WakeAndTick(bool green)
    {
        Pawn actor = fx.Mechanitor();
        Pawn mech = fx.Mech();
        CompDeadManSwitch comp = mech.GetComp<CompDeadManSwitch>();
        MechanitorControlGroup group = Fixture.Enroll(actor, mech);
        group.SetWorkMode(MechWorkModeDefOf.Recharge);
        group.mechRechargeThresholds = new FloatRange(0.12f, 0.85f);
        Check(actor.mechanitor.UsedBandwidth == 8 && actor.mechanitor.ControlledPawns.Contains(mech), "sleeping mech uses 8 bandwidth in a real native group");
        comp.woken_Lurk = true;
        int before = letters;
        comp.Wake();
        Check(mech.GetStatValue(StatDefOf.BandwidthCost, cacheStaleAfterTicks: 600) == (green ? 0f : 8f), "Wake invalidates any cached sleeping bandwidth immediately");
        Check(comp.woken && !comp.woken_Lurk && mech.interactions != null, "Wake sets flags and initializes real interaction tracker");
        Check(mech.Name.ToString() == "Awakened" && letters == before + 1, "Wake retains upstream name generation and one letter");
        Check((mech.GetOverseer() == actor) == green, green ? "Wake retains exact Overseer relation" : "RED Wake removes Overseer relation");
        Check(ReferenceEquals(mech.GetMechControlGroup(), group) == green, green ? "Wake retains exact MechanitorControlGroup instance" : "RED Wake removes control-group membership");
        Check(actor.mechanitor.UsedBandwidth == 0, "Wake drops used bandwidth from 8 to 0 immediately");
        if (green)
        {
            Check(actor.mechanitor.ControlledPawns.Contains(mech), "Wake immediately retains controlled-pawn membership");
            Check(group.WorkMode == MechWorkModeDefOf.Recharge && group.mechRechargeThresholds == new FloatRange(0.12f, 0.85f), "Wake preserves Recharge mode and custom charge limits");
        }
        Pawn_InteractionsTracker? tracker = mech.interactions;
        Name name = mech.Name;
        comp.woken_Lurk = true;
        comp.Wake();
        Check(letters == before + 1 && ReferenceEquals(name, mech.Name) && ReferenceEquals(tracker, mech.interactions) && !comp.woken_Lurk, "Wake remains idempotent and clears lurking flag");
        if (!green) Fixture.Enroll(actor, mech);
        comp.woken_Lurk = true;
        for (int tick = 0; tick < 12; tick++) comp.CompTick();
        Check(comp.woken == green && !comp.woken_Lurk, green ? "repeated real CompTick never clears awakening while overseen" : "RED periodic TryTriggerDMS resets awakening after re-control");
        if (green)
        {
            comp.outgoing = true;
            comp.outgoingTime = 0;
            comp.CompTick();
            Check(comp.outgoing && comp.outgoingTime == 1, "outgoing departure countdown is unchanged by the transpiler");
            comp.outgoing = false;
        }
        comp.woken = false;
        actor.relations.TryRemoveDirectRelation(PawnRelationDefOf.Overseer, mech);
        comp.woken_Lurk = true;
        comp.timeToWake = 2;
        comp.CompTick();
        Check(comp.timeToWake == 1 && !comp.woken, "lurking awakening countdown retains its original decrement");
        comp.timeToWake = 0;
        comp.CompTick();
        Check(comp.woken, "lurking countdown still invokes real Wake");
    }

    private static void DetachedRecontrol(bool green)
    {
        Pawn actor = fx.Mechanitor();
        Pawn occupying = fx.Mech(dms: false);
        Fixture.Enroll(actor, occupying);
        Pawn mech = fx.Mech(woken: true);
        Check(actor.mechanitor.TotalBandwidth == actor.mechanitor.UsedBandwidth && actor.mechanitor.UsedBandwidth == 8, "re-control fixture has exactly zero free bandwidth");
        Check(mech.GetOverseer() == null && mech.GetMechControlGroup() == null, "existing-save fixture starts awakened and detached");
        Check(JobGiver_GetEnergy.GetMinAutorechargeThreshold(mech) == (green ? 5 : 100), green ? "detached awakened mech recharges at 5 not 100" : "RED detached awakened mech recharges at 100");
        AcceptanceReport canControl = MechanitorUtility.CanControlMech(actor, mech);
        Check(canControl.Accepted == green, (green ? "native CanControlMech accepts awake mech at zero free bandwidth" : "RED native CanControlMech rejects detached awake mech at zero free bandwidth")
            + $" (colony={mech.IsColonyMech}, down={mech.Downed}, dead={mech.Dead}, attacking={mech.IsAttacking()}, subject={mech.OverseerSubject != null}, cost={mech.GetStatValue(StatDefOf.BandwidthCost)}, reason={canControl.Reason})");
        if (green)
        {
            MechanitorControlGroup group = Fixture.Enroll(actor, mech);
            Check(mech.GetOverseer() == actor && group != null && mech.GetComp<CompDeadManSwitch>().woken, "native relation creation re-enrolls without resetting woken");
            Check(actor.mechanitor.UsedBandwidth == 8 && actor.mechanitor.ControlledPawns.Contains(mech), "re-control leaves used bandwidth unchanged and mech controlled");
            group!.SetWorkMode(MechWorkModeDefOf.Recharge);
            Check(mech.GetMechWorkMode() == MechWorkModeDefOf.Recharge, "recontrolled mech supports native Recharge work mode");
            Pawn zero = fx.Mechanitor(0f);
            Pawn free = fx.Mech(woken: true);
            Check(MechanitorUtility.CanControlMech(zero, free), "zero total bandwidth also permits awakened mech control");
            Fixture.Enroll(zero, free);
            Check(zero.mechanitor.UsedBandwidth == 0 && zero.mechanitor.ControlledPawns.Contains(free), "zero-capacity tracker retains its zero-cost controlled subject");
        }
    }

    private static void PhysicalBandwidth(bool green)
    {
        Pawn mech = fx.Mech(woken: true);
        Check(mech.GetStatValue(StatDefOf.BandwidthCost) == (green ? 0f : 8f), "real BandwidthCost is zero only for patched awakened mechs");
        Check(mech.GetStatValue(StatDefOf.WastepacksPerRecharge) == 40f, "real WastepacksPerRecharge preserves vanilla 5 times 8 physical weight");
        Check(LocalMesh(mech) == 8f, "real private LocalMesh weight preserves physical bandwidth 8");
        Check(mech.GetStatValue(StatDefOf.BandwidthCost, cacheStaleAfterTicks: 600) == (green ? 0f : 8f), "physical reads do not pollute cached mechanitor bandwidth");
        HediffComp_MechCustomization comp = fx.Customize(mech);
        comp.bandwidthMult = 1.5f;
        comp.wastepacksMult = 2f;
        StatDefOf.BandwidthCost.Worker.ClearCacheForThing(mech);
        Check(mech.GetStatValue(StatDefOf.BandwidthCost) == (green ? 0f : 12f), "DMSSR bandwidth multiplier cannot undo zero cost");
        Check(mech.GetStatValue(StatDefOf.WastepacksPerRecharge) == 120f, "wastepacks preserve both DMSSR multipliers (5 times 2 times 12)");
        Check(LocalMesh(mech) == 12f, "LocalMesh preserves DMSSR-adjusted physical weight 12");
        Pawn ordinary = fx.Mech(dms: false);
        Check(ordinary.GetStatValue(StatDefOf.BandwidthCost) == 8f && LocalMesh(ordinary) == 8f, "ordinary non-DMS mechs retain native bandwidth");
        Check(StatDefOf.BandwidthCost.Worker.GetValueAbstract(fx.World.MechDef) == 8f, "abstract race bandwidth is not zeroed");
        if (green)
        {
            Check(!string.IsNullOrEmpty(StatDefOf.BandwidthCost.parts.OfType<StatPart_AwakenedBandwidth>().Single().ExplanationPart(StatRequest.For(mech))), "information card explains awakening's zero bandwidth");
            Pawn actor = fx.Mechanitor(20f);
            Fixture.Enroll(actor, mech);
            _ = mech.GetStatValue(StatDefOf.BandwidthCost, cacheStaleAfterTicks: 600);
            _ = mech.GetStatValue(StatDefOf.WastepacksPerRecharge, cacheStaleAfterTicks: 600);
            mech.GetComp<CompDeadManSwitch>().Notify_Killed(fx.World.Map);
            Check(!mech.GetComp<CompDeadManSwitch>().woken && mech.GetStatValue(StatDefOf.BandwidthCost, cacheStaleAfterTicks: 600) == 12f, "real Notify_Killed clears awakening and cached zero for later resurrection");
            Check(actor.mechanitor.UsedBandwidth == 12 && actor.mechanitor.ControlledPawns.Contains(mech), "death notification refreshes native tracker with restored physical cost");
            Check(mech.GetStatValue(StatDefOf.WastepacksPerRecharge, cacheStaleAfterTicks: 600) == 120f, "death notification also invalidates wastepack cache");
        }
    }

    private static void ThinkAndRecharge(bool green)
    {
        Pawn mech = fx.Mech(woken: true);
        ThinkNode_Condition_Wake wake = new();
        Check(Satisfied(wake, mech), "overseerless awakened mech keeps FFF independent think branch");
        HediffComp_MechCustomization comp = fx.Customize(mech);
        comp.supply = 2f;
        Check(JobGiver_GetEnergy.GetMinAutorechargeThreshold(mech) == (green ? 10 : 200), "DMSSR supply postfix scales the detached recharge fallback");
        Pawn actor = fx.Mechanitor();
        MechanitorControlGroup group = Fixture.Enroll(actor, mech);
        Check(Satisfied(wake, mech) == !green, green ? "overseen awakened mech bypasses FFF independent branch" : "RED Wake node steals overseen awakened mech from WorkMode routing");
        foreach (MechWorkModeDef mode in new[] { MechWorkModeDefOf.Work, MechWorkModeDefOf.Recharge,
            MechWorkModeDefOf.SelfShutdown, MechWorkModeDefOf.Escort, Fixture.Mode("WVC_WorkAndRecharge") })
        {
            group.SetWorkMode(mode);
            Check(Satisfied(new ThinkNode_ConditionalWorkMode { workMode = mode }, mech), "real native WorkMode node admits " + mode.defName);
            if (green) Check(!Satisfied(wake, mech), "awakened branch does not preempt " + mode.defName);
        }
        group.mechRechargeThresholds = new FloatRange(0.2f, 0.8f);
        Check(JobGiver_GetEnergy.GetMinAutorechargeThreshold(mech) == 40, "group charge limits and supply still compose for overseen mech");
        if (green)
        {
            AccessTools.Field(typeof(Pawn_HealthTracker), "healthState").SetValue(actor.health, PawnHealthState.Down);
            Check(MechanitorUtility.CanDraftMech(mech), "FFF still permits awakened drafting with downed mechanitor");
            Check(MechanitorUtility.InMechanitorCommandRange(mech, new LocalTargetInfo(new IntVec3(1000, 0, 1000))), "FFF retains unlimited command range with downed mechanitor");
            AccessTools.Field(typeof(Thing), "mapIndexOrState").SetValue(actor, (sbyte)-1);
            Check(MechanitorUtility.CanDraftMech(mech), "FFF still permits awakened drafting with off-map mechanitor");
            Check(MechanitorUtility.InMechanitorCommandRange(mech, new LocalTargetInfo(new IntVec3(1000, 0, 1000))), "FFF retains unlimited command range with off-map mechanitor");
            Check(mech.OverseerSubject.State == OverseerSubjectState.Overseen && mech.drafter.ShowDraftGizmo, "FFF Overseen state and drafting gizmo remain available");
        }
        Pawn normal = fx.Mech();
        Check(JobGiver_GetEnergy.GetMinAutorechargeThreshold(normal) == 100, "non-awakened detached recharge threshold is unchanged");
        OverseerFixture overseer = Fixture.CopyMech<OverseerFixture>(fx.Mech(woken: true));
        overseer.MinCharge = 0.3f;
        Check(JobGiver_GetEnergy.GetMinAutorechargeThreshold(overseer) == 30, "custom IOverseerMech retains FFF's own charge threshold");
    }

    private static void StorageRoundTrip(bool green)
    {
        Pawn actor = fx.Mechanitor(0f);
        Pawn mech = fx.Mech(woken: true);
        Check((bool)Call(Storage, "RequiresEnrollment", mech)! == green, green ? "real storage API requests awake mech re-enrollment" : "RED storage API strands awakened mech without enrollment");
        Pawn ordinary = fx.Mech();
        Check((bool)Call(Storage, "RequiresEnrollment", ordinary)!, "storage still enrolls non-awakened subjects");
        Pawn drone = fx.Mech(woken: true);
        drone.AllComps.Remove(drone.GetComp<CompOverseerSubject>());
        Check(!(bool)Call(Storage, "RequiresEnrollment", drone)!, "storage does not enroll subject-less drones");
        StateFixture state = Fixture.CopyMech<StateFixture>(fx.Mech(woken: true));
        Check(!(bool)Call(Storage, "RequiresEnrollment", state)!, "storage keeps autonomous IStateControllableMech outside enrollment");
        if (!green) return;
        Building_MechCapsule capsule = new()
        {
            def = new ThingDef { defName = "AwakeCapsule", label = "capsule", category = ThingCategory.Building,
                thingClass = typeof(Building_MechCapsule), comps = new List<CompProperties>(), useHitPoints = false },
            innerContainer = null!,
        };
        capsule.innerContainer = new ThingOwner<Pawn>(capsule);
        AccessTools.Field(typeof(Thing), "mapIndexOrState").SetValue(capsule, (sbyte)0);
        AccessTools.Field(typeof(Thing), "positionInt").SetValue(capsule, new IntVec3(8, 0, 8));
        AccessTools.Field(typeof(Thing), "factionInt").SetValue(capsule, fx.World.PlayerFaction);
        AccessTools.Field(typeof(Thing), "mapIndexOrState").SetValue(mech, (sbyte)-1);
        capsule.innerContainer.TryAdd(mech);
        Check(((AcceptanceReport)Call(Storage, "CanActivate", capsule, actor)!).Accepted, "real capsule CanActivate accepts zero free bandwidth");
        Check((bool)Call(Storage, "Activate", capsule, actor)!, "real capsule Activate completes container release");
        Check(mech.GetOverseer() == actor && mech.GetMechControlGroup() != null && actor.mechanitor.ControlledPawns.Contains(mech), "real capsule activation creates Overseer and a native control group");
        Check(mech.GetComp<CompDeadManSwitch>().woken && actor.mechanitor.UsedBandwidth == 0, "capsule release preserves awakening and zero bandwidth");
    }

    private static void Outgoing(bool green)
    {
        Pawn mech = fx.Mech(woken: true);
        fx.Settlement(fx.World.HostileFaction);
        foreach (int branch in new[] { 0, 1, 2 })
        {
            fx.Dms.OutgoingMeches.Clear();
            fx.Dms.removedCache.Clear();
            OutgoingMech outgoing = new() { mech = mech };
            fx.Dms.OutgoingMeches.Add(outgoing);
            Exception? error = WithSeed(branch, outgoing.Trigger);
            Check((error == null) == (green || branch == 2), "null outgoing attack/loot configuration branch " + branch + (error == null ? " completes" : " reproduces " + error.GetType().Name));
            if (green)
            {
                Check(fx.Dms.OutgoingMeches.Contains(outgoing) && fx.Dms.removedCache.Contains(mech), "outgoing branch " + branch + " queues instead of mutating collection");
                Check(outgoing.timeToTrigger >= 300000 && outgoing.timeToTrigger < 1800000, "outgoing reset delay stays within upstream range");
            }
        }
        fx.Dms.OutgoingMeches.Clear();
        fx.Dms.removedCache.Clear();
        fx.Dms.OutgoingMeches.Add(new OutgoingMech { mech = mech, timeToTrigger = 1 });
        Exception? enumeration = WithSeed(2, fx.Dms.GameComponentTick);
        Check((enumeration == null) == green, green ? "real GameComponentTick foreach completes without mutation" : "RED quiet outgoing branch mutates active foreach");
        if (green)
        {
            Check(fx.Dms.OutgoingMeches.Count == 1 && fx.Dms.removedCache.Count == 1, "enumerated outgoing entry remains until next tick");
            fx.Dms.GameComponentTick();
            Check(fx.Dms.OutgoingMeches.Count == 0 && fx.Dms.removedCache.Count == 0, "next real game tick drains deferred outgoing removal");
            CompProperties_DeadManSwitch props = mech.GetComp<CompDeadManSwitch>().Props;
            props.attackRule = fx.StoryRule;
            props.lootRule = fx.StoryRule;
            props.lootSetMaker = new ThingSetMakerDef { defName = "AwakeLoot", root = new EmptyLoot() };
            Find.World.worldObjects.AllWorldObjects.Clear();
            Check(WithSeed(0, new OutgoingMech { mech = mech }.Trigger) == null, "valid attack rule with zero hostile settlements is safe");
            fx.Settlement(fx.World.PlayerFaction);
            Check(WithSeed(0, new OutgoingMech { mech = mech }.Trigger) == null && destroyed == 0, "player settlement cannot be selected for attack");
            fx.Settlement(fx.World.HostileFaction);
            int before = letters;
            Check(WithSeed(0, new OutgoingMech { mech = mech }.Trigger) == null && destroyed == 1 && letters == before + 1, "valid hostile attack still resolves story, letter and settlement destruction");
            hasHome = false;
            Check(WithSeed(1, new OutgoingMech { mech = mech }.Trigger) == null && drops == 0, "valid loot rules without home map skip safely");
            hasHome = true;
            Check(WithSeed(1, new OutgoingMech { mech = mech }.Trigger) == null && drops == 1, "valid loot still generates transport pod and story");
            props.lootSetMaker = null;
            Check(WithSeed(1, new OutgoingMech { mech = mech }.Trigger) == null && drops == 1, "valid loot rule with missing lootSetMaker skips safely");
            props.lootSetMaker = new ThingSetMakerDef { defName = "MissingRoot", root = null! };
            Check(WithSeed(1, new OutgoingMech { mech = mech }.Trigger) == null && drops == 1, "valid loot rule with missing generator root skips safely");
            fx.Dms.OutgoingMeches.Clear();
            fx.Dms.removedCache.Clear();
            fx.Dms.OutgoingMeches.Add(new OutgoingMech { mech = null!, timeToTrigger = 1 });
            Check(WithSeed(0, fx.Dms.GameComponentTick) == null, "missing outgoing pawn is safe inside real foreach");
            fx.Dms.GameComponentTick();
            Check(fx.Dms.OutgoingMeches.Count == 0, "missing outgoing pawn sentinel is removed before next enumeration");
        }
        fx.Dms.OutgoingMeches.Clear();
        fx.Dms.removedCache.Clear();
    }

    private static void PatternContract()
    {
        MethodInfo target = AccessTools.Method(typeof(CompDeadManSwitch), "TryTriggerDMS");
        List<CodeInstruction> original = PatchProcessor.GetOriginalInstructions(target);
        List<CodeInstruction> rewritten = AwakenedControlGroupFix.KeepAwakening(original.Select(i => new CodeInstruction(i))).ToList();
        Check(original.Count == rewritten.Count && original.Zip(rewritten).Count(pair => pair.First.opcode != pair.Second.opcode) == 3, "transpiler changes only three assignment instructions");
        FieldInfo woken = AccessTools.Field(typeof(CompDeadManSwitch), "woken");
        FieldInfo lurk = AccessTools.Field(typeof(CompDeadManSwitch), "woken_Lurk");
        Check(!rewritten.Any(i => i.StoresField(woken)) && rewritten.Count(i => i.StoresField(lurk)) == 1, "transpiler removes only reset of woken and retains reset of lurking");
        foreach (IEnumerable<CodeInstruction> drift in new[] { Array.Empty<CodeInstruction>(), original.Concat(original) })
        {
            Exception? error = Catch(() => AwakenedControlGroupFix.KeepAwakening(drift).ToList());
            Check(error is InvalidOperationException && error.Message.Contains("exactly one", StringComparison.Ordinal), "zero/duplicate reset pattern explicitly refuses upstream drift");
        }
        string xml = Path.Combine(Program.GameRoot, "Data", "Biotech", "Defs", "Stats", "Stats_Pawns_General.xml");
        XElement waste = XDocument.Load(xml).Root!.Elements("StatDef").Single(e => (string?)e.Element("defName") == "WastepacksPerRecharge");
        Check(waste.Element("postProcessStatFactors")!.Elements("li").Single().Value == "BandwidthCost" && (float)waste.Element("defaultBaseValue")! == 5f, "wastepack fixture matches installed vanilla XML postprocess factor");
    }

    private static void ExceptionScopes()
    {
        Pawn mech = fx.Mech(woken: true);
        throwBandwidth = true;
        Check(Catch(() => LocalMesh(mech)) is InvalidOperationException, "exception traverses real LocalMesh scope");
        throwBandwidth = false;
        Check(mech.GetStatValue(StatDefOf.BandwidthCost) == 0f, "LocalMesh finalizer restores suppression after exception");
        throwBandwidth = true;
        Check(Catch(() => mech.GetStatValue(StatDefOf.WastepacksPerRecharge)) is InvalidOperationException, "exception traverses real wastepack FinalizeValue scope");
        throwBandwidth = false;
        Check(mech.GetStatValue(StatDefOf.BandwidthCost) == 0f, "wastepack finalizer restores suppression after exception");
        NestedBandwidth.Clear();
        nestedScope = true;
        Check(mech.GetStatValue(StatDefOf.WastepacksPerRecharge) == 40f && NestedBandwidth.SequenceEqual(new[] { 8f, 8f }), "nested LocalMesh scope preserves outer wastepack suppression depth");
        nestedScope = false;
        Check(mech.GetStatValue(StatDefOf.BandwidthCost, cacheStaleAfterTicks: 600) == 0f, "nested physical reads leave no cached raw bandwidth");
        crossThreadScope = true;
        Check(LocalMesh(mech) == 8f && crossThreadBandwidth == 0f, "active thread-local suppression does not leak to another thread");
    }

    private static void RemoteComposition()
    {
        Pawn actor = fx.World.NewColonist(new IntVec3(7, 0, 5));
        fx.World.GiveHediffDirect(actor, fx.World.RemoteController, fx.World.Part("Brain"));
        actor.mechanitor = new Pawn_MechanitorTracker(actor);
        Check(MechanitorUtility.ShouldBeMechanitor(actor), "remote-control implant composes with awakening subsystem");
        Pawn mech = fx.Mech(woken: true);
        Fixture.Enroll(actor, mech);
        Check(actor.mechanitor.UsedBandwidth == 0 && actor.mechanitor.TotalAvailableControlGroups >= 1
            && actor.mechanitor.ControlledPawns.Contains(mech), "remote-only controller retains awakened zero-cost controlled mech");
        Check(JobGiver_GetEnergy.GetMinAutorechargeThreshold(mech) == 5, "remote-control group retains native 5-percent recharge minimum");
        mech.GetComp<CompDeadManSwitch>().CompTick();
        Check(mech.GetComp<CompDeadManSwitch>().woken && LocalMesh(mech) == 8f, "remote control, periodic DMS check and LocalMesh remain composable");
    }

    private static float LocalMesh(Pawn pawn) => (float)Call(typeof(CompAbilityEffect_LocalMesh), "GetBandwidthMultiplier", pawn)!;
    private static bool Satisfied(ThinkNode node, Pawn pawn) => (bool)AccessTools.Method(node.GetType(), "Satisfied").Invoke(node, new object[] { pawn })!;
    private static object? Call(Type type, string name, params object[] args) => AccessTools.Method(type, name).Invoke(null, args);
    private static Exception? Catch(Action action)
    {
        try { action(); return null; }
        catch (TargetInvocationException error) { return error.InnerException; }
        catch (Exception error) { return error; }
    }
    private static Exception? WithSeed(int branch, Action action)
    {
        int seed = 0;
        for (; seed < 1000; seed++)
        {
            Rand.PushState(seed);
            _ = Rand.Range(300000, 1800000);
            int rolled = Rand.Chance(0.5f) ? 0 : Rand.Chance(0.5f) ? 1 : 2;
            Rand.PopState();
            if (rolled == branch) break;
        }
        Rand.PushState(seed);
        try { return Catch(action); }
        finally { Rand.PopState(); }
    }
    private static bool Letter() { letters++; return false; }
    private static bool Home(ref Map __result) { __result = hasHome ? fx.World.Map : null!; return false; }
    private static bool DropCell(ref IntVec3 __result) { __result = new IntVec3(9, 0, 9); return false; }
    private static bool Drop() { drops++; return false; }
    private static bool DestroySettlement() { destroyed++; return false; }
    private static bool ClearFootprint(ref bool __result) { __result = true; return false; }
    private static bool DestroyThing() => false;
    private static bool Skip() => false;
    private static bool Spawn(Thing newThing, IntVec3 loc, Map map, ref Thing __result)
    {
        AccessTools.Field(typeof(Thing), "mapIndexOrState").SetValue(newThing, (sbyte)0);
        AccessTools.Field(typeof(Thing), "positionInt").SetValue(newThing, loc);
        __result = newThing;
        return false;
    }
    private static void BandwidthProbe(StatWorker __instance, StatRequest req)
    {
        if ((StatDef)AccessTools.Field(typeof(StatWorker), "stat").GetValue(__instance)! != StatDefOf.BandwidthCost) return;
        if (throwBandwidth) throw new InvalidOperationException("Awake scope exception fixture");
        if (crossThreadScope && req.Thing is Pawn threaded)
        {
            crossThreadScope = false;
            crossThreadBandwidth = Task.Run(() => StatDefOf.BandwidthCost.Worker.GetValue(StatRequest.For(threaded))).GetAwaiter().GetResult();
        }
        if (nestedScope && req.Thing is Pawn pawn)
        {
            nestedScope = false;
            NestedBandwidth.Add(LocalMesh(pawn));
            NestedBandwidth.Add(pawn.GetStatValue(StatDefOf.BandwidthCost));
        }
    }
    private static void Check(bool value, string label)
    {
        if (!value) throw new InvalidOperationException("AWAKE FAIL " + label);
        assertions++;
        Console.WriteLine("PASS " + label);
    }

    private sealed class EmptyLoot : ThingSetMaker
    {
        protected override void Generate(ThingSetMakerParams parms, List<Thing> outThings) { }
        protected override IEnumerable<ThingDef> AllGeneratableThingsDebugSub(ThingSetMakerParams parms) => Array.Empty<ThingDef>();
    }

    private sealed class StateFixture : Pawn, IStateControllableMech
    {
        public bool ControllableByState => true;
    }

    private sealed class OverseerFixture : Pawn, IOverseerMech
    {
        public bool ControllableByState => true;
        public CompOverseer Comp => null!;
        public float MinCharge { get; set; }
        public float MaxCharge { get; set; } = 1f;
        public MechWorkModeDef WorkMode { get; set; } = null!;
        public void Notify_NameChanged() { }
    }
}
