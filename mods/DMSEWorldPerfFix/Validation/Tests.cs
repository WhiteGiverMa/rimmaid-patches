using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using DMSE;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using WhiteGiver.DMSEWorldPerfFix;

namespace DMSEWorldPerfFix.Validation;

internal static class Tests
{
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    private const string StubHarmonyId = "whitegiver.dmse.worldperffix.validation";

    private static readonly Type AlertType = typeof(WorldComponent_FactionAirDefenseAlert);
    private static readonly Type NetworkType = typeof(WorldComponent_BVRNetwork);

    private static readonly FieldInfo SiteListField = AccessTools.DeclaredField(AlertType, "siteList")!;
    private static readonly FieldInfo SitesField = AccessTools.DeclaredField(AlertType, "sites")!;
    private static readonly FieldInfo NodesField = AccessTools.DeclaredField(NetworkType, "nodes")!;
    private static readonly FieldInfo ScanNodesField = AccessTools.DeclaredField(NetworkType, "scanNodes")!;
    private static readonly FieldInfo NodeActiveUntilField = AccessTools.DeclaredField(NetworkType, "nodeActiveUntil")!;

    private static readonly MethodInfo StateForMethod =
        AccessTools.DeclaredMethod(AlertType, "StateFor", new[] { typeof(MapParent), typeof(bool) })!;

    private static readonly MethodInfo BuildNodesMethod =
        AccessTools.DeclaredMethod(NetworkType, "BuildNodes", new[] { typeof(List<BVRNode>), typeof(bool) })!;

    private static int checks;
    private static int failures;

    internal static void Run(string mode)
    {
        InstallUiStubs();

        switch (mode)
        {
            case "probe":
                Probe();
                break;
            case "red":
                Red();
                break;
            case "green":
                Green();
                break;
            default:
                throw new ArgumentException("Unknown mode: " + mode);
        }

        Console.WriteLine();
        Console.WriteLine($"{(failures == 0 ? "OK" : "FAILED")}: {checks - failures}/{checks} checks passed.");
        if (failures != 0)
        {
            throw new InvalidOperationException($"{failures} validation check(s) failed.");
        }
    }

    // ==================================================================== probe

