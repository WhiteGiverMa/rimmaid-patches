using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Mono.Cecil;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args is ["--help"])
        {
            Console.WriteLine("Usage: Validation bind|red|run|ce GAME_ROOT WORKSHOP_ROOT MOD_ROOT MOD_DLL QA_ROOT");
            Console.WriteLine("Offline only. QA_ROOT must be outside the game, workshop and real save/config directories.");
            return 0;
        }
        if (args.Length != 6 || args[0] is not ("bind" or "red" or "run" or "ce"))
        {
            Console.Error.WriteLine("Expected a validation mode and five paths; use --help.");
            return 1;
        }
        try
        {
            string game = Path.GetFullPath(args[1]);
            string workshop = Path.GetFullPath(args[2]);
            string modRoot = Path.GetFullPath(args[3]);
            string patch = Path.GetFullPath(args[4]);
            string qa = Path.GetFullPath(args[5]);
            foreach (string protectedRoot in new[] { game, workshop, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + "Low" })
                if (qa.StartsWith(protectedRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("QA output must not be inside game/workshop/save directories.");
            string bound = Path.Combine(qa, "bound");
            string harmony = Path.Combine(workshop, "2009463077/1.7/Assemblies");
            AssemblyLoadContext.Default.Resolving += (context, name) =>
            {
                foreach (string directory in new[] { bound, game, harmony })
                {
                    string path = Path.Combine(directory, name.Name + ".dll");
                    if (File.Exists(path)) return context.LoadFromAssemblyPath(path);
                }
                return null;
            };
            if (args[0] == "bind") Bind(game, workshop, bound);
            else Run(args[0], game, workshop, modRoot, patch, qa);
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static void Bind(string game, string workshop, string bound)
    {
        Directory.CreateDirectory(bound);
        using var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(game);
        resolver.AddSearchDirectory(Path.Combine(workshop, "2009463077/1.7/Assemblies"));
        foreach (string source in new[]
        {
            Path.Combine(game, "Assembly-CSharp.dll"),
            Path.Combine(workshop, "3807251471/Assemblies/WeaponStation.dll"),
            Path.Combine(workshop, "3662471742/1.7/Assemblies/VFM_VanillaFireModes.dll")
        })
        {
            using var assembly = AssemblyDefinition.ReadAssembly(source, new ReaderParameters { AssemblyResolver = resolver });
            foreach (AssemblyNameReference reference in assembly.MainModule.AssemblyReferences)
            {
                if (!reference.Name.StartsWith("UnityEngine", StringComparison.Ordinal)) continue;
                reference.Name = "UnityStubs";
                reference.Version = new Version(1, 0, 0, 0);
            }
            if (assembly.Name.Name == "Assembly-CSharp")
                foreach (string name in new[] { "Verse.ModsConfig", "Verse.BaseContent", "RimWorld.Building_TurretGun" })
                    ClearInitializer(assembly.MainModule.GetType(name));
            if (assembly.Name.Name == "VFM_VanillaFireModes")
                ClearInitializer(assembly.MainModule.GetType("VFM_VanillaFireModes.Utilities.VFM_IconTexture"));
            assembly.Write(Path.Combine(bound, Path.GetFileName(source)));
        }
        Console.WriteLine("PASS: sandbox copies only; Unity references rebound, ModsConfig I/O and static texture/material loading disabled. Shooting/stat/comp/Scribe methods unchanged.");
    }

    private static void ClearInitializer(TypeDefinition type)
    {
        MethodDefinition method = type.Methods.Single(m => m.Name == ".cctor");
        method.Body.Instructions.Clear();
        method.Body.ExceptionHandlers.Clear();
        method.Body.Variables.Clear();
        method.Body.GetILProcessor().Emit(Mono.Cecil.Cil.OpCodes.Ret);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Run(string mode, string game, string workshop, string modRoot, string patch, string qa)
    {
        Assembly assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(patch);
        Driver.Run(mode, game, workshop, modRoot, assembly, qa);
    }
}
