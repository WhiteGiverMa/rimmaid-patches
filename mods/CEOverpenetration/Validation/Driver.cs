using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml;
using CEOverpenetration;
using CombatExtended;
using UnityEngine;
using Verse;

public static class OverpenetrationSettingsValidation
{
    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception("FAIL: " + description);
        Console.WriteLine("PASS: " + description);
    }

    public static void Run()
    {
        string path = Path.Combine(Path.GetTempPath(), "CEOverpenetration-" + Guid.NewGuid() + ".xml");
        var property = typeof(CEOverpenetrationMod).GetProperty("Settings", BindingFlags.Static | BindingFlags.NonPublic);
        var enabled = typeof(CEOverpenetrationMod).GetProperty("LogContinuationsEnabled", BindingFlags.Static | BindingFlags.NonPublic);
        using (Log.LockMessages())
        {
            DeepProfiler.enabled = false;
            try
            {
                Check(!(bool)enabled.GetValue(null), "logging defaults off before settings load");
                var settings = new CEOverpenetrationSettings();
                property.SetValue(null, settings);
                Check(!(bool)enabled.GetValue(null), "logging defaults off after settings load");
                settings.LogContinuations = true;
                Check((bool)enabled.GetValue(null), "enabling takes effect immediately");
                settings.LogContinuations = false;
                Check(!(bool)enabled.GetValue(null), "disabling takes effect immediately");
                settings.LogContinuations = true;
                Scribe.saver.InitSaving(path, "Settings");
                settings.ExposeData();
                Scribe.saver.FinalizeSaving();
                var xml = new XmlDocument();
                xml.Load(path);
                Check(xml.DocumentElement["logContinuations"].InnerText == "True", "enabled choice is persisted");
                var loaded = new CEOverpenetrationSettings();
                Scribe.mode = LoadSaveMode.LoadingVars;
                Scribe.loader.curXmlParent = xml.DocumentElement;
                loaded.ExposeData();
                property.SetValue(null, loaded);
                Check((bool)enabled.GetValue(null), "saved choice loads as enabled");
                xml.LoadXml("<Settings />");
                Scribe.loader.curXmlParent = xml.DocumentElement;
                loaded.ExposeData();
                Check(!(bool)enabled.GetValue(null), "missing choice loads as off");
            }
            finally
            {
                Scribe.mode = LoadSaveMode.Inactive;
                property.SetValue(null, null);
                if (File.Exists(path)) File.Delete(path);
            }
        }
    }
}

/// <summary>
/// Exercises the overpenetration trajectory repair against the real Combat Extended assembly.
/// The captured <c>-PatchDll</c> must implement <c>OverpenetrationBridge.TryAdoptBallisticContinuation</c>;
/// a pre-fix assembly fails here because a Lerped projectile cannot express retained-speed decay.
/// </summary>
public static class OverpenetrationContinuationValidation
{
    private const float LaunchSpeed = 40f;
    private const float RetainedSpeed = 0.5f;
    private const int MaxSimulatedTicks = 600;

