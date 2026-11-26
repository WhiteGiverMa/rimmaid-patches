using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using WhiteGiver.MechanitorBandwidthRelay;

internal static class Driver
{
    private const string PatchId = "whitegiver.mechanitor.bandwidthrelay";
    private static readonly Harmony QA = new(PatchId + ".validation");
    private static readonly Dictionary<Pawn, List<int>> Audits = new();
    private static readonly List<Window> Windows = new();
    private static readonly List<string> Errors = new();
    private static readonly List<string> Warnings = new();
    private static readonly List<(string Label, bool Active)> Buttons = new();
    private static string? clickedButton;
    private static int clickIndex;
    private static string? enteredAmount;
    private static readonly Faction Player = new() { def = new FactionDef { defName = "TestPlayer", isPlayer = true }, loadID = 1 };
    private static readonly Faction Other = new() { def = new FactionDef { defName = "TestOther" }, loadID = 2 };
    private static ThingDef relayDef = null!;
    private static Game game = null!;
    private static RelayNetwork network = null!;
    private static int checks;
    private static int nextId = 1000;

    internal static void Run(string mod)
    {
        Setup(mod);
        var donor = NewMechanitor("Aster", 6);
        var recipient = NewMechanitor("Birch", 2);
        var oldest = Assign(recipient, 2, 100);
        var newest = Assign(recipient, 2, 200);
        var relay = NewRelay();
        recipient.mechanitor.Notify_BandwidthChanged();
        Equal(2, recipient.mechanitor.TotalBandwidth, "unlinked tracker is stock");
        Check(recipient.mechanitor.ControlledPawns.SequenceEqual([oldest]), "stock assigned-order audit before a link");

        var distant = NewMechanitor("Cedar", 4, map: 1);
        var foreign = NewMechanitor("Dune", 4, faction: Other);
        Check(!relay.CanLink(donor, donor) && !relay.CanLink(donor, distant) && !relay.CanLink(donor, foreign),
            "new pairs reject self-links, other maps and other factions");
        relay.ApplyFromDialog(new RelayLink(donor, distant, 5));
        Check(relay.Link.Requested == 0 && network.Grant(relay) == 0, "an off-map pawn cannot be paired at the apply entry point");
        var donorMech = Assign(donor, 2.2f, 300);
        donor.mechanitor.Notify_BandwidthChanged();
        relay.ApplyFromDialog(new RelayLink(donor, recipient, 5));
        Equal(3, network.Grant(relay), "fractional mech cost is rounded up before allocating spare");
        Totals(donor, recipient, 3, 5, "equal capacity debit and credit");
        Check(donor.mechanitor.ControlledPawns.Contains(donorMech) && recipient.mechanitor.ControlledPawns.Contains(newest), "original control audit sees transfer on both sides");
        Check(newest.GetOverseer() == recipient && oldest.GetOverseer() == recipient, "no ownership or group transfer");

        SetCapacity(donor, 4);
        Audits.Clear();
        donor.mechanitor.Notify_ApparelChanged();
        Totals(donor, recipient, 3, 3, "donor capacity drop settles before its native apparel audit");
        Check(Audits[donor].SequenceEqual([3]) && Audits[recipient].SequenceEqual([3]), "both audits observe only the final capacity");
        Check(donor.mechanitor.ControlledPawns.Contains(donorMech) && !recipient.mechanitor.ControlledPawns.Contains(newest), "donor retains own mech; recipient loses the newer unfunded mech");
        Check(newest.GetOverseer() == recipient && recipient.mechanitor.controlGroups[0].AssignedMechs.Any(a => a.pawn == newest), "native disconnect retains relation and group");
        Equal(95, Field<int>(typeof(CompOverseerSubject), "delayUntilFeralCheck", newest.OverseerSubject), "vanilla grace timer started for the disconnected mech");

        SetCapacity(donor, 10);
        donor.mechanitor.Notify_BandwidthChanged();
        Totals(donor, recipient, 5, 7, "capacity restoration resumes the original request");
        var donorBill = Bill(donor, 2);
        Tick();
        Equal(5, network.Grant(relay), "gift fits alongside existing gestation");
        SetCapacity(donor, 7);
        donor.mechanitor.Notify_BandwidthChanged();
        Totals(donor, recipient, 5, 4, "active native gestation reserves donor bandwidth");
        Check(!donorBill.suspended && donor.mechanitor.ControlledPawns.Contains(donorMech), "donor's bill and mech stay funded");
        var recipientBill = Bill(recipient, 2);
        recipient.mechanitor.Notify_BandwidthChanged();
        Check(recipientBill.suspended, "native gestator refuses unfunded bill");
        SetCapacity(donor, 10);
        donor.mechanitor.Notify_BandwidthChanged();
        Check(!recipientBill.suspended, "native gestator resumes once recipient can fund bill");

        var third = NewMechanitor("Elm", 0);
        var second = NewRelay();
        second.ApplyFromDialog(new RelayLink(donor, third, 5));
        Equal(0, network.Grant(second), "older relay has first claim on donor spare");
        var forward = NewRelay();
        forward.ApplyFromDialog(new RelayLink(recipient, third, 5));
        Equal(0, network.Grant(forward), "inbound capacity cannot be lent onward");
        int total = donor.mechanitor.TotalBandwidth + recipient.mechanitor.TotalBandwidth + third.mechanitor.TotalBandwidth;
        Equal(12, total, "no bandwidth is created by overlapping links");

        Power(relay, false);
        Equal(0, network.Grant(relay), "vanilla power signal pauses the link immediately");
        Equal(5, network.Grant(second), "other relay can claim released donor spare");
        Check(recipientBill.suspended && relay.Link.Requested == 5, "loss suspends bill but does not clear link configuration");
        Power(relay, true);
        Check(!recipientBill.suspended && recipient.mechanitor.ControlledPawns.Contains(newest), "restored power reconnects mechs and bill");

        MovePawn(donor, 1);
        Audits.Clear();
        Tick();
        Totals(donor, recipient, 5, 7, "moving the donor to a different map preserves the exact loan");
        Check(Audits.Count == 0 && recipient.mechanitor.ControlledPawns.Contains(newest) && !recipientBill.suspended,
            "changing maps neither audits nor disconnects funded mechs or bills");
        Check(relay.Link == new RelayLink(donor, recipient, 5) && relay.GetInspectString().Contains("实际转接带宽：5 / 5") &&
            !relay.GetInspectString().Contains("已暂停"), "off-map links display an active grant");

        MovePawn(recipient, 1);
        Tick();
        Totals(donor, recipient, 5, 7, "both elsewhere still use the original powered relay");
        var caravan = new Caravan();
        MovePawn(recipient, -1);
        Check(caravan.pawns.TryAdd(recipient, canMergeWithExistingStacks: false) && recipient.GetCaravan() == caravan && !recipient.Spawned,
            "recipient is held by a real vanilla caravan container");
        Tick();
        Totals(donor, recipient, 5, 7, "recipient in a caravan keeps the loan");
        recipient.mechanitor.Notify_BandwidthChanged();
        Check(!recipientBill.suspended && recipient.mechanitor.ControlledPawns.Contains(newest), "vanilla bandwidth audit still funds caravan recipient's mechs and bill");
        Power(relay, false);
        Totals(donor, recipient, 5, 2, "power loss revokes the caravan loan without requiring anyone to return");
        Check(network.Grant(second) == 5 && recipientBill.suspended && !recipient.mechanitor.ControlledPawns.Contains(newest),
            "remote endpoints and competing stations see the same settled allocation after power loss");
        Power(relay, true);
        Check(network.Grant(relay) == 5 && !recipientBill.suspended && recipient.mechanitor.ControlledPawns.Contains(newest),
            "power restoration reconnects off-map mechs and resumes the bill");

        MovePawn(donor, -1);
        Check(caravan.pawns.TryAdd(donor, canMergeWithExistingStacks: false) && donor.GetCaravan() == caravan, "donor joins the real caravan too");
        Tick();
        Totals(donor, recipient, 5, 7, "both caravan members keep the loan");
        MovePawn(donor, 0);
        MovePawn(recipient, 0);
        Tick();
        Totals(donor, recipient, 5, 7, "return to the relay map does not reapply the loan");
        var donorTracker = donor.mechanitor;
        donor.mechanitor = null;
        Tick();
        Equal(0, network.Grant(relay), "loss of the mechanitor tracker revokes the link");
        donor.mechanitor = donorTracker;
        Tick();
        Equal(5, network.Grant(relay), "restored mechanitor status resumes the saved link");
        OnMap(relay, -1);
        Tick();
        Equal(0, network.Grant(relay), "missing/despawned relay grants nothing");
        OnMap(relay, 0);
        network.Track(relay, keep: true);

        var panelFixture = NewRelay();
        Windows.Clear();
        var configure = panelFixture.GetGizmos().OfType<Command_Action>().Single(g => g.Label == "设置带宽转接");
        configure.ProcessInput(null);
        var dialog = (Dialog_RelayConfig)Windows.Single();
        Draw(dialog, "选择机械师");
        FloatMenu donorMenu = Windows.OfType<FloatMenu>().Single();
        Check(MenuOptions(donorMenu).Select(o => o.Label).Order().SequenceEqual(["Aster", "Birch", "Elm"]),
            "configuration picker lists local player mechanitors, not other maps or factions");
        MenuOptions(donorMenu).Single(o => o.Label == "Aster").Chosen(false, donorMenu);
        Windows.Remove(donorMenu);
        Draw(dialog, "选择机械师");
        FloatMenu recipientMenu = Windows.OfType<FloatMenu>().Single();
        Check(MenuOptions(recipientMenu).All(o => o.Label != "Aster"), "receiver picker excludes the selected donor");
        MenuOptions(recipientMenu).Single(o => o.Label == "Elm").Chosen(false, recipientMenu);
        Windows.Remove(recipientMenu);
        Draw(dialog, "Confirm", input: "-1");
        Check(Buttons.Contains(("Confirm", false)) && panelFixture.Link.Requested == 0, "negative input leaves confirm disabled and changes nothing");
        Draw(dialog, "Confirm", input: "2");
        Check(panelFixture.Link == new RelayLink(donor, third, 2), "gizmo dialogs drive actual pawn selection and integer confirmation");
        panelFixture.ApplyFromDialog(default);

        Windows.Clear();
        FloatMenuOption option = relay.GetFloatMenuOptions(donor).Single(o => o.Label == "设置带宽转接");
        Check(option.revalidateClickTarget == relay && !option.Disabled, "native building context option points to the relay");
        option.Chosen(colonistOrdering: false, floatMenu: null);
        Check(Windows.Single() is Dialog_RelayConfig && relay.GetInspectString().Contains("实际转接带宽：5 / 5"), "right-click opens the configuration window and real inspect reports the active grant");
        var editDialog = (Dialog_RelayConfig)Windows.Single();
        MovePawn(donor, 1);
        Windows.Clear();
        option.Chosen(colonistOrdering: false, floatMenu: null);
        Check(Windows.Count == 0 && !relay.GetFloatMenuOptions(donor).Any(o => o.Label == "设置带宽转接"), "off-map pawn cannot configure from a stale right-click");
        Draw(editDialog, "Confirm", input: "8");
        Check(Buttons.Contains(("对调", false)) && Buttons.Contains(("Confirm", true)) && relay.Link == new RelayLink(donor, recipient, 8) &&
            network.Grant(relay) == 5 && relay.GetInspectString().Contains("实际转接带宽：5 / 8"),
            "remote saved pair can edit its amount but cannot swap direction");
        relay.ApplyFromDialog(new RelayLink(recipient, donor, 2));
        relay.ApplyFromDialog(new RelayLink(donor, third, 2));
        Check(Windows.Count == 0 && relay.Link == new RelayLink(donor, recipient, 8), "off-map re-pairing and reversal are rejected without changing the existing link");
        relay.ApplyFromDialog(default);
        Check(Windows.Single() is Dialog_MessageBox && relay.Link.Requested == 8, "risky remote disconnect still requires confirmation");
        Field<Action>(typeof(Dialog_MessageBox), "buttonAAction", Windows.Single())();
        Check(relay.Link.Requested == 0 && recipientBill.suspended && !recipient.mechanitor.ControlledPawns.Contains(newest), "confirmed remote disconnect invokes the original audit");

        MovePawn(donor, 0);
        relay.ApplyFromDialog(new RelayLink(donor, recipient, 5));
        Equal(5, network.Grant(relay), "reconfigured original relay recovers its stable priority");
        RoundTrip(relay, donor, recipient);
        Check(Errors.Count == 0, "no game errors: " + string.Join(" | ", Errors));
        Console.WriteLine($"PASS: {checks} checks, real tracker, control/gestation, local configuration, cross-map and caravan links, power/map removal, and off-map Scribe references.");
        Console.WriteLine("Not exercised: Waylith rendering, construction/job scheduling, real world travel, complete game save/load.");
    }

