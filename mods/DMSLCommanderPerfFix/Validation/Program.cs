using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace WhiteGiver.DMSL.CommanderPerfFix.Validation;

internal static class Program
{
    private const string DefaultGameRoot = @"A:\SteamLibrary\steamapps\common\RimWorld";
    private const string DefaultHarmony = @"A:\SteamLibrary\steamapps\workshop\content\294100\2009463077\1.7\Assemblies\0Harmony.dll";
    private const string DefaultLegion = @"A:\SteamLibrary\steamapps\workshop\content\294100\3666117000\1.7\Assemblies\DMS_Legion.dll";
    private const string DefaultStubbedGame = @"G:\dev\rimmaid-patches\mods\DMSEWorldPerfFix\Validation\bin\Release\net10.0\bound\Assembly-CSharp.dll";

    private static readonly string BoundDirectory = Path.Combine(AppContext.BaseDirectory, "bound");
    private static string gameRoot = DefaultGameRoot;
    private static string harmonyPath = DefaultHarmony;
    private static string legionPath = DefaultLegion;
    private static string patchPath = Path.Combine(AppContext.BaseDirectory, "DMSLCommanderPerfFix.dll");
    private static string stubbedGamePath = DefaultStubbedGame;

    private static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--help")
        {
            Console.WriteLine("Usage: Validation bind|red|green [GAME_ROOT HARMONY_DLL LEGION_DLL PATCH_DLL STUBBED_GAME_DLL]");
            return 0;
        }

        if (args.Length != 1 && args.Length != 6 || args.Length == 0 || args[0] is not ("bind" or "red" or "green"))
        {
            Console.Error.WriteLine("Expected bind, red or green; use --help for optional assembly overrides.");
            return 1;
        }

        string mode = args[0];
        if (args.Length == 6)
        {
            gameRoot = Path.GetFullPath(args[1]);
            harmonyPath = Path.GetFullPath(args[2]);
            legionPath = Path.GetFullPath(args[3]);
            patchPath = Path.GetFullPath(args[4]);
            stubbedGamePath = Path.GetFullPath(args[5]);
        }

        try
        {
            if (mode == "bind")
            {
                Bind();
                return 0;
            }

            string boundGame = Path.Combine(BoundDirectory, "Assembly-CSharp.dll");
            if (!File.Exists(boundGame))
            {
                throw new InvalidOperationException("Bound QA assemblies are missing; run bind first.");
            }

            AssemblyLoadContext.Default.Resolving += Resolve;
            LoadBoundAssemblies();
            Tests.Run(mode);
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
        Console.WriteLine("Real Assembly-CSharp SHA256: " + Hash(Path.Combine(gameRoot, "Assembly-CSharp.dll")));
        Console.WriteLine("Real DMS_Legion      SHA256: " + Hash(legionPath));
        Console.WriteLine("Real 0Harmony        SHA256: " + Hash(harmonyPath));
        Console.WriteLine("Config-safe base     SHA256: " + Hash(stubbedGamePath));

        // Reuse the specified real-1.7-derived bound copy whose ModsConfig cctor is already disabled.
        // QA counters are added only to our private copy; neither the base nor the installed game is written.
        string boundGame = Path.Combine(BoundDirectory, "Assembly-CSharp.dll");
        string temporaryGame = Path.Combine(BoundDirectory, "Assembly-CSharp.instrumented.dll");
        using (DefaultAssemblyResolver resolver = new())
        {
            resolver.AddSearchDirectory(gameRoot);
            resolver.AddSearchDirectory(Path.GetDirectoryName(harmonyPath)!);
            resolver.AddSearchDirectory(Path.GetDirectoryName(legionPath)!);
            using AssemblyDefinition game = AssemblyDefinition.ReadAssembly(
                stubbedGamePath,
                new ReaderParameters { AssemblyResolver = resolver });
            InjectProbe(game, "Verse.ThingWithComps", "GetComp", "CompQueries");
            InjectProbe(game, "Verse.Game", "GetComponent", "ComponentQueries");
            game.Write(temporaryGame);
        }

        File.Copy(temporaryGame, boundGame, overwrite: true);
        File.Delete(temporaryGame);
        Console.WriteLine("Bound private Assembly-CSharp: config IO disabled + QA-only GetComp/GetComponent entry counters.");

        using (DefaultAssemblyResolver resolver = new())
        {
            resolver.AddSearchDirectory(gameRoot);
            resolver.AddSearchDirectory(Path.GetDirectoryName(harmonyPath)!);
            resolver.AddSearchDirectory(Path.GetDirectoryName(legionPath)!);
            using AssemblyDefinition legion = AssemblyDefinition.ReadAssembly(
                legionPath,
                new ReaderParameters { AssemblyResolver = resolver });
            int changed = 0;
            foreach (AssemblyNameReference reference in legion.MainModule.AssemblyReferences)
            {
                if (!reference.Name.StartsWith("UnityEngine", StringComparison.Ordinal))
                {
                    continue;
                }

                reference.Name = "UnityStubs";
                reference.Version = new Version(1, 0, 0, 0);
                changed++;
            }

            legion.Write(Path.Combine(BoundDirectory, "DMS_Legion.dll"));
            Console.WriteLine($"Bound private DMS_Legion: {changed} UnityEngine references redirected; Workshop source unchanged.");
        }

        string existingBound = Path.GetDirectoryName(stubbedGamePath)!;
        foreach (string name in new[] { "DMS.dll", "Fortified.dll" })
        {
            string source = Path.Combine(existingBound, name);
            if (File.Exists(source))
            {
                File.Copy(source, Path.Combine(BoundDirectory, name), overwrite: true);
                Console.WriteLine("Copied config-safe dependency: " + name);
            }
        }
    }

