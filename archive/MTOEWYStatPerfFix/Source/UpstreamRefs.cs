using System;
using System.Linq;
using System.Reflection;
using MiliraTheOneEntwinedWithYou.Data;
using MiliraTheOneEntwinedWithYou.GameComponents;
using MiliraTheOneEntwinedWithYou.Rewards;
using Verse;

namespace WhiteGiver.MTOEWYStatPerfFix;

/// <summary>
/// Resolves the handful of MTOEWY internals whose exact behaviour we reuse instead of
/// re-implementing (world-effect movement factor, burst window, reward lookup, original
/// recovery transaction flag). They are bound once to cached delegates, so the steady
/// state never pays reflection costs.
/// </summary>
internal static class UpstreamRefs
{
    internal const string UpstreamAssemblyName = "MiliraTheOneEntwinedWithYou";
    internal const string MoveFactorType = "MiliraTheOneEntwinedWithYou.Abilities.IdealWorldEffectService";
    internal const string KitRuntimeType = "MiliraTheOneEntwinedWithYou.Rewards.IdealKitRuntime";
    internal const string TrackerType = "MiliraTheOneEntwinedWithYou.GameComponents.GameComponent_IdealTracker";
    internal const string TraitStateType = "MiliraTheOneEntwinedWithYou.Data.IdealExclusiveTraitState";

    internal static Func<Pawn, float>? MoveFactor { get; private set; }
    internal static Func<string, IdealRewardRecord?>? ActiveReward { get; private set; }
    internal static Func<GameComponent_IdealTracker, bool>? HasOriginalRecoveryTransaction { get; private set; }
    internal static Func<IdealExclusiveTraitState, int, bool>? BurstActive { get; private set; }

    internal static Assembly? ResolvedAssembly { get; private set; }

    internal static bool Resolve()
    {
        if (MoveFactor != null && ActiveReward != null && HasOriginalRecoveryTransaction != null && BurstActive != null)
        {
            return true;
        }

        Assembly? assembly = FindAssembly();
        if (assembly == null)
        {
            Log.Warning("[MTOEWYStatPerfFix] MiliraTheOneEntwinedWithYou.dll is not loaded; the optimization stays disabled.");
            return false;
        }

        try
        {
            MethodInfo? moveFactor = FindMethod(assembly, "IdealWorldEffectService", "MoveFactor", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            MethodInfo? active = FindMethod(assembly, "IdealKitRuntime", "Active", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            MethodInfo? burst = FindMethod(assembly, "IdealExclusiveTraitState", "BurstActive", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            PropertyInfo? recovery = FindType(assembly, "GameComponent_IdealTracker")?
                .GetProperty("HasOriginalRecoveryTransaction", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            if (moveFactor == null || active == null || burst == null || recovery?.GetMethod == null)
            {
                Log.Warning("[MTOEWYStatPerfFix] Upstream method layout changed; the optimization stays disabled.");
                return false;
            }

            Func<Pawn, float>? moveFactorDelegate = moveFactor.CreateDelegate(typeof(Func<Pawn, float>)) as Func<Pawn, float>;
            Func<string, IdealRewardRecord?>? activeDelegate = active.CreateDelegate(typeof(Func<string, IdealRewardRecord?>)) as Func<string, IdealRewardRecord?>;
            Func<GameComponent_IdealTracker, bool>? recoveryDelegate = recovery.GetMethod.CreateDelegate(typeof(Func<GameComponent_IdealTracker, bool>)) as Func<GameComponent_IdealTracker, bool>;
            Func<IdealExclusiveTraitState, int, bool>? burstDelegate = burst.CreateDelegate(typeof(Func<IdealExclusiveTraitState, int, bool>)) as Func<IdealExclusiveTraitState, int, bool>;

            if (moveFactorDelegate == null || activeDelegate == null || recoveryDelegate == null || burstDelegate == null)
            {
                Log.Warning("[MTOEWYStatPerfFix] Upstream delegates could not be bound; the optimization stays disabled.");
                return false;
            }

            MoveFactor = moveFactorDelegate;
            ActiveReward = activeDelegate;
            HasOriginalRecoveryTransaction = recoveryDelegate;
            BurstActive = burstDelegate;
            ResolvedAssembly = assembly;
            return true;
        }
        catch (Exception exception)
        {
            Log.Warning("[MTOEWYStatPerfFix] Upstream reflection failed; the optimization stays disabled: " + exception);
            return false;
        }
    }

    private static Assembly? FindAssembly()
    {
        Assembly? loaded = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == UpstreamAssemblyName);
        if (loaded != null)
        {
            return loaded;
        }

        try
        {
            return Assembly.Load(new AssemblyName(UpstreamAssemblyName));
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static Type? FindType(Assembly assembly, string simpleName)
    {
        try
        {
            return assembly.GetTypes().FirstOrDefault(t => t.Name == simpleName);
        }
        catch (ReflectionTypeLoadException exception)
        {
            return exception.Types.Where(t => t != null).FirstOrDefault(t => t!.Name == simpleName);
        }
    }

    private static MethodInfo? FindMethod(Assembly assembly, string typeName, string methodName, BindingFlags flags)
    {
        Type? type = FindType(assembly, typeName);
        return type?.GetMethod(methodName, flags);
    }
}
