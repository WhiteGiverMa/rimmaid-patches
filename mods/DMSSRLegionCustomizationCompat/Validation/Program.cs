using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace DMSSRLegionCustomizationCompat.Validation;

internal static class Program
{
    private static string gameRoot = "";
    private static string harmonyPath = "";
    private static string dmssrPath = "";
    private static string fortifiedPath = "";
    private static string legionPath = "";
    private static string dmsCorePath = "";
    private static string ancientCorpsRoot = "";
    private static string patchPath = "";

    internal static string LegionRoot => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(legionPath)!, "..", ".."));
    internal static string DmsCoreRoot => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(dmsCorePath)!, "..", ".."));

    private static readonly string BoundDirectory = Path.Combine(AppContext.BaseDirectory, "bound");

    private static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--help")
        {
            Console.WriteLine("Usage: Validation bind|red|green GAME_ROOT HARMONY_DLL DMSSR_DLL FORTIFIED_DLL LEGION_DLL DMS_CORE_DLL ANCIENT_CORPS_ROOT PATCH_DLL");
            return 0;
        }

        if (args.Length != 9 || args[0] is not ("bind" or "red" or "green"))
        {
            Console.Error.WriteLine("Expected a validation mode and eight paths; use --help.");
            return 1;
        }

        string mode = args[0];
        gameRoot = Path.GetFullPath(args[1]);
        harmonyPath = Path.GetFullPath(args[2]);
        dmssrPath = Path.GetFullPath(args[3]);
        fortifiedPath = Path.GetFullPath(args[4]);
        legionPath = Path.GetFullPath(args[5]);
        dmsCorePath = Path.GetFullPath(args[6]);
        ancientCorpsRoot = Path.GetFullPath(args[7]);
        patchPath = Path.GetFullPath(args[8]);

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
    /// Writes a QA-only copy of the real game assembly with Verse.ModsConfig's static constructor
    /// removed, so validation never reads the user's ModsConfig.xml and DLC flags stay at their
    /// defaults. The installed game DLL and the Workshop DLLs are never modified.
    /// </summary>
    private static void Bind()
    {
        Directory.CreateDirectory(BoundDirectory);

        using DefaultAssemblyResolver resolver = new();
        foreach (string directory in SearchDirectories())
        {
            resolver.AddSearchDirectory(directory);
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

    private static string[] SearchDirectories()
    {
        return new[]
        {
            gameRoot,
            Path.GetDirectoryName(harmonyPath) ?? "",
            Path.GetDirectoryName(dmssrPath) ?? "",
            Path.GetDirectoryName(fortifiedPath) ?? "",
            Path.GetDirectoryName(legionPath) ?? "",
            Path.GetDirectoryName(dmsCorePath) ?? "",
        }.Where(directory => directory.Length != 0).Distinct().ToArray();
    }

    private static void LoadBoundAssemblies()
    {
        AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(gameRoot, "UnityStubs.dll"));
        AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(BoundDirectory, "Assembly-CSharp.dll"));

        // Load the real upstream assemblies in dependency order. Fortified must be present before
        // DMSSR is touched because DMSSR's window types derive from Fortified's window base class.
        AssemblyLoadContext.Default.LoadFromAssemblyPath(fortifiedPath);
        AssemblyLoadContext.Default.LoadFromAssemblyPath(dmssrPath);
        AssemblyLoadContext.Default.LoadFromAssemblyPath(legionPath);
    }

    private static void Run(string mode)
    {
        Assembly patch = AssemblyLoadContext.Default.LoadFromAssemblyPath(patchPath);
        Type modType = patch.GetType(
            "WhiteGiver.DMSSRLegionCustomizationCompat.DMSSRLegionCustomizationCompatMod",
            throwOnError: true)!;

        Tests.Run(mode, modType, gameRoot, LegionRoot, DmsCoreRoot, ancientCorpsRoot);
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

        foreach (string directory in SearchDirectories().Prepend(BoundDirectory))
        {
            string candidate = Path.Combine(directory, name.Name + ".dll");
            if (File.Exists(candidate))
            {
                return context.LoadFromAssemblyPath(candidate);
            }
        }

        return null;
    }
}
