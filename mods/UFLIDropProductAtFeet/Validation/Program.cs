using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using HarmonyLib;
using Mono.Cecil;
using RimWorld;
using UFRecipes;
using Verse;
using Verse.AI;
using WhiteGiver.UFLI.DropProductAtFeet;

internal static class Bootstrap
{
    internal const string DefaultGameRoot = @"A:\SteamLibrary\steamapps\common\RimWorld";
    internal const string DefaultHarmonyDirectory = @"A:\SteamLibrary\steamapps\workshop\content\294100\2009463077\1.7\Assemblies";
    internal const string DefaultUfliDll = @"A:\SteamLibrary\steamapps\workshop\content\294100\3651799915\1.7\Assemblies\UFRecipes.dll";
    internal const string DefaultQa = @"C:\Users\A1337\AppData\Local\Temp\opencode\ufli-drop-at-feet-20261109";

    internal static readonly string GameRoot =
        Environment.GetEnvironmentVariable("RIMWORLD_ROOT") ?? DefaultGameRoot;
    internal static readonly string HarmonyDirectory =
        Environment.GetEnvironmentVariable("HARMONY_DIRECTORY") ?? DefaultHarmonyDirectory;
    internal static readonly string UfliDll =
        Environment.GetEnvironmentVariable("UFLI_DLL") ?? DefaultUfliDll;
    internal static string QaRoot =
        Environment.GetEnvironmentVariable("UFLI_QA_DIR") ?? DefaultQa;
    internal static string Bound = Path.Combine(QaRoot, "bound");

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

        foreach (string directory in new[] { Bound, GameRoot, HarmonyDirectory, Path.GetDirectoryName(UfliDll)! })
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

        string gameDll = Path.Combine(Bootstrap.GameRoot, "Assembly-CSharp.dll");
        RewriteUnityReferences(gameDll, Path.Combine(bound, "Assembly-CSharp.dll"));
        RewriteUnityReferences(Bootstrap.UfliDll, Path.Combine(bound, "UFRecipes.dll"));

