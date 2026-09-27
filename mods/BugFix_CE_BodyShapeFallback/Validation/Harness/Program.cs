using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using CombatExtended;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Meidocho.BodyShapeFallbackHarness;

// Exercises the REAL compiled patch DLL against the REAL CE_Utility.GetCollisionBodyFactors
// from the active Combat Extended assembly, without booting the game.
//
// Fixtures supply posture/downed/body-size for ctor-less pawn shells and a stand-in
// collision-body-factor callback registered through CE's public Patches API (the same hook
// VehiclesCompat uses), so no vehicle assembly is required.
//
// Phase 1 (failing-before): the unmodified CE method logs "CE returning BodyType Undefined"
//     for a nonstanding pawn missing bodyShape.
// Phase 2 (passing-after): after the patch DLL's static constructor runs, the same pawn is
//     guarded, standing pawns and callback-owned (vehicle stand-in) pawns stay on CE values.
public static class Program
{
    private const string ModHarmonyId = "com.local.bugfix.ce.bodyshapefallback";

    private static readonly string[] ProbeDirs =
    {
        @"A:\SteamLibrary\steamapps\common\RimWorld\RimWorldWin64_Data\Managed",
        @"A:\SteamLibrary\steamapps\workshop\content\294100\2009463077\Current\Assemblies",
        @"A:\SteamLibrary\steamapps\common\RimWorld\Mods\CombatExtendedLocal\Assemblies",
    };

    private static int failures;
    private static int errorOnceCalls;
    private static byte fixturePostureValue;
    private static bool fixtureDowned;
    private static float fixtureBodySize = 1.5f;

    static Program()
    {
        AppDomain.CurrentDomain.AssemblyResolve += (_, e) =>
        {
            string name = new AssemblyName(e.Name).Name;
            foreach (string dir in ProbeDirs)
            {
                string path = Path.Combine(dir, name + ".dll");
                if (File.Exists(path))
                {
                    return Assembly.LoadFrom(path);
                }
            }
            return null;
        };
    }

    public static bool PosturePrefix(ref PawnPosture __result) { __result = (PawnPosture)fixturePostureValue; return false; }
    public static bool DownedPrefix(ref bool __result) { __result = fixtureDowned; return false; }
    public static bool BodySizePrefix(ref float __result) { __result = fixtureBodySize; return false; }
    public static bool ErrorOncePrefix() { errorOnceCalls++; return false; }

    public static int Main(string[] args)
    {
        try
        {
            if (args.Length != 1)
            {
                Console.WriteLine("Usage: BodyShapeFallbackHarness.exe <path-to-BugFix_CE_BodyShapeFallback.dll>");
                return 2;
            }
            return Run(Path.GetFullPath(args[0]));
        }
        catch (Exception ex)
        {
            Console.WriteLine("HARNESS CRASH: " + ex.GetType().FullName + ": " + ex.Message);
            Console.WriteLine(ex.StackTrace);
            return 2;
        }
    }

    private static int Run(string patchDll)
    {
        // Silence Verse.Log (it would route into UnityEngine logging outside the game).
        using (Log.LockMessages())
        {
            Console.WriteLine("== BugFix_CE_BodyShapeFallback regression driver ==");
            Console.WriteLine("CE assembly: " + typeof(CE_Utility).Assembly.Location + " v" + typeof(CE_Utility).Assembly.GetName().Version.ToString());
            Console.WriteLine("patch assembly: " + patchDll);
            Console.WriteLine("patch sha256: " + Sha256(patchDll));

            Assembly patchAssembly = Assembly.LoadFrom(patchDll);
            Type modType = patchAssembly.GetType("BugFix_CE_BodyShapeFallback.BodyShapeFallbackPatch", throwOnError: true);

            var fixtureHarmony = new Harmony("meidocho.bodyshapefallback.harness");
            InstallFixtures(fixtureHarmony);
            SetBodyShapes();

            var missingShapeExtension = new RacePropertiesExtensionCE { overrideFactors = true, horiScale = 2f, vertScale = 3f };
            var humanoidExtension = new RacePropertiesExtensionCE { overrideFactors = true, horiScale = 2f, vertScale = 3f, bodyShape = CE_BodyShapeDefOf.Humanoid };

            var standingPawn = NewPawn(missingShapeExtension);
            var baselineLayingPawn = NewPawn(missingShapeExtension);
            var guardedPawn = NewPawn(missingShapeExtension);
            var downedPawn = NewPawn(missingShapeExtension);
            var shapedPawn = NewPawn(humanoidExtension);
            var vehiclePawn = NewPawn(null);

            CombatExtended.Compatibility.Patches.RegisterCollisionBodyFactorCallback(delegate (Pawn p)
            {
                if (p == vehiclePawn) return new Tuple<bool, Vector2>(true, new Vector2(1f, 0.4f));
                return new Tuple<bool, Vector2>(false, new Vector2());
            });

            Console.WriteLine("-- baseline: CE without this mod's prefix --");
            fixturePostureValue = (byte)PawnPosture.Standing;
            fixtureDowned = false;
            CheckClose(CE_Utility.GetCollisionBodyFactors(standingPawn), new Vector2(2f, 3f),
                "baseline standing pawn, missing bodyShape -> CE base factors");
            fixturePostureValue = (byte)PawnPosture.LayingOnGroundNormal;
            int errorBaseline = errorOnceCalls;
            CheckClose(CE_Utility.GetCollisionBodyFactors(baselineLayingPawn), new Vector2(2f, 3f),
                "baseline laying pawn, missing bodyShape -> CE base factors (Invalid shape math is neutral)");
            Check(errorOnceCalls == errorBaseline + 1,
                "baseline laying pawn, missing bodyShape -> CE logs 'CE returning BodyType Undefined' (repro)");

            Console.WriteLine("-- applying mod: " + patchAssembly.GetName().Name + " --");
            RuntimeHelpers.RunClassConstructor(modType.TypeHandle);
            MethodInfo target = AccessTools.Method("CombatExtended.CE_Utility:GetCollisionBodyFactors");
            Check(PrefixInstalled(target), "mod prefix is installed on CE_Utility.GetCollisionBodyFactors");

            Console.WriteLine("-- patched behaviour matrix --");
            fixturePostureValue = (byte)PawnPosture.Standing;
            fixtureDowned = false;
            CheckClose(CE_Utility.GetCollisionBodyFactors(standingPawn), new Vector2(2f, 3f),
                "standing pawn, missing bodyShape -> stays on CE base factors");

            fixturePostureValue = (byte)PawnPosture.LayingOnGroundNormal;
            CheckClose(CE_Utility.GetCollisionBodyFactors(vehiclePawn), new Vector2(1f, 0.4f),
                "nonstanding callback-owned pawn (vehicle stand-in) -> keeps callback factors (1, fillPercent)");

            int errorGuard = errorOnceCalls;
            CheckClose(CE_Utility.GetCollisionBodyFactors(guardedPawn), new Vector2(0.6f, 1.875f),
                "nonstanding pawn, missing bodyShape -> still receives the body-size fallback guard");
            Check(errorOnceCalls == errorGuard,
                "guard still suppresses the 'BodyType Undefined' error");

            fixtureDowned = true;
            fixturePostureValue = (byte)PawnPosture.Standing;
            int errorDowned = errorOnceCalls;
            CheckClose(CE_Utility.GetCollisionBodyFactors(downedPawn), new Vector2(0.6f, 1.875f),
                "downed pawn, missing bodyShape -> still receives the body-size fallback guard");
            Check(errorOnceCalls == errorDowned,
                "downed guard still suppresses the error");

            fixtureDowned = false;
            fixturePostureValue = (byte)PawnPosture.LayingOnGroundNormal;
            int errorShaped = errorOnceCalls;
            CheckClose(CE_Utility.GetCollisionBodyFactors(shapedPawn), new Vector2(4f, 0.9f),
                "laying pawn with defined bodyShape -> CE shape math untouched");
            Check(errorOnceCalls == errorShaped,
                "defined bodyShape logs no error");
        }

        Console.WriteLine(failures == 0 ? "HARNESS RESULT: ALL CHECKS PASSED" : "HARNESS RESULT: " + failures + " CHECK(S) FAILED");
        return failures == 0 ? 0 : 1;
    }