    private static void Setup(string mod)
    {
        Hook(typeof(Log), nameof(Log.Error), nameof(RecordError), [typeof(string)]);
        Hook(typeof(Log), nameof(Log.Warning), nameof(RecordWarning), [typeof(string)]);
        Hook(typeof(Log), nameof(Log.Message), nameof(Skip), [typeof(string)]);
        Hook(typeof(ContentFinder<Texture2D>), nameof(ContentFinder<Texture2D>.Get), nameof(NoTexture), [typeof(string), typeof(bool)]);
        Hook(typeof(CompPowerTrader), "UpdateOverlays", nameof(Skip));
        Hook(typeof(CompPowerTrader), "StartSustainerPoweredIfInactive", nameof(Skip));
        Hook(typeof(Verse.Sound.SoundStarter), nameof(Verse.Sound.SoundStarter.PlayOneShot), nameof(Skip));
        Hook(typeof(Verse.Sound.SoundStarter), nameof(Verse.Sound.SoundStarter.PlayOneShotOnCamera), nameof(Skip));
        Hook(typeof(WindowStack), nameof(WindowStack.Add), nameof(CaptureWindow));
        Hook(typeof(FloatMenuOption), nameof(FloatMenuOption.SetSizeMode), nameof(Skip));
        Hook(typeof(LongEventHandler), nameof(LongEventHandler.ExecuteWhenFinished), nameof(Skip));
        Hook(typeof(Widgets), nameof(Widgets.Label), nameof(Skip), [typeof(Rect), typeof(string)]);
        Hook(typeof(Widgets), nameof(Widgets.Label), nameof(Skip), [typeof(Rect), typeof(TaggedString)]);
        Hook(typeof(Widgets), nameof(Widgets.ButtonText), nameof(ClickButton), [typeof(Rect), typeof(string), typeof(bool), typeof(bool), typeof(bool), typeof(TextAnchor?)]);
        Hook(typeof(Widgets), nameof(Widgets.TextField), nameof(TypeAmount), [typeof(Rect), typeof(string), typeof(int), typeof(Regex)]);
        Hook(typeof(BuildCopyCommandUtility), nameof(BuildCopyCommandUtility.FindAllowedDesignator), nameof(Skip));
        Hook(typeof(Gizmo_RoomStats), nameof(Gizmo_RoomStats.GetRoomToShowStatsFor), nameof(Skip));
        // Skip caravan UI/food/bed caches, not the actual ThingOwner add/remove or pawn ownership.
        Hook(typeof(Caravan), nameof(Caravan.Notify_PawnAdded), nameof(Skip));
        Hook(typeof(Caravan), nameof(Caravan.Notify_PawnRemoved), nameof(Skip));
        Hook(typeof(ColoredText), nameof(ColoredText.Resolve), nameof(Plain), [typeof(TaggedString)]);
        Hook(typeof(DefOfHelper), nameof(DefOfHelper.EnsureInitializedInCtor), nameof(Skip));
        Hook(typeof(MechanitorControlGroup), "SetWorkModeForPawn", nameof(Skip));
        Hook(typeof(Messages), nameof(Messages.Message), nameof(Skip), [typeof(string), typeof(MessageTypeDef), typeof(bool)]);

        Current.ProgramState = ProgramState.Playing;
        Set(typeof(Prefs), "data", null, RuntimeHelpers.GetUninitializedObject(typeof(PrefsData)));
        Set(typeof(ModsConfig), "biotechActive", null, true);
        Set(typeof(ModsConfig), "activeModsHashSet", null, new HashSet<string> { "ludeon.rimworld.biotech" });
        var metadata = (ModMetaData)RuntimeHelpers.GetUninitializedObject(typeof(ModMetaData));
        Set(typeof(ModMetaData), "packageIdLowerCase", metadata, "ludeon.rimworld.biotech");
        Set(typeof(ModLister), "modsByPackageIdIgnorePostfix", null, new Dictionary<string, List<ModMetaData>> { ["ludeon.rimworld.biotech"] = [metadata] });

        var language = new LoadedLanguage("ChineseSimplified");
        Set(typeof(LoadedLanguage), "dataIsLoaded", language, true);
        Set(typeof(LoadedLanguage), "workerInt", language, new LanguageWorker_English());
        LanguageDatabase.activeLanguage = language;
        LanguageDatabase.defaultLanguage = language;
        foreach (XElement entry in XDocument.Load(Path.Combine(mod, "Languages/ChineseSimplified/Keyed/BandwidthRelay.xml")).Root!.Elements())
            language.keyedReplacements.Add(entry.Name.LocalName, new LoadedLanguage.KeyedReplacement { key = entry.Name.LocalName, value = entry.Value });
        foreach (string key in new[] { "PowerNeeded", "PowerOutput", "Confirm", "GoBack", "CancelButton", "CommandBuildCopy", "CommandBuildCopyDesc", "CommandDesignateTogglePowerLabel", "CommandDesignateTogglePowerDesc" })
            language.keyedReplacements.Add(key, new LoadedLanguage.KeyedReplacement { key = key, value = key });

        var root = (Root_Play)RuntimeHelpers.GetUninitializedObject(typeof(Root_Play));
        var ui = (UIRoot_Play)RuntimeHelpers.GetUninitializedObject(typeof(UIRoot_Play));
        ui.windows = new WindowStack();
        root.uiRoot = ui;
        Set(typeof(Current), "rootInt", null, root);
        Add(new DesignationDef { defName = "TestDesignation" });
        NewGame(start: true);

        Seed(typeof(HediffDefOf), nameof(HediffDefOf.MechlinkImplant), new HediffDef { defName = "MechlinkImplant", hediffClass = typeof(Hediff) });
        Seed(typeof(PawnRelationDefOf), nameof(PawnRelationDefOf.Overseer), new PawnRelationDef
        {
            defName = "Overseer", reflexive = true, workerClass = typeof(PawnRelationWorker_Overseer)
        });
        Seed(typeof(StatDefOf), nameof(StatDefOf.MechBandwidth), Stat("MechBandwidth"));
        Seed(typeof(StatDefOf), nameof(StatDefOf.BandwidthCost), Stat("BandwidthCost"));
        Seed(typeof(StatDefOf), nameof(StatDefOf.MechControlGroups), Stat("MechControlGroups", 1));
        Seed(typeof(MechWorkModeDefOf), nameof(MechWorkModeDefOf.Work), new MechWorkModeDef { defName = "Work", label = "work" });
        Seed(typeof(SoundDefOf), nameof(SoundDefOf.Power_OnSmall), new SoundDef { defName = "Power_OnSmall" });
        Seed(typeof(SoundDefOf), nameof(SoundDefOf.Power_OffSmall), new SoundDef { defName = "Power_OffSmall" });
        Add(new DesignationCategoryDef { defName = "Biotech" });
        Add(new ResearchProjectDef { defName = "BasicMechtech" });
        Add(new EffecterDef { defName = "Repair" });
        Add(new TerrainAffordanceDef { defName = "Light" });
        Add(new StatDef { defName = "SellPriceFactor" });
        Seed(typeof(StatDefOf), nameof(StatDefOf.MaxHitPoints), Stat("MaxHitPoints", 150));
        Add(new StatDef { defName = "WorkToBuild" });
        Add(new StatDef { defName = "Mass" });
        Add(new StatDef { defName = "Flammability" });
        Add(new ThingDef { defName = "Filth_RubbleBuilding" });
        Add(new ThingDef { defName = "Steel" });
        Add(new ThingDef { defName = "ComponentIndustrial" });
        Add(new SoundDef { defName = "BulletImpact_Metal" });

        var content = (ModContentPack)RuntimeHelpers.GetUninitializedObject(typeof(ModContentPack));
        Set(typeof(ModContentPack), "rootDirInt", content, new DirectoryInfo(mod));
        Set(typeof(ModContentPack), "nameInt", content, "Bandwidth Relay QA");
        content.assemblies = new ModAssemblyHandler(content);
        content.assemblies.loadedAssemblies.Add(typeof(BandwidthRelayMod).Assembly);
        LoadedModManager.RunningModsListForReading.Add(content);
        GenTypes.ClearCache();
        var core = Xml(Path.Combine(Program.GameRoot, "Data/Core/Defs/ThingDefs_Buildings/Buildings_Base.xml"));
        var custom = Xml(Path.Combine(mod, PackageChecks.DefFile));
        XmlInheritance.TryRegisterAllFrom(core, null);
        XmlInheritance.TryRegisterAllFrom(custom, content);
        XmlInheritance.Resolve();
        relayDef = (ThingDef)DirectXmlToObjectNew.DefFromNodeNew(custom.xmlDoc.DocumentElement!.FirstChild!, custom);
        DirectXmlCrossRefLoader.ResolveAllWantedCrossReferences(FailMode.LogErrors);
        XmlInheritance.Clear();
        Add(relayDef);
        Check(relayDef.thingClass == typeof(Building_BandwidthRelay) && relayDef.BuildableByPlayer &&
            relayDef.researchPrerequisites.Single().defName == "BasicMechtech" && relayDef.costList.Count == 2 &&
            relayDef.costList[0].count == 200 && relayDef.costList[1].count == 4, "real XML inheritance and cross-reference loader produce a buildable def");

        var target = AccessTools.PropertyGetter(typeof(Pawn_MechanitorTracker), nameof(Pawn_MechanitorTracker.TotalBandwidth));
        Set(typeof(ModsConfig), "biotechActive", null, false);
        _ = new BandwidthRelayMod(content);
        Check(Harmony.GetPatchInfo(target)?.Owners.Contains(PatchId) != true, "missing Biotech does not patch any bandwidth getter");
        Set(typeof(ModsConfig), "biotechActive", null, true);
        _ = new BandwidthRelayMod(content);
        Check(Harmony.GetPatchInfo(target)?.Postfixes.Count(patch => patch.owner == PatchId) == 1, "actual mod installs its tracker getter patch");
        QA.Patch(AccessTools.Method(typeof(Pawn_MechanitorTracker), nameof(Pawn_MechanitorTracker.Notify_BandwidthChanged)),
            postfix: new HarmonyMethod(typeof(Driver), nameof(RecordAudit)) { priority = Priority.First });
    }

