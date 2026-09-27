using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Verse;
using Verse.AI;

// Seam-level validation for CE_Fix_IsHunkering_NRE.
//
// The game cannot run headless, so the "correct seam" for this mod is the Harmony
// patch topology against the real CombatExtended assembly plus the patch entry points
// that need no live map state. validate.ps1 loads Assembly-CSharp, the active
// CombatExtended.dll, 0Harmony and the patch DLL, then this driver runs the patcher's
// [StaticConstructorOnStartup] constructor exactly like RimWorld does.
//
// Harmony is accessed by reflection because RimWorld's MonoMod-based 0Harmony references
// types the Windows PowerShell 5.1 in-box compiler cannot consume; runtime is unaffected.
// validate.ps1 relaunches itself under powershell.exe because MonoMod's shared state does
// not initialize on CoreCLR. The C# is kept at language version 5 so both hosts compile it.
//
// RED  (v1.1 DLL): owns a prefix on the private CompTacticalManager.TryGiveTacticalJobs
//                  (full method shadow that drifts from upstream) and an early prefix on
//                  CompTend.TryGiveTacticalJob (returns before the flee-cover branch).
// GREEN (v1.2 DLL): no shadow; finalizer on the IsHunkering getter; postfix on CompTend
//                  that can only remove an already-built TendSelf job.
public static class IsHunkeringPatchValidation
{
    private const string HarmonyId = "meidocho.ce_fix_ishunkering_nre";

    private static int failures;

    private static void Check(bool condition, string description)
    {
        Console.WriteLine((condition ? "PASS: " : "FAIL: ") + description);
        if (!condition)
        {
            failures++;
        }
    }

    private static bool Flag(object value)
    {
        return value is bool && (bool)value;
    }

    private static Type FindLoadedType(string fullName)
    {
        Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
        for (int i = 0; i < assemblies.Length; i++)
        {
            Type type = assemblies[i].GetType(fullName, false);
            if (type != null)
            {
                return type;
            }
        }
        return null;
    }

    private static object GetMemberValue(object instance, string name)
    {
        Type type = instance.GetType();
        PropertyInfo property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
        if (property != null)
        {
            return property.GetValue(instance, null);
        }
        FieldInfo field = type.GetField(name, BindingFlags.Public | BindingFlags.Instance);
        return field == null ? null : field.GetValue(instance);
    }

    // Returns owned patches for one collection (name variants: Prefixes/prefixes, ...).
    private static List<object> OwnedPatches(object patches, string capitalName, string lowerName)
    {
        List<object> result = new List<object>();
        if (patches == null)
        {
            return result;
        }
        object collection = GetMemberValue(patches, capitalName);
        if (collection == null)
        {
            collection = GetMemberValue(patches, lowerName);
        }
        IEnumerable items = collection as IEnumerable;
        if (items == null)
        {
            return result;
        }
        foreach (object patch in items)
        {
            object owner = GetMemberValue(patch, "owner");
            if (owner is string && (string)owner == HarmonyId)
            {
                result.Add(patch);
            }
        }
        return result;
    }

    private static string Describe(List<object> patches)
    {
        if (patches.Count == 0)
        {
            return "none";
        }
        return string.Join(", ", patches.Select(delegate(object patch)
        {
            MethodInfo method = GetMemberValue(patch, "PatchMethod") as MethodInfo;
            return method == null ? "unknown" : method.DeclaringType.Name + "." + method.Name;
        }).ToArray());
    }

    private static List<object> PatchInfoPatches(MethodInfo getPatchInfo, MethodBase method, string capitalName, string lowerName)
    {
        object info = getPatchInfo.Invoke(null, new object[] { method });
        return OwnedPatches(info, capitalName, lowerName);
    }

