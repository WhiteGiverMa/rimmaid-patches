using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using CombatExtended;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace CEOverpenetration;

public static class OverpenetrationBridge
{
    private const int MaxTargets = 16;
    private const float MinimumPenetration = 0.1f;
    private const float MinimumExitSpeedRetention = 0.20f;

    private static readonly ConditionalWeakTable<ProjectileCE, OverpenState> States = new();
    private static readonly FieldInfo DamageAmountField = AccessTools.Field(typeof(ProjectileCE), "damageAmount");
    private static readonly FieldInfo TrajectoryWorkerField = AccessTools.Field(typeof(ProjectileCE), "forcedTrajectoryWorker");

    [ThreadStatic]
    private static Stack<ImpactContext> impactContexts;

    private sealed class ImpactContext
    {
        public readonly BulletCE Bullet;
        public readonly Thing Target;
        public int ArmorDepth;
        public bool PrimaryResultCaptured;

        public ImpactContext(BulletCE bullet, Thing target)
        {
            Bullet = bullet;
            Target = target;
        }
    }

    public static OverpenState GetOrCreateState(ProjectileCE projectile)
        => States.GetOrCreateValue(projectile);

    public static OverpenState GetState(ProjectileCE projectile)
        => States.TryGetValue(projectile, out var state) ? state : null;

    public static void BeginImpact(BulletCE bullet, Thing target)
    {
        impactContexts ??= new Stack<ImpactContext>();
        impactContexts.Push(new ImpactContext(bullet, target));
    }

    public static void EndImpact(BulletCE bullet)
    {
        if (impactContexts == null || impactContexts.Count == 0) return;
        if (ReferenceEquals(impactContexts.Peek().Bullet, bullet))
            impactContexts.Pop();
    }

    public static void BeginArmorCalculation()
    {
        if (TryGetImpactContext(out var context))
            context.ArmorDepth++;
    }

    public static void EndArmorCalculation(
        DamageInfo originalDinfo,
        DamageInfo resultDinfo,
        bool armorDeflected,
        bool shieldAbsorbed,
        Pawn pawn)
    {
        if (!TryGetImpactContext(out var context)) return;

        try
        {
            if (context.ArmorDepth != 1 || context.PrimaryResultCaptured) return;
            context.PrimaryResultCaptured = true;

            try
            {
                TryContinueProjectile(context.Bullet, context.Target, pawn, originalDinfo, resultDinfo, armorDeflected, shieldAbsorbed);
            }
            catch (Exception exception)
            {
                Log.Error($"[CE Overpenetration] Continuation failed; preserving the normal CE impact.\n{exception}");
            }
        }
        finally
        {
            context.ArmorDepth = Math.Max(0, context.ArmorDepth - 1);
        }
    }

    private static bool TryGetImpactContext(out ImpactContext context)
    {
        if (impactContexts != null && impactContexts.Count > 0)
        {
            context = impactContexts.Peek();
            return true;
        }
        context = null;
        return false;
    }

    // Terminal payloads consume or split the projectile at its first valid impact.
    private static bool HasTerminalPayload(ProjectileCE projectile)
    {
        return projectile.def.projectile.explosionRadius > 0f
            || projectile.TryGetComp<CompExplosiveCE>() != null
            || projectile.GetComps<CompFragments>().Any();
    }

    /// <summary>
    /// CE's default <see cref="LerpedTrajectoryWorker"/> precomputes origin, destination and flight
    /// time at launch and reports <see cref="ProjectileCE.RemainingSpeedPct"/> as 1, so speed lost to
    /// penetration cannot reduce its damage or penetration. Switching the projectile onto CE's
    /// ballistic worker at the moment of overpenetration hands it back to CE's native model:
    /// RemainingSpeedPct/KineticEnergyPct drive damage and penetration again, and CE's own ballistic
    /// motion carries it forward until it reaches the ground or drops below the destruction threshold.
    /// Returns false when the projectile cannot adopt the ballistic model; the caller must then keep
    /// the normal CE impact instead of continuing.
    /// </summary>
    public static bool TryAdoptBallisticContinuation(ProjectileCE projectile)
    {
        // RemainingSpeedPct divides by initialSpeed; without a launch speed the retained ratio cannot
        // be expressed, so such a projectile must not be switched onto the ballistic model.
        if (projectile.initialSpeed <= 0f) return false;

        var worker = projectile.TrajectoryWorker;
        if (worker is BallisticsTrajectoryWorker) return true;
        if (worker is not LerpedTrajectoryWorker) return false;

        if (TrajectoryWorkerField == null)
        {
            Log.ErrorOnce("[CE Overpenetration] Combat Extended's trajectory worker field was not found; Lerped projectiles will not continue through targets.", 1948376252);
            return false;
        }

        TrajectoryWorkerField.SetValue(projectile, ProjectilePropertiesCE.defaultBallisticTrajectoryWorker);
        // Keep the deprecated mirror in sync so CE's Tick() consistency check stays silent.
        projectile.lerpPosition = false;
        projectile.cachedPredictedPositions = null;
        return true;
    }

