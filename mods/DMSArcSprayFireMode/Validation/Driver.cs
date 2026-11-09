using System.Globalization;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Xml;
using Verb_ArcSprayProjectile = Fortified.Verb_ArcSprayProjectile;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using WeaponStation;
using WhiteGiver.DMSArcSprayFireMode;

internal static class Driver
{
    private static Faction player = null!, enemy = null!;
    private static Map map = null!;
    private static int checks, id = 500;
    private static readonly List<LaunchData> launches = new();
    private sealed record LaunchData(LocalTargetInfo Used, LocalTargetInfo Intended, ProjectileHitFlags Flags, Thing? Equipment);
    private static readonly List<Verb> reports = new();
    private static readonly Dictionary<Thing, float> weaponAccuracy = new();
    private static float shooterAccuracy = 1f, blockChance;
    private static Thing? cover;
    private static int wildCalls, fxCalls, muzzleCalls, soundCalls;
    private static bool los = true;
    private static int errors;
    private static ThingDef projectile = null!;
    private static Selector selector = null!;
    private static readonly MethodInfo shoot = AccessTools.DeclaredMethod(typeof(Verb_Spray), "TryCastShot");
    private static readonly MethodInfo prepare = AccessTools.DeclaredMethod(typeof(Verb_ArcSprayProjectile), "PreparePath");
    private static readonly Func<ThingWithComps, IEnumerable<Gizmo>> baseGizmos = BaseGizmoCaller();

    internal static void Run(string mode, string game, string workshop, string mod, string qa)
    {
        Setup(mod, qa);
        if (mode == "ce")
        {
            Set(typeof(ModsConfig), "activeModsHashSet", null, new HashSet<string> { "ceteam.combatextended" });
            _ = new ArcFireModesMod(null!);
            Check(!ArcFireModes.Enabled && !OwnedPatches().Any(), "CE installs no hooks");
            var (gun, verb) = Gun("CEArc"); var host = Turret(gun);
            ArcFireModes.SetChoice(gun, FireMode.Direct);
            Check(!ArcFireModes.Controls(host).Any() && !ArcFireModes.IsDirect(verb), "CE no controls or direct fire");
            Console.WriteLine($"PASS CE: {checks} checks."); return;
        }
        var (baseGun, baseVerb) = Gun("DMS_GAU8Gatlinggun");
        var turret = Turret(baseGun);
        if (mode == "red")
        {
            Begin(baseVerb);
            int failures = 0;
            failures += ExpectRed(() => Check(baseVerb.AimAngleOverride == null, "player defaults to direct aim"));
            Check(Cast(baseVerb), "baseline fires");
            failures += ExpectRed(() => Check(reports.Count == 1 && launches.Single().Intended == baseVerb.CurrentTarget, "player direct targets currentTarget via ShotReport"));
            failures += ExpectRed(() => Check(baseGizmos(turret).OfType<Command_Action>().Any(c => c.groupKey is 173802701 or 173802702), "player gets mode control"));
            Check(failures == 3, "three desired assertions really fail before applying patches");
            Console.WriteLine("RED reproduced: 3 expected failing assertions; actual FFF and vanilla shot methods executed without our hooks.");
            return;
        }
        if (mode == "drift")
        {
            _ = new ArcFireModesMod(null!);
            Check(!ArcFireModes.Enabled && !OwnedPatches().Any() && errors == 1, "real constructor refuses drift, logs error and leaves zero hooks");
            Check(!ArcFireModes.Controls(turret).Any() && !ArcFireModes.IsDirect(baseVerb), "drift cannot expose partially functioning controls");
            Console.WriteLine("PASS DRIFT: actual Apply refuses altered QA-only shot IL, reports one error, retains zero forward hooks and no UI."); return;
        }
        _ = new ArcFireModesMod(null!); _ = new ArcFireModesMod(null!);
        foreach (LogMessage message in Log.Messages) Console.WriteLine("GAME LOG " + message.text);
        Check(ArcFireModes.Enabled, "patch group installed");
        Check(OwnedPatches().Count() == 6, "six forward targets installed once");
        ValidateDirect(baseGun, baseVerb);
        ValidateSpray(baseGun, baseVerb);
        ValidateSwitching(baseGun, baseVerb);
        ValidateGizmosAndFactions(workshop);
        ValidateScribe(baseGun, qa);
        ValidateBurst(baseGun, baseVerb, turret);
        Check(errors == 0, "zero game error logs during green checks");
        Console.WriteLine($"PASS GREEN: {checks} assertions; real original and reverse-patched methods, FFF path, ShotReport, scatter, fuel/charges, Gizmos and Scribe exercised.");
        Console.WriteLine("NOT TESTED: game rendering, hardware mouse input, projectile flight/impact, full-map AI or any user save. Map/weather/cover/LOS/stat inputs and final spawn/launch/FX boundaries are isolated; target shot/path/report/burst/Scribe methods are real.");
    }

