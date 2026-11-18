using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace WhiteGiver.MTOEWYStatPerfFix;

/// <summary>
/// Installs the replacement postfix and removes MTOEWY's own postfix from
/// <see cref="StatExtension.GetStatValue(Thing, StatDef, bool, int)"/>.
/// <para>
/// Install order is chosen so that a failure can never leave the game without the
/// upstream bonuses: our postfix is added first, upstream's is removed second, and
/// both operations are verified on the live Harmony patch table. If verification
/// fails, the partial change is rolled back and the upstream patch is left intact.
/// </para>
/// </summary>
public static class Bootstrap
{
    public const string HarmonyId = "whitegiver.mtoewy.statperffix";

    internal const string UpstreamHarmonyId = "FunnyFlandre.MiliraTheOneEntwinedWithYou";
    internal const string UpstreamPatchType = "MiliraTheOneEntwinedWithYou.Patches.IdealExclusiveStatPatch";
    internal const string UpstreamPostfixName = "Postfix";

    private static readonly Harmony Harmony = new(HarmonyId);
    private static readonly Version ValidatedUpstreamVersion = new(1, 4, 0, 0);

    private static MethodInfo? original;
    private static MethodInfo? ourPostfix;
    private static bool installed;

    public static bool Installed => installed;

    public static void Install()
    {
        if (installed)
        {
            return;
        }

        if (!UpstreamRefs.Resolve())
        {
            return;
        }

        original ??= AccessTools.Method(
            typeof(StatExtension),
            nameof(StatExtension.GetStatValue),
            new[] { typeof(Thing), typeof(StatDef), typeof(bool), typeof(int) });
        if (original == null)
        {
            Log.Warning("[MTOEWYStatPerfFix] StatExtension.GetStatValue was not found; the optimization stays disabled.");
            return;
        }

        Version? upstreamVersion = UpstreamRefs.ResolvedAssembly?.GetName().Version;
        if (upstreamVersion != null && upstreamVersion != ValidatedUpstreamVersion)
        {
            Log.Warning("[MTOEWYStatPerfFix] This build was validated against MiliraTheOneEntwinedWithYou " + ValidatedUpstreamVersion +
                        "; the loaded assembly is " + upstreamVersion + ". Re-check the 14-stat list and the trait/equipment multipliers after an upstream update.");
        }

        ourPostfix ??= AccessTools.Method(typeof(FastStatPostfix), nameof(FastStatPostfix.Postfix));
        if (ourPostfix == null)
        {
            Log.Warning("[MTOEWYStatPerfFix] Replacement postfix was not found; the optimization stays disabled.");
            return;
        }

        MethodInfo? upstream = FindUpstreamPostfix();
        if (upstream == null)
        {
            // Do not resurrect a patch the installed upstream version no longer applies.
            Log.Warning("[MTOEWYStatPerfFix] MTOEWY's IdealExclusiveStatPatch.Postfix is not attached to StatExtension.GetStatValue; the optimization stays disabled.");
            return;
        }

        InstallGameChangeHook();
        WarnAboutThirdPartyPostfixes();

        bool addedOurs = false;
        try
        {
            if (!HasOurPostfix())
            {
                Harmony.Patch(original, postfix: new HarmonyMethod(ourPostfix));
                addedOurs = true;
            }

            Harmony.Unpatch(original, upstream);

            if (!HasOurPostfix())
            {
                throw new InvalidOperationException("replacement postfix is not attached after patching");
            }

            MethodInfo? remainingUpstream = FindUpstreamPostfix();
            if (remainingUpstream != null)
            {
                throw new InvalidOperationException("upstream postfix is still attached after unpatching");
            }

            installed = true;
            Log.Message("[MTOEWYStatPerfFix] MTOEWY stat postfix replaced with the allocation-free dispatcher.");
        }
        catch (Exception exception)
        {
            if (addedOurs)
            {
                TryRemoveOurPostfix();
            }

            Log.Error("[MTOEWYStatPerfFix] Could not install the stat optimization; upstream behaviour was left untouched: " + exception);
        }
    }

