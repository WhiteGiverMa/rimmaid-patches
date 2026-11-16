using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using DMSSR.FloatMenus;
using DMSSR.Hediffs;
using DMSSR.Jobs;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace WhiteGiver.DMSSRNeuroglueFix
{
    internal static class UseNeurogluePatches
    {
        private static readonly FloatMenuOption[] EmptyOptions = Array.Empty<FloatMenuOption>();

        internal static bool CanRefillPatched { get; private set; }
        internal static bool MakeNewToilsPatched { get; private set; }
        internal static bool FloatMenuPatched { get; private set; }
        internal static bool UpstreamOutcomeFilterDetected { get; private set; }

        internal static void Apply(Harmony harmony)
        {
            UpstreamOutcomeFilterDetected = DetectUpstreamRefillOnlyFilter();

            MethodInfo? canRefill = AccessTools.Method(
                typeof(JobDriver_UseNeuroglue),
                nameof(JobDriver_UseNeuroglue.CanRefill),
                new[] { typeof(Pawn) });
            if (canRefill != null && canRefill.ReturnType == typeof(bool))
            {
                harmony.Patch(
                    canRefill,
                    postfix: new HarmonyMethod(typeof(UseNeurogluePatches), nameof(CanRefillPostfix)));
                CanRefillPatched = true;
            }
            else
            {
                SafeLog.Warning("JobDriver_UseNeuroglue.CanRefill(Pawn) signature changed; skipped patch.");
            }

            MethodInfo? makeNewToils = AccessTools.Method(
                typeof(JobDriver_UseNeuroglue),
                "MakeNewToils",
                Type.EmptyTypes);
            if (makeNewToils != null && typeof(IEnumerable<Toil>).IsAssignableFrom(makeNewToils.ReturnType))
            {
                harmony.Patch(
                    makeNewToils,
                    postfix: new HarmonyMethod(typeof(UseNeurogluePatches), nameof(MakeNewToilsPostfix)));
                MakeNewToilsPatched = true;
            }
            else
            {
                SafeLog.Warning("JobDriver_UseNeuroglue.MakeNewToils() signature changed; skipped patch.");
            }

            MethodInfo? getOptionsFor = AccessTools.Method(
                typeof(FloatMenuOptionProvider_UseNeuroglue),
                nameof(FloatMenuOptionProvider_UseNeuroglue.GetOptionsFor),
                new[] { typeof(Thing), typeof(FloatMenuContext) });
            if (getOptionsFor != null && typeof(IEnumerable<FloatMenuOption>).IsAssignableFrom(getOptionsFor.ReturnType))
            {
                harmony.Patch(
                    getOptionsFor,
                    prefix: new HarmonyMethod(typeof(UseNeurogluePatches), nameof(GetOptionsForPrefix)),
                    postfix: new HarmonyMethod(typeof(UseNeurogluePatches), nameof(GetOptionsForPostfix)));
                FloatMenuPatched = true;
            }
            else
            {
                SafeLog.Warning("FloatMenuOptionProvider_UseNeuroglue.GetOptionsFor signature changed; skipped patch.");
            }
        }

        private static bool DetectUpstreamRefillOnlyFilter()
        {
            try
            {
                foreach (MethodInfo method in AccessTools.GetDeclaredMethods(typeof(JobDriver_UseNeuroglue)))
                {
                    if (!method.Name.Contains("MakeNewToils", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    foreach (CodeInstruction instruction in PatchProcessor.GetOriginalInstructions(method))
                    {
                        if (instruction.opcode == OpCodes.Isinst
                            && instruction.operand is Type operandType
                            && operandType == typeof(IngestionOutcomeDoer_RefillBiogel))
                        {
                            return true;
                        }
                    }
                }
            }
            catch
            {
                // Purely diagnostic; never fail startup on IL inspection.
            }

            return false;
        }

        private static void CanRefillPostfix(Pawn? target, ref bool __result)
        {
            if (__result || target == null || target.Dead || target.RaceProps == null || !target.RaceProps.IsMechanoid)
            {
                return;
            }

            if (MechBiogelCache.NeedsTherapeuticDose(target))
            {
                __result = true;
            }
        }

        private static IEnumerable<Toil> MakeNewToilsPostfix(IEnumerable<Toil> values, JobDriver_UseNeuroglue __instance)
        {
            using IEnumerator<Toil> enumerator = values.GetEnumerator();
            if (!enumerator.MoveNext())
            {
                yield break;
            }

            Toil buffered = enumerator.Current;
            while (enumerator.MoveNext())
            {
                yield return buffered;
                buffered = enumerator.Current;
            }

            JobDriver_UseNeuroglue driver = __instance;
            buffered.initAction = () => CompleteUseNeuroglue(driver);
            yield return buffered;
        }

        internal static void CompleteUseNeuroglue(JobDriver_UseNeuroglue driver)
        {
            Pawn? actor = driver.pawn;
            Pawn? recipient = driver.job?.GetTarget(TargetIndex.A).Thing as Pawn;
            Thing? carriedThing = actor?.carryTracker?.CarriedThing;
            if (recipient == null
                || carriedThing == null
                || !JobDriver_UseNeuroglue.CanRefill(recipient)
                || !NeuroglueDefs.IsNeuroglue(carriedThing.def))
            {
                driver.EndJobWith(JobCondition.Incompletable);
                return;
            }

            List<IngestionOutcomeDoer>? outcomeDoers = carriedThing.def.ingestible?.outcomeDoers;
            if (outcomeDoers == null || outcomeDoers.Count == 0)
            {
                driver.EndJobWith(JobCondition.Incompletable);
                return;
            }

            bool anyApplied = false;
            for (int i = 0; i < outcomeDoers.Count; i++)
            {
                IngestionOutcomeDoer? doer = outcomeDoers[i];
                if (doer != null)
                {
                    doer.DoIngestionOutcome(recipient, carriedThing, 1);
                    anyApplied = true;
                }
            }

            if (!anyApplied)
            {
                driver.EndJobWith(JobCondition.Incompletable);
                return;
            }

            Thing consumed = carriedThing.SplitOff(1);
            if (!consumed.Destroyed)
            {
                consumed.Destroy(DestroyMode.Vanish);
            }
        }

        private static bool GetOptionsForPrefix()
        {
            // Skip compiler-generated iterator allocation cleanly; GetOptionsForPostfix supplies the result.
            return false;
        }

        private static void GetOptionsForPostfix(
            Thing clickedThing,
            FloatMenuContext context,
            ref IEnumerable<FloatMenuOption> __result)
        {
            if (UseNeurogluePatches.TryResolveFloatMenuTarget(clickedThing, context, out Pawn? actor, out Pawn? target, out Thing? gel, out JobDef? jobDef))
            {
                FloatMenuOption option = new(
                    TryFormatUseOnLabel(target!),
                    () =>
                    {
                        Job job = JobMaker.MakeJob(jobDef!, target!, gel!);
                        job.count = 1;
                        job.playerForced = true;
                        actor!.jobs.TryTakeOrderedJob(job, JobTag.Misc, false);
                    },
                    MenuOptionPriority.Default);

                __result = new[] { option };
                return;
            }

            __result = EmptyOptions;
        }

        internal static bool TryResolveFloatMenuTarget(
            Thing? clickedThing,
            FloatMenuContext? context,
            out Pawn? actor,
            out Pawn? target,
            out Thing? gel,
            out JobDef? jobDef)
        {
            actor = context?.FirstSelectedPawn;
            target = null;
            gel = null;
            jobDef = null;

            if (actor == null || actor.health?.capacities?.CapableOf(PawnCapacityDefOf.Manipulation) != true)
            {
                return false;
            }

            target = (clickedThing as Pawn) ?? actor;
            if (target.Faction != Faction.OfPlayer || !JobDriver_UseNeuroglue.CanRefill(target))
            {
                return false;
            }

            gel = clickedThing is Pawn ? NeuroglueFinder.FindClosestAvailable(actor) : clickedThing;
            if (gel == null
                || !NeuroglueDefs.IsNeuroglue(gel.def)
                || !NeuroglueFinder.IsAvailable(actor, gel)
                || (target != actor
                    && (!actor.CanReach(target, PathEndMode.Touch, Danger.Some)
                        || !actor.CanReserve(target, 1, -1, null, false))))
            {
                return false;
            }

            jobDef = DMS_JobDefOf.DMS_UseNeuroglue ?? NeuroglueDefs.UseNeuroglueJob;
            return jobDef != null;
        }

        private static string TryFormatUseOnLabel(Pawn target)
        {
            string shortLabel = target.LabelShortCap;
            if (LanguageDatabase.activeLanguage != null)
            {
                return "DMSSR.Biogel.UseOn".Translate(shortLabel);
            }

            return "Use neuroglue on " + shortLabel;
        }
    }
}
