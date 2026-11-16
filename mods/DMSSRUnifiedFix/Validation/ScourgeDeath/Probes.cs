using WhiteGiver.DMSSRUnifiedFix;
using DMSSRUnifiedFix.Validation;
using System.Diagnostics;
using System.Reflection;
using DMSSR.Content;
using DMSSR.Patches;
using Fortified;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace DMSSRUnifiedFix.Validation.ScourgeDeath;

// Headless seams only. The death/health/ownership/placement methods under test run their real IL.
internal static class Probes
{
    internal static Fixture CurrentFixture = null!;
    internal static readonly List<string> Errors = new();
    internal static readonly List<string> Events = new();
    internal static int PawnKills;
    internal static int MechKills;
    internal static int SetDeadCalls;
    internal static string BuggedSpawnStack = "";
    internal static string NestedKillStack = "";
    private static readonly Harmony Harness = new("whitegiver.dmssr.unifiedfix.validation");
    private static readonly Selector Selector = new();
    private static readonly ColonistBar ColonistBar = (ColonistBar)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(ColonistBar));
    internal static bool CancelDeath;
    internal static bool ThrowDeath;
    internal static bool? FlagAtCancellation;

    internal static void Install(bool sidecarFirst)
    {
        // A single logger suppresses engine output while every observer still receives each log.
        LogCapture.ErrorObserved = text => { Error(text); };
        // No texture/material IO during headless Def construction.
        Harness.Patch(AccessTools.Method(typeof(ContentFinder<UnityEngine.Texture2D>), "Get"), prefix: Method(nameof(Skip)));
        foreach (Type type in new[] { typeof(MaterialPool), typeof(SolidColorMaterials) })
            foreach (MethodInfo target in AccessTools.GetDeclaredMethods(type).Where(method => method.Name is "MatFrom" or "NewSolidColorTexture" or "SimpleSolidColorMaterial"))
                Harness.Patch(target, prefix: Method(nameof(Skip)));
        MethodInfo graphic = AccessTools.GetDeclaredMethods(typeof(GraphicDatabase)).Single(method =>
            method.Name == "Get" && method.IsGenericMethodDefinition && method.GetParameters() is [{ ParameterType: var parameter }] && parameter == typeof(string));
        Harness.Patch(graphic.MakeGenericMethod(typeof(Graphic_Single)), prefix: Method(nameof(Skip)));
        Harness.Patch(AccessTools.PropertyGetter(typeof(Find), nameof(Find.Selector)), prefix: Method(nameof(GetSelector)));
        Harness.Patch(AccessTools.Method(typeof(Translator), nameof(Translator.Translate), new[] { typeof(string) }), prefix: Method(nameof(Translate)));
        Harness.Patch(AccessTools.Method(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.SetDead)), prefix: Method(nameof(SetDead)));
        Harness.Patch(AccessTools.Method(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.RemoveHediff)), prefix: Method(nameof(Removing)));
        Harness.Patch(AccessTools.Method(typeof(Corpse), nameof(Corpse.SpawnSetup)), prefix: Method(nameof(CorpseSpawn)));
        Harness.Patch(AccessTools.Method(typeof(Pawn), nameof(Pawn.MakeCorpse), new[] { typeof(Building_Grave), typeof(bool), typeof(float) }), postfix: Method(nameof(MadeCorpse)));

        // A Unity-free map keeps real ThingGrid/ListerThings and actual ThingOwner ownership.
        Harness.Patch(AccessTools.Method(typeof(Thing), nameof(Thing.SpawnSetup)), prefix: Method(nameof(SpawnThing)));
        Harness.Patch(AccessTools.Method(typeof(Thing), nameof(Thing.DeSpawn)), prefix: Method(nameof(DespawnThing)));
        Harness.Patch(AccessTools.Method(typeof(Pawn), nameof(Pawn.DeSpawn)), prefix: Method(nameof(DespawnThing)));
        Harness.Patch(AccessTools.Method(typeof(Pawn), nameof(Pawn.Destroy)), prefix: Method(nameof(DestroyPawn)));
        Harness.Patch(AccessTools.Method(typeof(Pawn), nameof(Pawn.SpawnSetup)), prefix: Method(nameof(SpawnThing)));
        Harness.Patch(AccessTools.Method(typeof(HumanlikeMech), nameof(Pawn.SpawnSetup)), prefix: Method(nameof(SpawnMech)));
        Harness.Patch(AccessTools.Method(typeof(Corpse), nameof(Corpse.Destroy)), prefix: Method(nameof(DestroyCorpse)));

        // Rendering, story/quest notifications, world bookkeeping and component generation require a game host.
        foreach ((Type type, string name) in new[]
        {
            (typeof(Pawn), "DoKillSideEffects"), (typeof(Pawn), "PreDeathPawnModifications"), (typeof(Pawn), "DropBeforeDying"),
            (typeof(Pawn), "DropAndForbidEverything"), (typeof(Pawn), "Notify_DisabledWorkTypesChanged"),
            (typeof(GenLeaving), "DoLeavingsFor"), (typeof(GenHostility), "Notify_PawnLostForTutor"),
            (typeof(QuestManager), "Notify_PawnKilled"), (typeof(FactionManager), "Notify_PawnKilled"),
            (typeof(IdeoManager), "Notify_PawnKilled"), (typeof(GameComponent_Bossgroup), "Notify_PawnKilled"),
            (typeof(PawnComponentsUtility), "RemoveComponentsOnKilled"), (typeof(PawnComponentsUtility), "CreateInitialComponents"),
            (typeof(PawnRenderer), "SetAllGraphicsDirty"), (typeof(PawnRenderer), "SetAnimation"),
            (typeof(ForbidUtility), "SetForbiddenIfOutsideHomeArea"), (typeof(Corpse), "NotifyColonistBar"),
            (typeof(PawnDiedOrDownedThoughtsUtility), "RemoveLostThoughts"),
            (typeof(PawnDiedOrDownedThoughtsUtility), "RemoveResuedRelativeThought"),
            (typeof(PawnDiedOrDownedThoughtsUtility), "TryGiveThoughts"),
            (typeof(ThingOwner), "NotifyColonistBarIfColonistCorpse"),
            (typeof(BillUtility), "Notify_ColonistUnavailable"),
            (typeof(StoryWatcher_PopAdaptation), "Notify_PawnEvent"),
            (typeof(Faction), "Notify_MemberDied"),
            (typeof(ColonistBar), "MarkColonistsDirty"),
            (typeof(FleckMaker), "Static"), (typeof(Pawn_NeedsTracker), "AddOrRemoveNeedsAsAppropriate"),
        })
        {
            foreach (MethodInfo target in AccessTools.GetDeclaredMethods(type).Where(method => method.Name == name))
                Harness.Patch(target, prefix: Method(nameof(Skip)));
        }
        Harness.Patch(AccessTools.PropertyGetter(typeof(Pawn), "IsColonistPlayerControlled"), prefix: Method(nameof(False)));
        Harness.Patch(AccessTools.PropertyGetter(typeof(Find), nameof(Find.ColonistBar)), prefix: Method(nameof(NoColonistBar)));
        Harness.Patch(AccessTools.Method(typeof(MechRepairUtility), nameof(MechRepairUtility.IsMissingWeapon)), prefix: Method(nameof(False)));
        Harness.Patch(AccessTools.Method(typeof(PawnUtility), nameof(PawnUtility.ShouldSendNotificationAbout)), prefix: Method(nameof(False)));
        Harness.Patch(AccessTools.Method(typeof(WorldPawnsUtility), nameof(WorldPawnsUtility.IsWorldPawn)), prefix: Method(nameof(False)));

        if (sidecarFirst) ApplySidecar();
        // Compile callers only after their headless seams have been patched (avoid JIT-inlined UI calls).
        foreach (Type patch in new[]
        {
            typeof(Patch_ScourgeNextDeath), typeof(Patch_ScourgeResurrected), typeof(Patch_ContentDeathDrops),
            typeof(Patch_Schizophrenia_CascadeKill), typeof(Patch_RegisterUndying_OnCorpseSpawn), typeof(Patch_InitialDeathCheat_SpawnSetup),
            typeof(ScourgeResurrection).Assembly.GetType("DMSSR.Bosses.Hydra.Patch_HydraDisguiseKilled")!,
        }) Harness.CreateClassProcessor(patch).Patch();
        Harness.Patch(AccessTools.Method(typeof(Pawn), nameof(Pawn.Kill)), prefix: Method(nameof(PawnKill)));
        Harness.Patch(AccessTools.Method(typeof(Pawn), nameof(Pawn.Kill)), prefix: new HarmonyMethod(AccessTools.Method(typeof(Probes), nameof(Cancel))) { priority = Priority.Last });
        Harness.Patch(AccessTools.Method(typeof(HumanlikeMech), nameof(Pawn.Kill)), prefix: Method(nameof(MechKill)));
    }

    internal static void ApplySidecar()
    {
        Assembly patch = Assembly.LoadFrom(Program.PatchPath);
        patch.GetType("WhiteGiver.DMSSRUnifiedFix.DMSSRUnifiedFixMod")!
            .GetMethod("ApplyPatches", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(null, null);
    }

    private static HarmonyMethod Method(string name) => new(AccessTools.Method(typeof(Probes), name));
    private static bool Skip() => false;
    private static bool Translate(string key, ref TaggedString __result) { __result = key; return false; }
    private static bool GetSelector(ref Selector __result) { __result = Selector; return false; }
    private static bool NoColonistBar(ref ColonistBar __result) { __result = ColonistBar; return false; }
    private static bool False(ref bool __result) { __result = false; return false; }
    private static bool Cancel(Pawn __instance)
    {
        if (!CancelDeath && !ThrowDeath) return true;
        FlagAtCancellation = __instance.health.isBeingKilled;
        if (ThrowDeath) throw new InvalidOperationException("Expected death-prefix failure");
        return false;
    }
    private static bool Error(string text)
    {
        Errors.Add(text);
        if (text.Contains("spawned in bugged state", StringComparison.Ordinal))
        {
            BuggedSpawnStack = new StackTrace(true).ToString();
            Console.WriteLine(BuggedSpawnStack);
        }
        return false;
    }
    private static bool Warning(string text) { Console.WriteLine("LOG.WARNING " + text); return false; }
    private static void PawnKill(Pawn __instance) { PawnKills++; Events.Add($"Pawn.Kill dead={__instance.Dead} killing={__instance.health.isBeingKilled} holder={__instance.ParentHolder?.GetType().Name}"); }
    private static void MechKill(Pawn __instance)
    {
        MechKills++;
        if (MechKills == 2) NestedKillStack = new StackTrace(true).ToString();
        Events.Add($"HumanlikeMech.Kill dead={__instance.Dead} killing={__instance.health.isBeingKilled}");
    }
    private static void SetDead(Pawn_HealthTracker __instance) { SetDeadCalls++; Events.Add($"SetDead wasDead={__instance.Dead}"); }
    private static void Removing(Pawn_HealthTracker __instance, Hediff hediff) => Events.Add($"RemoveHediff {hediff.def.defName} killing={__instance.isBeingKilled}");
    private static void CorpseSpawn(Corpse __instance) => Events.Add($"Corpse.SpawnSetup id={__instance.thingIDNumber} bugged={__instance.Bugged}");
    private static void MadeCorpse(Corpse __result)
    {
        if (__result != null) { CurrentFixture.Corpses.Add(__result); Events.Add($"MakeCorpse id={__result.thingIDNumber} holder={__result.InnerPawn?.ParentHolder?.GetType().Name}"); }
    }
    private static bool SpawnThing(Thing __instance, Map map)
    {
        if (!ReferenceEquals(map, CurrentFixture.Map)) throw new InvalidOperationException("Unexpected map");
        CurrentFixture.Register(__instance, __instance.Position);
        Events.Add($"SpawnThing id={__instance.thingIDNumber} spawned={__instance.Spawned} map={__instance.Map?.uniqueID}");
        return false;
    }
    private static bool SpawnMech(Thing __instance, Map map, bool respawningAfterLoad)
    {
        SpawnThing(__instance, map);
        // Execute DMSSR's real Pawn.SpawnSetup postfix (the headless seam skips the render-heavy override).
        AccessTools.Method(typeof(Patch_InitialDeathCheat_SpawnSetup), "Postfix")
            .Invoke(null, new object[] { __instance, map, respawningAfterLoad });
        return false;
    }
    private static bool DespawnThing(Thing __instance) { CurrentFixture.Unregister(__instance); return false; }

    internal static void SetCancellationPriority(int priority)
    {
        MethodInfo kill = AccessTools.Method(typeof(Pawn), nameof(Pawn.Kill));
        MethodInfo cancel = AccessTools.Method(typeof(Probes), nameof(Cancel));
        Harness.Unpatch(kill, cancel);
        Harness.Patch(kill, prefix: new HarmonyMethod(cancel) { priority = priority });
    }
    private static bool DestroyPawn(Pawn __instance)
    {
        CurrentFixture.Unregister(__instance);
        Fixture.Set(typeof(Thing), "mapIndexOrState", __instance, (sbyte)-2);
        return false;
    }
    private static bool DestroyCorpse(Corpse __instance)
    {
        CurrentFixture.Unregister(__instance);
        Fixture.Set(typeof(Thing), "mapIndexOrState", __instance, (sbyte)-2);
        return false;
    }

    internal static void Reset()
    {
        Errors.Clear(); Events.Clear(); PawnKills = MechKills = SetDeadCalls = 0; BuggedSpawnStack = NestedKillStack = "";
        RemovalProbe.PreCount = RemovalProbe.PostCount = 0; RemovalProbe.Throw = false;
        CancelDeath = ThrowDeath = false; FlagAtCancellation = null;
    }
}
