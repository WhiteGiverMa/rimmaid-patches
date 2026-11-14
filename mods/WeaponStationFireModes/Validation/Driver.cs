using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Xml;
using System.Globalization;
using System.Diagnostics;
using Cecil = Mono.Cecil;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using VFM_VanillaFireModes;
using VFM_VanillaFireModes.Settings;
using WeaponStation;
using WhiteGiver.WeaponStationFireModes;
using VFM_VanillaFireModes.Settings.CustomWeaponProfile;
using VFM_VanillaFireModes.Utilities;

internal static class Driver
{
    private static Faction player = null!;
    private static int nextId = 100;

    internal static void Run(string mode, string game, string workshop, string modRoot, Assembly patch, string qa)
    {
        Setup(game, qa, patch, modRoot, workshop);
        if (mode == "ce")
        {
            Set(typeof(ModsConfig), "activeModsHashSet", null, new HashSet<string> { "ceteam.combatextended" });
            Activator.CreateInstance(patch.GetType("WhiteGiver.WeaponStationFireModes.StationFireModesMod", true)!, new object?[] { null });
            Check(!Harmony.GetAllPatchedMethods().Any(m => Harmony.GetPatchInfo(m)?.Owners.Contains("whitegiver.weaponstation.firemodes") == true), "CE must not register the vanilla adapter.");
            var ceXml = StationXml(workshop);
            ValidatePatchXml(ceXml, modRoot);
            Building_WeaponStation disabled = NewStation(ceXml, false);
            Attach(disabled, NewGun(game, workshop));
            var dormant = disabled.GetComp<CompStationFireModes>();
            dormant.Mode = VFM_FireMode.Precision;
            Check(!dormant.CompGetGizmosExtra().Any() && dormant.CompInspectStringExtra() == null, "CE no-op leaked UI.");
            Near(dormant.GetStatOffset(StatDefOf.AimingDelayFactor), 0, "CE no-op gear callback");
            Near(dormant.GetStatFactor(StatDefOf.AimingDelayFactor), 1, "CE no-op mode callback");
            Console.WriteLine("PASS: CE configuration rejects all adapter patches and disables comp UI, gear and mode callbacks.");
            return;
        }
        XmlDocument defs = StationXml(workshop);
        if (mode == "red")
        {
            var station = NewStation(defs, false);
            ThingWithComps gun = NewGun(game, workshop);
            Attach(station, gun);
            Near(station.GetStatValue(StatDefOf.ShootingAccuracyTurret), 0.96f, "Unpatched shooter accuracy");
            Near(station.WeaponWarmupTime, 3.5f, "Unpatched aim time");
            Near(Cooldown(station), 2.3f, "Unpatched cooldown");
            Check(station.AttackVerb.BurstShotCount == 5, "Unpatched REAPR burst differs.");
            Console.WriteLine("RED: real WeaponStation, REAPR verb and StatWorker ignore the equipped +0.4/-10%; accuracy=.96, aim=3.5s, cooldown=2.3s, burst=5.");
            return;
        }
        ValidatePatchXml(defs, modRoot);
        ValidateBehavior(game, workshop, modRoot, patch, qa, defs);
        ValidateIlGuards(patch);
        Console.WriteLine("NOT TESTED: actual game rendering, OS mouse input, projectile flight/impact, full-map AI or loading the user's save.");
    }

    private static void Setup(string gameRoot, string qa, Assembly patch, string modRoot, string workshop)
    {
        Current.ProgramState = ProgramState.Entry;
        Current.Game = (Game)RuntimeHelpers.GetUninitializedObject(typeof(Game));
        Set(typeof(Game), "maps", Current.Game, new List<Map>());
        Set(typeof(Game), "tickManager", Current.Game, new TickManager());
        Set(typeof(Game), "uniqueIDsManager", Current.Game, new UniqueIDsManager());
        Set(typeof(Game), "battleLog", Current.Game, new BattleLog());
        Current.Game.questManager = new QuestManager();
        player = new Faction { def = new FactionDef { defName = "ValidationPlayer", isPlayer = true } };
        Current.Game.InitData = new GameInitData { playerFaction = player };
        Set(typeof(ModsConfig), "activeModsHashSet", null, new HashSet<string>
        {
            "brrainz.harmony", "vortex.weaponstation", "aliza.vanillafiremodes", "whitegiver.weaponstation.firemodes"
        });
        string sandbox = Path.Combine(qa, "save-data");
        Directory.CreateDirectory(Path.Combine(sandbox, "Config"));
        Set(typeof(GenFilePaths), "saveDataPath", null, sandbox);
        Set(typeof(Prefs), "data", null, RuntimeHelpers.GetUninitializedObject(typeof(PrefsData)));
        var language = (LoadedLanguage)RuntimeHelpers.GetUninitializedObject(typeof(LoadedLanguage));
        Set(typeof(LoadedLanguage), "workerInt", language, new LanguageWorker_English());
        Set(typeof(LoadedLanguage), "dataIsLoaded", language, true);
        language.keyedReplacements = new Dictionary<string, LoadedLanguage.KeyedReplacement>();
        foreach (string key in new[] { "VFM_DefaultMode", "VFM_PrecisionMode", "VFM_ShortBurstMode", "VFM_SuppressionMode", "VFM_AutoSelection", "VFM_AutoSelection_Desc", "WSFM_StationModeDescription", "Nothing" })
            language.keyedReplacements[key] = new LoadedLanguage.KeyedReplacement { key = key, value = key };
        foreach (string path in new[] { Path.Combine(modRoot, "Languages/English/Keyed/WeaponStationFireModes.xml"),
            Path.Combine(workshop, "3662471742/1.7/Languages/English/Keyed/VFM_Labels.xml"),
            Path.Combine(workshop, "3807251471/Languages/English/Keyed/WeaponStation.xml") })
        {
            var translations = new XmlDocument();
            translations.Load(path);
            foreach (XmlNode entry in translations.DocumentElement!.ChildNodes)
                if (entry.NodeType == XmlNodeType.Element)
                    language.keyedReplacements[entry.Name] = new LoadedLanguage.KeyedReplacement { key = entry.Name, value = entry.InnerText.Replace("\\n", "\n") };
        }
        LanguageDatabase.activeLanguage = language;
        var content = (ModContentPack)RuntimeHelpers.GetUninitializedObject(typeof(ModContentPack));
        Set(typeof(ModContentPack), "rootDirInt", content, new DirectoryInfo(qa));
        content.assemblies = new ModAssemblyHandler(content);
        content.assemblies.loadedAssemblies.AddRange(new[] { patch, typeof(Building_WeaponStation).Assembly, typeof(VanillaFireModes).Assembly });
        LoadedModManager.RunningModsListForReading.Add(content);
        GenTypes.ClearCache();
        foreach (string name in new[] { "VortexWS_InstallWeaponDesignation", "VortexWS_UninstallWeaponDesignation" })
            DefDatabase<DesignationDef>.Add(new DesignationDef { defName = name });
        VanillaFireModes.settings = new VanillaFireModesModSetting();
        LoadStats(gameRoot);
        RuntimeHelpers.RunClassConstructor(typeof(VFM_StatPatcher).TypeHandle);
    }

