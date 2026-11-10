using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml;
using HarmonyLib;
using RimWorld;
using Verse;

internal static class ReadEntryRegression
{
    private static int entries;
    private static int checks;

    public static int Run()
    {
        int failures = 0;
        MethodInfo target = typeof(CompEquippableAbility).GetMethod(nameof(CompEquippableAbility.PostExposeData))!;
        Harmony guard = new("whitegiver.equippableability.validation.depth");
        guard.Patch(target, prefix: new HarmonyMethod(typeof(ReadEntryRegression), nameof(Guard)) { priority = Priority.First });
        try
        {
            foreach (LoadSaveMode mode in new[] { LoadSaveMode.Inactive, LoadSaveMode.LoadingVars,
                         LoadSaveMode.ResolvingCrossRefs, LoadSaveMode.PostLoadInit })
            {
                entries = 0;
                Scribe.mode = mode;
                XmlDocument xml = new();
                xml.LoadXml("<comp><verbTracker IsNull=\"True\"/><ability IsNull=\"True\"/></comp>");
                Scribe.loader.curXmlParent = xml.DocumentElement;
                CompEquippableAbility comp = new() { props = new CompProperties_EquippableAbility() };
                try
                {
                    comp.PostExposeData();
                    bool ok = entries == 1 && (mode != LoadSaveMode.LoadingVars || comp.verbTracker == null);
                    Console.WriteLine((ok ? "GREEN " : "RED   ") + "real PostExposeData " + mode
                        + " runs once and preserves base serialization; entries=" + entries);
                    if (!ok) failures++;
                }
                catch (Exception exception)
                {
                    failures++;
                    Console.WriteLine("RED   real PostExposeData " + mode + ": " + exception.GetBaseException().Message);
                }
            }
            foreach (bool psycast in new[] { false, true })
            {
                try
                {
                    RoundTrip(psycast);
                }
                catch (Exception exception)
                {
                    failures++;
                    Console.WriteLine("RED   real Scribe round-trip " + (psycast ? "Psycast" : "Ability") + ": " + exception);
                }
            }
        }
        finally
        {
            Scribe.mode = LoadSaveMode.Inactive;
            Scribe.loader.curXmlParent = null;
            guard.Unpatch(target, HarmonyPatchType.Prefix, guard.Id);
        }
        return failures;
    }

    private static void RoundTrip(bool psycast)
    {
        string name = psycast ? "ValidationPsycast" : "ValidationAbility";
        AbilityDef definition = new()
        {
            defName = name,
            abilityClass = psycast ? typeof(Psycast) : typeof(Ability),
            verbProperties = new VerbProperties { verbClass = typeof(Verb_Shoot), isPrimary = true },
            comps = new List<AbilityCompProperties>(),
            charges = 3
        };
        DefDatabase<AbilityDef>.Add(definition);
        Ability ability = psycast ? new Psycast(null!) : new Ability();
        ability.def = definition;
        ability.Id = psycast ? 7202 : 7201;
        ability.maxCharges = 3;
        ability.RemainingCharges = 2;
        ability.lastCastTick = 88;
        Set(ability, "cooldownDuration", 600);
        Set(ability, "cooldownEndTick", 1200);
        Set(ability, "inCooldown", true);
        VerbTracker abilityVerbs = new(ability);
        string abilityVerbId = ability.UniqueVerbOwnerID() + "_0";
        Verb abilityVerb = NewVerb(abilityVerbId, abilityVerbs);
        Set(abilityVerbs, "verbs", new List<Verb> { abilityVerb });
        Set(ability, "verbTracker", abilityVerbs);

        CompEquippableAbility original = new()
        {
            props = new CompProperties_EquippableAbility { abilityDef = definition }
        };
        ThingWithComps originalWeapon = (ThingWithComps)RuntimeHelpers.GetUninitializedObject(typeof(ThingWithComps));
        originalWeapon.def = new ThingDef { defName = name + "Weapon", category = ThingCategory.Item };
        original.parent = originalWeapon;
        Set(original, "ability", ability);
        Set(original.verbTracker, "verbs", new List<Verb> { NewVerb("equipment-verb", original.verbTracker) });
        string directory = Environment.GetEnvironmentVariable("RW_VALIDATION_OUTPUT")
            ?? Path.Combine(Path.GetTempPath(), "opencode", "equippable-ability-roundtrip");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, name + ".xml");
        Scribe.mode = LoadSaveMode.Inactive;
        Scribe.saver = new ScribeSaver();
        Scribe.saver.InitSaving(path, "comp");
        entries = 0;
        original.PostExposeData();
        Check(entries == 1, name + " Saving enters the real method once");
        Scribe.saver.FinalizeSaving();
        XmlDocument document = new();
        document.Load(path);
        Check(document.DocumentElement!.SelectNodes("ability")!.Count == 1
            && document.DocumentElement.SelectNodes("verbTracker")!.Count == 1,
            name + " writes each ability/equipment tracker node exactly once");

