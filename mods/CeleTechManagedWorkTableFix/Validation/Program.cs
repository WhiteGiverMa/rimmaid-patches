using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using HarmonyLib;
using Mono.Cecil;
using RimWorld;
using Verse;

internal static class Program
{
    private const string Owner = "whitegiver.celetech.managedworktablefix";
    private static string gameRoot = "";
    private static string harmonyPath = "";
    private static string upstreamPath = "";
    private static string patchPath = "";
    private static readonly string BoundDirectory = Path.Combine(AppContext.BaseDirectory, "bound");

    private static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--help")
        {
            Console.WriteLine("Usage: Validation bind|red|green|absent GAME_ROOT HARMONY_DLL CELETECH_DLL PATCH_DLL");
            return 0;
        }
        if (args.Length != 5 || args[0] is not ("bind" or "red" or "green" or "absent" or "products"))
        {
            Console.Error.WriteLine("Expected a validation mode and four assembly paths; use --help.");
            return 1;
        }
        gameRoot = Path.GetFullPath(args[1]);
        harmonyPath = Path.GetFullPath(args[2]);
        upstreamPath = Path.GetFullPath(args[3]);
        patchPath = Path.GetFullPath(args[4]);
        AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            foreach (string directory in new[] { BoundDirectory, gameRoot, Path.GetDirectoryName(harmonyPath)! })
            {
                string path = Path.Combine(directory, name.Name + ".dll");
                if (File.Exists(path)) return context.LoadFromAssemblyPath(path);
            }
            return null;
        };
        try
        {
            if (args[0] == "bind") Bind();
            else Run(args[0]);
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static void Bind()
    {
        Directory.CreateDirectory(BoundDirectory);
        using var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(gameRoot);
        resolver.AddSearchDirectory(Path.GetDirectoryName(harmonyPath)!);
        resolver.AddSearchDirectory(Path.GetDirectoryName(upstreamPath)!);
        using var assembly = AssemblyDefinition.ReadAssembly(upstreamPath, new ReaderParameters { AssemblyResolver = resolver });
        int changed = 0;
        foreach (AssemblyNameReference reference in assembly.MainModule.AssemblyReferences)
        {
            if (!reference.Name.StartsWith("UnityEngine", StringComparison.Ordinal)) continue;
            reference.Name = "UnityStubs";
            reference.Version = new Version(1, 0, 0, 0);
            changed++;
        }
        assembly.Write(Path.Combine(BoundDirectory, "CeleTech.Base.dll"));
        Console.WriteLine($"QA-only copy: {changed} Unity assembly references rebound; original DLL unchanged.");
        using var game = AssemblyDefinition.ReadAssembly(Path.Combine(gameRoot, "Assembly-CSharp.dll"),
            new ReaderParameters { AssemblyResolver = resolver });
        // 隔离验证不得读取/扫描或重写用户配置；不改被测方法、列表、扣料逻辑。
        MethodDefinition configInitializer = game.MainModule.GetType("Verse.ModsConfig").Methods.Single(method => method.Name == ".cctor");
        configInitializer.Body.Instructions.Clear();
        configInitializer.Body.ExceptionHandlers.Clear();
        configInitializer.Body.Variables.Clear();
        configInitializer.Body.GetILProcessor().Emit(Mono.Cecil.Cil.OpCodes.Ret);
        game.Write(Path.Combine(BoundDirectory, "Assembly-CSharp.dll"));
        Console.WriteLine("QA-only game copy: ModsConfig initialization disabled; no user-config IO, DLC gates remain false.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Run(string mode)
    {
        Assembly patch = AssemblyLoadContext.Default.LoadFromAssemblyPath(patchPath);
        Type mod = patch.GetType("WhiteGiver.CeleTechManagedWorkTableFix.CeleTechManagedWorkTableFixMod", true)!;
        if (mode == "absent")
        {
            Activator.CreateInstance(mod, new object?[] { null });
            Check(!Harmony.GetAllPatchedMethods().Any(method => Harmony.GetPatchInfo(method)?.Owners.Contains(Owner) == true),
                "Absent CeleTech registered a patch.");
            Console.WriteLine("PASS: actual Mod constructor skips absent CeleTech without registering patches.");
            return;
        }
        if (mode == "products")
        {
            RunProducts(mod, patch);
            return;
        }

        Assembly upstream = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(BoundDirectory, "CeleTech.Base.dll"));
        Type hub = upstream.GetType("CeleTech.Base.Building_ManagedWorkTable", true)!;
        MethodInfo consume = AccessTools.DeclaredMethod(hub, "TryConsumeGatheredMaterials", Type.EmptyTypes);
        MethodInfo original = AccessTools.Method(typeof(ListerThings), nameof(ListerThings.ThingsInGroup), new[] { typeof(ThingRequestGroup) });
        Check(PatchProcessor.GetOriginalInstructions(consume).Count(instruction => instruction.Calls(original)) == 1,
            "Expected exactly one live-list query in the actual target.");
        // 无 Def 数据库时不能构造 DesignationManager；这里只跳过无关的地图清理。
        new Harmony(Owner + ".validation").Patch(AccessTools.Method(typeof(Thing), "RemoveAllReservationsAndDesignationsOnThis"),
            prefix: new HarmonyMethod(typeof(Program), nameof(SkipMapCleanup)));
        if (mode == "red")
        {
            Fixture fixture = CreateFixture(hub, new[] { 10, 100 }, 10);
            bool reproduced = false;
            try { consume.Invoke(fixture.Hub, null); }
            catch (TargetInvocationException exception) when (exception.InnerException is InvalidOperationException invalid
                && invalid.Message.Contains("Collection was modified", StringComparison.Ordinal))
            {
                reproduced = true;
            }
            Check(reproduced, "Original collection-modified exception did not reproduce.");
            Check(Progress(fixture) == 0 && fixture.Piles[0].Destroyed && fixture.Piles[0].stackCount == 0
                && fixture.Piles[1].stackCount == 100, "RED did not leave consumed materials with zero progress.");
            Console.WriteLine("RED: actual upstream method consumed 10, removed the whole stack, threw Collection was modified, progress remained 0.");
            return;
        }

        Activator.CreateInstance(mod, new object?[] { null });
        Check(Harmony.GetPatchInfo(consume)?.Transpilers.Count(patchInfo => patchInfo.owner == Owner) == 1,
            "Actual Mod constructor did not register one transpiler.");
        MethodInfo? makeProducts = AccessTools.Method(typeof(GenRecipe), nameof(GenRecipe.MakeRecipeProducts));
        if (makeProducts == null) throw new InvalidOperationException("GenRecipe.MakeRecipeProducts is missing.");
        Check(Harmony.GetAllPatchedMethods().Where(method => Harmony.GetPatchInfo(method)?.Owners.Contains(Owner) == true)
            .ToHashSet().SetEquals(new[] { consume, makeProducts }), "Patch touched a method outside the intended targets.");
        Type fix = patch.GetType("WhiteGiver.CeleTechManagedWorkTableFix.MaterialListFix", true)!;
        MethodInfo replacement = fix.GetMethod("SnapshotMaterials")!;
        List<CodeInstruction> current = PatchProcessor.GetCurrentInstructions(consume);
        Check(current.Count(instruction => instruction.Calls(replacement)) == 1 && !current.Any(instruction => instruction.Calls(original)),
            "Actual patched target does not use exactly one snapshot call.");
        ValidateTranspiler(fix, consume, original, replacement);

        foreach ((int[] stacks, int need, int[] remaining) in new[]
        {
            (new[] { 10, 100 }, 10, new[] { 0, 100 }),
            (new[] { 3, 7, 100 }, 10, new[] { 0, 0, 100 }),
            (new[] { 100 }, 10, new[] { 90 }),
            (new[] { 5 }, 10, new[] { 5 })
        })
        {
            Fixture fixture = CreateFixture(hub, stacks, need);
            consume.Invoke(fixture.Hub, null);
            Check(fixture.Piles.Select(thing => thing.stackCount).SequenceEqual(remaining), "Material consumption differs from recipe count.");
            bool enough = stacks.Sum() >= need;
            Check(Progress(fixture) == (enough ? 0.001f : 0f), "Production progress does not match material availability.");
        }
        Console.WriteLine("GREEN: actual target handles whole stack, multiple whole stacks, partial stack, insufficient materials; exact consumption/progress verified.");

        Fixture empty = CreateFixture(hub, Array.Empty<int>(), 0);
        consume.Invoke(empty.Hub, null);
        Check(Progress(empty) == 0.001f, "Ingredient-free recipe no longer starts.");
        hub.GetField("sourceBill")!.SetValue(empty.Hub, null);
        hub.GetField("progress")!.SetValue(empty.Hub, 0f);
        consume.Invoke(empty.Hub, null);
        Check(Progress(empty) == 0, "Null bill changed progress.");
        Console.WriteLine("GREEN: ingredient-free recipe and null-bill boundaries preserved; actual Mod/Harmony3 registration passed.");
    }

    private static void RunProducts(Type mod, Assembly patch)
    {
        Current.ProgramState = ProgramState.Entry;
        Current.Game = (Game)RuntimeHelpers.GetUninitializedObject(typeof(Game));
        AccessTools.Field(typeof(Game), "maps").SetValue(Current.Game, new List<Map>());
        AccessTools.Field(typeof(Game), "tickManager").SetValue(Current.Game, new TickManager());
        AccessTools.Field(typeof(Game), "uniqueIDsManager").SetValue(Current.Game, new UniqueIDsManager());
        Assembly upstream = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(BoundDirectory, "CeleTech.Base.dll"));
        Type hubType = upstream.GetType("CeleTech.Base.Building_ManagedWorkTable", true)!;
        MethodInfo? maybeMake = AccessTools.Method(typeof(GenRecipe), nameof(GenRecipe.MakeRecipeProducts));
        if (maybeMake == null) throw new InvalidOperationException("GenRecipe.MakeRecipeProducts is missing.");
        MethodInfo make = maybeMake;
        object hub = Activator.CreateInstance(hubType)!;
        IBillGiver billGiver = (IBillGiver)hub;
        Pawn worker = (Pawn)RuntimeHelpers.GetUninitializedObject(typeof(Pawn));

        var category = new StuffCategoryDef { defName = "ValidationStuffCategory" };
        var stuff = new ThingDef
        {
            defName = "ValidationStuff", thingClass = typeof(Thing), stackLimit = 1, useHitPoints = false,
            stuffProps = new StuffProperties { categories = new List<StuffCategoryDef> { category } }
        };
        Check(stuff.IsStuff, "Fixture stuff is not recognized as stuff.");
        var product = new ThingDef
        {
            defName = "ValidationStuffProduct", thingClass = typeof(Thing), stackLimit = 1, useHitPoints = false,
            stuffCategories = new List<StuffCategoryDef> { category }, defaultStuff = stuff
        };
        Check(product.MadeFromStuff, "Fixture product is not made from stuff.");
        var recipe = new RecipeDef
        {
            defName = "ValidationStuffRecipe",
            ingredients = new List<IngredientCount>(),
            products = new List<ThingDefCountClass> { new() { thingDef = product, count = 1 } }
        };
        object?[] Args(RecipeDef def, Pawn pawn, Thing? dominant) =>
            new object?[] { def, pawn, new List<Thing>(), dominant, billGiver, null, null, null };

        bool reproduced = false;
        try { Collect((IEnumerable<Thing>)make.Invoke(null, Args(recipe, worker, null))!); }
        catch (NullReferenceException) { reproduced = true; }
        Check(reproduced, "Original MakeRecipeProducts did not throw for a stuff product without a dominant ingredient.");

        Activator.CreateInstance(mod, new object?[] { null });
        Check(Harmony.GetPatchInfo(make)?.Prefixes.Count(info => info.owner == Owner) == 1,
            "Actual Mod constructor did not register one MakeRecipeProducts prefix.");
        List<Thing> products = Collect((IEnumerable<Thing>)make.Invoke(null, Args(recipe, worker, null))!);
        Check(products.Count == 1 && products[0].def == product && products[0].Stuff == stuff,
            "Patched hub call did not yield the product with default stuff.");

        Thing dominant = ThingMaker.MakeThing(stuff);
        products = Collect((IEnumerable<Thing>)make.Invoke(null, Args(recipe, worker, dominant))!);
        Check(products.Count == 1 && products[0].Stuff == stuff, "Existing dominant ingredient was not respected.");

        var plain = new ThingDef { defName = "ValidationPlainProduct", thingClass = typeof(Thing), stackLimit = 1, useHitPoints = false };
        var plainRecipe = new RecipeDef
        {
            defName = "ValidationPlainRecipe",
            ingredients = new List<IngredientCount>(),
            products = new List<ThingDefCountClass> { new() { thingDef = plain, count = 1 } }
        };
        products = Collect((IEnumerable<Thing>)make.Invoke(null, Args(plainRecipe, worker, null))!);
        Check(products.Count == 1 && products[0].Stuff == null, "Non-stuff product behavior changed.");

        IBillGiver otherGiver = (IBillGiver)RuntimeHelpers.GetUninitializedObject(typeof(Building_WorkTable));
        bool leaked = true;
        try
        {
            Collect((IEnumerable<Thing>)make.Invoke(null,
                new object?[] { recipe, worker, new List<Thing>(), null, otherGiver, null, null, null })!);
            leaked = false;
        }
        catch (NullReferenceException) { }
        Check(leaked, "MakeRecipeProducts prefix leaked to a non-CeleTech bill giver.");

        Console.WriteLine("RED: actual GenRecipe.MakeRecipeProducts threw NullReferenceException for a stuff product with null dominant ingredient.");
        Console.WriteLine("GREEN: actual Mod constructor added one prefix; hub product got default stuff, real dominant/non-stuff/other-giver paths preserved.");
    }

    private static List<Thing> Collect(IEnumerable<Thing> items) => items.ToList();

    private static void ValidateTranspiler(Type fix, MethodInfo consume, MethodInfo original, MethodInfo replacement)
    {
        MethodInfo transpiler = fix.GetMethod("Transpiler", BindingFlags.NonPublic | BindingFlags.Static)!;
        List<CodeInstruction> before = PatchProcessor.GetOriginalInstructions(consume);
        var opcodes = before.Select(instruction => instruction.opcode).ToArray();
        var operands = before.Select(instruction => instruction.operand).ToArray();
        int callIndex = before.FindIndex(instruction => instruction.Calls(original));
        var generator = new DynamicMethod("labels", typeof(void), Type.EmptyTypes).GetILGenerator();
        before[callIndex].labels.Add(generator.DefineLabel());
        before[callIndex].blocks.Add(new ExceptionBlock(ExceptionBlockType.BeginExceptionBlock));
        var labels = before.Select(instruction => instruction.labels.ToArray()).ToArray();
        var blocks = before.Select(instruction => instruction.blocks.ToArray()).ToArray();
        var after = ((IEnumerable<CodeInstruction>)transpiler.Invoke(null, new object[] { before })!).ToList();
        Check(after.Count == before.Count, "Transpiler changed instruction count.");
        for (int index = 0; index < before.Count; index++)
        {
            Check(ReferenceEquals(before[index], after[index])
                && after[index].opcode == (index == callIndex ? OpCodes.Call : opcodes[index])
                && Equals(after[index].operand, index == callIndex ? replacement : operands[index])
                && after[index].labels.SequenceEqual(labels[index]) && after[index].blocks.SequenceEqual(blocks[index]),
                "Transpiler changed unrelated instructions, labels or exception blocks.");
        }
        foreach (int count in new[] { 0, 2 })
        {
            bool rejected = false;
            try
            {
                transpiler.Invoke(null, new object[] { Enumerable.Range(0, count)
                    .Select(_ => new CodeInstruction(OpCodes.Callvirt, original)).ToList() });
            }
            catch (TargetInvocationException exception) when (exception.InnerException is InvalidOperationException) { rejected = true; }
            Check(rejected, "Unexpected call count was not rejected.");
        }
        Console.WriteLine("GREEN: only the intended call changed; instruction identities/labels/exception blocks preserved; 0/2-call drift rejected.");
    }

    private sealed record Fixture(object Hub, Thing[] Piles);

    private static bool SkipMapCleanup() => false;

    private sealed class ValidationThing : Thing
    {
        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
        {
            // 测试替身仅保留真实列表移除；图形、区域和 AI 记账不在本离线接缝中。
            Map.listerThings.Remove(this);
            AccessTools.Field(typeof(Thing), "mapIndexOrState").SetValue(this, (sbyte)-1);
        }
    }

    private static Fixture CreateFixture(Type hubType, int[] stacks, int need)
    {
        Current.ProgramState = ProgramState.Entry;
        Current.Game = (Game)RuntimeHelpers.GetUninitializedObject(typeof(Game));
        AccessTools.Field(typeof(Game), "maps").SetValue(Current.Game, new List<Map>());
        AccessTools.Field(typeof(Game), "tickManager").SetValue(Current.Game, new TickManager());
        AccessTools.Field(typeof(Game), "uniqueIDsManager").SetValue(Current.Game, new UniqueIDsManager());
        Current.Game.InitData = new GameInitData { playerFaction = new Faction() };
        var map = new Map();
        map.info.Size = new IntVec3(30, 1, 30);
        map.listerThings = new ListerThings(ListerThingsUse.Global);
        map.cellIndices = new CellIndices(map);
        map.haulDestinationManager = new HaulDestinationManager(map);
        map.zoneManager = new ZoneManager(map);
        map.listerMergeables = new ListerMergeables(map);
        Current.Game.Maps.Add(map);
        var material = new ThingDef
        {
            defName = "ValidationMaterial", category = ThingCategory.Item, thingClass = typeof(Thing),
            alwaysHaulable = true, destroyable = true, useHitPoints = false, stackLimit = 1000, size = new IntVec2(1, 1)
        };
        var recipe = new RecipeDef { ingredients = new List<IngredientCount>() };
        if (need > 0)
        {
            var ingredient = new IngredientCount();
            ingredient.filter.SetAllow(material, true);
            ingredient.SetBaseCount(need);
            recipe.ingredients.Add(ingredient);
        }
        var bill = new Bill_Production { recipe = recipe, ingredientFilter = new ThingFilter() };
        bill.ingredientFilter.SetAllow(material, true);
        object hub = Activator.CreateInstance(hubType)!;
        hubType.GetField("sourceBill")!.SetValue(hub, bill);
        SetLocation((Thing)hub, 0, new IntVec3(1, 0, 1));
        Thing[] piles = stacks.Select(count => (Thing)new ValidationThing { def = material, stackCount = count }).ToArray();
        foreach (Thing pile in piles)
        {
            SetLocation(pile, 0, new IntVec3(2, 0, 2));
            map.listerThings.Add(pile);
        }
        return new Fixture(hub, piles);
    }

    private static void SetLocation(Thing thing, sbyte mapIndex, IntVec3 position)
    {
        AccessTools.Field(typeof(Thing), "mapIndexOrState").SetValue(thing, mapIndex);
        AccessTools.Field(typeof(Thing), "positionInt").SetValue(thing, position);
    }

    private static float Progress(Fixture fixture) => (float)fixture.Hub.GetType().GetField("progress")!.GetValue(fixture.Hub)!;

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
