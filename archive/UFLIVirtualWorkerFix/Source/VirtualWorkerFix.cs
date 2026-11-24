using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace WhiteGiver.UFLI.VirtualWorkerFix;

/// <summary>
/// Sidecar patch for "UF Light Industries Furniture" (KindSeal.UFLI).
///
/// UFRecipes.MapComponent_UFAutoVirtualWorkerPool.CreateWorker() generates a random
/// adult male colonist with forceNoIdeo=true, but forceNoBackstory=false. The random
/// backstory (and, depending on DLC/mods, traits/genes/hediffs) can set WorkTags that
/// make SkillRecord.TotallyDisabled true. Refresh() then writes the bench skill level
/// into skill.levelInt, but SkillRecord.GetLevel() returns 0 while TotallyDisabled is
/// true. As a result StatWorker logs
/// "Attempted to calculate value for disabled stat ButcheryMechanoidEfficiency"
/// in dev mode and ButcheryMechanoidEfficiency is evaluated at level 0 (0.75 instead
/// of the bench's 1.125 at level 15).
///
/// This patch normalizes the pooled pawn after every TryAcquire:
///   * replace disabling childhood/adulthood backstories with no-disable ones;
///   * remove traits/genes/hediffs that disable work or grant skill aptitude;
///   * refresh the skill-disable caches;
///   * set every Skills.skills level to the bench's skillLevel.
///
/// The ghost pawn is not saved by UFLI (the MapComponent does not scribe it), so
/// replacing its backstory with a real DefDatabase def is safe. A synthetic fallback
/// is only used when no suitable Def exists (e.g. the offline validation harness).
/// </summary>
public sealed class UFLIVirtualWorkerFixMod : Mod
{
    public UFLIVirtualWorkerFixMod(ModContentPack content) : base(content)
    {
        VirtualWorkerFix.Apply();
    }
}

public static class VirtualWorkerFix
{
    public const string HarmonyId = "whitegiver.ufli.virtualworkerfix";
    private const string LogPrefix = "[UFLI Virtual Worker Fix]";
    private const string PoolTypeName = "UFRecipes.MapComponent_UFAutoVirtualWorkerPool";

    private static readonly Dictionary<BackstorySlot, BackstoryDef> NoDisableBackstories = new();
    private static readonly HashSet<string> ReportedMismatches = new();
    private static readonly HashSet<string> ReportedResidualTags = new();
    private static readonly FieldInfo? CachedTotallyDisabled =
        AccessTools.Field(typeof(SkillRecord), "cachedTotallyDisabled");
    private static readonly FieldInfo? CachedPermanentlyDisabled =
        AccessTools.Field(typeof(SkillRecord), "cachedPermanentlyDisabled");

    private static bool applied;

    public static void Apply()
    {
        if (applied) return;
        applied = true;

        try
        {
            Type? pool = AccessTools.TypeByName(PoolTypeName);
            if (pool == null)
            {
                Log.Message($"{LogPrefix} {PoolTypeName} not found; UFLI is not loaded, patch skipped.");
                return;
            }

            Harmony harmony = new Harmony(HarmonyId);

            MethodInfo? tryAcquire = AccessTools.Method(pool, "TryAcquire");
            if (IsExpectedTryAcquire(tryAcquire))
            {
                try
                {
                    harmony.Patch(tryAcquire,
                        postfix: new HarmonyMethod(typeof(VirtualWorkerFix), nameof(TryAcquirePostfix)));
                    Log.Message($"{LogPrefix} Patched {PoolTypeName}.TryAcquire; pooled virtual workers are normalized on every acquire.");
                    return;
                }
                catch (Exception exception)
                {
                    Log.Warning($"{LogPrefix} Could not patch TryAcquire ({exception.Message}); trying Refresh fallback.");
                }
            }

            MethodInfo? refresh = AccessTools.Method(pool, "Refresh");
            if (IsExpectedRefresh(refresh))
            {
                harmony.Patch(refresh,
                    postfix: new HarmonyMethod(typeof(VirtualWorkerFix), nameof(RefreshPostfix)));
                Log.Message($"{LogPrefix} Patched {PoolTypeName}.Refresh (TryAcquire seam unavailable); pooled virtual workers are normalized on every acquire.");
                return;
            }

            Log.Error($"{LogPrefix} Expected TryAcquire(SkillDef,int,AutoWorkerIdeoMode,out Pawn) and Refresh(Pawn,SkillDef,int,AutoWorkerIdeoMode) were not found; patch not applied. UFLI may have changed; retire this sidecar.");
        }
        catch (Exception exception)
        {
            Log.Error($"{LogPrefix} Could not apply patch: {exception}");
        }
    }