    private static void LoadStats(string game)
    {
        foreach ((string file, string[] names) in new[]
        {
            ("Stats_Basics_General.xml", new[] { "ShootingAccuracyTurret", "MaxHitPoints" }),
            ("Stats_Pawns_Combat.xml", new[] { "ShootingAccuracyPawn", "AimingDelayFactor", "RangedCooldownFactor", "ShootingAccuracyFactor_Touch", "ShootingAccuracyFactor_Short", "ShootingAccuracyFactor_Medium", "ShootingAccuracyFactor_Long", "ShootingAccuracyOutdoorsLitOffset", "ShootingAccuracyOutdoorsDarkOffset", "ShootingAccuracyIndoorsDarkOffset", "ShootingAccuracyIndoorsLitOffset" }),
            ("Stats_Weapons_Ranged.xml", new[] { "AccuracyTouch", "AccuracyShort", "AccuracyMedium", "AccuracyLong", "RangedWeapon_Cooldown", "RangedWeapon_WarmupMultiplier" })
        })
        {
            var document = new XmlDocument();
            document.Load(Path.Combine(game, "Data/Core/Defs/Stats", file));
            // QA fixture has no DLC/content catalog; omit only XML nodes gated on DLCs.
            foreach (XmlNode optional in document.SelectNodes("//*[@MayRequire or @MayRequireAnyOf]")!.Cast<XmlNode>().ToArray())
                optional.ParentNode!.RemoveChild(optional);
            XmlInheritance.Clear();
            foreach (XmlNode node in document.SelectNodes("Defs/StatDef")!) XmlInheritance.TryRegister(node, null);
            XmlInheritance.Resolve();
            foreach (string name in names)
            {
                XmlNode? node = document.SelectSingleNode($"Defs/StatDef[defName='{name}']");
                if (node == null) continue;
                var stat = (StatDef)DirectXmlToObjectNew.DefFromNodeNew(node, null);
                DefDatabase<StatDef>.Add(stat);
                FieldInfo? field = AccessTools.Field(typeof(StatDefOf), name);
                field?.SetValue(null, stat);
            }
            XmlInheritance.Clear();
        }
        foreach (string name in new[] { "ShootingAccuracyOutdoorsLitOffset", "ShootingAccuracyOutdoorsDarkOffset", "ShootingAccuracyIndoorsDarkOffset", "ShootingAccuracyIndoorsLitOffset" })
        {
            if (DefDatabase<StatDef>.GetNamedSilentFail(name) != null) continue;
            var stat = new StatDef { defName = name };
            DefDatabase<StatDef>.Add(stat);
            AccessTools.Field(typeof(StatDefOf), name).SetValue(null, stat);
        }
        foreach (StatDef stat in DefDatabase<StatDef>.AllDefs)
        {
            stat.immutable = false;
            stat.Worker.SetCacheability(false);
        }
        Set(typeof(StatDef), "mutableStats", null, new HashSet<StatDef> { StatDefOf.RangedCooldownFactor });
    }

    private static XmlDocument StationXml(string workshop)
    {
        var document = new XmlDocument();
        document.Load(Path.Combine(workshop, "3807251471/Defs/bundle.xml"));
        OmitUnavailableExtensions(document);
        return document;
    }

    private static void ValidatePatchXml(XmlDocument defs, string root)
    {
        var before = defs.DocumentElement!.ChildNodes.Cast<XmlNode>().Where(n => n.NodeType == XmlNodeType.Element)
            .ToDictionary(n => n["defName"]!.InnerText, n => n.OuterXml);
        var xml = new XmlDocument();
        xml.Load(Path.Combine(root, "Patches/WeaponStation.xml"));
        XmlNode node = xml.SelectSingleNode("Patch/Operation")!;
        Check(defs.SelectNodes(node["xpath"]!.InnerText)!.Count == 1, "XML must match one unmanned station.");
        PatchOperation operation = DirectXmlToObject.ObjectFromXml<PatchOperation>(node, false);
        Check(operation.Apply(defs), "Real PatchOperationAdd failed.");
        foreach (XmlNode def in defs.DocumentElement.ChildNodes)
        {
            if (def.NodeType != XmlNodeType.Element) continue;
            string name = def["defName"]!.InnerText;
            if (name != "VortexWS_WeaponStation") Check(def.OuterXml == before[name], "Unrelated XML changed: " + name);
        }
        Check(defs.SelectNodes("Defs/ThingDef[defName='VortexWS_WeaponStation']/comps/li[@Class='WhiteGiver.WeaponStationFireModes.CompProperties_StationFireModes']")!.Count == 1, "Missing station comp.");
        Console.WriteLine("PASS: actual XML patch adds only one unmanned comp; every manned-mount and other XML node is unchanged.");
    }

    private static Building_WeaponStation NewStation(XmlDocument document, bool manned)
    {
        string name = manned ? "VortexWS_WeaponMount" : "VortexWS_WeaponStation";
        XmlNode xml = document.SelectSingleNode($"Defs/ThingDef[defName='{name}']")!;
        var def = (ThingDef)DirectXmlToObjectNew.DefFromNodeNew(xml, null);
        def.building.turretGunDef = new ThingDef { defName = "VortexWS_EmptyGun" };
        var station = (Building_WeaponStation)RuntimeHelpers.GetUninitializedObject(typeof(Building_WeaponStation));
        station.def = def;
        station.thingIDNumber = nextId++;
        Set(typeof(Thing), "factionInt", station, player);
        var comps = new List<ThingComp>();
        foreach (CompProperties props in def.comps.Where(p => p.compClass == typeof(CompMannable) || p.compClass.FullName == "WhiteGiver.WeaponStationFireModes.CompStationFireModes"))
        {
            var comp = (ThingComp)Activator.CreateInstance(props.compClass)!;
            comp.parent = station;
            comp.Initialize(props);
            comps.Add(comp);
        }
        Set(typeof(ThingWithComps), "comps", station, comps);
        return station;
    }