        File.WriteAllText(Path.Combine(qa, "bind.txt"),
            $"game={Bootstrap.GameRoot}\n" +
            $"ufli={Bootstrap.UfliDll}\n" +
            $"harmony={Bootstrap.HarmonyDirectory}\n" +
            $"bound={bound}\n" +
            $"gameSha256={Sha256(gameDll)}\n" +
            $"ufliSha256={Sha256(Bootstrap.UfliDll)}\n");
        Console.WriteLine("PASS: QA-only Unity reference binding; real game and UFRecipes method bodies remain unchanged.");
        Console.WriteLine($"Assembly-CSharp sha256={Sha256(gameDll)}");
        Console.WriteLine($"UFRecipes sha256={Sha256(Bootstrap.UfliDll)}");
    }

    private static string Sha256(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
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
    private const string ValidationHarmonyId = "validation.ufli.dropproductatfeet";

    private sealed record Placement(Thing Product, IntVec3 Center, ThingPlaceMode Mode);

    private static readonly List<string> Messages = new();
    private static readonly List<Placement> Placements = new();
    private static Faction? playerFaction;
    private static Thing? nextRecipeProduct;
    private static BillRepeatModeDef? repeatCountMode;
    private static int pauseCalls;
    private static int freeCraftDeliveryFailureCalls;

    public static void Run(string mode)
    {
        Assembly game = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(Bootstrap.Bound, "Assembly-CSharp.dll"));
        Assembly ufli = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(Bootstrap.Bound, "UFRecipes.dll"));
        Check(game.GetType("UFRecipes.Building_SuperWorkbench") == null, "UFLI types unexpectedly came from Assembly-CSharp.");
        Check(ufli.GetType("UFRecipes.Building_SuperWorkbench") != null, "Real UFRecipes.Building_SuperWorkbench was not loaded.");

        HookLog(game);
        SetupFakeRimWorldPaths(game);
        SetupPrefs(game);
        Map map = NewWorld(out Game gameInstance);
        Check(ReferenceEquals(Current.Game, gameInstance), "Failed to install the isolated validation Game.");

        Harmony validationHarmony = new Harmony(ValidationHarmonyId + "." + mode);
        PatchValidationSeams(validationHarmony, ufli);

        if (mode == "red") Red(map);
        else Green(map, ufli);
    }

    private static void Red(Map map)
    {
        Building_SuperWorkbench bench = NewBench(map);
        RemoteCrate crate = NewRemoteCrate(map);
        ThingDef productDef = NewProductDef("UFLIDPAF_RedProduct");
        SeedRemoteStock(crate, NewProduct(productDef));
        Thing product = NewProduct(productDef);

        bool delivered = DeliverProduct(bench, product);

        Check(delivered, "RED: original DeliverProduct did not accept the remote container product.");
        Check(product.Destroyed, "RED: original remote stack merge should consume the source product.");
        Check(crate.AcceptCalls == 1, $"RED: remote IHaulDestination.Accepts calls={crate.AcceptCalls}, expected 1.");
        Check(crate.Contents.Count == 1 && crate.Contents[0].stackCount == 2,
            "RED: product was not merged into the remote crate's real ThingOwner.");
        Check(Placements.Count == 0, "RED: original remote delivery unexpectedly fell back to GenPlace near the bench.");
        Console.WriteLine("RED: original DeliverProduct used AllHaulDestinationsListInPriorityOrder; " +
                          $"remoteAccepts={crate.AcceptCalls}, remoteStack={crate.Contents[0].stackCount}, nearPlacements={Placements.Count}.");
    }

    private static void Green(Map map, Assembly ufli)
    {
        var settings = new DropProductAtFeetSettings();
        Check(settings.dropAtFeet, "GREEN: dropAtFeet default must be true.");
        SetModSettings(settings);
        DropProductAtFeet.Apply();

        MethodInfo deliver = DeliverMethod(ufli);
        Patches? patches = Harmony.GetPatchInfo(deliver);
        Check(patches != null && patches.Owners.Contains(DropProductAtFeet.HarmonyId),
            "GREEN: DeliverProduct was not patched by " + DropProductAtFeet.HarmonyId + ".");

        Building_SuperWorkbench bench = NewBench(map);
        RemoteCrate crate = NewRemoteCrate(map);
        ThingDef productDef = NewProductDef("UFLIDPAF_GreenProduct");
        SeedRemoteStock(crate, NewProduct(productDef));

        Thing nearProduct = NewProduct(productDef);
        bool delivered = DeliverProduct(bench, nearProduct);
        Check(delivered, "GREEN: drop-at-feet DeliverProduct returned false after successful near placement.");
        Check(nearProduct.Spawned && !nearProduct.Destroyed && ReferenceEquals(nearProduct.Map, map),
            "GREEN: near product is not spawned on the bench map.");
        Check(crate.AcceptCalls == 0 && crate.Contents[0].stackCount == 1,
            "GREEN: drop-at-feet still touched the remote crate.");
        Check(Placements.Count == 1, $"GREEN: expected one near placement, got {Placements.Count}.");
        Check(Placements[0].Mode == ThingPlaceMode.Near,
            $"GREEN: placement mode was {Placements[0].Mode}, expected Near.");
        Check(Placements[0].Center == bench.InteractionCell,
            $"GREEN: placement center {Placements[0].Center} != InteractionCell {bench.InteractionCell}.");

        // Verify every original guard remains effective while enabled.
        Check(!DeliverProduct(bench, null!), "GREEN: null product guard was not preserved.");
        ThingDef unstorableDef = NewProductDef("UFLIDPAF_Unstorable", storable: false);
        Check(!DeliverProduct(bench, NewProduct(unstorableDef)), "GREEN: EverStorable(false) guard was not preserved.");
        Thing destroyed = NewProduct(productDef);
        typeof(Thing).GetField("mapIndexOrState", InstanceAny)!.SetValue(destroyed, (sbyte)-2);
        Check(!DeliverProduct(bench, destroyed), "GREEN: Destroyed product guard was not preserved.");
        Building_SuperWorkbench unmappedBench = NewBench(null);
        Check(!DeliverProduct(unmappedBench, NewProduct(productDef)), "GREEN: null bench.Map guard was not preserved.");
        Check(Placements.Count == 1 && crate.AcceptCalls == 0,
            "GREEN: a guarded delivery unexpectedly touched storage or GenPlace.");

        // The setting is read for every delivery, not cached at patch time.
        settings.dropAtFeet = false;
        Thing originalProduct = NewProduct(productDef);
        bool originalDelivered = DeliverProduct(bench, originalProduct);
        Check(originalDelivered && originalProduct.Destroyed,
            "GREEN: disabling the toggle did not restore original remote-container delivery.");
        Check(crate.AcceptCalls == 1 && crate.Contents[0].stackCount == 2 && Placements.Count == 1,
            "GREEN: disabled mode did not use the original remote path exactly once.");

        settings.dropAtFeet = true;
        Thing nearAgain = NewProduct(productDef);
        Check(DeliverProduct(bench, nearAgain) && nearAgain.Spawned,
            "GREEN: re-enabling the toggle did not immediately restore near delivery.");
        Check(crate.AcceptCalls == 1 && Placements.Count == 2,
            "GREEN: re-enabled mode unexpectedly touched remote storage or missed GenPlace.");

        VerifyNormalProduceCaller(bench, productDef);
        VerifyFreeCraftCaller(bench, productDef);

        Check(pauseCalls == 0, $"GREEN: PauseForMissingIngredients was called {pauseCalls} time(s).");
        Check(freeCraftDeliveryFailureCalls == 0,
            $"GREEN: NotifyFreeCraftDeliveryFailed was called {freeCraftDeliveryFailureCalls} time(s).");
        Check(!Messages.Any(m => m.StartsWith("ERROR: [UFRecipes]", StringComparison.Ordinal) ||
                                 m.StartsWith("ERROR: [UFLI Drop Product At Feet]", StringComparison.Ordinal)),
            "GREEN: unexpected UFLI/patch error: " + string.Join("\n", Messages));

        Console.WriteLine("GREEN: drop-at-feet used real TryPlaceProductNear -> GenPlace(Near, InteractionCell), returned true, " +
                          "skipped remote storage; off/on switching restored each mode immediately.");
        Console.WriteLine($"GREEN: Produce and TryProduceFreeCraft succeeded; PauseForMissingIngredients={pauseCalls}, " +
                          $"NotifyFreeCraftDeliveryFailed={freeCraftDeliveryFailureCalls}, totalNearPlacements={Placements.Count}.");
    }

    private static void VerifyNormalProduceCaller(Building_SuperWorkbench bench, ThingDef productDef)
    {
        RecipeDef recipe = NewRecipe(productDef);
        bench.activeBill = NewBill(recipe);
        nextRecipeProduct = NewProduct(productDef);
        int before = Placements.Count;

        bench.Produce(NewWorker());

        Check(nextRecipeProduct.Spawned, "GREEN: Produce's recipe product was not spawned at the printer.");
        Check(Placements.Count == before + 1, "GREEN: Produce did not execute exactly one near delivery.");
        Check(!bench.paused && bench.activeBill == null,
            "GREEN: Produce paused or failed to finish after successful near delivery.");
    }

    private static void VerifyFreeCraftCaller(Building_SuperWorkbench bench, ThingDef productDef)
    {
        RecipeDef recipe = NewRecipe(productDef);
        Bill_Production bill = NewBill(recipe);
        Type benchType = typeof(Building_SuperWorkbench);
        Type spiritKind = ufliType("UFRecipes.UFMechineSpiritKind");
        benchType.GetField("mechineSpiritKind", InstanceAny)!.SetValue(
            bench, Enum.Parse(spiritKind, "FreeCraft"));
        benchType.GetField("freeCraftCharges", InstanceAny)!.SetValue(bench, 2);
        int before = Placements.Count;

        object result = benchType.GetMethod("TryProduceFreeCraft", InstanceAny)!.Invoke(
            bench, new object[] { bill, NewWorker() })!;

        Check(result.ToString() == "Succeeded",
            $"GREEN: TryProduceFreeCraft returned {result}, expected Succeeded.");
        Check(Placements.Count == before + 1, "GREEN: free craft did not execute exactly one near delivery.");
        Check((int)benchType.GetField("freeCraftCharges", InstanceAny)!.GetValue(bench)! == 1,
            "GREEN: successful free craft did not consume exactly one charge.");
    }

    private static Type ufliType(string name) => typeof(Building_SuperWorkbench).Assembly.GetType(name, true)!;

    private static bool DeliverProduct(Building_SuperWorkbench bench, Thing product)
    {
        return (bool)DeliverMethod(typeof(Building_SuperWorkbench).Assembly).Invoke(bench, new object?[] { product })!;
    }

    private static MethodInfo DeliverMethod(Assembly ufli)
    {
        return ufli.GetType("UFRecipes.Building_SuperWorkbench", true)!
            .GetMethod("DeliverProduct", InstanceAny)
            ?? throw new MissingMethodException("UFRecipes.Building_SuperWorkbench.DeliverProduct");
    }

    private static void PatchValidationSeams(Harmony harmony, Assembly ufli)
    {
        MethodInfo playerGetter = AccessTools.PropertyGetter(typeof(Faction), nameof(Faction.OfPlayer));
        harmony.Patch(playerGetter, prefix: new HarmonyMethod(typeof(Driver), nameof(PlayerFactionPrefix)));
        harmony.Patch(typeof(Thing).GetMethod("RemoveAllReservationsAndDesignationsOnThis", InstanceAny)!,
            prefix: new HarmonyMethod(typeof(Driver), nameof(SkipMapCleanupPrefix)));

        Type benchType = ufli.GetType("UFRecipes.Building_SuperWorkbench", true)!;
        harmony.Patch(benchType.GetMethod("InHomeArea", InstanceAny)!,
            prefix: new HarmonyMethod(typeof(Driver), nameof(InHomeAreaPrefix)));
        harmony.Patch(benchType.GetMethod("PauseForMissingIngredients", InstanceAny)!,
            prefix: new HarmonyMethod(typeof(Driver), nameof(PausePrefix)));
        harmony.Patch(benchType.GetMethod("NotifyFreeCraftDeliveryFailed", InstanceAny)!,
            prefix: new HarmonyMethod(typeof(Driver), nameof(FreeCraftFailurePrefix)));
        harmony.Patch(benchType.GetMethod("GetEfficiencyFactorFor", InstanceAny)!,
            prefix: new HarmonyMethod(typeof(Driver), nameof(EfficiencyPrefix)));

        MethodInfo place = typeof(GenPlace).GetMethod("TryPlaceThing", new[]
        {
            typeof(Thing), typeof(IntVec3), typeof(Map), typeof(ThingPlaceMode),
            typeof(Action<Thing, int>), typeof(Predicate<IntVec3>), typeof(Rot4?), typeof(int),
        }) ?? throw new MissingMethodException("GenPlace.TryPlaceThing overload used by UFLI");
        harmony.Patch(place, prefix: new HarmonyMethod(typeof(Driver), nameof(PlacePrefix)));

        MethodInfo makeProducts = typeof(GenRecipe).GetMethod("MakeRecipeProducts", StaticAny)
            ?? throw new MissingMethodException("GenRecipe.MakeRecipeProducts");
        harmony.Patch(makeProducts, prefix: new HarmonyMethod(typeof(Driver), nameof(MakeProductsPrefix)));
    }

    private static bool PlayerFactionPrefix(ref Faction __result)
    {
        __result = playerFaction!;
        return false;
    }

    private static bool InHomeAreaPrefix(ref bool __result)
    {
        __result = true;
        return false;
    }

    private static bool SkipMapCleanupPrefix() => false;

    private static bool PausePrefix()
    {
        pauseCalls++;
        return false;
    }

    private static bool FreeCraftFailurePrefix()
    {
        freeCraftDeliveryFailureCalls++;
        return false;
    }

    private static bool EfficiencyPrefix(ref float __result)
    {
        __result = 1f;
        return false;
    }

    private static bool MakeProductsPrefix(ref IEnumerable<Thing> __result)
    {
        Check(nextRecipeProduct != null, "Validation did not provide a MakeRecipeProducts result.");
        __result = new[] { nextRecipeProduct! };
        return false;
    }

    private static bool PlacePrefix(Thing thing, IntVec3 center, Map map, ThingPlaceMode mode, ref bool __result)
    {
        Placements.Add(new Placement(thing, center, mode));
        SetThingMap(thing, map, center);
        __result = true;
        return false;
    }

    private static Map NewWorld(out Game game)
    {
        game = (Game)RuntimeHelpers.GetUninitializedObject(typeof(Game));
        typeof(Game).GetField("maps", InstanceAny)!.SetValue(game, new List<Map>());
        game.components = new List<GameComponent>();
        game.uniqueIDsManager = new UniqueIDsManager();
        Current.Game = game;

        var map = (Map)RuntimeHelpers.GetUninitializedObject(typeof(Map));
        map.info = new MapInfo { Size = new IntVec3(50, 1, 50) };
        map.haulDestinationManager = new HaulDestinationManager(map);
        game.Maps.Add(map);
        playerFaction = (Faction)RuntimeHelpers.GetUninitializedObject(typeof(Faction));
        return map;
    }

    private static Building_SuperWorkbench NewBench(Map? map)
    {
        var bench = new Building_SuperWorkbench
        {
            def = new ThingDef
            {
                defName = "UFLIDPAF_ValidationBench",
                label = "validation bench",
                thingClass = typeof(Building_SuperWorkbench),
                category = ThingCategory.Building,
                size = new IntVec2(3, 3),
                hasInteractionCell = true,
                interactionCellOffset = new IntVec3(0, 0, -2),
                useHitPoints = false,
                comps = new List<CompProperties>(),
            },
        };
        SetThingMap(bench, map, new IntVec3(10, 0, 10));
        return bench;
    }

    private static RemoteCrate NewRemoteCrate(Map map)
    {
        var crate = new RemoteCrate
        {
            def = new ThingDef
            {
                defName = "UFLIDPAF_ValidationRemoteCrate",
                label = "validation remote crate",
                thingClass = typeof(RemoteCrate),
                category = ThingCategory.Building,
                size = IntVec2.One,
                useHitPoints = false,
                comps = new List<CompProperties>(),
            },
        };
        SetThingMap(crate, map, new IntVec3(30, 0, 30));
        crate.SetFactionDirect(playerFaction);
        map.haulDestinationManager.AllHaulDestinationsListInPriorityOrder.Add(crate);
        return crate;
    }

    private static ThingDef NewProductDef(string name, bool storable = true)
    {
        return new ThingDef
        {
            defName = name,
            label = name,
            thingClass = typeof(ThingWithComps),
            category = ThingCategory.Item,
            thingCategories = storable
                ? new List<ThingCategoryDef> { new ThingCategoryDef { defName = name + "Category", label = name } }
                : new List<ThingCategoryDef>(),
            stackLimit = 75,
            useHitPoints = false,
            destroyable = true,
            comps = new List<CompProperties>(),
        };
    }

    private static Thing NewProduct(ThingDef def)
    {
        Thing product = ThingMaker.MakeThing(def);
        product.stackCount = 1;
        return product;
    }

    private static void SeedRemoteStock(RemoteCrate crate, Thing product)
    {
        // Prepopulate an already-existing stack without firing UI/map notifications.
        // The subsequent DeliverProduct merge still uses the real ThingOwner implementation.
        typeof(Thing).GetField("holdingOwner", InstanceAny)!.SetValue(product, crate.Contents);
        crate.Contents.InnerListForReading.Add(product);
    }

    private static Bill_Production NewBill(RecipeDef recipe)
    {
        repeatCountMode ??= new BillRepeatModeDef { defName = "UFLIDPAF_RepeatCount" };
        typeof(BillRepeatModeDefOf).GetField("RepeatCount", StaticAny)!.SetValue(null, repeatCountMode);
        return new Bill_Production(recipe)
        {
            repeatMode = new BillRepeatModeDef { defName = "UFLIDPAF_Forever" },
        };
    }

    private static RecipeDef NewRecipe(ThingDef productDef)
    {
        return new RecipeDef
        {
            defName = "UFLIDPAF_ValidationRecipe_" + productDef.defName,
            label = "validation recipe",
            workAmount = 60f,
            workerClass = typeof(RecipeWorker),
            defaultIngredientFilter = new ThingFilter(),
            products = new List<ThingDefCountClass> { new ThingDefCountClass(productDef, 1) },
            ingredients = new List<IngredientCount>(),
            specialProducts = null,
        };
    }

    private static Pawn NewWorker() => (Pawn)RuntimeHelpers.GetUninitializedObject(typeof(Pawn));

    private static void SetThingMap(Thing thing, Map? map, IntVec3 position)
    {
        typeof(Thing).GetField("positionInt", InstanceAny)!.SetValue(thing, position);
        typeof(Thing).GetField("mapIndexOrState", InstanceAny)!.SetValue(thing, (sbyte)(map?.Index ?? -1));
    }

    private static void SetModSettings(DropProductAtFeetSettings settings)
    {
        PropertyInfo property = typeof(UFLIDropProductAtFeetMod).GetProperty("Settings", StaticAny)
            ?? throw new MissingMemberException("UFLIDropProductAtFeetMod.Settings");
        property.GetSetMethod(true)!.Invoke(null, new object[] { settings });
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
            "  <version>1.7.5000 rev1275</version>\n" +
            "  <activeMods><li>ludeon.rimworld</li></activeMods>\n" +
            "  <knownExpansions />\n" +
            "</ModsConfigData>\n");

        genFilePaths.GetField("saveDataPath", StaticAny)!.SetValue(null, save);
        genFilePaths.GetField("modsFolderPath", StaticAny)!.SetValue(null, mods);
        genFilePaths.GetField("officialModsFolderPath", StaticAny)!.SetValue(null, official);
    }

    private static void SetupPrefs(Assembly game)
    {
        Type prefs = game.GetType("Verse.Prefs", true)!;
        Type prefsData = game.GetType("Verse.PrefsData", true)!;
        object data = Activator.CreateInstance(prefsData)!;
        prefs.GetField("data", StaticAny)!.SetValue(null, data);
    }

    private static void HookLog(Assembly game)
    {
        Type logType = game.GetType("Verse.Log", true)!;
        HookLog(logType, "Message", nameof(LogMessage));
        HookLog(logType, "Warning", nameof(LogWarning));
        HookLog(logType, "Error", nameof(LogError));
        Type[] parameterTypes = { typeof(string), typeof(int) };
        MethodInfo errorOnce = logType.GetMethod("ErrorOnce", parameterTypes)
            ?? throw new MissingMethodException("Verse.Log.ErrorOnce(string,int)");
        new Harmony(ValidationHarmonyId + ".log").Patch(errorOnce,
            prefix: new HarmonyMethod(typeof(Driver), nameof(LogErrorOnce)));
    }

    private static void HookLog(Type logType, string methodName, string prefixName)
    {
        MethodInfo target = logType.GetMethod(methodName, new[] { typeof(string) })
            ?? throw new MissingMethodException($"Verse.Log.{methodName}");
        MethodInfo prefix = typeof(Driver).GetMethod(prefixName, StaticAny)
            ?? throw new MissingMethodException($"Driver.{prefixName}");
        new Harmony(ValidationHarmonyId + ".log").Patch(target, prefix: new HarmonyMethod(prefix));
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

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

internal sealed class RemoteCrate : Building, IHaulDestination, IThingHolder
{
    private readonly StorageSettings settings = new StorageSettings();

    public ThingOwner<Thing> Contents { get; } = new ThingOwner<Thing>();
    public int AcceptCalls { get; private set; }
    public bool HaulDestinationEnabled => true;
    public bool StorageTabVisible => true;

    public RemoteCrate()
    {
        Contents = new ThingOwner<Thing>(this);
    }

    public bool Accepts(Thing t)
    {
        AcceptCalls++;
        return true;
    }

    public StorageSettings GetStoreSettings() => settings;
    public StorageSettings GetParentStoreSettings() => null!;
    public void Notify_SettingsChanged() { }
    public ThingOwner GetDirectlyHeldThings() => Contents;
    public void GetChildHolders(List<IThingHolder> outChildren) { }
}
