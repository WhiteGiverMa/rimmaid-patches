using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using UnburnedDNAFPerfFix;
using Verse;
using Verse.AI;

namespace Validation;

internal static class BehaviorTests
{
    private static Thing? rangedTarget;
    private static Verb? verb;
    private static int rangedScans;
    private static void Call(JobDriver_Wait d) => Tests.Wait(d);
    internal static void Run(bool green)
    {
        var f = new Fixture(30);
        Pawn p = f.Pawn(new IntVec3(10, 0, 10), true);
        Tests.Flammability = 1;
        Fire large = f.Fire(new IntVec3(11, 0, 10), 1.5f);
        Fire small = f.Fire(new IntVec3(9, 0, 10), 0.2f);
        Tests.Beats.Clear(); Call(f.Wait(p));
        Tests.Check(Tests.Beats.LastOrDefault() == small, "smallest adjacent fire selected");
        Fire center = f.Fire(p.Position, 1.7f); f.Tick();
        Tests.Beats.Clear(); Call(f.Wait(p));
        Tests.Check(Tests.Beats.LastOrDefault() == center, "center i==8 overrides smaller neighbor");
        center.parent = p; f.Tick();
        Tests.Beats.Clear(); Call(f.Wait(p));
        Tests.Check(Tests.Beats.LastOrDefault() == small, "own attached fire excluded");
        Tests.Flammability = 0;
        Type settings = Program.Unburned.GetType("Unburned.Settings")!;
        settings.GetField("OnlyDrafted")!.SetValue(null, false);
        Tests.Beats.Clear(); Call(f.Wait(p));
        Tests.Check(Tests.Beats.Count == 0, "real PawnShouldBeatFire rejects nonflammable pawn");
        settings.GetField("OnlyDrafted")!.SetValue(null, true);
        Tests.Flammability = 1;
        Pawn enemy = f.Pawn(p.Position + GenAdj.AdjacentCellsAndInside[0], true, f.Enemy);
        Pawn second = f.Pawn(p.Position + GenAdj.AdjacentCellsAndInside[1], true, f.Enemy);
        f.Tick(); Tests.Attacks.Clear(); Tests.Beats.Clear();
        JobDriver_Wait wait = f.Wait(p); Call(wait);
        Tests.Check(Tests.Attacks.FirstOrDefault() == enemy && Tests.Beats.Count == 0 && wait.collideWithPawns, "first melee target wins over fire");
        Fixture.Set(enemy.health, "healthState", PawnHealthState.Down);
        f.Tick(); Tests.Attacks.Clear(); Call(f.Wait(p));
        Tests.Check(Tests.Attacks.FirstOrDefault() == second, "downed first target skipped");

        // Original state guard must not reset collideWithPawns.
        p.kindDef.canMeleeAttack = false; wait = f.Wait(p); Call(wait);
        Tests.Check(wait.collideWithPawns, "initial guard preserves collideWithPawns");
        p.kindDef.canMeleeAttack = true;
        var far = f.Pawn(new IntVec3(25, 0, 25), true); f.Tick();
        wait = f.Wait(far); Call(wait);
        Tests.Check(!wait.collideWithPawns, "eligible idle pawn clears collideWithPawns");

        // Target selector is a leaf boundary: assert exact call flags and attack action,
        // without constructing an entire LOS/cover/verb equipment subsystem.
        Audit.Hooks.Patch(AccessTools.PropertyGetter(typeof(Pawn), nameof(Pawn.CurrentEffectiveVerb)), prefix: new HarmonyMethod(typeof(BehaviorTests), nameof(VerbHook)));
        Audit.Hooks.Patch(AccessTools.Method(typeof(AttackTargetFinder), nameof(AttackTargetFinder.BestShootTargetFromCurrentPosition)), prefix: new HarmonyMethod(typeof(BehaviorTests), nameof(Ranged)));
        Audit.Hooks.Patch(AccessTools.Method(typeof(Pawn), nameof(Pawn.TryStartAttack)), prefix: new HarmonyMethod(typeof(BehaviorTests), nameof(StartAttack)));
        verb = new TestVerb { verbProps = new VerbProperties() };
        rangedTarget = second; rangedScans = 0;
        wait = f.Wait(far, true); Call(wait);
        Tests.Check(rangedScans == 1 && wait.collideWithPawns && Tests.Attacks.Last() == second, "Wait_Combat ranged targeting preserved outside melee bounds");
        rangedScans = 0; Call(f.Wait(far));
        Tests.Check(rangedScans == 0, "noncombat wait does not run ranged selector");
        wait = f.Wait(far, true); wait.job.canUseRangedWeapon = false; Call(wait);
        Tests.Check(rangedScans == 0, "canUseRangedWeapon=false honored");
        rangedTarget = null;

        if (green)
        {
            MapSnapshot s = MapSnapshot.Get(f.Map);
            int builds = s.Builds;
            for (int i = 0; i < 100; i++) Call(f.Wait(far));
            Tests.Check(s.Builds == builds, "at most one full snapshot per map/tick");
            // Move an enemy after the snapshot. Use real ThingGrid.Register event.
            f.Map.thingGrid.Deregister(second);
            Fixture.Set(second, "positionInt", far.Position + GenAdj.AdjacentCellsAndInside[0]);
            f.Map.thingGrid.Register(second);
            Tests.Attacks.Clear(); Call(f.Wait(far));
            Tests.Check(Tests.Attacks.FirstOrDefault() == second, "same-tick target movement expands melee reach");

            f.Map.thingGrid.Deregister(enemy);
            Fixture.Set(enemy, "positionInt", new IntVec3(0, 0, 0));
            f.Map.thingGrid.Register(enemy);
            f.Map.thingGrid.Deregister(second);
            Fixture.Set(second, "positionInt", new IntVec3(1, 0, 0));
            f.Map.thingGrid.Register(second);
            f.Tick(); Tests.Scans = 0; Call(f.Wait(far));
            Tests.Check(Tests.Scans == 0, "active threats elsewhere do not restore distant pawn's nine-cell scan");
            f.Map.thingGrid.Deregister(second);
            Fixture.Set(second, "positionInt", far.Position + GenAdj.AdjacentCellsAndInside[0]);
            f.Map.thingGrid.Register(second);

            // Pair-only mod postfix can turn hostility off. Exact live checks still run.
            MethodInfo hostile = AccessTools.Method(typeof(GenHostility), nameof(GenHostility.HostileTo), [typeof(Thing), typeof(Thing)]);
            MethodInfo remove = AccessTools.Method(typeof(BehaviorTests), nameof(RemoveHostility));
            Audit.Hooks.Patch(hostile, postfix: new HarmonyMethod(remove));
            Tests.Attacks.Clear(); Call(f.Wait(far));
            Tests.Check(Tests.Attacks.Count == 0, "live pair-hostility postfix respected (DMSSR suppression shape)");
            Audit.Hooks.Unpatch(hostile, remove);

            // Randomized differential fire selection vs retained upstream body.
            var original = Audit.Hooks.CreateReversePatcher(Tests.Pretty, new HarmonyMethod(typeof(BehaviorTests), nameof(OriginalPretty))).Patch();
            var rng = new Random(7717);
            for (int i = 0; i < 200; i++)
            {
                large.fireSize = (float)rng.NextDouble() * 2; small.fireSize = (float)rng.NextDouble() * 2;
                center.parent = rng.Next(2) == 0 ? p : null;
                Tests.Beats.Clear(); Tests.Attacks.Clear(); var d = f.Wait(p);
                original.Invoke(null, [d]);
                var expectedBeats = Tests.Beats.ToArray(); var expectedAttacks = Tests.Attacks.ToArray(); bool collide = d.collideWithPawns;
                Tests.Beats.Clear(); Tests.Attacks.Clear(); d = f.Wait(p); Call(d);
                if (!Tests.Beats.SequenceEqual(expectedBeats) || !Tests.Attacks.SequenceEqual(expectedAttacks) || collide != d.collideWithPawns)
                    throw new Exception("Differential selection failed seed 7717 iteration " + i);
            }
            Tests.Check(true, "200 deterministic exact-selection differential cases");
        }
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool OriginalPretty(JobDriver_Wait driver) => throw new NotSupportedException("Harmony reverse-patch stand-in");
    private static bool VerbHook(ref Verb? __result) { __result = verb; return false; }
    private static bool Ranged(TargetScanFlags flags, ref IAttackTarget? __result)
    {
        if (flags != (TargetScanFlags)291) throw new Exception("Unexpected target flags: " + flags);
        rangedScans++; __result = rangedTarget as IAttackTarget; return false;
    }
    private static bool StartAttack(LocalTargetInfo targ, ref bool __result) { Tests.Attacks.Add(targ.Thing); __result = true; return false; }
    private static void RemoveHostility(ref bool __result) => __result = false;
    private sealed class TestVerb : Verb
    {
        protected override bool TryCastShot() => true;
    }
}