    private static IEnumerable<MethodBase> OwnedPatches() => Harmony.GetAllPatchedMethods().Where(m => Harmony.GetPatchInfo(m)?.Owners.Contains(ArcFireModesMod.HarmonyId) == true);
    private static Func<ThingWithComps, IEnumerable<Gizmo>> BaseGizmoCaller()
    {
        // Nonvirtual call into the actual patched base iterator, without unrelated Pawn/UI catalogs.
        var method = new DynamicMethod("BaseGizmos", typeof(IEnumerable<Gizmo>), new[] { typeof(ThingWithComps) }, typeof(Driver).Module, true);
        ILGenerator il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, AccessTools.DeclaredMethod(typeof(ThingWithComps), "GetGizmos")); il.Emit(OpCodes.Ret);
        return method.CreateDelegate<Func<ThingWithComps, IEnumerable<Gizmo>>>();
    }
    private static int ExpectRed(Action assertion)
    {
        try { assertion(); }
        catch (InvalidOperationException exception) { Console.WriteLine("EXPECTED RED " + exception.Message); return 1; }
        return 0;
    }

    private static void Setup(string root, string qa)
    {
        Current.ProgramState = ProgramState.Entry;
        Current.Game = (Game)RuntimeHelpers.GetUninitializedObject(typeof(Game));
        Set(typeof(Game), "maps", Current.Game, new List<Map>());
        Set(typeof(Game), "tickManager", Current.Game, new TickManager());
        Set(typeof(Game), "uniqueIDsManager", Current.Game, new UniqueIDsManager());
        Current.Game.questManager = new QuestManager();
        player = new Faction { def = new FactionDef { defName = "QAP", isPlayer = true } };
        enemy = new Faction { def = new FactionDef { defName = "QAE" } };
        Current.Game.InitData = new GameInitData { playerFaction = player };
        Set(typeof(ModsConfig), "activeModsHashSet", null, new HashSet<string> { "aoba.framework_steam" });
        string sandbox = Path.Combine(qa, "save-data"); Directory.CreateDirectory(Path.Combine(sandbox, "Config"));
        Set(typeof(GenFilePaths), "saveDataPath", null, sandbox);
        Set(typeof(Prefs), "data", null, RuntimeHelpers.GetUninitializedObject(typeof(PrefsData)));
        var language = (LoadedLanguage)RuntimeHelpers.GetUninitializedObject(typeof(LoadedLanguage));
        Set(typeof(LoadedLanguage), "workerInt", language, new LanguageWorker_English());
        Set(typeof(LoadedLanguage), "dataIsLoaded", language, true);
        language.keyedReplacements = new Dictionary<string, LoadedLanguage.KeyedReplacement>();
        var xml = new XmlDocument(); xml.Load(Path.Combine(root, "Languages/English/Keyed/FireModes.xml"));
        foreach (XmlNode node in xml.DocumentElement!.ChildNodes)
            if (node.NodeType == XmlNodeType.Element) language.keyedReplacements[node.Name] = new LoadedLanguage.KeyedReplacement { key = node.Name, value = node.InnerText.Replace("\\n", "\n") };
        LanguageDatabase.activeLanguage = language;
        var content = (ModContentPack)RuntimeHelpers.GetUninitializedObject(typeof(ModContentPack));
        Set(typeof(ModContentPack), "rootDirInt", content, new DirectoryInfo(qa));
        content.assemblies = new ModAssemblyHandler(content);
        content.assemblies.loadedAssemblies.AddRange(new[] { typeof(Verb_ArcSprayProjectile).Assembly, typeof(ArcFireModes).Assembly, typeof(Building_WeaponStation).Assembly });
        LoadedModManager.RunningModsListForReading.Add(content); GenTypes.ClearCache();
        map = new Map(); Set(typeof(Map), "info", map, new MapInfo { Size = new IntVec3(200, 1, 200) }); Current.Game.Maps.Add(map);
        map.lordManager = (Verse.AI.Group.LordManager)RuntimeHelpers.GetUninitializedObject(typeof(Verse.AI.Group.LordManager));
        map.lordManager.lords = new List<Verse.AI.Group.Lord>();
        projectile = new ThingDef { defName = "QAProjectile", thingClass = typeof(Projectile), projectile = new ProjectileProperties { flyOverhead = true } };
        var h = new Harmony("qa.boundary");
        selector = new Selector(); Hook(h, typeof(Find), "get_Selector", nameof(SelectorBoundary));
        Hook(h, typeof(Log), "Error", nameof(Error), new[] { typeof(string) });
        Hook(h, typeof(Log), "Message", nameof(Message), new[] { typeof(string) });
        Hook(h, typeof(Verb), "TryFindShootLineFromTo", nameof(Line));
        Hook(h, typeof(GenSpawn), "Spawn", nameof(Spawn), new[] { typeof(ThingDef), typeof(IntVec3), typeof(Map), typeof(WipeMode) });
        Hook(h, typeof(Projectile), "Launch", nameof(Launch), new[] { typeof(Thing), typeof(Vector3), typeof(LocalTargetInfo), typeof(LocalTargetInfo), typeof(ProjectileHitFlags), typeof(bool), typeof(Thing), typeof(ThingDef) });
        Hook(h, typeof(Thing), "get_DrawPos", nameof(Draw)); Hook(h, typeof(Pawn), "get_DrawPos", nameof(Draw));
        Hook(h, typeof(StatExtension), "GetStatValue", nameof(Stat), new[] { typeof(Thing), typeof(StatDef), typeof(bool), typeof(int) });
        Hook(h, typeof(CoverUtility), "CalculateCoverGiverSet", nameof(Covers));
        Hook(h, typeof(CoverUtility), "CalculateOverallBlockChance", nameof(Block));
        Hook(h, typeof(GridsUtility), "Roofed", nameof(True), new[] { typeof(IntVec3), typeof(Map) });
        Hook(h, typeof(GasUtility), "AnyGas", nameof(False), new[] { typeof(IntVec3), typeof(Map), typeof(GasType) });
        Hook(h, typeof(DefOfHelper), "EnsureInitializedInCtor", nameof(Skip));
        Hook(h, typeof(ShotReport), "HitReportFor", nameof(Report), postfix: true);
        Hook(h, typeof(ShootLine), "ChangeDestToMissWild", nameof(Wild), postfix: true);
        Hook(h, typeof(EffecterDef), "Spawn", nameof(Fx), new[] { typeof(IntVec3), typeof(IntVec3), typeof(Map), typeof(float) });
        Hook(h, typeof(Thing), "GetGizmos", nameof(EmptyGizmos));
        Hook(h, typeof(Pawn_EquipmentTracker), "GetGizmos", nameof(EmptyGizmos)); // equipment UI hotkeys need the live selector; base Pawn iterator stays real.
        Hook(h, typeof(Thing), "ExposeData", nameof(Skip)); // base Thing serialization needs full Def/load-reference catalog; keep real patched ThingWithComps/Scribe.
        Hook(h, typeof(ThingOwnerUtility), "ShouldRemoveDesignationsOnAddedThings", nameof(False));
        Hook(h, typeof(FleckMaker), "Static", nameof(Muzzle), new[] { typeof(IntVec3), typeof(Map), typeof(FleckDef), typeof(float) });
        Hook(h, typeof(Verse.Sound.SoundStarter), "PlayOneShot", nameof(Sound), new[] { typeof(SoundDef), typeof(Verse.Sound.SoundInfo) });
        Hook(h, typeof(Verse.Sound.SoundStarter), "PlayOneShotOnCamera", nameof(SoundCamera), new[] { typeof(SoundDef), typeof(Map) });
        foreach (string name in new[] { "ShootingAccuracyPawn", "ShootingAccuracyTurret", "ShootingAccuracyFactor_Touch", "ShootingAccuracyFactor_Short", "ShootingAccuracyFactor_Medium", "ShootingAccuracyFactor_Long", "AccuracyTouch", "AccuracyShort", "AccuracyMedium", "AccuracyLong" })
            AccessTools.Field(typeof(StatDefOf), name).SetValue(null, new StatDef { defName = name });
        Rand.PushState(92843);
    }

    private static (ThingWithComps Gun, Verb_ArcSprayProjectile Verb) Gun(string name, VerbProperties? properties = null)
    {
        properties ??= new VerbProperties { verbClass = typeof(Verb_ArcSprayProjectile), burstShotCount = 18, ticksBetweenBurstShots = 2, sprayWidth = 5, sprayThicknessCells = 3, sprayArching = 4, range = 60, minRange = 3.9f, canGoWild = true };
        properties.defaultProjectile = projectile;
        var def = new ThingDef { defName = name, label = name, category = ThingCategory.Item, thingClass = typeof(ThingWithComps), equipmentType = EquipmentType.Primary };
        Set(typeof(ThingDef), "verbs", def, new List<VerbProperties> { properties });
        var gun = new ThingWithComps { def = def, thingIDNumber = id++, stackCount = 1 };
        var equippable = new CompEquippable { parent = gun, props = new CompProperties(typeof(CompEquippable)) };
        var verb = new Verb_ArcSprayProjectile { verbProps = properties, verbTracker = equippable.verbTracker };
        Set(typeof(VerbTracker), "verbs", equippable.verbTracker, new List<Verb> { verb });
        Set(typeof(ThingWithComps), "comps", gun, new List<ThingComp> { equippable });
        weaponAccuracy[gun] = 1;
        return (gun, verb);
    }

    private static T Turret<T>(ThingWithComps gun) where T : Building_TurretGun
    {
        var host = (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
        host.def = new ThingDef { defName = "DMS_Turret_GateKeeper", label = "Gatekeeper", category = ThingCategory.Building, thingClass = typeof(T), size = new IntVec2(3, 3), building = new BuildingProperties { neverBuildable = true } };
        host.gun = gun; host.thingIDNumber = id++;
        Set(typeof(Building_Turret), "forcedTarget", host, LocalTargetInfo.Invalid);
        Set(typeof(Building_TurretGun), "currentTargetInt", host, LocalTargetInfo.Invalid);
        Set(typeof(Thing), "positionInt", host, new IntVec3(40, 0, 40)); Set(typeof(Thing), "mapIndexOrState", host, (sbyte)0);
        Set(typeof(Thing), "factionInt", host, player); Set(typeof(ThingWithComps), "comps", host, null);
        foreach (Verb verb in gun.GetComp<CompEquippable>().AllVerbs) verb.caster = host;
        return host;
    }
    private static Building_TurretGun Turret(ThingWithComps gun) => Turret<Building_TurretGun>(gun);

    private static Pawn PawnWith(ThingWithComps gun)
    {
        var pawn = (Pawn)RuntimeHelpers.GetUninitializedObject(typeof(Pawn));
        pawn.def = new ThingDef { defName = "QAMech", race = new RaceProperties { intelligence = Intelligence.ToolUser }, category = ThingCategory.Pawn, thingClass = typeof(Pawn), size = IntVec2.One };
        pawn.thingIDNumber = id++; Set(typeof(Thing), "factionInt", pawn, player);
        pawn.health = new Pawn_HealthTracker(pawn);
        Set(typeof(Thing), "positionInt", pawn, new IntVec3(40, 0, 40)); Set(typeof(Thing), "mapIndexOrState", pawn, (sbyte)0);
        Set(typeof(ThingWithComps), "comps", pawn, null);
        pawn.equipment = new Pawn_EquipmentTracker(pawn); pawn.equipment.GetDirectlyHeldThings().TryAdd(gun);
        foreach (Verb verb in gun.GetComp<CompEquippable>().AllVerbs) verb.caster = pawn;
        return pawn;
    }

    private static void Begin(Verb_ArcSprayProjectile verb, int shotIndex = 4)
    {
        verb.state = VerbState.Bursting;
        Set(typeof(Verb), "currentTarget", verb, new LocalTargetInfo(new IntVec3(40, 0, 70)));
        Set(typeof(Verb), "burstShotsLeft", verb, verb.BurstShotCount - shotIndex);
        prepare.Invoke(verb, null); launches.Clear(); reports.Clear();
    }
    private static bool Cast(Verb verb) => (bool)shoot.Invoke(verb, null)!;

    private static void ValidateDirect(ThingWithComps gun, Verb_ArcSprayProjectile verb)
    {
        Begin(verb);
        Check(verb.AimAngleOverride == null && ArcFireModes.IsDirect(verb), "player default direct / no spray angle");
        Check(Cast(verb), "real reverse shot executes without invalid cast");
        Check(reports.Count == 1 && ReferenceEquals(reports[0], verb), "real ShotReport receives ORIGINAL spray verb");
        Check(launches.Single().Used.Cell == verb.CurrentTarget.Cell && launches[0].Intended == verb.CurrentTarget, "direct aims at currentTarget");
        Check(ReferenceEquals(launches[0].Equipment, gun), "real gun retained for projectile attribution");
        int high = Sample(verb, 1f, 1f), low = Sample(verb, 0.85f, 0.2f);
        Check(high == 400 && low < 40 && wildCalls > 300, $"precision distribution high={high}/400 low={low}/400 wild={wildCalls}");
        foreach (int distance in new[] { 2, 10, 20, 35, 50 })
        {
            Set(typeof(Verb), "currentTarget", verb, new LocalTargetInfo(new IntVec3(40, 0, 40 + distance)));
            shooterAccuracy = .98f; weaponAccuracy[gun] = .65f;
            float actual = ShotReport.HitReportFor(verb.caster, verb, verb.CurrentTarget).AimOnTargetChance_StandardTarget;
            Check(Math.Abs(actual - Mathf.Max(.0201f, Mathf.Pow(.98f, distance) * .65f)) < .00001f, "real weapon accuracy and ShootingAccuracyTurret at " + distance);
        }
        shooterAccuracy = 1; weaponAccuracy[gun] = 1;
        var target = new Thing { def = new ThingDef { defName = "QATarget", category = ThingCategory.Building, building = new BuildingProperties { turretGunDef = gun.def }, useHitPoints = true, fillPercent = 0.4f, size = IntVec2.One } };
        Set(typeof(Thing), "positionInt", target, new IntVec3(40, 0, 70)); Set(typeof(Thing), "mapIndexOrState", target, (sbyte)0);
        cover = new Thing { def = new ThingDef { defName = "QACover" } }; Set(typeof(Thing), "positionInt", cover, new IntVec3(40, 0, 69));
        Set(typeof(Verb), "currentTarget", verb, new LocalTargetInfo(target)); blockChance = 1;
        launches.Clear(); Check(Cast(verb) && launches.Single().Used.Thing == cover, "PassCoverChance / GetRandomCoverToMissInto chooses real cover");
        blockChance = 0; cover = null;
        Set(typeof(Verb), "currentTarget", verb, new LocalTargetInfo(new IntVec3(40, 0, 70)));
        Set(typeof(VerbProperties), "forcedMissRadius", verb.verbProps, 4f); verb.verbProps.forcedMissEvenDispersal = true;
        launches.Clear(); int reportBefore = reports.Count;
        for (int i = 0; i < 18; i++) Check(Cast(verb), "forced miss shot");
        Check(launches.Select(l => l.Used.Cell).Distinct().Count() > 3 && reports.Count - reportBefore < 18, "vanilla >.5 forced-radius/even-dispersal branch executes");
        verb.Reset(); Set(typeof(VerbProperties), "forcedMissRadius", verb.verbProps, 0f); Begin(verb);
        los = false; verb.verbProps.stopBurstWithoutLos = true; launches.Clear();
        Check(!Cast(verb) && launches.Count == 0, "lost LOS stops shot"); los = true;
        Console.WriteLine($"PASS direct: original receiver ShotReport + high/low accuracy {high}/{low} per 400; wild, cover, forced-radius and LOS branches real.");
    }

    private static int Sample(Verb_ArcSprayProjectile verb, float shooter, float equipment)
    {
        shooterAccuracy = shooter; weaponAccuracy[verb.EquipmentSource] = equipment; launches.Clear(); reports.Clear(); wildCalls = 0;
        for (int i = 0; i < 400; i++) Check(Cast(verb), "sample fires");
        return launches.Count(l => (l.Flags & ProjectileHitFlags.IntendedTarget) != 0);
    }

    private static void ValidateSpray(ThingWithComps gun, Verb_ArcSprayProjectile verb)
    {
        ArcFireModes.SetChoice(gun, FireMode.Spray); Begin(verb);
        Check(verb.AimAngleOverride.HasValue, "spray angle preserved");
        List<IntVec3> path = (List<IntVec3>)AccessTools.Field(typeof(Verb_Spray), "path").GetValue(verb)!;
        Check(path.Count == 18 && path.First() == verb.CurrentTarget.Cell && path.Last() == verb.CurrentTarget.Cell && path.Distinct().Count() > 10, "FFF zigzag path remains original");
        int[] signs = path.Select(cell => Math.Sign(cell.x - 40)).Where(s => s != 0).ToArray();
        Check(signs.Zip(signs.Skip(1), (a, b) => a != b).Count(change => change) == 3, "three side-to-side zigzag traversals");
        for (int i = 0; i < 18; i++)
        {
            Set(typeof(Verb), "burstShotsLeft", verb, 18 - i);
            Check(verb.AimAngleOverride.HasValue && Cast(verb), "spray shot and angle");
            Check(launches.Last().Used.Cell == path[i] && launches.Last().Intended.Cell == path[i], "original FFF HitCell destination");
        }
        Check(reports.Count == 0, "DMS mode bypasses ShotReport just like upstream");
        Console.WriteLine("PASS spray: actual FFF PreparePath/BuildZigzagPath/HitCell, 18 distributed cells, three zigzag traversals, original AimAngleOverride.");
    }

    private static void ValidateSwitching(ThingWithComps gun, Verb_ArcSprayProjectile verb)
    {
        Begin(verb, 0);
        for (int i = 0; i < 18; i++)
        {
            ArcFireModes.SetChoice(gun, i % 2 == 0 ? FireMode.Direct : FireMode.Spray);
            Set(typeof(Verb), "burstShotsLeft", verb, 18 - i);
            Check(verb.AimAngleOverride.HasValue == (i % 2 != 0) && Cast(verb), "mid-burst both directions safe");
        }
        Check(launches.Count == 18 && reports.Count == 9, "mode changed each next shot, same burst count");
        Console.WriteLine("PASS transition: all 18 burst indices alternate direct/spray with safe aim and unchanged path.");
    }

    private static void ValidateGizmosAndFactions(string workshop)
    {
        foreach (string name in new[] { "DMS_M214Gatlinggun", "DMS_GAU19Gatlinggun", "DMS_GAU4Gatlinggun_Hold", "DMS_GAU4Gatlinggun", "DMS_GAU8Gatlinggun", "DMS_Gsh30Gatlinggun", "DMS_Gun_GateKeeper" })
        {
            string file = name == "DMS_Gsh30Gatlinggun"
                ? Path.Combine(workshop, "3469398006/1.7/Defs/ThingDef_Weapon/MilitaryGun_Heavy.xml")
                : Path.Combine(workshop, "3819799478/1.7/Defs", name == "DMS_Gun_GateKeeper" ? "Things_Building/DMS_Security.xml" : "Things_Weapon/MilitaryGun_Gatling.xml");
            var xml = new XmlDocument(); xml.Load(file);
            XmlNode node = xml.SelectSingleNode($"Defs/ThingDef[defName='{name}']/verbs/li[verbClass='Fortified.Verb_ArcSprayProjectile']") ?? throw new InvalidOperationException("Real XML missing arc verb: " + name);
            var props = new VerbProperties();
            props.verbClass = typeof(Verb_ArcSprayProjectile);
            foreach (string fieldName in new[] { "burstShotCount", "ticksBetweenBurstShots", "sprayWidth", "sprayThicknessCells", "sprayArching", "minRange", "range", "consumeFuelPerShot" })
            {
                if (node[fieldName] == null) continue;
                FieldInfo field = AccessTools.Field(typeof(VerbProperties), fieldName);
                field.SetValue(props, Convert.ChangeType(node[fieldName]!.InnerText, field.FieldType, CultureInfo.InvariantCulture));
            }
            var (gun, verb) = Gun(name, props);
            ThingWithComps host = name == "DMS_Gun_GateKeeper" ? Turret(gun) : PawnWith(gun);
            Check(ArcFireModes.Controls(host).OfType<Command_Action>().Count() == 1, name + " exposes player control");
            Check(baseGizmos(host).OfType<Command_Action>().Count(c => c.groupKey is 173802701 or 173802702) == 1, name + " real patched base UI has exactly one control");
            var command = ArcFireModes.Controls(host).OfType<Command_Action>().Single(); command.action();
            Check(ArcFireModes.GetChoice(gun) == FireMode.Spray && !ArcFireModes.IsDirect(verb), name + " toggles spray");
            ArcFireModes.Controls(host).OfType<Command_Action>().Single().action();
            Check(ArcFireModes.GetChoice(gun) == FireMode.Direct && ArcFireModes.IsDirect(verb), name + " toggles direct");
            Begin(verb, 2); Check(verb.AimAngleOverride == null && Cast(verb) && reports.Count == 1 && ReferenceEquals(reports[0], verb), name + " player Pawn/turret executes real direct shot");
            foreach (Faction? faction in new[] { enemy, new Faction { def = new FactionDef { defName = "QANeutral" } }, null })
            {
                Set(typeof(Thing), "factionInt", host, faction);
                Check(!ArcFireModes.Controls(host).Any() && !ArcFireModes.IsDirect(verb), name + " non-player no gizmo, direct veto");
                Check(!baseGizmos(host).OfType<Command_Action>().Any(c => c.groupKey is 173802701 or 173802702), name + " real patched base UI excludes selected non-player");
                if (host is Building_TurretGun || ReferenceEquals(faction, enemy) && host is Pawn)
                    Check(!host.GetGizmos().OfType<Command_Action>().Any(c => c.groupKey is 173802701 or 173802702), name + " full real selected enemy UI excludes mode control");
                Begin(verb, 2); Check(verb.AimAngleOverride.HasValue && Cast(verb) && reports.Count == 0, name + " non-player executes real DMS spray despite stored Direct");
            }
            Set(typeof(Thing), "factionInt", host, player);
            command.action(); Check(ArcFireModes.GetChoice(gun) == FireMode.Spray, "stale GUI click is idempotent");
            // The patched ThingWithComps hook is inside the REAL Pawn / Turret iterator, before unrelated vanilla UI.
            ArcFireModes.SetChoice(gun, FireMode.Direct);
            Check(host.GetGizmos().OfType<Command_Action>().First(c => c.groupKey == 173802701) != null, name + " real GetGizmos integration");
        }
        var (stationGun, stationVerb) = Gun("StationGun"); var station = Turret<Building_WeaponStation>(stationGun);
        Check(station.GetGizmos().OfType<Command_Action>().First(c => c.groupKey == 173802701) != null, "actual WeaponStation iterator includes control");
        var mannable = new CompMannable { parent = station, props = new CompProperties(typeof(CompMannable)) };
        Set(typeof(ThingWithComps), "comps", station, new List<ThingComp> { mannable });
        var (operatorGun, _) = Gun("OperatorGun"); Pawn operatorPawn = PawnWith(operatorGun);
        Set(typeof(CompMannable), "lastManPawn", mannable, operatorPawn); Set(typeof(CompMannable), "lastManTick", mannable, Find.TickManager.TicksGame);
        Check(ArcFireModes.Controls(station).Any() && ArcFireModes.IsDirect(stationVerb), "player-operated manned mount supported");
        Check(baseGizmos(station).OfType<Command_Action>().Any(c => c.groupKey == 173802701), "real mounted-weapon UI control");
        Set(typeof(Thing), "factionInt", operatorPawn, enemy);
        Check(!ArcFireModes.Controls(station).Any() && !ArcFireModes.IsDirect(stationVerb), "hostile mount operator veto");
        Begin(stationVerb); Check(Cast(stationVerb) && reports.Count == 0, "hostile operator actually sprays");
        Set(typeof(Thing), "factionInt", operatorPawn, player);
        var (secondGun, _) = Gun("SecondGun"); var second = Turret(secondGun);
        Command_Action a = ArcFireModes.Controls(station).OfType<Command_Action>().Single(), b = ArcFireModes.Controls(second).OfType<Command_Action>().Single();
        Check(a.GroupsWith(b), "same modes group in real Command.GroupsWith"); a.action(); b.action();
        Check(ArcFireModes.GetChoice(stationGun) == FireMode.Spray && ArcFireModes.GetChoice(secondGun) == FireMode.Spray, "grouped callbacks unify both weapons");
        ArcFireModes.SetChoice(secondGun, FireMode.Direct);
        Set(typeof(Thing), "factionInt", second, enemy); b.action();
        Check(!ArcFireModes.Controls(second).Any() && ArcFireModes.GetChoice(secondGun) == FireMode.Direct, "stale action cannot mutate newly hostile turret");
        Set(typeof(Thing), "factionInt", station, enemy);
        Check(!baseGizmos(station).OfType<Command_Action>().Any(c => c.groupKey is 173802701 or 173802702) && !ArcFireModes.IsDirect(stationVerb), "enemy station has no control even with player operator");
        Set(typeof(Thing), "factionInt", station, player);
        ThingWithComps previousGun = stationGun;
        station.gun = secondGun; stationVerb.caster = station;
        Check(ArcFireModes.GetChoice(previousGun) == FireMode.Spray && ArcFireModes.GetChoice(secondGun) == FireMode.Direct, "gun-swap choices follow instances, not stations");
        var (fffGun, fffVerb) = Gun("FFFTurretGun"); var fff = Turret<Fortified.Building_TurretCapacity>(fffGun);
        Check(fff.GetGizmos().OfType<Command_Action>().First(c => c.groupKey == 173802701) != null && ArcFireModes.IsDirect(fffVerb), "actual FFF turret subclass inherits mode UI/execution");
        var (ordinary, _) = Gun("OrdinaryGun"); Set(typeof(ThingDef), "verbs", ordinary.def, new List<VerbProperties> { new() { verbClass = typeof(Verb_Shoot) } });
        Check(!ArcFireModes.Controls(Turret(ordinary)).Any(), "ordinary guns unaffected");
        Console.WriteLine("PASS UI/faction: seven actual weapon XML verb fixtures, Pawns/turret/WeaponStation/manned mount; enemy, neutral, factionless and captured-player Direct guns always spray, no controls; real grouping and stale-click gate.");
    }

    private static void ValidateScribe(ThingWithComps gun, string qa)
    {
        // CompEquippable's unrelated verb graph needs a complete loaded world; serialize the same gun via real ThingWithComps.ExposeData with no unrelated comps.
        object comps = AccessTools.Field(typeof(ThingWithComps), "comps").GetValue(gun)!;
        Set(typeof(ThingWithComps), "comps", gun, null);
        foreach (FireMode expected in new[] { FireMode.Spray, FireMode.Direct, FireMode.Automatic })
        {
            ArcFireModes.SetChoice(gun, expected); string file = Path.Combine(qa, "gun-" + expected + ".xml");
            Scribe.saver.InitSaving(file, "Gun"); try { gun.ExposeData(); } finally { Scribe.saver.FinalizeSaving(); }
            ArcFireModes.SetChoice(gun, FireMode.Spray);
            Scribe.loader.InitLoading(file); try { gun.ExposeData(); } finally { Scribe.loader.FinalizeLoading(); }
            Check(ArcFireModes.GetChoice(gun) == expected, "real Scribe round-trip " + expected);
            Check(!File.ReadAllText(file).Contains("WhiteGiver", StringComparison.Ordinal), "no custom save class reference");
        }
        var xml = new XmlDocument(); xml.LoadXml("<OldGun />");
        ArcFireModes.SetChoice(gun, FireMode.Spray); Scribe.mode = LoadSaveMode.LoadingVars; Scribe.loader.curXmlParent = xml.DocumentElement;
        try { gun.ExposeData(); } finally { Scribe.mode = LoadSaveMode.Inactive; }
        Check(ArcFireModes.GetChoice(gun) == FireMode.Automatic, "old save missing key uses automatic default");
        xml.LoadXml("<Gun><whitegiverDmsArcFireMode>999</whitegiverDmsArcFireMode></Gun>");
        Scribe.mode = LoadSaveMode.LoadingVars; Scribe.loader.curXmlParent = xml.DocumentElement;
        try { gun.ExposeData(); } finally { Scribe.mode = LoadSaveMode.Inactive; }
        Check(ArcFireModes.GetChoice(gun) == FireMode.Automatic, "unknown scalar choice falls back to documented default");
        Set(typeof(ThingWithComps), "comps", gun, comps);
        var (restored, _) = Gun("FreshRestoredGun"); Set(typeof(ThingWithComps), "comps", restored, null);
        Scribe.loader.InitLoading(Path.Combine(qa, "gun-Spray.xml"));
        try { restored.ExposeData(); } finally { Scribe.loader.FinalizeLoading(); }
        Check(ArcFireModes.GetChoice(restored) == FireMode.Spray && ArcFireModes.GetChoice(gun) == FireMode.Automatic, "Scribe restores a fresh gun instance without leaking state");
        Console.WriteLine("PASS save: actual patched ThingWithComps.ExposeData and Scribe file round-trip all modes, old-save default, scalar-only uninstall-safe XML.");
    }

    private static void ValidateBurst(ThingWithComps gun, Verb_ArcSprayProjectile verb, Building_TurretGun turret)
    {
        ArcFireModes.SetChoice(gun, FireMode.Direct); shooterAccuracy = 1; weaponAccuracy[gun] = 1;
        var fuel = new CompRefuelable { parent = turret, props = new CompProperties_Refuelable { fuelCapacity = 100 } }; Set(typeof(CompRefuelable), "fuel", fuel, 100f);
        Set(typeof(ThingWithComps), "comps", turret, new List<ThingComp> { fuel });
        var changeable = new CompChangeableProjectile { parent = gun, props = new CompProperties(typeof(CompChangeableProjectile)) };
        changeable.LoadShell(new ThingDef { defName = "QAShell", projectileWhenLoaded = projectile }, 10);
        var reload = new CompApparelReloadable { parent = gun, props = new CompProperties_ApparelReloadable { maxCharges = 10 } }; Set(typeof(CompApparelVerbOwner_Charged), "remainingCharges", reload, 10);
        var comps = (List<ThingComp>)AccessTools.Field(typeof(ThingWithComps), "comps").GetValue(gun)!; comps.Add(changeable); comps.Add(reload);
        verb.verbProps.consumeFuelPerShot = 1; verb.verbProps.consumeFuelPerBurst = 2; verb.verbProps.muzzleFlashScale = 1;
        verb.verbProps.sprayEffecterDef = new EffecterDef(); verb.verbProps.soundCast = new SoundDef(); verb.verbProps.soundCastTail = new SoundDef();
        verb.Reset(); Begin(verb, 0); launches.Clear(); fxCalls = 0; muzzleCalls = 0; soundCalls = 0;
        verb.WarmupComplete();
        Check(launches.Count == 1 && changeable.loadedCount == 10 && reload.RemainingCharges == 10, "first direct shot does not consume burst charges");
        for (int tick = 1; tick <= 34; tick++)
        {
            if (tick == 10) ArcFireModes.SetChoice(gun, FireMode.Spray);
            if (tick == 20) ArcFireModes.SetChoice(gun, FireMode.Direct);
            verb.VerbTick();
        }
        Check(launches.Count == 18 && verb.state == VerbState.Idle, "18 shots exactly / real VerbTick interval 2");
        Check(changeable.loadedCount == 9 && reload.RemainingCharges == 9, "charges consumed exactly once on LAST shot");
        Check(Math.Abs(fuel.Fuel - 80) < .001f, "per-shot 18 + per-burst 2 fuel maintained");
        Check(fxCalls == 18 && muzzleCalls == 18 && soundCalls == 36, $"spray FX/muzzle/audio preserved: {fxCalls}/{muzzleCalls}/{soundCalls}");
        Console.WriteLine("PASS burst: actual WarmupComplete/TryCastNextBurstShot/VerbTick; 18 shots over 34 ticks, mid-burst both switches, last-shot charge 1, fuel 18+2, FX18/muzzle18/audio36.");
    }

    private static void Hook(Harmony h, Type type, string method, string handler, Type[]? args = null, bool postfix = false)
    {
        MethodInfo original = AccessTools.DeclaredMethod(type, method, args) ?? throw new MissingMethodException(type.FullName, method);
        var hook = new HarmonyMethod(typeof(Driver), handler); h.Patch(original, prefix: postfix ? null : hook, postfix: postfix ? hook : null);
    }
    private static bool Line(IntVec3 root, LocalTargetInfo targ, ref ShootLine resultingLine, ref bool __result) { resultingLine = new ShootLine(root, targ.Cell); __result = los; return false; }
    private static bool Spawn(ThingDef def, ref Thing __result) { __result = new Bullet { def = def }; return false; }
    private static bool Launch(LocalTargetInfo usedTarget, LocalTargetInfo intendedTarget, ProjectileHitFlags hitFlags, Thing equipment)
    { launches.Add(new LaunchData(usedTarget, intendedTarget, hitFlags, equipment)); return false; }
    private static bool Draw(Thing __instance, ref Vector3 __result) { __result = __instance.Position.ToVector3Shifted(); return false; }
    private static bool Stat(Thing thing, StatDef stat, ref float __result)
    {
        __result = stat == StatDefOf.ShootingAccuracyPawn || stat == StatDefOf.ShootingAccuracyTurret ? shooterAccuracy
            : stat == StatDefOf.AccuracyTouch || stat == StatDefOf.AccuracyShort || stat == StatDefOf.AccuracyMedium || stat == StatDefOf.AccuracyLong ? weaponAccuracy.GetValueOrDefault(thing, 1) : 1f;
        return false;
    }
    private static bool Covers(ref List<CoverInfo> __result) { __result = cover == null ? new List<CoverInfo>() : new List<CoverInfo> { new(cover, blockChance) }; return false; }
    private static bool Block(ref float __result) { __result = blockChance; return false; }
    private static bool True(ref bool __result) { __result = true; return false; }
    private static bool False(ref bool __result) { __result = false; return false; }
    private static bool Skip() => false;
    private static bool Error(string text) { errors++; Console.WriteLine("GAME ERROR " + text); return false; }
    private static bool Message(string text) { Console.WriteLine("GAME LOG " + text); return false; }
    private static void Report(Verb verb) => reports.Add(verb);
    private static void Wild() => wildCalls++;
    private static bool Fx() { fxCalls++; return false; }
    private static bool Muzzle() { muzzleCalls++; return false; }
    private static bool Sound() { soundCalls++; return false; }
    private static bool SoundCamera() { soundCalls++; return false; }
    private static bool EmptyGizmos(ref IEnumerable<Gizmo> __result) { __result = Array.Empty<Gizmo>(); return false; }
    private static bool SelectorBoundary(ref Selector __result) { __result = selector; return false; }
    private static void Set(Type type, string name, object? instance, object? value) => AccessTools.Field(type, name).SetValue(instance, value);
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException("FAIL: " + message); checks++; }
}
