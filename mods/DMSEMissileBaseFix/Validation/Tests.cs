using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using System.Xml.XPath;
using DMSE;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;
using Verse.AI.Group;
using WhiteGiver.DMSEMissileBaseFix;

namespace DMSEMissileBaseFixValidation;

internal static class Tests
{
    private static readonly string[] SiteNames =
    {
        "DMSE_MissileBase",
        "DMSE_FieldLaunchSite",
        "DMSE_EarlyLaunchSite",
    };

    private static int checks;

    internal static void Run(string mode, string dmseRoot, string modRoot)
    {
        checks = 0;
        SiteFixture.InstallHeadlessBoundary();
        XDocument defs = AuditRealXml(dmseRoot, modRoot);

        if (mode == "red")
        {
            RunRed(defs);
        }
        else
        {
            RunGreen(defs);
        }

        Console.WriteLine($"{mode.ToUpperInvariant()} COMPLETE: {checks} assertions passed against real RimWorld 1.7 + DMSE DLL/XML.");
        Console.WriteLine("Headless boundaries only: rendering, translation/message/window presentation, ThingWithComps spawn/despawn registration, pathfinding/region traversal, and the pawn CarryingCapacity stat.");
    }

    private static void Check(bool condition, string label)
    {
        if (!condition)
        {
            throw new InvalidOperationException("ASSERT FAILED: " + label);
        }

        checks++;
        Console.WriteLine($"[PASS {checks:D3}] {label}");
    }