    private static void TryContinueProjectile(
        BulletCE bullet,
        Thing impactTarget,
        Pawn pawn,
        DamageInfo originalDinfo,
        DamageInfo resultDinfo,
        bool armorDeflected,
        bool shieldAbsorbed)
    {
        if (!ReferenceEquals(impactTarget, pawn)) return;
        if (bullet.TrajectoryWorker is not BallisticsTrajectoryWorker and not LerpedTrajectoryWorker) return;
        if (bullet.def.projectile.flyOverhead) return;
        if (HasTerminalPayload(bullet)) return;
        if (originalDinfo.Def?.armorCategory != DamageArmorCategoryDefOf.Sharp) return;
        if (resultDinfo.Def?.armorCategory != DamageArmorCategoryDefOf.Sharp) return;
        if (armorDeflected || shieldAbsorbed) return;
        if (originalDinfo.Amount <= 0f || resultDinfo.Amount <= 0f) return;

        var state = GetOrCreateState(bullet);
        if (state.overpenCount >= MaxTargets || state.alreadyHitThingIds.Contains(pawn.thingIDNumber)) return;

        if (DamageAmountField == null)
        {
            Log.ErrorOnce("[CE Overpenetration] Combat Extended's damageAmount field was not found; continuation is disabled for compatibility.", 1948376251);
            return;
        }

        float currentPenetration = bullet.PenetrationAmount;
        if (currentPenetration <= MinimumPenetration) return;

        float transmittedDamageRatio = Mathf.Clamp01(resultDinfo.Amount / originalDinfo.Amount);
        float postArmorPenetration = currentPenetration * transmittedDamageRatio;
        // ponytail: CE exposes tissue density but not direction-resolved pawn thickness; cube-root BodySize estimates traversal length.
        float bodyTraversalResistance = pawn.GetStatValue(CE_StatDefOf.BodyPartSharpArmor)
            * Mathf.Pow(pawn.BodySize, 1f / 3f);
        float exitPenetration = postArmorPenetration - bodyTraversalResistance;
        float speedRetention = exitPenetration / currentPenetration;

        if (exitPenetration <= MinimumPenetration || speedRetention < MinimumExitSpeedRetention) return;

        // The retained speed only means something on CE's ballistic model; a Lerped projectile that
        // cannot adopt it keeps the normal impact instead of continuing at undecayed damage.
        if (!TryAdoptBallisticContinuation(bullet)) return;

        bullet.velocity *= speedRetention;
        bullet.shotSpeed = bullet.velocity.magnitude * GenTicks.TicksPerRealSecond;
        bullet.cachedPredictedPositions = null;
        DamageAmountField.SetValue(bullet, null);

        state.alreadyHitThingIds.Add(pawn.thingIDNumber);
        state.overpenCount++;
        state.continuationActive = true;
        state.skipBaseImpact = true;
        bullet.landed = false;

        if (CEOverpenetrationMod.LogContinuationsEnabled)
        {
            Log.Message($"[CE Overpenetration] {bullet.def.defName} continued through {pawn.LabelShortCap}; "
                + $"retained speed {speedRetention:P0}, penetration {bullet.PenetrationAmount:F1}, chain {state.overpenCount}.");
        }

        if (Controller.settings.DebugDrawInterceptChecks)
        {
            MoteMakerCE.ThrowText(pawn.Position.ToVector3Shifted(), bullet.Map, ">>", Color.cyan);
        }
    }

    public static bool ConsumeSkipBaseImpact(ProjectileCE projectile)
    {
        var state = GetState(projectile);
        if (state == null || !state.skipBaseImpact) return false;
        state.skipBaseImpact = false;
        return true;
    }

    public static bool WasAlreadyHit(ProjectileCE projectile, Thing thing)
    {
        var state = GetState(projectile);
        return state != null && state.alreadyHitThingIds.Contains(thing.thingIDNumber);
    }

    public static void MaintainCollisionHorizon(ProjectileCE projectile)
    {
        if (projectile.ticksToImpact >= 1) return;
        var state = GetState(projectile);
        if (state?.continuationActive == true)
            projectile.ticksToImpact = 1;
    }

    public static void ExposeData(ProjectileCE projectile)
    {
        var state = GetOrCreateState(projectile);
        Scribe_Values.Look(ref state.overpenCount, "ceOverpenetrationCount", 0);
        Scribe_Values.Look(ref state.continuationActive, "ceOverpenetrationActive", false);

        List<int> hitIds = Scribe.mode == LoadSaveMode.Saving
            ? state.alreadyHitThingIds.ToList()
            : null;
        Scribe_Collections.Look(ref hitIds, "ceOverpenetrationHitIds", LookMode.Value);

        if (Scribe.mode == LoadSaveMode.LoadingVars)
        {
            state.alreadyHitThingIds.Clear();
            if (hitIds != null)
            {
                foreach (int id in hitIds)
                    state.alreadyHitThingIds.Add(id);
            }
        }

        if (Scribe.mode == LoadSaveMode.PostLoadInit && state.continuationActive
            && projectile.TrajectoryWorker is LerpedTrajectoryWorker)
        {
            // A loaded projectile derives its worker from the def again, which restores Lerped for
            // default CE ammo; re-adopt the ballistic model so serialized in-flight shots keep
            // their speed-driven damage, penetration and continued flight.
            TryAdoptBallisticContinuation(projectile);
        }
    }
}
