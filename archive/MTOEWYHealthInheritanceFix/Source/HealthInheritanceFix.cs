using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml;
using HarmonyLib;
using RimWorld;
using Verse;

namespace WhiteGiver.MTOEWYHealthInheritanceFix;

public sealed class TransferHediffEntry : IExposable
{
    public string defName = "";
    public string bodyDefName = "";
    public int partIndex = -1;
    public string partDefName = "";
    public string partCustomLabel = "";
    public float severity;
    public int level = -1;
    public bool visible;

    public void ExposeData()
    {
        Scribe_Values.Look(ref defName, "defName", "", forceSave: true);
        Scribe_Values.Look(ref bodyDefName, "bodyDefName", "", forceSave: true);
        Scribe_Values.Look(ref partIndex, "partIndex", -1, forceSave: true);
        Scribe_Values.Look(ref partDefName, "partDefName", "", forceSave: true);
        Scribe_Values.Look(ref partCustomLabel, "partCustomLabel", "", forceSave: true);
        Scribe_Values.Look(ref severity, "severity", 0f, forceSave: true);
        Scribe_Values.Look(ref level, "level", -1, forceSave: true);
        Scribe_Values.Look(ref visible, "visible", false, forceSave: true);
    }
}

[StaticConstructorOnStartup]
public static class HealthInheritanceFix
{
    public const string HarmonyId = "whitegiver.mtoewy.healthinheritancefix";
    private const string LogPrefix = "[MTOEWY Health Inheritance Fix]";
    private const string NeuralTypeName = "MiliraTheOneEntwinedWithYou.Transfer.IdealTransferNeural";
    private const string CodecTypeName = "MiliraTheOneEntwinedWithYou.Transfer.IdealTransferCodec";
    private static readonly ConditionalWeakTable<object, List<TransferHediffEntry>> Captures = new();
    private static bool attempted;

    static HealthInheritanceFix() => Apply();

    public static void Apply()
    {
        if (attempted) return;
        attempted = true;
        var harmony = new Harmony(HarmonyId);
        try
        {
            Type neural = AccessTools.TypeByName(NeuralTypeName)
                ?? throw new MissingMethodException($"Missing target type {NeuralTypeName}.");
            Type codec = AccessTools.TypeByName(CodecTypeName)
                ?? throw new MissingMethodException($"Missing target type {CodecTypeName}.");
            MethodInfo capture = RequiredMethod(neural, "Capture", new[] { typeof(Pawn) });
            MethodInfo exposeData = RequiredMethod(neural, "ExposeData", Type.EmptyTypes);
            MethodInfo project = RequiredMethod(codec, "Project", new[] { typeof(XmlDocument) });
            MethodInfo restore = RequiredMethod(neural, "Restore", new[] { typeof(Pawn) });

            harmony.Patch(capture, postfix: new HarmonyMethod(typeof(HealthInheritanceFix), nameof(CapturePostfix)));
            harmony.Patch(exposeData, postfix: new HarmonyMethod(typeof(HealthInheritanceFix), nameof(ExposeDataPostfix)));
            harmony.Patch(project,
                prefix: new HarmonyMethod(typeof(HealthInheritanceFix), nameof(ProjectPrefix)),
                postfix: new HarmonyMethod(typeof(HealthInheritanceFix), nameof(ProjectPostfix)));
            harmony.Patch(restore, postfix: new HarmonyMethod(typeof(HealthInheritanceFix), nameof(RestorePostfix)));
            Log.Message($"{LogPrefix} Patched neural capture, persistence, projection and restoration.");
        }
        catch (Exception exception)
        {
            harmony.UnpatchAll(HarmonyId);
            Log.Error($"{LogPrefix} Target signature mismatch or patch failure; no patch retained: {exception}");
        }
    }

    private static MethodInfo RequiredMethod(Type type, string name, Type[] parameters) =>
        AccessTools.Method(type, name, parameters)
        ?? throw new MissingMethodException($"Missing target method {type.FullName}.{name}({string.Join(", ", parameters.Select(parameter => parameter.FullName))}).");