    private static XDocument AuditRealXml(string dmseRoot, string modRoot)
    {
        Console.WriteLine("=== REAL XML / DLL AUDIT ===");
        string[] files =
        {
            @"Defs\WorldObjects_MissileBase.xml",
            @"Defs\WorldObjects_FieldLaunchSite.xml",
            @"Defs\WorldObjects_EarlyLaunchSite.xml",
            @"Defs\DMSE_BVRWorld.xml",
            @"Defs\ThingDefs_Buildings\DMSE_Security.xml",
            @"Defs\ThingDefs_Buildings\DMSE_GenerationPlatforms.xml",
            @"Defs\ThingDefs_Buildings\DMSE_GenerationTurrets.xml",
            @"Defs\ThingDefs_Buildings\DMSE_Gen_Decoration.xml",
            @"Defs\ThingDefs_Buildings\DMSE_MissileRack.xml",
            @"Defs\ThingDefs_Buildings\DMSE_LightMissileLauncher.xml",
            @"Defs\ThingDefs_Buildings\DMSE_RotaryMissileLauncher.xml",
            @"Defs\ThingDefs_Buildings\DMSE_Security_Missile.xml",
            @"Defs\ThingDefs_Buildings\DMSE_LoiteringLauncher.xml",
        };

        XDocument merged = new(new XElement("Defs"));
        foreach (string relative in files)
        {
            XDocument doc = XDocument.Load(Path.Combine(dmseRoot, relative));
            foreach (XElement element in doc.Root!.Elements())
            {
                merged.Root!.Add(new XElement(element));
            }
        }

        foreach (string siteName in SiteNames)
        {
            XElement def = merged.XPathSelectElement($"Defs/WorldObjectDef[defName='{siteName}']")!;
            Check(def.Element("worldObjectClass")!.Value == "RimWorld.Planet.Site", $"{siteName} uses real Site");
            Check(def.Element("validLaunchTarget")!.Value == "false", $"RED XML: {siteName}.validLaunchTarget=false");
            Check(def.XPathSelectElement("comps/li[@Class='WorldObjectCompProperties_FormCaravan']") != null,
                $"{siteName} has the vanilla FormCaravan comp");
            Check(def.XPathSelectElement("comps/li[@Class='DMSE.CompProperties_MissileBase']") != null,
                $"{siteName} has DMSE's MissileBase comp");
        }

        XElement wing = merged.XPathSelectElement("Defs/ThingDef[defName='DMSE_Turret_Vulcan_WingBack']")!;
        XElement fuel = wing.XPathSelectElement("comps/li[@Class='CompProperties_Refuelable']")!;
        float capacity = Float(fuel, "fuelCapacity");
        float initial = Float(fuel, "initialFuelPercent");
        float threshold = Float(fuel, "minimumFueledThreshold");
        Check(capacity == 600f && initial == 0.5f && threshold == 0.9f,
            "Wing-Back real XML: capacity=600, initial=50%, absolute threshold=0.9");
        Check(capacity * initial == 300f && capacity * initial >= threshold,
            "Wing-Back starts with 300 absolute fuel units and is NOT dry");
        Check(Float(wing, "verbs/li/consumeFuelPerShot") == 0f || wing.XPathSelectElement("verbs/li/consumeFuelPerShot") == null,
            "Wing-Back building def delegates its verb to turretGunDef");
        XElement wingGun = merged.XPathSelectElement("Defs/ThingDef[defName='DMSE_Gun_Vulcan_WingBack']")!;
        Check(Float(wingGun, "verbs/li/consumeFuelPerShot") == 1f, "Wing-Back gun consumes one fuel unit per shot");

        XElement field = merged.XPathSelectElement("Defs/ThingDef[defName='DMSE_Building_FieldAirDefense']")!;
        Check(field.Element("thingClass")!.Value == "Building_TurretGun", "Field Air-Defense is a real automated Building_TurretGun");
        Check(field.XPathSelectElement("comps/li[@Class='DMSE.CompProperties_MissileRackStorage']") != null,
            "Field Air-Defense stores separate interceptor Things in CompMissileRackStorage");
        Check(field.XPathSelectElement("comps/li[@Class='DMSE.CompProperties_MissileLauncher']") != null,
            "Field Air-Defense has an independent interceptor launcher");
        Check(Float(field, "comps/li[@Class='CompProperties_Refuelable']/fuelCapacity") == 200f
            && Float(field, "comps/li[@Class='CompProperties_Refuelable']/initialFuelPercent") == 0.5f,
            "Field Air-Defense starts with 100 cannon rounds, not zero");

        XElement mini = XDocument.Load(Path.Combine(
            @"A:\SteamLibrary\steamapps\common\RimWorld",
            @"Data\Core\Defs\ThingDefs_Buildings\Buildings_Security_Turrets.xml"))
            .XPathSelectElement("Defs/ThingDef[defName='Turret_MiniTurret']")!;
        Check(Float(mini, "comps/li[@Class='CompProperties_Refuelable']/fuelCapacity") == 60f
            && Float(mini, "comps/li[@Class='CompProperties_Refuelable']/initialFuelPercent") == 1f
            && Float(mini, "comps/li[@Class='CompProperties_Refuelable']/minimumFueledThreshold") == 1f,
            "Real 1.7 mini-turret starts with 60/60 fuel and an absolute one-unit threshold");

        string mid = File.ReadAllText(Path.Combine(dmseRoot, @"Defs\Structures\LaunchSite_Mid.xml"));
        string early = File.ReadAllText(Path.Combine(dmseRoot, @"Defs\Structures\LaunchSite_Early.xml"));
        string missileBase = File.ReadAllText(Path.Combine(dmseRoot, @"Defs\WorldObjects_MissileBase.xml"));
        Check(missileBase.Contains("<def>DMSE_Turret_Vulcan_WingBack</def>", StringComparison.Ordinal),
            "MissileBase structure really spawns Wing-Back");
        Check(mid.Contains("<def>DMSE_Building_FieldAirDefense</def>", StringComparison.Ordinal)
            && mid.Contains("<def>Turret_MiniTurret</def>", StringComparison.Ordinal)
            && mid.Contains("<def>DMSE_Turret_LegacyFlak</def>", StringComparison.Ordinal),
            "Field structure set includes FieldAirDefense, MiniTurret and LegacyFlak");
        Check(early.Contains("<def>DMSE_Turret_LegacyFlak</def>", StringComparison.Ordinal)
            && early.Contains("<def>DMSE_LoiteringRack</def>", StringComparison.Ordinal),
            "Early structure set includes manned LegacyFlak and lootable racks");

        Check(typeof(Building_MissileRack).GetInterfaces().Contains(typeof(IHaulSource)),
            "REAL DMSE DLL: Building_MissileRack implements IHaulSource");
        Check(typeof(CompMissileRackStorage).GetInterfaces().Contains(typeof(IThingHolder))
            && !typeof(CompMissileRackStorage).GetInterfaces().Contains(typeof(IHaulSource)),
            "REAL DMSE DLL: CompMissileRackStorage is IThingHolder but not IHaulSource");

        XDocument patch = XDocument.Load(Path.Combine(modRoot, @"1.7\Patches\MissileBaseLaunchTargetPatch.xml"));
        string xpath = patch.XPathSelectElement("Patch/Operation/xpath")!.Value;
        List<XElement> matches = merged.XPathSelectElements(xpath).ToList();
        Check(matches.Count == 3, "XML patch conditional sees exactly the three intended validLaunchTarget nodes");
        foreach (XElement match in matches)
        {
            match.Value = "true";
        }
        Check(SiteNames.All(name => merged.XPathSelectElement($"Defs/WorldObjectDef[defName='{name}']/validLaunchTarget")!.Value == "true"),
            "XML patch semantics enable all three targets");
        Check(merged.XPathSelectElement("Defs/WorldObjectDef[defName='DMSE_Interceptor']/validLaunchTarget")?.Value == "false",
            "XML patch leaves DMSE_Interceptor false");

        XDocument about = XDocument.Load(Path.Combine(modRoot, @"About\About.xml"));
        Check(about.XPathSelectElement("ModMetaData/packageId")!.Value == "whitegiver.dmse.missilebasefix",
            "About packageId is exact");
        Check(about.XPathSelectElement("ModMetaData/modVersion")!.Value == "1.0.0", "About version=1.0.0");
        string[] required = { "brrainz.harmony", "AOBA.Framework", "Aoba.DeadManSwitch.Core", "Aoba.DeadManSwitch.Expedition" };
        List<string> dependencies = about.XPathSelectElements("ModMetaData/modDependencies/li/packageId").Select(x => x.Value).ToList();
        List<string> loadAfter = about.XPathSelectElements("ModMetaData/loadAfter/li").Select(x => x.Value).ToList();
        Check(required.All(dependencies.Contains) && required.All(loadAfter.Contains),
            "About declares all four dependencies/loadAfter using base packageIds");
        Check(!dependencies.Concat(loadAfter).Any(x => x.EndsWith("_steam", StringComparison.OrdinalIgnoreCase)),
            "About never uses _steam packageId variants");
        return merged;
    }

