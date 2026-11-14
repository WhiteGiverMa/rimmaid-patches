using System.Diagnostics;
using Verse;
using Verse.AI;

namespace Validation;

internal static class WorkloadTests
{
    internal static void Run(bool green)
    {
        Fixture f = new(325);
        List<JobDriver_Wait> waiting = [];
        for (int i = 0; i < 82; i++) waiting.Add(f.Wait(f.Pawn(new IntVec3(10 + i % 3, 0, 10 + i / 28))));
        for (int i = 0; i < 251; i++) waiting.Add(f.Wait(f.Pawn(new IntVec3(30 + i % 20, 0, 30 + i / 20), true)));
        foreach (bool fire in new[] { false, true })
        {
            if (fire) f.Fire(new IntVec3(250, 0, 250));
            f.Tick();
            foreach (var d in waiting) Tests.Wait(d);
            Tests.Scans = 0;
            Stopwatch sw = Stopwatch.StartNew();
            const int rounds = 20;
            for (int i = 0; i < rounds; i++)
            {
                f.Tick(); foreach (var d in waiting) Tests.Wait(d);
            }
            sw.Stop();
            Tests.Check(Tests.Scans == (green ? 0 : rounds * 333 * 9), $"325x325/333 pawns/82 chickens/{(fire ? "localized fire" : "peaceful")}: {Tests.Scans} GetThingList calls for {rounds * 333} waits");
            Console.WriteLine($"BENCH {(green ? "GREEN" : "RED")} fire={fire}: {sw.Elapsed.TotalMilliseconds:F3} ms / {rounds * 333} calls (offline fixture, not game frame time)");
        }
    }
}