        CompEquippableAbility loaded = new() { props = original.props, parent = originalWeapon };
        Scribe.loader = new ScribeLoader();
        Scribe.loader.curXmlParent = document.DocumentElement;
        Scribe.mode = LoadSaveMode.LoadingVars;
        entries = 0;
        loaded.PostExposeData();
        Check(entries == 1, name + " LoadingVars enters the real method once");
        Ability restored = loaded.AbilityForReading;
        Check(restored != null && restored.GetType() == ability.GetType() && restored.def == definition
            && restored.Id == ability.Id, name + " real deep load preserves class, def and ID");
        Check(restored!.CooldownTicksTotal == 600 && restored.RemainingCharges == 2
            && restored.lastCastTick == 88, name + " preserves cooldown, charges and last cast state");
        Check(loaded.verbTracker.directOwner == loaded && loaded.verbTracker.AllVerbs.Count == 1
            && loaded.verbTracker.AllVerbs[0].loadID == "equipment-verb", name + " restores the equipment tracker and verb list");
        Check(restored.VerbTracker.directOwner == restored && restored.VerbTracker.AllVerbs.Count == 1
            && restored.VerbTracker.AllVerbs[0].loadID == abilityVerbId, name + " restores the ability tracker and verb list");
        Scribe.mode = LoadSaveMode.ResolvingCrossRefs;
        restored.VerbTracker.ExposeData();
        Check(restored.verb.loadID == abilityVerbId && restored.verb.verbProps == definition.verbProperties,
            name + " real cross-reference phase restores verb properties");

        Pawn holder = (Pawn)RuntimeHelpers.GetUninitializedObject(typeof(Pawn));
        holder.equipment = new Pawn_EquipmentTracker(holder);
        ThingWithComps weapon = (ThingWithComps)RuntimeHelpers.GetUninitializedObject(typeof(ThingWithComps));
        weapon.holdingOwner = new ThingOwner<ThingWithComps>(holder.equipment);
        loaded.parent = weapon;
        Scribe.mode = LoadSaveMode.PostLoadInit;
        entries = 0;
        loaded.PostExposeData();
        Check(entries == 1 && ReferenceEquals(loaded.AbilityForReading, restored), name + " PostLoadInit preserves the loaded ability instance");
        Check(restored.pawn == holder && restored.verb.caster == holder
            && restored.VerbTracker.AllVerbs.Count == 1, name + " rebinds holder/caster without duplicating verbs");
        Scribe.mode = LoadSaveMode.Inactive;
        Console.WriteLine("GREEN " + name + " real Scribe save/load round-trip completed");
    }

    private static Verb NewVerb(string id, VerbTracker owner) => new Verb_Shoot
    {
        loadID = id,
        verbTracker = owner,
        verbProps = new VerbProperties { isPrimary = true }
    };

    private static void Set(object owner, string field, object value) =>
        (AccessTools.Field(owner.GetType(), field) ?? throw new MissingFieldException(field)).SetValue(owner, value);

    private static void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException(label);
        checks++;
        Console.WriteLine("GREEN " + label);
    }

    private static void Guard()
    {
        if (++entries >= 3)
            throw new InvalidOperationException("PostExposeData reentered its own patched override through a virtual base call.");
    }
}