    private static float Float(XElement element, string path)
    {
        XElement? value = element.XPathSelectElement(path);
        return value == null ? 0f : float.Parse(value.Value, CultureInfo.InvariantCulture);
    }

    private static void RunRed(XDocument defs)
    {
        Console.WriteLine("=== RED: REPRODUCE UPSTREAM FAILURES ===");
        RedShuttleGate();
        RedFoggedThreatGate();
        RedTurretGate();
        RedLootGate(defs);
    }

    private static void RedShuttleGate()
    {
        SiteFixture fixture = SiteFixture.Create();
        Pawn pawn = fixture.Map.mapPawns.FreeColonistsSpawned[0];
        CompTransporter transporter = fixture.AddTransporter();
        transporter.innerContainer.TryAdd(pawn, canMergeWithExistingStacks: false);
        int launches = 0;
        bool accepted = CompLaunchable.ChoseWorldTarget(
            fixture.Site,
            fixture.Site.Tile,
            new IThingHolder[] { transporter },
            maxLaunchDistance: -1,
            launchAction: (_, _) => launches++,
            launchable: null);
        Check(!accepted && launches == 0, "RED: real ChoseWorldTarget rejects the missile site before arrival menus");
    }

    private static void RedFoggedThreatGate()
    {
        SiteFixture fixture = SiteFixture.Create();
        fixture.AddDormantEnemy(new IntVec3(35, 0, 35), fogged: true);
        fixture.SetBaseDestroyed(true);
        Check(!fixture.Site.CanReformFoggedEnemies, "RED: Site.CanReformFoggedEnemies=false");
        Check(fixture.Form.AnyActiveThreatNow, "RED: fogged dormant enemy remains an active reform blocker");
        Check(!fixture.Form.CanFormOrReformCaravanNow, "RED: CanFormOrReformCaravanNow=false");
        Check(fixture.Map.exitMapGrid.MapUsesExitGrid, "RED: green exit grid remains active");
        Check(!HasReformGizmo(fixture), "RED: no reform gizmo despite a free player pawn");
        Check(!fixture.Form.CanReformNow(), "RED: CanReformNow=false");

        fixture.SetBaseDestroyed(false);
        bool alsoRemove = true;
        Patch_MissileBaseSiteKept.Postfix(fixture.Site, __result: true, ref alsoRemove);
        Check(!alsoRemove, "RED: DMSE keeps a not-yet-destroyed site after map removal");
        fixture.SetBaseDestroyed(true);
        alsoRemove = true;
        Patch_MissileBaseSiteKept.Postfix(fixture.Site, __result: true, ref alsoRemove);
        Check(alsoRemove, "DMSE's BaseSiteDestroyed controls site removal, not reform eligibility");
    }

