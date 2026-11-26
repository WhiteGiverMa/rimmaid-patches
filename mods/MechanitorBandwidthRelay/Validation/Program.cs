using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class Program
{
    internal static readonly string GameRoot = Environment.GetEnvironmentVariable("RIMWORLD_ROOT")
        ?? @"A:\SteamLibrary\steamapps\common\RimWorld";
    private static readonly string HarmonyDirectory = Environment.GetEnvironmentVariable("RIMWORLD_HARMONY")
        ?? @"A:\SteamLibrary\steamapps\workshop\content\294100\2009463077\1.7\Assemblies";

    private static int Main(string[] args)
    {
        if (args is ["--help"])
        {
            Console.WriteLine("Usage: Validation [MOD_DIRECTORY]");
            Console.WriteLine("Drives the released mod DLL with an isolated copy of the installed game assembly.");
            return 0;
        }
        if (args.Length > 1 || (args.Length == 1 && args[0].StartsWith('-')))
        {
            Console.Error.WriteLine("Invalid arguments; use --help.");
            return 1;
        }

        string mod = Path.GetFullPath(args.Length == 1 ? args[0] : Path.Combine(AppContext.BaseDirectory, "../../../.."));
        AssemblyLoadContext.Default.Resolving += Resolve;
        string original = Path.Combine(GameRoot, "Assembly-CSharp.dll");
        using var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(GameRoot);
        using AssemblyDefinition game = AssemblyDefinition.ReadAssembly(original, new ReaderParameters { AssemblyResolver = resolver });
        // Keep mod discovery, config and always-on placeholder rendering out of the in-memory fixture.
        foreach (string type in new[] { "Verse.ModsConfig", "Verse.ModLister", "Verse.BaseContent", "Verse.Widgets" })
        {
            var initializer = game.MainModule.GetType(type).Methods.Single(method => method.Name == ".cctor");
            initializer.Body.Instructions.Clear();
            initializer.Body.ExceptionHandlers.Clear();
            initializer.Body.Variables.Clear();
            initializer.Body.GetILProcessor().Emit(OpCodes.Ret);
        }
        using var isolated = new MemoryStream();
        game.Write(isolated);
        isolated.Position = 0;
        AssemblyLoadContext.Default.LoadFromStream(isolated);

        string dll = Path.Combine(mod, "Assemblies", "MechanitorBandwidthRelay.dll");
        AssemblyLoadContext.Default.LoadFromAssemblyPath(dll);
        Console.WriteLine($"Assembly-CSharp SHA256: {Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(original)))}");
        Console.WriteLine($"Mod SHA256: {Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(dll)))}");
        PackageChecks.Run(mod);
        Drive(mod);
        return 0;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Drive(string mod) => Driver.Run(mod);

    private static Assembly? Resolve(AssemblyLoadContext context, AssemblyName name)
    {
        foreach (string directory in new[] { GameRoot, HarmonyDirectory })
        {
            string path = Path.Combine(directory, name.Name + ".dll");
            if (File.Exists(path)) return context.LoadFromAssemblyPath(path);
        }
        return null;
    }
}
