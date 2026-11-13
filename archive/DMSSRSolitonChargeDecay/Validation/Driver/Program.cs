using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Xml.Linq;
using System.Xml;
using DMSSR.Verbs;
using HarmonyLib;
using RimWorld;
using Verse;

internal static class Program
{
    private static readonly string GameRoot = Environment.GetEnvironmentVariable("RW_GAME_ROOT")
        ?? "A:/SteamLibrary/steamapps/common/RimWorld";
    private static readonly string Workshop = Environment.GetEnvironmentVariable("RW_WORKSHOP_ROOT")
        ?? "A:/SteamLibrary/steamapps/workshop/content/294100";
    private static string output = "";
    private static int checks;

    public static int Main(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine("Usage: Driver <isolated-reference-directory> <red|patch-dll>");
            return 2;
        }
        AssemblyLoadContext.Default.Resolving += (_, name) =>
        {
            string file = name.Name + ".dll";
            string[] roots = [args[0], GameRoot, Workshop + "/3835512735/1.7/Assemblies",
                Workshop + "/2009463077/1.7/Assemblies", Workshop + "/3498575851/1.7/Assemblies",
                Workshop + "/3819799478/1.7/Assemblies"];
            foreach (string root in roots)
            {
                string candidate = Path.GetFullPath(Path.Combine(root, file));
                if (File.Exists(candidate)) return AssemblyLoadContext.Default.LoadFromAssemblyPath(candidate);
            }
            return null;
        };
        try
        {
            output = Path.GetFullPath(args[0]);
            Run(args[1]);
            Console.WriteLine($"PASS: {checks} real-DLL checks.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Run(string patchPath)
    {
        if (!Path.GetDirectoryName(typeof(Verb).Assembly.Location)!.Equals(output, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The game assembly must resolve from the isolated validation directory.");
        if (!typeof(Verb_MechParticleBeam).Assembly.Location.Equals(Path.GetFullPath(
                Workshop + "/3835512735/1.7/Assemblies/DMSSR.dll"), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The beam verb must resolve from the actual Workshop DMSSR.dll.");
        Game game = (Game)RuntimeHelpers.GetUninitializedObject(typeof(Game));
        Set(game, "tickManager", new TickManager());
        Current.Game = game;
        var defs = Directory.GetFiles(Workshop + "/3835512735/1.7/Defs", "*.xml", SearchOption.AllDirectories)
            .SelectMany(path => XDocument.Load(path).Root!.Elements("ThingDef"))
            .Where(def => def.Element("verbs")?.Elements("li").Any(verb =>
                verb.Element("verbClass")?.Value == "DMSSR.Verbs.Verb_MechParticleBeam") == true)
            .ToDictionary(def => def.Element("defName")!.Value);
        Check(defs.Count == 5, "XML: exactly five ThingDefs use Verb_MechParticleBeam");

        foreach (string name in defs.Keys)
        {
            Verb_MechParticleBeam beam = Beam(defs[name], 900, 1000);
            float before = beam.ChargePercent;
            beam.Reset();
            float carry = Call<float>(beam, "ComputeCarryTicks", 1000);
            if (name == "DMSSR_Soliton_Beam")
                Check(carry > 0 && beam.ChargePercent == 0,
                    $"RED {name}: native carry is {carry} ticks but idle ChargePercent is zero");
            else
                Check(carry == 0 && beam.ChargePercent == 0 && before > 0,
                    $"RED {name}: Reset clears native carry and charge immediately");
        }
        Verb_MechParticleBeam redDisplay = Beam(defs["DMSSR_Soliton_Beam"], 100, 1000);
        Check(redDisplay.DisplayChargePercent == 1f, "RED: native display reaches full charge during firing");
        redDisplay.Reset();
        SetTick(1009);
        Check(redDisplay.DisplayChargePercent == 0f && Call<float>(redDisplay, "ComputeCarryTicks", 1009) > 0f,
            "RED: native UI loses all charge within nine ticks while stored carry still exists");

        Verb_MechParticleBeam redFinish = Beam(defs["DMSSR_Soliton_Beam"], 900, 1000);
        CallVoid(redFinish, "TryCastNextBurstShot");
        Check(redFinish.state == VerbState.Idle && Get<int>(redFinish, "burstStartTick") == 900,
            "RED: real TryCastNextBurstShot with no caster map changes to Idle without DMSSR Reset");
        redFinish.Reset();
        Check(Call<float>(redFinish, "ComputeCarryTicks", 1000) == 0f,
            "RED: later Reset calls GetWarmupTicks in Idle and captures zero even with beamSwitchFire=true");

        if (patchPath == "red") return;
        if (patchPath != "expect-decay")
        {
            Assembly patch = Assembly.LoadFrom(Path.GetFullPath(patchPath));
            Type modType = patch.GetType("WhiteGiver.DMSSRSolitonChargeDecay.DMSSRSolitonChargeDecayMod", true)!;
            _ = Activator.CreateInstance(modType, RuntimeHelpers.GetUninitializedObject(typeof(ModContentPack)));
            patch.GetType("WhiteGiver.DMSSRSolitonChargeDecay.SolitonChargeDecay", true)!.GetMethod("Apply")!.Invoke(null, null);
            Check(Harmony.GetAllPatchedMethods().Count(method => Harmony.GetPatchInfo(method)?.Owners
                .Contains("whitegiver.dmssr.solitonchargedecay") == true) == 4,
                "GREEN: real Mod constructor registers exactly four distinct hooks and repeated Apply is idempotent");
        }
        Verb_MechParticleBeam heavy = Beam(defs["DMSSR_Soliton_Beam_Heavy"], 100, 1000);
        heavy.Reset();
        Check(Math.Abs(heavy.ChargePercent - 0.5f) < 0.00001f,
            "GREEN: stopping a half-charged heavy beam retains 50% activity on the stop tick");
        SetTick(1180);
        Check(Math.Abs(heavy.ChargePercent - 0.25f) < 0.00001f,
            "GREEN: heavy beam decays to 25% after half of native 360-tick coldTime");

        ValidateAllDefs(defs);
        ValidateBurstLifecycle(defs);
        ValidateSafety(defs);
        ValidateRealCasters(defs);
        ValidateScribe(defs);
    }

    private static void ValidateAllDefs(Dictionary<string, XElement> defs)
    {
        foreach ((string name, XElement def) in defs)
        {
            Verb_MechParticleBeam beam = Beam(def, 100, 1000);
            var props = (VerbProperties_MechParticleBeam)beam.verbProps;
            float configured = props.warmupExponentParam;
            int cold = props.coldTime > 0 ? props.coldTime : (int)MathF.Round(configured / 5f);
            float active = beam.ChargePercent;
            Check(Call<int>(beam, "get_ShotsPerBurst") == int.MaxValue,
                $"{name}: XML burstShotCount=1 is overridden by infinite burst");
            SetTick(1001);
            Check(beam.ChargePercent >= active, $"{name}: ordinary shot gaps keep charging in Bursting state");
            SetTick(1000);
            float stopped = beam.DisplayChargePercent;
            beam.Reset();
            Check(Close(beam.ChargePercent, active), $"{name}: stop tick retains capped activity");
            Check(Get<int>(beam, "burstStartTick") == -1 && beam.state == VerbState.Idle,
                $"{name}: original Reset still clears the firing state");
            Check(props.beamSwitchFire == (name == "DMSSR_Soliton_Beam") && props.coldTime == (name == "DMSSR_Soliton_Beam" ? 120 : 0),
                $"{name}: XML properties remain unchanged");
            SetTick(1000 + cold / 2);
            Check(Close(beam.ChargePercent, active / 2f), $"{name}: idle activity halves over {cold / 2} ticks");
            Check(Close(beam.DisplayChargePercent, active / 2f), $"{name}: real display follows native decay");
            beam.Reset();
            Check(Close(beam.ChargePercent, active / 2f) && Get<int>(beam, "lastStopTick") == 1000,
                $"{name}: repeated idle Reset neither reheats nor restarts cooling");
            SetTick(1000 + cold);
            Check(beam.ChargePercent == 0f, $"{name}: coldTime ends at zero");
            SetTick(1000 + cold + 500);
            Check(beam.ChargePercent == 0f && Call<float>(beam, "ComputeCarryTicks", 999) >= 0,
                $"{name}: expired and backward-clock queries stay nonnegative");
            Check(stopped == active, $"{name}: display and actual activity agree while firing");
            Verb_MechParticleBeam everyTick = Beam(def, 100, 1000);
            everyTick.Reset();
            bool linear = true;
            for (int tick = 0; tick <= cold + 2; tick++)
            {
                SetTick(1000 + tick);
                float expected = active * Math.Max(0f, 1f - (float)tick / cold);
                linear &= Close(everyTick.ChargePercent, expected) && Close(everyTick.DisplayChargePercent, expected);
            }
            Check(linear, $"{name}: every tick of real activity and UI matches native linear decay");
        }
    }

    private static void ValidateBurstLifecycle(Dictionary<string, XElement> defs)
    {
        MethodInfo next = AccessTools.DeclaredMethod(typeof(Verb), "TryCastNextBurstShot");
        Verb_MechParticleBeam interrupted = Beam(defs["DMSSR_Soliton_Beam_Heavy"], 100, 1000);
        int callbacks = 0;
        interrupted.castCompleteCallback = () => callbacks++;
        CallVoid(interrupted, "TryCastNextBurstShot");
        Check(interrupted.state == VerbState.Idle && Get<int>(interrupted, "burstStartTick") == -1,
            "GREEN: real failed-shot completion runs DMSSR Reset cleanup");
        Check(Close(interrupted.ChargePercent, 0.5f) && callbacks == 1,
            "GREEN: failed-shot completion retains 50% and original callback executes once");
        Check(interrupted.castCompleteCallback != null,
            "GREEN: failed-shot cleanup preserves the original callback for later turret bursts");

        Harmony qa = new("whitegiver.dmssr.solitonchargedecay.validation");
        MethodInfo shot = AccessTools.DeclaredMethod(typeof(Verb_MechParticleBeam), "TryCastShot");
        qa.Patch(shot, prefix: new HarmonyMethod(typeof(Program), nameof(DeliveredShot)));
        foreach ((string name, XElement def) in defs)
        {
            Verb_MechParticleBeam beam = Beam(def, 100, 1000);
            float capped = Math.Min(900f, ((VerbProperties_MechParticleBeam)beam.verbProps).warmupExponentParam);
            int cold = Call<int>(beam, "GetColdTimeTicks");
            beam.Reset();
            SetTick(1000 + cold / 2);
            beam.WarmupComplete();
            Check(Close(Get<float>(beam, "warmupCarryTicks"), capped / 2f) && beam.state == VerbState.Bursting,
                $"{name}: real WarmupComplete inherits decayed carry after the aiming interval");
            float restarted = beam.ChargePercent;
            SetTick(1000 + cold / 2 + 1);
            CallVoid(beam, "TryCastNextBurstShot");
            Check(beam.state == VerbState.Bursting && beam.ChargePercent >= restarted,
                $"{name}: successful real burst shot does not stop charge");
            CallVoid(beam, "UpdateDerivedStats");
            Check(Get<float>(beam, "cachedArmorPenetration") >= ((VerbProperties_MechParticleBeam)beam.verbProps).baseArmorPenetration,
                $"{name}: original derived-stat updates still execute after resuming");
            beam.Reset();
            float firstStop = beam.ChargePercent;
            SetTick(2000);
            Check(beam.ChargePercent == 0f && firstStop > 0f, $"{name}: second firing interval also cools completely");
        }
        qa.Unpatch(shot, HarmonyPatchType.All, qa.Id);
        Check(Harmony.GetPatchInfo(next)!.Postfixes.Count(p => p.owner == "whitegiver.dmssr.solitonchargedecay") == 1,
            "GREEN: exactly one lifecycle postfix is registered on actual Verb.TryCastNextBurstShot");

        Verb_MechParticleBeam idleCapture = Beam(defs["DMSSR_Soliton_Beam_Heavy"], 100, 1000);
        idleCapture.state = VerbState.Idle;
        idleCapture.Reset();
        Check(Close(idleCapture.ChargePercent, 0.5f), "GREEN: stop capture survives Idle being set before Reset");
        Verb_MechParticleBeam early = Beam(defs["DMSSR_Soliton_Beam_Heavy"], 1000, 999);
        early.Reset();
        Check(early.ChargePercent == 0f, "GREEN: stop before burstStartTick cannot create negative activity");
        Verb_MechParticleBeam custom = Beam(defs["DMSSR_Soliton_Beam_Heavy"], 100, 1000);
        ((VerbProperties_MechParticleBeam)custom.verbProps).coldTime = 1000;
        custom.Reset();
        SetTick(1500);
        Check(Close(custom.ChargePercent, 0.25f), "GREEN: upstream/custom positive coldTime is honored exactly");
        NonBeamVerb unrelated = new() { verbProps = new VerbProperties(), verbTracker = custom.verbTracker, caster = custom.caster,
            state = VerbState.Bursting };
        unrelated.castCompleteCallback = () => callbacks++;
        CallVoid(unrelated, "TryCastNextBurstShot");
        Check(unrelated.state == VerbState.Idle && unrelated.castCompleteCallback != null && callbacks == 2,
            "GREEN: global lifecycle postfix leaves non-DMSSR verbs unchanged");
    }

    private static void ValidateSafety(Dictionary<string, XElement> defs)
    {
        Verb_MechParticleBeam beam = Beam(defs["DMSSR_Soliton_Beam_Heavy"], 100, 1900);
        Check(beam.IsOverchargedState && Close(Call<float>(beam, "GetBaseDamage"), 90f),
            "GREEN: original full-charge overheat threshold and doubled damage remain");
        beam.ToggleSafetyMode();
        Check(beam.SafetyMode && beam.IsSafetyCooling && beam.SafetyCooldownTicks == 360,
            "GREEN: real ToggleSafetyMode at overload starts original 360-tick forced cooling");
        Check(beam.ChargePercent == 0 && Get<float>(beam, "storedWarmupTicks") == 0 && Get<int>(beam, "lastStopTick") == -1,
            "GREEN: safety cooldown explicitly vents all activity instead of retaining it");
        Check(!beam.Available(), "GREEN: safety cooling blocks firing through real Available");
        beam.WarmupComplete();
        Check(beam.state == VerbState.Idle && beam.ChargePercent == 0,
            "GREEN: WarmupComplete cannot bypass forced cooling");
        SetTick(2260);
        Check(!beam.IsSafetyCooling && beam.Available(), "GREEN: original safety duration expires normally");
        beam.ToggleSafetyMode();
        Check(!beam.SafetyMode && beam.ChargePercent == 0f, "GREEN: mode toggle while cold adds no charge");
    }

    private static void ValidateRealCasters(Dictionary<string, XElement> defs)
    {
        Map map = (Map)RuntimeHelpers.GetUninitializedObject(typeof(Map));
        Set(Current.Game, "maps", new List<Map> { map });
        Thing target = new() { def = new ThingDef { category = ThingCategory.Building, building = new BuildingProperties() } };
        Set(target, "mapIndexOrState", (sbyte)0);
        Set(target, "positionInt", new IntVec3(30, 0, 0));

        Building_TurretGun turret = (Building_TurretGun)RuntimeHelpers.GetUninitializedObject(typeof(Building_TurretGun));
        turret.def = new ThingDef { category = ThingCategory.Building, building = new BuildingProperties() };
        Set(turret, "mapIndexOrState", (sbyte)0);
        Set(turret, "positionInt", IntVec3.Zero);
        Set(turret, "forcedTarget", LocalTargetInfo.Invalid);
        Verb_MechParticleBeam held = Beam(defs["DMS_Gun_BeamLaser"], 100, 1000);
        held.caster = turret;
        Set(held, "currentTarget", new LocalTargetInfo(target));
        Set(turret, "holdFire", true);
        held.BurstingTick();
        Check(held.state == VerbState.Idle && Close(held.ChargePercent, 0.5f),
            "GREEN: real turret holdFire -> ShouldInterruptForTurret -> BurstingTick -> Reset retains charge");
        SetTick(1180);
        Check(Close(held.ChargePercent, 0.25f), "GREEN: held-fire turret charge decays to 25% after 180 ticks");

        Set(turret, "holdFire", false);
        Verb_MechParticleBeam retarget = Beam(defs["DMS_Gun_BeamLaser"], 100, 1000);
        retarget.caster = turret;
        Set(retarget, "currentTarget", new LocalTargetInfo(target));
        Set(retarget, "forcedTargetAtStart", LocalTargetInfo.Invalid);
        Set(turret, "forcedTarget", new LocalTargetInfo(target));
        retarget.BurstingTick();
        Check(retarget.state == VerbState.Idle && Close(retarget.ChargePercent, 0.5f),
            "GREEN: real turret forced-target change captures charge before switching targets");

        Set(turret, "forcedTarget", LocalTargetInfo.Invalid);
        Verb_MechParticleBeam gap = Beam(defs["DMS_Gun_BeamLaser"], 100, 1000);
        gap.caster = turret;
        Set(gap, "currentTarget", new LocalTargetInfo(target));
        Set(gap, "ticksToNextBurstShot", 100);
        Harmony visuals = new("whitegiver.dmssr.solitonchargedecay.validation.visuals");
        visuals.Patch(AccessTools.DeclaredMethod(typeof(Verb_MechParticleBeam), "GetVisualBudgetScale"),
            prefix: new HarmonyMethod(typeof(Program), nameof(NoVisualBudget)));
        visuals.Patch(AccessTools.DeclaredMethod(typeof(Verb_MechParticleBeam), "UpdateBeamVisual"),
            prefix: new HarmonyMethod(typeof(Program), nameof(Skip)));
        SetTick(1001);
        gap.VerbTick();
        Check(gap.state == VerbState.Bursting && gap.ChargePercent > 0.5f && Get<int>(gap, "burstStartTick") == 100,
            "GREEN: real VerbTick -> BurstingTick during a shot gap keeps the same charging burst");
        visuals.UnpatchAll(visuals.Id);

        Pawn pawn = (Pawn)RuntimeHelpers.GetUninitializedObject(typeof(Pawn));
        pawn.def = new ThingDef { category = ThingCategory.Pawn, race = new RaceProperties() };
        Set(pawn, "mapIndexOrState", (sbyte)0);
        Set(pawn, "positionInt", IntVec3.Zero);
        pawn.stances = new Pawn_StanceTracker(pawn);
        Verb_MechParticleBeam pawnBeam = Beam(defs["DMSSR_Soliton_Beam_Heavy"], 100, 1000);
        pawnBeam.caster = pawn;
        Set(pawnBeam, "currentTarget", new LocalTargetInfo(target));
        Harmony qa = new("whitegiver.dmssr.solitonchargedecay.validation.pawn");
        qa.Patch(AccessTools.PropertyGetter(typeof(Pawn), nameof(Pawn.Dead)), prefix: new HarmonyMethod(typeof(Program), nameof(False)));
        qa.Patch(AccessTools.PropertyGetter(typeof(Pawn), nameof(Pawn.Downed)), prefix: new HarmonyMethod(typeof(Program), nameof(False)));
        qa.Patch(AccessTools.PropertyGetter(typeof(Pawn), nameof(Pawn.IsPlayerControlled)), prefix: new HarmonyMethod(typeof(Program), nameof(True)));
        qa.Patch(AccessTools.PropertyGetter(typeof(Pawn), nameof(Pawn.CurJob)), prefix: new HarmonyMethod(typeof(Program), nameof(NoJob)));
        pawnBeam.BurstingTick();
        Check(pawnBeam.state == VerbState.Idle && Close(pawnBeam.ChargePercent, 0.5f),
            "GREEN: real Pawn no-job manual stop -> ShouldInterruptBeam -> Reset retains charge");
        qa.UnpatchAll(qa.Id);
    }

    public static bool DeliveredShot(ref bool __result) { __result = true; return false; }
    public static bool False(ref bool __result) { __result = false; return false; }
    public static bool True(ref bool __result) { __result = true; return false; }
    public static bool NoJob(ref Verse.AI.Job? __result) { __result = null; return false; }
    public static bool NoVisualBudget(ref float __result) { __result = 0f; return false; }
    public static bool Skip() => false;

    private sealed class NonBeamVerb : Verb
    {
        protected override bool TryCastShot() => false;
    }

    private static void ValidateScribe(Dictionary<string, XElement> defs)
    {
        Verb_MechParticleBeam beam = Beam(defs["DMSSR_Soliton_Beam_Heavy"], 100, 1000);
        beam.Reset();
        string path = Path.Combine(output, "charge-roundtrip.xml");
        Scribe.mode = LoadSaveMode.Inactive;
        Scribe.saver = new ScribeSaver();
        Scribe.saver.InitSaving(path, "verb");
        beam.ExposeData();
        Scribe.saver.FinalizeSaving();
        XmlDocument xml = new();
        xml.Load(path);
        Check(xml.DocumentElement!.SelectSingleNode("storedWarmupTicks")!.InnerText == "900"
            && xml.DocumentElement.SelectSingleNode("lastStopTick")!.InnerText == "1000",
            "GREEN: real DMSSR ExposeData saves existing native charge fields");
        Verb_MechParticleBeam loaded = Beam(defs["DMSSR_Soliton_Beam_Heavy"], -1, 1180);
        Scribe.loader = new ScribeLoader { curXmlParent = xml.DocumentElement };
        Scribe.mode = LoadSaveMode.LoadingVars;
        loaded.ExposeData();
        Scribe.mode = LoadSaveMode.Inactive;
        Scribe.loader.curXmlParent = null;
        Check(loaded.state == VerbState.Idle && Close(loaded.ChargePercent, 0.25f),
            "GREEN: real Scribe round-trip restores idle decay without adding save fields");
    }

    private static Verb_MechParticleBeam Beam(XElement def, int start, int now)
    {
        VerbProperties_MechParticleBeam props = new();
        XElement xml = def.Element("verbs")!.Element("li")!;
        foreach (FieldInfo field in typeof(VerbProperties_MechParticleBeam).GetFields())
        {
            string? value = xml.Element(field.Name)?.Value;
            if (value == null) continue;
            if (field.FieldType == typeof(bool) || field.FieldType == typeof(int) || field.FieldType == typeof(float))
                field.SetValue(props, Convert.ChangeType(value, field.FieldType, System.Globalization.CultureInfo.InvariantCulture));
        }
        ThingWithComps weapon = new() { def = new ThingDef { defName = "ValidationBeam", category = ThingCategory.Item } };
        CompEquippable comp = new() { parent = weapon };
        Verb_MechParticleBeam beam = new()
        {
            verbProps = props, state = VerbState.Bursting, caster = weapon, verbTracker = new VerbTracker(comp)
        };
        Set(beam, "burstStartTick", start);
        SetTick(now);
        return beam;
    }

    private static void SetTick(int tick) => Set(Find.TickManager, "ticksGameInt", tick);
    private static void Set(object target, string name, object value) => AccessTools.Field(target.GetType(), name).SetValue(target, value);
    private static T Get<T>(object target, string name) => (T)AccessTools.Field(target.GetType(), name).GetValue(target)!;
    private static T Call<T>(object target, string name, params object[] args) =>
        (T)AccessTools.Method(target.GetType(), name).Invoke(target, args)!;
    private static void CallVoid(object target, string name, params object[] args) =>
        AccessTools.Method(target.GetType(), name).Invoke(target, args);
    private static bool Close(float value, float expected) => Math.Abs(value - expected) < 0.00001f;
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("FAIL: " + message);
        checks++;
        Console.WriteLine("PASS: " + message);
    }
}