    private static void NewGame(bool start)
    {
        game = (Game)RuntimeHelpers.GetUninitializedObject(typeof(Game));
        Current.Game = game;
        Set(typeof(Game), "maps", game, new List<Map>());
        Set(typeof(Game), "tickManager", game, new TickManager());
        Set(typeof(Game), "uniqueIDsManager", game, new UniqueIDsManager());
        Set(typeof(Game), "questManager", game, new QuestManager());
        var world = (World)RuntimeHelpers.GetUninitializedObject(typeof(World));
        world.factionManager = new FactionManager();
        world.ideoManager = new IdeoManager();
        Player.ideos = new FactionIdeosTracker(Player);
        world.factionManager.Add(Player);
        world.factionManager.Add(Other);
        world.worldPawns = new WorldPawns();
        Set(typeof(Game), "worldInt", game, world);
        network = new RelayNetwork(game);
        Set(typeof(Game), "components", game, new List<GameComponent> { network });
        for (int i = 0; i < 2; i++)
        {
            var map = (Map)RuntimeHelpers.GetUninitializedObject(typeof(Map));
            map.listerThings = new ListerThings(ListerThingsUse.Global);
            map.mapPawns = new MapPawns(map);
            map.events = new MapEvents(map);
            map.lordManager = new Verse.AI.Group.LordManager(map);
            map.reservationManager = new Verse.AI.ReservationManager(map);
            map.physicalInteractionReservationManager = new PhysicalInteractionReservationManager();
            map.designationManager = new DesignationManager(map);
            game.Maps.Add(map);
        }
        if (start) network.StartedNewGame();
    }