    private static readonly PropertyInfo ShouldCollideWithSomething =
        typeof(ProjectileCE).GetProperty("ShouldCollideWithSomething", BindingFlags.Instance | BindingFlags.NonPublic);

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception("FAIL: " + description);
        Console.WriteLine("PASS: " + description);
    }

    private static BulletCE MakeBullet(Type trajectoryWorker = null)
    {
        var props = new ProjectilePropertiesCE
        {
            speed = LaunchSpeed,
            damageDef = new DamageDef { defaultDamage = 10 },
            armorPenetrationSharp = 5f,
            armorPenetrationBlunt = 4f,
            trajectoryWorker = trajectoryWorker,
        };
        // ThingDef's constructor loads Unity shaders through BaseContent, which does not exist in a
        // standalone driver; an uninitialized instance still carries def.projectile for the
        // trajectory assertions.
        var def = (ThingDef)RuntimeHelpers.GetUninitializedObject(typeof(ThingDef));
        def.projectile = props;
        return new BulletCE
        {
            def = def,
            initialSpeed = LaunchSpeed,
            shotSpeed = LaunchSpeed,
            velocity = new Vector3(LaunchSpeed / GenTicks.TicksPerRealSecond, 0f, 0f),
            mass = 0.01f,
            radius = 0.005f,
            ballisticCoefficient = 0.3f,
            GravityPerHeight = 1.2f,
            GravityPerWidth = 1.2f,
            ExactPosition = new Vector3(0f, 0.85f, 0f),
            ticksToImpact = 1,
        };
    }

    private static void ApplyRetainedSpeed(BulletCE bullet, float retention)
    {
        bullet.velocity *= retention;
        bullet.shotSpeed = bullet.velocity.magnitude * GenTicks.TicksPerRealSecond;
    }

    public static void Run()
    {
        // ---- CE default (Lerped) ammo: the defect a pre-fix assembly leaves in place ----
        var lerped = MakeBullet();
        Check(lerped.TrajectoryWorker is LerpedTrajectoryWorker,
            "CE default ammo resolves to the Lerped trajectory worker");
        ApplyRetainedSpeed(lerped, RetainedSpeed);
        Check(Mathf.Approximately(lerped.RemainingSpeedPct, 1f),
            "pre-fix: Lerped projectile keeps RemainingSpeedPct at 1 after losing half its speed");
        Check(Mathf.Approximately(lerped.DamageAmount, 10f),
            "pre-fix: Lerped projectile keeps full damage after losing half its speed");
        lerped.ticksToImpact = 0;
        Check((bool)ShouldCollideWithSomething.GetValue(lerped),
            "pre-fix: Lerped projectile at ticksToImpact<=0 forces an impact instead of extending flight");

        // ---- Post-fix: Lerped projectiles adopt CE's ballistic model ----
        var adopt = typeof(OverpenetrationBridge).GetMethod(
            "TryAdoptBallisticContinuation", BindingFlags.Public | BindingFlags.Static);
        Check(adopt != null,
            "patch DLL implements the ballistic-continuation repair for Lerped projectiles");

        var continued = MakeBullet();
        ApplyRetainedSpeed(continued, RetainedSpeed);
        continued.ticksToImpact = 0;
        Check((bool)adopt.Invoke(null, new object[] { continued }),
            "Lerped projectile adopts CE's ballistic model on overpenetration");
        Check(continued.TrajectoryWorker is BallisticsTrajectoryWorker,
            "post-fix: trajectory worker is BallisticsTrajectoryWorker");
        Check(!continued.lerpPosition, "post-fix: deprecated lerpPosition mirror is kept in sync");
        Check(Mathf.Approximately(continued.RemainingSpeedPct, RetainedSpeed),
            "post-fix: RemainingSpeedPct reflects the retained speed");
        Check(Mathf.Approximately(continued.DamageAmount, 2.5f),
            "post-fix: damage follows retained kinetic energy (25% of base)");
        Check(!(bool)ShouldCollideWithSomething.GetValue(continued),
            "post-fix: ticksToImpact<=0 no longer forces a premature impact");

        float startX = continued.ExactPosition.x;
        int ticks = 0;
        while (continued.ExactPosition.y > 0f && ticks < MaxSimulatedTicks)
        {
            continued.LastPos = continued.ExactPosition;
            continued.ExactPosition = continued.TrajectoryWorker.MoveForward(continued);
            ticks++;
        }
        Check(ticks > 0 && ticks < MaxSimulatedTicks,
            "post-fix: ballistic continuation reaches the ground within a bounded lifetime");
        Check(continued.ExactPosition.x - startX > 1f,
            "post-fix: projectile keeps travelling horizontally after overpenetration");

        // ---- Chained overpenetration keeps decaying ----
        var chained = MakeBullet();
        ApplyRetainedSpeed(chained, RetainedSpeed);
        Check((bool)adopt.Invoke(null, new object[] { chained }),
            "chained fixture adopts the ballistic model");
        ApplyRetainedSpeed(chained, RetainedSpeed);
        Check(Mathf.Approximately(chained.RemainingSpeedPct, 0.25f),
            "post-fix: a second overpenetration further reduces RemainingSpeedPct");
        Check(Mathf.Approximately(chained.DamageAmount, 0.625f),
            "post-fix: chained overpenetration keeps decaying damage");

        // ---- Ballistics ammo keeps its existing behavior ----
        var ballistic = MakeBullet(typeof(BallisticsTrajectoryWorker));
        Check(ballistic.TrajectoryWorker is BallisticsTrajectoryWorker,
            "fixture: ballistic ammo resolves to BallisticsTrajectoryWorker");
        var originalWorker = ballistic.TrajectoryWorker;
        ApplyRetainedSpeed(ballistic, RetainedSpeed);
        Check((bool)adopt.Invoke(null, new object[] { ballistic }),
            "ballistic projectile accepts the continuation path");
        Check(ReferenceEquals(originalWorker, ballistic.TrajectoryWorker),
            "ballistic projectile keeps its original worker instance");
        Check(Mathf.Approximately(ballistic.RemainingSpeedPct, RetainedSpeed),
            "ballistic retained speed still drives RemainingSpeedPct");

        // ---- Safety fallbacks ----
        var noLaunchSpeed = MakeBullet();
        noLaunchSpeed.initialSpeed = 0f;
        Check(!(bool)adopt.Invoke(null, new object[] { noLaunchSpeed }),
            "projectile without a launch speed refuses ballistic continuation (no divide-by-zero)");
        Check(noLaunchSpeed.TrajectoryWorker is LerpedTrajectoryWorker,
            "refused projectile stays on its original worker");

        // ---- Serialized in-flight bullets ----
        var reloaded = MakeBullet();
        OverpenetrationBridge.GetOrCreateState(reloaded).continuationActive = true;
        Scribe.mode = LoadSaveMode.PostLoadInit;
        try
        {
            OverpenetrationBridge.ExposeData(reloaded);
        }
        finally
        {
            Scribe.mode = LoadSaveMode.Inactive;
        }
        Check(reloaded.TrajectoryWorker is BallisticsTrajectoryWorker,
            "reloaded in-flight continuation re-adopts the ballistic model");
    }
}
