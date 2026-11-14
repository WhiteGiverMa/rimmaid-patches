using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace VanillaFireModesPerfFix.Validation;

/// <summary>
/// Offline RED/GREEN harness for VanillaFireModesPerfFix.
///
/// Modes:
///   bind   GAME_ROOT HARMONY_DLL VFM_DLL MOD_DLL [BOUND_SOURCE_DIR]
///          Stages the QA-only game assembly used by the other modes. It never touches the installed game or any
///          Workshop file: the only rewrite is <c>Verse.ModsConfig..cctor</c>, which is emptied so the harness can
///          never read or write the user's ModsConfig.xml. When BOUND_SOURCE_DIR (default: the DMSEWorldPerfFix
///          validation output) already contains that stub it is copied verbatim.
///   red    GAME_ROOT HARMONY_DLL VFM_DLL MOD_DLL
///          Real Vanilla Fire Modes patches only. Prints the behavioural transcript, the hot-loop timings and the
///          number of burst-table probes each VFM patch performs.
///   green  GAME_ROOT HARMONY_DLL VFM_DLL MOD_DLL
///          The same transcript and loops after VanillaFireModesPerfFix took over VFM's patches.
///   verify GAME_ROOT HARMONY_DLL VFM_DLL MOD_DLL
///          Runs red and green in one process and diffs them; also asserts the patch graph and idempotency.
/// </summary>
internal static class Program
{
    internal const string ModOwner = "whitegiver.vfm.perffix";
    internal const string VfmOwner = "Aliza.VanillaFireModes";

    private const string DmseBoundDefault =
        @"G:\dev\rimmaid-patches\mods\DMSEWorldPerfFix\Validation\bin\Release\net10.0\bound";

    internal static string GameRoot = "";
    internal static string HarmonyPath = "";
    internal static string VfmPath = "";

    internal static string BoundDirectory => Path.Combine(AppContext.BaseDirectory, "bound");

    internal static string ModAssemblyPath => Path.Combine(AppContext.BaseDirectory, "VanillaFireModesPerfFix.dll");

    private static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--help")
        {
            Usage();
            return 0;
        }

        if (args.Length < 4)
        {
            Usage();
            return 1;
        }

        string mode = args[0];
        GameRoot = Path.GetFullPath(args[1]);
        HarmonyPath = Path.GetFullPath(args[2]);
        VfmPath = Path.GetFullPath(args[3]);
        string boundSource = args.Length > 5 ? Path.GetFullPath(args[5]) : DmseBoundDefault;

        try
        {
            if (mode == "bind")
            {
                Bind(boundSource);
                return 0;
            }

            if (!File.Exists(Path.Combine(BoundDirectory, "Assembly-CSharp.dll")))
            {
                throw new InvalidOperationException($"Bound game assemblies are missing from {BoundDirectory}; run the bind mode first.");
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

    private static void Usage()
    {
        Console.WriteLine("Usage: Validation bind|red|green|verify GAME_ROOT HARMONY_DLL VFM_DLL MOD_DLL [BOUND_SOURCE_DIR]");
        Console.WriteLine("  GAME_ROOT        RimWorld install directory containing Assembly-CSharp.dll and UnityStubs.dll");
        Console.WriteLine("  HARMONY_DLL      0Harmony.dll used by the running game");
        Console.WriteLine("  VFM_DLL          VFM_VanillaFireModes.dll (1.7)");
        Console.WriteLine("  MOD_DLL          VanillaFireModesPerfFix.dll under test");
        Console.WriteLine("  BOUND_SOURCE_DIR directory whose Assembly-CSharp.dll is copied by bind (default: DMSEWorldPerfFix)");
    }

    /// <summary>
    /// Produces the QA-only copy of Assembly-CSharp. The stub only disables <c>Verse.ModsConfig..cctor</c>, which
    /// is the single member that would read/write the real <c>ModsConfig.xml</c> during startup.
    /// </summary>
    private static void Bind(string boundSource)
    {
        Directory.CreateDirectory(BoundDirectory);
        string destination = Path.Combine(BoundDirectory, "Assembly-CSharp.dll");
        string staged = Path.Combine(boundSource, "Assembly-CSharp.dll");

        if (File.Exists(staged))
        {
            File.Copy(staged, destination, overwrite: true);
            Console.WriteLine($"bind: copied stubbed Assembly-CSharp.dll from {boundSource}");
        }
        else
        {
            string source = Path.Combine(GameRoot, "Assembly-CSharp.dll");
            using DefaultAssemblyResolver resolver = new();
            resolver.AddSearchDirectory(GameRoot);
            using AssemblyDefinition game = AssemblyDefinition.ReadAssembly(
                source,
                new ReaderParameters { AssemblyResolver = resolver });
            MethodDefinition initializer = game.MainModule.GetType("Verse.ModsConfig")?.Methods
                .SingleOrDefault(method => method.Name == ".cctor")
                ?? throw new InvalidOperationException("Verse.ModsConfig..cctor was not found.");
            initializer.Body.Instructions.Clear();
            initializer.Body.ExceptionHandlers.Clear();
            initializer.Body.Variables.Clear();
            initializer.Body.GetILProcessor().Emit(OpCodes.Ret);
            game.Write(destination);
            Console.WriteLine($"bind: generated stubbed Assembly-CSharp.dll from {source}");
        }

        using (AssemblyDefinition bound = AssemblyDefinition.ReadAssembly(destination))
        {
            MethodDefinition initializer = bound.MainModule.GetType("Verse.ModsConfig")?.Methods
                .SingleOrDefault(method => method.Name == ".cctor")
                ?? throw new InvalidOperationException("The stubbed Assembly-CSharp has no Verse.ModsConfig..cctor.");
            bool disabled = initializer.Body.Instructions.Count == 1 && initializer.Body.Instructions[0].OpCode == OpCodes.Ret;
            Console.WriteLine($"bind: ModsConfig..cctor disabled = {disabled}");
            if (!disabled)
            {
                throw new InvalidOperationException("The staged Assembly-CSharp still initialises ModsConfig.");
            }
        }

        Console.WriteLine($"bind: sha256 {Sha256(destination)}  {destination}");
        Console.WriteLine($"bind: sha256 {Sha256(Path.Combine(GameRoot, "Assembly-CSharp.dll"))}  (installed game, unmodified)");
        Console.WriteLine($"bind: sha256 {Sha256(VfmPath)}  (workshop VFM, unmodified)");
    }

    private static void LoadBoundAssemblies()
    {
        AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(GameRoot, "UnityStubs.dll"));
        AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(BoundDirectory, "Assembly-CSharp.dll"));
        AssemblyLoadContext.Default.LoadFromAssemblyPath(VfmPath);
        AssemblyLoadContext.Default.LoadFromAssemblyPath(ModAssemblyPath);
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
            GameRoot,
            Path.GetDirectoryName(HarmonyPath) ?? "",
            Path.GetDirectoryName(VfmPath) ?? "",
            AppContext.BaseDirectory,
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

    internal static string Sha256(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