    private static Building_BandwidthRelay NewRelay()
    {
        var relay = (Building_BandwidthRelay)ThingMaker.MakeThing(relayDef);
        Set(typeof(Thing), "factionInt", relay, Player);
        OnMap(relay, 0);
        Set(typeof(CompPowerTrader), "powerOnInt", relay.GetComp<CompPowerTrader>(), true);
        return relay;
    }

    private static Pawn NewMechanitor(string name, float bandwidth, sbyte map = 0, Faction? faction = null)
    {
        Pawn pawn = NewPawn(name, faction ?? Player);
        pawn.health.hediffSet.hediffs.Add(new Hediff { def = HediffDefOf.MechlinkImplant, pawn = pawn });
        pawn.mechanitor = new Pawn_MechanitorTracker(pawn);
        pawn.mechanitor.controlGroups.Add(new MechanitorControlGroup(pawn.mechanitor));
        MovePawn(pawn, map);
        SetCapacity(pawn, bandwidth);
        return pawn;
    }

    private static Pawn NewPawn(string name, Faction faction)
    {
        var pawn = (Pawn)RuntimeHelpers.GetUninitializedObject(typeof(Pawn));
        pawn.thingIDNumber = nextId++;
        pawn.stackCount = 1;
        pawn.def = new ThingDef { defName = name, label = name, category = ThingCategory.Pawn,
            race = new RaceProperties { lifeStageAges = [new LifeStageAge { def = new LifeStageDef { defName = "TestAdult" } }] } };
        pawn.kindDef = new PawnKindDef { defName = name, label = name, race = pawn.def };
        pawn.Name = new NameSingle(name);
        pawn.health = new Pawn_HealthTracker(pawn);
        pawn.relations = new Pawn_RelationsTracker(pawn);
        pawn.needs = new Pawn_NeedsTracker(pawn);
        pawn.ageTracker = new Pawn_AgeTracker(pawn);
        Set(typeof(Pawn_AgeTracker), "cachedLifeStageIndex", pawn.ageTracker, 0);
        Set(typeof(Thing), "factionInt", pawn, faction);
        OnMap(pawn, -1);
        return pawn;
    }