    private static void Probe()
    {
        WorldFixture fixture = WorldFixture.Create();
        Info("tiles on the registered planet layer: " + Find.WorldGrid.TilesCount);
        Info("alert system enabled: " + WorldComponent_FactionAirDefenseAlert.Enabled);
        Info("siteList: " + SiteListCount(fixture));
        Info("sites dict: " + SitesCount(fixture));

        AirDefenseAlertTuningDef tuning = AirDefenseAlertTuningDef.Current;
        Info($"tuning: baselineNoRadar={tuning.baselineNoRadar} baselineRadar={tuning.baselineRadar} baselineMilitary={tuning.baselineMilitary}");

        Settlement industrial = fixture.Settlements[0];
        Settlement tribal = fixture.Settlements[WorldFixture.FirstTribalIndex];
        Settlement military = fixture.Settlements[WorldFixture.FirstMilitaryIndex];
        Info($"baseline industrial={WorldComponent_FactionAirDefenseAlert.BaselineFor(industrial)}");
        Info($"baseline tribal={WorldComponent_FactionAirDefenseAlert.BaselineFor(tribal)}");
        Info($"baseline military={WorldComponent_FactionAirDefenseAlert.BaselineFor(military)}");
        Info($"baseline player={WorldComponent_FactionAirDefenseAlert.BaselineFor(fixture.Settlements[WorldFixture.FirstPlayerIndex])}");
        Info($"radar capable industrial={WorldComponent_FactionAirDefenseAlert.RadarCapable(industrial)}");
        Info($"radar capable tribal={WorldComponent_FactionAirDefenseAlert.RadarCapable(tribal)}");

        Settlement? near = fixture.NearestSameFaction(industrial, 20f);
        Info($"nearest same-faction to settlement 0: {(near == null ? "none" : near.Name + " @ " + fixture.DistanceBetween(industrial, near).ToString("0.##") + " tiles")}");

        BuildNodes(fixture, NodesField, activeOnly: false);
        Info("siteList after BuildNodes(activeOnly:false): " + SiteListCount(fixture));
        Info("sites dict after BuildNodes(activeOnly:false): " + SitesCount(fixture));

        fixture.SetTicks(250);
        fixture.Alert.WorldComponentTick();
        Info("siteList after WorldComponentTick(250): " + SiteListCount(fixture));
    }

    // ==================================================================== RED

    private static void Red()
    {
        Info("=== RED: unpatched DMSE 1.7 on a 1,300-settlement world ===");
        WorldFixture fixture = WorldFixture.Create();
        Require(fixture.Settlements.Count == WorldFixture.SettlementCount, "fixture has 1,300 settlements");
        Require(WorldComponent_FactionAirDefenseAlert.Enabled, "DMSE radar-network research stub enables the alert system");

        int before = SiteListCount(fixture);
        Require(before == 0, "fresh world starts with an empty siteList");

        BuildNodes(fixture, NodesField, activeOnly: false);
        int after = SiteListCount(fixture);
        int military = fixture.MilitarySites().Count;
        Info($"BuildNodes(activeOnly:false) materialised {after} AirDefenseSiteAlert objects " +
             $"({WorldFixture.DefenseSettlementCount} settlement-defense nodes + {military} military sites)");
        Require(after >= 1000, "RED: a read-only node rebuild pollutes siteList with 1,000+ settlements");
        Require(SitesCount(fixture) == after, "RED: the sites dictionary grows together with siteList");

        int idle = 0;
        foreach (AirDefenseSiteAlert state in SiteList(fixture))
        {
            if (state.stage == state.baseline
                && state.local == 0f
                && state.regional == 0f
                && state.records.Count == 0
                && state.waves.Count == 0
                && state.blockadeAt < 0
                && state.warnedStage == 0
                && state.radarWarmUntil < 0
                && state.directThreatUntil < 0)
            {
                idle++;
            }
        }

        Info($"of those, {idle}/{after} are 100% idle baseline states (empty records/waves, zero pressure, -1 timers)");
        Require(idle == after, "RED: every polluted state is an idle baseline state");

        fixture.SetTicks(250);
        long allocBefore = GC.GetAllocatedBytesForCurrentThread();
        Stopwatch stopwatch = Stopwatch.StartNew();
        fixture.Alert.WorldComponentTick();
        stopwatch.Stop();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocBefore;

        Info($"WorldComponentTick at t=250 over {after} idle sites: {stopwatch.Elapsed.TotalMilliseconds:0.###} ms, {allocated:N0} B allocated");
        Require(SiteListCount(fixture) == after, "RED: the 250-tick pass never removes a site that is at its peaceful baseline");
        Require(allocated > 50_000, "RED: the 250-tick pass allocates the per-site RemoveAll closures (and doctrine closures)");

        long allocBeforeSecond = GC.GetAllocatedBytesForCurrentThread();
        fixture.SetTicks(500);
        fixture.Alert.WorldComponentTick();
        long allocatedSecond = GC.GetAllocatedBytesForCurrentThread() - allocBeforeSecond;
        Info($"second 250-tick pass: {allocatedSecond:N0} B allocated, siteList still {SiteListCount(fixture)}");
        Require(allocatedSecond > 50_000, "RED: the allocation repeats every 250 ticks in steady state");

        long doctrineBefore = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
        {
            _ = MissileDoctrine.For(fixture.IndustrialFaction.def, DefDatabase<MissileDoctrineDef>.AllDefsListForReading);
        }

        long doctrineAllocated = GC.GetAllocatedBytesForCurrentThread() - doctrineBefore;
        Info($"1,000 x MissileDoctrine.For(industrial): {doctrineAllocated:N0} B allocated");
        Require(doctrineAllocated > 10_000, "RED: every doctrine lookup allocates a FirstOrDefault closure");

        fixture.SetTicks(250);
        List<BVRNode> scanNodes = ScanNodes(fixture);
        NodeActiveUntil(fixture).Clear();
        NodeActiveUntil(fixture)[fixture.Settlements[0].ID] = 10_000;
        NodeActiveUntil(fixture)[fixture.Settlements[1].ID] = 10_000;
        NodeActiveUntil(fixture)[fixture.Settlements[WorldFixture.FirstMilitaryIndex].ID] = 10_000;
        NodeActiveUntil(fixture)[fixture.Settlements[2].ID] = 249;
        NodeActiveUntil(fixture)[fixture.Settlements[WorldFixture.DestroyedIndex].ID] = 10_000;
        NodeActiveUntil(fixture)[987654321] = 10_000;

        BuildNodes(fixture, ScanNodesField, activeOnly: true);
        Info($"BuildNodes(activeOnly:true) with 6 ids in nodeActiveUntil produced {scanNodes.Count} scan nodes");

        stopwatch.Restart();
        for (int i = 0; i < 200; i++)
        {
            BuildNodes(fixture, ScanNodesField, activeOnly: true);
        }

        stopwatch.Stop();
        Info($"200 x BuildNodes(activeOnly:true): {stopwatch.Elapsed.TotalMilliseconds:0.###} ms");
        Require(stopwatch.Elapsed.TotalMilliseconds > 0.5, "RED: the active scan rebuild walks every world object");
        Require(scanNodes.Count == 3, "RED: only the 3 live ids become scan nodes");
    }

    // ==================================================================== GREEN

    private static void Green()
    {
        Info("=== GREEN: DMSEWorldPerfFix on a 1,300-settlement world ===");

        // ---------------------------------------------------------------- reference (upstream)
        WorldFixture referenceWorld = WorldFixture.Create();
        Require(WorldComponent_FactionAirDefenseAlert.Enabled, "DMSE radar-network research stub enables the alert system");
        Require(SiteListCount(referenceWorld) == 0, "fresh reference world starts with an empty siteList");

        BuildNodes(referenceWorld, NodesField, activeOnly: false);
        int referencePollution = SiteListCount(referenceWorld);
        Info($"upstream pollution baseline: {referencePollution} idle states");

        referenceWorld.SetTicks(250);
        long referenceAllocBefore = GC.GetAllocatedBytesForCurrentThread();
        Stopwatch referenceWatch = Stopwatch.StartNew();
        referenceWorld.Alert.WorldComponentTick();
        referenceWatch.Stop();
        long referenceTickAlloc = GC.GetAllocatedBytesForCurrentThread() - referenceAllocBefore;
        Info($"upstream tick: {referenceWatch.Elapsed.TotalMilliseconds:0.###} ms, {referenceTickAlloc:N0} B");

        int[] referenceStage = new int[WorldFixture.SettlementCount];
        bool[] referenceWarm = new bool[WorldFixture.SettlementCount];
        for (int i = 0; i < WorldFixture.SettlementCount; i++)
        {
            Settlement settlement = referenceWorld.Settlements[i];
            referenceStage[i] = referenceWorld.Alert.StageOf(settlement);
            referenceWarm[i] = referenceWorld.Alert.RadarWarm(settlement);
        }

        Dictionary<string, string> referenceDoctrine = new();
        foreach (Faction faction in referenceWorld.AllFactions())
        {
            referenceDoctrine[faction.def.defName] = MissileDoctrine.For(faction)?.defName ?? "<null>";
        }

        long referenceDoctrineAllocBefore = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
        {
            _ = MissileDoctrine.For(referenceWorld.IndustrialFaction.def, DefDatabase<MissileDoctrineDef>.AllDefsListForReading);
        }

        long referenceDoctrineAlloc = GC.GetAllocatedBytesForCurrentThread() - referenceDoctrineAllocBefore;

        referenceWorld.SetTicks(250);
        NodeActiveUntil(referenceWorld).Clear();
        NodeActiveUntil(referenceWorld)[referenceWorld.Settlements[0].ID] = 10_000;
        NodeActiveUntil(referenceWorld)[referenceWorld.Settlements[1].ID] = 10_000;
        NodeActiveUntil(referenceWorld)[referenceWorld.Settlements[WorldFixture.FirstMilitaryIndex].ID] = 10_000;
        NodeActiveUntil(referenceWorld)[referenceWorld.Settlements[2].ID] = 249;
        NodeActiveUntil(referenceWorld)[referenceWorld.Settlements[WorldFixture.DestroyedIndex].ID] = 10_000;
        NodeActiveUntil(referenceWorld)[987654321] = 10_000;

        List<string> referenceScan = Snapshot(referenceWorld);
        Stopwatch referenceScanWatch = Stopwatch.StartNew();
        for (int i = 0; i < 200; i++)
        {
            BuildNodes(referenceWorld, ScanNodesField, activeOnly: true);
        }

        referenceScanWatch.Stop();

        Info($"upstream 200 x active scan rebuild: {referenceScanWatch.Elapsed.TotalMilliseconds:0.###} ms, {referenceScan.Count} nodes");

        // ---------------------------------------------------------------- install
        int installed = WorldPerfFixMod.ApplyPatches();
        Info($"installed {installed} patch groups: {string.Join(", ", WorldPerfFixMod.InstalledPatches)}");
        Require(installed >= 4, "all four patch groups install");

        // ---------------------------------------------------------------- G1 polluted save
        referenceWorld.SetTicks(500);
        long coldAllocBefore = GC.GetAllocatedBytesForCurrentThread();
        Stopwatch pruneWatch = Stopwatch.StartNew();
        referenceWorld.Alert.WorldComponentTick();
        pruneWatch.Stop();
        long coldAlloc = GC.GetAllocatedBytesForCurrentThread() - coldAllocBefore;

        Info($"GREEN cold prune of the {referencePollution}-state polluted save: {pruneWatch.Elapsed.TotalMilliseconds:0.###} ms, {coldAlloc} B (one-time JIT/reflection warm-up)");
        Require(SiteListCount(referenceWorld) == 0, "GREEN: one tick hibernates every quiescent baseline state of a polluted save");
        Require(SitesCount(referenceWorld) == 0, "GREEN: the sites dictionary is emptied with it");
        Require(pruneWatch.Elapsed.TotalMilliseconds < 5.0, "GREEN: the cleanup stays under 5 ms");

        // Warm pass: pollute 1,000 settlements again now that every code path is JIT-compiled and cached.
        referenceWorld.SetTicks(750);
        for (int i = 0; i < 1000; i++)
        {
            _ = StateFor(referenceWorld, referenceWorld.Settlements[i]);
        }

        Require(SiteListCount(referenceWorld) == 1000, "GREEN: 1,000 idle states can be re-materialised for the warm measurement");
        referenceWorld.Advance(250);
        long warmAllocBefore = GC.GetAllocatedBytesForCurrentThread();
        Stopwatch warmWatch = Stopwatch.StartNew();
        referenceWorld.Alert.WorldComponentTick();
        warmWatch.Stop();
        long warmAlloc = GC.GetAllocatedBytesForCurrentThread() - warmAllocBefore;
        Info($"GREEN warm prune of 1,000 idle states: {warmWatch.Elapsed.TotalMilliseconds:0.###} ms, {warmAlloc} B");
        Require(SiteListCount(referenceWorld) == 0, "GREEN: the warm cleanup hibernates all 1,000 idle states");
        Require(warmAlloc < 1024, "GREEN: the warm cleanup is allocation-free (0 closure garbage)");
        Require(warmWatch.Elapsed.TotalMilliseconds < 5.0, "GREEN: the warm cleanup stays under 5 ms");

        long empty1 = EmptyTickAlloc(referenceWorld);
        long empty2 = EmptyTickAlloc(referenceWorld);
        long empty3 = EmptyTickAlloc(referenceWorld);
        Info($"GREEN idle-world tick with an empty siteList: {empty1} B, {empty2} B, {empty3} B");
        Require(empty3 == 0, "GREEN: an empty siteList tick is a zero-allocation O(1) return");

        Info($"GREEN upstream references: tick {referenceTickAlloc:N0} B, doctrine {referenceDoctrineAlloc:N0} B per 1,000 calls");

        // ---------------------------------------------------------------- G2 query parity on a fresh world
        WorldFixture world = WorldFixture.Create();
        AirspaceRestrictionUtility.Invalidate();

        int mismatches = 0;
        for (int i = 0; i < WorldFixture.SettlementCount; i++)
        {
            Settlement settlement = world.Settlements[i];
            int stage = world.Alert.StageOf(settlement);
            bool warm = world.Alert.RadarWarm(settlement);
            if (stage != referenceStage[i] || warm != referenceWarm[i])
            {
                mismatches++;
            }
        }

        Info($"GREEN StageOf/RadarWarm parity over {WorldFixture.SettlementCount} settlements: {mismatches} mismatches");
        Require(mismatches == 0, "GREEN: StageOf and RadarWarm return 100% identical values to upstream");
        Require(SiteListCount(world) == 0, "GREEN: the 1,300 read-only queries add zero states to siteList");
        Require(SitesCount(world) == 0, "GREEN: the sites dictionary stays empty");

        int[] distribution = new int[6];
        for (int i = 0; i < WorldFixture.SettlementCount; i++)
        {
            distribution[Math.Clamp(referenceStage[i], 0, 5)]++;
        }

        Info($"baseline distribution (stage 0..5): {string.Join(", ", distribution.Select((count, stage) => stage + "=" + count))}");

        // ---------------------------------------------------------------- G3 mixed quarantine
        int now = 500;
        world.SetTicks(now);
        int activeStart = WorldFixture.FirstPlayerIndex - 6;
        for (int i = 0; i < activeStart; i++)
        {
            _ = StateFor(world, world.Settlements[i]);
        }

        Settlement[] active = world.Settlements.Skip(activeStart).Take(6).ToArray();
        AirDefenseSiteAlert pressure = StateFor(world, active[0]);
        pressure.local = 45f;

        AirDefenseSiteAlert waves = StateFor(world, active[1]);
        waves.waves.Add(new AirDefenseWaveRecord { id = "validation-wave", tick = now, local = true });

        AirDefenseSiteAlert pending = StateFor(world, active[2]);
        pending.blockadeAt = now + 5000;
        pending.stage = 4;

        AirDefenseSiteAlert warming = StateFor(world, active[3]);
        warming.radarWarmUntil = now + 500;

        AirDefenseSiteAlert direct = StateFor(world, active[4]);
        direct.directThreatUntil = now + 500;

        AirDefenseSiteAlert warned = StateFor(world, active[5]);
        warned.stage = 3;
        warned.warnedStage = 3;
        warned.stageSinceTick = now;

        Info($"G3 created {activeStart} idle + 6 active states ({SiteListCount(world)} in siteList)");
        world.Advance(250);
        world.Alert.WorldComponentTick();
        int remaining = SiteListCount(world);
        Info($"G3 after one tick: {remaining} states remain");
        Require(remaining == 6, "GREEN: exactly the six non-quiescent states survive hibernation");
        Require(SitesCount(world) == remaining, "GREEN: the sites dictionary matches siteList after hibernation");
        Require(Exists(world, active[0]) && Exists(world, active[1]) && Exists(world, active[2]) && Exists(world, active[3]) && Exists(world, active[4]) && Exists(world, active[5]),
            "GREEN: active pressure/waves/blockade/warm-up/direct-threat/warned states are untouched");

        // ---------------------------------------------------------------- G4 wake up / escalate / hibernate
        WorldFixture wake = WorldFixture.Create();
        AirspaceRestrictionUtility.Invalidate();
        wake.SetTicks(10_000);

        Settlement anchor = wake.Settlements[900];
        Require(anchor.Faction == wake.IndustrialFaction || anchor.Faction == wake.SpacerFaction || anchor.Faction == wake.UltraFaction,
            "G4 anchor is an Industrial+ settlement");

        wake.Alert.DevAddPressure(anchor, 25f, 0f);
        AirDefenseSiteAlert? anchorState = StateForOrNull(wake, anchor);
        Require(anchorState != null, "GREEN: DevAddPressure materialises the threatened site");
        Info($"G4 DevAddPressure 25 -> stage {anchorState!.stage}, threat {anchorState.Threat:0.##}, radarWarm={wake.Alert.RadarWarm(anchor)}");
        Require(anchorState.stage == 3, "GREEN: pressure escalates the threatened site to stage 3");
        Require(SiteListCount(wake) == 1, "GREEN: only the threatened site is materialised");

        Settlement tribal = wake.Settlements[WorldFixture.FirstTribalIndex + 10];
        wake.Alert.DevSetStage(tribal, 3);
        AirDefenseSiteAlert? tribalState = StateForOrNull(wake, tribal);
        Require(tribalState != null && tribalState.radarWarmUntil > wake.Now, "GREEN: a stage jump arms the radar warm-up window");
        Require(!wake.Alert.RadarWarm(tribal), "GREEN: RadarWarm is false while the radar warms up");
        wake.Advance(601);
        Require(wake.Alert.RadarWarm(tribal), "GREEN: RadarWarm becomes true once the window elapses");

        Settlement neighborAnchor = wake.Settlements[950];
        Settlement? neighbor = wake.NearestSameFaction(neighborAnchor, 18f);
        Require(neighbor != null, "G4 support-radius neighbour exists within 18 tiles");
        Settlement? far = wake.Settlements
            .Where(s => s.Faction == neighborAnchor.Faction && !s.Destroyed)
            .OrderByDescending(s => wake.DistanceBetween(neighborAnchor, s))
            .First();

        bool farHadState = HasState(wake, far!);
        wake.Alert.DevInject(neighborAnchor, "targeted");
        Require(HasState(wake, neighbor!), "GREEN: a real observation wakes the support-radius neighbour into siteList");
        Require(farHadState || !HasState(wake, far!), "GREEN: settlements outside the support radius stay cold");
        Info($"G4 DevInject(targeted) at {neighborAnchor.Name}: neighbour {neighbor!.Name} @ {wake.DistanceBetween(neighborAnchor, neighbor):0.##} tiles woken; far {far!.Name} @ {wake.DistanceBetween(neighborAnchor, far):0.##} tiles cold={!HasState(wake, far)}");

        int beforeStage5 = wake.Now;
        for (int i = 0; i < 6; i++)
        {
            wake.Advance(1);
            wake.Alert.DevInject(neighborAnchor, "targeted");
        }

        AirDefenseSiteAlert? escalated = StateForOrNull(wake, neighborAnchor);
        Require(escalated != null, "G4 the injected site keeps a state");
        Info($"G4 after 7 targeted injections: stage {escalated!.stage}, L={escalated.local:0.#}, R={escalated.regional:0.#}, waves={escalated.RecentWaves(wake.Now)}, blockadeAt={escalated.blockadeAt - wake.Now}");
        Require(escalated.waves.Count >= 2, "GREEN: repeated waves are recorded");
        Require(escalated.local >= 80f, "GREEN: repeated waves accumulate stage-5 pressure");
        Require(escalated.stage >= 4, "GREEN: pressure escalates to stage 4 with a pending blockade");

        int blockadeAt = escalated.blockadeAt >= 0 ? escalated.blockadeAt : wake.Now + 5000;
        wake.SetTicks((blockadeAt / 250 + 2) * 250);
        wake.Alert.WorldComponentTick();
        AirDefenseSiteAlert? blocked = StateForOrNull(wake, neighborAnchor);
        Info($"G4 after blockade notice delay: stage {blocked?.stage}, blockadeActive={blocked?.BlockadeActive}");
        Require(blocked != null && blocked.BlockadeActive, "GREEN: the blockade becomes active at stage 5");
        Require(SiteListCount(wake) >= 1, "GREEN: an active blockade is never hibernated");

        // Jump past attackWindowTicks (60,000), the record keep window (120,000) and minStageHoldTicks
        // (15,000) between every evaluation, so the stage machine can step down once per tick.
        wake.SetTicks(beforeStage5 + 200_000);
        for (int i = 0; i < 12; i++)
        {
            wake.SetTicks((wake.Now / 250 + 1) * 250 + 20_000);
            wake.Alert.WorldComponentTick();
        }

        Info($"G4 after decay + 12 ticks: siteList={SiteListCount(wake)}, anchor stage={StateForOrNull(wake, neighborAnchor)?.stage.ToString() ?? "<gone>"}");
        Require(SiteListCount(wake) == 0, "GREEN: once threat, waves, records and timers are gone every site hibernates again");

        // ---------------------------------------------------------------- G5 scan-node parity
        NodeActiveUntil(world).Clear();
        NodeActiveUntil(world)[world.Settlements[0].ID] = 100_000;
        NodeActiveUntil(world)[world.Settlements[1].ID] = 100_000;
        NodeActiveUntil(world)[world.Settlements[WorldFixture.FirstMilitaryIndex].ID] = 100_000;
        NodeActiveUntil(world)[world.Settlements[2].ID] = 249;
        NodeActiveUntil(world)[world.Settlements[WorldFixture.DestroyedIndex].ID] = 100_000;
        NodeActiveUntil(world)[987654321] = 100_000;
        world.SetTicks(250);

        List<string> patchedScan = Snapshot(world);
        Require(patchedScan.Count == referenceScan.Count, $"GREEN: active scan rebuild has the same node count ({patchedScan.Count})");
        Require(patchedScan.SequenceEqual(referenceScan), "GREEN: active scan rebuild produces an identical BVRNode sequence (content and order)");

        Stopwatch patchedScanWatch = Stopwatch.StartNew();
        for (int i = 0; i < 200; i++)
        {
            BuildNodes(world, ScanNodesField, activeOnly: true);
        }

        patchedScanWatch.Stop();
        Info($"patched 200 x active scan rebuild: {patchedScanWatch.Elapsed.TotalMilliseconds:0.###} ms (upstream {referenceScanWatch.Elapsed.TotalMilliseconds:0.###} ms)");
        Require(patchedScanWatch.Elapsed.TotalMilliseconds * 2 < referenceScanWatch.Elapsed.TotalMilliseconds,
            "GREEN: the active scan rebuild is more than twice as fast as upstream");

        // ---------------------------------------------------------------- G6 doctrine cache
        int doctrineMismatch = 0;
        foreach (Faction faction in world.AllFactions())
        {
            string actual = MissileDoctrine.For(faction)?.defName ?? "<null>";
            if (referenceDoctrine[faction.def.defName] != actual)
            {
                doctrineMismatch++;
            }
        }

        Require(doctrineMismatch == 0, "GREEN: MissileDoctrine.For returns the same doctrine for every faction");

        long doctrineAllocBefore = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10_000; i++)
        {
            _ = MissileDoctrine.For(world.IndustrialFaction.def, DefDatabase<MissileDoctrineDef>.AllDefsListForReading);
        }

        long doctrineAlloc = GC.GetAllocatedBytesForCurrentThread() - doctrineAllocBefore;
        Info($"GREEN 10,000 x MissileDoctrine.For(industrial): {doctrineAlloc} B (upstream 1,000 calls: {referenceDoctrineAlloc:N0} B)");
        Require(doctrineAlloc < 512, "GREEN: the cached doctrine lookup is allocation-free");

        // ---------------------------------------------------------------- G7 idempotency
        int again = WorldPerfFixMod.ApplyPatches();
        Require(again == installed, "GREEN: applying the patches twice is a no-op");
    }

