using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace DMSSRStatPathPerfFix.Validation;

internal static class Program
{
    internal const string Owner = "whitegiver.dmssr.statpathperffix";
    internal const string ValidationOwner = "whitegiver.dmssr.statpathperffix.validation";

    private static string gameRoot = "";
    private static string harmonyPath = "";
    private static string upstreamPath = "";
    private static string patchPath = "";

    private static readonly string BoundDirectory = Path.Combine(AppContext.BaseDirectory, "bound");

    private static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--help")
        {
            Console.WriteLine("Usage: Validation bind|red|green|late-red|early-green GAME_ROOT HARMONY_DLL DMSSR_DLL PATCH_DLL");
            return 0;
        }

        if (args.Length != 5 || args[0] is not ("bind" or "red" or "green" or "late-red" or "early-green"))
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
            // The driver uses a project reference for internal test hooks. The script stages the
            // requested runtime bytes next to it; verify equality rather than silently testing a
            // potentially different project-reference copy.
            Assembly runtimePatch = AssemblyLoadContext.Default.LoadFromAssemblyPath(patchPath);
            string expectedHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(patchPath)));
            string actualHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(runtimePatch.Location)));
            if (actualHash != expectedHash) throw new InvalidOperationException("Wrong runtime DLL bytes: " + runtimePatch.Location);
            Console.WriteLine("Runtime patch: " + patchPath + " SHA256=" + actualHash);
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
    /// Writes a QA-only copy of the real game assembly with Verse.ModsConfig's static constructor
    /// removed, so validation never reads the user's ModsConfig.xml and DLC flags stay at their
    /// defaults. The installed game DLL and the Workshop DLL are never modified.
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
            Console.WriteLine("DMSSR references: " + string.Join(
                ", ",
                upstream.MainModule.AssemblyReferences.Select(reference => reference.Name)));
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
        AssemblyLoadContext.Default.LoadFromAssemblyPath(upstreamPath);
    }

    private static void Run(string mode)
    {
        Assembly patch = AssemblyLoadContext.Default.LoadFromAssemblyPath(patchPath);
        Type modType = patch.GetType(
            "WhiteGiver.DMSSRStatPathPerfFix.DMSSRStatPathPerfFixMod",
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
            Environment.GetEnvironmentVariable("DMSSR_VALIDATION_FORTIFIED_DIR")
                ?? @"A:\SteamLibrary\steamapps\workshop\content\294100\3498575851\1.7\Assemblies",
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
