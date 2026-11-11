using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using DMSSR.Jobs;
using DMSSR.UI;
using Fortified;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

internal static class Program
{
    private static readonly string GameRoot = Environment.GetEnvironmentVariable("RW_GAME_ROOT")
        ?? "A:/SteamLibrary/steamapps/common/RimWorld";
    private static readonly string HarmonyPath = Environment.GetEnvironmentVariable("RW_HARMONY_DLL")
        ?? "A:/SteamLibrary/steamapps/workshop/content/294100/2009463077/1.7/Assemblies/0Harmony.dll";
    private static readonly string FortifiedPath = Environment.GetEnvironmentVariable("RW_FORTIFIED_DLL")
        ?? "A:/SteamLibrary/steamapps/workshop/content/294100/3498575851/1.7/Assemblies/Fortified.dll";
    private static readonly string MultiplayerPath = Environment.GetEnvironmentVariable("RW_MULTIPLAYER_DLL")
        ?? "A:/SteamLibrary/steamapps/workshop/content/294100/3498575851/1.7/Assemblies/0MultiplayerAPI.dll";
    private static readonly string DmssrPath = Environment.GetEnvironmentVariable("RW_DMSSR_DLL")
        ?? "A:/SteamLibrary/steamapps/workshop/content/294100/3835512735/1.7/Assemblies/DMSSR.dll";

    private static int checks;