    private static ThingWithComps NewGun(string game, string workshop)
    {
        var xml = new XmlDocument();
        xml.Load(Path.Combine(workshop, "3285968063/1.7/Vanilla/Defs/Weapons/RangedIndustrial_LMG.xml"));
        OmitUnavailableExtensions(xml);
        var def = (ThingDef)DirectXmlToObjectNew.DefFromNodeNew(xml.SelectSingleNode("Defs/ThingDef[defName='HIDGun_REAPR']")!, null);
        XmlNode original = xml.SelectSingleNode("Defs/ThingDef[defName='HIDGun_REAPR']")!;
        def.statBases = ReadModifiers(original["statBases"]!);
        def.equippedStatOffsets = ReadModifiers(original["equippedStatOffsets"]!);
        def.category = ThingCategory.Item;
        def.thingClass = typeof(ThingWithComps);
        def.equipmentType = EquipmentType.Primary;
        var weapon = new ThingWithComps { def = def, thingIDNumber = nextId++, stackCount = 1 };
        var eq = new CompEquippable { parent = weapon, props = new CompProperties(typeof(CompEquippable)) };
        var verb = new Verb_Shoot { verbProps = def.Verbs[0], verbTracker = eq.verbTracker };
        // Projectile cross-reference is outside the isolated Def catalog; use a real projectile-shaped Def.
        verb.verbProps.defaultProjectile = new ThingDef { defName = "ValidationProjectile", projectile = new ProjectileProperties() };
        Set(typeof(VerbTracker), "verbs", eq.verbTracker, new List<Verb> { verb });
        Set(typeof(ThingWithComps), "comps", weapon, new List<ThingComp> { eq });
        return weapon;
    }

    // Third-party mod extensions (e.g. GTekFunctions.GunDrawExtension) live in assemblies the sandbox never loads;
    // they only affect drawing, so drop exactly those nodes instead of failing the whole def.
    private static readonly HashSet<string> ReportedMissingTypes = new();

    private static void OmitUnavailableExtensions(XmlDocument document)
    {
        foreach (XmlNode node in document.SelectNodes("//*[@Class]")!.Cast<XmlNode>().ToArray())
        {
            string type = node.Attributes!["Class"]!.Value;
            if (GenTypes.GetTypeInAnyAssembly(type) != null) continue;
            if (ReportedMissingTypes.Add(type))
                Console.WriteLine($"QA: omitted {type}, which is not loaded in the sandbox.");
            node.ParentNode!.RemoveChild(node);
        }
    }

    private static List<StatModifier> ReadModifiers(XmlNode node)
    {
        var result = new List<StatModifier>();
        foreach (XmlNode item in node.ChildNodes)
        {
            StatDef? stat = DefDatabase<StatDef>.GetNamedSilentFail(item.Name);
            if (stat != null) result.Add(new StatModifier { stat = stat, value = float.Parse(item.InnerText, CultureInfo.InvariantCulture) });
        }
        return result;
    }

    private static void Attach(Building_WeaponStation station, ThingWithComps gun)
    {
        station.gun = gun;
        foreach (Verb verb in gun.GetComp<CompEquippable>().AllVerbs) verb.caster = station;
    }

    private static float Cooldown(Building_WeaponStation station) => (float)AccessTools.DeclaredMethod(typeof(Building_WeaponStation), "BurstCooldownTime").Invoke(station, null)!;

    private static void ValidateBehavior(string game, string workshop, string root, Assembly patch, string qa, XmlDocument defs)
    {
        const string owner = StationFireModesMod.HarmonyId;
        using var messages = Log.LockMessages();
        var mod = new StationFireModesMod(null!);
        _ = new StationFireModesMod(null!);
        StationStats.Apply();
        StationStats.Apply();
        MethodBase[] targets = Harmony.GetAllPatchedMethods().Where(m => Harmony.GetPatchInfo(m)?.Owners.Contains(owner) == true).ToArray();
        foreach (LogMessage message in Log.Messages) Console.WriteLine("GAME LOG: " + message.text);
        Check(targets.Length == 7, "Expected seven actual adapter patch targets.");
        Check(targets.All(t => Harmony.GetPatchInfo(t)!.Prefixes.Concat(Harmony.GetPatchInfo(t)!.Postfixes).Concat(Harmony.GetPatchInfo(t)!.Transpilers).Count(p => p.owner == owner) == 1), "Duplicate patch after repeated constructor.");
        Check(StatDefOf.ShootingAccuracyTurret.parts.Count(p => p is StationAccuracyPart) == 1, "Accuracy part was duplicated.");
        Console.WriteLine("PASS: actual Mod constructor/Harmony3 registers seven targets once; stat injection is idempotent.");

        Building_WeaponStation station = NewStation(defs, false);
        ThingWithComps gun = NewGun(game, workshop);
        Attach(station, gun);
        CompStationFireModes comp = station.GetComp<CompStationFireModes>() ?? throw new InvalidOperationException("Unmanned comp binding failed.");
        Check(station.GetComp<CompMannable>() == null, "Unmanned station became mannable.");
        Near(station.GetStatValue(StatDefOf.ShootingAccuracyTurret), 0.962f, "REAPR equipped accuracy");
        Near(station.WeaponWarmupTime, 3.15f, "REAPR equipped aim");
        Near(Cooldown(station), 2.3f, "REAPR default cooldown");
        comp.Mode = VFM_FireMode.Precision;
        Near(station.GetStatValue(StatDefOf.ShootingAccuracyTurret), 0.9765f, "REAPR precision accuracy");
        Near(station.WeaponWarmupTime, 3.78f, "REAPR precision aim");
        Near(Cooldown(station), 1.15f, "REAPR precision cooldown");
        ValidateReportAndInspect(station, comp);
        Console.WriteLine("PASS: real StatWorker and WeaponStation getters: REAPR default .962/3.15s/2.3s; precision .9765/3.78s/1.15s.");

        var profile = new VFM_WeaponProfile { defName = gun.def.defName,
            Default = new VFM_FireModeProfile(1.1f, 0.7f, 0.6f, 9),
            Precision = new VFM_FireModeProfile(2f, 1.4f, 0.3f, 7),
            Burst = new VFM_FireModeProfile(0.9f, 0.6f, 0.4f, 3),
            Suppression = new VFM_FireModeProfile(0.4f, 0.5f, 1.3f, 12) };
        VanillaFireModes.settings.CustomWeaponProfiles[gun.def.defName] = profile;
        Near(station.WeaponWarmupTime, 4.41f, "Per-weapon warmup profile");
        Near(Cooldown(station), 0.69f, "Per-weapon cooldown profile");
        float profileAccuracy = StatDefOf.ShootingAccuracyPawn.postProcessCurve.Evaluate(16.8f);
        Near(station.GetStatValue(StatDefOf.ShootingAccuracyTurret), profileAccuracy, "Per-weapon accuracy profile");
        VanillaFireModes.settings.CustomWeaponProfiles.Clear();
        ThingWithComps other = NewGun(game, workshop);
        other.def.equippedStatOffsets.Clear();
        Attach(station, other);
        comp.Mode = VFM_FireMode.Default;
        Near(station.GetStatValue(StatDefOf.ShootingAccuracyTurret), 0.96f, "Swapping removes old offsets immediately");
        Near(station.WeaponWarmupTime, 3.5f, "Swapping removes old warmup offset");
        Attach(station, gun);
        Near(station.GetStatValue(StatDefOf.ShootingAccuracyTurret), 0.962f, "Restoring gun restores offsets");
        var independent = NewStation(defs, false);
        Attach(independent, other);
        independent.GetComp<CompStationFireModes>().Mode = VFM_FireMode.Precision;
        Near(independent.GetStatValue(StatDefOf.ShootingAccuracyTurret), 0.975f, "Independent station precision");
        Near(station.GetStatValue(StatDefOf.ShootingAccuracyTurret), 0.962f, "Mode leaked between stations");
        Console.WriteLine("PASS: live VFM weapon-profile overrides, independent station modes and gun swaps use current gun/configuration without stale stat values.");

        ValidateGizmos(comp, station);
        ValidateScribe(comp, qa);
        ValidateManned(defs, game, workshop, patch);
        ValidateBurstAndAuto(station, comp, gun, profile);
        ValidateNpc(station, comp, gun, profile);
        ValidateHotPath(station, comp, gun);
    }

