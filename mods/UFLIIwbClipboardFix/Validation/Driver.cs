using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using HarmonyLib;
using RimWorld;
using UFRecipes;
using Verse;

public static class Driver
{
    private const string Owner = "whitegiver.ufli.iwbclipboardfix";
    private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    private static readonly List<string> Warnings = new();
    private static readonly List<string> Errors = new();
    private static object handler = null!;
    private static Type handlerType = null!;
    private static Type compat = null!;
    private static int checks;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Run(string mode, string patchPath)
    {
        var qa = new Harmony("validation.ufli.iwbclipboard");
        Hook(qa, typeof(Log), nameof(Log.Message), nameof(Ignore));
        Hook(qa, typeof(Log), nameof(Log.Warning), nameof(Warning));
        Hook(qa, typeof(Log), nameof(Log.Error), nameof(Error));
        Set(typeof(Prefs), "data", null, RuntimeHelpers.GetUninitializedObject(typeof(PrefsData)));
        qa.Patch(AccessTools.Method(typeof(DefOfHelper), nameof(DefOfHelper.EnsureInitializedInCtor)),
            prefix: new HarmonyMethod(typeof(Driver), nameof(Ignore)));
        qa.Patch(AccessTools.Method(typeof(Verse.Sound.SoundStarter), nameof(Verse.Sound.SoundStarter.PlayOneShotOnCamera)),
            prefix: new HarmonyMethod(typeof(Driver), nameof(Ignore)));

        if (mode == "absent")
        {
            LoadPatch(patchPath);
            Check(!Harmony.GetAllPatchedMethods().Any(method => Harmony.GetPatchInfo(method)?.Owners.Contains(Owner) == true),
                "Absent dependencies must register no compatibility hook.");
            Finish(mode);
            return;
        }

        Assembly ufli = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(Bootstrap.Bound, "UFRecipes.dll"));
        Assembly iwb = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(Bootstrap.Bound, "ImprovedWorkbenches.dll"));
        Check(typeof(Bill).Assembly.GetType("UFRecipes.BillStackFacade") == null, "Game assembly must not contain UFLI types.");
        Check(ufli == typeof(BillStackFacade).Assembly, "Use one real UFRecipes assembly in the default load context.");
        compat = ufli.GetType("UFRecipes.ImprovedWorkbenchesCompat", true)!;
        Type main = iwb.GetType("ImprovedWorkbenches.Main", true)!;
        handlerType = iwb.GetType("ImprovedWorkbenches.BillCopyPaste", true)!;
        Check(main.GetProperty("BillCopyPasteHandler", Any) == null, "RED contract: handler is not a property.");
        Check(main.GetField("BillCopyPasteHandler", Any)?.FieldType == handlerType, "Handler field contract.");
        Check(handlerType.GetField("_copiedBills", Any)?.FieldType == typeof(List<Bill_Production>), "Production-list contract.");
        object instance = RuntimeHelpers.GetUninitializedObject(main);
        handler = Activator.CreateInstance(handlerType)!;
        Set(main, "BillCopyPasteHandler", instance, handler);
        Set(main, "<Instance>k__BackingField", null, instance);
        AddMod("kindseal.ufli", ufli);
        AddMod("falconne.bwm", iwb);

        Current.ProgramState = ProgramState.Entry;
        Current.Game = (Game)RuntimeHelpers.GetUninitializedObject(typeof(Game));
        Set(typeof(Game), "uniqueIDsManager", Current.Game, new UniqueIDsManager());
        Set(typeof(BillRepeatModeDefOf), "RepeatCount", null, new BillRepeatModeDef { defName = "ValidationRepeatCount" });
        RecipeDef recipe = new() { defName = "ValidationRecipe", defaultIngredientFilter = new ThingFilter() };
        Building_WorkTable source = NewTable(recipe);
        Building_WorkTable target = NewTable(recipe);
        Bill_Production first = NewBill(recipe, 7);
        Bill_Production second = NewBill(recipe, 13);
        source.BillStack.AddBill(first);
        source.BillStack.AddBill(second);
        Copy(source);
        Check(RawClipboard().Count == 2, "Real IWB copy-all must capture two production bills.");
        Check((object)RawClipboard() is not List<Bill>, "RED contract: List<Bill_Production> is not List<Bill>.");
        BillStackFacade.Clipboard.Clear();
        BillUtility.Clipboard = null;

        if (mode == "red")
        {
            Check(!BillStackFacade.CanPaste(target), "RED: original IWB-only source must not be pasteable.");
            Check(BillStackFacade.Paste(target) == 0 && target.BillStack.Count == 0, "RED: original paste adds nothing.");
            Check(BillStackFacade.PasteSourceCount() == 0, "RED: original source count is zero.");
            Check(Warnings.Count(text => text.Contains("could not resolve _copiedBills", StringComparison.Ordinal)) == 1,
                "RED: reproduce the exact one-time compatibility warning.");
            // Isolate the second bug after fixing only the member lookup in QA memory.
            Set(compat, "copyPasteHandler", null, handler);
            Set(compat, "copiedBillsField", null, handlerType.GetField("_copiedBills", Any)!);
            Check(BillStackFacade.PasteSourceCount() == 0, "RED: field-only repair still fails the invariant generic cast.");
            Console.WriteLine("RED: real IWB copy-all -> UFLI CanPaste=false, Paste=0; exact warning once; field-only repair still yields 0.");
            Finish(mode);
            return;
        }

        LoadPatch(patchPath);
        MethodInfo get = compat.GetMethod("GetCopiedBills", Any)!;
        Check(Harmony.GetPatchInfo(get)?.Owners.Contains(Owner) == true, "Real compatibility getter must be patched.");
        Check(Harmony.GetPatchInfo(AccessTools.Method(typeof(BillStackFacade), "PasteSource")) == null,
            "Preserve UFLI clipboard source precedence without patching PasteSource.");
        Check(BillStackFacade.PasteSourceCount() == 2 && BillStackFacade.CanPaste(target), "GREEN: both copied bills are visible.");
        Check(BillStackFacade.Paste(target) == 2 && target.BillStack.Count == 2, "GREEN: actual paste adds both bills.");
        Check(target.BillStack[0] != first && target.BillStack[1] != second, "Paste creates clones, not shared source bills.");
        Check(((Bill_Production)target.BillStack[0]).repeatCount == 7
            && ((Bill_Production)target.BillStack[1]).repeatCount == 13, "Cloned bills preserve order and repeat counts.");
        Check(target.BillStack.Bills.All(bill => bill.recipe == recipe && bill.billStack == target.BillStack),
            "Clones keep recipes and acquire destination ownership.");
        Check(first.billStack == source.BillStack && second.billStack == source.BillStack && RawClipboard().Count == 2,
            "Reading/pasting never mutates source bills or IWB clipboard.");
        Check(Warnings.Count == 0, "Fixed copy/paste emits no compatibility warning.");

        BillUtility.Clipboard = first;
        Check(BillStackFacade.PasteSourceCount() == 1, "Vanilla clipboard retains precedence over IWB.");
        BillStackFacade.Copy(new[] { first, second });
        handlerType.GetMethod("Clear")!.Invoke(handler, null);
        Check(BillStackFacade.PasteSourceCount() == 2 && RawClipboard().Count == 0 && BillUtility.Clipboard == first,
            "Actual UFLI Copy takes precedence over distinct vanilla and empty IWB sources.");
        BillStackFacade.Clipboard.Clear();
        BillUtility.Clipboard = null;
        handlerType.GetMethod("Clear")!.Invoke(handler, null);
        Check(BillStackFacade.PasteSourceCount() == 0 && !BillStackFacade.CanPaste(target), "Cleared IWB clipboard is empty.");
        handlerType.GetMethod("DoCopy", new[] { typeof(Bill_Production) })!.Invoke(handler, new object[] { second });
        Check(BillStackFacade.PasteSourceCount() == 1 && BillStackFacade.Paste(target) == 1, "Real single-bill copy and paste work.");
        Building_WorkTable incompatible = NewTable(new RecipeDef
            { defName = "OtherRecipe", defaultIngredientFilter = new ThingFilter() });
        Check(!BillStackFacade.CanPaste(incompatible) && BillStackFacade.Paste(incompatible) == 0,
            "Existing recipe compatibility gates remain in force.");
        second.deleted = true;
        Check(!BillStackFacade.CanPaste(target) && BillStackFacade.Paste(target) == 0, "Deleted source bills still cannot be pasted.");
        Check(RawClipboard().Count == 1, "UFLI read does not silently clean another mod's clipboard.");
        second.deleted = false;
        Copy(source);
        Check(BillStackFacade.PasteSourceCount() == 2, "IWB copy-all updates are read after first use.");
        handlerType.GetMethod("RemoveBill")!.Invoke(handler, new object[] { first });
        Check(BillStackFacade.PasteSourceCount() == 1, "Real IWB RemoveBill is observed without a cached snapshot.");

        Bill_Production unsupported = NewBill(incompatible.def.AllRecipes[0], 19);
        source.BillStack.AddBill(unsupported);
        Copy(source);
        RawClipboard().Add(null!);
        Building_WorkTable mixedTarget = NewTable(recipe);
        Check(BillStackFacade.PasteSourceCount() == 4, "Snapshot preserves mixed and null entries without editing IWB state.");
        Check(BillStackFacade.CanPaste(mixedTarget) && BillStackFacade.Paste(mixedTarget) == 2,
            "UFLI's any-valid and per-item filtering semantics remain unchanged for mixed/null lists.");
        Check(((Bill_Production)mixedTarget.BillStack[0]).repeatCount == 7
            && ((Bill_Production)mixedTarget.BillStack[1]).repeatCount == 13, "Mixed paste retains valid source order.");

        object replacement = RuntimeHelpers.GetUninitializedObject(main);
        handler = Activator.CreateInstance(handlerType)!;
        Set(main, "BillCopyPasteHandler", replacement, handler);
        Set(main, "<Instance>k__BackingField", null, replacement);
        Check(BillStackFacade.PasteSourceCount() == 0, "Main/handler replacement is read live, not cached.");
        handlerType.GetMethod("DoCopy", new[] { typeof(Bill_Production) })!.Invoke(handler, new object[] { first });
        Check(BillStackFacade.PasteSourceCount() == 1, "Replacement handler clipboard is read live.");
        Set(main, "<Instance>k__BackingField", null, null);
        Check(BillStackFacade.PasteSourceCount() == 0, "Unavailable Main instance falls through to original UFLI.");
        Check(Warnings.Count(text => text.Contains("could not resolve _copiedBills", StringComparison.Ordinal)) == 1,
            "Fallback preserves the original warning rather than globally suppressing it.");
        Set(main, "<Instance>k__BackingField", null, replacement);
        Check(BillStackFacade.PasteSourceCount() == 1,
            "Recovered dependency is readable even after original UFLI cached a failed resolution.");
        Check(Errors.Count == 0, "Real clipboard flow must emit no error log.");
        Console.WriteLine("GREEN: real IWB Copy(single/all) -> UFLI CanPaste/Paste -> actual destination BillStack passed.");
        Finish(mode);
    }

    private static void LoadPatch(string path)
    {
        Assembly patch = AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
        Check(!patch.GetReferencedAssemblies().Any(reference => reference.Name is "UFRecipes" or "ImprovedWorkbenches"),
            "Sidecar must not hard-reference upstream mod DLLs.");
        Type mod = patch.GetType("WhiteGiver.UFLI.IwbClipboardFix.UFLIIwbClipboardFixMod", true)!;
        Activator.CreateInstance(mod, new object?[] { null });
        Activator.CreateInstance(mod, new object?[] { null });
        if (compat != null)
            Check(Harmony.GetPatchInfo(compat.GetMethod("GetCopiedBills", Any)!)?.Prefixes.Count(p => p.owner == Owner) == 1,
                "Repeated Mod construction must register exactly one hook.");
    }

    private static void AddMod(string id, Assembly assembly)
    {
        var content = (ModContentPack)RuntimeHelpers.GetUninitializedObject(typeof(ModContentPack));
        Set(typeof(ModContentPack), "packageIdInt", content, id);
        content.assemblies = new ModAssemblyHandler(content);
        content.assemblies.loadedAssemblies.Add(assembly);
        LoadedModManager.RunningModsListForReading.Add(content);
    }

    private static Building_WorkTable NewTable(RecipeDef recipe)
    {
        var table = (Building_WorkTable)RuntimeHelpers.GetUninitializedObject(typeof(Building_WorkTable));
        table.def = (ThingDef)RuntimeHelpers.GetUninitializedObject(typeof(ThingDef));
        table.def.defName = "ValidationTable";
        Set(typeof(ThingDef), "allRecipesCached", table.def, new List<RecipeDef> { recipe });
        Set(typeof(Thing), "mapIndexOrState", table, (sbyte)-1);
        table.billStack = new BillStack(table);
        return table;
    }

    private static Bill_Production NewBill(RecipeDef recipe, int count) => new(recipe) { repeatCount = count };
    private static List<Bill_Production> RawClipboard() => (List<Bill_Production>)handlerType.GetField("_copiedBills", Any)!.GetValue(handler)!;
    private static void Copy(Building_WorkTable table) => handlerType.GetMethod("DoCopy", new[] { typeof(Building_WorkTable) })!
        .Invoke(handler, new object[] { table });
    private static void Set(Type type, string field, object? instance, object? value) => AccessTools.Field(type, field).SetValue(instance, value);
    private static void Hook(Harmony harmony, Type type, string method, string prefix) => harmony.Patch(
        type.GetMethod(method, new[] { typeof(string) })!, prefix: new HarmonyMethod(typeof(Driver), prefix));
    private static bool Ignore() => false;
    private static bool Warning(string text) { Warnings.Add(text); return false; }
    private static bool Error(string text) { Errors.Add(text); return false; }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        checks++;
    }
    private static void Finish(string mode)
    {
        Console.WriteLine($"PASS: {mode}, {checks} checks. User configuration, saves and installed assemblies untouched.");
        Console.WriteLine("NOT TESTED: native game UI, mouse interactions, full mod startup, production or save/load.");
    }
}