    public static void CapturePostfix(Pawn pawn, object? __result)
    {
        if (__result == null) return;
        var entries = new List<TransferHediffEntry>();
        if (pawn?.health?.hediffSet?.hediffs != null)
        {
            foreach (Hediff hediff in pawn.health.hediffSet.hediffs)
            {
                if (!ShouldCapture(hediff)) continue;
                entries.Add(new TransferHediffEntry
                {
                    defName = hediff.def.defName,
                    bodyDefName = hediff.Part?.body?.defName ?? "",
                    partIndex = hediff.Part?.Index ?? -1,
                    partDefName = hediff.Part?.def?.defName ?? "",
                    partCustomLabel = hediff.Part?.customLabel ?? "",
                    severity = hediff.Severity,
                    level = hediff is Hediff_Level levelHediff ? levelHediff.level : -1,
                    visible = hediff.Visible
                });
            }
        }
        SetEntries(__result, entries);
    }

    private static bool ShouldCapture(Hediff? hediff)
    {
        if (hediff?.def == null || string.IsNullOrEmpty(hediff.def.defName)) return false;
        if (hediff.def.defName is "PsychicAmplifier" or "MechlinkImplant" or "MTOEWY_EntwinedBond"
            or "MTOEWY_KitAura" or "MTOEWY_RewardAura") return false;
        if (hediff is Hediff_Injury or Hediff_MissingPart or Hediff_Pregnant) return false;

        bool persistentPart = hediff is Hediff_Implant or Hediff_AddedPart or Hediff_Level;
        if (hediff is HediffWithComps { comps: not null } withComps)
        {
            foreach (HediffComp comp in withComps.comps)
            {
                if (comp is HediffComp_RemoveIfApparelDropped) return false;
                if (!persistentPart && comp is HediffComp_Disappears or HediffComp_DisappearsOnDeath) return false;
            }
        }
        if (HasWornApparel(hediff)) return false;
        if (hediff.def.isBad && !persistentPart && !HediffUtility.IsPermanent(hediff)) return false;
        return true;
    }

