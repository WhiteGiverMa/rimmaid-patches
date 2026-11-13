using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class Bootstrap
{
    internal static readonly string GameRoot = Environment.GetEnvironmentVariable("RIMWORLD_ROOT")
        ?? @"A:\SteamLibrary\steamapps\common\RimWorld";
    internal static readonly string HarmonyDirectory = Environment.GetEnvironmentVariable("HARMONY_DIRECTORY")
        ?? @"A:\SteamLibrary\steamapps\workshop\content\294100\2009463077\1.7\Assemblies";
    internal static readonly string UfliDll = Environment.GetEnvironmentVariable("UFLI_DLL")
        ?? @"A:\SteamLibrary\steamapps\workshop\content\294100\3651799915\1.7\Assemblies\UFRecipes.dll";
    internal static readonly string IwbDll = Environment.GetEnvironmentVariable("IWB_DLL")
        ?? @"A:\SteamLibrary\steamapps\workshop\content\294100\935982361\Assemblies\ImprovedWorkbenches.dll";
    internal static string Bound = "";

    [ModuleInitializer]
    internal static void Init() => AssemblyLoadContext.Default.Resolving += Resolve;

    private static Assembly? Resolve(AssemblyLoadContext context, AssemblyName name)
    {
        Assembly? loaded = context.Assemblies.FirstOrDefault(item => item.GetName().Name == name.Name);
        if (loaded != null) return loaded;
        foreach (string directory in new[] { Bound, GameRoot, HarmonyDirectory,
                     Path.GetDirectoryName(UfliDll)!, Path.GetDirectoryName(IwbDll)! })
        {
            string candidate = Path.Combine(directory, name.Name + ".dll");
            if (File.Exists(candidate)) return context.LoadFromAssemblyPath(Path.GetFullPath(candidate));
        }
        return null;
    }
}

internal static class Entry
{
    private static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--help")
        {
            Console.WriteLine("Usage: Validation bind|red|green|absent QA_DIRECTORY [PATCH_DLL]");
            Console.WriteLine("QA-only copies; no game process, installed DLL, user config or save is changed.");
            return 0;
        }
        if (args.Length is < 2 or > 3 || args[0] is not ("bind" or "red" or "green" or "absent"))
        {
            Console.Error.WriteLine("Invalid arguments; use --help.");
            return 1;
        }
        Bootstrap.Bound = Path.Combine(Path.GetFullPath(args[1]), "bound");
        try
        {
            if (args[0] == "bind") Bind();
            else typeof(Entry).Assembly.GetType("Driver", true)!.GetMethod("Run")!
                .Invoke(null, new object[] { args[0], args.Length == 3 ? Path.GetFullPath(args[2]) : "" });
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
        string[] sources = { Path.Combine(Bootstrap.GameRoot, "Assembly-CSharp.dll"), Bootstrap.UfliDll, Bootstrap.IwbDll };
        foreach (string protectedDirectory in sources.Select(path => Path.GetDirectoryName(path)!))
            if (Bootstrap.Bound.StartsWith(Path.GetFullPath(protectedDirectory) + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("QA output must be outside installed game/mod directories.");
        Directory.CreateDirectory(Bootstrap.Bound);
        using var resolver = new DefaultAssemblyResolver();
        foreach (string directory in sources.Select(path => Path.GetDirectoryName(path)!).Append(Bootstrap.HarmonyDirectory))
            resolver.AddSearchDirectory(directory);
        foreach (string source in sources)
        {
            using AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(source,
                new ReaderParameters { AssemblyResolver = resolver });
            foreach (AssemblyNameReference reference in assembly.MainModule.AssemblyReferences)
            {
                if (!reference.Name.StartsWith("UnityEngine", StringComparison.Ordinal)) continue;
                reference.Name = "UnityStubs";
                reference.Version = new Version(1, 0, 0, 0);
            }
            if (assembly.Name.Name == "Assembly-CSharp")
            {
                // QA-only: never let ModsConfig initialize or rewrite the player's configuration.
                MethodDefinition initializer = assembly.MainModule.GetType("Verse.ModsConfig").Methods
                    .Single(method => method.Name == ".cctor");
                initializer.Body.Instructions.Clear();
                initializer.Body.ExceptionHandlers.Clear();
                initializer.Body.Variables.Clear();
                initializer.Body.GetILProcessor().Emit(OpCodes.Ret);
            }
            assembly.Write(Path.Combine(Bootstrap.Bound, Path.GetFileName(source)));
            Console.WriteLine($"SOURCE {Path.GetFileName(source)} SHA256={Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source)))}");
        }
        Console.WriteLine("PASS: QA-only Unity reference rebinding and ModsConfig initialization isolation. Clipboard methods unchanged.");
    }
}