    private static void RedTurretGate()
    {
        SiteFixture wing = SiteFixture.Create();
        Building_TurretGun turret = wing.AddTurret("DMSE_Turret_Vulcan_WingBack", 600f, 0.5f, 0.9f, powerOn: true);
        CompRefuelable fuel = turret.GetComp<CompRefuelable>();
        Check(fuel.Fuel == 300f && fuel.HasFuel, "RED: real CompRefuelable proves Wing-Back starts armed (300 >= 0.9)");
        Check(turret.AttackVerb.Available(), "RED: armed Wing-Back AttackVerb.Available=true");
        Check(!turret.ThreatDisabled(null) && !wing.Form.CanFormOrReformCaravanNow,
            "RED: armed powered Wing-Back correctly blocks reform");
        SiteFixture.SetFuel(fuel, 0f);
        Check(!fuel.HasFuel && !turret.AttackVerb.Available(), "RED: zero-fuel Wing-Back cannot fire");
        Check(!turret.ThreatDisabled(null), "RED: vanilla ThreatDisabled ignores the dry AttackVerb");
        Check(!wing.Form.CanFormOrReformCaravanNow && wing.Map.exitMapGrid.MapUsesExitGrid,
            "RED: powered dry Wing-Back blocks reform and keeps exit grid");

        SiteFixture field = SiteFixture.Create();
        Building_TurretGun aa = field.AddTurret("DMSE_Building_FieldAirDefense", 200f, 0f, 0f,
            powerOn: true, fieldAirDefense: true, storedMissiles: 3);
        Check(aa.GetComp<CompMissileLauncher_Interceptor>().Active,
            "RED: dry cannons do not disable Field Air-Defense's loaded SAM capability");
        Check(!aa.ThreatDisabled(null) && !field.Form.CanFormOrReformCaravanNow,
            "RED: missile-armed Field Air-Defense is a real reform blocker");

        SiteFixture unpowered = SiteFixture.Create();
        Building_TurretGun off = unpowered.AddTurret("DMSE_Building_FieldAirDefense", 200f, 0.5f, 0f,
            powerOn: false, fieldAirDefense: true, storedMissiles: 3);
        Check(off.ThreatDisabled(null) && unpowered.Form.CanFormOrReformCaravanNow,
            "RED negative control: unpowered turret is already non-threatening upstream");

        SiteFixture manned = SiteFixture.Create();
        Building_TurretGun flak = manned.AddTurret("DMSE_Turret_LegacyFlak", 80f, 0.5f, 0f,
            powerOn: true, mannable: true);
        Check(flak.ThreatDisabled(null) && manned.Form.CanFormOrReformCaravanNow,
            "RED negative control: unmanned LegacyFlak is already non-threatening upstream");
    }

    private static void RedLootGate(XDocument defs)
    {
        _ = defs;
        SiteFixture fixture = SiteFixture.Create();
        Thing ground = fixture.AddGroundMissile(new IntVec3(10, 0, 10));
        Building_MissileRack rack = fixture.AddRack("DMSE_Building_LightMissileLauncher", 2);
        Building compBuilding = fixture.AddCompStorageBuilding("DMSE_FieldStructure_AmmoStorage", 3);
        List<Thing> compItems = compBuilding.GetComp<CompMissileRackStorage>().HeldThings.ToList();
        List<Thing> rackItems = rack.GetDirectlyHeldThings().Cast<Thing>().ToList();

        List<Thing> items = CaravanFormingUtility.AllReachableColonyItems(fixture.Map, true, true, true).ToList();
        Check(items.Contains(ground), "RED loot: ground missile is visible");
        Check(rackItems.All(items.Contains), "RED loot: Building_MissileRack contents are visible through IHaulSource");
        Check(compItems.All(item => !items.Contains(item)), "RED BUG: CompMissileRackStorage missiles are invisible");

        CompTransporter transporter = fixture.AddTransporter();
        List<Thing> sendable = TransporterUtility.AllSendableItems(new List<CompTransporter> { transporter }, fixture.Map).ToList();
        Check(compItems.All(item => !sendable.Contains(item)), "RED BUG: shuttle AllSendableItems also omits comp-storage missiles");

        Pawn pawn = fixture.Map.mapPawns.FreeColonistsSpawned[0];
        Lord lord = fixture.MakeCaravanLord(pawn, compItems[0]);
        Thing? found = GatherItemsForCaravanUtility.FindThingToHaul(pawn, lord);
        Check(found == null, "RED BUG: real caravan gathering search cannot discover the comp-storage missile");
    }

