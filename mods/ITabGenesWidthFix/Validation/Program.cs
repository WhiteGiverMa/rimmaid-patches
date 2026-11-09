using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

AssemblyLoadContext.Default.Resolving += (_, name) =>
{
    var file = name.Name!.StartsWith("UnityEngine", StringComparison.Ordinal) ? "UnityStubs.dll" : name.Name + ".dll";
    var search = new[]
    {
        @"A:\SteamLibrary\steamapps\common\RimWorld",
        @"A:\SteamLibrary\steamapps\workshop\content\294100\2009463077\1.7\Assemblies",
        AppContext.BaseDirectory
    };
    foreach (var dir in search)
    {
        var path = Path.Combine(dir, file);
        if (File.Exists(path)) return AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
    }
    return null;
};

Run();

static void Run()
{
    const string HarmonyId = "whitegiver.itabgenes.widthfix.validation";
    var updateSize = typeof(InspectTabBase).GetMethod("UpdateSize", BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new Exception("InspectTabBase.UpdateSize not found");
    var sizeField = typeof(InspectTabBase).GetField("size", BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new Exception("InspectTabBase.size field not found");

    // Def resolution happens before UI.ApplyUIScale, so the singleton is built with screenWidth == 0.
    UI.screenWidth = 0;
    var tab = new ITab_Genes();
    if (((Vector2)sizeField.GetValue(tab)!).x != 0f)
        throw new Exception("Expected zero width before repair");

    // RED: unpatched UpdateSize does not repair.
    updateSize.Invoke(tab, null);
    if (((Vector2)sizeField.GetValue(tab)!).x != 0f)
        throw new Exception("RED failed: unpatched UpdateSize changed width");

    // Apply the real patch, then simulate a normal screen width at first OnGUI.
    var harmony = new Harmony(HarmonyId);
    var postfix = typeof(WhiteGiver.ITabGenesWidthFix.Patch.UpdateSize_Postfix)
        .GetMethod("Postfix", BindingFlags.Public | BindingFlags.Static)
        ?? throw new Exception("Postfix not found");
    harmony.Patch(updateSize, postfix: new HarmonyMethod(postfix));
    UI.screenWidth = 1920;
    updateSize.Invoke(tab, null);
    var repaired = (Vector2)sizeField.GetValue(tab)!;
    if (repaired.x != 736f)
        throw new Exception($"GREEN failed: expected 736, got {repaired.x}");

    // Idempotent: a second call keeps the repaired width.
    updateSize.Invoke(tab, null);
    if (((Vector2)sizeField.GetValue(tab)!).x != 736f)
        throw new Exception("GREEN failed: width changed on second call");

    // A small screen must clamp to its real width instead of 736.
    UI.screenWidth = 0;
    var small = new ITab_Genes();
    UI.screenWidth = 500;
    updateSize.Invoke(small, null);
    if (((Vector2)sizeField.GetValue(small)!).x != 500f)
        throw new Exception("GREEN failed: small screen width not respected");

    harmony.UnpatchAll(HarmonyId);
    Console.WriteLine("GREEN: ITab_Genes zero width repaired to 736 (and 500 on a small screen); RED confirmed unpatched behavior.");
}
