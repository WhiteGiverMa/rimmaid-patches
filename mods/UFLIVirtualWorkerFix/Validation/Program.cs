using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using HarmonyLib;
using Mono.Cecil;
using RimWorld;
using Verse;
using WhiteGiver.UFLI.VirtualWorkerFix;

internal static class Bootstrap
{
    internal const string DefaultGameRoot = @"A:\SteamLibrary\steamapps\common\RimWorld";
    internal const string DefaultHarmonyDirectory = @"A:\SteamLibrary\steamapps\workshop\content\294100\2009463077\1.7\Assemblies";
    internal const string DefaultUfliDll = @"A:\SteamLibrary\steamapps\workshop\content\294100\3651799915\1.7\Assemblies\UFRecipes.dll";
    internal const string DefaultQa = @"C:\Users\A1337\AppData\Local\Temp\opencode\ufli-virtualworker-fix-20261108";

    internal static readonly string GameRoot =
        Environment.GetEnvironmentVariable("RIMWORLD_ROOT") ?? DefaultGameRoot;
    internal static readonly string HarmonyDirectory =
        Environment.GetEnvironmentVariable("HARMONY_DIRECTORY") ?? DefaultHarmonyDirectory;
    internal static readonly string UfliDll =
        Environment.GetEnvironmentVariable("UFLI_DLL") ?? DefaultUfliDll;
    internal static string QaRoot =
        Environment.GetEnvironmentVariable("UFLI_QA_DIR") ?? DefaultQa;
    internal static string Bound = Path.Combine(QaRoot, "bound");

    // Runs before Main is JIT-compiled, so the resolver exists before the
    // Validation assembly's Assembly-CSharp reference is resolved.
    [ModuleInitializer]
    internal static void Init()
    {
        AssemblyLoadContext.Default.Resolving += Resolve;
    }

    private static Assembly? Resolve(AssemblyLoadContext context, AssemblyName name)
    {
        foreach (Assembly loaded in context.Assemblies)
        {
            if (string.Equals(loaded.GetName().Name, name.Name, StringComparison.OrdinalIgnoreCase))
                return loaded;
        }

        foreach (string directory in new[] { Bound, GameRoot, HarmonyDirectory })
        {
            string candidate = Path.Combine(directory, name.Name + ".dll");
            if (File.Exists(candidate)) return context.LoadFromAssemblyPath(candidate);
        }
        return null;
    }
}

internal static class Entry
{
    private static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--help")
        {
            Console.WriteLine("Usage: Validation bind|red|green [QA_DIRECTORY]");
            Console.WriteLine("Only bind writes QA copies; no game process, save, Workshop file or installed assembly is changed.");
            return 0;
        }

        if (args.Length < 1 || args.Length > 2 || args[0] is not ("bind" or "red" or "green"))
        {
            Console.Error.WriteLine("Invalid arguments; use --help.");
            return 1;
        }

        string qa = Path.GetFullPath(args.Length == 2 ? args[1] : Bootstrap.DefaultQa);
        Bootstrap.QaRoot = qa;
        Bootstrap.Bound = Path.Combine(qa, "bound");