    private static void RunGreen(XDocument defs)
    {
        _ = defs;
        Console.WriteLine("=== GREEN: APPLY FIX / VERIFY COMPLETE BEHAVIOR ===");
        List<SiteFixture> launchFixtures = SiteNames.Select(name => SiteFixture.Create(name)).ToList();
        DefDatabase<WorldObjectDef>.Clear();
        foreach (SiteFixture fixture in launchFixtures)
        {
            DefDatabase<WorldObjectDef>.Add(fixture.Site.def);
        }
        WorldObjectDef interceptorDef = new() { defName = "DMSE_Interceptor", validLaunchTarget = false };
        DefDatabase<WorldObjectDef>.Add(interceptorDef);

        int enabled = DMSEMissileBaseFixMod.EnsureLaunchTargetsEnabled();
        int installed = DMSEMissileBaseFixMod.ApplyPatches();
        Check(enabled == 3, "GREEN: startup fallback enables exactly three DMSE site defs");
        Check(installed == 3, $"GREEN: all three Harmony patches installed (got {installed}: {string.Join(", ", DMSEMissileBaseFixMod.InstalledPatches)})");
        Check(launchFixtures.All(f => f.Site.def.validLaunchTarget), "GREEN: every target def accepts shuttles/pods");
        Check(!interceptorDef.validLaunchTarget, "GREEN: unrelated interceptor target remains false");

        Harmony upstreamReentry = new("validation.upstream.dmse.reentry");
        upstreamReentry.Patch(AccessTools.Method(typeof(TransportersArrivalAction_VisitSite), "CanVisit"),
            postfix: new HarmonyMethod(typeof(Patch_MissileBaseReentry), nameof(Patch_MissileBaseReentry.Postfix)));

        foreach (SiteFixture fixture in launchFixtures)
        {
            Current.Game = fixture.Game;
            GreenShuttleEntry(fixture);
        }

        GreenReentryAndRisk(launchFixtures[0]);
        GreenFoggedThreat();
        GreenTurrets();
        GreenLoot();
    }

    private static void GreenShuttleEntry(SiteFixture fixture)
    {
        fixture.SetLocalMapAssociation(false);
        Pawn pawn = fixture.Map.mapPawns.FreeColonistsSpawned[0];
        CompTransporter transporter = fixture.AddTransporter();
        transporter.innerContainer.TryAdd(pawn, canMergeWithExistingStacks: false);
        TransportersArrivalAction? selected = null;
        SiteFixture.LastWindow = null;
        bool result = CompLaunchable.ChoseWorldTarget(
            fixture.Site,
            fixture.Site.Tile,
            new IThingHolder[] { transporter },
            maxLaunchDistance: -1,
            launchAction: (_, action) => selected = action,
            launchable: null);
        Check(!result && SiteFixture.LastWindow is FloatMenu,
            $"GREEN {fixture.Site.def.defName}: ChoseWorldTarget reaches the arrival-choice menu");
        List<FloatMenuOption> options = GetFloatMenuOptions((FloatMenu)SiteFixture.LastWindow!);
        Check(options.Count == 2, $"GREEN {fixture.Site.def.defName}: both edge and center arrival options exist");
        options[0].action();
        Check(selected is TransportersArrivalAction_VisitSite,
            $"GREEN {fixture.Site.def.defName}: selection produces real TransportersArrivalAction_VisitSite");
    }