    public static int Run()
    {
        using (Log.LockMessages())
        {
            Type patchType = FindLoadedType("CE_Fix_IsHunkering_NRE.Patch_IsHunkering");
            Check(patchType != null, "sidecar type CE_Fix_IsHunkering_NRE.Patch_IsHunkering loads");
            if (patchType == null)
            {
                return failures;
            }

            Type harmonyType = FindLoadedType("HarmonyLib.Harmony");
            Check(harmonyType != null, "HarmonyLib.Harmony is loaded");
            if (harmonyType == null)
            {
                return failures;
            }
            MethodInfo getPatchInfo = harmonyType.GetMethod("GetPatchInfo", BindingFlags.Public | BindingFlags.Static);
            Check(getPatchInfo != null, "Harmony.GetPatchInfo is available");
            if (getPatchInfo == null)
            {
                return failures;
            }

            // RimWorld runs [StaticConstructorOnStartup] constructors after startup; mirror that.
            RuntimeHelpers.RunClassConstructor(patchType.TypeHandle);

            MethodInfo tacticalJobs = typeof(CombatExtended.CompTacticalManager)
                .GetMethod("TryGiveTacticalJobs", BindingFlags.NonPublic | BindingFlags.Instance);

            PropertyInfo hunkeringProperty = typeof(CombatExtended.CompSuppressable)
                .GetProperty("IsHunkering", BindingFlags.Public | BindingFlags.Instance);
            MethodInfo hunkeringGetter = hunkeringProperty == null ? null : hunkeringProperty.GetGetMethod();

            MethodInfo tendJob = typeof(CombatExtended.AI.CompTend)
                .GetMethod("TryGiveTacticalJob", BindingFlags.Public | BindingFlags.Instance);

            Check(tacticalJobs != null && hunkeringGetter != null && tendJob != null,
                "CE seam methods resolve (TryGiveTacticalJobs / IsHunkering getter / CompTend.TryGiveTacticalJob)");

            List<object> tacticalOwned = new List<object>();
            if (tacticalJobs != null)
            {
                tacticalOwned.AddRange(PatchInfoPatches(getPatchInfo, tacticalJobs, "Prefixes", "prefixes"));
                tacticalOwned.AddRange(PatchInfoPatches(getPatchInfo, tacticalJobs, "Postfixes", "postfixes"));
                tacticalOwned.AddRange(PatchInfoPatches(getPatchInfo, tacticalJobs, "Finalizers", "finalizers"));
            }
            Check(tacticalOwned.Count == 0,
                "CompTacticalManager.TryGiveTacticalJobs is not shadowed by the sidecar (owned: " + Describe(tacticalOwned) + ")");

            List<object> hunkeringFinalizers = hunkeringGetter == null ? new List<object>()
                : PatchInfoPatches(getPatchInfo, hunkeringGetter, "Finalizers", "finalizers");
            List<object> hunkeringWrappers = new List<object>();
            if (hunkeringGetter != null)
            {
                hunkeringWrappers.AddRange(PatchInfoPatches(getPatchInfo, hunkeringGetter, "Prefixes", "prefixes"));
                hunkeringWrappers.AddRange(PatchInfoPatches(getPatchInfo, hunkeringGetter, "Postfixes", "postfixes"));
            }
            Check(hunkeringFinalizers.Count == 1 && Describe(hunkeringFinalizers).EndsWith("Finalizer_IsHunkering"),
                "IsHunkering getter has exactly Finalizer_IsHunkering (owned: " + Describe(hunkeringFinalizers) + ")");
            Check(hunkeringWrappers.Count == 0,
                "IsHunkering getter has no prefix/postfix that could replace or wrap the getter body (owned: " + Describe(hunkeringWrappers) + ")");

            List<object> tendPostfixes = tendJob == null ? new List<object>()
                : PatchInfoPatches(getPatchInfo, tendJob, "Postfixes", "postfixes");
            List<object> tendPrefixes = tendJob == null ? new List<object>()
                : PatchInfoPatches(getPatchInfo, tendJob, "Prefixes", "prefixes");
            Check(tendPostfixes.Count == 1 && Describe(tendPostfixes).EndsWith("Postfix_CompTend_TryGiveTacticalJob"),
                "CompTend.TryGiveTacticalJob has exactly Postfix_CompTend_TryGiveTacticalJob (owned: " + Describe(tendPostfixes) + ")");
            Check(tendPrefixes.Count == 0,
                "CompTend.TryGiveTacticalJob has no early prefix that could suppress the flee-cover branch (owned: " + Describe(tendPrefixes) + ")");

            // --- Finalizer behavior (needs no game state) ---
            MethodInfo finalizer = patchType.GetMethod("Finalizer_IsHunkering", BindingFlags.Public | BindingFlags.Static);
            Check(finalizer != null, "Finalizer_IsHunkering exists");
            if (finalizer != null)
            {
                object[] args = new object[] { new NullReferenceException("probe"), false };
                object returned = finalizer.Invoke(null, args);
                Check(returned == null && !Flag(args[1]),
                    "NullReferenceException -> IsHunkering=false and exception swallowed");

                InvalidOperationException foreign = new InvalidOperationException("probe");
                args = new object[] { foreign, true };
                returned = finalizer.Invoke(null, args);
                Check(ReferenceEquals(returned, foreign) && Flag(args[1]),
                    "unrelated exception -> propagated untouched, result preserved");

                args = new object[] { null, true };
                returned = finalizer.Invoke(null, args);
                Check(returned == null && Flag(args[1]),
                    "no exception -> pass-through finalizer");

                args = new object[] { new NullReferenceException("probe"), true };
                returned = finalizer.Invoke(null, args);
                Check(returned == null && !Flag(args[1]),
                    "NRE with previous result=true still degrades to false");
            }

            // --- Postfix guards (needs no game state) ---
            MethodInfo postfix = patchType.GetMethod("Postfix_CompTend_TryGiveTacticalJob", BindingFlags.Public | BindingFlags.Static);
            Check(postfix != null, "Postfix_CompTend_TryGiveTacticalJob exists");
            if (postfix != null)
            {
                object[] args = new object[] { null, null };
                postfix.Invoke(null, args);
                Check(args[1] == null, "null result -> no-throw no-op");

                // Fabricate just enough def/job state to reach the TendSelf branch without the game.
                FieldInfo tendSelfField = typeof(CombatExtended.CE_JobDefOf)
                    .GetField("TendSelf", BindingFlags.Public | BindingFlags.Static);
                JobDef tendSelf = new JobDef();
                tendSelf.defName = "TendSelfValidation";
                if (tendSelfField != null)
                {
                    tendSelfField.SetValue(null, tendSelf);
                }
                CombatExtended.AI.CompTend compTend = new CombatExtended.AI.CompTend();
                Job tendJobInstance = new Job();
                tendJobInstance.def = tendSelf;
                args = new object[] { compTend, tendJobInstance };
                postfix.Invoke(null, args);
                Check(ReferenceEquals(args[1], tendJobInstance),
                    "TendSelf result without map/pawn state -> preserved, no throw");

                JobDef fleeDef = new JobDef();
                fleeDef.defName = "RunForCoverValidation";
                Job fleeProxy = new Job();
                fleeProxy.def = fleeDef;
                args = new object[] { compTend, fleeProxy };
                postfix.Invoke(null, args);
                Check(ReferenceEquals(args[1], fleeProxy),
                    "non-TendSelf result (flee-cover proxy) -> passes through untouched");

                args = new object[] { null, tendJobInstance };
                postfix.Invoke(null, args);
                Check(ReferenceEquals(args[1], tendJobInstance),
                    "TendSelf result with null CompTend instance -> no throw");
            }

            Console.WriteLine(failures == 0
                ? "GREEN: narrowed patch topology and entry-point guards verified."
                : "RED: " + failures + " check(s) failed.");
            return failures;
        }
    }
}