    private static void InstallFixtures(Harmony fixtureHarmony)
    {
        fixtureHarmony.Patch(AccessTools.Method(typeof(PawnUtility), "GetPosture"),
            prefix: new HarmonyMethod(typeof(Program).GetMethod("PosturePrefix")));
        fixtureHarmony.Patch(AccessTools.PropertyGetter(typeof(Pawn), "Downed"),
            prefix: new HarmonyMethod(typeof(Program).GetMethod("DownedPrefix")));
        fixtureHarmony.Patch(AccessTools.PropertyGetter(typeof(Pawn), "BodySize"),
            prefix: new HarmonyMethod(typeof(Program).GetMethod("BodySizePrefix")));
        fixtureHarmony.Patch(AccessTools.Method(typeof(Log), "ErrorOnce"),
            prefix: new HarmonyMethod(typeof(Program).GetMethod("ErrorOncePrefix")));
    }

    private static void SetBodyShapes()
    {
        CE_BodyShapeDefOf.Invalid = new BodyShapeDef();
        CE_BodyShapeDefOf.Humanoid = new BodyShapeDef { width = 0.25f, widthLaying = 0.5f, height = 1f, heightLaying = 0.3f };
    }

    private static Pawn NewPawn(RacePropertiesExtensionCE extension)
    {
        // ThingDef/Pawn cannot be constructed normally outside the game: field initializers
        // pull in Unity content loading. Allocate ctor-less shells and set only the fields
        // the CE collision path reads (def.defName, def.modExtensions, pawn.def).
        var def = (ThingDef)FormatterServices.GetUninitializedObject(typeof(ThingDef));
        def.defName = "ValidationFixturePawn";
        def.modExtensions = new List<DefModExtension>();
        if (extension != null) def.modExtensions.Add(extension);
        var pawn = (Pawn)FormatterServices.GetUninitializedObject(typeof(Pawn));
        pawn.def = def;
        return pawn;
    }

    private static bool PrefixInstalled(MethodInfo target)
    {
        HarmonyLib.Patches info = Harmony.GetPatchInfo(target);
        if (info == null) return false;
        foreach (Patch patch in info.Prefixes)
        {
            if (patch.owner == ModHarmonyId) return true;
        }
        return false;
    }

    private static void Check(bool condition, string description)
    {
        if (condition)
        {
            Console.WriteLine("PASS: " + description);
        }
        else
        {
            failures++;
            Console.WriteLine("FAIL: " + description);
        }
    }

    private static void CheckClose(Vector2 actual, Vector2 expected, string description)
    {
        bool ok = Math.Abs(actual.x - expected.x) < 0.0001f && Math.Abs(actual.y - expected.y) < 0.0001f;
        Check(ok, description + " [expected " + expected.x + "," + expected.y + " got " + actual.x + "," + actual.y + "]");
    }

    private static string Sha256(string path)
    {
        using (var stream = File.OpenRead(path))
        using (var sha = SHA256.Create())
        {
            byte[] hash = sha.ComputeHash(stream);
            var sb = new StringBuilder();
            foreach (byte b in hash) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }
}