    private static List<FloatMenuOption> GetFloatMenuOptions(FloatMenu menu)
    {
        return (List<FloatMenuOption>)typeof(FloatMenu).GetField("options", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(menu)!;
    }

    private static void GreenReentryAndRisk(SiteFixture fixture)
    {
        Current.Game = fixture.Game;
        Pawn pawn = fixture.AddPlayerPawn(new IntVec3(6, 0, 6));
        CompTransporter transporter = fixture.AddTransporter();
        transporter.innerContainer.TryAdd(pawn, canMergeWithExistingStacks: false);
        IThingHolder[] pods = { transporter };

        fixture.SetReentryBlockedUntil(1000);
        FloatMenuAcceptanceReport direct = FloatMenuAcceptanceReport.WasAccepted;
        Patch_MissileBaseReentry.Postfix(fixture.Site, ref direct);
        Check(!direct.Accepted, "GREEN: direct real DMSE re-entry postfix rejects during cooldown");
        FloatMenuAcceptanceReport blocked = TransportersArrivalAction_VisitSite.CanVisit(pods, fixture.Site);
        Check(!blocked.Accepted,
            "GREEN: DMSE's own re-entry cooldown still rejects shuttle/pod visits");
        fixture.SetReentryBlockedUntil(-1);
        Check(TransportersArrivalAction_VisitSite.CanVisit(pods, fixture.Site).Accepted,
            "GREEN: expired/cleared re-entry cooldown accepts the visit");

        fixture.SetLocalMapAssociation(false);
        fixture.AddWorldAirDefense();
        CompLaunchable launchable = fixture.AddLaunchable();
        SiteFixture.LastWindow = null;
        bool runOriginal = DMSE.Patch_InterceptRisk_TryLaunch.Prefix(
            launchable,
            fixture.Site.Tile,
            arrivalAction: null,
            __runOriginal: true);
        Check(!runOriginal && SiteFixture.LastWindow is Dialog_MessageBox,
            "GREEN: DMSE's real BVR prefix defers launch and opens its interception-risk confirmation");
    }

    private static void GreenFoggedThreat()
    {
        SiteFixture fixture = SiteFixture.Create();
        fixture.AddDormantEnemy(new IntVec3(35, 0, 35), fogged: true);
        fixture.SetBaseDestroyed(true);
        Check(fixture.Site.CanReformFoggedEnemies, "GREEN: DMSE Site CanReformFoggedEnemies=true");
        Check(!fixture.Form.AnyActiveThreatNow, "GREEN: fogged dormant enemy no longer blocks reform");
        Check(fixture.Form.CanFormOrReformCaravanNow, "GREEN: CanFormOrReformCaravanNow=true");
        Check(!fixture.Map.exitMapGrid.MapUsesExitGrid, "GREEN: exit grid switches off");
        Check(HasReformGizmo(fixture), "GREEN: real reform gizmo appears");
        Check(fixture.Form.CanReformNow(), "GREEN: CanReformNow=true with a free player pawn");

        SiteFixture plain = SiteFixture.Create("ValidationPlainSite", missileBase: false);
        plain.AddDormantEnemy(new IntVec3(35, 0, 35), fogged: true);
        Check(!plain.Site.CanReformFoggedEnemies && plain.Form.AnyActiveThreatNow && !plain.Form.CanFormOrReformCaravanNow,
            "GREEN negative control: non-DMSE sites retain vanilla fogged-threat blocking");

        Caravan nonMapParent = (Caravan)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Caravan));
        Check(!nonMapParent.CanReformFoggedEnemies,
            "GREEN safety: non-MapParent WorldObject (Caravan) does not throw InvalidCastException on CanReformFoggedEnemies");
    }

    private static void GreenTurrets()
    {
        SiteFixture wing = SiteFixture.Create();
        Building_TurretGun turret = wing.AddTurret("DMSE_Turret_Vulcan_WingBack", 600f, 0.5f, 0.9f, powerOn: true);
        CompRefuelable fuel = turret.GetComp<CompRefuelable>();
        Check(fuel.HasFuel && !turret.ThreatDisabled(null) && !wing.Form.CanFormOrReformCaravanNow,
            "GREEN: initially armed Wing-Back remains a threat");
        SiteFixture.SetFuel(fuel, 0f);
        Check(turret.ThreatDisabled(null), "GREEN: genuinely dry Wing-Back is ThreatDisabled=true");
        Check(wing.Form.CanFormOrReformCaravanNow && !wing.Map.exitMapGrid.MapUsesExitGrid,
            "GREEN: dry Wing-Back releases FormCaravanComp and ExitMapGrid");
        SiteFixture.SetFuel(fuel, 300f);
        Check(!turret.ThreatDisabled(null) && !wing.Form.CanFormOrReformCaravanNow,
            "GREEN: refueling immediately restores the active threat");
        wing.SetPower(turret, false);
        Check(turret.ThreatDisabled(null) && wing.Form.CanFormOrReformCaravanNow,
            "GREEN: unpowered armed turret remains safely neutralized");

        SiteFixture field = SiteFixture.Create();
        Building_TurretGun aa = field.AddTurret("DMSE_Building_FieldAirDefense", 200f, 0f, 0f,
            powerOn: true, fieldAirDefense: true, storedMissiles: 3);
        List<Thing> missiles = aa.GetComp<CompMissileRackStorage>().HeldThings.ToList();
        Check(!aa.ThreatDisabled(null) && !field.Form.CanFormOrReformCaravanNow,
            "GREEN: dry cannons + loaded operational SAM still block reform");
        List<Thing> activeLoot = CaravanFormingUtility.AllReachableColonyItems(field.Map, true, true, true).ToList();
        Check(missiles.All(item => !activeLoot.Contains(item)) && aa.GetComp<CompMissileRackStorage>().StoredCount == 3,
            "GREEN: active hostile SAM storage cannot be remotely looted or emptied");
        field.SetPower(aa, false);
        Check(aa.ThreatDisabled(null) && field.Form.CanFormOrReformCaravanNow,
            "GREEN: powering down the SAM releases reform without deleting its missiles");

        SiteFixture dryField = SiteFixture.Create();
        Building_TurretGun dryAa = dryField.AddTurret("DMSE_Building_FieldAirDefense", 200f, 0f, 0f,
            powerOn: true, fieldAirDefense: true, storedMissiles: 0);
        Check(dryAa.ThreatDisabled(null) && dryField.Form.CanFormOrReformCaravanNow,
            "GREEN: dry cannon + empty SAM + dry CIWS is no longer a false threat");

        SiteFixture manned = SiteFixture.Create();
        Building_TurretGun flak = manned.AddTurret("DMSE_Turret_LegacyFlak", 80f, 0.5f, 0f,
            powerOn: true, mannable: true);
        Check(flak.ThreatDisabled(null) && manned.Form.CanFormOrReformCaravanNow,
            "GREEN: unmanned manned-turret behavior remains unchanged");

        SiteFixture mini = SiteFixture.Create();
        Building_TurretGun miniTurret = mini.AddTurret("Turret_MiniTurret", 60f, 1f, 1f, powerOn: true);
        SiteFixture.SetFuel(miniTurret.GetComp<CompRefuelable>(), 0f);
        Check(miniTurret.ThreatDisabled(null) && mini.Form.CanFormOrReformCaravanNow,
            "GREEN: real 1.7 dry mini-turret no longer deadlocks a DMSE site");

        SiteFixture plain = SiteFixture.Create("ValidationPlainSite", missileBase: false);
        Building_TurretGun plainTurret = plain.AddTurret("ValidationDryTurret", 60f, 0f, 1f, powerOn: true);
        Check(!plainTurret.ThreatDisabled(null) && !plain.Form.CanFormOrReformCaravanNow,
            "GREEN negative control: non-DMSE maps retain vanilla dry-turret threat semantics");
    }

    private static void GreenLoot()
    {
        SiteFixture fixture = SiteFixture.Create();
        Thing ground = fixture.AddGroundMissile(new IntVec3(10, 0, 10));
        Building_MissileRack rack = fixture.AddRack("DMSE_Building_LightMissileLauncher", 2);
        Building compBuilding = fixture.AddCompStorageBuilding("DMSE_FieldStructure_AmmoStorage", 3);
        CompMissileRackStorage storage = compBuilding.GetComp<CompMissileRackStorage>();
        List<Thing> compItems = storage.HeldThings.ToList();
        List<Thing> rackItems = rack.GetDirectlyHeldThings().Cast<Thing>().ToList();

        List<Thing> items = CaravanFormingUtility.AllReachableColonyItems(fixture.Map, true, true, true).ToList();
        Check(items.Contains(ground) && rackItems.All(items.Contains) && compItems.All(items.Contains),
            "GREEN loot: ground + Building_MissileRack + CompMissileRackStorage missiles are all listed");
        Check(compItems.All(item => item.Spawned) && storage.StoredCount == 0,
            "GREEN loot: comp contents become real ground objects through ThingOwner.TryDrop");
        Check(items.Distinct().Count() == items.Count, "GREEN loot: no duplicate Thing entries");
        Check(rack.StoredCount == 2 && rackItems.All(item => !item.Spawned),
            "GREEN loot: vanilla IHaulSource rack remains in place; no unnecessary auto-emptying");

        CompTransporter transporter = fixture.AddTransporter();
        List<Thing> sendable = TransporterUtility.AllSendableItems(new List<CompTransporter> { transporter }, fixture.Map).ToList();
        Check(compItems.All(sendable.Contains), "GREEN loot: Dialog_LoadTransporters item source includes every comp missile");

        Pawn pawn = fixture.Map.mapPawns.FreeColonistsSpawned[0];
        Lord lord = fixture.MakeCaravanLord(pawn, compItems[0]);
        Thing? caravanTarget = GatherItemsForCaravanUtility.FindThingToHaul(pawn, lord);
        Check(caravanTarget == compItems[0], "GREEN loot: real caravan gathering finds the formerly stored missile");

        TransferableOneWay left = new();
        left.things.Add(compItems[0]);
        left.AdjustTo(1);
        transporter.leftToLoad = new List<TransferableOneWay> { left };
        ThingCount transporterTarget = LoadTransportersJobUtility.FindThingToLoad(pawn, transporter);
        Check(transporterTarget.Thing == compItems[0] && transporterTarget.Count == 1,
            "GREEN loot: real LoadTransportersJobUtility finds one formerly stored missile");

        ThingOwner<Thing> bag = new();
        int carried = pawn.carryTracker.TryStartCarry(compItems[0], 1, reserve: false);
        Check(carried == 1 && pawn.carryTracker.CarriedThing == compItems[0],
            "GREEN loot: real Pawn_CarryTracker picks up the formerly stored ground missile");
        Check(pawn.carryTracker.innerContainer.TryTransferToContainer(compItems[0], bag) && bag.Contains(compItems[0]),
            "GREEN loot: carry-to-outbound-inventory transfer succeeds");
        Check(!fixture.Map.listerThings.AllThings.Contains(compItems[0]) && !bag.ToList().Any(item => item != compItems[0] && item.ThingID == compItems[0].ThingID),
            "GREEN loot: transfer leaves exactly one object and no map duplicate");
        int rackBefore = rack.StoredCount;
        Check(bag.TryAddOrTransfer(rackItems[0], canMergeWithExistingStacks: false) && rack.StoredCount == rackBefore - 1,
            "GREEN loot: hostile Building_MissileRack transfers without claiming and decrements its real container");

        SiteFixture reservedFixture = SiteFixture.Create();
        Building reservedBuilding = reservedFixture.AddCompStorageBuilding("ReservedCompStorage", 1);
        Thing reservedItem = reservedBuilding.GetComp<CompMissileRackStorage>().HeldThings[0];
        Pawn reservedPawn = reservedFixture.Map.mapPawns.FreeColonistsSpawned[0];
        Job job = new();
        Check(reservedPawn.Reserve(reservedItem, job), "GREEN edge: reserve the unspawned stored missile through the real ReservationManager");
        List<Thing> reservedItems = CaravanFormingUtility.AllReachableColonyItems(reservedFixture.Map, true, false, true).ToList();
        Check(!reservedItems.Contains(reservedItem) && reservedBuilding.GetComp<CompMissileRackStorage>().StoredCount == 1,
            "GREEN edge: reserved contents are neither dropped nor listed when reservations are disallowed");
        List<Thing> allowedReserved = CaravanFormingUtility.AllReachableColonyItems(reservedFixture.Map, true, true, true).ToList();
        Check(allowedReserved.Contains(reservedItem), "GREEN edge: reform's allowEvenIfReserved=true includes the same missile");

        SiteFixture fogFixture = SiteFixture.Create();
        Building fogBuilding = fogFixture.AddCompStorageBuilding("FoggedCompStorage", 1);
        Thing fogItem = fogBuilding.GetComp<CompMissileRackStorage>().HeldThings[0];
        fogFixture.Map.fogGrid.Refog(CellRect.SingleCell(fogBuilding.Position));
        List<Thing> fogLoot = CaravanFormingUtility.AllReachableColonyItems(fogFixture.Map, true, true, true).ToList();
        Check(!fogLoot.Contains(fogItem) && fogBuilding.GetComp<CompMissileRackStorage>().StoredCount == 1,
            "GREEN edge: fogged storage is not remotely emptied");

        SiteFixture homeMode = SiteFixture.Create();
        Building homeStorage = homeMode.AddCompStorageBuilding("HomeModeCompStorage", 1);
        Thing homeItem = homeStorage.GetComp<CompMissileRackStorage>().HeldThings[0];
        List<Thing> homeItems = CaravanFormingUtility.AllReachableColonyItems(homeMode.Map, false, true, false).ToList();
        Check(!homeItems.Contains(homeItem) && homeStorage.GetComp<CompMissileRackStorage>().StoredCount == 1,
            "GREEN edge: allowEvenIfOutsideHomeArea=false leaves normal home-map behavior untouched");

        SiteFixture activeSite = SiteFixture.Create();
        Building activeStorage = activeSite.AddCompStorageBuilding("ActiveSiteCompStorage", 1);
        Thing activeItem = activeStorage.GetComp<CompMissileRackStorage>().HeldThings[0];
        activeSite.AddTurret("ActiveSiteArmedTurret", 60f, 1f, 1f, powerOn: true);
        List<Thing> activeSiteItems = CaravanFormingUtility.AllReachableColonyItems(activeSite.Map, true, true, true).ToList();
        Check(!activeSiteItems.Contains(activeItem) && activeStorage.GetComp<CompMissileRackStorage>().StoredCount == 1,
            "GREEN edge: a visible active hostile anywhere on the site prevents comp-storage auto-dropping");

        SiteFixture plain = SiteFixture.Create("ValidationPlainSite", missileBase: false);
        Building plainStorage = plain.AddCompStorageBuilding("PlainSiteCompStorage", 1);
        Thing plainItem = plainStorage.GetComp<CompMissileRackStorage>().HeldThings[0];
        List<Thing> plainItems = CaravanFormingUtility.AllReachableColonyItems(plain.Map, true, true, true).ToList();
        Check(!plainItems.Contains(plainItem) && plainStorage.GetComp<CompMissileRackStorage>().StoredCount == 1,
            "GREEN negative control: comp-storage loot patch is DMSE-site-only");
    }

    private static bool HasReformGizmo(SiteFixture fixture)
    {
        return fixture.Form.GetGizmos().OfType<Command_Action>()
            .Any(command => command.tutorTag == "ReformCaravan" && !command.Disabled);
    }
}
