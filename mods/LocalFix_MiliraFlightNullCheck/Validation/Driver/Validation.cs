using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using HarmonyLib;

namespace LocalFix.MiliraFlightNullCheck.Validation
{
    internal static class Validation
    {
        private const string OurHarmonyId = "local.meidocho.MiliraFlightNullCheckFix";
        private const string MiliraHarmonyId = "Ancot.MiliraRaceHarmonyPatch";
        private const string StubHarmonyId = "local.validation.can-ever-fly-stub";
        private const string MiliraPatchTypeName = "Milira.MilianPatch_Pawn_FlightTracker_Notify_JobStarted";
        private const string TargetTypeName = "RimWorld.Pawn_FlightTracker";
        private const string TargetMethodName = "Notify_JobStarted";
        private const string MiliraBodyDefName = "Milira_Body";
        private const string StartupAttributeName = "Verse.StaticConstructorOnStartup";
        private const string HarmonyPatchAttributeName = "HarmonyLib.HarmonyPatch";
        private const string OurPatchTypeName = "LocalFix_MiliraFlightNullCheck.Fix_MiliraFlightNullCheck";

        private static int failures;

        // ---------------------------------------------------------------- modes

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static int RunInspect(string patchDll, string managed, string harmony, string milira)
        {
            failures = 0;
            Section("inspect " + patchDll);
            Context ctx = LoadContext(managed, harmony, milira, patchDll);

            List<Type> startupTypes = TypesWithAttribute(ctx.Patch, StartupAttributeName);
            List<Type> patchClasses = TypesWithAttribute(ctx.Patch, HarmonyPatchAttributeName);

            Check(startupTypes.Count == 1,
                "exactly one [StaticConstructorOnStartup] bootstrap type (found " + startupTypes.Count +
                (startupTypes.Count > 0 ? ": " + string.Join(", ", startupTypes.Select(t => t.FullName)) : "") + ")");
            Check(patchClasses.Count == 1,
                "exactly one [HarmonyPatch] class (found " + patchClasses.Count +
                (patchClasses.Count > 0 ? ": " + string.Join(", ", patchClasses.Select(t => t.FullName)) : "") + ")");

            Type modType = ctx.CSharp.GetType("Verse.Mod", throwOnError: false);
            int modSubclasses = modType == null ? 0 : ctx.Patch.GetTypes().Count(t => modType.IsAssignableFrom(t));
            Console.WriteLine("  info: Mod subclasses: " +
                (modSubclasses == 0 ? "none (bootstrap-only mod)" : modSubclasses.ToString()));

            Console.WriteLine(failures == 0
                ? "INSPECT RESULT: GREEN - the DLL can self-register through the game's static constructor scan"
                : "INSPECT RESULT: RED - the DLL cannot self-register (missing bootstrap or patch class)");
            return failures == 0 ? 0 : 1;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static int RunRed(string managed, string harmony, string milira)
        {
            failures = 0;
            Section("red: upstream Milira prefix, Milira-body pawn without CompFlightControl");
            Context ctx = LoadContext(managed, harmony, milira, null);

            RegisterCanEverFlyStub(ctx);
            RegisterMiliraPatch(ctx);
            Check(CountPrefixes(ctx.NotifyMethod, MiliraHarmonyId) == 1,
                "upstream prefix registered through the same PatchClassProcessor path PatchAll uses (owner " + MiliraHarmonyId + ")");

            object pawn = NewPawn(ctx, MiliraBodyDefName);
            object tracker = Activator.CreateInstance(ctx.TrackerType, pawn);
            object job = NewJob(ctx);

            try
            {
                ctx.NotifyMethod.Invoke(tracker, new[] { job });
                Check(false, "invoking " + TargetTypeName + "." + TargetMethodName + " throws NullReferenceException (actual: no exception)");
            }
            catch (TargetInvocationException tie)
            {
                Exception inner = tie.InnerException;
                Check(inner is NullReferenceException,
                    "invoking " + TargetTypeName + "." + TargetMethodName + " throws NullReferenceException (actual: " + Describe(inner) + ")");
                Check(inner != null && inner.StackTrace != null &&
                      inner.StackTrace.Contains("MilianPatch_Pawn_FlightTracker_Notify_JobStarted"),
                    "NRE originates in Milira.MilianPatch_Pawn_FlightTracker_Notify_JobStarted.Prefix");
            }

            Console.WriteLine(failures == 0
                ? "RED RESULT: upstream failure reproduced - without a registered guard this crash is live"
                : "RED RESULT: unexpected");
            return failures == 0 ? 0 : 1;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static int RunGreen(string patchDll, string managed, string harmony, string milira)
        {
            failures = 0;
            Section("green " + patchDll);
            Context ctx = LoadContext(managed, harmony, milira, patchDll);

            // Worst-case registration order: Milira's prefix is already registered before our bootstrap runs.
            RegisterMiliraPatch(ctx);
            Check(CountPrefixes(ctx.NotifyMethod, MiliraHarmonyId) == 1, "upstream prefix registered first");
            Check(CountPrefixes(ctx.NotifyMethod, OurHarmonyId) == 0, "our Harmony ID is not registered before the bootstrap runs");

            RegisterCanEverFlyStub(ctx);

            List<Type> startupTypes = TypesWithAttribute(ctx.Patch, StartupAttributeName);
            Check(startupTypes.Count == 1, "bootstrap type found (" + startupTypes.Count + ")");

            if (startupTypes.Count == 1)
            {
                // Same mechanism as Verse.StaticConstructorOnStartupUtility.CallAll().
                RuntimeHelpers.RunClassConstructor(startupTypes[0].TypeHandle);
            }

            Patches info = Harmony.GetPatchInfo(ctx.NotifyMethod);
            Check(info != null, "target method is patched");
            List<Patch> ourPrefixes = info == null
                ? new List<Patch>()
                : info.Prefixes.Where(p => p.owner == OurHarmonyId).ToList();
            Check(ourPrefixes.Count == 1, "self-registration added our prefix exactly once");
            if (ourPrefixes.Count == 1)
            {
                Check(ourPrefixes[0].priority == Priority.HigherThanNormal,
                    "our prefix priority is HigherThanNormal(500), so it sorts before Milira's Normal(400)");
                Check(ourPrefixes[0].PatchMethod != null && ourPrefixes[0].PatchMethod.DeclaringType != null &&
                      ourPrefixes[0].PatchMethod.DeclaringType.FullName == OurPatchTypeName,
                    "registered prefix is " + OurPatchTypeName + ".Prefix");
            }

            List<Patch> miliraPrefixes = info == null
                ? new List<Patch>()
                : info.Prefixes.Where(p => p.owner == MiliraHarmonyId).ToList();
            Check(miliraPrefixes.Count == 1 && miliraPrefixes[0].priority == Priority.Normal,
                "upstream prefix priority is Normal(400)");

            // Exactly-once: run the bootstrap a second time; the static constructor is already done.
            if (startupTypes.Count == 1)
            {
                RuntimeHelpers.RunClassConstructor(startupTypes[0].TypeHandle);
            }
            info = Harmony.GetPatchInfo(ctx.NotifyMethod);
            Check(info != null && info.Prefixes.Count(p => p.owner == OurHarmonyId) == 1,
                "running the bootstrap again leaves exactly one registration");

            var ourHarmony = new Harmony(OurHarmonyId);
            List<MethodBase> ourPatchedMethods = ourHarmony.GetPatchedMethods().ToList();
            Check(ourPatchedMethods.Count == 1 && ourPatchedMethods[0].Equals(ctx.NotifyMethod),
                "our Harmony ID patches exactly " + TargetTypeName + "." + TargetMethodName);

            // End-to-end: the real patched method, the same call the game makes.
            object pawn = NewPawn(ctx, MiliraBodyDefName);
            object tracker = Activator.CreateInstance(ctx.TrackerType, pawn);
            object job = NewJob(ctx);
            bool crashed = false;
            try
            {
                ctx.NotifyMethod.Invoke(tracker, new[] { job });
            }
            catch (TargetInvocationException tie)
            {
                crashed = true;
                Check(false, "end-to-end invocation threw " + Describe(tie.InnerException));
            }
            if (!crashed)
            {
                Check(true, "end-to-end invocation of the patched method completes without an exception");
                Check(!GetJobFlying(job), "job.flying was forced to false (landing fallback)");
            }

            // Direct behavior of our prefix, bypassing Harmony dispatch.
            MethodInfo ourPrefix = ctx.Patch.GetType(OurPatchTypeName, throwOnError: true)
                .GetMethod("Prefix", BindingFlags.Static | BindingFlags.Public);
            Check(ourPrefix != null, "prefix method found for direct invocation");
            if (ourPrefix != null)
            {
                object humanPawn = NewPawn(ctx, "Human");
                object humanTracker = Activator.CreateInstance(ctx.TrackerType, humanPawn);
                object humanJob = NewJob(ctx);
                bool r1 = (bool)ourPrefix.Invoke(null, new[] { humanJob, humanTracker, humanPawn });
                Check(r1, "non-Milira body -> true (non-Milira path preserved)");

                object compPawn = NewPawn(ctx, MiliraBodyDefName);
                AddComp(compPawn, ctx.CompFlightControlType);
                object compTracker = Activator.CreateInstance(ctx.TrackerType, compPawn);
                object compJob = NewJob(ctx);
                bool r2 = (bool)ourPrefix.Invoke(null, new[] { compJob, compTracker, compPawn });
                Check(r2, "Milira body with CompFlightControl -> true (upstream handles it)");

                bool r3 = (bool)ourPrefix.Invoke(null, new[] { job, tracker, pawn });
                Check(!r3 && !GetJobFlying(job),
                    "Milira body without CompFlightControl -> false with landing fallback");
            }

            Console.WriteLine(failures == 0
                ? "GREEN RESULT: self-registration, ordering and crash guard all verified"
                : "GREEN RESULT: FAILED");
            return failures == 0 ? 0 : 1;
        }

        // ---------------------------------------------------------------- setup

        private static Context LoadContext(string managed, string harmonyPath, string miliraPath, string patchPath)
        {
            Program.AddProbeDirectory(managed);
            Program.AddProbeDirectory(Path.GetDirectoryName(Path.GetFullPath(harmonyPath)));
            Program.AddProbeDirectory(Path.GetDirectoryName(Path.GetFullPath(miliraPath)));
            if (patchPath != null)
            {
                Program.AddProbeDirectory(Path.GetDirectoryName(Path.GetFullPath(patchPath)));
            }

            var ctx = new Context();
            ctx.CSharp = Program.LoadAssemblyFrom(Path.Combine(managed, "Assembly-CSharp.dll"));
            ctx.Harmony = Program.LoadAssemblyFrom(harmonyPath);
            ctx.Milira = Program.LoadAssemblyFrom(miliraPath);
            if (patchPath != null)
            {
                ctx.Patch = Program.LoadAssemblyFrom(patchPath);
            }

            ctx.PawnType = ctx.CSharp.GetType("Verse.Pawn", throwOnError: true);
            ctx.ThingDefType = ctx.CSharp.GetType("Verse.ThingDef", throwOnError: true);
            ctx.RacePropsType = ctx.CSharp.GetType("Verse.RaceProperties", throwOnError: true);
            ctx.BodyDefType = ctx.CSharp.GetType("Verse.BodyDef", throwOnError: true);
            ctx.JobType = ctx.CSharp.GetType("Verse.AI.Job", throwOnError: true);
            ctx.TrackerType = ctx.CSharp.GetType(TargetTypeName, throwOnError: true);
            ctx.CompFlightControlType = ctx.Milira.GetType("Milira.CompFlightControl", throwOnError: true);
            ctx.NotifyMethod = ctx.TrackerType.GetMethod(TargetMethodName, new[] { ctx.JobType });
            if (ctx.NotifyMethod == null)
            {
                throw new MissingMethodException(ctx.TrackerType.FullName, TargetMethodName);
            }
            return ctx;
        }

        private static void RegisterMiliraPatch(Context ctx)
        {
            Type patchType = ctx.Milira.GetType(MiliraPatchTypeName, throwOnError: true);
            var harmony = new Harmony(MiliraHarmonyId);
            // PatchAll(assembly) would enumerate every type in Milira.dll and pull in HAR and
            // AncotLibrary types we do not need. CreateClassProcessor is the exact per-class
            // path that PatchAll uses, so this class registers identically.
            harmony.CreateClassProcessor(patchType).Patch();
        }

        private static void RegisterCanEverFlyStub(Context ctx)
        {
            // Pawn_FlightTracker.CanEverFly needs DefDatabase/StatDefOf state that only exists
            // inside a running game; stub it to true so the null-comp dereference is reachable
            // offline. This only affects this driver process.
            var harmony = new Harmony(StubHarmonyId);
            MethodInfo getter = ctx.TrackerType
                .GetProperty("CanEverFly", BindingFlags.Public | BindingFlags.Instance)
                .GetGetMethod();
            MethodInfo stub = typeof(Validation).GetMethod(nameof(CanEverFlyStub),
                BindingFlags.Static | BindingFlags.NonPublic);
            harmony.Patch(getter, prefix: new HarmonyMethod(stub));
        }

        private static bool CanEverFlyStub(ref bool __result)
        {
            __result = true;
            return false;
        }

        // ---------------------------------------------------------------- fabrication

        private static object NewPawn(Context ctx, string bodyDefName)
        {
            object body = FormatterServices.GetUninitializedObject(ctx.BodyDefType);
            SetField(body, "defName", bodyDefName);

            object race = FormatterServices.GetUninitializedObject(ctx.RacePropsType);
            SetField(race, "body", body);
            SetField(race, "canFlyInVacuum", true);

            object def = FormatterServices.GetUninitializedObject(ctx.ThingDefType);
            SetField(def, "race", race);

            object pawn = FormatterServices.GetUninitializedObject(ctx.PawnType);
            SetField(pawn, "def", def);
            return pawn;
        }

        private static object NewJob(Context ctx)
        {
            return FormatterServices.GetUninitializedObject(ctx.JobType);
        }

        private static void AddComp(object pawn, Type compType)
        {
            object comp = Activator.CreateInstance(compType);
            FieldInfo compsField = FindField(pawn.GetType(), "comps");
            IList list = (IList)Activator.CreateInstance(compsField.FieldType);
            list.Add(comp);
            compsField.SetValue(pawn, list);
        }

        private static bool GetJobFlying(object job)
        {
            FieldInfo field = job.GetType().GetField("flying", BindingFlags.Instance | BindingFlags.Public);
            return (bool)field.GetValue(job);
        }

        private static void SetField(object instance, string fieldName, object value)
        {
            FindField(instance.GetType(), fieldName).SetValue(instance, value);
        }

        private static FieldInfo FindField(Type type, string fieldName)
        {
            Type current = type;
            while (current != null)
            {
                FieldInfo field = current.GetField(fieldName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null)
                {
                    return field;
                }
                current = current.BaseType;
            }
            throw new MissingFieldException(type.FullName, fieldName);
        }

        // ---------------------------------------------------------------- helpers

        private static int CountPrefixes(MethodBase method, string owner)
        {
            Patches info = Harmony.GetPatchInfo(method);
            return info == null ? 0 : info.Prefixes.Count(p => p.owner == owner);
        }

        private static List<Type> TypesWithAttribute(Assembly assembly, string attributeFullName)
        {
            var result = new List<Type>();
            foreach (Type type in assembly.GetTypes())
            {
                foreach (object attribute in type.GetCustomAttributes(inherit: false))
                {
                    if (attribute.GetType().FullName == attributeFullName)
                    {
                        result.Add(type);
                        break;
                    }
                }
            }
            return result;
        }

        private static void Check(bool condition, string message)
        {
            if (condition)
            {
                Console.WriteLine("  PASS: " + message);
            }
            else
            {
                failures++;
                Console.WriteLine("  FAIL: " + message);
            }
        }

        private static void Section(string title)
        {
            Console.WriteLine(Environment.NewLine + "== " + title + " ==");
        }

        private static string Describe(Exception ex)
        {
            return ex == null ? "no exception" : ex.GetType().Name + ": " + ex.Message;
        }

        private sealed class Context
        {
            public Assembly CSharp;
            public Assembly Harmony;
            public Assembly Milira;
            public Assembly Patch;
            public Type PawnType;
            public Type ThingDefType;
            public Type RacePropsType;
            public Type BodyDefType;
            public Type JobType;
            public Type TrackerType;
            public Type CompFlightControlType;
            public MethodInfo NotifyMethod;
        }
    }
}