    private static bool IsExpectedTryAcquire(MethodInfo? method)
    {
        if (method == null) return false;
        ParameterInfo[] parameters = method.GetParameters();
        return parameters.Length == 4
            && parameters[0].ParameterType == typeof(SkillDef)
            && parameters[1].ParameterType == typeof(int)
            && parameters[2].ParameterType.Name == "AutoWorkerIdeoMode"
            && parameters[3].ParameterType.IsByRef
            && parameters[3].ParameterType.GetElementType() == typeof(Pawn);
    }

    private static bool IsExpectedRefresh(MethodInfo? method)
    {
        if (method == null) return false;
        ParameterInfo[] parameters = method.GetParameters();
        return parameters.Length == 4
            && parameters[0].ParameterType == typeof(Pawn)
            && parameters[1].ParameterType == typeof(SkillDef)
            && parameters[2].ParameterType == typeof(int)
            && parameters[3].ParameterType.Name == "AutoWorkerIdeoMode";
    }

    private static void TryAcquirePostfix(bool __result, ref Pawn result, int skillLevel)
    {
        if (!__result || result == null) return;
        try
        {
            Normalize(result, skillLevel);
        }
        catch (Exception exception)
        {
            Log.Error($"{LogPrefix} Normalize failed for {result}: {exception}");
        }
    }

    private static void RefreshPostfix(Pawn pawn, int skillLevel)
    {
        if (pawn == null) return;
        try
        {
            Normalize(pawn, skillLevel);
        }
        catch (Exception exception)
        {
            Log.Error($"{LogPrefix} Normalize failed for {pawn}: {exception}");
        }
    }

