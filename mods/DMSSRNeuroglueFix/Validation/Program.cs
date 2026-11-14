using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace DMSSRNeuroglueFix.Validation;

internal static class Program
{
    private static string gameRoot = "";
    private static string harmonyPath = "";
    private static string dmsRoot = "";
    private static string dmssrRoot = "";
    private static string fortifiedPath = "";
    private static string patchPath = "";

    private static readonly string BoundDirectory = Path.Combine(AppContext.BaseDirectory, "bound");

    private static int Main(string[] args)
    {
        if (args.Length != 7 || args[0] is not ("bind" or "red" or "green"))
        {
            Console.Error.WriteLine("Usage: Validation bind|red|green GAME_ROOT HARMONY_DLL DMS_ROOT DMSSR_ROOT FORTIFIED_DLL PATCH_DLL");
            return 1;
        }

        string mode = args[0];
        gameRoot = Path.GetFullPath(args[1]);
        harmonyPath = Path.GetFullPath(args[2]);
        dmsRoot = Path.GetFullPath(args[3]);
        dmssrRoot = Path.GetFullPath(args[4]);
        fortifiedPath = Path.GetFullPath(args[5]);
        patchPath = Path.GetFullPath(args[6]);

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
                throw new InvalidOperationException("Bound Assembly-CSharp.dll missing; run bind first.");
            }

            AssemblyLoadContext.Default.Resolving += Resolve;
            AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(gameRoot, "UnityStubs.dll"));
            AssemblyLoadContext.Default.LoadFromAssemblyPath(boundGame);
            AssemblyLoadContext.Default.LoadFromAssemblyPath(fortifiedPath);
            AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(dmsRoot, @"1.7\Assemblies\DMS.dll"));
            AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(dmssrRoot, @"1.7\Assemblies\DMSSR.dll"));
            Assembly patch = AssemblyLoadContext.Default.LoadFromAssemblyPath(patchPath);

            Tests.Run(mode, patch, dmsRoot, dmssrRoot, Path.GetFullPath(Path.Combine(patchPath, @"..\..\..")));
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

        using DefaultAssemblyResolver resolver = new();
        resolver.AddSearchDirectory(gameRoot);
        resolver.AddSearchDirectory(Path.GetDirectoryName(harmonyPath)!);
        resolver.AddSearchDirectory(Path.GetDirectoryName(fortifiedPath)!);
        resolver.AddSearchDirectory(Path.Combine(dmsRoot, @"1.7\Assemblies"));
        resolver.AddSearchDirectory(Path.Combine(dmssrRoot, @"1.7\Assemblies"));

        using AssemblyDefinition game = AssemblyDefinition.ReadAssembly(
            Path.Combine(gameRoot, "Assembly-CSharp.dll"),
            new ReaderParameters { AssemblyResolver = resolver });

        MethodDefinition configCctor = game.MainModule
            .GetType("Verse.ModsConfig")?
            .Methods
            .Single(m => m.Name == ".cctor")
            ?? throw new InvalidOperationException("Verse.ModsConfig..cctor not found.");

        configCctor.Body.Instructions.Clear();
        configCctor.Body.ExceptionHandlers.Clear();
        configCctor.Body.Variables.Clear();
        configCctor.Body.GetILProcessor().Emit(OpCodes.Ret);

        game.Write(Path.Combine(BoundDirectory, "Assembly-CSharp.dll"));
        Console.WriteLine("Bound Assembly-CSharp.dll prepared (no user ModsConfig IO).");
    }

    private static Assembly? Resolve(AssemblyLoadContext context, AssemblyName name)
    {
        foreach (Assembly loaded in context.Assemblies)
        {
            if (string.Equals(loaded.GetName().Name, name.Name, StringComparison.Ordinal))
            {
                return loaded;
            }
        }

        string[] dirs =
        {
            BoundDirectory,
            gameRoot,
            Path.GetDirectoryName(harmonyPath) ?? "",
            Path.GetDirectoryName(fortifiedPath) ?? "",
            Path.Combine(dmsRoot, @"1.7\Assemblies"),
            Path.Combine(dmssrRoot, @"1.7\Assemblies"),
        };

        foreach (string dir in dirs)
        {
            if (dir.Length == 0)
            {
                continue;
            }

            string candidate = Path.Combine(dir, name.Name + ".dll");
            if (File.Exists(candidate))
            {
                return context.LoadFromAssemblyPath(candidate);
            }
        }

        return null;
    }
}
