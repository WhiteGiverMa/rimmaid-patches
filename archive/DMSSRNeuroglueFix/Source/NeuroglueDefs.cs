using Verse;

namespace WhiteGiver.DMSSRNeuroglueFix
{
    /// <summary>
    /// Resolves and caches all relevant Def references once so hot paths compare object
    /// references instead of strings or DefDatabase lookups.
    /// </summary>
    internal static class NeuroglueDefs
    {
        private static bool initialized;

        internal static ThingDef? Neuroglue { get; private set; }
        internal static JobDef? UseNeuroglueJob { get; private set; }
        internal static HediffDef? NeuroglueHigh { get; private set; }
        internal static HediffDef? DegenerativeSyndrome { get; private set; }
        internal static HediffDef? SyntheticRejection { get; private set; }
        internal static HediffDef? BionicShockRemove { get; private set; }
        internal static HediffDef? BionicShock { get; private set; }

        internal static void EnsureInitialized()
        {
            if (initialized && Neuroglue != null && UseNeuroglueJob != null)
            {
                return;
            }

            Neuroglue = DefDatabase<ThingDef>.GetNamedSilentFail("Neuroglue");
            UseNeuroglueJob = DefDatabase<JobDef>.GetNamedSilentFail("DMS_UseNeuroglue");
            NeuroglueHigh = DefDatabase<HediffDef>.GetNamedSilentFail("NeuroglueHigh");
            DegenerativeSyndrome = DefDatabase<HediffDef>.GetNamedSilentFail("DMS_DegenerativeSyndrome");
            SyntheticRejection = DefDatabase<HediffDef>.GetNamedSilentFail("DMS_SyntheticRejection");
            BionicShockRemove = DefDatabase<HediffDef>.GetNamedSilentFail("DMS_BionicShockRemove");
            BionicShock = DefDatabase<HediffDef>.GetNamedSilentFail("DMS_BionicShock");
            initialized = Neuroglue != null && UseNeuroglueJob != null;
        }

        internal static void ResetForTesting()
        {
            initialized = false;
            Neuroglue = null;
            UseNeuroglueJob = null;
            NeuroglueHigh = null;
            DegenerativeSyndrome = null;
            SyntheticRejection = null;
            BionicShockRemove = null;
            BionicShock = null;
        }

        internal static bool IsNeuroglue(ThingDef? def)
        {
            EnsureInitialized();
            if (def == null)
            {
                return false;
            }

            return Neuroglue != null ? ReferenceEquals(def, Neuroglue) : def.defName == "Neuroglue";
        }
    }
}