    /// <summary>
    /// Makes the pooled pawn usable as a vanilla worker:
    /// no disabled work, no skill aptitude, and every skill at <paramref name="skillLevel"/>.
    /// Public so the offline RED/GREEN driver can exercise exactly the same code.
    /// </summary>
    public static void Normalize(Pawn pawn, int skillLevel)
    {
        if (pawn == null) return;

        int target = Math.Clamp(skillLevel, 0, 20);
        bool changed = false;

        Pawn_StoryTracker? story = pawn.story;
        if (story != null)
        {
            try
            {
                if (NeedsReplacement(story.Childhood, BackstorySlot.Childhood))
                {
                    story.Childhood = GetNoDisableBackstory(BackstorySlot.Childhood);
                    changed = true;
                }

                if (NeedsReplacement(story.Adulthood, BackstorySlot.Adulthood))
                {
                    story.Adulthood = GetNoDisableBackstory(BackstorySlot.Adulthood);
                    changed = true;
                }
            }
            catch (Exception exception)
            {
                Log.Warning($"{LogPrefix} Could not replace backstory on {pawn}: {exception.Message}");
            }

            try
            {
                TraitSet? traits = story.traits;
                if (traits?.allTraits != null)
                {
                    for (int i = traits.allTraits.Count - 1; i >= 0; i--)
                    {
                        Trait? trait = traits.allTraits[i];
                        if (trait == null) continue;
                        if (!TraitDisablesWork(trait) && !TraitGrantsAptitude(trait, pawn)) continue;
                        RemoveTraitSafely(pawn, traits, trait);
                        changed = true;
                    }
                }
            }
            catch (Exception exception)
            {
                Log.Warning($"{LogPrefix} Could not clear disabling traits on {pawn}: {exception.Message}");
            }
        }

        if (ModsConfig.BiotechActive && pawn.genes?.GenesListForReading != null)
        {
            try
            {
                foreach (Gene gene in pawn.genes.GenesListForReading.ToList())
                {
                    if (gene == null || !gene.Active) continue;
                    if (!GeneDisablesWork(gene) && !GeneGrantsAptitude(gene, pawn)) continue;
                    try
                    {
                        pawn.genes.RemoveGene(gene);
                        changed = true;
                    }
                    catch (Exception exception)
                    {
                        Log.Warning($"{LogPrefix} Could not remove gene {gene.def?.defName} on {pawn}: {exception.Message}");
                    }
                }
            }
            catch (Exception exception)
            {
                Log.Warning($"{LogPrefix} Could not inspect genes on {pawn}: {exception.Message}");
            }
        }

        if (pawn.health?.hediffSet?.hediffs != null)
        {
            try
            {
                foreach (Hediff hediff in pawn.health.hediffSet.hediffs.ToList())
                {
                    if (hediff == null) continue;
                    if (!HediffDisablesWork(hediff) && !HediffGrantsAptitude(hediff, pawn)) continue;
                    try
                    {
                        pawn.health.RemoveHediff(hediff);
                        changed = true;
                    }
                    catch (Exception exception)
                    {
                        Log.Warning($"{LogPrefix} Could not remove hediff {hediff.def?.defName} on {pawn}: {exception.Message}");
                    }
                }
            }
            catch (Exception exception)
            {
                Log.Warning($"{LogPrefix} Could not inspect hediffs on {pawn}: {exception.Message}");
            }
        }

        if (changed)
        {
            pawn.Notify_DisabledWorkTypesChanged();
        }

        pawn.skills?.DirtyAptitudes();

        if (pawn.CombinedDisabledWorkTags != WorkTags.None)
        {
            string key = pawn.thingIDNumber + "/" + pawn.CombinedDisabledWorkTags;
            if (ReportedResidualTags.Add(key))
            {
                Log.Warning($"{LogPrefix} {pawn} still reports disabled work tags {pawn.CombinedDisabledWorkTags} after normalization; forcing skill disable caches off.");
            }
        }

        if (pawn.skills?.skills == null) return;

        foreach (SkillRecord skill in pawn.skills.skills)
        {
            if (skill?.def == null) continue;

            if (skill.TotallyDisabled)
            {
                ForceSkillNotDisabled(skill);
            }

            int aptitude = skill.Aptitude;
            skill.levelInt = Math.Clamp(target - aptitude, 0, 20);
            skill.xpSinceLastLevel = 0f;

            int actual = skill.Level;
            if (actual != target)
            {
                string key = pawn.thingIDNumber + "/" + skill.def.defName + "/" + target + "/" + actual;
                if (ReportedMismatches.Add(key))
                {
                    Log.Warning($"{LogPrefix} Could not set {skill.def.defName} to {target} on {pawn} (aptitude {aptitude}, levelInt {skill.levelInt}, actual {actual}).");
                }
            }
        }
    }

    private static bool NeedsReplacement(BackstoryDef? current, BackstorySlot slot)
    {
        return current == null || current.slot != slot || current.workDisables != WorkTags.None;
    }

    private static BackstoryDef GetNoDisableBackstory(BackstorySlot slot)
    {
        if (NoDisableBackstories.TryGetValue(slot, out BackstoryDef? cached) && cached != null)
        {
            return cached;
        }

        BackstoryDef? found = null;
        try
        {
            foreach (BackstoryDef def in DefDatabase<BackstoryDef>.AllDefsListForReading)
            {
                if (def == null || def.slot != slot || def.workDisables != WorkTags.None) continue;
                if (slot == BackstorySlot.Childhood && def.IsPlayerColonyChildBackstory) continue;
                if (string.IsNullOrEmpty(def.title) || string.IsNullOrEmpty(def.titleShort)) continue;
                found = def;
                break;
            }
        }
        catch (Exception exception)
        {
            Log.Warning($"{LogPrefix} Could not search BackstoryDefs for {slot}: {exception.Message}");
        }

        found ??= CreateSyntheticBackstory(slot);
        NoDisableBackstories[slot] = found;
        return found;
    }