    private static Pawn Assign(Pawn mechanitor, float cost, int tick)
    {
        Pawn mech = NewPawn("TestMech", Player);
        mech.def.statBases = [new StatModifier { stat = StatDefOf.BandwidthCost, value = cost }];
        Set(typeof(ThingWithComps), "comps", mech, new List<ThingComp> {
            new CompOverseerSubject { parent = mech, props = new CompProperties_OverseerSubject { delayUntilFeralCheck = 95 } }
        });
        Set(typeof(TickManager), "ticksGameInt", Find.TickManager, tick);
        mechanitor.relations.AddDirectRelation(PawnRelationDefOf.Overseer, mech);
        return mech;
    }

    private static Bill_ProductionMech Bill(Pawn pawn, float cost)
    {
        var output = new ThingDef { defName = "BillMech", statBases = [new StatModifier { stat = StatDefOf.BandwidthCost, value = cost }] };
        var bill = new Bill_ProductionMech { recipe = new RecipeDef { products = [new ThingDefCountClass(output, 1)] } };
        Set(typeof(Bill_Mech), "boundPawn", bill, pawn);
        var gestator = (Building_MechGestator)RuntimeHelpers.GetUninitializedObject(typeof(Building_MechGestator));
        gestator.thingIDNumber = nextId++;
        gestator.def = new ThingDef { defName = "TestGestator", thingClass = typeof(Building_MechGestator), category = ThingCategory.Building, building = new BuildingProperties() };
        gestator.ActiveBill = bill;
        OnMap(gestator, 0);
        game.Maps[0].listerThings.Add(gestator);
        return bill;
    }