    // ==================================================================== helpers

    private static void InstallUiStubs()
    {
        Harmony harmony = new(StubHarmonyId);
        harmony.UnpatchAll(StubHarmonyId);

        MethodInfo message = AccessTools.Method(
            typeof(Messages),
            "Message",
            new[] { typeof(string), typeof(LookTargets), typeof(MessageTypeDef), typeof(bool) })
            ?? throw new MissingMethodException("Verse.Messages", "Message");

        MethodInfo letter = AccessTools.Method(
            typeof(LetterStack),
            "ReceiveLetter",
            new[]
            {
                typeof(TaggedString), typeof(TaggedString), typeof(LetterDef), typeof(LookTargets),
                typeof(Faction), typeof(Quest), typeof(List<ThingDef>), typeof(string), typeof(int), typeof(bool),
            })
            ?? throw new MissingMethodException("Verse.LetterStack", "ReceiveLetter");

        MethodInfo blockade = AccessTools.DeclaredMethod(
            AlertType,
            "NotifyBlockade",
            new[]
            {
                typeof(MapParent), typeof(AirDefenseSiteAlert), typeof(string), typeof(string), typeof(LetterDef),
            })
            ?? throw new MissingMethodException("DMSE.WorldComponent_FactionAirDefenseAlert", "NotifyBlockade");

        MethodInfo languageWorker = AccessTools.PropertyGetter(typeof(Find), "ActiveLanguageWorker")
            ?? throw new MissingMethodException("Verse.Find", "get_ActiveLanguageWorker");

        HarmonyMethod skip = new(typeof(Tests), nameof(SkipUi));
        harmony.Patch(message, prefix: skip);
        harmony.Patch(letter, prefix: skip);
        harmony.Patch(blockade, prefix: skip);
        harmony.Patch(
            languageWorker,
            prefix: new HarmonyMethod(typeof(Tests), nameof(EnglishWorkerPrefix)));
    }