    private static void ValidateReportAndInspect(Building_WeaponStation station, CompStationFireModes comp)
    {
        Type reportPatch = typeof(Building_WeaponStation).Assembly.GetType("WeaponStation.Patch_StatsReport_TurretGun", true)!;
        MethodInfo prefix = AccessTools.DeclaredMethod(reportPatch, "Prefix");
        MethodInfo finalizer = AccessTools.DeclaredMethod(reportPatch, "Finalizer");
        object?[] args = { station, null };
        prefix.Invoke(null, args);
        try
        {
            Check(!station.HasWeapon, "Upstream StatsReport fixture did not replace turretGunDef.");
            Near(station.GetStatValue(StatDefOf.ShootingAccuracyTurret), 0.9765f, "StatsReport precision accuracy");
            Near(station.WeaponWarmupTime, 3.78f, "StatsReport precision aim");
            Near(Cooldown(station), 1.15f, "StatsReport precision cooldown");
            string? explanation = StatDefOf.ShootingAccuracyTurret.parts.OfType<StationAccuracyPart>().Single()
                .ExplanationPart(StatRequest.For(station));
            Check(explanation?.Contains("97.65", StringComparison.Ordinal) == true, "StatsReport lost adapted accuracy explanation.");
        }
        finally { finalizer.Invoke(null, args); }
        Check(station.HasWeapon, "Upstream report finalizer did not restore empty-gun Def.");
        string info = station.GetInspectString();
        Check(info.Contains("Fire mode: Precision", StringComparison.Ordinal)
            && info.Contains("3.78", StringComparison.Ordinal) && info.Contains("97.65", StringComparison.Ordinal),
            "Actual station inspect text omitted adapted mode/accuracy/aim.");
        Check(info.Split("Fire mode:", StringSplitOptions.None).Length == 2, "Inspect text duplicated mode block.");
        Thing gun = station.gun;
        station.gun = EmptyGun(station.def.building.turretGunDef);
        try { Check(!station.GetInspectString().Contains("Fire mode:", StringComparison.Ordinal), "Empty station exposes mode inspect text."); }
        finally { station.gun = gun; }
        Console.WriteLine("PASS: actual WS StatsReport prefix/finalizer preserves adapted stats and explanation; real GetInspectString displays one mode/accuracy/time block and hides it when empty.");
    }

    private static void ValidateGizmos(CompStationFireModes comp, Building_WeaponStation station)
    {
        comp.Mode = VFM_FireMode.Default;
        comp.AutoSelection = false;
        for (int i = 0; i < 4; i++)
        {
            Gizmo[] gizmos = comp.CompGetGizmosExtra().ToArray();
            Check(gizmos.Length == 2 && gizmos[0] is Command_Action && gizmos[1] is Command_Toggle, "Manual/auto gizmos differ.");
            ((Command_Action)gizmos[0]).action();
            Check((int)comp.Mode == (i + 1) % 4, "Mode button did not cycle.");
        }
        var toggle = comp.CompGetGizmosExtra().OfType<Command_Toggle>().Single();
        toggle.toggleAction();
        Check(toggle.isActive() && comp.CompGetGizmosExtra().Count() == 1, "Auto must hide manual mode.");
        VanillaFireModes.settings.enableAutoSelectionForPlayer = false;
        Check(comp.CompGetGizmosExtra().Single() is Command_Action, "Global auto disabled must restore manual mode.");
        VanillaFireModes.settings.enableAutoSelectionForPlayer = true;
        toggle.toggleAction();
        Thing old = station.gun;
        station.gun = EmptyGun(station.def.building.turretGunDef);
        Check(!comp.CompGetGizmosExtra().Any(), "Empty station exposes firing controls.");
        Near(comp.GetStatOffset(StatDefOf.AimingDelayFactor), 0f, "Empty station has no gear offsets");
        station.gun = old;
        Console.WriteLine("PASS: actual gizmo actions cycle all modes, toggle automatic selection, respect global auto and hide for an empty station.");
    }

    private static void ValidateScribe(CompStationFireModes comp, string qa)
    {
        comp.Mode = VFM_FireMode.Suppression;
        comp.AutoSelection = true;
        string file = Path.Combine(qa, "station-mode.xml");
        Scribe.saver.InitSaving(file, "StationFixture");
        try { comp.PostExposeData(); }
        finally { Scribe.saver.FinalizeSaving(); }
        comp.Mode = VFM_FireMode.Default;
        comp.AutoSelection = false;
        Scribe.loader.InitLoading(file);
        try { comp.PostExposeData(); }
        finally { Scribe.loader.FinalizeLoading(); }
        Check(comp.Mode == VFM_FireMode.Suppression && comp.AutoSelection, "Mode/auto Scribe round-trip failed.");
        foreach (bool initial in new[] { false, true })
        {
            VanillaFireModes.settings.autoModeDefaultOn = initial;
            var xml = new XmlDocument();
            xml.LoadXml("<OldStation />");
            Scribe.mode = LoadSaveMode.LoadingVars;
            Scribe.loader.curXmlParent = xml.DocumentElement;
            try { comp.PostExposeData(); }
            finally { Scribe.mode = LoadSaveMode.Inactive; }
            Check(comp.Mode == VFM_FireMode.Default && comp.AutoSelection == initial, "Old save missing comp keys has incorrect defaults.");
        }
        VanillaFireModes.settings.autoModeDefaultOn = false;
        comp.AutoSelection = false;
        Console.WriteLine("PASS: real Scribe file save/load and pre-comp old-save defaults preserve mode/auto and follow VFM initial-auto setting.");
    }

