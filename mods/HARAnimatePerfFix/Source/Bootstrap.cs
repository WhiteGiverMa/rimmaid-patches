using System;
using System.Reflection;
using HarmonyLib;
using HARAnimate.Animation;
using Verse;

namespace WhiteGiver.HARAnimatePerfFix
{
    /// <summary>
    /// Installs the Harmony patches exactly once.
    ///
    /// Entry points:
    ///  * the <see cref="Mod" /> constructor — Mod constructors run while the mod list is being loaded,
    ///    before any <c>[StaticConstructorOnStartup]</c> type is touched, so patching here guarantees the
    ///    <c>AnimatorSystem.Register</c> prefix is in place before HARAnimate's own startup registration
    ///    pass, and
    ///  * <c>[StaticConstructorOnStartup]</c> — runs after all defs are loaded, then performs the scan of
    ///    <c>AnimatorSystem.stacks</c> that catches registrations which happened before us.
    ///
    /// Both paths are idempotent.
    /// </summary>
    public static class Bootstrap
    {
        internal const string HarmonyId = "whitegiver.haranimate.perffix";

        private static readonly object Gate = new();

        private static bool applied;

        public static void Apply()
        {
            lock (Gate)
            {
                if (applied)
                {
                    return;
                }

                applied = true;

                try
                {
                    Harmony harmony = new Harmony(HarmonyId);

                    Patch(
                        harmony,
                        typeof(AnimatorCache),
                        "Get",
                        new[] { typeof(Pawn), typeof(PawnDrawParms), typeof(int), typeof(string), typeof(int) },
                        typeof(AnimatorCachePatches),
                        nameof(AnimatorCachePatches.GetPrefix),
                        null);

                    Patch(
                        harmony,
                        typeof(AnimatorCache),
                        "Cleanup",
                        new[] { typeof(int), typeof(int) },
                        typeof(AnimatorCachePatches),
                        nameof(AnimatorCachePatches.CleanupPrefix),
                        null);

                    Patch(
                        harmony,
                        typeof(AnimatorCache),
                        "CleanAll",
                        Type.EmptyTypes,
                        typeof(AnimatorCachePatches),
                        nameof(AnimatorCachePatches.CleanAllPrefix),
                        null);

                    Patch(
                        harmony,
                        typeof(AnimatorCache),
                        "CacheCount",
                        Type.EmptyTypes,
                        typeof(AnimatorCachePatches),
                        nameof(AnimatorCachePatches.CacheCountPrefix),
                        null);

                    Patch(
                        harmony,
                        typeof(AnimatorCacheCleaner),
                        nameof(AnimatorCacheCleaner.CacheCleaner),
                        Type.EmptyTypes,
                        typeof(AnimatorCacheCleanerPatches),
                        nameof(AnimatorCacheCleanerPatches.CacheCleanerPrefix),
                        null);

                    Patch(
                        harmony,
                        typeof(AnimatorSystem),
                        nameof(AnimatorSystem.Register),
                        new[] { typeof(string), typeof(AnimatorStack) },
                        typeof(AnimatorSystemPatches),
                        nameof(AnimatorSystemPatches.RegisterPrefix),
                        nameof(AnimatorSystemPatches.RegisterPostfix));

                    Patch(
                        harmony,
                        typeof(AnimatorSystem),
                        nameof(AnimatorSystem.Evaluate),
                        new[] { typeof(Pawn), typeof(PawnDrawParms), typeof(string), typeof(int) },
                        typeof(AnimatorSystemPatches),
                        nameof(AnimatorSystemPatches.EvaluatePrefix),
                        null);

                    // Registrations that happened before the patches above (i.e. before this mod's Mod
                    // constructor ran) are already sitting in the private dictionary: mirror them now.
                    FastAnimatorSystem.Sync();
                }
                catch (Exception exception)
                {
                    Log.Error("[HARAnimatePerfFix] Failed to install patches; HARAnimate keeps its original behaviour: "
                              + exception);
                }
            }
        }

        private static void Patch(
            Harmony harmony,
            Type targetType,
            string targetName,
            Type[] parameters,
            Type patchType,
            string? prefixName,
            string? postfixName)
        {
            MethodInfo? target = AccessTools.Method(targetType, targetName, parameters);

            if (target is null)
            {
                Log.Warning(
                    $"[HARAnimatePerfFix] {targetType.FullName}.{targetName} was not found; that patch is skipped.");
                return;
            }

            try
            {
                harmony.Patch(
                    target,
                    prefixName is null ? null : new HarmonyMethod(patchType, prefixName),
                    postfixName is null ? null : new HarmonyMethod(patchType, postfixName));
            }
            catch (Exception exception)
            {
                Log.Error(
                    $"[HARAnimatePerfFix] Patching {targetType.FullName}.{targetName} failed; upstream behaviour kept: "
                    + exception);
            }
        }
    }

    /// <summary>
    /// Mod constructor runs before every <c>[StaticConstructorOnStartup]</c>, which is the earliest point
    /// a mod DLL can patch other mods' assemblies.
    /// </summary>
    public sealed class HARAnimatePerfFixMod : Mod
    {
        public HARAnimatePerfFixMod(ModContentPack content) : base(content)
        {
            Bootstrap.Apply();
        }
    }

    /// <summary>
    /// Runs after all defs (and therefore after HARAnimate's own startup registration pass).
    /// Re-applying is a no-op; the sync picks up anything HARAnimate registered before our Mod
    /// constructor ran.
    /// </summary>
    [StaticConstructorOnStartup]
    internal static class BootstrapStartup
    {
        static BootstrapStartup()
        {
            Bootstrap.Apply();
            FastAnimatorSystem.Sync();
        }
    }
}