    private static bool SkipUi() => false;

    private static readonly LanguageWorker EnglishWorker = new LanguageWorker_English();

    private static bool EnglishWorkerPrefix(ref LanguageWorker __result)
    {
        __result = EnglishWorker;
        return false;
    }

    private static List<AirDefenseSiteAlert> SiteList(WorldFixture fixture)
    {
        return (List<AirDefenseSiteAlert>)SiteListField.GetValue(fixture.Alert)!;
    }

    private static int SiteListCount(WorldFixture fixture)
    {
        return SiteList(fixture).Count;
    }

    private static int SitesCount(WorldFixture fixture)
    {
        return ((Dictionary<int, AirDefenseSiteAlert>)SitesField.GetValue(fixture.Alert)!).Count;
    }

    private static Dictionary<int, int> NodeActiveUntil(WorldFixture fixture)
    {
        return (Dictionary<int, int>)NodeActiveUntilField.GetValue(fixture.Network)!;
    }

    private static List<BVRNode> ScanNodes(WorldFixture fixture)
    {
        return (List<BVRNode>)ScanNodesField.GetValue(fixture.Network)!;
    }

    private static void BuildNodes(WorldFixture fixture, FieldInfo field, bool activeOnly)
    {
        List<BVRNode> target = (List<BVRNode>)field.GetValue(fixture.Network)!;
        BuildNodesMethod.Invoke(fixture.Network, new object[] { target, activeOnly });
    }

