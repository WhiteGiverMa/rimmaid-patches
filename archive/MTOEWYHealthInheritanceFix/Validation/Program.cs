using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Xml;
using HarmonyLib;
using MiliraTheOneEntwinedWithYou.Data;
using MiliraTheOneEntwinedWithYou.Transfer;
using Mono.Cecil;
using Mono.Cecil.Cil;
using RimWorld;
using Verse;
using WhiteGiver.MTOEWYHealthInheritanceFix;

internal static class Entry
{
    private static readonly string GameRoot = Environment.GetEnvironmentVariable("RIMWORLD_ROOT") ?? @"A:\SteamLibrary\steamapps\common\RimWorld";
    private static readonly string HarmonyDirectory = Environment.GetEnvironmentVariable("HARMONY_DIRECTORY") ?? @"A:\SteamLibrary\steamapps\workshop\content\294100\2009463077\1.7\Assemblies";
    private static readonly string TargetDll = Environment.GetEnvironmentVariable("MTOEWY_DLL") ?? @"A:\SteamLibrary\steamapps\workshop\content\294100\3814027507\Assemblies\MiliraTheOneEntwinedWithYou.dll";

    private static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--help")
        {
            Console.WriteLine("Usage: Validation bind|red|green QA_DIRECTORY");
            Console.WriteLine("Only bind writes QA copies; no game process, save, Workshop file or installed assembly is changed.");
            return 0;
        }
        if (args.Length != 2 || args[0] is not ("bind" or "red" or "green"))
        {
            Console.Error.WriteLine("Invalid arguments; use --help.");
            return 1;
        }
        string bound = Path.Combine(Path.GetFullPath(args[1]), "bound");
        string targetDirectory = Path.GetDirectoryName(TargetDll)!;
        AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            foreach (string directory in new[] { bound, GameRoot, HarmonyDirectory, targetDirectory })
            {
                string candidate = Path.Combine(directory, name.Name + ".dll");
                if (File.Exists(candidate)) return context.LoadFromAssemblyPath(candidate);
            }
            return null;
        };
        try
        {
            if (args[0] == "bind") Bind(bound, targetDirectory);
            else Assembly.GetExecutingAssembly().GetType("Driver", true)!.GetMethod("Run")!.Invoke(null, new object[] { args[0] });
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static void Bind(string bound, string targetDirectory)
    {
        if (bound.StartsWith(GameRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || bound.StartsWith(targetDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("QA output must be outside the installed game and Workshop mod.");
        Directory.CreateDirectory(bound);
        using var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(GameRoot);
        resolver.AddSearchDirectory(HarmonyDirectory);
        resolver.AddSearchDirectory(targetDirectory);
        using var assembly = AssemblyDefinition.ReadAssembly(Path.Combine(GameRoot, "Assembly-CSharp.dll"),
            new ReaderParameters { AssemblyResolver = resolver });
        foreach (AssemblyNameReference reference in assembly.MainModule.AssemblyReferences)
        {
            if (!reference.Name.StartsWith("UnityEngine", StringComparison.Ordinal)) continue;
            reference.Name = "UnityStubs";
            reference.Version = new Version(1, 0, 0, 0);
        }
        foreach (string name in new[] { "Verse.ModsConfig", "Verse.ModLister", "Verse.BaseContent" })
        {
            MethodDefinition initializer = assembly.MainModule.GetType(name).Methods.Single(method => method.Name == ".cctor");
            initializer.Body.Instructions.Clear();
            initializer.Body.ExceptionHandlers.Clear();
            initializer.Body.Variables.Clear();
            initializer.Body.GetILProcessor().Emit(OpCodes.Ret);
        }
        assembly.Write(Path.Combine(bound, "Assembly-CSharp.dll"));
        using (var target = AssemblyDefinition.ReadAssembly(TargetDll, new ReaderParameters { AssemblyResolver = resolver }))
        {
            foreach (AssemblyNameReference reference in target.MainModule.AssemblyReferences)
            {
                if (!reference.Name.StartsWith("UnityEngine", StringComparison.Ordinal)) continue;
                reference.Name = "UnityStubs";
                reference.Version = new Version(1, 0, 0, 0);
            }
            target.Write(Path.Combine(bound, Path.GetFileName(TargetDll)));
        }
        Console.WriteLine("PASS: QA-only Unity reference binding and configuration/art isolation; real transfer, Scribe, Hediff and body-part method bodies remain unchanged.");
    }
}

public static class Driver
{
    private const BindingFlags InstanceFields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags StaticMethods = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly Dictionary<string, HediffDef> Defs = new(StringComparer.Ordinal);
    private static readonly List<string> Messages = new();
    private static readonly string[] RequiredNames =
    {
        "ValidationBionicArm", "ValidationBionicLeg", "ValidationPairedOrgan", "ValidationLevelImplant",
        "DMS_MechlinkOverload", "DMST_MagierSigil", "Cinder_ExtraArmourPlate", "ValidationCustomPsylink"
    };
    private static Type codec = null!;
    private static MethodInfo capture = null!;
    private static MethodInfo restore = null!;
    private static int dirtyCacheCalls;
    private static int capacityDirtyCalls;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Run(string mode)
    {
        var qa = new Harmony("validation.mtoewy.healthinheritance.boundaries");
        Hook(qa, typeof(Log), nameof(Log.Message), nameof(LogMessage), new[] { typeof(string) });
        Hook(qa, typeof(Log), nameof(Log.Warning), nameof(LogWarning), new[] { typeof(string) });
        Hook(qa, typeof(Log), nameof(Log.Error), nameof(LogError), new[] { typeof(string) });
        Hook(qa, typeof(DefOfHelper), nameof(DefOfHelper.EnsureInitializedInCtor), nameof(Skip));
        Hook(qa, typeof(Pawn_HealthTracker), "CheckForStateChange", nameof(Skip));
        Hook(qa, typeof(HediffSet), nameof(HediffSet.DirtyCache), nameof(DirtyCache));
        Hook(qa, typeof(HediffSet), nameof(HediffSet.GetPartHealth), nameof(PartHealth));
        Hook(qa, typeof(PawnCapacitiesHandler), nameof(PawnCapacitiesHandler.Notify_CapacityLevelsDirty), nameof(CapacityDirty));
        Hook(qa, typeof(Hediff_Psylink), nameof(Hediff_Psylink.PostAdd), nameof(Skip));
        qa.Patch(AccessTools.PropertyGetter(typeof(GenTypes), "AllActiveAssemblies"),
            prefix: new HarmonyMethod(typeof(Driver), nameof(ActiveAssemblies)));

        Type neuralType = typeof(IdealTransferNeural);
        codec = neuralType.Assembly.GetType("MiliraTheOneEntwinedWithYou.Transfer.IdealTransferCodec", throwOnError: true)!;
        capture = AccessTools.Method(neuralType, "Capture", new[] { typeof(Pawn) });
        restore = AccessTools.Method(neuralType, "Restore", new[] { typeof(Pawn) });
        Setup();

        if (mode == "green")
        {
            HealthInheritanceFix.Apply();
            MethodBase[] patched = Harmony.GetAllPatchedMethods().Where(method => Harmony.GetPatchInfo(method)?.Owners.Contains(HealthInheritanceFix.HarmonyId) == true).ToArray();
            Check(patched.Length == 4, $"Expected four registered targets, found {patched.Length}.");
            Check(patched.Contains(capture) && patched.Contains(restore), "Neural capture/restore were not registered.");
            Check(patched.Contains(AccessTools.Method(neuralType, "ExposeData", Type.EmptyTypes)), "Neural ExposeData was not registered.");
            Check(patched.Contains(AccessTools.Method(codec, "Project", new[] { typeof(XmlDocument) })), "Codec Project was not registered.");
            Check(Messages.Any(message => message.Contains("Patched neural capture", StringComparison.Ordinal)), "Registration did not report success.");
        }

        Pawn source = NewPawn(mirrored: false);
        PopulateSource(source, includeStandard: true);
        Pawn target = NewPawn(mirrored: true);
        RoundTripResult result = RoundTrip(source, target);
        CheckStandardRestored(target);

        if (mode == "red")
        {
            Check(result.Loaded.Neural.PsylinkLevel == 4 && result.Loaded.Neural.Mechlink, "Upstream did not retain its standard scalar neural fields.");
            Check(!result.Payload.Contains("hediffs", StringComparison.Ordinal), "Unexpected upstream hediff sidecar; RED baseline is not unpatched.");
            Check(target.health.hediffSet.hediffs.Count == 2, $"RED target should contain only PsychicAmplifier/MechlinkImplant, found {target.health.hediffSet.hediffs.Count}.");
            foreach (string name in RequiredNames)
                Check(!Has(target, name), $"RED baseline unexpectedly restored {name}.");
            Check(!Messages.Any(message => message.StartsWith("ERROR:", StringComparison.Ordinal)), "Unexpected game error: " + string.Join("\n", Messages));
            Console.WriteLine("RED: real upstream Capture/Codec/Wrap/Unwrap/Load/Restore preserved psylink + mechlink but lost bionics, paired implants, level implant and persistent states.");
            return;
        }

        VerifyGreenPayload(result.Payload);
        VerifyRestored(target);
        Check(dirtyCacheCalls > 0 && capacityDirtyCalls > 0, "Restoration did not reach cache/capacity invalidation boundaries.");

        Pawn persistentOnlySource = NewPawn(mirrored: false);
        Add(persistentOnlySource, "DMS_MechlinkOverload", null, 0.8f);
        Pawn persistentOnlyTarget = NewPawn(mirrored: true);
        RoundTripResult persistentOnly = RoundTrip(persistentOnlySource, persistentOnlyTarget);
        Check(persistentOnly.Loaded.Neural.PsylinkLevel == 0 && !persistentOnly.Loaded.Neural.Mechlink, "Early-return fixture unexpectedly has standard neural state.");
        Check(persistentOnlyTarget.health.hediffSet.hediffs.Count == 1 && Has(persistentOnlyTarget, "DMS_MechlinkOverload"),
            "Restore postfix did not run through the upstream zero-psylink/no-mechlink early return.");
        Check(!Messages.Any(message => message.StartsWith("ERROR:", StringComparison.Ordinal)), "Unexpected game error: " + string.Join("\n", Messages));
        Console.WriteLine("GREEN: full real transfer chain passed Parse and restored four bionic limbs, left/right paired organs, level + custom psylink implants, persistent states, PsychicAmplifier and MechlinkImplant.");
        Console.WriteLine("GREEN: mirrored body indices exercised label fallback; same-part level implant survived two-phase restore; generated child MissingPart entries were not fresh; zero-standard early return still restored persistent state.");
        Console.WriteLine("BOUNDARIES: only configuration/art static initialization, rendering/cache side effects, health state transitions and psylink ability-grant PostAdd were isolated; no live game session was used.");
    }

    private static RoundTripResult RoundTrip(Pawn source, Pawn target)
    {
        var neural = (IdealTransferNeural)capture.Invoke(null, new object[] { source })!;
        var record = new IdealTransferRecord
        {
            Id = "00000000000000000000000000000001",
            CreatedUtc = "2026-11-06T00:00:00.0000000Z",
            SourceTick = 123,
            Progress = new IdealTrackerState(),
            Neural = neural
        };
        string payload = (string)CodecMethod("Capture", typeof(IdealTransferRecord)).Invoke(null, new object[] { record })!;
        CodecMethod("Parse", typeof(string)).Invoke(null, new object[] { payload });
        string wrapped = (string)CodecMethod("Wrap", typeof(string)).Invoke(null, new object[] { payload })!;
        var unwrapped = (XmlDocument)CodecMethod("Unwrap", typeof(string)).Invoke(null, new object[] { wrapped })!;
        var loaded = (IdealTransferRecord)CodecMethod("Load", typeof(XmlDocument)).Invoke(null, new object[] { unwrapped })!;
        Check(loaded.Neural != null, "Loaded record has no neural payload.");
        restore.Invoke(loaded.Neural, new object[] { target });
        return new RoundTripResult(payload, loaded);
    }

    private static MethodInfo CodecMethod(string name, Type parameter) =>
        AccessTools.Method(codec, name, new[] { parameter }) ?? throw new MissingMethodException(codec.FullName, name);

    private static void VerifyGreenPayload(string payload)
    {
        Check(payload.Contains("<hediffs>", StringComparison.Ordinal), "GREEN payload has no hediffs sidecar.");
        foreach (string name in RequiredNames)
            Check(payload.Contains($"<defName>{name}</defName>", StringComparison.Ordinal), $"GREEN payload omitted {name}.");
        foreach (string name in new[] { "PsychicAmplifier", "MechlinkImplant", "ValidationAcute" })
            Check(!payload.Contains($"<defName>{name}</defName>", StringComparison.Ordinal), $"Sidecar should exclude upstream-owned/filtered {name}.");
        var document = (XmlDocument)CodecMethod("Parse", typeof(string)).Invoke(null, new object[] { payload })!;
        XmlElement[] entries = document.SelectNodes("/record/neural/hediffs/li")!.Cast<XmlElement>().ToArray();
        Check(entries.Length == 11, $"Expected eleven captured persistent entries, found {entries.Length}.");
        string[] expected = { "defName", "bodyDefName", "partIndex", "partDefName", "partCustomLabel", "severity", "level", "visible" };
        foreach (XmlElement entry in entries)
        {
            Check(entry.Attributes.Count == 0, "TransferHediffEntry emitted an XML attribute.");
            Check(entry.ChildNodes.Cast<XmlNode>().Select(node => node.Name).SequenceEqual(expected), $"Entry fields differ for {entry["defName"]?.InnerText}.");
            Check(entry.ChildNodes.Cast<XmlNode>().All(node => node is XmlElement element && element.Attributes.Count == 0), "Scalar entry emitted an XML attribute.");
        }
        Console.WriteLine("GREEN XML: eleven pure-scalar entries, exact eight-field whitelist, no Class/IsNull attributes under hediffs; real IdealTransferCodec.Parse accepted Capture, Wrap and Unwrap payloads.");
    }

    private static void VerifyRestored(Pawn target)
    {
        foreach (string label in new[] { "left arm", "right arm" })
            Check(Has(target, "ValidationBionicArm", label) && Get(target, "ValidationBionicArm", label) is Hediff_AddedPart, $"Missing bionic {label}.");
        foreach (string label in new[] { "left leg", "right leg" })
            Check(Has(target, "ValidationBionicLeg", label) && Get(target, "ValidationBionicLeg", label) is Hediff_AddedPart, $"Missing bionic {label}.");
        Check(target.health.hediffSet.hediffs.Count(hediff => hediff.def == Defs["ValidationPairedOrgan"]) == 2, "Paired organs collapsed or duplicated.");
        Check(Nearly(Get(target, "ValidationPairedOrgan", "left lung").Severity, 0.6f), "Left paired organ severity/side changed.");
        Check(Nearly(Get(target, "ValidationPairedOrgan", "right lung").Severity, 0.7f), "Right paired organ severity/side changed.");
        Hediff level = Get(target, "ValidationLevelImplant", "left arm");
        Check(level is Hediff_Level { level: 4 } && Nearly(level.Severity, 4f), "Level implant or same-part two-phase restore failed.");
        Hediff customPsylink = Get(target, "ValidationCustomPsylink", "brain");
        Check(customPsylink is Hediff_Psylink && ((Hediff_Level)customPsylink).level == 2 && Nearly(customPsylink.Severity, 2f), "Non-standard psylink level changed.");
        Check(Nearly(Get(target, "DMS_MechlinkOverload").Severity, 0.8f), "Persistent DMS state missing.");
        Check(Nearly(Get(target, "DMST_MagierSigil").Severity, 0.9f), "Persistent DMST state missing.");
        Check(Nearly(Get(target, "Cinder_ExtraArmourPlate").Severity, 1.1f), "Persistent Cinder state missing.");
        Check(!Has(target, "ValidationAcute"), "Acute bad state was incorrectly captured.");
        foreach (string parentLabel in new[] { "left arm", "right arm", "left leg", "right leg" })
        {
            BodyPartRecord parent = Part(target, parentLabel);
            foreach (BodyPartRecord child in parent.parts)
            {
                var missing = target.health.hediffSet.hediffs.OfType<Hediff_MissingPart>().SingleOrDefault(hediff => hediff.Part == child);
                Check(missing != null && !missing.IsFresh, $"Added-part child {child.customLabel} remained fresh or was not represented.");
            }
        }
    }

    private static void CheckStandardRestored(Pawn target)
    {
        Hediff psylink = Get(target, "PsychicAmplifier", "brain");
        Check(psylink is Hediff_Psylink && ((Hediff_Level)psylink).level == 4 && Nearly(psylink.Severity, 4f), "Original PsychicAmplifier restore changed.");
        Check(Has(target, "MechlinkImplant", "brain"), "Original MechlinkImplant restore changed.");
    }

    private static void Setup()
    {
        var game = (Game)RuntimeHelpers.GetUninitializedObject(typeof(Game));
        Current.Game = game;
        AccessTools.Field(typeof(Prefs), "data").SetValue(null, RuntimeHelpers.GetUninitializedObject(typeof(PrefsData)));
        AccessTools.Field(typeof(Game), "uniqueIDsManager").SetValue(game, new UniqueIDsManager());
        AccessTools.Field(typeof(Game), "tickManager").SetValue(game, new TickManager());
        DefDatabase<HediffDef>.Clear();
        Defs.Clear();
        Define("MissingBodyPart", typeof(Hediff_MissingPart), bad: true);
        Define("SurgicalCut", typeof(Hediff_Injury), bad: true);
        HediffDefOf.MissingBodyPart = Defs["MissingBodyPart"];
        HediffDefOf.SurgicalCut = Defs["SurgicalCut"];
        BodyPartTagDefOf.ConsciousnessSource = new BodyPartTagDef { defName = "ValidationConsciousnessSource" };
        Define("PsychicAmplifier", typeof(Hediff_Psylink)).maxSeverity = 6f;
        Define("MechlinkImplant", typeof(Hediff_Implant));
        Define("ValidationBionicArm", typeof(Hediff_AddedPart));
        Define("ValidationBionicLeg", typeof(Hediff_AddedPart));
        Define("ValidationPairedOrgan", typeof(Hediff_Implant));
        Define("ValidationLevelImplant", typeof(Hediff_Level));
        Define("DMS_MechlinkOverload", typeof(Hediff));
        Define("DMST_MagierSigil", typeof(Hediff));
        Define("Cinder_ExtraArmourPlate", typeof(Hediff));
        Define("ValidationCustomPsylink", typeof(Hediff_Psylink));
        Define("ValidationAcute", typeof(Hediff), bad: true);
    }

    private static HediffDef Define(string name, Type hediffClass, bool bad = false)
    {
        var def = new HediffDef { defName = name, hediffClass = hediffClass, isBad = bad };
        Defs.Add(name, def);
        DefDatabase<HediffDef>.Add(def);
        return def;
    }

    private static void PopulateSource(Pawn pawn, bool includeStandard)
    {
        if (includeStandard)
        {
            Add(pawn, "PsychicAmplifier", Part(pawn, "brain"), 4f, 4);
            Add(pawn, "MechlinkImplant", Part(pawn, "brain"), 1f);
        }
        foreach (string label in new[] { "left arm", "right arm" }) Add(pawn, "ValidationBionicArm", Part(pawn, label), 1f);
        foreach (string label in new[] { "left leg", "right leg" }) Add(pawn, "ValidationBionicLeg", Part(pawn, label), 1f);
        Add(pawn, "ValidationPairedOrgan", Part(pawn, "left lung"), 0.6f);
        Add(pawn, "ValidationPairedOrgan", Part(pawn, "right lung"), 0.7f);
        Add(pawn, "ValidationLevelImplant", Part(pawn, "left arm"), 4f, 4);
        Add(pawn, "DMS_MechlinkOverload", null, 0.8f);
        Add(pawn, "DMST_MagierSigil", null, 0.9f);
        Add(pawn, "Cinder_ExtraArmourPlate", null, 1.1f);
        Add(pawn, "ValidationCustomPsylink", Part(pawn, "brain"), 2f, 2);
        Add(pawn, "ValidationAcute", Part(pawn, "torso"), 1f);
    }

    private static Hediff Add(Pawn pawn, string name, BodyPartRecord? part, float severity, int level = -1)
    {
        Hediff hediff = HediffMaker.MakeHediff(Defs[name], pawn, part);
        if (hediff is Hediff_Level levelHediff && level > 0) levelHediff.level = level;
        if (hediff is Hediff_Psylink psylink) psylink.suppressPostAddLetter = true;
        hediff.Severity = severity;
        pawn.health.AddHediff(hediff, part, null, null);
        if (hediff is Hediff_Level addedLevel && level > 0) addedLevel.level = level;
        hediff.Severity = severity;
        return hediff;
    }

    private static Pawn NewPawn(bool mirrored)
    {
        BodyPartDef torsoDef = BodyPartDef("Torso");
        BodyPartDef headDef = BodyPartDef("Head");
        BodyPartDef brainDef = BodyPartDef("Brain");
        brainDef.tags.Add(BodyPartTagDefOf.ConsciousnessSource);
        BodyPartDef armDef = BodyPartDef("Arm");
        BodyPartDef handDef = BodyPartDef("Hand");
        BodyPartDef lungDef = BodyPartDef("Lung");
        BodyPartDef legDef = BodyPartDef("Leg");
        BodyPartDef footDef = BodyPartDef("Foot");

        BodyPartRecord Arm(string side) => Record(armDef, side + " arm", Record(handDef, side + " hand"));
        BodyPartRecord Lung(string side) => Record(lungDef, side + " lung");
        BodyPartRecord Leg(string side) => Record(legDef, side + " leg", Record(footDef, side + " foot"));
        string first = mirrored ? "right" : "left";
        string second = mirrored ? "left" : "right";
        var body = new BodyDef
        {
            defName = mirrored ? "ValidationBodyMirrored" : "ValidationBody",
            corePart = Record(torsoDef, "torso",
                Record(headDef, "head", Record(brainDef, "brain")),
                Arm(first), Arm(second), Lung(first), Lung(second), Leg(first), Leg(second))
        };
        body.ResolveReferences();
        var pawn = (Pawn)RuntimeHelpers.GetUninitializedObject(typeof(Pawn));
        pawn.def = new ThingDef
        {
            defName = "ValidationPawn",
            category = ThingCategory.Pawn,
            race = new RaceProperties { body = body }
        };
        pawn.health = new Pawn_HealthTracker(pawn);
        return pawn;
    }

    private static BodyPartDef BodyPartDef(string name) => new() { defName = name, label = name, destroyableByDamage = false };

    private static BodyPartRecord Record(BodyPartDef def, string label, params BodyPartRecord[] children)
    {
        var record = new BodyPartRecord { def = def, customLabel = label, coverage = 0.1f };
        record.parts.AddRange(children);
        return record;
    }

    private static BodyPartRecord Part(Pawn pawn, string label) => pawn.RaceProps.body.AllParts.Single(part => part.customLabel == label);
    private static bool Has(Pawn pawn, string name, string? part = null) => pawn.health.hediffSet.hediffs.Any(hediff => hediff.def == Defs[name] && (part == null || hediff.Part?.customLabel == part));
    private static Hediff Get(Pawn pawn, string name, string? part = null) => pawn.health.hediffSet.hediffs.Single(hediff => hediff.def == Defs[name] && (part == null || hediff.Part?.customLabel == part));
    private static bool Nearly(float left, float right) => Math.Abs(left - right) < 0.0001f;

    public static bool Skip() => false;
    public static bool ActiveAssemblies(ref IEnumerable<Assembly> __result)
    {
        __result = new[] { typeof(GenTypes).Assembly, typeof(IdealTransferRecord).Assembly, typeof(TransferHediffEntry).Assembly };
        return false;
    }
    public static bool DirtyCache() { dirtyCacheCalls++; return false; }
    public static bool CapacityDirty() { capacityDirtyCalls++; return false; }
    public static bool PartHealth(ref float __result) { __result = 1f; return false; }
    public static bool LogMessage(string text) { Messages.Add("INFO: " + text); return false; }
    public static bool LogWarning(string text) { Messages.Add("WARN: " + text); return false; }
    public static bool LogError(string text) { Messages.Add("ERROR: " + text); return false; }

    private static void Hook(Harmony harmony, Type type, string name, string callback, Type[]? args = null) =>
        harmony.Patch(AccessTools.Method(type, name, args), prefix: new HarmonyMethod(typeof(Driver), callback));

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message + "\nLogs:\n" + string.Join("\n", Messages));
    }

    private sealed record RoundTripResult(string Payload, IdealTransferRecord Loaded);
}
