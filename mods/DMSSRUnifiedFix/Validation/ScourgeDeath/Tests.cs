using WhiteGiver.DMSSRUnifiedFix;
using DMSSRUnifiedFix.Validation;
using DMSSR.Content;
using DMSSR.Hediffs;
using DMSSR.Misc;
using DMSSR.Patches;
using Fortified;
using HarmonyLib;
using RimWorld;
using Verse;

namespace DMSSRUnifiedFix.Validation.ScourgeDeath;

internal static class Tests
{
    private const string Owner = "whitegiver.dmssr.unifiedfix";
    private static int assertions;

    internal static void Run(string mode)
    {
        bool green = mode != "red";
        Probes.Install(mode == "green-first");
        Fixture fixture = new();
        Check(Harmony.HasAnyPatches(Owner) == (mode == "green-first"), "sidecar initial load order is correct");
        if (mode == "green")
            Probes.ApplySidecar();
        if (green)
        {
            Check(Harmony.HasAnyPatches(Owner), "sidecar patches are installed");
            Check(Harmony.GetAllPatchedMethods().Count(method => Harmony.GetPatchInfo(method).Transpilers.Any(patch => patch.owner == Owner && patch.PatchMethod.DeclaringType?.Name == "DeathHediffRemoval")) == 2,
                "sidecar changes exactly the two removal callers");
        }
        Lifecycle(fixture, mode, player: false);
        Lifecycle(fixture, mode, player: true);
        NullDamageImplants(fixture, mode);
        if (green) Compatibility(fixture);
        Console.WriteLine($"PASS {mode.ToUpperInvariant()} assertions={assertions}");
    }

    private static void Lifecycle(Fixture fixture, string mode, bool player)
    {
        fixture.ClearMap();
        HumanlikeMech pawn = fixture.NewMech();
        if (player) fixture.SetPlayer(pawn);
        AccessTools.Method(typeof(Patch_InitialDeathCheat_SpawnSetup), "Postfix")
            .Invoke(null, new object[] { pawn, fixture.Map, false });
        Check(pawn.health.hediffSet.GetFirstHediffOfDef(DMS_HediffDefOf.DMS_DeathCheat)
            .TryGetComp<HediffComp_DeathCheatCharges>().charges == 1, "real initial-death-cheat postfix grants one charge");
        Check(!pawn.health.hediffSet.HasHediff(fixture.Born), "first life has no BornFromDeath");
        Probes.Reset();
        fixture.LethalBullet(pawn);
        AssertCleanDeath(fixture, pawn, 1, "first death");
        Corpse first = pawn.Corpse;
        if (player)
        {
            Check(!fixture.Tracker.IsPending(first), "player corpse waits for release");
            Command_Action release = Patch_Corpse_GetGizmos_AddUndying.Postfix(Array.Empty<Gizmo>(), first).OfType<Command_Action>().Single();
            Check(!(bool)AccessTools.Field(typeof(Gizmo), "disabled").GetValue(release)!, "real player Hyperplasia Release gizmo is enabled");
            release.action();
        }
        Check(fixture.Tracker.IsPending(first), player ? "player schedule accepted" : "real corpse-spawn postfix schedules NPC release");
        fixture.SetTick(Find.TickManager.TicksGame + 240);
        fixture.Tracker.MapComponentTick();
        Check(!pawn.Dead && pawn.Spawned && first.Destroyed && first.InnerPawn == null, "real tracker and TryResurrect restore the pawn and destroy its old corpse");
        Check(pawn.health.hediffSet.HasHediff(fixture.Born), "real resurrection postfix reapplies BornFromDeath");
        Check(!pawn.health.hediffSet.HasHediff(DMS_HediffDefOf.DMS_DeathCheat), "real release consumes the single charge");
        Check(pawn.health.hediffSet.HasHediff(DMS_HediffDefOf.DMS_OppressiveRestructuring), "release adds OppressiveRestructuring");
        Check(Probes.Errors.Count == 0, "first death and resurrection have no Log.Error");

        fixture.Corpses.Clear();
        Probes.Reset();
        fixture.LethalBullet(pawn);
        Dump($"{mode} {(player ? "player" : "NPC")} second death", fixture);
        Check(!pawn.health.hediffSet.HasHediff(fixture.Born), "second death removes BornFromDeath");
        if (mode == "red")
        {
            Check(Probes.PawnKills == 2, "RED reenters Pawn.Kill");
            Check(Probes.NestedKillStack.Contains("Pawn_HealthTracker.RemoveHediff")
                && Probes.NestedKillStack.Contains("Pawn_HealthTracker.CheckForStateChange"), "RED nested Kill originates in the removal state check");
            Check(Probes.Errors.Any(error => error.Contains("set dead while already dead.")), "RED double-dead error");
            Check(Probes.Errors.Any(error => error.Contains("spawned in bugged state.")), "RED bugged corpse error");
            Check(Probes.Errors.Count == 2, "RED contains exactly the two reported errors");
            Check(fixture.Corpses.Count == 2 && fixture.Corpses[0].Bugged && fixture.Corpses[0].InnerPawn == null
                && fixture.Corpses[1].InnerPawn == pawn && !fixture.Corpses[1].Bugged, "RED first corpse is empty, second owns the pawn");
            Check(!fixture.Corpses[0].Spawned && !fixture.Corpses[0].Destroyed, "RED rejected empty corpse is unspawned, not half-spawned");
            Check(Probes.BuggedSpawnStack.Split("Verse.GenSpawn.Spawn(").Length - 1 == 2,
                "RED bugged-corpse stack contains exactly two real GenSpawn.Spawn frames");
        }
        else
        {
            AssertCleanDeath(fixture, pawn, 1, "second death");
            Corpse second = pawn.Corpse;
            Probes.Reset();
            Check(ResurrectionUtility.TryResurrect(pawn), "external second resurrection succeeds");
            Check(second.Destroyed && pawn.health.hediffSet.HasHediff(fixture.Born), "external resurrection reapplies BornFromDeath");
            fixture.Corpses.Clear();
            Probes.Reset();
            fixture.LethalBullet(pawn);
            AssertCleanDeath(fixture, pawn, 1, "third death");
            Check(!pawn.health.hediffSet.HasHediff(fixture.Born), "third death removes re-applied buff");
        }
    }

