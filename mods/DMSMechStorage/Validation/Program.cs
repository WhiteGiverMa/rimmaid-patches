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
            Console.WriteLine("       Validation run GAME_ROOT WORKSHOP_ROOT MOD_DLL QA_ROOT");
            Console.WriteLine("Only bind writes QA-only assembly copies, never installed assemblies or user configuration.");
            return 0;
        }
        bool bind = args.Length == 4 && args[0] == "bind";
        if (!bind && !(args.Length == 5 && args[0] == "run"))
        {
            Console.Error.WriteLine("Invalid arguments; use --help.");
            return 1;
        }

        string game = Path.GetFullPath(args[1]);
        string workshop = Path.GetFullPath(args[2]);
        string bound = Path.Combine(Path.GetFullPath(args[^1]), "bound");
        string harmony = Path.Combine(workshop, "2009463077", "1.7", "Assemblies");
        AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            foreach (string directory in new[] { bound, game, harmony, Path.Combine(workshop, "3498575851", "1.7", "Assemblies") })
            {
                string path = Path.Combine(directory, name.Name + ".dll");
                if (File.Exists(path)) return context.LoadFromAssemblyPath(path);
            }
            return null;
        };
        try
        {
            if (bind) Bind(game, workshop, bound);
            else
            {
                AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(args[3]));
                Assembly.GetExecutingAssembly().GetType("Driver", true)!.GetMethod("Run")!
                    .Invoke(null, new object[] { workshop });
            }
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
        string fortifiedPath = Path.Combine(workshop, "3498575851", "1.7", "Assemblies", "Fortified.dll");
        resolver.AddSearchDirectory(Path.GetDirectoryName(fortifiedPath)!);
        using (var fortified = AssemblyDefinition.ReadAssembly(fortifiedPath, new ReaderParameters { AssemblyResolver = resolver }))
        {
            foreach (AssemblyNameReference reference in fortified.MainModule.AssemblyReferences)
            {
                if (!reference.Name.StartsWith("UnityEngine", StringComparison.Ordinal)) continue;
                reference.Name = "UnityStubs";
                reference.Version = new Version(1, 0, 0, 0);
            }
            fortified.Write(Path.Combine(bound, "Fortified.dll"));
        }
        using var game = AssemblyDefinition.ReadAssembly(Path.Combine(gameRoot, "Assembly-CSharp.dll"),
            new ReaderParameters { AssemblyResolver = resolver });
        foreach (string name in new[] { "Verse.ModsConfig", "Verse.ModLister", "Verse.BaseContent" })
        {
            MethodDefinition initializer = game.MainModule.GetType(name).Methods.Single(method => method.Name == ".cctor");
            initializer.Body.Instructions.Clear();
            initializer.Body.ExceptionHandlers.Clear();
            initializer.Body.Variables.Clear();
            initializer.Body.GetILProcessor().Emit(OpCodes.Ret);
        }
        game.Write(Path.Combine(bound, "Assembly-CSharp.dll"));
        Console.WriteLine("PASS: QA-only Unity binding and disabled ModsConfig/ModLister/BaseContent initialization; original methods otherwise unchanged.");
        Console.WriteLine("Installed assemblies and user configuration were not modified.");
    }
}