    private static void InjectProbe(AssemblyDefinition assembly, string typeName, string methodName, string fieldName)
    {
        ModuleDefinition module = assembly.MainModule;
        const string probeTypeName = "DMSLCommanderPerfFix.Validation.Probes";
        TypeDefinition? probes = module.GetType(probeTypeName);
        if (probes == null)
        {
            probes = new TypeDefinition(
                "DMSLCommanderPerfFix.Validation",
                "Probes",
                Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Abstract | Mono.Cecil.TypeAttributes.Sealed,
                module.TypeSystem.Object);
            module.Types.Add(probes);
        }

        FieldDefinition counter = new(
            fieldName,
            Mono.Cecil.FieldAttributes.Public | Mono.Cecil.FieldAttributes.Static,
            module.TypeSystem.Int32);
        probes.Fields.Add(counter);

        MethodDefinition method = module.GetType(typeName).Methods.Single(
            candidate => candidate.Name == methodName && candidate.HasGenericParameters && candidate.Parameters.Count == 0);
        Instruction[] probe =
        {
            Instruction.Create(OpCodes.Ldsfld, counter),
            Instruction.Create(OpCodes.Ldc_I4_1),
            Instruction.Create(OpCodes.Add),
            Instruction.Create(OpCodes.Stsfld, counter),
        };
        for (int i = 0; i < probe.Length; i++)
        {
            method.Body.Instructions.Insert(i, probe[i]);
        }
    }

    private static void LoadBoundAssemblies()
    {
        AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(gameRoot, "UnityStubs.dll"));
        AssemblyLoadContext.Default.LoadFromAssemblyPath(harmonyPath);
        AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(BoundDirectory, "Assembly-CSharp.dll"));
        foreach (string name in new[] { "DMS.dll", "Fortified.dll" })
        {
            string candidate = Path.Combine(BoundDirectory, name);
            if (File.Exists(candidate))
            {
                AssemblyLoadContext.Default.LoadFromAssemblyPath(candidate);
            }
        }

        AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(BoundDirectory, "DMS_Legion.dll"));
        AssemblyLoadContext.Default.LoadFromAssemblyPath(patchPath);
        Console.WriteLine("Loaded real 1.7 game/Legion IL from private config-safe bound copies; ModsConfig.xml is never accessed.");
    }

    private static Assembly? Resolve(AssemblyLoadContext context, AssemblyName name)
    {
        foreach (Assembly loaded in AssemblyLoadContext.Default.Assemblies)
        {
            if (string.Equals(loaded.GetName().Name, name.Name, StringComparison.Ordinal))
            {
                return loaded;
            }
        }

        string[] directories =
        {
            BoundDirectory,
            AppContext.BaseDirectory,
            gameRoot,
            Path.GetDirectoryName(harmonyPath) ?? string.Empty,
            Path.GetDirectoryName(legionPath) ?? string.Empty,
            Path.GetDirectoryName(patchPath) ?? string.Empty,
        };
        foreach (string directory in directories)
        {
            if (directory.Length == 0)
            {
                continue;
            }

            string candidate = Path.Combine(directory, name.Name + ".dll");
            if (File.Exists(candidate))
            {
                return context.LoadFromAssemblyPath(candidate);
            }
        }

        return null;
    }

    private static string Hash(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
