using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace DMSEWorldPerfFix.Validation;

internal static class Program
{
    internal const string Owner = "whitegiver.dmse.worldperffix";

    private static readonly string BoundDirectory = Path.Combine(AppContext.BaseDirectory, "bound");
    private static readonly List<string> extraAssemblies = new();

    private static string gameRoot = "";
    private static string harmonyPath = "";
    private static string dmsePath = "";
    private static string patchPath = "";

    private static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--help")
        {
            Console.WriteLine("Usage: Validation bind|probe|red|green GAME_ROOT HARMONY_DLL DMSE_DLL PATCH_DLL [EXTRA_DLL ...]");
            return 0;
        }

        if (args.Length < 5 || args[0] is not ("bind" or "probe" or "red" or "green"))
        {
            Console.Error.WriteLine("Expected a validation mode and four assembly paths; use --help.");
            return 1;
        }

        string mode = args[0];
        gameRoot = Path.GetFullPath(args[1]);
        harmonyPath = Path.GetFullPath(args[2]);
        dmsePath = Path.GetFullPath(args[3]);
        patchPath = Path.GetFullPath(args[4]);

        for (int i = 5; i < args.Length; i++)
        {
            extraAssemblies.Add(Path.GetFullPath(args[i]));
        }

        try
        {
            if (mode == "bind")
            {
                Bind();
                return 0;
            }

            if (!Directory.Exists(BoundDirectory) || !File.Exists(Path.Combine(BoundDirectory, "Assembly-CSharp.dll")))
            {
                throw new InvalidOperationException("Bound game assemblies are missing; run the bind mode first.");
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

    /// <summary>
    /// Writes QA-only copies of the real game and mod assemblies:
    ///  1. every UnityEngine* reference in DMSE / DMS / Fortified is rebound to UnityStubs so they can load in a
    ///     plain .NET host,
    ///  2. ModsConfig's static constructor is emptied so validation never reads or writes the user's
    ///     ModsConfig.xml.
    /// The Workshop DLLs and the installed game DLL are never modified.
    /// </summary>
    private static void Bind()
    {
        Directory.CreateDirectory(BoundDirectory);

        using DefaultAssemblyResolver resolver = new();
        resolver.AddSearchDirectory(gameRoot);
        resolver.AddSearchDirectory(Path.GetDirectoryName(harmonyPath)!);
        resolver.AddSearchDirectory(Path.GetDirectoryName(dmsePath)!);

        string dmseDir = Path.GetDirectoryName(dmsePath)!;
        string workshopRoot = Path.GetFullPath(Path.Combine(dmseDir, "..", "..", ".."));

        List<string> modAssemblies = new() { dmsePath };
        foreach (string extra in extraAssemblies)
        {
            if (File.Exists(extra))
            {
                modAssemblies.Add(extra);
            }
        }

        AddIfExists(Path.Combine(workshopRoot, "3498575851", "1.7", "Assemblies", "Fortified.dll"));
        AddIfExists(Path.Combine(workshopRoot, "3819799478", "1.7", "Assemblies", "DMS.dll"));

        void AddIfExists(string path)
        {
            if (File.Exists(path) && !modAssemblies.Any(existing => string.Equals(existing, path, StringComparison.OrdinalIgnoreCase)))
            {
                modAssemblies.Add(path);
            }
        }

        foreach (string source in modAssemblies)
        {
            resolver.AddSearchDirectory(Path.GetDirectoryName(source)!);
            string name = Path.GetFileName(source);
            using AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(
                source,
                new ReaderParameters { AssemblyResolver = resolver });

            int changed = 0;
            foreach (AssemblyNameReference reference in assembly.MainModule.AssemblyReferences)
            {
                if (!reference.Name.StartsWith("UnityEngine", StringComparison.Ordinal))
                {
                    continue;
                }

                reference.Name = "UnityStubs";
                reference.Version = new Version(1, 0, 0, 0);
                changed++;
            }

            assembly.Write(Path.Combine(BoundDirectory, name));
            Console.WriteLine($"Bound {name}: {changed} UnityEngine references redirected to UnityStubs; source unchanged.");
        }

        using (AssemblyDefinition game = AssemblyDefinition.ReadAssembly(
                   Path.Combine(gameRoot, "Assembly-CSharp.dll"),
                   new ReaderParameters { AssemblyResolver = resolver }))
        {
            MethodDefinition configInitializer = game.MainModule
                .GetType("Verse.ModsConfig")?
                .Methods
                .Single(method => method.Name == ".cctor")
                ?? throw new InvalidOperationException("Verse.ModsConfig..cctor was not found.");

            configInitializer.Body.Instructions.Clear();
            configInitializer.Body.ExceptionHandlers.Clear();
            configInitializer.Body.Variables.Clear();
            configInitializer.Body.GetILProcessor().Emit(OpCodes.Ret);

            game.Write(Path.Combine(BoundDirectory, "Assembly-CSharp.dll"));
            Console.WriteLine("Bound Assembly-CSharp: ModsConfig initialization disabled; no user-config IO.");
        }
    }

    private static void LoadBoundAssemblies()
    {
        AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(gameRoot, "UnityStubs.dll"));
        AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(BoundDirectory, "Assembly-CSharp.dll"));

        // Load in dependency order; DMSE needs Assembly-CSharp, DMS and Fortified.
        foreach (string name in new[] { "DMS.dll", "Fortified.dll" })
        {
            string candidate = Path.Combine(BoundDirectory, name);
            if (File.Exists(candidate))
            {
                AssemblyLoadContext.Default.LoadFromAssemblyPath(candidate);
            }
        }

        AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(BoundDirectory, "DMSE.dll"));
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
            gameRoot,
            Path.GetDirectoryName(harmonyPath) ?? "",
            Path.GetDirectoryName(dmsePath) ?? "",
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

    internal static Assembly PatchAssembly => AssemblyLoadContext.Default.LoadFromAssemblyPath(patchPath);
}
