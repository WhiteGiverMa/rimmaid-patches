using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using Mono.Cecil;

namespace DMSSRScourgeDeathFix.Validation;

internal static class Program
{
    internal static string PatchPath = "";
    internal static string WorkshopRoot = "";
    private static string gameRoot = "";
    private static string boundGame = "";

    private static int Main(string[] args)
    {
        if (args.Length != 5 || args[0] is not ("red" or "green" or "green-first"))
        {
            Console.Error.WriteLine("Validation red|green|green-first BOUND_GAME_DLL GAME_ROOT WORKSHOP_ROOT PATCH_DLL");
            return 1;
        }

        boundGame = Path.GetFullPath(args[1]);
        gameRoot = Path.GetFullPath(args[2]);
        WorkshopRoot = Path.GetFullPath(args[3]);
        PatchPath = Path.GetFullPath(args[4]);
        try
        {
            Audit.Run(boundGame, gameRoot, WorkshopRoot, PatchPath);
            AssemblyLoadContext.Default.Resolving += Resolve;
            foreach (string path in new[]
            {
                Path.Combine(gameRoot, "UnityStubs.dll"), boundGame,
                ModAssembly("2009463077", "0Harmony"),
                ModAssembly("3498575851", "Fortified"),
                ModAssembly("3835512735", "DMSSR"),
            })
            {
                using AssemblyDefinition definition = AssemblyDefinition.ReadAssembly(path);
                string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
                Console.WriteLine($"INPUT {definition.Name.Name} MVID={definition.MainModule.Mvid} SHA256={hash} PATH={path}");
                AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
            }

            Run(args[0]);
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Run(string mode) => Tests.Run(mode);

    private static string ModAssembly(string id, string name) =>
        Path.Combine(WorkshopRoot, id, "1.7", "Assemblies", name + ".dll");

    private static Assembly? Resolve(AssemblyLoadContext context, AssemblyName name)
    {
        foreach (string directory in new[]
        {
            Path.GetDirectoryName(boundGame)!, gameRoot,
            Path.GetDirectoryName(ModAssembly("2009463077", "0Harmony"))!,
            Path.GetDirectoryName(ModAssembly("3498575851", "Fortified"))!,
            Path.GetDirectoryName(ModAssembly("3835512735", "DMSSR"))!,
            Path.GetDirectoryName(PatchPath)!,
        })
        {
            string path = Path.Combine(directory, name.Name + ".dll");
            if (File.Exists(path)) return context.LoadFromAssemblyPath(path);
        }
        return null;
    }
}
