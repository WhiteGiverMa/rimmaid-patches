using WhiteGiver.DMSSRUnifiedFix;
using DMSSRUnifiedFix.Validation;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace DMSSRUnifiedFix.Validation.ScourgeDeath;

internal static class Audit
{
    internal static void Run(string boundPath, string gameRoot, string workshop, string patchPath)
    {
        using DefaultAssemblyResolver resolver = new();
        resolver.AddSearchDirectory(gameRoot);
        resolver.AddSearchDirectory(Path.Combine(workshop, @"2009463077\1.7\Assemblies"));
        ReaderParameters parameters = new() { AssemblyResolver = resolver };
        using AssemblyDefinition bound = AssemblyDefinition.ReadAssembly(boundPath);
        using AssemblyDefinition installed = AssemblyDefinition.ReadAssembly(Path.Combine(gameRoot, "Assembly-CSharp.dll"));
        using AssemblyDefinition dmssr = AssemblyDefinition.ReadAssembly(Path.Combine(workshop, @"3835512735\1.7\Assemblies\DMSSR.dll"), parameters);
        using AssemblyDefinition fortified = AssemblyDefinition.ReadAssembly(Path.Combine(workshop, @"3498575851\1.7\Assemblies\Fortified.dll"));
        MethodDefinition config = bound.MainModule.GetType("Verse.ModsConfig").Methods.Single(method => method.Name == ".cctor");
        Require(config.Body.Instructions.All(instruction => instruction.OpCode == OpCodes.Nop || instruction.OpCode == OpCodes.Ret),
            "bound ModsConfig initializer is disabled before loading: no user-config IO");
        foreach ((string type, string[] methods) in new[]
        {
            ("Verse.Pawn", new[] { "Kill", "MakeCorpse" }),
            ("Verse.Pawn_HealthTracker", new[] { "AddHediff", "RemoveHediff", "CheckForStateChange", "ShouldBeDead", "SetDead", "Notify_Resurrected" }),
            ("Verse.DamageWorker_AddInjury", new[] { "FinalizeAndAddInjury" }),
            ("Verse.HediffSet", new[] { "AddDirect", "DirtyCache" }),
            ("Verse.Corpse", new[] { "SpawnSetup", "get_Bugged" }),
            ("Verse.GenSpawn", new[] { "Spawn", "CheckMoveItemsAside" }),
            ("Verse.GenPlace", new[] { "TryPlaceThing", "TryPlaceDirect", "SplitAndSpawnOneStackOnCell" }),
            ("RimWorld.ResurrectionUtility", new[] { "TryResurrect" }),
        })
        {
            foreach (MethodDefinition method in bound.MainModule.GetType(type).Methods.Where(method => methods.Contains(method.Name)))
            {
                MethodDefinition counterpart = installed.MainModule.GetType(type).Methods.Single(candidate => candidate.FullName == method.FullName);
                Require(Fingerprint(method) == Fingerprint(counterpart), "bound and installed IL match: " + method.FullName);
            }
        }
        MethodDefinition kill = bound.MainModule.GetType("Verse.Pawn").Methods.Single(method => method.Name == "Kill");
        Require(!kill.Body.ExceptionHandlers.Where(handler => handler.HandlerType == ExceptionHandlerType.Finally).Any(handler =>
            kill.Body.Instructions.Where(instruction => instruction.Offset >= handler.HandlerStart.Offset
                && (handler.HandlerEnd == null || instruction.Offset < handler.HandlerEnd.Offset))
                .Any(instruction => instruction.Operand is FieldReference { Name: "isBeingKilled" })),
            "Pawn.Kill has no finally cleanup for isBeingKilled (only enumerator disposal)");
        Require(kill.Body.Instructions.Any(instruction => instruction.OpCode == OpCodes.Stfld && instruction.Operand is FieldReference { Name: "isBeingKilled" }), "Pawn.Kill sets its flag in the body");
        Require(!kill.Body.Instructions.Any(instruction => instruction.OpCode == OpCodes.Ldfld && instruction.Operand is FieldReference { Name: "isBeingKilled" }), "Pawn.Kill never reads the flag as an entry guard");
        MethodDefinition removal = dmssr.MainModule.GetType("DMSSR.Content.Patch_ScourgeNextDeath").Methods.Single(method => method.Name == "Prefix");
        MethodDefinition mechKill = fortified.MainModule.GetType("Fortified.HumanlikeMech").Methods.Single(method => method.Name == "Kill");
        Require(Calls(removal, "Verse.Pawn_HealthTracker", "RemoveHediff") == 1, "Scourge prefix contains one unguarded removal call");
        Require(!removal.Body.Instructions.Any(instruction => instruction.Operand is FieldReference { Name: "isBeingKilled" }), "Scourge removal has no upstream death flag scope");
        Require(Calls(mechKill, "Verse.Pawn_HealthTracker", "RemoveHediff") == 1 && Calls(mechKill, "Verse.Pawn", "Kill") == 1,
            "Fortified override removes implants before one base Kill");
        int removalIndex = mechKill.Body.Instructions.ToList().FindIndex(instruction => instruction.Operand is MethodReference { Name: "RemoveHediff" });
        int baseIndex = mechKill.Body.Instructions.ToList().FindIndex(instruction => instruction.Operand is MethodReference { Name: "Kill" });
        Require(removalIndex < baseIndex, "Fortified removal precedes base Kill");
        Require(!mechKill.Body.Instructions.Any(instruction => instruction.Operand is FieldReference { Name: "isBeingKilled" }), "Fortified removal has no upstream death flag scope");
        List<TypeDefinition> killPatches = dmssr.MainModule.Types.Where(type => type.CustomAttributes.Any(attribute =>
            attribute.AttributeType.FullName == "HarmonyLib.HarmonyPatch" && attribute.ConstructorArguments.Any(argument => argument.Value is TypeReference { FullName: "Verse.Pawn" })
            && attribute.ConstructorArguments.Any(argument => Equals(argument.Value, "Kill")))).ToList();
        Require(killPatches.Count == 4, "DMSSR has exactly the four reviewed Pawn.Kill patch classes");
        foreach (TypeDefinition type in killPatches)
        {
            int removes = type.Methods.Where(method => method.HasBody).Sum(method => Calls(method, "Verse.Pawn_HealthTracker", "RemoveHediff"));
            Console.WriteLine($"AUDIT Pawn.Kill patch {type.FullName}: direct RemoveHediff calls={removes}");
        }
        Console.WriteLine("AUDIT Current game: " + File.ReadAllText(Path.Combine(gameRoot, "Version.txt")).Trim());
        if (File.Exists(patchPath))
        {
            using AssemblyDefinition patch = AssemblyDefinition.ReadAssembly(patchPath);
            Require(patch.Name.Version == new Version(1, 1, 4, 0), "sidecar assembly version 1.1.4.0");
            Require(!patch.MainModule.GetMemberReferences().Any(member => member.DeclaringType.FullName.StartsWith("Verse.Scribe_", StringComparison.Ordinal)), "sidecar has zero persistent Scribe_* references");
        }
    }

    private static int Calls(MethodDefinition method, string type, string name) => method.Body.Instructions.Count(instruction =>
        instruction.Operand is MethodReference reference && reference.DeclaringType.FullName == type && reference.Name == name);

    private static string Fingerprint(MethodDefinition method)
    {
        var indices = method.Body.Instructions.Select((instruction, index) => (instruction, index)).ToDictionary(pair => pair.instruction, pair => pair.index);
        string Operand(object? operand) => operand switch
        {
            Instruction target => "@" + indices[target],
            Instruction[] targets => string.Join(",", targets.Select(target => indices[target])),
            _ => operand?.ToString() ?? "",
        };
        return string.Join("\n", method.Body.Instructions.Select(instruction => instruction.OpCode + " " + Operand(instruction.Operand)))
            + string.Join("\n", method.Body.ExceptionHandlers.Select(handler =>
                $"{handler.HandlerType}:{indices[handler.TryStart]}:{indices[handler.HandlerStart]}:{handler.CatchType}"));
    }

    private static void Require(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException("AUDIT FAIL " + description);
        Console.WriteLine("AUDIT PASS " + description);
    }
}