    private static void ValidateManned(XmlDocument defs, string game, string workshop, Assembly patch)
    {
        Building_WeaponStation mount = NewStation(defs, true);
        Attach(mount, NewGun(game, workshop));
        Check(mount.GetComp<CompStationFireModes>() == null && mount.GetComp<CompMannable>() != null, "Manned mount got autonomous comp.");
        Check(!mount.GetInspectString().Contains("Fire mode:", StringComparison.Ordinal), "Manned mount got autonomous inspect text.");
        Near(mount.WeaponWarmupTime, 3.5f, "Manned mount aim unchanged");
        Near(Cooldown(mount), 2.3f, "Manned mount cooldown unchanged");
        Near(ShotReport.HitFactorFromShooter(mount, 25f), Mathf.Pow(0.96f, 25f), "Empty mount shooter unchanged");
        MethodInfo original = AccessTools.DeclaredMethod(typeof(ShotReport), nameof(ShotReport.HitFactorFromShooter));
        Type upstream = typeof(Building_WeaponStation).Assembly.GetType("WeaponStation.Patch_ShotReport_MannedStationUsesOperator", true)!;
        var harmony = new Harmony("validation.weaponstation.operator");
        harmony.Patch(original, prefix: new HarmonyMethod(AccessTools.DeclaredMethod(upstream, "Prefix")));
        var pawn = (Pawn)RuntimeHelpers.GetUninitializedObject(typeof(Pawn));
        pawn.def = new ThingDef { defName = "ValidationOperator", category = ThingCategory.Pawn, race = new RaceProperties() };
        if (Current.Game.Maps.Count == 0) Current.Game.Maps.Add(new Map());
        float queriedAccuracy = 0.99f;
        var statHook = new Harmony("validation.operator.stats");
        OperatorAccuracy = queriedAccuracy;
        statHook.Patch(AccessTools.DeclaredMethod(typeof(StatWorker), nameof(StatWorker.GetValue), new[] { typeof(Thing), typeof(bool), typeof(int) }),
            prefix: new HarmonyMethod(typeof(Driver), nameof(OperatorStats)));
        Set(typeof(Thing), "mapIndexOrState", pawn, (sbyte)0);
        Set(typeof(CompMannable), "lastManPawn", mount.GetComp<CompMannable>(), pawn);
        Set(typeof(CompMannable), "lastManTick", mount.GetComp<CompMannable>(), Find.TickManager.TicksGame);
        Check(ReferenceEquals(mount.ManningPawn, pawn), "Occupied mount fixture has no active operator.");
        var hitFactor = original.CreateDelegate<HitFactor>();
        float expected = hitFactor(pawn, 25f, null) * 1.2f;
        Near(hitFactor(mount, 25f, null), expected, "Upstream operator precision and 1.2 bonus preserved");
        Near(mount.WeaponWarmupTime, 3.5f, "Occupied mount aim unchanged");
        Check(mount.AttackVerb.BurstShotCount == 5, "Occupied mount burst changed.");
        statHook.UnpatchAll("validation.operator.stats");
        Console.WriteLine("PASS: empty/occupied manned mount excluded; actual upstream operator HitFactorFromShooter redirection and x1.2 bonus preserved (operator stat input isolated).");
    }

    private static float OperatorAccuracy;
    private delegate float HitFactor(Thing caster, float distance, float? accuracy);
    private static bool OperatorStats(StatWorker __instance, Thing thing, ref float __result)
    {
        if (thing is not Pawn) return true;
        var stat = (StatDef)AccessTools.Field(typeof(StatWorker), "stat").GetValue(__instance)!;
        __result = stat == StatDefOf.ShootingAccuracyPawn ? OperatorAccuracy : 1f;
        return false;
    }

    private static void ValidateBurstAndAuto(Building_WeaponStation station, CompStationFireModes comp, ThingWithComps gun, VFM_WeaponProfile profile)
    {
        InstallAttackBoundary();
        Set(typeof(Thing), "mapIndexOrState", station, (sbyte)0);
        Set(typeof(Thing), "positionInt", station, new IntVec3(0, 0, 0));
        station.def.building.playTargetAcquiredSound = false;
        Type upstream = typeof(Building_WeaponStation).Assembly.GetType("WeaponStation.Patch_Turret_Warmup", true)!;
        new Harmony("validation.weaponstation.warmup").Patch(AccessTools.DeclaredMethod(typeof(Building_TurretGun), "TryStartShootSomething"),
            postfix: new HarmonyMethod(AccessTools.DeclaredMethod(upstream, "Postfix")));

        comp.AutoSelection = true;
        foreach (bool forced in new[] { false, true })
        foreach ((int distance, VFM_FireMode mode) in new[] { (40, VFM_FireMode.Precision), (20, VFM_FireMode.Burst), (5, VFM_FireMode.Suppression) })
        {
            station.def.building.turretBurstWarmupTime = new FloatRange(0, 0);
            comp.Mode = VFM_FireMode.Default;
            Target = new LocalTargetInfo(new IntVec3(distance, 0, 0));
            Set(typeof(Building_Turret), "forcedTarget", station, forced ? Target : LocalTargetInfo.Invalid);
            Set(typeof(Building_TurretGun), "currentTargetInt", station, LocalTargetInfo.Invalid);
            station.AttackVerb.Reset();
            FirstBurst = -1;
            station.TryStartShootSomething(true);
            int expected = FireModeDB.GetBurstCount(mode, 5, gun.def.defName);
            Check(comp.Mode == mode && FirstBurst == expected, $"First immediate burst differs: forced={forced} d={distance} mode={comp.Mode}/{mode}, count={FirstBurst}/{expected}, spawned={station.Spawned}, available={station.AttackVerb.Available()}, target={station.CurrentTarget.IsValid}, state={station.AttackVerb.state}.");
            Near(FirstWarmup, 3.15f * FireModeDB.GetWarmup(mode, gun.def.defName), "First immediate mode warmup input");
            Near(station.GetStatValue(StatDefOf.ShootingAccuracyTurret),
                StatDefOf.ShootingAccuracyPawn.postProcessCurve.Evaluate(8.4f * FireModeDB.GetAccuracy(mode, gun.def.defName)), "First immediate mode accuracy");
        }
        station.AttackVerb.Reset();
        station.def.building.turretBurstWarmupTime = new FloatRange(0.25f, 0.75f);
        comp.Mode = VFM_FireMode.Default;
        Target = new LocalTargetInfo(new IntVec3(40, 0, 0));
        Set(typeof(Building_Turret), "forcedTarget", station, Target);
        FirstBurst = -1;
        station.TryStartShootSomething(true);
        Check(FirstBurst == -1 && comp.Mode == VFM_FireMode.Precision, "Normal warmup must select mode before firing.");
        Check((int)AccessTools.Field(typeof(Building_TurretGun), "burstWarmupTicksLeft").GetValue(station)! == 3.78f.SecondsToTicks(), "Actual WS warmup postfix did not use equipped+mode aim time.");
        Console.WriteLine("PASS: actual TryStartShootSomething first attack selects all distance modes before immediate bursts, for both forced targets and automatic targeting; normal warmup uses 3.78s precision aim.");

        comp.AutoSelection = false;
        comp.Mode = VFM_FireMode.Precision;
        Verb verb = station.AttackVerb;
        verb.Reset();
        verb.WarmupComplete();
        Check(FirstBurst == 4 && verb.BurstShotCount == 4, "Precision burst must lock to four shots.");
        comp.Mode = VFM_FireMode.Suppression;
        VanillaFireModes.settings.precisionBurstLinearMultiplier = 1f;
        Check(verb.BurstShotCount == 4, "Changing mode/settings modified an ongoing burst.");
        verb.state = VerbState.Idle;
        verb.VerbTick();
        Check(verb.BurstShotCount == 5, "Burst cache did not clear when idle.");
        VanillaFireModes.settings.precisionBurstLinearMultiplier = 0.8f;
        VanillaFireModes.settings.CustomWeaponProfiles[gun.def.defName] = profile;
        comp.Mode = VFM_FireMode.Precision;
        verb.WarmupComplete();
        Check(FirstBurst == 7 && verb.BurstShotCount == 7, "Per-weapon burst profile ignored.");
        VanillaFireModes.settings.CustomWeaponProfiles.Clear();
        verb.Reset();

        var single = NewFixtureVerb(gun, station, typeof(Verb_Shoot), 1);
        comp.Mode = VFM_FireMode.Suppression;
        single.WarmupComplete();
        Check(FirstBurst == 1, "Single-shot weapon gained global burst shots.");
        var oneUse = NewFixtureVerb(gun, station, typeof(Verb_ShootOneUse), 3);
        oneUse.WarmupComplete();
        Check(FirstBurst == 3 && oneUse.BurstShotCount == 3, "One-use verb gained burst shots.");
        Attach(station, gun);
        Console.WriteLine("PASS: real WarmupComplete/getter/VerbTick locks burst counts, clears idle state, respects live profiles, single-shot and one-use exclusions.");
    }

