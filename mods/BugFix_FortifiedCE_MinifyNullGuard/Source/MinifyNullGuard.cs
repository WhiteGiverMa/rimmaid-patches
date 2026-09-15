using HarmonyLib;
using RimWorld;
using Verse;

namespace FortifiedCE_MinifyNullGuard
{
    [StaticConstructorOnStartup]
    public static class MinifyNullGuard
    {
        static MinifyNullGuard()
        {
            var harmony = new Harmony("com.local.fortifiedce.minifynullguard");
            harmony.Patch(
                original: AccessTools.Method(typeof(MinifyUtility), "MakeMinified", new[] { typeof(Thing), typeof(DestroyMode) }),
                prefix: new HarmonyMethod(typeof(MinifyNullGuard), "Prefix")
            );
        }

        /// <summary>
        /// Historical implementation: skip only the original for non-minifiable things.
        /// Harmony postfixes still run; this does NOT guard FortifiedCE's null postfix.
        /// </summary>
        public static bool Prefix(Thing thing, ref MinifiedThing __result)
        {
            if (thing != null && !thing.def.Minifiable)
            {
                __result = null;
                return false; // skip original only; postfixes still run
            }
            return true;
        }
    }
}