    private static BackstoryDef CreateSyntheticBackstory(BackstorySlot slot)
    {
        return new BackstoryDef
        {
            defName = slot == BackstorySlot.Childhood
                ? "UFLIVWF_ChildhoodNoWorkDisables"
                : "UFLIVWF_AdulthoodNoWorkDisables",
            label = "colonist",
            description = "Synthetic no-disable backstory created by UFLI Virtual Worker Fix.",
            slot = slot,
            title = "colonist",
            titleFemale = "colonist",
            titleShort = "colonist",
            titleShortFemale = "colonist",
            workDisables = WorkTags.None,
            requiredWorkTags = WorkTags.None,
            shuffleable = false,
        };
    }

    private static bool TraitDisablesWork(Trait trait)
    {
        TraitDef? def = trait.def;
        if (def == null) return false;
        if (def.disabledWorkTags != WorkTags.None) return true;
        return def.disabledWorkTypes != null && def.disabledWorkTypes.Count > 0;
    }

    private static bool TraitGrantsAptitude(Trait trait, Pawn pawn)
    {
        if (trait.def == null || pawn.skills?.skills == null) return false;
        TraitDegreeData? data = trait.def.DataAtDegree(trait.Degree);
        if (data == null) return false;
        foreach (SkillRecord skill in pawn.skills.skills)
        {
            if (skill?.def != null && data.AptitudeFor(skill.def) != 0) return true;
        }
        return false;
    }

    private static bool GeneDisablesWork(Gene gene)
    {
        return gene.def != null && gene.def.disabledWorkTags != WorkTags.None;
    }

    private static bool GeneGrantsAptitude(Gene gene, Pawn pawn)
    {
        if (gene.def == null || pawn.skills?.skills == null) return false;
        foreach (SkillRecord skill in pawn.skills.skills)
        {
            if (skill?.def != null && gene.def.AptitudeFor(skill.def) != 0) return true;
        }
        return false;
    }

    private static bool HediffDisablesWork(Hediff hediff)
    {
        return hediff.CurStage != null && hediff.CurStage.disabledWorkTags != WorkTags.None;
    }

    private static bool HediffGrantsAptitude(Hediff hediff, Pawn pawn)
    {
        if (hediff.def == null || pawn.skills?.skills == null) return false;
        foreach (SkillRecord skill in pawn.skills.skills)
        {
            if (skill?.def != null && hediff.def.AptitudeFor(skill.def) != 0) return true;
        }
        return false;
    }

    private static void RemoveTraitSafely(Pawn pawn, TraitSet traits, Trait trait)
    {
        try
        {
            if (pawn.needs != null && pawn.abilities != null)
            {
                traits.RemoveTrait(trait);
                return;
            }
        }
        catch (Exception exception)
        {
            Log.Warning($"{LogPrefix} Could not use TraitSet.RemoveTrait for {trait.def?.defName} on {pawn}: {exception.Message}");
        }

        try
        {
            traits.allTraits.Remove(trait);
        }
        catch (Exception exception)
        {
            Log.Warning($"{LogPrefix} Could not remove trait {trait.def?.defName} on {pawn}: {exception.Message}");
        }
    }

    private static void ForceSkillNotDisabled(SkillRecord skill)
    {
        try
        {
            CachedTotallyDisabled?.SetValue(skill, BoolUnknown.False);
            CachedPermanentlyDisabled?.SetValue(skill, BoolUnknown.False);
        }
        catch (Exception exception)
        {
            Log.Warning($"{LogPrefix} Could not force disable caches for {skill.def?.defName}: {exception.Message}");
        }
    }
}