    private static void NullDamageImplants(Fixture fixture, string mode)
    {
        foreach (bool scourge in new[] { false, true })
        {
            fixture.ClearMap();
            HumanlikeMech pawn = fixture.NewMech(scourge);
            pawn.health.AddHediff(fixture.Implant);
            fixture.AddFatalInjuryWithoutCheck(pawn);
            Probes.Reset();
            pawn.Kill(null, pawn.health.hediffSet.hediffs.Last());
            Dump($"{mode} Kill(null) scourge={scourge}", fixture);
            Check(!pawn.health.hediffSet.HasHediff(fixture.Implant) && RemovalProbe.PreCount == 1 && RemovalProbe.PostCount == 1,
                "implant PreRemoved/PostRemoved run exactly once");
            if (mode == "red")
            {
                Check(Probes.PawnKills == 2 && Probes.Errors.Any(error => error.Contains("set dead while already dead.")), "RED Fortified null-damage path also reenters death");
                Check(Probes.Errors.Count == 3 && Probes.Errors.Any(error => error.Contains("spawned in bugged state."))
                    && Probes.Errors.Any(error => error.Contains("in a null map.")), "RED null-damage path also loses the implant drop and empties the first corpse");
            }
            else
            {
                AssertCleanDeath(fixture, pawn, 1, "null-damage death");
                Check(fixture.Map.listerThings.AllThings.Count(thing => thing.def == fixture.Implant.spawnThingOnRemoved) == 1,
                    "Fortified implant drop is preserved exactly once");
            }
        }
    }