    public static int Main(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine("Usage: Driver <isolated-reference-directory> <patch-dll>");
            return 2;
        }
        string references = Path.GetFullPath(args[0]);
        AssemblyLoadContext.Default.Resolving += (_, name) =>
        {
            if (name.Name == "0Harmony") return AssemblyLoadContext.Default.LoadFromAssemblyPath(HarmonyPath);
            if (name.Name == "Fortified") return AssemblyLoadContext.Default.LoadFromAssemblyPath(FortifiedPath);
            if (name.Name == "0MultiplayerAPI") return AssemblyLoadContext.Default.LoadFromAssemblyPath(MultiplayerPath);
            if (name.Name == "DMSSR") return AssemblyLoadContext.Default.LoadFromAssemblyPath(DmssrPath);
            string file = (name.Name ?? "") + ".dll";
            foreach (string root in new[] { references, GameRoot })
            {
                string candidate = Path.Combine(root, file);
                if (File.Exists(candidate)) return AssemblyLoadContext.Default.LoadFromAssemblyPath(candidate);
            }
            return null;
        };
        try
        {
            Run(Path.GetFullPath(args[1]));
            Console.WriteLine($"PASS: {checks} real-DLL checks; no game process, configuration or save was opened.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Run(string patchDll)
    {
        SetupBoundaryStubs();

        // 1. RED baseline: verify the exact upstream failures before applying the patch
        {
            Fixture redTendon = new();
            Thing stackOfTwo = redTendon.SpawnItem(redTendon.TendonDef, stackCount: 2, new IntVec3(2, 0, 0));

            // Queue 2 Synthetic Tendon installs (Left Leg = 1, Right Leg = 2) using real MechModificationOrders.OrderInstall
            MechModificationOrders.OrderInstall(redTendon.Mech, redTendon.TendonDef, stackOfTwo, partIndex: 1, allowEquivalentPart: false, queue: false);
            Check(redTendon.Mech.CurJob is Job_Modification j0 && j0.targetPartIndex == 1,
                "RED: first OrderInstall becomes current job for Left Leg");
            Check(ModificationUtility.AvailableCount(stackOfTwo, redTendon.Mech) == 0,
                "RED: upstream reserves whole stack (-1), immediately reducing AvailableCount of a stack of 2 to 0");

            MechModificationOrders.OrderInstall(redTendon.Mech, redTendon.TendonDef, stackOfTwo, partIndex: 2, allowEquivalentPart: false, queue: true);
            Check(redTendon.Mech.jobs.jobQueue.Count == 0,
                "RED: second OrderInstall (Right Leg) is silently swallowed by TryTakeOrderedJob because curJob.JobIsSameAs returns true");
        }

        {
            Fixture redArmor = new();
            Thing armorStack = redArmor.SpawnItem(redArmor.CeramicArmorDef, stackCount: 2, new IntVec3(2, 0, 0));

            // Queue 2 Ceramic Armor installs on the same corePart (partIndex = 0)
            MechModificationOrders.OrderInstall(redArmor.Mech, redArmor.CeramicArmorDef, armorStack, partIndex: 0, allowEquivalentPart: false, queue: false);
            MechModificationOrders.OrderInstall(redArmor.Mech, redArmor.CeramicArmorDef, armorStack, partIndex: 0, allowEquivalentPart: false, queue: true);
            Check(redArmor.Mech.CurJob is Job_Modification && redArmor.Mech.jobs.jobQueue.Count == 0,
                "RED: second Ceramic Armor install on same body part is also swallowed by JobIsSameAs");
        }

        {
            Fixture redSeparate = new();
            Thing item1 = redSeparate.SpawnItem(redSeparate.TendonDef, stackCount: 1, new IntVec3(1, 0, 0));
            Thing item2 = redSeparate.SpawnItem(redSeparate.TendonDef, stackCount: 1, new IntVec3(3, 0, 0));

            Job_Modification job0 = ModificationJobUtility.MakeApplyJob(FFF_DefOf.FFF_Modification, redSeparate.Mech, item1, redSeparate.LeftLeg);
            Job_Modification job1 = ModificationJobUtility.MakeApplyJob(FFF_DefOf.FFF_Modification, redSeparate.Mech, item1, redSeparate.RightLeg);
            Check(job0.TryMakePreToilReservations(redSeparate.Mech, false), "RED: job0 reserves item1");
            Check(job1.TryMakePreToilReservations(redSeparate.Mech, false), "RED: job1 also reserves already-full item1 because CanReserve ignores same-claimant saturation");
            Check(job1.GetTarget(TargetIndex.B).Thing == item1,
                "RED: job1 fails to switch to item2 when queued because item1 was not checked for per-job availability");
        }

        // 2. Apply our patch and verify registration & idempotency
        Assembly patch = Assembly.LoadFrom(patchDll);
        Type fixType = patch.GetType("WhiteGiver.FFFMechModificationQueueFix.MechModificationQueueFix", throwOnError: true)!;
        MethodInfo applyMethod = fixType.GetMethod("Apply", BindingFlags.Public | BindingFlags.Static)!;
        applyMethod.Invoke(null, null);
        applyMethod.Invoke(null, null);

        MethodInfo jobIsSameAs = AccessTools.DeclaredMethod(typeof(Job), nameof(Job.JobIsSameAs), new[] { typeof(Pawn), typeof(Job) })!;
        MethodInfo tryResolve = AccessTools.DeclaredMethod(typeof(JobDriver_ApplyModification), "TryResolveAndReserveItem", new[] { typeof(bool) })!;
        MethodInfo tryIssue = AccessTools.DeclaredMethod(typeof(Window_MechModification), "TryIssueEntry", new[] { typeof(MechModificationQueueEntry), typeof(bool) })!;
        MethodInfo dmssrReserve = AccessTools.DeclaredMethod(typeof(JobDriver_ApplySyntheticBionic), nameof(JobDriver.TryMakePreToilReservations), new[] { typeof(bool) })!;

        Check(Harmony.GetPatchInfo(jobIsSameAs)?.Postfixes.Count(p => p.owner == "whitegiver.fff.mechmodificationqueuefix") == 1,
            "GREEN: Job.JobIsSameAs postfix registered once");
        Check(Harmony.GetPatchInfo(tryResolve)?.Prefixes.Count(p => p.owner == "whitegiver.fff.mechmodificationqueuefix") == 1,
            "GREEN: JobDriver_ApplyModification.TryResolveAndReserveItem prefix registered once");
        Check(Harmony.GetPatchInfo(tryIssue)?.Prefixes.Count(p => p.owner == "whitegiver.fff.mechmodificationqueuefix") == 1,
            "GREEN: Window_MechModification.TryIssueEntry prefix registered once");
        Check(Harmony.GetPatchInfo(dmssrReserve)?.Prefixes.Count(p => p.owner == "whitegiver.fff.mechmodificationqueuefix") == 1,
            "GREEN: DMSSR JobDriver_ApplySyntheticBionic.TryMakePreToilReservations prefix registered once");

        // 3. GREEN Scenario A: 2 Synthetic Tendons from a single stack of 2 (Left Leg + Right Leg)
        {
            Fixture greenStack = new();
            Thing stackOfTwo = greenStack.SpawnItem(greenStack.TendonDef, stackCount: 2, new IntVec3(2, 0, 0));

            MechModificationOrders.OrderInstall(greenStack.Mech, greenStack.TendonDef, stackOfTwo, partIndex: 1, allowEquivalentPart: false, queue: false);
            Check(greenStack.Mech.CurJob is Job_Modification j0 && j0.targetPartIndex == 1,
                "GREEN (stack=2 tendons): first job is active on Left Leg");
            Check(ModificationUtility.AvailableCount(stackOfTwo, greenStack.Mech) == 1,
                "GREEN (stack=2 tendons): first job reserves stackCount=1, leaving 1 available in the stack");

            MechModificationOrders.OrderInstall(greenStack.Mech, greenStack.TendonDef, stackOfTwo, partIndex: 2, allowEquivalentPart: false, queue: true);
            Check(greenStack.Mech.jobs.jobQueue.Count == 1,
                "GREEN (stack=2 tendons): second job is preserved in jobQueue");
            Job_Modification queuedJob = (Job_Modification)greenStack.Mech.jobs.jobQueue.Peek().job;
            Check(queuedJob.targetPartIndex == 2 && queuedJob.GetTarget(TargetIndex.B).Thing == stackOfTwo,
                "GREEN (stack=2 tendons): queued job targets Right Leg and shares the 2-item stack");
            Check(ModificationUtility.AvailableCount(stackOfTwo, greenStack.Mech) == 0,
                "GREEN (stack=2 tendons): after both jobs reserve 1 each, AvailableCount is 0");
            Check(!greenStack.OtherPawn.CanReserve(stackOfTwo),
                "GREEN (stack=2 tendons): another pawn cannot steal the reserved stack");

            // Simulate job0 taking 1 item from stackOfTwo and finishing
            stackOfTwo.stackCount -= 1;
            Check(stackOfTwo.Spawned && stackOfTwo.stackCount == 1,
                "GREEN (stack=2 tendons): 1 item remains spawned on ground after SplitOff(1)");
            greenStack.Mech.ClearReservationsForJob(greenStack.Mech.CurJob);

            // Simulate StartJob(queuedJob, fromQueue: true) re-running TryMakePreToilReservations
            Check(queuedJob.TryMakePreToilReservations(greenStack.Mech, false),
                "GREEN (stack=2 tendons): queued second job succeeds its start-time reservation check without self-blocking");
            Check(greenStack.Map.reservationManager.ReservedBy(stackOfTwo, greenStack.Mech, queuedJob),
                "GREEN (stack=2 tendons): queued second job still holds reservation on the remaining ground item");
        }

        // 4. GREEN Scenario B: 2 Synthetic Tendons from 2 separate single stacks (item1 stack=1, item2 stack=1)
        {
            Fixture greenSplit = new();
            Thing item1 = greenSplit.SpawnItem(greenSplit.TendonDef, stackCount: 1, new IntVec3(1, 0, 0));
            Thing item2 = greenSplit.SpawnItem(greenSplit.TendonDef, stackCount: 1, new IntVec3(3, 0, 0));

            // Both entries initially point to item1 (matching Window_MechModification.availableItems)
            MechModificationOrders.OrderInstall(greenSplit.Mech, greenSplit.TendonDef, item1, partIndex: 1, allowEquivalentPart: false, queue: false);
            MechModificationOrders.OrderInstall(greenSplit.Mech, greenSplit.TendonDef, item1, partIndex: 2, allowEquivalentPart: false, queue: true);

            Check(greenSplit.Mech.CurJob is Job_Modification active && active.GetTarget(TargetIndex.B).Thing == item1,
                "GREEN (2x stack=1 tendons): first job reserves closer item1");
            Check(greenSplit.Mech.jobs.jobQueue.Count == 1,
                "GREEN (2x stack=1 tendons): second job is queued");
            Job_Modification second = (Job_Modification)greenSplit.Mech.jobs.jobQueue.Peek().job;
            Check(second.GetTarget(TargetIndex.B).Thing == item2,
                "GREEN (2x stack=1 tendons): second job automatically switches targetB from saturated item1 to item2");
            Check(greenSplit.Map.reservationManager.ReservedBy(item1, greenSplit.Mech, greenSplit.Mech.CurJob)
                && greenSplit.Map.reservationManager.ReservedBy(item2, greenSplit.Mech, second),
                "GREEN (2x stack=1 tendons): both item1 and item2 are reserved by their respective jobs");

            // Simulate job0 destroying item1 and job1 starting from queue
            greenSplit.Mech.ClearReservationsForJob(greenSplit.Mech.CurJob);
            greenSplit.Map.listerThings.Remove(item1);
            SetField(item1, "mapIndexOrState", (sbyte)-2); // Destroyed
            Check(second.TryMakePreToilReservations(greenSplit.Mech, false),
                "GREEN (2x stack=1 tendons): second job starts cleanly on item2 after item1 is destroyed");
        }

        // 5. GREEN Scenario C: 2 Disposable Ceramic Armor plates queued on the SAME body part (corePart)
        {
            Fixture greenArmor = new();
            Thing armorStack = greenArmor.SpawnItem(greenArmor.CeramicArmorDef, stackCount: 3, new IntVec3(2, 0, 0));

            MechModificationOrders.OrderInstall(greenArmor.Mech, greenArmor.CeramicArmorDef, armorStack, partIndex: 0, allowEquivalentPart: false, queue: false);
            MechModificationOrders.OrderInstall(greenArmor.Mech, greenArmor.CeramicArmorDef, armorStack, partIndex: 0, allowEquivalentPart: false, queue: true);

            Check(greenArmor.Mech.CurJob is Job_Modification a0 && a0.targetPartIndex == 0,
                "GREEN (2x Ceramic Armor on corePart): first armor install is active");
            Check(greenArmor.Mech.jobs.jobQueue.Count == 1,
                "GREEN (2x Ceramic Armor on corePart): second armor install on the same corePart is queued instead of swallowed");
            Check(ModificationUtility.AvailableCount(armorStack, greenArmor.Mech) == 1,
                "GREEN (2x Ceramic Armor on corePart): 2 of 3 armor plates are reserved, leaving 1 available");
        }

        // 6. GREEN Scenario D: Window_MechModification.StartQueuedJobs while mech is already running a modification job
        {
            Fixture greenWindow = new();
            Thing item1 = greenWindow.SpawnItem(greenWindow.TendonDef, stackCount: 2, new IntVec3(2, 0, 0));

            MechModificationOrders.OrderInstall(greenWindow.Mech, greenWindow.TendonDef, item1, partIndex: 1, allowEquivalentPart: false, queue: false);
            Job firstActiveJob = greenWindow.Mech.CurJob;

            Window_MechModification window = (Window_MechModification)RuntimeHelpers.GetUninitializedObject(typeof(Window_MechModification));
            SetField(window, "Mech", greenWindow.Mech);
            List<MechModificationQueueEntry> queueList = new()
            {
                new MechModificationQueueEntry
                {
                    kind = MechModificationOperationKind.Install,
                    item = item1,
                    itemDef = greenWindow.TendonDef,
                    part = greenWindow.RightLeg,
                    submitted = false
                }
            };
            SetField(window, "QueuedOperations", queueList);
            MethodInfo startQueuedJobs = AccessTools.DeclaredMethod(typeof(Window_MechModification), "StartQueuedJobs")!;
            int started = (int)startQueuedJobs.Invoke(window, null)!;

            Check(started == 1 && greenWindow.Mech.CurJob == firstActiveJob && greenWindow.Mech.jobs.jobQueue.Count == 1,
                "GREEN (reopened window): submitting a new modification while one is already running appends to queue instead of interrupting");
        }

        // 7. GREEN Scenario E: DMSSR JobDriver_ApplySyntheticBionic queuing & fallback to second item
        {
            Fixture greenDmssr = new(useSynthroidBody: true);
            Thing bionic1 = greenDmssr.SpawnItem(greenDmssr.SyntheticBionicDef, stackCount: 1, new IntVec3(1, 0, 0));
            Thing bionic2 = greenDmssr.SpawnItem(greenDmssr.SyntheticBionicDef, stackCount: 1, new IntVec3(4, 0, 0));

            JobDef synthJobDef = new()
            {
                defName = "DMS_ApplySyntheticBionic",
                driverClass = typeof(JobDriver_ApplySyntheticBionic)
            };

            Job_ApplySyntheticBionic sJob0 = new()
            {
                def = synthJobDef,
                loadID = Find.UniqueIDsManager.GetNextJobID(),
                recipeDefName = "InstallSyntheticBionicLeg",
                targetPartIndex = 1,
                targetPartDefName = "DMS_SynthicLeg",
                count = 1,
                playerForced = true
            };
            sJob0.SetTarget(TargetIndex.A, greenDmssr.Mech);
            sJob0.SetTarget(TargetIndex.B, bionic1);

            Job_ApplySyntheticBionic sJob1 = new()
            {
                def = synthJobDef,
                loadID = Find.UniqueIDsManager.GetNextJobID(),
                recipeDefName = "InstallSyntheticBionicLeg",
                targetPartIndex = 2,
                targetPartDefName = "DMS_SynthicLeg",
                count = 1,
                playerForced = true
            };
            sJob1.SetTarget(TargetIndex.A, greenDmssr.Mech);
            sJob1.SetTarget(TargetIndex.B, bionic1);

            Check(greenDmssr.Mech.jobs.TryTakeOrderedJob(sJob0, JobTag.Misc, requestQueueing: false),
                "GREEN (DMSSR synthetic bionic): first bionic job accepted");
            Check(greenDmssr.Mech.jobs.TryTakeOrderedJob(sJob1, JobTag.Misc, requestQueueing: true),
                "GREEN (DMSSR synthetic bionic): second bionic job accepted");
            Check(greenDmssr.Mech.CurJob == sJob0 && greenDmssr.Mech.jobs.jobQueue.Count == 1,
                "GREEN (DMSSR synthetic bionic): second bionic job is queued, not swallowed by JobIsSameAs");
            Check(sJob1.GetTarget(TargetIndex.B).Thing == bionic2,
                "GREEN (DMSSR synthetic bionic): second bionic job automatically switches to bionic2 when bionic1 is already reserved");
        }
    }

    private static void SetupBoundaryStubs()
    {
        Harmony h = new("fff.mechmodqueue.test.stubs");

        h.Patch(AccessTools.Method(typeof(ForbidUtility), nameof(ForbidUtility.IsForbidden), new[] { typeof(Thing), typeof(Pawn) }),
            prefix: new HarmonyMethod(typeof(Program), nameof(NotForbiddenPrefix)));

        h.Patch(AccessTools.Method(typeof(ReachabilityUtility), nameof(ReachabilityUtility.CanReach),
                new[] { typeof(Pawn), typeof(LocalTargetInfo), typeof(PathEndMode), typeof(Danger), typeof(bool), typeof(bool), typeof(TraverseMode) }),
            prefix: new HarmonyMethod(typeof(Program), nameof(AlwaysReachablePrefix)));

        h.Patch(AccessTools.Method(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.IsCurrentJobPlayerInterruptible)),
            prefix: new HarmonyMethod(typeof(Program), nameof(AlwaysInterruptiblePrefix)));

        h.Patch(AccessTools.Method(typeof(Pawn_JobTracker), "CheckForJobOverride"),
            prefix: new HarmonyMethod(typeof(Program), nameof(StartQueuedFirstJobOnOverridePrefix)));
    }

    public static bool NotForbiddenPrefix(ref bool __result)
    {
        __result = false;
        return false;
    }

    public static bool AlwaysReachablePrefix(ref bool __result)
    {
        __result = true;
        return false;
    }

    public static bool AlwaysInterruptiblePrefix(ref bool __result)
    {
        __result = true;
        return false;
    }

    public static bool StartQueuedFirstJobOnOverridePrefix(Pawn_JobTracker __instance, Pawn ___pawn)
    {
        if (__instance.curJob == null && __instance.jobQueue.Count > 0)
        {
            QueuedJob qj = __instance.jobQueue.Dequeue();
            __instance.curJob = qj.job;
            __instance.curDriver = qj.job.MakeDriver(___pawn);
            ___pawn.mindState.lastJobTag = qj.tag ?? JobTag.Misc;
        }
        return false;
    }

    private sealed class Fixture
    {
        public Map Map { get; } = Uninitialized<Map>();
        public Pawn Mech { get; }
        public Pawn OtherPawn { get; }
        public ThingDef TendonDef { get; }
        public ThingDef CeramicArmorDef { get; }
        public ThingDef SyntheticBionicDef { get; }
        public BodyPartRecord CorePart { get; }
        public BodyPartRecord LeftLeg { get; }
        public BodyPartRecord RightLeg { get; }

        public Fixture(bool useSynthroidBody = false)
        {
            KeyBindingDefOf.QueueOrder ??= new KeyBindingDef();
            FFF_DefOf.FFF_Modification ??= new JobDef
            {
                defName = "FFF_Modification",
                driverClass = typeof(JobDriver_ApplyModification)
            };

            Game game = Uninitialized<Game>();
            SetField(game, "maps", new List<Map> { Map });
            SetField(game, "uniqueIDsManager", new UniqueIDsManager());
            Current.Game = game;

            Map.events = new MapEvents(Map);
            Map.physicalInteractionReservationManager = new PhysicalInteractionReservationManager();
            Map.reservationManager = new ReservationManager(Map);
            Map.pawnDestinationReservationManager = new PawnDestinationReservationManager();
            Map.enrouteManager = new EnrouteManager(Map);
            Map.attackTargetReservationManager = new AttackTargetReservationManager(Map);
            Map.listerThings = new ListerThings(ListerThingsUse.Global);

            BodyPartDef chassisDef = new() { defName = "DMS_Chassis", label = "chassis" };
            BodyPartDef legDef = new() { defName = "DMS_SynthicLeg", label = "leg" };
            CorePart = new BodyPartRecord { def = chassisDef, customLabel = "主体结构" };
            LeftLeg = new BodyPartRecord { def = legDef, customLabel = "左腿", parent = CorePart };
            RightLeg = new BodyPartRecord { def = legDef, customLabel = "右腿", parent = CorePart };
            CorePart.parts = new List<BodyPartRecord> { LeftLeg, RightLeg };

            BodyDef bodyDef = new() { defName = useSynthroidBody ? "DMS_Synthroid" : "DMS_IguanaBody", corePart = CorePart };
            SetField(bodyDef, "cachedAllParts", new List<BodyPartRecord> { CorePart, LeftLeg, RightLeg });
            CorePart.body = bodyDef;
            LeftLeg.body = bodyDef;
            RightLeg.body = bodyDef;

            TendonDef = GetOrAddThingDef("DMS_SyntheticTendon");
            CeramicArmorDef = GetOrAddThingDef("DMS_CeramicsPlating");
            SyntheticBionicDef = GetOrAddSyntheticBionicDef(legDef);

            Faction faction = Uninitialized<Faction>();
            Mech = MakePawn(faction, bodyDef, useSynthroidBody);
            OtherPawn = MakePawn(faction, bodyDef, false);
        }

        public Thing SpawnItem(ThingDef def, int stackCount, IntVec3 pos)
        {
            ThingWithComps thing = Uninitialized<ThingWithComps>();
            thing.def = def;
            thing.thingIDNumber = Find.UniqueIDsManager.GetNextThingID();
            thing.stackCount = stackCount;
            SetField(thing, "mapIndexOrState", (sbyte)0);
            SetField(thing, "positionInt", pos);
            Map.listerThings.Add(thing);
            return thing;
        }

        private static ushort nextShortHash = 100;
        private static ThingDef GetOrAddThingDef(string defName)
        {
            ThingDef? existing = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
            if (existing != null) return existing;
            ThingDef def = new()
            {
                defName = defName,
                label = defName,
                shortHash = nextShortHash++,
                thingClass = typeof(ThingWithComps),
                category = ThingCategory.Item,
                useHitPoints = false,
                stackLimit = 10
            };
            DefDatabase<ThingDef>.Add(def);
            return def;
        }

        private static ThingDef GetOrAddSyntheticBionicDef(BodyPartDef legDef)
        {
            ThingDef def = GetOrAddThingDef("DMS_SyntheticBionicLegItem");
            def.tradeTags ??= new List<string> { "Bionic" };
            if (DefDatabase<RecipeDef>.GetNamedSilentFail("InstallSyntheticBionicLeg") == null)
            {
                HediffDef addedLeg = new()
                {
                    defName = "SyntheticBionicLegHediff",
                    addedPartProps = new AddedBodyPartProps()
                };
                ThingFilter filter = new();
                SetField(filter, "allowedDefs", new HashSet<ThingDef> { def });
                IngredientCount ing = new();
                ing.SetBaseCount(1f);
                ing.filter = filter;
                RecipeDef recipe = new()
                {
                    defName = "InstallSyntheticBionicLeg",
                    addsHediff = addedLeg,
                    appliedOnFixedBodyParts = new List<BodyPartDef> { legDef },
                    ingredients = new List<IngredientCount> { ing }
                };
                DefDatabase<RecipeDef>.Add(recipe);
            }
            return def;
        }

        private static Pawn MakePawn(Faction faction, BodyDef bodyDef, bool withSyntheticInterface)
        {
            Pawn pawn = Uninitialized<Pawn>();
            pawn.thingIDNumber = Find.UniqueIDsManager.GetNextThingID();
            pawn.def = new ThingDef
            {
                defName = "DMS_Mech_Iguana",
                category = ThingCategory.Pawn,
                race = new RaceProperties { body = bodyDef }
            };
            SetField(pawn, "mapIndexOrState", (sbyte)0);
            SetField(pawn, "positionInt", IntVec3.Zero);
            SetField(pawn, "factionInt", faction);
            pawn.health = Uninitialized<Pawn_HealthTracker>();
            pawn.health.hediffSet = Uninitialized<HediffSet>();
            pawn.health.hediffSet.pawn = pawn;
            pawn.health.hediffSet.hediffs = new List<Hediff>();
            if (withSyntheticInterface)
            {
                Hediff access = Uninitialized<Hediff>();
                access.def = new HediffDef { defName = "DMSSR_WetwareSyntheticInterface" };
                pawn.health.hediffSet.hediffs.Add(access);
            }
            pawn.mindState = Uninitialized<Pawn_MindState>();
            pawn.mindState.pawn = pawn;
            pawn.mindState.lastJobTag = JobTag.Idle;
            pawn.jobs = new Pawn_JobTracker(pawn);
            pawn.inventory = new Pawn_InventoryTracker(pawn);
            pawn.stances = new Pawn_StanceTracker(pawn);
            return pawn;
        }
    }

    private static T Uninitialized<T>() where T : class => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));

    private static void SetField(object owner, string name, object value) =>
        (AccessTools.Field(owner.GetType(), name) ?? throw new MissingFieldException(owner.GetType().FullName, name)).SetValue(owner, value);

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException("FAIL: " + description);
        checks++;
        Console.WriteLine("PASS: " + description);
    }
}