    private static void ValidateNpc(Building_WeaponStation station, CompStationFireModes comp, ThingWithComps gun, VFM_WeaponProfile profile)
    {
        Set(typeof(Thing), "factionInt", station, new Faction { def = new FactionDef { defName = "ValidationOther" } });
        VanillaFireModes.settings.enableFireModeForNPC = false;
        VanillaFireModes.settings.CustomWeaponProfiles[gun.def.defName] = profile;
        comp.Mode = VFM_FireMode.Default;
        Near(station.GetStatValue(StatDefOf.ShootingAccuracyTurret), 0.962f, "NPC off must ignore even modified default accuracy");
        Near(station.WeaponWarmupTime, 3.15f, "NPC off must ignore modified default warmup");
        Near(Cooldown(station), 2.3f, "NPC off must ignore modified default cooldown");
        station.AttackVerb.Reset();
        FirstBurst = -1;
        station.AttackVerb.WarmupComplete();
        Check(FirstBurst == 5, "NPC off must ignore even modified default profile burst.");
        Check(!comp.CompGetGizmosExtra().Any(), "Non-player station exposes player gizmos.");
        VanillaFireModes.settings.enableFireModeForNPC = true;
        Near(station.WeaponWarmupTime, 3.15f * 0.7f, "NPC enable applies profile again");
        VanillaFireModes.settings.CustomWeaponProfiles.Clear();
        Set(typeof(Thing), "factionInt", station, player);
        Console.WriteLine("PASS: disabling NPC fire modes bypasses all mode effects, including non-neutral Default profiles, while preserving gear offsets.");
    }

    // 1.1.0 hot-path proof. Verb.VerbTick and the BurstShotCount getter run for every verb of every pawn, animal,
    // mech and turret on the map, while only a station's own verb can ever hold a burst lock. The RED measurement
    // swaps the 1.0.0 cleanup/getter bodies into the shipped patch stubs, so both numbers share the same Harmony
    // trampolines, the same vanilla method bodies and the same 96 unrelated verbs.
    private const int PerfVerbs = 96;
    private const int PerfCalls = 60000;
    private const int PerfRounds = 3;

    private sealed class LegacyBurst
    {
        internal bool Valid;
    }

    private static ConditionalWeakTable<Verb, LegacyBurst>? legacyTable;
    private static int legacyLookups;

    private static void ValidateHotPath(Building_WeaponStation station, CompStationFireModes comp, ThingWithComps gun)
    {
        station.AttackVerb.Reset();
        station.AttackVerb.VerbTick();
        WaitForBurstLocks(0);

        List<Verb> unrelated = NewUnrelatedVerbs(gun);
        TimeUnrelatedVerbCalls(unrelated);
        double legacyNs = TimeWithBodies(unrelated, "legacy", nameof(LegacyCleanup), nameof(LegacyBurstCount), true, out int legacyCalls);
        int expectedCalls = PerfCalls * 2 * PerfRounds;
        Check(legacyCalls == expectedCalls, $"The 1.0.0 bodies must hash on every call, saw {legacyCalls} of {expectedCalls}.");
        Console.WriteLine($"RED: 1.0.0 CleanupBurst/BurstCount bodies under the shipped stubs hash ConditionalWeakTable<Verb, BurstState> on every non-station verb ({legacyCalls} lookups for {PerfCalls * PerfRounds} VerbTick+BurstShotCount rounds x2, {legacyNs:F2} ns/call).");

        long lookups = StationFireModesDiagnostics.BurstTableLookups;
        long locks = StationFireModesDiagnostics.BurstTableLocks;
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        double greenNs = TimeUnrelatedVerbCalls(unrelated);
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
        Check(StationFireModesDiagnostics.BurstTableLookups == lookups,
            $"Non-station verbs reached the burst table {StationFireModesDiagnostics.BurstTableLookups - lookups} times.");
        Check(StationFireModesDiagnostics.BurstTableLocks == locks,
            $"Non-station verbs created {StationFireModesDiagnostics.BurstTableLocks - locks} burst states.");
        Check(allocated == 0, $"VerbTick/BurstShotCount allocated {allocated} B on non-station verbs.");
        Check(greenNs < legacyNs * 0.9f, $"1.1.0 bodies ({greenNs:F2} ns/call) must beat 1.0.0 ({legacyNs:F2} ns/call) by at least 10%.");

        // Floor of the measurement: the same stubs with both bodies replaced by `return false`.
        double emptyNs = TimeWithBodies(unrelated, "empty", nameof(EmptyCleanup), nameof(EmptyBurstCount), false, out _);
        Console.WriteLine($"GREEN: the same 96 colonist, melee and ordinary-turret verbs reach the table 0 times, create 0 states and allocate {allocated} B in {PerfCalls} VerbTick+BurstShotCount calls ({greenNs:F2} ns/call; 1.0.0 was {legacyNs:F2}, so {legacyNs - greenNs:F2} ns/call or {(legacyNs - greenNs) / legacyNs:P0} of the per-call cost is gone, ~{(legacyNs - greenNs) * 10000 / 1000000:F3} ms/frame at the ~10,000 VerbTick calls/frame reported by Dubs Performance Analyzer).");
        Console.WriteLine($"INFO: replacing both bodies with an empty stub only reaches {emptyNs:F2} ns/call, so the rest of the 1.1.0 figure is Harmony's trampoline plus vanilla VerbTick/BurstShotCount; the two guards and their call frames add about {greenNs - emptyNs:F2} ns/call.");

        lookups = StationFireModesDiagnostics.BurstTableLookups;
        station.AttackVerb.VerbTick();
        _ = station.AttackVerb.BurstShotCount;
        Check(StationFireModesDiagnostics.BurstTableLookups == lookups, "An idle station touched the burst table while no burst lock was active.");
        Console.WriteLine("PASS: idle station verbs leave after the lock counter read and add no table lookups.");

        comp.Mode = VFM_FireMode.Precision;
        station.AttackVerb.Reset();
        Check(StationFireModesDiagnostics.ActiveBurstLocks == 0, "Colony reports a live burst lock while nothing fires.");
        station.AttackVerb.WarmupComplete();
        Check(StationFireModesDiagnostics.ActiveBurstLocks == 1, "A station burst did not register exactly one lock.");
        Check(station.AttackVerb.BurstShotCount == 4, "Precision burst lock no longer overrides the getter.");
        station.AttackVerb.Reset();
        station.AttackVerb.VerbTick();
        Check(StationFireModesDiagnostics.ActiveBurstLocks == 0, "Idle VerbTick did not release the station burst lock.");

        LockAndAbandonBurst(gun, station);
        Check(StationFireModesDiagnostics.ActiveBurstLocks == 1, "An abandoned burst did not stay counted before collection.");
        WaitForBurstLocks(0);
        Console.WriteLine("PASS: a verb that dies mid-burst releases its lock through the BurstState finalizer, so the counter returns to zero instead of pinning the fast path open.");
    }

