using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using HarmonyLib;
using Mono.Cecil;
using RimWorld;
using UnityEngine;
using Verse;

internal static class Program
{
    private static readonly string Game = Environment.GetEnvironmentVariable("RIMWORLD_ROOT") ?? @"A:\SteamLibrary\steamapps\common\RimWorld";
    private static readonly string HarmonyDirectory = Environment.GetEnvironmentVariable("HARMONY_DIRECTORY") ?? @"A:\SteamLibrary\steamapps\workshop\content\294100\2009463077\1.7\Assemblies";
    private static readonly string StationDirectory = Environment.GetEnvironmentVariable("WEAPONSTATION_DIRECTORY") ?? @"A:\SteamLibrary\steamapps\workshop\content\294100\3807251471";
    private static readonly string CePath = Environment.GetEnvironmentVariable("CE_ASSEMBLY") ?? Path.Combine(Game, @"Mods\CombatExtendedLocal\Assemblies\CombatExtended.dll");
    private static readonly string BoundDirectory = Path.Combine(AppContext.BaseDirectory, "bound");
    private static readonly string PatchPath = Environment.GetEnvironmentVariable("RECOIL_FIX_ASSEMBLY") ?? Path.Combine(AppContext.BaseDirectory, "WeaponStationCERecoilFix.dll");
    private delegate void RecoilDelegate(ThingDef weaponDef, Verb shootVerb, out Vector3 drawOffset, out float angleOffset, float aimAngle, bool handheld);