    private static void RoundTrip(Building_BandwidthRelay relay, Pawn donor, Pawn recipient)
    {
        string sandbox = Path.Combine(Path.GetTempPath(), "opencode", "bandwidth-relay-qa");
        Directory.CreateDirectory(sandbox);
        string file = Path.Combine(sandbox, $"relay-{Guid.NewGuid():N}.xml");
        try
        {
            MovePawn(donor, -1);
            MovePawn(recipient, 1);
            Check(new Caravan().pawns.TryAdd(donor, canMergeWithExistingStacks: false), "caravan donor is saved off-map");
            Tick();
            Equal(5, network.Grant(relay), "loan is active from caravan to another map when saving");
            Scribe.saver.InitSaving(file, "RelayFixture");
            relay.ExposeData();
            Scribe.saver.FinalizeSaving();
            XElement saved = XDocument.Load(file).Root!;
            Check((string?)saved.Element("bandwidthDonor") == donor.GetUniqueLoadID() &&
                (string?)saved.Element("bandwidthRecipient") == recipient.GetUniqueLoadID() &&
                (int?)saved.Element("bandwidthRequested") == 5, "Scribe keeps the existing reference fields and requested amount for remote pawns");

            NewGame(start: false);
            var loadedDonor = NewMechanitor("Aster", 10, map: -1);
            var loadedRecipient = NewMechanitor("Birch", 2, map: 1);
            loadedDonor.thingIDNumber = donor.thingIDNumber;
            loadedRecipient.thingIDNumber = recipient.thingIDNumber;
            Check(new Caravan().pawns.TryAdd(loadedDonor, canMergeWithExistingStacks: false), "restored donor is again held by a caravan");
            var oldest = Assign(loadedRecipient, 2, 100);
            var newest = Assign(loadedRecipient, 2, 200);
            loadedRecipient.mechanitor.ControlledPawns.Add(newest); // State already loaded by native Pawn_MechanitorTracker.ExposeData.
            Assign(loadedDonor, 2.2f, 300);
            Bill(loadedDonor, 2);
            var bill = Bill(loadedRecipient, 2);

            var restored = new Building_BandwidthRelay();
            Warnings.Clear();
            Scribe.loader.InitLoading(file);
            Scribe.loader.curParent = restored;
            var directory = Field<LoadedObjectDirectory>(typeof(CrossRefHandler), "loadedObjectDirectory", Scribe.loader.crossRefs);
            directory.RegisterLoaded(loadedDonor);
            directory.RegisterLoaded(loadedRecipient);
            Scribe.loader.crossRefs.RegisterForCrossRefResolve(restored);
            Scribe.loader.initer.RegisterForPostLoadInit(restored);
            restored.ExposeData();
            Scribe.loader.FinalizeLoading();
            Check(restored.Link == new RelayLink(loadedDonor, loadedRecipient, 5) && !Warnings.Any(text => text.Contains("resolve reference")),
                "Scribe resolves both exact load IDs to fresh pawn instances without fallback");
            Audits.Clear();
            loadedRecipient.mechanitor.Notify_PawnSpawned(respawningAfterLoad: true);
            Check(!Audits.ContainsKey(loadedRecipient) && loadedRecipient.mechanitor.ControlledPawns.Contains(newest), "pawn-before-building load audit cannot disconnect saved mechs");
            Set(typeof(Thing), "factionInt", restored, Player);
            OnMap(restored, 0);
            network.LoadedGame();
            Totals(loadedDonor, loadedRecipient, 5, 7, "finalized game reconstructs the off-map transfer before vanilla audits");
            Check(Audits[loadedRecipient].SequenceEqual([7]) && loadedRecipient.mechanitor.ControlledPawns.Contains(newest) && !bill.suspended,
                "recipient is audited once against the restored remote link");

            // Source map removal invokes this vanilla building callback, not DeSpawn; there is no intervening tick.
            MovePawn(loadedRecipient, -1);
            Check(loadedDonor.GetCaravan().pawns.TryAdd(loadedRecipient, canMergeWithExistingStacks: false), "both endpoints can be off-map during abandonment");
            Audits.Clear();
            restored.Notify_MyMapRemoved();
            Totals(loadedDonor, loadedRecipient, 10, 2, "discarding the source map removes the loan immediately");
            Check(restored.Discarded && network.Grant(restored) == 0 && Audits[loadedRecipient].SequenceEqual([2]) &&
                loadedRecipient.mechanitor.ControlledPawns.SequenceEqual([oldest]) && bill.suspended,
                "abandonment cannot leave a funded mech in the cache if the game saves before its next tick");
        }
        finally { File.Delete(file); }
    }

