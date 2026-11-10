using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
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
    private static readonly string PocketPath = Environment.GetEnvironmentVariable("RW_POCKETSAND_DLL")
        ?? "A:/SteamLibrary/steamapps/workshop/content/294100/2226330302/1.6/Assemblies/PocketSand.dll";
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
            if (name.Name == "PocketSand") return AssemblyLoadContext.Default.LoadFromAssemblyPath(PocketPath);
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
        Assembly pocket = Assembly.LoadFrom(PocketPath);
        Type driverType = pocket.GetType("PocketSand.JobDriver_Equip", throwOnError: true)!;
        MethodInfo original = driverType.GetMethod(nameof(JobDriver.TryMakePreToilReservations))!;
        Fixture baseline = new();
        Job baselineHaul = baseline.MakeJob(typeof(JobDriver_HaulToCell), baseline.Weapon);
        baselineHaul.targetB = new IntVec3(4, 0, 4);
        Check(baselineHaul.TryMakePreToilReservations(baseline.Hauler, false), "fixture: real vanilla hauling driver reserves its targets");
        Check(!baseline.Wearer.CanReserve(baseline.Weapon), "fixture: vanilla hauling reservation is respected by another pawn");
        baseline.Hauler.ClearReservationsForJob(baselineHaul);
        Check(baseline.Wearer.CanReserve(baseline.Weapon), "fixture: vanilla cleanup releases hauling reservations");
        Fixture red = new();
        Job redJob = red.MakeJob(driverType, red.Weapon, playerForced: true);
        Check(redJob.TryMakePreToilReservations(red.Wearer, false), "RED: original Pocket Sand reports reservation success");
        Check(!red.Map.reservationManager.IsReserved(red.Weapon), "RED: no actual reservation was created");
        Check(red.Hauler.CanReserve(red.Weapon), "RED: ordinary automatic hauler can reserve the equip target");

        Assembly patch = Assembly.LoadFrom(patchDll);
        Type fix = patch.GetType("WhiteGiver.PocketSandEquipReservationFix.EquipReservationFix", true)!;
        MethodInfo apply = fix.GetMethod("Apply")!;
        apply.Invoke(null, null);
        Check(Harmony.GetPatchInfo(original)?.Postfixes.Count(p => p.owner == "whitegiver.pocketsand.equipreservationfix") == 1,
            "patch is attached to the real Pocket Sand method");
        apply.Invoke(null, null);
        Check(Harmony.GetPatchInfo(original)?.Postfixes.Count(p => p.owner == "whitegiver.pocketsand.equipreservationfix") == 1,
            "registration is idempotent");

        Fixture floor = new();
        Job job = floor.MakeJob(driverType, floor.Weapon, playerForced: true);
        Check(job.TryMakePreToilReservations(floor.Wearer, false), "GREEN: ground equip reserves successfully");
        ReservationManager.Reservation reservation = floor.Map.reservationManager.ReservationsReadOnly.Single();
        Check(reservation.Target.Thing == floor.Weapon && reservation.Claimant == floor.Wearer && reservation.Job == job,
            "reservation belongs to the real target, wearer and job");
        Check(reservation.MaxPawns == 1 && reservation.StackCount == -1 && reservation.Layer == null,
            "reservation uses vanilla one-pawn whole-stack parameters");
        Check(!floor.Hauler.CanReserve(floor.Weapon), "GREEN: ordinary automatic hauling is blocked while wearer walks");
        Check(job.TryMakePreToilReservations(floor.Wearer, false), "queue-to-start second reservation succeeds");
        Check(floor.Map.reservationManager.ReservationsReadOnly.Count == 1, "queue-to-start does not duplicate the reservation");
        floor.Wearer.ClearReservationsForJob(job);
        Check(floor.Hauler.CanReserve(floor.Weapon), "vanilla job cleanup releases the weapon");

        Fixture stack = new();
        stack.Weapon.stackCount = 3;
        Job stackJob = stack.MakeJob(driverType, stack.Weapon);
        Check(stackJob.TryMakePreToilReservations(stack.Wearer, false), "stacked ground weapon reserves");
        Check(!stack.Hauler.CanReserve(stack.Weapon, 1, 1), "whole-stack reservation also blocks a partial haul");
        stack.Wearer.ClearReservationsForJob(stackJob);
        Check(stack.Hauler.CanReserve(stack.Weapon, 1, 1), "stack reservation is released on cleanup");

        Fixture inventory = new();
        SetField(inventory.Weapon, "mapIndexOrState", (sbyte)-1);
        inventory.Weapon.holdingOwner = new ThingOwner<Thing>(inventory.Wearer.inventory);
        Job inventoryJob = inventory.MakeJob(driverType, inventory.Weapon);
        Check(inventoryJob.TryMakePreToilReservations(inventory.Wearer, false), "inventory weapon swap still succeeds");
        Check(inventory.Map.reservationManager.ReservationsReadOnly.Count == 0, "inventory swap creates no map reservation");

        Fixture standFixture = new();
        Building_OutfitStand stand = Uninitialized<Building_OutfitStand>();
        stand.def = new ThingDef();
        SetField(stand, "mapIndexOrState", (sbyte)0);
        SetField(standFixture.Weapon, "mapIndexOrState", (sbyte)-1);
        standFixture.Weapon.holdingOwner = new ThingOwner<Thing>(stand);
        Job standJob = standFixture.MakeJob(driverType, standFixture.Weapon);
        Check(standJob.TryMakePreToilReservations(standFixture.Wearer, false), "outfit-stand weapon reserves its source");
        Check(standFixture.Map.reservationManager.ReservationsReadOnly.Single().Target.Thing == stand,
            "outfit stand, not its unspawned weapon, is reserved");
        Check(!standFixture.Hauler.CanReserve(stand), "a second ordinary pawn cannot reserve the outfit stand");

        Fixture conflict = new();
        Job haul = conflict.MakeJob(typeof(JobDriver_HaulToCell), conflict.Weapon);
        Check(conflict.Hauler.Reserve(conflict.Weapon, haul, errorOnFailed: false), "existing ordinary hauling reservation is established");
        Job blocked = conflict.MakeJob(driverType, conflict.Weapon);
        Check(!blocked.TryMakePreToilReservations(conflict.Wearer, false), "non-forced equip returns reservation failure");
        Check(conflict.Map.reservationManager.ReservedBy(conflict.Weapon, conflict.Hauler, haul),
            "failed non-forced equip does not steal another job's reservation");
        conflict.Hauler.jobs.jobQueue.EnqueueLast(haul);
        Job forced = conflict.MakeJob(driverType, conflict.Weapon, playerForced: true);
        Check(forced.TryMakePreToilReservations(conflict.Wearer, false), "player-forced equip retains vanilla reservation preemption");
        Check(conflict.Hauler.jobs.jobQueue.Count == 0, "preempted queued haul is removed by the real job tracker");
        Check(conflict.Map.reservationManager.ReservationsReadOnly.Count == 1
            && conflict.Map.reservationManager.ReservedBy(conflict.Weapon, conflict.Wearer, forced),
            "preemption leaves only the equip reservation");

        Fixture active = new();
        Job currentHaul = active.MakeJob(typeof(JobDriver_HaulToCell), active.Weapon);
        Check(active.Hauler.Reserve(active.Weapon, currentHaul, errorOnFailed: false), "active haul reserves target before equip order");
        active.Hauler.jobs.curJob = currentHaul;
        active.Hauler.jobs.curDriver = new JobDriver_HaulToCell { pawn = active.Hauler, job = currentHaul };
        Job currentForced = active.MakeJob(driverType, active.Weapon, playerForced: true);
        // Keep real termination/cleanup, stopping only the next unrelated work search at its boundary.
        new Harmony("pocketsand.reservation.validation").Patch(
            AccessTools.Method(typeof(Pawn_JobTracker), "TryFindAndStartJob"),
            prefix: new HarmonyMethod(typeof(Program), nameof(SkipNextJobSearch)));
        Check(currentForced.TryMakePreToilReservations(active.Wearer, false), "player-forced equip preempts an active haul");
        Check(active.Hauler.jobs.curJob == null && active.Hauler.jobs.curDriver == null,
            "real job termination clears the active hauling job and driver");
        Check(active.Map.reservationManager.ReservationsReadOnly.Count == 1
            && active.Map.reservationManager.ReservedBy(active.Weapon, active.Wearer, currentForced),
            "active haul cleanup preserves the new equip reservation");

        MethodInfo postfix = fix.GetMethod("ReserveEquipment")!;
        foreach (Thing? target in new Thing?[] { conflict.Weapon, null })
        {
            JobDriver driver = (JobDriver)Activator.CreateInstance(driverType)!;
            driver.pawn = conflict.Wearer;
            driver.job = conflict.MakeJob(driverType, target);
            object[] arguments = { driver, false, false };
            int previousCount = conflict.Map.reservationManager.ReservationsReadOnly.Count;
            postfix.Invoke(null, arguments);
            Check(!(bool)arguments[2] && conflict.Map.reservationManager.ReservationsReadOnly.Count == previousCount,
                "an upstream false result is preserved without new reservations");
        }
        Job invalid = conflict.MakeJob(driverType, null);
        Check(invalid.TryMakePreToilReservations(conflict.Wearer, false), "invalid target does not introduce a new exception");
    }

    private static bool SkipNextJobSearch() => false;

    private sealed class Fixture
    {
        public Map Map { get; } = Uninitialized<Map>();
        public Pawn Wearer { get; }
        public Pawn Hauler { get; }
        public ThingWithComps Weapon { get; }

        public Fixture()
        {
            Game game = Uninitialized<Game>();
            SetField(game, "maps", new List<Map> { Map });
            Current.Game = game;
            Map.events = new MapEvents(Map);
            Map.physicalInteractionReservationManager = new PhysicalInteractionReservationManager();
            Map.reservationManager = new ReservationManager(Map);
            Map.pawnDestinationReservationManager = new PawnDestinationReservationManager();
            Map.enrouteManager = new EnrouteManager(Map);
            Map.attackTargetReservationManager = new AttackTargetReservationManager(Map);
            Faction faction = Uninitialized<Faction>();
            Wearer = MakePawn(faction);
            Hauler = MakePawn(faction);
            Weapon = Uninitialized<ThingWithComps>();
            Weapon.def = new ThingDef { defName = "FixtureWeapon", category = ThingCategory.Item, stackLimit = 1 };
            Weapon.stackCount = 1;
            SetField(Weapon, "mapIndexOrState", (sbyte)0);
        }

        public Job MakeJob(Type driver, Thing? target, bool playerForced = false) => new()
        {
            def = new JobDef { defName = "FixtureEquip", driverClass = driver },
            targetA = target == null ? LocalTargetInfo.Invalid : new LocalTargetInfo(target),
            playerForced = playerForced
        };

        private static Pawn MakePawn(Faction faction)
        {
            Pawn pawn = Uninitialized<Pawn>();
            pawn.def = new ThingDef { defName = "FixturePawn", category = ThingCategory.Pawn };
            SetField(pawn, "mapIndexOrState", (sbyte)0);
            SetField(pawn, "factionInt", faction);
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