    private static void Compatibility(Fixture fixture)
    {
        foreach (int priority in new[] { Priority.First, Priority.Last })
        foreach (bool throwPrefix in new[] { false, true })
        {
            Probes.SetCancellationPriority(priority);
            fixture.ClearMap();
            HumanlikeMech pawn = fixture.NewMech();
            ScourgeResurrection.Apply(pawn);
            fixture.AddFatalInjuryWithoutCheck(pawn);
            Probes.Reset();
            Probes.CancelDeath = !throwPrefix;
            Probes.ThrowDeath = throwPrefix;
            try { pawn.Kill(new DamageInfo(fixture.Bullet, 100)); }
            catch (InvalidOperationException exception) when (throwPrefix && exception.Message == "Expected death-prefix failure") { }
            Check(!pawn.Dead && !pawn.health.isBeingKilled && Probes.FlagAtCancellation == false,
                throwPrefix ? "throwing Pawn.Kill prefix sees/restores the normal flag" : "cancelling Pawn.Kill prefix sees/restores the normal flag");
            Check(fixture.Corpses.Count == 0 && Probes.SetDeadCalls == 0 && Probes.Errors.Count == 0, "cancel/throw creates no corpse and no errors");
            if (!throwPrefix) Check(!pawn.health.hediffSet.HasHediff(fixture.Born), "cancel retains upstream buff-removal semantics without leaking the flag");
        }

        Probes.SetCancellationPriority(Priority.Last);

        fixture.ClearMap();
        HumanlikeMech exceptional = fixture.NewMech(false);
        exceptional.health.AddHediff(fixture.Implant);
        fixture.AddFatalInjuryWithoutCheck(exceptional);
        Probes.Reset();
        RemovalProbe.Throw = true;
        bool caught = false;
        try { exceptional.Kill(null); }
        catch (InvalidOperationException exception) when (exception.Message == "Expected removal failure") { caught = true; }
        Check(caught && !exceptional.health.isBeingKilled && !exceptional.Dead, "removal callback exception restores the flag and propagates");

        fixture.ClearMap();
        HumanlikeMech alreadyGuarded = fixture.NewMech(false);
        alreadyGuarded.health.AddHediff(fixture.Implant);
        Probes.Reset();
        alreadyGuarded.health.isBeingKilled = true;
        Probes.CancelDeath = true;
        alreadyGuarded.Kill(null);
        Check(alreadyGuarded.health.isBeingKilled && Probes.FlagAtCancellation == true, "pre-existing true flag is preserved rather than forcibly cleared");
        alreadyGuarded.health.isBeingKilled = false;
        Probes.Reset();

        fixture.ClearMap();
        HumanlikeMech combined = fixture.NewMech();
        ScourgeResurrection.Apply(combined);
        combined.health.AddHediff(fixture.Implant);
        fixture.AddFatalInjuryWithoutCheck(combined);
        Probes.Reset();
        combined.Kill(null);
        AssertCleanDeath(fixture, combined, 1, "BornFromDeath and implant together");
        Check(!combined.health.hediffSet.HasHediff(fixture.Born) && !combined.health.hediffSet.HasHediff(fixture.Implant), "both targeted removals complete normally");

        fixture.ClearMap();
        HumanlikeMech outside = fixture.NewMech(false);
        Hediff implant = outside.health.AddHediff(fixture.Implant);
        fixture.AddFatalInjuryWithoutCheck(outside);
        Probes.Reset();
        outside.health.RemoveHediff(implant);
        AssertCleanDeath(fixture, outside, 1, "normal RemoveHediff outside death still checks fatal state");
    }

    private static void AssertCleanDeath(Fixture fixture, Pawn pawn, int corpses, string name)
    {
        Check(pawn.Dead && Probes.PawnKills == 1 && Probes.SetDeadCalls == 1, name + ": exactly one Kill/SetDead");
        Check(Probes.Errors.Count == 0 && !pawn.health.isBeingKilled, name + ": zero Log.Error and restored flag");
        Check(fixture.Corpses.Count == corpses && fixture.Corpses.Last() == pawn.Corpse
            && pawn.Corpse.Spawned && !pawn.Corpse.Bugged && pawn.Corpse.InnerPawn == pawn, name + ": one valid owning corpse");
    }

    private static void Dump(string name, Fixture fixture)
    {
        foreach (string line in Probes.Events) Console.WriteLine("TRACE " + line);
        if (Probes.NestedKillStack.Length != 0) Console.WriteLine("NESTED KILL STACK\n" + Probes.NestedKillStack);
        Console.WriteLine($"RESULT {name} kills={Probes.PawnKills} setDead={Probes.SetDeadCalls} corpses={fixture.Corpses.Count} errors={Probes.Errors.Count}");
    }

    internal static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("FAIL " + message);
        assertions++;
        Console.WriteLine("PASS " + message);
    }
}
