using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--help")
        {
            Console.WriteLine("Usage: Validation bind GAME_ROOT WORKSHOP_ROOT QA_ROOT");
            Console.WriteLine("       Validation contract|run|absent GAME_ROOT WORKSHOP_ROOT PATCH_DLL QA_ROOT");
            Console.WriteLine("Only bind writes QA copies. It never edits installed assemblies or user configuration.");
            return 0;
        }
        bool bind = args.Length == 4 && args[0] == "bind";
        bool run = args.Length == 5 && args[0] is "contract" or "run" or "absent";
        if (!bind && !run)
        {
            Console.Error.WriteLine("Invalid arguments; use --help.");
            return 1;
        }

        string game = Path.GetFullPath(args[1]);
        string workshop = Path.GetFullPath(args[2]);
        string qa = Path.GetFullPath(args[^1]);
        string bound = Path.Combine(qa, "bound");
        string harmony = Path.Combine(workshop, "2009463077", "1.7", "Assemblies");
        AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            foreach (string directory in new[] { bound, game, harmony })
            {
                string path = Path.Combine(directory, name.Name + ".dll");
                if (File.Exists(path)) return context.LoadFromAssemblyPath(path);
            }
            return null;
        };
        try
        {
            if (bind) Bind(game, workshop, bound);
            else Assembly.GetExecutingAssembly().GetType("Driver", true)!.GetMethod("Run")!
                .Invoke(null, new object[] { args[0], Path.GetFullPath(args[3]), game, workshop, qa });
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static void Bind(string gameRoot, string workshop, string bound)
    {
        if (bound.StartsWith(gameRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || bound.StartsWith(workshop + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("QA output must be outside the installed game and Workshop.");
        Directory.CreateDirectory(bound);
        using var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(gameRoot);
        resolver.AddSearchDirectory(Path.Combine(workshop, "2009463077", "1.7", "Assemblies"));
        string[] upstreamFiles =
        {
            Path.Combine(workshop, "3256974620", "1.6", "Assemblies", "Milira.dll"),
            Path.Combine(workshop, "2988801276", "1.6", "Assemblies", "AncotLibrary.dll"),
            Path.Combine(workshop, "3496629836", "1.6", "Assemblies", "MilianModification.dll")
        };
        foreach (string path in upstreamFiles) resolver.AddSearchDirectory(Path.GetDirectoryName(path)!);
        foreach (string path in upstreamFiles)
        {
            using var assembly = AssemblyDefinition.ReadAssembly(path, new ReaderParameters { AssemblyResolver = resolver });
            foreach (AssemblyNameReference reference in assembly.MainModule.AssemblyReferences)
            {
                if (!reference.Name.StartsWith("UnityEngine", StringComparison.Ordinal)) continue;
                reference.Name = "UnityStubs";
                reference.Version = new Version(1, 0, 0, 0);
            }
            assembly.Write(Path.Combine(bound, Path.GetFileName(path)));
        }

        using var game = AssemblyDefinition.ReadAssembly(Path.Combine(gameRoot, "Assembly-CSharp.dll"),
            new ReaderParameters { AssemblyResolver = resolver });
        foreach (string type in new[] { "Verse.ModsConfig", "Verse.BaseContent" })
        {
            MethodDefinition initializer = game.MainModule.GetType(type).Methods.Single(method => method.Name == ".cctor");
            Clear(initializer);
            initializer.Body.GetILProcessor().Emit(OpCodes.Ret);
        }

        // QA-only final drop placement; equipment transfer and the Milira job remain unchanged.
        MethodDefinition drop = game.MainModule.GetType("Verse.GenDrop").Methods.Single(method => method.Name == "TryDropSpawn");
        var validation = new AssemblyNameReference("Validation", new Version(1, 0, 0, 0));
        game.MainModule.AssemblyReferences.Add(validation);
        var hook = new MethodReference("Drop", game.MainModule.TypeSystem.Boolean,
            new TypeReference("", "ValidationHooks", game.MainModule, validation));
        foreach (ParameterDefinition parameter in drop.Parameters) hook.Parameters.Add(new ParameterDefinition(parameter.ParameterType));
        Clear(drop);
        ILProcessor il = drop.Body.GetILProcessor();
        foreach (ParameterDefinition parameter in drop.Parameters) il.Emit(OpCodes.Ldarg, parameter);
        il.Emit(OpCodes.Call, hook);
        il.Emit(OpCodes.Ret);
        game.Write(Path.Combine(bound, "Assembly-CSharp.dll"));
        Console.WriteLine("PASS: QA-only Unity binding, isolated ModsConfig/render-content initialization and final drop placement; Prefs is seeded in-memory.");
        Console.WriteLine("Installed game/mod assemblies and user configuration were not modified.");
    }

    private static void Clear(MethodDefinition method)
    {
        method.Body.Instructions.Clear();
        method.Body.ExceptionHandlers.Clear();
        method.Body.Variables.Clear();
    }
}