    private static bool HasWornApparel(Hediff hediff)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        for (Type? type = hediff.GetType(); type != null && type != typeof(object); type = type.BaseType)
        {
            foreach (FieldInfo field in type.GetFields(flags))
            {
                if (field.Name == "wornApparel" && field.GetValue(hediff) != null) return true;
            }
            foreach (PropertyInfo property in type.GetProperties(flags))
            {
                if (property.Name == "wornApparel" && property.CanRead && property.GetIndexParameters().Length == 0
                    && property.GetValue(hediff) != null) return true;
            }
        }
        return false;
    }

    public static void ExposeDataPostfix(object __instance)
    {
        if (Scribe.mode == LoadSaveMode.Saving)
        {
            if (!Captures.TryGetValue(__instance, out List<TransferHediffEntry>? captured) || captured.Count == 0) return;
            List<TransferHediffEntry>? entries = captured;
            Scribe_Collections.Look(ref entries, "hediffs", LookMode.Deep);
        }
        else if (Scribe.mode == LoadSaveMode.LoadingVars)
        {
            List<TransferHediffEntry>? entries = null;
            Scribe_Collections.Look(ref entries, "hediffs", LookMode.Deep);
            if (entries == null) Captures.Remove(__instance);
            else SetEntries(__instance, entries);
        }
    }

    public static void ProjectPrefix(XmlDocument doc, ref XmlElement? __state)
    {
        __state = null;
        XmlElement? neural = doc?.DocumentElement?["neural"];
        XmlElement? hediffs = neural?["hediffs"];
        if (hediffs == null) return;
        neural!.RemoveChild(hediffs);
        __state = hediffs;
    }

    public static void ProjectPostfix(XmlDocument doc, XmlElement? __state)
    {
        XmlElement? neural = doc?.DocumentElement?["neural"];
        if (__state == null || neural == null) return;
        KeepChildren(__state, "li");
        foreach (XmlElement entry in __state.ChildNodes.OfType<XmlElement>().ToArray())
        {
            KeepChildren(entry, "defName bodyDefName partIndex partDefName partCustomLabel severity level visible");
        }
        neural.AppendChild(__state);
    }

    private static void KeepChildren(XmlNode parent, string names)
    {
        var allowed = new HashSet<string>(names.Split(' '), StringComparer.Ordinal);
        for (int index = parent.ChildNodes.Count - 1; index >= 0; index--)
        {
            XmlNode? child = parent.ChildNodes[index];
            if (child != null && !allowed.Contains(child.Name)) parent.RemoveChild(child);
        }
    }

    public static void RestorePostfix(object __instance, Pawn pawn)
    {
        if (!Captures.TryGetValue(__instance, out List<TransferHediffEntry>? entries) || entries.Count == 0) return;
        if (pawn?.health?.hediffSet == null) return;

        for (int phase = 0; phase < 2; phase++)
        {
            foreach (TransferHediffEntry entry in entries)
            {
                HediffDef? def = DefDatabase<HediffDef>.GetNamedSilentFail(entry.defName);
                if (def == null)
                {
                    Log.Warning($"{LogPrefix} Skipping missing HediffDef '{entry.defName}'.");
                    continue;
                }
                bool addedPart = typeof(Hediff_AddedPart).IsAssignableFrom(def.hediffClass);
                if ((phase == 0) != addedPart) continue;

                BodyPartRecord? targetPart = ResolveTargetPart(pawn, entry, def);
                if (entry.partIndex >= 0 && targetPart == null)
                {
                    Log.Warning($"{LogPrefix} Skipping '{entry.defName}': body part {entry.partIndex} ('{entry.partCustomLabel}'/'{entry.partDefName}') is unavailable or missing.");
                    continue;
                }

                Hediff? existing = pawn.health.hediffSet.hediffs.FirstOrDefault(hediff => hediff.def == def && hediff.Part == targetPart);
                if (existing != null)
                {
                    if (existing is Hediff_Level existingLevel && entry.level > 0) existingLevel.level = entry.level;
                    existing.Severity = entry.severity;
                    continue;
                }

                Hediff hediff = HediffMaker.MakeHediff(def, pawn, targetPart);
                if (hediff is Hediff_Level levelHediff && entry.level > 0) levelHediff.level = entry.level;
                if (hediff is Hediff_Psylink psylink) psylink.suppressPostAddLetter = true;
                hediff.Severity = entry.severity;
                pawn.health.AddHediff(hediff, targetPart, null, null);
                if (hediff is Hediff_Level addedLevel && entry.level > 0) addedLevel.level = entry.level;
                hediff.Severity = entry.severity;

                if (hediff is Hediff_AddedPart)
                {
                    foreach (Hediff_MissingPart missing in pawn.health.hediffSet.hediffs.OfType<Hediff_MissingPart>())
                    {
                        if (missing.Part != null && IsDescendantOf(missing.Part, targetPart)) missing.IsFresh = false;
                    }
                }
            }
        }
        pawn.health.hediffSet.DirtyCache();
        pawn.health.capacities.Notify_CapacityLevelsDirty();
    }

    private static BodyPartRecord? ResolveTargetPart(Pawn pawn, TransferHediffEntry entry, HediffDef def)
    {
        if (entry.partIndex < 0) return null;
        List<BodyPartRecord>? allParts = pawn.RaceProps?.body?.AllParts;
        if (allParts == null || allParts.Count == 0) return null;

        BodyPartRecord? target = null;
        if (entry.partIndex < allParts.Count)
        {
            BodyPartRecord indexed = allParts[entry.partIndex];
            if ((string.IsNullOrEmpty(entry.partDefName) || indexed.def?.defName == entry.partDefName)
                && (string.IsNullOrEmpty(entry.partCustomLabel) || indexed.customLabel == entry.partCustomLabel)) target = indexed;
        }
        if (target == null && !string.IsNullOrEmpty(entry.partCustomLabel))
        {
            target = allParts.FirstOrDefault(part => part.customLabel == entry.partCustomLabel && !HasHediffAtPart(pawn, def, part));
        }
        target ??= allParts.FirstOrDefault(part => part.def?.defName == entry.partDefName && !HasHediffAtPart(pawn, def, part));
        if (target == null) return null;

        bool addedPart = typeof(Hediff_AddedPart).IsAssignableFrom(def.hediffClass);
        for (BodyPartRecord? current = addedPart ? target.parent : target; current != null; current = current.parent)
        {
            if (pawn.health.hediffSet.hediffs.Any(hediff => hediff is Hediff_MissingPart && hediff.Part == current)) return null;
        }
        return target;
    }

    private static bool HasHediffAtPart(Pawn pawn, HediffDef def, BodyPartRecord part) =>
        pawn.health.hediffSet.hediffs.Any(hediff => hediff.def == def && hediff.Part == part);

    private static bool IsDescendantOf(BodyPartRecord candidate, BodyPartRecord? ancestor)
    {
        for (BodyPartRecord? current = candidate.parent; current != null; current = current.parent)
        {
            if (current == ancestor) return true;
        }
        return false;
    }

    private static void SetEntries(object key, List<TransferHediffEntry> entries)
    {
        Captures.Remove(key);
        Captures.Add(key, entries);
    }
}
