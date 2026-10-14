using Mono.Cecil;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

if (args[0] == "patch")
{
    using var resolver = new UnityResolver();
    resolver.AddSearchDirectory(@"A:\SteamLibrary\steamapps\common\RimWorld");
    resolver.AddSearchDirectory(Path.GetDirectoryName(Path.GetFullPath(args[1]))!);
    using var assembly = AssemblyDefinition.ReadAssembly(args[1], new ReaderParameters { AssemblyResolver = resolver, ReadingMode = ReadingMode.Immediate });
    var methods = AllTypes(assembly.MainModule.Types).SelectMany(t => t.Methods).Where(m => m.HasBody).ToArray();
    var before = methods.ToDictionary(m => m.FullName, m => string.Join("\n", m.Body.Instructions.Select(i => i.ToString())));
    var calls = methods.SelectMany(m => m.Body.Instructions).Where(i => i.Operand is MethodReference r && r.DeclaringType.FullName == "UnityEngine.Time" && r.Name == "get_unscaledTimeAsDouble").ToArray();
    if (calls.Length != 3) throw new Exception("Expected exactly three missing clock calls; upstream changed.");
    foreach (var reference in calls.Select(i => (MethodReference)i.Operand).Distinct())
    {
        if (reference.ReturnType.FullName != "System.Double" || reference.Parameters.Count != 0 || reference.HasThis)
            throw new Exception("Clock signature changed.");
        reference.Name = "get_realtimeSinceStartupAsDouble";
    }
    assembly.Write(args[2]);
    using var patched = AssemblyDefinition.ReadAssembly(args[2]);
    foreach (var method in AllTypes(patched.MainModule.Types).SelectMany(t => t.Methods).Where(m => m.HasBody))
        if (before[method.FullName].Replace("get_unscaledTimeAsDouble", "get_realtimeSinceStartupAsDouble", StringComparison.Ordinal) != string.Join("\n", method.Body.Instructions.Select(i => i.ToString())))
            throw new Exception("Unexpected IL change: " + method.FullName);
    Console.WriteLine("PATCH PASS: three clock call sites fixed; all method instruction sequences otherwise preserved.");
}
else if (args[0] == "qa-bind")
{
    using var resolver = new UnityResolver();
    resolver.AddSearchDirectory(@"A:\SteamLibrary\steamapps\common\RimWorld");
    resolver.AddSearchDirectory(Path.GetDirectoryName(Path.GetFullPath(args[1]))!);
    using var assembly = AssemblyDefinition.ReadAssembly(args[1], new ReaderParameters { AssemblyResolver = resolver, ReadingMode = ReadingMode.Immediate });
    foreach (var reference in assembly.MainModule.AssemblyReferences.Where(r => r.Name.StartsWith("UnityEngine", StringComparison.Ordinal)))
    {
        reference.Name = "UnityStubs";
        reference.Version = new Version(1, 0, 0, 0);
    }
    assembly.Write(args[2]);
    Console.WriteLine("QA ONLY: reproduced Waylith's UnityEngine*=UnityStubs assembly-name redirect for stock dotnet host.");
}
else
{
    const string game = @"A:\SteamLibrary\steamapps\common\RimWorld";
    AssemblyLoadContext.Default.Resolving += (_, name) => {
        var file = name.Name!.StartsWith("UnityEngine", StringComparison.Ordinal) ? "UnityStubs.dll" : name.Name + ".dll";
        var path = Path.Combine(game, file);
        return File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
    };
    var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(args[1]));
    var type = assembly.GetType("LayeredApparel.CompLayeredApparel", true)!;
    var method = type.GetMethod("UpdateVacuumStateCore", BindingFlags.NonPublic | BindingFlags.Instance)!;
    RuntimeHelpers.PrepareMethod(method.MethodHandle);
    Console.WriteLine("JIT PASS: actual UpdateVacuumStateCore compiled against actual UnityStubs.");
    var dialog = assembly.GetType("LayeredApparel.Dialog_ApparelManagement", true)!;
    RuntimeHelpers.PrepareMethod(dialog.GetMethod("UpdateStatus", BindingFlags.NonPublic | BindingFlags.Instance)!.MethodHandle);
    var apply = dialog.GetNestedTypes(BindingFlags.NonPublic).SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)).Single(m => m.Name == "<Apply>b__1");
    RuntimeHelpers.PrepareMethod(apply.MethodHandle);
    Console.WriteLine("JIT PASS: actual UpdateStatus and Apply callback compiled (all three affected methods).");
    var stubs = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(game, "UnityStubs.dll"));
    var clock = stubs.GetType("UnityEngine.Time", true)!.GetProperty("realtimeSinceStartupAsDouble")!.GetMethod!;
    var value = clock.Invoke(null, null);
    if (value is not double seconds || !double.IsFinite(seconds)) throw new Exception("Actual replacement clock failed.");
    Console.WriteLine("CALL PASS: actual replacement clock executed; full game state/GUI not initialized in this driver.");
}

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
            ? AssemblyDefinition.ReadAssembly(@"A:\SteamLibrary\steamapps\common\RimWorld\UnityStubs.dll")
            : base.Resolve(name);
}
