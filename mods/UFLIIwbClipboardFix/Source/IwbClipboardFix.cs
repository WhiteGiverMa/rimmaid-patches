using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace WhiteGiver.UFLI.IwbClipboardFix;

public sealed class UFLIIwbClipboardFixMod : Mod
{
    public UFLIIwbClipboardFixMod(ModContentPack content) : base(content)
    {
        IwbClipboardFix.Apply();
    }
}

public static class IwbClipboardFix
{
    public const string HarmonyId = "whitegiver.ufli.iwbclipboardfix";
    private const string LogPrefix = "[UFLI IWB Clipboard Fix]";
    private const BindingFlags StaticAny = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags InstanceAny = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static bool applied;
    private static PropertyInfo instanceProperty = null!;
    private static FieldInfo handlerField = null!;
    private static FieldInfo billsField = null!;

    public static void Apply()
    {
        if (applied) return;
        applied = true;
        try
        {
            Type? compat = FindType("kindseal.ufli", "UFRecipes", "UFRecipes.ImprovedWorkbenchesCompat");
            Type? main = FindType("falconne.bwm", "ImprovedWorkbenches", "ImprovedWorkbenches.Main");
            if (compat == null || main == null)
            {
                Log.Message($"{LogPrefix} UFLI or Better Workbench Management not loaded; patch skipped.");
                return;
            }

            MethodInfo? target = compat.GetMethod("GetCopiedBills", StaticAny, null, Type.EmptyTypes, null);
            PropertyInfo? instance = main.GetProperty("Instance", StaticAny);
            FieldInfo? handler = main.GetField("BillCopyPasteHandler", InstanceAny);
            FieldInfo? bills = handler?.FieldType.GetField("_copiedBills", InstanceAny);
            // ponytail: Support the verified IWB field contract only; revalidate after either upstream DLL changes.
            if (target?.ReturnType != typeof(List<Bill>) || instance?.PropertyType != main
                || instance.GetGetMethod(true) == null || handler == null || bills == null
                || !typeof(IEnumerable<Bill>).IsAssignableFrom(bills.FieldType))
            {
                Log.Warning($"{LogPrefix} Clipboard contract changed; original behavior retained. Revalidate or retire this sidecar.");
                return;
            }
            instanceProperty = instance;
            handlerField = handler;
            billsField = bills;
            new Harmony(HarmonyId).Patch(target,
                prefix: new HarmonyMethod(typeof(IwbClipboardFix), nameof(GetCopiedBillsPrefix)));
            Log.Message($"{LogPrefix} Patched UFRecipes.ImprovedWorkbenchesCompat.GetCopiedBills; BWM field/list conversion repaired.");
        }
        catch (Exception exception)
        {
            Log.Error($"{LogPrefix} Could not apply clipboard patch: {exception}");
        }
    }

    private static Type? FindType(string packageId, string assemblyName, string typeName) =>
        LoadedModManager.RunningMods.Where(mod => mod.PackageId == packageId)
            .SelectMany(mod => mod.assemblies.loadedAssemblies)
            .FirstOrDefault(assembly => assembly.GetName().Name == assemblyName)?.GetType(typeName);

    private static bool GetCopiedBillsPrefix(ref List<Bill>? __result)
    {
        try
        {
            object? instance = instanceProperty.GetValue(null);
            if (instance == null) return true;
            object? handler = handlerField.GetValue(instance);
            if (handler == null || billsField.GetValue(handler) is not IEnumerable<Bill> bills) return true;
            __result = new List<Bill>(bills);
            return false;
        }
        catch (Exception)
        {
            // Leave dependency/read failures to UFLI's original resolver and warning path.
            return true;
        }
    }
}
