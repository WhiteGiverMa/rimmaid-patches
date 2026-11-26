using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml;
using System.Xml.Linq;
using ASEL;
using HarmonyLib;
using RimWorld;
using Verse;
using WhiteGiver.MonolynFemaleGay;

internal static class Driver
{
    private static readonly Harmony Qa = new("whitegiver.monolyn.femalegay.qa");
    private static readonly List<Pawn> Pawns = [];
    private static readonly List<string> Drawn = [], Errors = [], Warnings = [];
    private static string? click;
    private static bool expectContractError;
    private static PawnGenerationRequest captured;
    private static string config = null!;
    private static int checks;
    private static GeneDef femaleGay = null!, abstinence = null!, sterile = null!, otherGene = null!;
    private static XenotypeDef monolyn = null!, baseliner = null!;
    private static ThingDef race = null!, human = null!;
    private static TraitDef gay = null!, asexual = null!, bisexual = null!;
    private static UpstreamContract contract = null!;
    private static MonolynFemaleGayMod modInstance = null!;
    private static Monolyn upstreamInstance = null!;
    private static MonolynFemaleGaySettings settings => MonolynFemaleGayMod.Settings!;

    internal static void Run(string mod, string game, string asel, string scratch, Assembly patch)
    {
        Setup(mod, game, asel, scratch, patch);
        Package(mod, patch);
        Contract();

        // RED: invoke the native methods before the sidecar's startup class runs.
        Check(Current.Game == null && !MonolynFemaleGayPatcher.Ready, "RED: main menu without sidecar hooks");
        DefMode(abstinence, "RED: stock Def with abstinence on");
        MonolynModSettings.Abstinence = false;
        contract.Apply();
        DefMode(sterile, "RED: stock Def with abstinence off");
        MonolynModSettings.Abstinence = true;
        contract.Apply();
        DefMode(abstinence, "RED: stock Def restored");
        PawnKindDef monolynKind = new() { race = race };
        Request(monolynKind, null, null, null, false, "RED: stock generator receives unchanged request");

        MNThingDefOf.MNRace = new ThingDef { defName = "DriftedRace" };
        expectContractError = true;
        MonolynFemaleGayPatcher.Install();
        expectContractError = false;
        Check(!MonolynFemaleGayPatcher.Ready && Errors.Count == 1 && Errors[0].Contains("ASEL.MNThingDefOf.MNRace")
            && !Harmony.GetAllPatchedMethods().Any(m => Harmony.GetPatchInfo(m)?.Owners.Contains(MonolynFemaleGayPatcher.HarmonyId) == true),
            "drifted Def rejected and logged; no sidecar Harmony hooks installed");
        DefMode(abstinence, "failed binding did not alter stock template");
        Errors.Clear();
        MNThingDefOf.MNRace = race;
        RuntimeHelpers.RunClassConstructor(typeof(MonolynFemaleGayStartup).TypeHandle);
        Check(MonolynFemaleGayPatcher.Ready, "GREEN: startup installed actual Harmony hooks");
        Check(settings.forceFemaleGay && !MonolynModSettings.Abstinence, "first install wins over upstream default");
        Check(File.Exists(Path.Combine(config, "Mod_3742031864_Monolyn.xml")), "first install persisted stock setting");
        foreach (MethodInfo method in new[] { contract.ApplyMethod, contract.DrawMethod, contract.GenerateMethod })
            Check(Harmony.GetPatchInfo(method)?.Owners.Contains(MonolynFemaleGayPatcher.HarmonyId) == true,
                $"registered patch on {method.Name}");
        DefMode(femaleGay, "GREEN: main-menu startup replaces stock gene before pawn selection");
        contract.Apply(); contract.Apply();
        DefMode(femaleGay, "GREEN: repeated main-menu Apply is idempotent");

        Request(monolynKind, null, null, Gender.Female, true, "Monolyn race request");
        Request(new PawnKindDef { race = human }, monolyn, null, Gender.Female, true, "forced Monolyn xenotype request");
        Request(new PawnKindDef { race = human, xenotypeSet = SetOf(1f) }, null, null, Gender.Female, true, "fixed Monolyn kind xenotype");
        var alternative = new XenotypeDef { defName = "QA_Alternative" };
        var faction = new Faction { def = new FactionDef { xenotypeSet = SetOf(.5f, alternative) } };
        Request(new PawnKindDef { race = human, xenotypeSet = SetOf(1f), useFactionXenotypes = true }, null, null, null, false,
            "faction adds another possible xenotype", faction);
        Request(new PawnKindDef { race = human, xenotypeSet = SetOf(.5f) }, null, null, null, false, "probabilistic non-Monolyn kind");
        Request(new PawnKindDef { race = human }, null, null, null, false, "non-Monolyn request");
        Request(monolynKind, null, Gender.Male, Gender.Male, false, "explicit male request (stock baptism)");
        settings.forceFemaleGay = false;
        Request(monolynKind, null, null, null, false, "disabled Monolyn request");
        settings.forceFemaleGay = true;

        Current.Game = Uninitialized<Game>();
        Set(typeof(Game), nameof(Game.uniqueIDsManager), Current.Game, new UniqueIDsManager());
        GeneBehavior(scratch);
        Modes();
        Check(!Errors.Any(), "no game errors: " + string.Join("; ", Errors));
        Check(!Warnings.Any(), "no game warnings: " + string.Join("; ", Warnings));
        Console.WriteLine($"PASS: {checks} checks (RED baseline, GREEN package, contracts, startup, lifecycle, settings, genes and generation).");
    }

