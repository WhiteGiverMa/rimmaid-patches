using System;
using System.Collections.Generic;
using System.Reflection;
using DMSSR.Hediffs;
using HarmonyLib;
using Verse;

namespace WhiteGiver.DMSSRUnifiedFix.Audited;

/// <summary>
/// DMSSR recognizes nanomech infections only by "nano"/"nanite"/"embryt" substrings. Vanilla's
/// <c>FibrousMechanites</c> and <c>SensoryMechanites</c> use "mechanite", so Immune
/// Heterogenization never doubled their duration and Immune Mutation deleted them as ordinary
/// diseases. Recognize any infection/immunizable hediff whose def name or tags contain "mechanite".
/// </summary>
internal static class MechaniteRecognitionFix
{
    internal static bool Installed { get; private set; }

    internal static void Apply(Harmony harmony)
    {
        Type? utility = typeof(DMS_HediffDefOf).Assembly.GetType("DMSSR.Gene.GeneSetUtility", false)
            ?? throw new MissingMethodException("DMSSR.Gene.GeneSetUtility");
        MethodInfo matcher = AccessTools.Method(utility, "IsNanomechInfection")
            ?? throw new MissingMethodException("DMSSR.Gene.GeneSetUtility.IsNanomechInfection");

        harmony.Patch(matcher, postfix: new HarmonyMethod(
            typeof(MechaniteRecognitionFix), nameof(IsNanomechInfectionPostfix)));
        Installed = true;
    }

    private static void IsNanomechInfectionPostfix(Hediff hediff, ref bool __result)
    {
        if (__result || hediff?.def == null)
        {
            return;
        }

        HediffDef def = hediff.def;
        if (!def.isInfection && !def.PossibleToDevelopImmunityNaturally())
        {
            return;
        }

        if (ContainsMechanite(def.defName))
        {
            __result = true;
            return;
        }

        List<string>? tags = def.tags;
        if (tags == null)
        {
            return;
        }
        for (int i = 0; i < tags.Count; i++)
        {
            if (ContainsMechanite(tags[i]))
            {
                __result = true;
                return;
            }
        }
    }

    private static bool ContainsMechanite(string? text)
    {
        return text != null && text.IndexOf("mechanite", StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