    private static AirDefenseSiteAlert StateFor(WorldFixture fixture, MapParent parent)
    {
        return (AirDefenseSiteAlert)StateForMethod.Invoke(fixture.Alert, new object[] { parent, true })!;
    }

    private static AirDefenseSiteAlert? StateForOrNull(WorldFixture fixture, MapParent parent)
    {
        return (AirDefenseSiteAlert?)StateForMethod.Invoke(fixture.Alert, new object[] { parent, false });
    }

    private static bool HasState(WorldFixture fixture, MapParent parent)
    {
        return StateForOrNull(fixture, parent) != null;
    }

    private static bool Exists(WorldFixture fixture, MapParent parent)
    {
        return StateForOrNull(fixture, parent) != null;
    }

    private static List<string> Snapshot(WorldFixture fixture)
    {
        BuildNodes(fixture, ScanNodesField, activeOnly: true);
        List<string> result = new();
        foreach (BVRNode node in ScanNodes(fixture))
        {
            string defense = node.defense?.GetType().Name ?? "<null>";
            string faction = node.faction?.def?.defName ?? "<null>";
            result.Add($"{node.parent?.ID}|{node.parent?.Label}|{faction}|{defense}|{node.alertStage}|{node.rangeFactor:0.####}|{node.jamEff:0.####}");
        }

        return result;
    }

    private static long EmptyTickAlloc(WorldFixture fixture)
    {
        fixture.Advance(250);
        long before = GC.GetAllocatedBytesForCurrentThread();
        fixture.Alert.WorldComponentTick();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private static void Info(string message)
    {
        Console.WriteLine(message);
    }

    private static void Require(bool condition, string message)
    {
        checks++;
        if (condition)
        {
            Console.WriteLine("  PASS  " + message);
            return;
        }

        failures++;
        Console.WriteLine("  FAIL  " + message);
    }

}

internal static class FactionExtensions
{
    internal static IEnumerable<Faction> AllFactions(this WorldFixture fixture)
    {
        yield return fixture.PlayerFaction;
        yield return fixture.TribalFaction;
        yield return fixture.IndustrialFaction;
        yield return fixture.SpacerFaction;
        yield return fixture.UltraFaction;
    }
}