    private static void Power(Building_BandwidthRelay relay, bool on) => relay.GetComp<CompPowerTrader>().PowerOn = on;
    private static List<FloatMenuOption> MenuOptions(FloatMenu menu) => Field<List<FloatMenuOption>>(typeof(FloatMenu), "options", menu);

    private static void Draw(Dialog_RelayConfig dialog, string button, string? input = null, int index = 0)
    {
        Buttons.Clear();
        clickedButton = button;
        clickIndex = index;
        enteredAmount = input;
        dialog.DoWindowContents(new Rect(0f, 0f, 494f, 333f));
        enteredAmount = null;
        clickedButton = null;
    }

    private static void OnMap(Thing thing, sbyte index) => Set(typeof(Thing), "mapIndexOrState", thing, index);

    private static void MovePawn(Pawn pawn, sbyte index)
    {
        pawn.GetCaravan()?.pawns.Remove(pawn);
        if (pawn.Map is { } old) Field<List<Pawn>>(typeof(MapPawns), "pawnsSpawned", old.mapPawns).Remove(pawn);
        OnMap(pawn, index);
        if (index >= 0) Field<List<Pawn>>(typeof(MapPawns), "pawnsSpawned", game.Maps[index].mapPawns).Add(pawn);
    }

    private static void SetCapacity(Pawn pawn, float bandwidth) => pawn.def.statBases = [new StatModifier { stat = StatDefOf.MechBandwidth, value = bandwidth }];
    private static void Tick()
    {
        Set(typeof(TickManager), "ticksGameInt", Find.TickManager, 600);
        network.GameComponentTick();
    }

