using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args is not [var mod, var game, var harmony, var asel, var scratch])
            throw new ArgumentException("Expected mod directory, game directory, Harmony DLL, ASEL DLL, and scratch directory.");

        string gameDll = Path.Combine(game, "Assembly-CSharp.dll");
        string patchDll = Path.Combine(mod, "1.7", "Assemblies", "MonolynFemaleGay.dll");
        string[] directories = [game, Path.GetDirectoryName(harmony)!, Path.GetDirectoryName(asel)!];
        var context = AssemblyLoadContext.Default;
        context.Resolving += (ctx, name) => directories.Select(d => Path.Combine(d, name.Name + ".dll"))
            .FirstOrDefault(File.Exists) is { } resolved ? ctx.LoadFromAssemblyPath(Path.GetFullPath(resolved)) : null;

        // Like the other repository drivers, sandbox only startup/environment entry points in memory.
        // No changes to upstream DLLs on disk and no stubs for gene or trait behavior.
        using var resolver = new DefaultAssemblyResolver();
        foreach (string directory in directories) resolver.AddSearchDirectory(directory);
        resolver.AddSearchDirectory(Path.GetDirectoryName(typeof(object).Assembly.Location)!);
        using var assembly = AssemblyDefinition.ReadAssembly(gameDll, new ReaderParameters { AssemblyResolver = resolver });
        foreach (string name in new[] { "Verse.ModsConfig", "Verse.ModLister", "Verse.BaseContent", "Verse.Widgets" })
        {
            var ctor = assembly.MainModule.GetType(name).Methods.Single(m => m.IsConstructor && m.IsStatic);
            ctor.Body = new Mono.Cecil.Cil.MethodBody(ctor);
            ctor.Body.GetILProcessor().Emit(OpCodes.Ret);
        }

        using var isolatedGame = new MemoryStream();
        assembly.Write(isolatedGame);
        isolatedGame.Position = 0;
        context.LoadFromStream(isolatedGame);
        context.LoadFromAssemblyPath(Path.GetFullPath(harmony));
        context.LoadFromAssemblyPath(Path.GetFullPath(asel));
        Assembly patch = context.LoadFromAssemblyPath(Path.GetFullPath(patchDll));
        foreach (string name in new[] { gameDll, harmony, asel, patchDll })
            Console.WriteLine($"{Path.GetFileName(name)} SHA256: {Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(name)))}");

        Run(mod, game, asel, scratch, patch);
        return 0;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Run(string mod, string game, string asel, string scratch, Assembly patch)
        => Driver.Run(mod, game, asel, scratch, patch);
}
