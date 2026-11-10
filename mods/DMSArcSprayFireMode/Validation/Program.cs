using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length != 5) { Console.Error.WriteLine("Usage: Validation bind|red|green|sound-red|sound|ce|sound-ce|drift-bind|drift|sound-drift|restore GAME WORKSHOP MOD QA"); return 1; }
        try
        {
            string game = Path.GetFullPath(args[1]), workshop = Path.GetFullPath(args[2]), mod = Path.GetFullPath(args[3]), qa = Path.GetFullPath(args[4]);
            foreach (string protectedRoot in new[] { game, workshop, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + "Low" })
                if (qa.StartsWith(protectedRoot, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("QA must be outside game, workshop and user configuration/save data.");
            string bound = Path.Combine(qa, "bound");
            AssemblyLoadContext.Default.Resolving += (context, name) =>
            {
                foreach (string directory in new[] { bound, game, Path.Combine(workshop, "2009463077/1.7/Assemblies"), Path.Combine(mod, "Source/bin/Release/net10.0") })
                {
                    string path = Path.Combine(directory, name.Name + ".dll");
                    if (File.Exists(path)) return context.LoadFromAssemblyPath(path);
                }
                return null;
            };
            if (args[0] == "bind") Bind(game, workshop, bound);
            else if (args[0] == "drift-bind") MutateForDrift(bound, game);
            else if (args[0] == "restore") File.Copy(Path.Combine(bound, "Assembly-CSharp.baseline"), Path.Combine(bound, "Assembly-CSharp.dll"), true);
            else Run(args[0], game, workshop, mod, qa);
            return 0;
        }
        catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
    }

    private static void MutateForDrift(string bound, string game)
    {
        string file = Path.Combine(bound, "Assembly-CSharp.dll");
        File.Copy(file, Path.Combine(bound, "Assembly-CSharp.baseline"), true);
        using var resolver = new DefaultAssemblyResolver(); resolver.AddSearchDirectory(game); resolver.AddSearchDirectory(bound);
        using var assembly = AssemblyDefinition.ReadAssembly(file, new ReaderParameters { AssemblyResolver = resolver, InMemory = true });
        MethodDefinition original = assembly.MainModule.GetType("Verse.Verb_LaunchProjectile").Methods.Single(m => m.Name == "TryCastShot");
        Instruction instruction = original.Body.Instructions.Single(i => i.Operand is MethodReference m && m.Name == "get_Projectile");
        // Valid stack shape but incompatible semantics: remove receiver, supply null projectile.
        // The installed game and all unmutated green targets remain untouched.
        instruction.OpCode = OpCodes.Pop; instruction.Operand = null;
        original.Body.GetILProcessor().InsertAfter(instruction, Instruction.Create(OpCodes.Ldnull));
        string candidate = file + ".candidate"; assembly.Write(candidate); File.Copy(candidate, file, true);
        Console.WriteLine("PASS drift fixture: removed one critical call from QA-only real shot method.");
    }

    private static void Bind(string game, string workshop, string bound)
    {
        Directory.CreateDirectory(bound);
        using var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(game);
        foreach (string source in new[] { Path.Combine(game, "Assembly-CSharp.dll"), Path.Combine(workshop, "3498575851/1.7/Assemblies/Fortified.dll"), Path.Combine(workshop, "3807251471/Assemblies/WeaponStation.dll") })
        {
            Console.WriteLine($"INPUT {Path.GetFileName(source)} SHA256 {Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(source)))}");
            using var assembly = AssemblyDefinition.ReadAssembly(source, new ReaderParameters { AssemblyResolver = resolver });
            if (assembly.Name.Name == "Assembly-CSharp")
            {
                foreach (string name in new[] { "Verse.Verb", "Verse.Verb_Spray", "Verse.Verb_ArcSpray", "Verse.Verb_LaunchProjectile", "Verse.Verb_Shoot", "Verse.ShotReport" })
                {
                    TypeDefinition type = assembly.MainModule.GetType(name);
                    Console.WriteLine($"TYPE {type.FullName} : {type.BaseType}");
                    foreach (MethodDefinition method in type.Methods.Where(m => m.Name is "TryCastShot" or "get_AimAngleOverride" or "PreparePath" or "TryCastNextBurstShot" or "HitReportFor"))
                    {
                        Console.WriteLine($"  METHOD {method.Attributes} {method.FullName}");
                        foreach (Instruction instruction in (method.HasBody ? method.Body.Instructions.AsEnumerable() : Enumerable.Empty<Instruction>()).Where(i => i.Operand is MethodReference or FieldReference))
                            Console.WriteLine($"    {instruction.OpCode} {instruction.Operand}");
                    }
                }
                foreach (string name in new[] { "Verse.ModsConfig", "Verse.BaseContent", "RimWorld.Building_TurretGun", "Verse.Command", "RimWorld.TexCommand", "RimWorld.CompRefuelable" })
                {
                    MethodDefinition? initializer = assembly.MainModule.GetType(name)?.Methods.SingleOrDefault(m => m.Name == ".cctor");
                    if (initializer == null) continue;
                    initializer.Body.Instructions.Clear(); initializer.Body.ExceptionHandlers.Clear(); initializer.Body.Variables.Clear();
                    initializer.Body.GetILProcessor().Emit(OpCodes.Ret);
                }
            }
            foreach (AssemblyNameReference reference in assembly.MainModule.AssemblyReferences.Where(r => r.Name.StartsWith("UnityEngine", StringComparison.Ordinal)))
            { reference.Name = "UnityStubs"; reference.Version = new Version(1, 0, 0, 0); }
            if (assembly.Name.Name == "Fortified")
            {
                MethodDefinition initializer = assembly.MainModule.GetType("Fortified.Building_TurretCapacity").Methods.Single(m => m.Name == ".cctor");
                initializer.Body.Instructions.Clear(); initializer.Body.ExceptionHandlers.Clear(); initializer.Body.Variables.Clear();
                initializer.Body.GetILProcessor().Emit(OpCodes.Ret);
            }
            assembly.Write(Path.Combine(bound, Path.GetFileName(source)));
        }
        Console.WriteLine("PASS: QA-only copies; Unity rebound, config I/O/static textures disabled. All shooting, FFF path, shot report, burst, Gizmo and Scribe target methods unchanged.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Run(string mode, string game, string workshop, string mod, string qa) => Driver.Run(mode, game, workshop, mod, qa);
}