    private static void Totals(Pawn donor, Pawn recipient, int a, int b, string reason)
    {
        Equal(a, donor.mechanitor.TotalBandwidth, reason + " / donor");
        Equal(b, recipient.mechanitor.TotalBandwidth, reason + " / recipient");
    }

    private static StatDef Stat(string name, float value = 0) => new() { defName = name, defaultBaseValue = value, minValue = 0 };
    private static LoadableXmlAsset Xml(string path) => new(Path.GetFileName(path), File.ReadAllText(path));
    private static void Seed<T>(Type type, string name, T def) where T : Def, new() { Add(def); Set(type, name, null, def); }
    private static void Add<T>(T def) where T : Def, new() { def.ResolveDefNameHash(); DefDatabase<T>.Add(def); }
    private static void Set(Type type, string field, object? target, object? value) => AccessTools.Field(type, field).SetValue(target, value);
    private static T Field<T>(Type type, string field, object target) => (T)AccessTools.Field(type, field).GetValue(target)!;
    private static void Equal(int expected, int actual, string description) => Check(expected == actual, $"{description}: expected {expected}, got {actual}");
    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
        checks++;
    }

    private static void Hook(Type type, string method, string hook, Type[]? args = null) =>
        QA.Patch(AccessTools.DeclaredMethod(type, method, args), prefix: new HarmonyMethod(typeof(Driver), hook));
    public static bool Skip() => false;
    public static bool RecordError(string text) { Errors.Add(text); return false; }
    public static bool RecordWarning(string text) { Warnings.Add(text); return false; }
    public static bool NoTexture(ref Texture2D? __result) { __result = null; return false; }
    public static bool CaptureWindow(Window window) { Windows.Add(window); return false; }
    public static bool Plain(TaggedString taggedStr, ref string __result) { __result = taggedStr.RawText; return false; }
    public static bool ClickButton(string label, bool active, ref bool __result)
    {
        Buttons.Add((label, active));
        __result = active && label == clickedButton && clickIndex-- == 0;
        return false;
    }
    public static bool TypeAmount(string text, ref string __result) { __result = enteredAmount ?? text; return false; }
    public static void RecordAudit(Pawn_MechanitorTracker __instance, bool __runOriginal)
    {
        if (!__runOriginal) return;
        if (!Audits.TryGetValue(__instance.Pawn, out var totals)) Audits.Add(__instance.Pawn, totals = new List<int>());
        totals.Add(__instance.TotalBandwidth);
    }
}
