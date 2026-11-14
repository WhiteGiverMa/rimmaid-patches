using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using UnburnedDNAFPerfFix;
using Verse;

namespace Validation;

internal static class CacheTests
{
    internal static void Run()
    {
        var f = new Fixture(10);
        Pawn p = f.Pawn(new IntVec3(5, 0, 5), true);
        ThingDef armor = new() { defName = "Armor" }, helmet = new() { defName = "Helmet" };
        Type settings = Program.Unburned.GetType("Unburned.Settings")!;
        settings.GetField("powerArmorsOptimized")!.SetValue(null, new List<ThingDef> { armor });
        settings.GetField("powerHelmetsOptimized")!.SetValue(null, new List<ThingDef> { helmet });
        p.apparel.WornApparel.Add(new Apparel { def = armor });
        p.apparel.WornApparel.Add(new Apparel { def = helmet });
        Tests.Check((bool)Tests.Sealed.Invoke(null, [p])!, "configured armor+helmet seal");
        Parallel.For(0, 10000, _ =>
        {
            if (!(bool)Tests.Sealed.Invoke(null, [p])!) throw new Exception("concurrent sealed read failed");
        });
        Tests.Check(true, "10000 parallel sealed reads");
        p.apparel.WornApparel.RemoveAt(1);
        p.apparel.Notify_ApparelChanged();
        Tests.Check(!(bool)Tests.Sealed.Invoke(null, [p])!, "apparel change invalidates within tick");
        p.apparel.WornApparel.Add(new Apparel { def = helmet });
        Fixture.Set(Find.TickManager, "ticksGameInt", Find.TickManager.TicksGame + 1000);
        Tests.Check((bool)Tests.Sealed.Invoke(null, [p])!, "900-tick expiry refreshes");
        Tests.Check(!(bool)Tests.Sealed.Invoke(null, [null])!, "null pawn O(1) false");
        WeakReference weak = MakeWeak(f, armor, helmet);
        for (int i = 0; i < 3; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); }
        Tests.Check(!weak.IsAlive, "sealed cache does not retain discarded pawn");
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference MakeWeak(Fixture f, ThingDef armor, ThingDef helmet)
    {
        Pawn p = f.Pawn(new IntVec3(2, 0, 2), true);
        p.apparel.WornApparel.Add(new Apparel { def = armor }); p.apparel.WornApparel.Add(new Apparel { def = helmet });
        _ = Tests.Sealed.Invoke(null, [p]);
        f.Map.thingGrid.Deregister(p); ((List<Pawn>)f.Map.mapPawns.AllPawnsSpawned).Remove(p);
        return new WeakReference(p);
    }
}