        try
        {
            if (args[0] == "bind") Bind(qa);
            else Driver.Run(args[0]);
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static void Bind(string qa)
    {
        string bound = Path.Combine(qa, "bound");
        Directory.CreateDirectory(bound);

        if (bound.StartsWith(Bootstrap.GameRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("QA output must be outside the installed game.");
        if (bound.StartsWith(Path.GetDirectoryName(Bootstrap.UfliDll)! + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("QA output must be outside the Workshop mod.");

        RewriteUnityReferences(Path.Combine(Bootstrap.GameRoot, "Assembly-CSharp.dll"), Path.Combine(bound, "Assembly-CSharp.dll"));
        RewriteUnityReferences(Bootstrap.UfliDll, Path.Combine(bound, "UFRecipes.dll"));

        File.WriteAllText(Path.Combine(qa, "bind.txt"),
            $"game={Bootstrap.GameRoot}\nufli={Bootstrap.UfliDll}\nharmony={Bootstrap.HarmonyDirectory}\nbound={bound}\n");
        Console.WriteLine("PASS: QA-only Unity reference binding; real game and UFRecipes method bodies remain unchanged.");
    }

    private static void RewriteUnityReferences(string source, string destination)
    {
        using var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(Bootstrap.GameRoot);
        resolver.AddSearchDirectory(Bootstrap.HarmonyDirectory);
        resolver.AddSearchDirectory(Path.GetDirectoryName(Bootstrap.UfliDll)!);
        using AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(
            source, new ReaderParameters { AssemblyResolver = resolver });
        foreach (AssemblyNameReference reference in assembly.MainModule.AssemblyReferences)
        {
            if (!reference.Name.StartsWith("UnityEngine", StringComparison.Ordinal)) continue;
            reference.Name = "UnityStubs";
            reference.Version = new Version(1, 0, 0, 0);
        }
        assembly.Write(destination);
    }
}

internal static class Driver
{
    private const BindingFlags StaticAny = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags InstanceAny = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const string ValidationHarmonyId = "validation.ufli.virtualworkerfix";

    private static readonly List<string> Messages = new();

    public static void Run(string mode)
    {
        Assembly game = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(Bootstrap.Bound, "Assembly-CSharp.dll"));
        Assembly ufli = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(Bootstrap.Bound, "UFRecipes.dll"));

        Type logType = game.GetType("Verse.Log", true)!;
        HookLog(logType, "Message", nameof(LogMessage));
        HookLog(logType, "Warning", nameof(LogWarning));
        HookLog(logType, "Error", nameof(LogError));
        HookLog(logType, "ErrorOnce", nameof(LogErrorOnce));

        SetupFakeRimWorldPaths(game);

        Harmony validationHarmony = new Harmony(ValidationHarmonyId);
        PatchFalse(validationHarmony, game, "RimWorld.PawnUtility", "EverBeenColonistOrTameAnimal");
        PatchFalse(validationHarmony, game, "RimWorld.PawnUtility", "EverBeenQuestLodger");

        // Minimal global Game so Pawn.CombinedDisabledWorkTags -> QuestUtility can run.
        var fakeGame = (Game)RuntimeHelpers.GetUninitializedObject(typeof(Game));
        fakeGame.questManager = new QuestManager();
        fakeGame.tickManager = null!;
        Current.Game = fakeGame;

        Type ufliPool = ufli.GetType("UFRecipes.MapComponent_UFAutoVirtualWorkerPool", true)!;
        Type ideoMode = ufli.GetType("UFRecipes.AutoWorkerIdeoMode", true)!;

        SkillDef crafting = NewSkill("Crafting", WorkTags.Crafting);
        SkillDef cooking = NewSkill("Cooking", WorkTags.Cooking);
        typeof(SkillDefOf).GetField("Crafting", StaticAny)!.SetValue(null, crafting);

        StatDef butchery = NewButcheryStat(crafting);

        if (mode == "red")
        {
            Pawn pawn = NewPawn(crafting, cooking, disablingBackstory: true);
            InvokeRefresh(ufliPool, ideoMode, pawn, crafting, 15);

            SkillRecord craft = pawn.skills.GetSkill(crafting);
            Check(craft.TotallyDisabled, "RED: Crafting should be TotallyDisabled with a Crafting-disabling backstory.");
            Check(craft.Level == 0, $"RED: Crafting level should read 0, got {craft.Level}.");
            Check(butchery.Worker.IsDisabledFor(pawn), "RED: ButcheryMechanoidEfficiency should be disabled for the pawn.");
            float value = Evaluate(butchery, pawn);
            Check(Math.Abs(value - 0.75f) < 0.0001f, $"RED: efficiency should be 0.75, got {value}.");
            Check(Messages.Any(m => m.Contains("disabled stat ButcheryMechanoidEfficiency", StringComparison.Ordinal)),
                "RED: the dev-mode 'disabled stat ButcheryMechanoidEfficiency' error was not observed.");

            Console.WriteLine($"RED: TotallyDisabled={craft.TotallyDisabled} Level={craft.Level} " +
                              $"IsDisabledFor={butchery.Worker.IsDisabledFor(pawn)} efficiency={value:0.000}");
            return;
        }

        VirtualWorkerFix.Apply();

        MethodInfo tryAcquire = ufliPool.GetMethod("TryAcquire", InstanceAny)
            ?? throw new MissingMethodException("UFRecipes.MapComponent_UFAutoVirtualWorkerPool.TryAcquire");
        Patches? patches = Harmony.GetPatchInfo(tryAcquire);
        Check(patches != null && patches.Owners.Contains(VirtualWorkerFix.HarmonyId),
            "GREEN: TryAcquire was not patched by " + VirtualWorkerFix.HarmonyId + ".");

        Pawn greenPawn = NewPawn(crafting, cooking, disablingBackstory: true);
        object pool = Activator.CreateInstance(ufliPool, new object?[] { RuntimeHelpers.GetUninitializedObject(typeof(Map)) })!;
        ufliPool.GetField("worker", InstanceAny)!.SetValue(pool, greenPawn);
        ufliPool.GetField("leased", InstanceAny)!.SetValue(pool, false);

        object?[] acquireArgs = { crafting, 15, Enum.ToObject(ideoMode, 1), null };
        bool acquired = (bool)tryAcquire.Invoke(pool, acquireArgs)!;
        Pawn? result = acquireArgs[3] as Pawn;
        Check(acquired, "GREEN: TryAcquire returned false.");
        Check(ReferenceEquals(result, greenPawn), "GREEN: TryAcquire did not return the prepopulated ghost pawn.");

        foreach (SkillRecord skill in greenPawn.skills.skills)
        {
            Check(!skill.TotallyDisabled, $"GREEN: {skill.def.defName} is still TotallyDisabled.");
            Check(skill.Level == 15, $"GREEN: {skill.def.defName} level is {skill.Level}, expected 15.");
        }

        Check(greenPawn.CombinedDisabledWorkTags == WorkTags.None,
            $"GREEN: CombinedDisabledWorkTags is {greenPawn.CombinedDisabledWorkTags}, expected None.");
        Check(!butchery.Worker.IsDisabledFor(greenPawn), "GREEN: ButcheryMechanoidEfficiency is still disabled.");
        float greenValue = Evaluate(butchery, greenPawn);
        Check(Math.Abs(greenValue - 1.125f) < 0.0001f, $"GREEN: efficiency should be 1.125, got {greenValue}.");

        TaleData_Pawn tale = TaleData_Pawn.GenerateFrom(greenPawn);
        Check(tale != null, "GREEN: TaleData_Pawn.GenerateFrom returned null.");

        var art = new CompArt
        {
            props = new CompProperties_Art(),
            parent = (ThingWithComps)RuntimeHelpers.GetUninitializedObject(typeof(ThingWithComps)),
        };
        art.JustCreatedBy(greenPawn);
        FieldInfo authorField = typeof(CompArt).GetField("authorNameInt", InstanceAny)!;
        var author = (TaggedString)authorField.GetValue(art)!;
        Check(!string.IsNullOrEmpty(author.RawText), "GREEN: CompArt.JustCreatedBy did not record an author.");

        Check(!Messages.Any(m => m.StartsWith("ERROR:", StringComparison.Ordinal)),
            "GREEN: unexpected game error: " + string.Join("\n", Messages));

        Console.WriteLine($"GREEN: all skills level=15, CombinedDisabledWorkTags={greenPawn.CombinedDisabledWorkTags}, " +
                          $"IsDisabledFor=False efficiency={greenValue:0.000}; TaleData_Pawn/CompArt OK.");
    }

    private static void SetupFakeRimWorldPaths(Assembly game)
    {
        Type genFilePaths = game.GetType("Verse.GenFilePaths", true)!;
        string save = Path.Combine(Bootstrap.QaRoot, "fake-save");
        string mods = Path.Combine(Bootstrap.QaRoot, "fake-mods");
        string official = Path.Combine(Bootstrap.GameRoot, "Data");
        string config = Path.Combine(save, "Config");
        Directory.CreateDirectory(config);
        Directory.CreateDirectory(mods);
        Directory.CreateDirectory(official);

        File.WriteAllText(Path.Combine(config, "ModsConfig.xml"),
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
            "<ModsConfigData>\n" +
            "  <version>1.7.9805</version>\n" +
            "  <activeMods>\n    <li>ludeon.rimworld</li>\n  </activeMods>\n" +
            "  <knownExpansions />\n" +
            "</ModsConfigData>\n");

        const BindingFlags StaticPrivate = BindingFlags.Static | BindingFlags.NonPublic;
        genFilePaths.GetField("saveDataPath", StaticPrivate)!.SetValue(null, save);
        genFilePaths.GetField("modsFolderPath", StaticPrivate)!.SetValue(null, mods);
        genFilePaths.GetField("officialModsFolderPath", StaticPrivate)!.SetValue(null, official);

        // DeepProfiler.Start() (called by ModLister) reads Prefs.LogVerbose, which needs Prefs.data.
        Type prefs = game.GetType("Verse.Prefs", true)!;
        Type prefsData = game.GetType("Verse.PrefsData", true)!;
        object prefsDataInstance = Activator.CreateInstance(prefsData)!;
        prefsData.GetField("devMode")!.SetValue(prefsDataInstance, true);
        prefs.GetField("data", StaticPrivate)!.SetValue(null, prefsDataInstance);
    }

    private static void HookLog(Type logType, string methodName, string prefixName)
    {
        Type[] parameterTypes = methodName == "ErrorOnce"
            ? new[] { typeof(string), typeof(int) }
            : new[] { typeof(string) };
        MethodInfo target = logType.GetMethod(methodName, parameterTypes)
            ?? throw new MissingMethodException($"Verse.Log.{methodName}");
        MethodInfo prefix = typeof(Driver).GetMethod(prefixName, StaticAny)
            ?? throw new MissingMethodException($"Driver.{prefixName}");
        new Harmony(ValidationHarmonyId).Patch(target, prefix: new HarmonyMethod(prefix));
    }

    private static void PatchFalse(Harmony harmony, Assembly game, string typeName, string methodName)
    {
        Type type = game.GetType(typeName, true)!;
        MethodInfo target = type.GetMethod(methodName, new[] { typeof(Pawn) })
            ?? throw new MissingMethodException($"{typeName}.{methodName}(Pawn)");
        MethodInfo prefix = typeof(Driver).GetMethod(nameof(FalsePrefix), StaticAny)
            ?? throw new MissingMethodException("Driver.FalsePrefix");
        harmony.Patch(target, prefix: new HarmonyMethod(prefix));
    }

    private static bool LogMessage(string text)
    {
        Messages.Add("MESSAGE: " + text);
        return false;
    }

    private static bool LogWarning(string text)
    {
        Messages.Add("WARNING: " + text);
        return false;
    }

    private static bool LogError(string text)
    {
        Messages.Add("ERROR: " + text);
        return false;
    }

    private static bool LogErrorOnce(string text)
    {
        Messages.Add("ERROR: " + text);
        return false;
    }

    private static bool FalsePrefix(ref bool __result)
    {
        __result = false;
        return false;
    }

    private static Pawn NewPawn(SkillDef crafting, SkillDef cooking, bool disablingBackstory)
    {
        var pawn = (Pawn)RuntimeHelpers.GetUninitializedObject(typeof(Pawn));
        pawn.thingIDNumber = 424242;
        pawn.def = NewThingDef();
        pawn.kindDef = NewKindDef();
        pawn.Name = new NameSingle("ValidationGhost");
        pawn.ageTracker = NewAgeTracker(pawn);
        pawn.health = new Pawn_HealthTracker(pawn);
        pawn.story = new Pawn_StoryTracker(pawn);
        pawn.skills = new Pawn_SkillTracker(pawn);
        pawn.equipment = new Pawn_EquipmentTracker(pawn);
        pawn.apparel = new Pawn_ApparelTracker(pawn);

        pawn.skills.skills.Add(new SkillRecord(pawn, crafting));
        pawn.skills.skills.Add(new SkillRecord(pawn, cooking));

        WorkTags childDisables = disablingBackstory ? WorkTags.Crafting : WorkTags.None;
        pawn.story.Childhood = NewBackstory("UFLIVWF_ValidationChild", BackstorySlot.Childhood, childDisables);
        pawn.story.Adulthood = NewBackstory("UFLIVWF_ValidationAdult", BackstorySlot.Adulthood, WorkTags.None);
        return pawn;
    }

    private static ThingDef NewThingDef()
    {
        var def = (ThingDef)RuntimeHelpers.GetUninitializedObject(typeof(ThingDef));
        def.defName = "UFLIVWF_ValidationPawn";
        def.label = "validation pawn";
        def.race = NewRace();
        def.statBases = new List<StatModifier>();
        def.comps = new List<CompProperties>();
        return def;
    }

    private static RaceProperties NewRace()
    {
        var race = (RaceProperties)RuntimeHelpers.GetUninitializedObject(typeof(RaceProperties));
        race.intelligence = Intelligence.Humanlike;
        race.hasGenders = false;
        typeof(RaceProperties).GetField("fleshType", InstanceAny)!.SetValue(race, new FleshTypeDef { isOrganic = true });
        race.lifeStageAges = new List<LifeStageAge> { new LifeStageAge { def = new LifeStageDef() } };
        race.lifeStageWorkSettings = new List<LifeStageWorkSettings>();
        return race;
    }

    private static PawnKindDef NewKindDef()
    {
        var kind = (PawnKindDef)RuntimeHelpers.GetUninitializedObject(typeof(PawnKindDef));
        kind.defName = "UFLIVWF_ValidationColonist";
        kind.label = "validation colonist";
        kind.disabledWorkTags = WorkTags.None;
        return kind;
    }

    private static Pawn_AgeTracker NewAgeTracker(Pawn pawn)
    {
        var tracker = new Pawn_AgeTracker(pawn);
        typeof(Pawn_AgeTracker).GetField("cachedLifeStageIndex", InstanceAny)!.SetValue(tracker, 0);
        return tracker;
    }

    private static BackstoryDef NewBackstory(string name, BackstorySlot slot, WorkTags disables)
    {
        return new BackstoryDef
        {
            defName = name,
            label = name,
            description = name,
            slot = slot,
            title = name,
            titleFemale = name,
            titleShort = name,
            titleShortFemale = name,
            workDisables = disables,
            requiredWorkTags = WorkTags.None,
            shuffleable = false,
        };
    }

    private static SkillDef NewSkill(string name, WorkTags disablingTags)
    {
        var def = (SkillDef)RuntimeHelpers.GetUninitializedObject(typeof(SkillDef));
        def.defName = name;
        def.label = name;
        def.disablingWorkTags = disablingTags;
        def.neverDisabledBasedOnWorkTypes = false;
        return def;
    }

    private static StatDef NewButcheryStat(SkillDef crafting)
    {
        var stat = (StatDef)RuntimeHelpers.GetUninitializedObject(typeof(StatDef));
        stat.defName = "ButcheryMechanoidEfficiency";
        stat.label = "mechanoid shredding efficiency";
        stat.workerClass = typeof(StatWorker);
        stat.defaultBaseValue = 1f;
        stat.minValue = 0f;
        stat.maxValue = 1.5f;
        stat.neverDisabled = false;
        stat.supressDisabledError = false;
        stat.index = 0;
        stat.parts = new List<StatPart>();
        stat.statFactors = new List<StatDef>();
        stat.skillNeedOffsets = new List<SkillNeed>();
        stat.capacityOffsets = new List<PawnCapacityOffset>();
        stat.capacityFactors = new List<PawnCapacityFactor>();

        var need = new SkillNeed_BaseBonus { skill = crafting, required = true };
        typeof(SkillNeed_BaseBonus).GetField("baseValue", InstanceAny)!.SetValue(need, 0.75f);
        typeof(SkillNeed_BaseBonus).GetField("bonusPerLevel", InstanceAny)!.SetValue(need, 0.025f);
        stat.skillNeedFactors = new List<SkillNeed> { need };
        return stat;
    }

    private static void InvokeRefresh(Type poolType, Type ideoMode, Pawn pawn, SkillDef crafting, int skillLevel)
    {
        MethodInfo refresh = poolType.GetMethod("Refresh", StaticAny, null,
            new[] { typeof(Pawn), typeof(SkillDef), typeof(int), ideoMode }, null)
            ?? throw new MissingMethodException("MapComponent_UFAutoVirtualWorkerPool.Refresh");
        refresh.Invoke(null, new object?[] { pawn, crafting, skillLevel, Enum.ToObject(ideoMode, 1) }); // Null ideo mode avoids Faction.OfPlayer offline.
    }

    private static float Evaluate(StatDef stat, Pawn pawn)
    {
        var worker = new StatWorker();
        worker.InitSetStat(stat);
        return worker.GetValueUnfinalized(StatRequest.For(pawn), true);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
