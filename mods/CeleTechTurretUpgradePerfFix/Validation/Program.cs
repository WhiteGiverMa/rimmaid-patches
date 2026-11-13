using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace CeleTechTurretUpgradePerfFix.Validation;

internal static class Program
{
    internal const string Owner = "whitegiver.celetech.turretupgradeperffix";

    private static string gameRoot = "";
    private static string harmonyPath = "";
    private static string upstreamPath = "";
    private static string patchPath = "";

    private static readonly string BoundDirectory = Path.Combine(AppContext.BaseDirectory, "bound");

    private static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--help")
        {
            Console.WriteLine("Usage: Validation bind|red|green GAME_ROOT HARMONY_DLL CELETECH_DLL PATCH_DLL");
            return 0;
        }

        if (args.Length != 5 || args[0] is not ("bind" or "red" or "green"))
        {
            Console.Error.WriteLine("Expected a validation mode and four assembly paths; use --help.");
            return 1;
        }

        string mode = args[0];
        gameRoot = Path.GetFullPath(args[1]);
        harmonyPath = Path.GetFullPath(args[2]);
        upstreamPath = Path.GetFullPath(args[3]);
        patchPath = Path.GetFullPath(args[4]);

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
            Run(mode);
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    /// <summary>
    /// Writes QA-only copies of the real game and CeleTech assemblies:
    /// 1. CeleTech's UnityEngine references are rebound to UnityStubs so it can load in a plain .NET host.
    /// 2. ModsConfig's static constructor is emptied so validation never reads or writes the user's
    ///    ModsConfig.xml and DLC flags stay at their defaults.
    /// The Workshop DLL and the installed game DLL are never modified.
    /// </summary>
    private static void Bind()
    {
        Directory.CreateDirectory(BoundDirectory);

        using DefaultAssemblyResolver resolver = new();
        resolver.AddSearchDirectory(gameRoot);
        resolver.AddSearchDirectory(Path.GetDirectoryName(harmonyPath)!);
        resolver.AddSearchDirectory(Path.GetDirectoryName(upstreamPath)!);

        using (AssemblyDefinition upstream = AssemblyDefinition.ReadAssembly(
                   upstreamPath,
                   new ReaderParameters { AssemblyResolver = resolver }))
        {
            int changed = 0;
            foreach (AssemblyNameReference reference in upstream.MainModule.AssemblyReferences)
            {
                if (!reference.Name.StartsWith("UnityEngine", StringComparison.Ordinal))
                {
                    continue;
                }

                reference.Name = "UnityStubs";
                reference.Version = new Version(1, 0, 0, 0);
                changed++;
            }

            if (changed == 0)
            {
                throw new InvalidOperationException("CeleTech.Base.dll no longer references UnityEngine assemblies; rebind step is stale.");
            }

            upstream.Write(Path.Combine(BoundDirectory, "CeleTech.Base.dll"));
            Console.WriteLine($"Bound CeleTech.Base: {changed} UnityEngine references redirected to UnityStubs; Workshop DLL unchanged.");
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
        AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(BoundDirectory, "CeleTech.Base.dll"));
    }

    private static void Run(string mode)
    {
        Assembly patch = AssemblyLoadContext.Default.LoadFromAssemblyPath(patchPath);
        Type modType = patch.GetType(
            "WhiteGiver.CeleTechTurretUpgradePerfFix.CeleTechTurretUpgradePerfFixMod",
            throwOnError: true)!;

        Tests.Run(mode, modType);
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
            Path.GetDirectoryName(upstreamPath) ?? "",
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
}