    private static void Setup(string mod, string game, string asel, string scratch, Assembly patch)
    {
        config = Path.Combine(scratch, "Config");
        Directory.CreateDirectory(config);
        Hook(AccessTools.PropertyGetter(typeof(GenFilePaths), nameof(GenFilePaths.ConfigFolderPath)), nameof(ConfigFolder));
        Hook(AccessTools.Method(typeof(Log), nameof(Log.Error), [typeof(string)]), nameof(Error));
        Hook(AccessTools.Method(typeof(Log), nameof(Log.Warning), [typeof(string)]), nameof(Warning));
        Hook(AccessTools.Method(typeof(Log), nameof(Log.Message), [typeof(string)]), nameof(Skip));
        Set(typeof(Prefs), "data", null, Uninitialized<PrefsData>());
        Hook(AccessTools.Method(typeof(DefOfHelper), nameof(DefOfHelper.EnsureInitializedInCtor)), nameof(Skip));
        Hook(AccessTools.PropertyGetter(typeof(PawnsFinder), nameof(PawnsFinder.All_AliveOrDead)), nameof(FindPawns));
        Hook(AccessTools.PropertyGetter(typeof(Pawn), nameof(Pawn.Drawer)), nameof(NoDrawer));
        Hook(AccessTools.PropertyGetter(typeof(Pawn), nameof(Pawn.Dead)), nameof(Living));
        Hook(AccessTools.Method(typeof(HediffSet), nameof(HediffSet.DirtyCache)), nameof(Skip));
        Hook(AccessTools.Method(typeof(Pawn_NeedsTracker), nameof(Pawn_NeedsTracker.AddOrRemoveNeedsAsAppropriate)), nameof(Skip));
        Hook(AccessTools.Method(typeof(Translator), nameof(Translator.TryTranslate), [typeof(string), typeof(TaggedString).MakeByRefType()]), nameof(Translate));
        Hook(AccessTools.Method(typeof(Listing_Standard), nameof(Listing_Standard.CheckboxLabeled),
            [typeof(string), typeof(bool).MakeByRefType(), typeof(string), typeof(float), typeof(float)]), nameof(Checkbox));
        Hook(AccessTools.Method(typeof(Listing_Standard), nameof(Listing_Standard.ButtonText),
            [typeof(string), typeof(string), typeof(float)]), nameof(Skip));
        Qa.Patch(AccessTools.Method(typeof(PawnGenerator), nameof(PawnGenerator.GeneratePawn), [typeof(PawnGenerationRequest)]),
            prefix: new HarmonyMethod(typeof(Driver), nameof(Capture)) { priority = Priority.Last });
        Set(typeof(ModsConfig), "biotechActive", null, true);
        Set(typeof(ModLister), "biotechInstalled", null, true);

        var stock = Pack(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(asel)!, "../..")), typeof(Monolyn).Assembly);
        var sidecar = Pack(mod, patch);
        upstreamInstance = Uninitialized<Monolyn>(); // do not install unrelated ASEL patches
        Set(typeof(Mod), "intContent", upstreamInstance, stock);
        upstreamInstance.GetSettings<MonolynModSettings>();
        modInstance = new MonolynFemaleGayMod(sidecar);
        var modClasses = (Dictionary<Type, Mod>)AccessTools.Field(typeof(LoadedModManager), "runningModClasses").GetValue(null)!;
        modClasses[typeof(Monolyn)] = upstreamInstance;
        modClasses[typeof(MonolynFemaleGayMod)] = modInstance;

        var traits = LoadXml(Path.Combine(game, "Data/Core/Defs/TraitDefs/Traits_Singular.xml"));
        gay = TraitDefOf.Gay = Add(ReadDef<TraitDef>(FindDef(traits, "TraitDef", "Gay")));
        asexual = TraitDefOf.Asexual = Add(ReadDef<TraitDef>(FindDef(traits, "TraitDef", "Asexual")));
        bisexual = TraitDefOf.Bisexual = Add(ReadDef<TraitDef>(FindDef(traits, "TraitDef", "Bisexual")));
        var mono = LoadXml(Path.Combine(stock.RootDir, "1.7/Defs/Genedefs/Monolyn.xml"));
        Add(ReadDef<GeneCategoryDef>(FindDef(mono, "GeneCategoryDef", "Monolyn")));
        Add(new StatDef { defName = "Fertility" });
        abstinence = MNGeneDefOf.HereditaryAbstinence = Add(ReadDef<GeneDef>(FindDef(mono, "GeneDef", "HereditaryAbstinence")));
        var health = LoadXml(Path.Combine(game, "Data/Biotech/Defs/GeneDefs/GeneDefs_Health.xml"));
        // The stock category ancestor only supplies display metadata irrelevant to this fixture.
        var stockSterile = (XmlElement)FindDef(health, "GeneDef", "Sterile");
        stockSterile.RemoveAttribute("ParentName");
        sterile = MNGeneDefOf.Sterile = Add(ReadDef<GeneDef>(stockSterile));
        femaleGay = Add(ReadDef<GeneDef>(LoadXml(Path.Combine(mod, "1.7/Defs/GeneDefs/MonolynFemaleGay.xml")).DocumentElement!.FirstChild!));
        DirectXmlCrossRefLoader.ResolveAllWantedCrossReferences(FailMode.LogErrors);
        DirectXmlCrossRefLoader.Clear();
        // Minimal race/xenotype definitions: don't instantiate HAR or the native render graph.
        otherGene = Add(new GeneDef { defName = "QA_UnrelatedGene" });
        monolyn = MNXenotypeDefOf.MonolynXenotype = Add(new XenotypeDef
        {
            defName = "MonolynXenotype", inheritable = true, genes = [otherGene, abstinence]
        });
        baseliner = XenotypeDefOf.Baseliner = Add(new XenotypeDef { defName = "Baseliner", inheritable = true });
        race = MNThingDefOf.MNRace = new ThingDef { defName = "MNRace", race = new RaceProperties { intelligence = Intelligence.Humanlike } };
        human = new ThingDef { defName = "Human", race = race.race };
        BeardDefOf.NoBeard = new BeardDef { defName = "NoBeard" };
        BodyTypeDefOf.Male = new BodyTypeDef { defName = "Male" };
        BodyTypeDefOf.Thin = new BodyTypeDef { defName = "Thin" };
        BodyTypeDefOf.Female = new BodyTypeDef { defName = "Female" };
        contract = UpstreamContract.Bind();
    }

    private static void Package(string mod, Assembly patch)
    {
        XElement about = XDocument.Load(Path.Combine(mod, "About/About.xml")).Root!;
        Equal("whitegiver.monolyn.femalegay", (string?)about.Element("packageId"), "package ID");
        Equal("1.0.0", (string?)about.Element("modVersion"), "version");
        Equal("WhiteGiverMa", (string?)about.Element("author"), "author");
        Check(about.Element("supportedVersions")!.Elements().Select(x => x.Value).SequenceEqual(["1.7"]), "RimWorld version");
        Check(about.Element("modDependencies")!.Elements().Select(x => x.Element("packageId")!.Value).SequenceEqual(
            ["brrainz.harmony", "Ludeon.RimWorld.Biotech", "ASEL.MonolynRace"]), "required dependencies");
        Check(about.Element("loadAfter")!.Elements().Select(x => x.Value).SequenceEqual(
            ["brrainz.harmony", "Ludeon.RimWorld.Biotech", "erdelf.HumanoidAlienRaces", "ASEL.MonolynRace", "Jamesli.MonolynSamePawnBaptism"]), "load order");
        Check(XDocument.Load(Path.Combine(mod, "LoadFolders.xml")).Root!.Element("v1.7")!.Elements().Select(x => x.Value)
            .SequenceEqual(["/", "1.7"]), "load folders");
        Equal("WG_MonolynFemaleGay", femaleGay.defName, "GeneDef name");
        Check(femaleGay.geneClass == patch.GetType("WhiteGiver.MonolynFemaleGay.Gene_MonolynFemaleGay", throwOnError: true), "XML class resolves to released DLL");
        Equal("Monolyn", femaleGay.displayCategory.defName, "GeneDef display category");
        Equal(abstinence.iconPath, femaleGay.iconPath, "inherits upstream icon");
        Check(femaleGay.forcedTraits.Select(t => t.def).SequenceEqual([gay]), "forced Gay");
        Check(femaleGay.suppressedTraits.Select(t => t.def).SequenceEqual([asexual, bisexual]), "suppressed Asexual and Bisexual");
        Equal(0f, femaleGay.statFactors.Single(f => f.stat.defName == "Fertility").value, "Fertility = 0");
        Check(femaleGay.sterilize && femaleGay.biostatMet == sterile.biostatMet && sterile.biostatMet == 1, "sterility and +1 metabolism");
        Check(!femaleGay.canGenerateInGeneSet && femaleGay.missingGeneRomanceChanceFactor == 1f && femaleGay.lovinMTBFactor == 1f,
            "no random genepack, romance or lovin penalty");
        var keys = new[] { "SettingsCategory", "EnabledLabel", "EnabledTooltip", "Explanation", "StatusFemaleGay", "StatusAbstinence", "StatusSterile", "Unavailable" }
            .Select(k => "WGMonolynFemaleGay." + k).ToArray();
        foreach (string language in new[] { "English", "ChineseSimplified" })
            Keys(Path.Combine(mod, $"Languages/{language}/Keyed/MonolynFemaleGay.xml"), keys);
        Keys(Path.Combine(mod, "Languages/ChineseSimplified/DefInjected/GeneDef/MonolynFemaleGay.xml"),
            ["WG_MonolynFemaleGay.label", "WG_MonolynFemaleGay.description", "WG_MonolynFemaleGay.customEffectDescriptions.0"]);
    }

    private static void Contract()
    {
        Equal(typeof(Monolyn), contract.ModType, "actual upstream Mod type");
        Equal(typeof(void), contract.ApplyMethod.ReturnType, "actual upstream ApplyGenderPatch signature");
        Equal(typeof(Listing_Standard), contract.DrawMethod.GetParameters().Single().ParameterType, "actual stock draw signature");
        Reject(() => UpstreamContract.RequireMethod(typeof(Monolyn), "RenamedApplyGenderPatch", true, typeof(void)), "missing method");
        Reject(() => UpstreamContract.RequireMethod(typeof(Monolyn), "ApplyGenderPatch", false, typeof(void)), "wrong staticness");
        Reject(() => UpstreamContract.RequireMethod(typeof(Monolyn), "DrawFirstTabSettings", false, typeof(bool), typeof(Listing_Standard)), "wrong return type");
        Reject(() => UpstreamContract.RequireMethod(typeof(Monolyn), "DrawFirstTabSettings", false, typeof(void), typeof(string)), "wrong parameter type");
        Reject(() => UpstreamContract.RequireField(typeof(MonolynModSettings), "Abstinence", typeof(string)), "wrong field type");
        Reject(() => UpstreamContract.RequireField(typeof(MonolynModSettings), "Missing", typeof(bool)), "missing field");
    }

    private static void GeneBehavior(string scratch)
    {
        foreach (TraitDef original in new[] { asexual, bisexual })
        {
            Pawn pawn = PawnOf(Gender.Male);
            var trait = new Trait(original);
            pawn.story.traits.GainTrait(trait);
            Gene gene = pawn.genes.AddGene(femaleGay, false);
            Equal(Gender.Female, pawn.gender, "male becomes female");
            Equal(Gender.Male, OriginalGender(gene), "gene records male");
            Check(pawn.story.traits.GetTrait(gay)?.sourceGene == gene && trait.Suppressed && trait.suppressedByGene == gene,
                $"Gay is gene-sourced and suppresses {original.defName}");
            Check(pawn.style.beardDef == BeardDefOf.NoBeard && pawn.story.bodyType == BodyTypeDefOf.Thin, "beard removed and male body normalized");
            pawn.genes.RemoveGene(gene);
            Check(pawn.gender == Gender.Male && !pawn.story.traits.HasTrait(gay) && !trait.Suppressed, $"restores male and {original.defName}");
        }

        Pawn female = PawnOf(Gender.Female);
        Gene femaleGene = female.genes.AddGene(femaleGay, false);
        Check(female.gender == Gender.Female && female.story.traits.GetTrait(gay)?.sourceGene == femaleGene && female.story.bodyType == BodyTypeDefOf.Female,
            "female keeps gender and body and becomes Gay");
        Pawn alreadyGay = PawnOf(Gender.Female);
        var naturalGay = new Trait(gay);
        alreadyGay.story.traits.GainTrait(naturalGay);
        Gene redundant = alreadyGay.genes.AddGene(femaleGay, false);
        alreadyGay.genes.RemoveGene(redundant);
        Check(alreadyGay.story.traits.GetTrait(gay) == naturalGay && naturalGay.sourceGene == null, "native Gay is not replaced or removed");
        Pawn unsexed = PawnOf(Gender.None);
        Gene unsexedGene = unsexed.genes.AddGene(femaleGay, false);
        Check(unsexed.gender == Gender.Female && OriginalGender(unsexedGene) == Gender.None, "genderless carrier becomes female without recording None");
        unsexed.genes.RemoveGene(unsexedGene);
        Equal(Gender.Female, unsexed.gender, "undefined original gender does not overwrite restored state");

        Pawn saved = PawnOf(Gender.Male);
        Gene savedGene = saved.genes.AddGene(femaleGay, false);
        string xml = Path.Combine(scratch, "gene.xml");
        Scribe.saver.InitSaving(xml, "gene");
        savedGene.ExposeData();
        Scribe.saver.FinalizeSaving();
        Equal("Male", (string?)XDocument.Load(xml).Root!.Element("originalGender"), "Scribe writes original gender");
        Scribe.loader.InitLoading(xml);
        var restored = new Gene_MonolynFemaleGay();
        try
        {
            Scribe.loader.curParent = restored;
            restored.ExposeData();
        }
        finally { Scribe.ForceStop(); }
        Equal(Gender.Male, OriginalGender(restored), "Scribe reads original gender back");
    }

    private static void Modes()
    {
        Pawn pawn = PawnOf(Gender.Male), non = PawnOf(Gender.Male), slipped = PawnOf(Gender.Male);
        Set(typeof(Pawn_GeneTracker), "xenotype", pawn.genes, monolyn);
        Pawns.AddRange([pawn, non, slipped]);
        Pawn starting = PawnOf(Gender.Male);
        starting.genes.SetXenotype(monolyn);
        Current.Game.InitData = new GameInitData { startingAndOptionalPawns = [starting] };

        settings.forceFemaleGay = false;
        MonolynModSettings.Abstinence = true;
        contract.Apply();
        Mode(pawn, abstinence, Gender.None, asexual, "stock abstinence");
        Mode(starting, abstinence, Gender.None, asexual, "already generated pawn in starting selection list");
        SetClick("WGMonolynFemaleGay.EnabledLabel");
        DrawUpstream();
        Check(settings.forceFemaleGay && !MonolynModSettings.Abstinence, "stock page sidecar toggle wins");
        Mode(pawn, femaleGay, Gender.Female, gay, "from abstinence to female/gay");
        Mode(starting, femaleGay, Gender.Female, gay, "already generated starting pawn switches immediately");
        Equal(Gender.Male, OriginalGender(pawn.genes.GetGene(femaleGay)), "switch captured restored original male");
        slipped.genes.AddGene(femaleGay, true);
        Equal(Gender.Female, slipped.gender, "non-Monolyn implanted carrier takes gene effect");
        Pawn baptism = PawnOf(Gender.Male);
        baptism.genes.SetXenotype(monolyn);
        Mode(baptism, femaleGay, Gender.Female, gay, "native SetXenotype ingestion used by generation and upstream baptism");
        Pawn samePawn = PawnOf(Gender.Male);
        foreach (GeneDef def in monolyn.genes) samePawn.genes.AddGene(def, false);
        Mode(samePawn, femaleGay, Gender.Female, gay, "append Def genes as same-pawn baptism does");
        pawn.gender = Gender.None;
        contract.Apply(); contract.Apply();
        Mode(pawn, femaleGay, Gender.Female, gay, "repairs gender without duplicate genes/traits");
        Equal(Gender.Male, OriginalGender(pawn.genes.GetGene(femaleGay)), "gender correction does not overwrite original");

        SetClick("AbstinenceSetting");
        DrawUpstream();
        Check(!settings.forceFemaleGay && MonolynModSettings.Abstinence, "explicit upstream UI wins over sidecar");
        Mode(pawn, abstinence, Gender.None, asexual, "direct female/gay to upstream abstinence");
        Check(slipped.gender == Gender.Male && slipped.genes.GenesListForReading.Count == 0 && !slipped.story.traits.HasTrait(gay),
            "disabled mode cleans a non-Monolyn xenogene carrier");
        Equal("False", Setting("Mod_MonolynFemaleGay_MonolynFemaleGayMod.xml", "forceFemaleGay"), "upstream toggle writes sidecar setting");
        string stockPath = Path.Combine(config, "Mod_3742031864_Monolyn.xml");
        Check(File.Exists(stockPath) && Setting("Mod_3742031864_Monolyn.xml", "MonolynAbstinence") is null or "True",
            "explicit stock toggle persists abstinence (true is the Scribe default and may be omitted)");
        Check(!new MonolynFemaleGayMod(modInstance.Content).GetSettings<MonolynFemaleGaySettings>().forceFemaleGay,
            "sidecar setting round-trips through Scribe");
        contract.Apply();
        Mode(pawn, abstinence, Gender.None, asexual, "non-UI apply keeps explicit stock mode");

        SetClick("AbstinenceSetting");
        DrawUpstream();
        Mode(pawn, sterile, Gender.Male, null, "abstinence off restores original male");
        SetClick("WGMonolynFemaleGay.EnabledLabel");
        MonolynFemaleGayPatcher.DrawToggle(Uninitialized<Listing_Standard>());
        Mode(pawn, femaleGay, Gender.Female, gay, "standalone page toggle uses shared setting");
        SetClick("WGMonolynFemaleGay.EnabledLabel");
        MonolynFemaleGayPatcher.DrawToggle(Uninitialized<Listing_Standard>());
        Mode(pawn, sterile, Gender.Male, null, "both settings off restores male and Sterile");
        SetClick("AbstinenceSetting");
        DrawUpstream();
        Mode(pawn, abstinence, Gender.None, asexual, "can switch from Sterile to stock abstinence again");
        Check(non.gender == Gender.Male && non.genes.GenesListForReading.Count == 0 && !non.story.traits.allTraits.Any(), "ordinary non-Monolyn untouched");
    }

    private static void Mode(Pawn pawn, GeneDef desired, Gender gender, TraitDef? trait, string message)
    {
        Check(pawn.gender == gender && pawn.genes.GenesListForReading.Where(g => IsModeGene(g.def)).Select(g => g.def).SequenceEqual([desired]), message + ": pawn gender and gene");
        Check(pawn.story.traits.allTraits.Where(t => t.def == gay || t.def == asexual || t.def == bisexual).Select(t => t.def)
            .SequenceEqual(trait == null ? [] : [trait]), message + ": exactly expected orientation");
        DefMode(desired, message + ": xenotype template");
    }

    private static void DefMode(GeneDef gene, string message) => Check(monolyn.genes.SequenceEqual([otherGene, gene]), message);
    private static bool IsModeGene(GeneDef def) => def == femaleGay || def == sterile || def == abstinence;
    private static Gender OriginalGender(Gene gene) => (Gender)AccessTools.Field(gene.GetType(), "originalGender").GetValue(gene)!;
    private static XenotypeSet SetOf(float chance, XenotypeDef? xenotype = null)
    {
        var set = new XenotypeSet();
        Set(typeof(XenotypeSet), "xenotypeChances", set, new List<XenotypeChance> { new() { xenotype = xenotype ?? monolyn, chance = chance } });
        return set;
    }

    private static void Request(PawnKindDef kind, XenotypeDef? forced, Gender? specified, Gender? expected, bool allowGay, string label, Faction? faction = null)
    {
        contract.GenerateMethod.Invoke(null, [new PawnGenerationRequest(kind, faction, allowGay: false, forcedXenotype: forced, fixedGender: specified)]);
        Check(captured.FixedGender == expected && captured.AllowGay == allowGay, label + ": actual Harmony request at generator boundary");
    }

    private static Pawn PawnOf(Gender gender)
    {
        var pawn = new Pawn { def = human, gender = gender };
        pawn.story = new Pawn_StoryTracker(pawn) { bodyType = gender == Gender.Male ? BodyTypeDefOf.Male : BodyTypeDefOf.Female };
        pawn.style = new Pawn_StyleTracker(pawn) { beardDef = gender == Gender.Male ? new BeardDef { defName = "QA_Beard" } : BeardDefOf.NoBeard };
        pawn.genes = new Pawn_GeneTracker(pawn);
        pawn.needs = new Pawn_NeedsTracker(pawn);
        pawn.health = new Pawn_HealthTracker(pawn);
        return pawn;
    }

    private static void DrawUpstream()
    {
        contract.DrawMethod.Invoke(upstreamInstance, [Uninitialized<Listing_Standard>()]);
        Check(Drawn.Contains("AbstinenceSetting") && Drawn.Contains("WGMonolynFemaleGay.EnabledLabel") && click == null, "both toggles drawn and intended click consumed");
    }

    private static void SetClick(string label) { Drawn.Clear(); click = label; }
    private static string? Setting(string file, string key) => XDocument.Load(Path.Combine(config, file)).Descendants(key).SingleOrDefault()?.Value;
    private static void Hook(MethodBase method, string prefix) => Qa.Patch(method, prefix: new HarmonyMethod(typeof(Driver), prefix));
    private static bool ConfigFolder(ref string __result) { __result = config; return false; }
    private static bool FindPawns(ref List<Pawn> __result) { __result = Pawns; return false; }
    private static bool Capture(PawnGenerationRequest __0) { captured = __0; return false; }
    private static bool Error(string __0)
    {
        Errors.Add(__0);
        if (expectContractError) Console.WriteLine("EXPECTED CONTRACT ERROR: " + __0.Split('\n')[0]);
        else Console.Error.WriteLine("GAME ERROR: " + __0);
        return false;
    }
    private static bool Warning(string __0) { Warnings.Add(__0); Console.Error.WriteLine("GAME WARNING: " + __0); return false; }
    private static bool Skip() => false;
    private static bool Living(ref bool __result) { __result = false; return false; }
    private static bool NoDrawer(ref Pawn_DrawTracker? __result) { __result = null; return false; }
    private static bool Translate(string __0, ref TaggedString __1, ref bool __result) { __1 = __0; __result = true; return false; }
    private static bool Checkbox(string __0, ref bool __1)
    {
        Drawn.Add(__0);
        if (__0 == click) { __1 = !__1; click = null; }
        return false;
    }

    private static T Uninitialized<T>() where T : class => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
    private static void Set(Type type, string field, object? instance, object value) => AccessTools.Field(type, field).SetValue(instance, value);
    private static ModContentPack Pack(string directory, Assembly assembly)
    {
        var pack = Uninitialized<ModContentPack>();
        Set(typeof(ModContentPack), "rootDirInt", pack, new DirectoryInfo(directory));
        pack.assemblies = new ModAssemblyHandler(pack);
        pack.assemblies.loadedAssemblies.Add(assembly);
        LoadedModManager.RunningModsListForReading.Add(pack);
        return pack;
    }
    private static XmlDocument LoadXml(string path) { var doc = new XmlDocument(); doc.Load(path); return doc; }
    private static XmlNode FindDef(XmlDocument doc, string type, string name) => doc.SelectSingleNode($"/Defs/{type}[defName='{name}']")!;
    private static T ReadDef<T>(XmlNode node) => DirectXmlToObject.ObjectFromXml<T>(node, doPostLoad: false);
    private static T Add<T>(T def) where T : Def { DefDatabase<T>.Add(def); return def; }
    private static void Keys(string path, string[] expected)
    {
        XElement root = XDocument.Load(path).Root!;
        Check(root.Name == "LanguageData" && root.Elements().All(x => !string.IsNullOrWhiteSpace(x.Value))
            && root.Elements().Select(x => x.Name.LocalName).Order().SequenceEqual(expected.Order()), $"complete translations: {path}");
    }
    private static void Reject(Action action, string context)
    {
        try { action(); }
        catch (InvalidOperationException) { Check(true, "contract rejects " + context); return; }
        throw new InvalidOperationException("Contract accepted " + context);
    }
    private static void Equal<T>(T expected, T actual, string label) => Check(EqualityComparer<T>.Default.Equals(expected, actual), $"{label} (expected {expected}, got {actual})");
    private static void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException("FAIL: " + label);
        ++checks;
        Console.WriteLine("PASS: " + label);
    }
}