    private static double TimeWithBodies(List<Verb> verbs, string label, string cleanup, string count, bool seedTable, out int lookups)
    {
        Type patches = typeof(StationFireModesMod).Assembly.GetType("WhiteGiver.WeaponStationFireModes.StationPatches", true)!;
        var harmony = new Harmony("validation.weaponstation.body." + label);
        harmony.Patch(AccessTools.DeclaredMethod(patches, "CleanupBurst"),
            prefix: new HarmonyMethod(typeof(Driver), cleanup));
        harmony.Patch(AccessTools.DeclaredMethod(patches, "BurstCount"),
            prefix: new HarmonyMethod(typeof(Driver), count));
        try
        {
            if (seedTable)
            {
                legacyTable = new ConditionalWeakTable<Verb, LegacyBurst>();
                // A live colony always has a populated table: every station verb that ever fired left an entry.
                for (int i = 0; i < 32; i++) legacyTable.GetOrCreateValue(new Verb_Shoot { verbProps = verbs[0].verbProps });
            }
            TimeUnrelatedVerbCalls(verbs);
            legacyLookups = 0;
            double ns = TimeUnrelatedVerbCalls(verbs);
            lookups = legacyLookups;
            return ns;
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
            legacyTable = null;
            TimeUnrelatedVerbCalls(verbs);
        }
    }

    private static bool LegacyCleanup(Verb __0)
    {
        legacyLookups++;
        if (__0.state != VerbState.Bursting && legacyTable!.TryGetValue(__0, out LegacyBurst? state))
            state.Valid = false;
        return false;
    }

    private static bool LegacyBurstCount(Verb __0)
    {
        legacyLookups++;
        legacyTable!.TryGetValue(__0, out _);
        return false;
    }

    private static bool EmptyCleanup(Verb __0) => false;

    private static bool EmptyBurstCount(Verb __0) => false;

    private static List<Verb> NewUnrelatedVerbs(ThingWithComps gun)
    {
        var colonist = (Pawn)RuntimeHelpers.GetUninitializedObject(typeof(Pawn));
        colonist.def = new ThingDef { defName = "ValidationColonist", category = ThingCategory.Pawn, race = new RaceProperties() };
        var turret = (Building_TurretGun)RuntimeHelpers.GetUninitializedObject(typeof(Building_TurretGun));
        turret.def = new ThingDef { defName = "ValidationTurret", category = ThingCategory.Building, building = new BuildingProperties() };
        var melee = new VerbProperties { verbClass = typeof(Verb_MeleeAttackDamage), burstShotCount = 1, warmupTime = 1f };
        VerbTracker tracker = gun.GetComp<CompEquippable>().verbTracker;
        var verbs = new List<Verb>(PerfVerbs);
        for (int i = 0; i < PerfVerbs; i++)
        {
            Thing caster = i % 3 == 0 ? turret : colonist;
            verbs.Add(i % 4 == 0
                ? new Verb_MeleeAttackDamage { verbProps = melee, verbTracker = tracker, caster = caster }
                : new Verb_Shoot { verbProps = gun.def.Verbs[0], verbTracker = tracker, caster = caster });
        }
        return verbs;
    }