    /// <summary>
    /// Runs after every mod has loaded. MTOEWY patches in its Mod constructor, so this is
    /// only a safety net for unusual load orders that put MTOEWY after this mod.
    /// </summary>
    public static void RepairIfNeeded()
    {
        if (!installed || original == null)
        {
            Install();
            return;
        }

        MethodInfo? upstream = FindUpstreamPostfix();
        if (upstream == null)
        {
            return;
        }

        try
        {
            Harmony.Unpatch(original, upstream);
            if (FindUpstreamPostfix() != null)
            {
                Log.Error("[MTOEWYStatPerfFix] A second MTOEWY stat postfix reappeared and could not be removed; stat bonuses may be applied twice. Please report this load order.");
                TryRemoveOurPostfix();
                installed = false;
            }
        }
        catch (Exception exception)
        {
            Log.Error("[MTOEWYStatPerfFix] Failed to re-remove MTOEWY's stat postfix; disabling the optimization to avoid double application: " + exception);
            TryRemoveOurPostfix();
            installed = false;
        }
    }

    /// <summary>
    /// Releases the cached Game graph when RimWorld swaps Current.Game (load, new colony,
    /// return to the main menu) so the static cache cannot root an old session.
    /// </summary>
    private static void InstallGameChangeHook()
    {
        try
        {
            MethodInfo? setter = AccessTools.PropertySetter(typeof(Current), nameof(Current.Game));
            if (setter == null)
            {
                return;
            }

            Harmony.Patch(setter, prefix: new HarmonyMethod(AccessTools.Method(typeof(Bootstrap), nameof(OnGameChanging))));
        }
        catch (Exception exception)
        {
            Log.Warning("[MTOEWYStatPerfFix] Could not hook Current.Game changes; the tracker cache will refresh on the next stat query instead: " + exception.Message);
        }
    }

    internal static void OnGameChanging(Game value)
    {
        if (!ReferenceEquals(Current.Game, value))
        {
            TrackerCache.Reset();
        }
    }

    /// <summary>
    /// Harmony runs same-priority postfixes in patch order. Our replacement is attached
    /// after MTOEWY's original position, so warn when another mod postfixes the same
    /// method and the composition order could differ.
    /// </summary>
    private static void WarnAboutThirdPartyPostfixes()
    {
        Patches? patches = original == null ? null : Harmony.GetPatchInfo(original);
        if (patches == null)
        {
            return;
        }

        for (int i = 0; i < patches.Postfixes.Count; i++)
        {
            Patch patch = patches.Postfixes[i];
            if (patch.owner != UpstreamHarmonyId && patch.owner != HarmonyId)
            {
                Log.Warning("[MTOEWYStatPerfFix] Another mod postfixes StatExtension.GetStatValue (" + patch.owner + ":" +
                            patch.PatchMethod.Name + "). MTOEWY's bonuses are still applied once, but their composition order may differ from the original patch position.");
            }
        }
    }

    private static void TryRemoveOurPostfix()
    {
        if (original == null || ourPostfix == null)
        {
            return;
        }

        try
        {
            Harmony.Unpatch(original, ourPostfix);
        }
        catch (Exception exception)
        {
            Log.Error("[MTOEWYStatPerfFix] Failed to roll back the replacement postfix: " + exception);
        }
    }

    private static bool HasOurPostfix()
    {
        Patches? patches = original == null ? null : Harmony.GetPatchInfo(original);
        return patches != null && patches.Postfixes.Any(patch => patch.owner == HarmonyId);
    }

    private static MethodInfo? FindUpstreamPostfix()
    {
        Patches? patches = original == null ? null : Harmony.GetPatchInfo(original);
        if (patches == null)
        {
            return null;
        }

        for (int i = 0; i < patches.Postfixes.Count; i++)
        {
            Patch patch = patches.Postfixes[i];
            MethodInfo method = patch.PatchMethod;
            if (method.Name == UpstreamPostfixName && method.DeclaringType?.FullName == UpstreamPatchType)
            {
                return method;
            }
        }

        return null;
    }
}
