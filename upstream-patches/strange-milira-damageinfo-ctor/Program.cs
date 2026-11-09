using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

const string GameDir = @"A:\SteamLibrary\steamapps\common\RimWorld";
const string TargetType = "strangeMiliraWeapon.Verb_Shoot_BurnSelf";

if (args.Length < 2)
{
    Console.Error.WriteLine("usage: patch <in> <out> | verify <dll> | inspect <dll>");
    return 2;
}

if (args[0] == "patch")
{
    using var resolver = new UnityResolver();
    resolver.AddSearchDirectory(GameDir);
    resolver.AddSearchDirectory(Path.GetDirectoryName(Path.GetFullPath(args[1]))!);
    using var assembly = AssemblyDefinition.ReadAssembly(args[1], new ReaderParameters { AssemblyResolver = resolver, ReadingMode = ReadingMode.Immediate });
    var module = assembly.MainModule;
    var type = module.GetType(TargetType) ?? throw new Exception("Type not found: " + TargetType);
    var method = type.Methods.Single(m => m.Name == "TryCastShot" && m.Parameters.Count == 0);
    if (!method.HasBody) throw new Exception("TryCastShot has no body");

    var before = AllTypes(module.Types).SelectMany(t => t.Methods).Where(m => m.HasBody)
        .ToDictionary(m => m.FullName, m => m.Body.Instructions.Select(InsKey).ToList());

    var instructions = method.Body.Instructions;
    var calls = instructions.Where(i => i.OpCode == OpCodes.Newobj && i.Operand is MethodReference mr
        && mr.DeclaringType.FullName == "Verse.DamageInfo" && mr.Name == ".ctor").ToList();
    if (calls.Count != 1) throw new Exception($"Expected exactly one DamageInfo ctor call, found {calls.Count}");
    var ctor = (MethodReference)calls[0].Operand;
    if (ctor.Parameters.Count != 13) throw new Exception($"Expected 13 params, found {ctor.Parameters.Count}");
    var newobjIndex = instructions.IndexOf(calls[0]);

    var newCtor = new MethodReference(".ctor", module.TypeSystem.Void, ctor.DeclaringType) { HasThis = true };
    foreach (var p in ctor.Parameters) newCtor.Parameters.Add(new ParameterDefinition(p.ParameterType));
    newCtor.Parameters.Add(new ParameterDefinition(module.TypeSystem.Boolean));

    var il = method.Body.GetILProcessor();
    il.InsertBefore(calls[0], Instruction.Create(OpCodes.Ldc_I4_0));
    calls[0].Operand = newCtor;

    assembly.Write(args[2]);

    using var patched = AssemblyDefinition.ReadAssembly(args[2]);
    foreach (var m in AllTypes(patched.MainModule.Types).SelectMany(t => t.Methods).Where(m => m.HasBody))
    {
        var oldList = before[m.FullName];
        var newList = m.Body.Instructions.Select(InsKey).ToList();
        if (m.FullName == method.FullName)
        {
            if (newList.Count != oldList.Count + 1) throw new Exception("Target instruction count mismatch");
            var diffIndex = -1;
            for (var i = 0; i < oldList.Count; i++)
                if (oldList[i] != newList[i]) { diffIndex = i; break; }
            if (diffIndex < 0) diffIndex = oldList.Count;
            if (newList[diffIndex] != "ldc.i4.0|") throw new Exception("Inserted instruction is not ldc.i4.0: " + newList[diffIndex]);
            for (var i = 0; i < oldList.Count; i++)
            {
                var ni = i < diffIndex ? i : i + 1;
                if (i == newobjIndex)
                {
                    if (newList[ni] == oldList[i]) throw new Exception("DamageInfo ctor reference was not changed");
                    continue;
                }
                if (oldList[i] != newList[ni]) throw new Exception($"Unexpected IL change at {m.FullName}[{i}]: {oldList[i]} => {newList[ni]}");
            }
            var newCall = m.Body.Instructions.Single(i => i.OpCode == OpCodes.Newobj && i.Operand is MethodReference r
                && r.DeclaringType.FullName == "Verse.DamageInfo");
            if (((MethodReference)newCall.Operand).Parameters.Count != 14) throw new Exception("Patched ctor does not have 14 params");
            if (((MethodReference)newCall.Operand).Parameters[13].ParameterType.FullName != "System.Boolean") throw new Exception("14th param is not Boolean");
        }
        else if (!oldList.SequenceEqual(newList))
        {
            throw new Exception("Unexpected IL change in " + m.FullName);
        }
    }
    Console.WriteLine("PATCH PASS: one ldc.i4.0 inserted, DamageInfo ctor 13->14 params; all other IL preserved.");
}
else if (args[0] == "verify")
{
    AssemblyLoadContext.Default.Resolving += (_, name) =>
    {
        var file = name.Name!.StartsWith("UnityEngine", StringComparison.Ordinal) ? "UnityStubs.dll" : name.Name + ".dll";
        var path = Path.Combine(GameDir, file);
        return File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
    };
    var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(args[1]));
    var type = assembly.GetType(TargetType, true)!;
    var method = type.GetMethod("TryCastShot", BindingFlags.NonPublic | BindingFlags.Instance)!;
    try
    {
        RuntimeHelpers.PrepareMethod(method.MethodHandle);
        Console.WriteLine("JIT PASS: " + method.DeclaringType!.FullName + ".TryCastShot compiled against actual Assembly-CSharp.");
    }
    catch (Exception ex)
    {
        Console.WriteLine("JIT FAIL: " + ex.GetType().FullName + ": " + ex.Message);
        return 1;
    }
}
else if (args[0] == "inspect")
{
    using var assembly = AssemblyDefinition.ReadAssembly(args[1]);
    var type = assembly.MainModule.GetType(TargetType) ?? throw new Exception("Type not found: " + TargetType);
    var method = type.Methods.Single(m => m.Name == "TryCastShot" && m.Parameters.Count == 0);
    foreach (var instruction in method.Body.Instructions) Console.WriteLine(instruction);
}
else
{
    Console.Error.WriteLine("unknown command: " + args[0]);
    return 2;
}
return 0;

static string InsKey(Instruction instruction)
    => instruction.OpCode.Name + "|" + (instruction.Operand is Instruction ? "target" : instruction.Operand?.ToString() ?? "");

static IEnumerable<TypeDefinition> AllTypes(IEnumerable<TypeDefinition> types)
{
    foreach (var type in types)
    {
        yield return type;
        foreach (var nested in AllTypes(type.NestedTypes)) yield return nested;
    }
}

sealed class UnityResolver : DefaultAssemblyResolver
{
    public override AssemblyDefinition Resolve(AssemblyNameReference name)
        => name.Name.StartsWith("UnityEngine", StringComparison.Ordinal)
            ? AssemblyDefinition.ReadAssembly(Path.Combine(@"A:\SteamLibrary\steamapps\common\RimWorld", "UnityStubs.dll"))
            : base.Resolve(name);
}