    private static double TimeUnrelatedVerbCalls(List<Verb> verbs)
    {
        double best = double.MaxValue;
        for (int round = 0; round < PerfRounds; round++)
        {
            long start = Stopwatch.GetTimestamp();
            for (int i = 0; i < PerfCalls; i++)
            {
                Verb verb = verbs[i % verbs.Count];
                verb.VerbTick();
                _ = verb.BurstShotCount;
            }
            best = Math.Min(best, (Stopwatch.GetTimestamp() - start) * 1000000000.0 / Stopwatch.Frequency / PerfCalls);
        }
        return best;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void LockAndAbandonBurst(ThingWithComps gun, Building_WeaponStation station)
    {
        var equippable = new CompEquippable { parent = gun, props = new CompProperties(typeof(CompEquippable)) };
        var verb = new Verb_Shoot { verbProps = gun.def.Verbs[0], verbTracker = equippable.verbTracker, caster = station };
        verb.WarmupComplete();
        Check(verb.BurstShotCount == 4, "Abandoned burst did not lock the precision count.");
        // The verb, its tracker and its CompEquippable are unreachable once this frame returns.
    }

    private static void WaitForBurstLocks(int expected)
    {
        for (int i = 0; i < 8 && StationFireModesDiagnostics.ActiveBurstLocks != expected; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
        Check(StationFireModesDiagnostics.ActiveBurstLocks == expected,
            $"Active burst locks: expected {expected}, got {StationFireModesDiagnostics.ActiveBurstLocks}.");
    }

    // Call-path proof on the shipped IL: every burst-table access sits behind the caster type check and the
    // live-lock counter, and no other method in the assembly can reach the table at all.
    private static void ValidateIlGuards(Assembly patch)
    {
        using Cecil.ModuleDefinition module = Cecil.ModuleDefinition.ReadModule(patch.Location);
        Cecil.TypeDefinition patches = module.GetType("WhiteGiver.WeaponStationFireModes.StationPatches")
            ?? throw new InvalidOperationException("StationPatches type missing from " + patch.Location);
        var reached = new List<string>();
        foreach (string name in new[] { "CleanupBurst", "BurstCount", "LockBurst" }) AssertGuardedBurstLookup(patches, name);
        foreach (Cecil.TypeDefinition type in Types(module.Types))
        foreach (Cecil.MethodDefinition method in type.Methods)
        {
            if (!method.HasBody) continue;
            if (method.Body.Instructions.Any(i => IsBurstLookup(i.Operand))) reached.Add(method.Name);
        }
        string[] expected = { "CleanupBurst", "BurstCount", "LockBurst" };
        Check(reached.Count == expected.Length && expected.All(reached.Contains),
            "Unexpected burst-table call sites: " + string.Join(", ", reached));
        Console.WriteLine("PASS: shipped IL reaches ConditionalWeakTable<Verb, BurstState> from three methods only, and each one is guarded by `caster is Building_WeaponStation` followed by the live-lock counter.");
    }

    private static void AssertGuardedBurstLookup(Cecil.TypeDefinition patches, string name)
    {
        Cecil.MethodDefinition method = patches.Methods.Single(m => m.Name == name);
        List<Cecil.Cil.Instruction> code = method.Body.Instructions.ToList();
        int lookup = code.FindIndex(i => IsBurstLookup(i.Operand));
        int caster = code.FindIndex(i => i.OpCode == Cecil.Cil.OpCodes.Isinst
            && i.Operand is Cecil.TypeReference type && type.FullName == "WeaponStation.Building_WeaponStation");
        int counter = code.FindIndex(i => i.Operand is Cecil.MethodReference call
            && call.DeclaringType.FullName == "System.Threading.Volatile" && call.Name == "Read");
        Check(lookup >= 0, $"{name} no longer uses the guarded burst table; call-path proof is stale.");
        Check(caster >= 0 && caster < lookup, $"{name} reaches the burst table before the caster type check.");
        Check(counter >= 0 && counter < lookup, $"{name} reaches the burst table before the lock counter check.");
        Check(code[counter - 1].Operand is Cecil.FieldReference field && field.Name == "activeBurstLocks",
            $"{name} guards on something other than activeBurstLocks.");
    }

    private static IEnumerable<Cecil.TypeDefinition> Types(IEnumerable<Cecil.TypeDefinition> source)
    {
        foreach (Cecil.TypeDefinition type in source)
        {
            yield return type;
            foreach (Cecil.TypeDefinition nested in Types(type.NestedTypes)) yield return nested;
        }
    }

    private static bool IsBurstLookup(object? operand) => operand is Cecil.MethodReference call
        && call.DeclaringType.Name == "BurstTable" && call.Name is "TryGetValue" or "GetOrCreateValue";

    private static int FirstBurst = -1;
    private static float FirstWarmup;
    private static LocalTargetInfo Target;

    private static void InstallAttackBoundary()
    {
        var harmony = new Harmony("validation.weaponstation.attack-boundary");
        harmony.Patch(AccessTools.DeclaredMethod(typeof(Verb_LaunchProjectile), "TryCastShot"), prefix: new HarmonyMethod(typeof(Driver), nameof(ShotBoundary)));
        harmony.Patch(AccessTools.DeclaredMethod(typeof(Verb), nameof(Verb.CanHitTarget)), prefix: new HarmonyMethod(typeof(Driver), nameof(CanHitBoundary)));
        harmony.Patch(AccessTools.DeclaredMethod(typeof(Building_TurretGun), nameof(Building_TurretGun.TryFindNewTarget)), prefix: new HarmonyMethod(typeof(Driver), nameof(TargetBoundary)));
        harmony.Patch(AccessTools.DeclaredMethod(typeof(BattleLog), nameof(BattleLog.Add)), prefix: new HarmonyMethod(typeof(Driver), nameof(SkipBattleLog)));
        // Isolate final projectile/FX work; keep actual WarmupComplete, burst state and callbacks.
        harmony.Patch(AccessTools.DeclaredMethod(typeof(Verb), "TryCastNextBurstShot"), prefix: new HarmonyMethod(typeof(Driver), nameof(CaptureBurstBoundary)));
    }

    private static bool ShotBoundary(ref bool __result) { __result = true; return false; }
    private static bool CanHitBoundary(ref bool __result) { __result = true; return false; }
    private static bool TargetBoundary(ref LocalTargetInfo __result) { __result = Target; return false; }
    private static bool SkipBattleLog() => false;
    private static bool CaptureBurstBoundary(Verb __instance)
    {
        FirstBurst = (int)AccessTools.Field(typeof(Verb), "burstShotsLeft").GetValue(__instance)!;
        FirstWarmup = __instance.Caster is Building_WeaponStation station ? station.WeaponWarmupTime : 0f;
        return false;
    }

    private static Verb NewFixtureVerb(ThingWithComps gun, Building_WeaponStation station, Type type, int count)
    {
        var verb = (Verb)Activator.CreateInstance(type)!;
        verb.verbProps = new VerbProperties { verbClass = type, burstShotCount = count, warmupTime = 3.5f, defaultProjectile = new ThingDef { projectile = new ProjectileProperties() } };
        verb.verbTracker = gun.GetComp<CompEquippable>().verbTracker;
        verb.caster = station;
        return verb;
    }

    private static ThingWithComps EmptyGun(ThingDef def)
    {
        var gun = new ThingWithComps { def = def };
        var equippable = new CompEquippable { parent = gun, props = new CompProperties(typeof(CompEquippable)) };
        var properties = new VerbProperties { verbClass = typeof(Verb_Shoot) };
        Set(typeof(ThingDef), "verbs", def, new List<VerbProperties> { properties });
        Set(typeof(VerbTracker), "verbs", equippable.verbTracker, new List<Verb>
        {
            new Verb_Shoot { verbProps = properties, verbTracker = equippable.verbTracker }
        });
        Set(typeof(ThingWithComps), "comps", gun, new List<ThingComp> { equippable });
        return gun;
    }

    private static void Set(Type type, string name, object? instance, object value) => AccessTools.Field(type, name).SetValue(instance, value);
    private static void Near(float actual, float expected, string message) => Check(Math.Abs(actual - expected) < 0.00001f, $"{message}: expected {expected}, got {actual}.");
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