    private static int Main(string[] args)
    {
        AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            foreach (string directory in new[] { BoundDirectory, Game, HarmonyDirectory })
            {
                string path = Path.Combine(directory, name.Name + ".dll");
                if (File.Exists(path)) return context.LoadFromAssemblyPath(path);
            }
            return null;
        };
        try
        {
            if (args.Length == 1 && args[0] == "--help")
            {
                Console.WriteLine("Usage: Validation bind|red|green|absent");
                return 0;
            }
            if (args.Length != 1) throw new ArgumentException("Usage: Validation bind|red|green|absent");
            if (args[0] == "bind") Bind();
            else if (args[0] == "red") Red();
            else if (args[0] == "green") Green();
            else if (args[0] == "absent") Absent();
            else throw new ArgumentException("Unknown QA mode: " + args[0]);
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static void Bind()
    {
        Directory.CreateDirectory(BoundDirectory);
        using var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(Game);
        resolver.AddSearchDirectory(HarmonyDirectory);
        foreach (string path in new[]
        {
            CePath,
            Path.Combine(StationDirectory, @"Assemblies\WeaponStation.dll"),
            Path.Combine(StationDirectory, @"CombatExtended\Assemblies\WeaponStation_CE.dll")
        })
        {
            using var assembly = AssemblyDefinition.ReadAssembly(path, new ReaderParameters { AssemblyResolver = resolver });
            foreach (AssemblyNameReference reference in assembly.MainModule.AssemblyReferences)
            {
                if (!reference.Name.StartsWith("UnityEngine", StringComparison.Ordinal)) continue;
                reference.Name = "UnityStubs";
                reference.Version = new Version(1, 0, 0, 0);
            }
            assembly.Write(Path.Combine(BoundDirectory, Path.GetFileName(path)));
            Console.WriteLine("QA-only Unity binding copy: " + path);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Red()
    {
        Assembly ce = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(BoundDirectory, "CombatExtended.dll"));
        MethodInfo recoil = ce.GetType("CombatExtended.CE_Utility", true)!.GetMethod("Recoil")!;
        var verb = new Verb_Shoot { verbProps = new VerbProperties { verbClass = typeof(Verb_Shoot), burstShotCount = 2, ticksBetweenBurstShots = 1 } };
        object[] values = { new ThingDef(), verb, Vector3.zero, 0f, 45f, false };
        try { recoil.Invoke(null, values); }
        catch (TargetInvocationException exception) when (exception.InnerException is InvalidCastException)
        {
            Console.WriteLine("RED: actual active CE Recoil rejects vanilla Verb_Shoot/VerbProperties with InvalidCastException.");
            Assembly station = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(BoundDirectory, "WeaponStation_CE.dll"));
            MethodInfo draw = station.GetType("WeaponStation_CE.Building_WeaponStationCE", true)!.GetMethod("DrawAt")!;
            int calls = PatchProcessor.GetOriginalInstructions(draw).Count(instruction => instruction.Calls(recoil));
            if (calls != 1) throw new InvalidOperationException("Expected exactly one original Recoil call; got " + calls);
            Console.WriteLine("RED: actual station DrawAt contains exactly one call to the failing CE method.");
            return;
        }
        throw new InvalidOperationException("The original InvalidCastException did not reproduce.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Absent()
    {
        Assembly patch = AssemblyLoadContext.Default.LoadFromAssemblyPath(PatchPath);
        Type mod = patch.GetType("Meidocho.WeaponStationCERecoilFix.WeaponStationCERecoilFixMod", true)!;
        Activator.CreateInstance(mod, new object?[] { null });
        if (Harmony.GetAllPatchedMethods().Any(method => Harmony.GetPatchInfo(method)?.Owners.Contains("meidocho.weaponstation.cerecoilfix") == true))
            throw new InvalidOperationException("Patch registered despite absent CE weapon station.");
        Console.WriteLine("GREEN: actual Mod constructor safely skips absent CE weapon station.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Green()
    {
        const string owner = "meidocho.weaponstation.cerecoilfix";
        Assembly ce = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(BoundDirectory, "CombatExtended.dll"));
        Assembly station = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(BoundDirectory, "WeaponStation_CE.dll"));
        Assembly patch = AssemblyLoadContext.Default.LoadFromAssemblyPath(PatchPath);
        Type fix = patch.GetType("Meidocho.WeaponStationCERecoilFix.RecoilFix", true)!;
        MethodInfo original = ce.GetType("CombatExtended.CE_Utility", true)!.GetMethod("Recoil")!;
        MethodInfo draw = station.GetType("WeaponStation_CE.Building_WeaponStationCE", true)!.GetMethod("DrawAt")!;
        MethodInfo replacement = fix.GetMethod("RecoilForStation")!;
        if (!original.GetParameters().Select(parameter => parameter.ParameterType).SequenceEqual(replacement.GetParameters().Select(parameter => parameter.ParameterType)))
            throw new InvalidOperationException("Wrapper parameter identities differ from actual CE Recoil.");
        Type mod = patch.GetType("Meidocho.WeaponStationCERecoilFix.WeaponStationCERecoilFixMod", true)!;
        Activator.CreateInstance(mod, new object?[] { null });
        if (Harmony.GetPatchInfo(draw)?.Transpilers.Count(p => p.owner == owner) != 1)
            throw new InvalidOperationException("The actual station DrawAt did not register exactly one transpiler.");
        if (Harmony.GetPatchInfo(original)?.Owners.Contains(owner) == true)
            throw new InvalidOperationException("The global CE Recoil was patched.");
        List<CodeInstruction> current = PatchProcessor.GetCurrentInstructions(draw);
        if (current.Count(instruction => instruction.Calls(replacement)) != 1 || current.Any(instruction => instruction.Calls(original)))
            throw new InvalidOperationException("Actual patched DrawAt did not replace only the original recoil call.");
        MethodInfo transpiler = fix.GetMethod("Transpiler", BindingFlags.NonPublic | BindingFlags.Static)!;
        List<CodeInstruction> before = PatchProcessor.GetOriginalInstructions(draw);
        var operands = before.Select(instruction => instruction.operand).ToArray();
        var opcodes = before.Select(instruction => instruction.opcode).ToArray();
        int callIndex = before.FindIndex(instruction => instruction.Calls(original));
        var generator = new DynamicMethod("labels", typeof(void), Type.EmptyTypes).GetILGenerator();
        Label label = generator.DefineLabel();
        before[callIndex].labels.Add(label);
        before[callIndex].blocks.Add(new ExceptionBlock(ExceptionBlockType.BeginExceptionBlock));
        var rewritten = ((IEnumerable<CodeInstruction>)transpiler.Invoke(null, new object[] { before })!).ToList();
        for (int index = 0; index < before.Count; index++)
        {
            object? expected = index == callIndex ? replacement : operands[index];
            if (!ReferenceEquals(before[index], rewritten[index]) || rewritten[index].opcode != opcodes[index] || !Equals(rewritten[index].operand, expected))
                throw new InvalidOperationException("Transpiler changed unrelated instructions or lost instruction identity.");
        }
        if (!rewritten[callIndex].labels.Contains(label) || rewritten[callIndex].blocks.Count != 1)
            throw new InvalidOperationException("Labels or exception blocks were lost.");
        foreach (int count in new[] { 0, 2 })
        {
            try
            {
                transpiler.Invoke(null, new object[] { Enumerable.Range(0, count).Select(_ => new CodeInstruction(OpCodes.Call, original)).ToList() });
                throw new InvalidOperationException("Unexpected call count was not rejected.");
            }
            catch (TargetInvocationException exception) when (exception.InnerException is InvalidOperationException) { }
        }
        var safe = (RecoilDelegate)replacement.CreateDelegate(typeof(RecoilDelegate));
        var vanilla = new Verb_Shoot { verbProps = new VerbProperties { verbClass = typeof(Verb_Shoot) } };
        Vector3 offset = new(7, 8, 9);
        float angle = 99;
        safe(new ThingDef(), vanilla, out offset, out angle, 45, false);
        if (offset != Vector3.zero || angle != 0) throw new InvalidOperationException("Vanilla verb did not return zero offsets.");
        safe(new ThingDef(), null!, out offset, out angle, 45, false);
        if (offset != Vector3.zero || angle != 0) throw new InvalidOperationException("Null verb did not return zero offsets.");
        var melee = new Verb_MeleeAttackDamage { verbProps = new VerbProperties { verbClass = typeof(Verb_MeleeAttackDamage) } };
        safe(new ThingDef(), melee, out offset, out angle, 45, false);
        if (offset != Vector3.zero || angle != 0) throw new InvalidOperationException("Melee verb did not return zero offsets.");
        Console.WriteLine("GREEN: actual Mod/Harmony3 registration; one call replaced; no global patch; vanilla/null/melee offsets zero; labels/blocks preserved; 0/2-call drift rejected.");

        Current.Game = (Game)RuntimeHelpers.GetUninitializedObject(typeof(Game));
        var ticks = (TickManager)RuntimeHelpers.GetUninitializedObject(typeof(TickManager));
        AccessTools.Field(typeof(Game), "tickManager").SetValue(Current.Game, ticks);
        var originalCall = (RecoilDelegate)original.CreateDelegate(typeof(RecoilDelegate));
        int comparisons = 0;
        int nonzero = 0;
        foreach (bool ceVerb in new[] { false, true })
        {
            var props = (VerbProperties)Activator.CreateInstance(ce.GetType("CombatExtended.VerbPropertiesCE", true)!)!;
            props.verbClass = ceVerb ? ce.GetType("CombatExtended.Verb_ShootCE", true)! : typeof(Verb_Shoot);
            props.burstShotCount = 2;
            props.ticksBetweenBurstShots = 30;
            props.defaultProjectile = new ThingDef { projectile = (ProjectileProperties)Activator.CreateInstance(ce.GetType("CombatExtended.ProjectilePropertiesCE", true)!)! };
            AccessTools.Field(props.GetType(), "recoilAmount").SetValue(props, 2f);
            var verb = (Verb)Activator.CreateInstance(props.verbClass)!;
            verb.verbProps = props;
            AccessTools.Field(typeof(Verb), "verbTracker").SetValue(verb, RuntimeHelpers.GetUninitializedObject(typeof(VerbTracker)));
            AccessTools.Field(typeof(Verb), "lastShotTick").SetValue(verb, 90);
            foreach (int tick in new[] { 90, 91, 100, 119, 120, 150 })
            foreach (float aim in new[] { 0f, 45f, 90f, 270f })
            {
                ticks.DebugSetTicksGame(tick);
                var def = new ThingDef();
                originalCall(def, verb, out Vector3 expectedOffset, out float expectedAngle, aim, false);
                safe(def, verb, out offset, out angle, aim, false);
                if (!offset.Equals(expectedOffset) || angle != expectedAngle) throw new InvalidOperationException("A valid CE animation changed.");
                if (offset.sqrMagnitude > 0 && angle != 0) nonzero++;
                comparisons++;
            }
        }
        if (nonzero == 0) throw new InvalidOperationException("All comparison offsets were zero; animation was not exercised.");
        Console.WriteLine($"GREEN: {comparisons} exact CE animation comparisons ({nonzero} nonzero), both CE verb and vanilla verb + CE properties; no shooting/definition mutation.");
    }

}
