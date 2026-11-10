using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

internal static class Tests
{
    private const BindingFlags CtorFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    public static int Run()
    {
        int red = 0;
        void Check(string label, bool ok, string? detail = null)
        {
            Console.WriteLine((ok ? "GREEN " : "RED   ") + label + (ok || detail == null ? "" : " :: " + detail));
            if (!ok) red++;
        }

        Type fixType = Type.GetType(
            "WhiteGiver.EquippableAbilityPsycastLoadFix.EquippableAbilityPsycastLoadFix, EquippableAbilityPsycastLoadFix",
            throwOnError: true)!;
        MethodInfo build = fixType.GetMethod("BuildCtorArgsFor", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;
        MethodInfo apply = fixType.GetMethod("Apply", BindingFlags.Static | BindingFlags.Public)!;

        object[] Args(Type type) => (object[])build.Invoke(null, new object?[] { type, null, null })!;

        Check("Psycast has no parameterless ctor (root cause)",
            typeof(Psycast).GetConstructor(CtorFlags, null, Type.EmptyTypes, null) == null);
        Check("Psycast has (Pawn) ctor",
            typeof(Psycast).GetConstructor(CtorFlags, null, new[] { typeof(Pawn) }, null) != null);
        Check("Ability keeps the parameterless path",
            Args(typeof(Ability)).Length == 0);

        object[] psyArgs = Args(typeof(Psycast));
        Check("Psycast resolves to (Pawn)", psyArgs.Length == 1 && psyArgs[0] == null,
            "length=" + psyArgs.Length);

        bool noArgThrows = false;
        try
        {
            Activator.CreateInstance(typeof(Psycast));
        }
        catch (MissingMethodException)
        {
            noArgThrows = true;
        }
        catch (Exception)
        {
        }
        Check("no-arg construction reproduces the red error", noArgThrows);

        object? instance = null;
        try
        {
            instance = Activator.CreateInstance(typeof(Psycast), CtorFlags, null, psyArgs, null, null);
        }
        catch (Exception ex)
        {
            Console.WriteLine("  instance threw: " + ex.GetType().Name + ": " + ex.Message);
        }
        Check("resolved args construct Psycast", instance is Psycast);

        Check("target method resolved",
            AccessTools.Method(typeof(CompEquippableAbility), nameof(CompEquippableAbility.PostExposeData)) != null);
        Check("ability field resolved",
            AccessTools.Field(typeof(CompEquippableAbility), "ability") != null);
        Check("base PostExposeData resolved",
            AccessTools.Method(typeof(CompEquippable), nameof(CompEquippable.PostExposeData)) != null);

        try
        {
            apply.Invoke(null, null);
        }
        catch (Exception ex)
        {
            Console.WriteLine("  apply threw: " + ex.GetType().Name + ": " + ex.Message);
        }

        Patches? patches = Harmony.GetPatchInfo(
            typeof(CompEquippableAbility).GetMethod(nameof(CompEquippableAbility.PostExposeData))!);
        Check("one prefix registered",
            patches?.Prefixes?.Count(p => p.owner == "whitegiver.equippableability.psycastloadfix") == 1);
        Check("prefix method matches",
            patches?.Prefixes?.Any(p => p.PatchMethod == fixType.GetMethod("Prefix")) == true);

        red += ReadEntryRegression.Run();
        Console.WriteLine(red == 0 ? "ALL GREEN" : red + " RED");
        return red == 0 ? 0 : 1;
    }
}
