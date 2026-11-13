using System;
using System.IO;
using System.Reflection;

internal static class Program
{
    internal const string GameRoot = @"A:\SteamLibrary\steamapps\common\RimWorld";
    internal const string HarmonyPath = @"A:\SteamLibrary\steamapps\workshop\content\294100\2009463077\1.7\Assemblies\0Harmony.dll";
    internal const string LegionPath = @"A:\SteamLibrary\steamapps\workshop\content\294100\3666117000\1.7\Assemblies\DMS_Legion.dll";

    private static int Main()
    {
        AppDomain.CurrentDomain.AssemblyResolve += Resolve;

        Assembly.LoadFrom(Path.Combine(GameRoot, "UnityStubs.dll"));
        Assembly.LoadFrom(Path.Combine(GameRoot, "Assembly-CSharp.dll"));

        if (!File.Exists(Path.Combine(AppContext.BaseDirectory, "DMS_Legion.dll")))
        {
            Console.Error.WriteLine("DMS_Legion.dll was not copied to the validation output directory. Run the build again.");
            return 2;
        }

        return Tests.Run();
    }

    private static Assembly? Resolve(object? sender, ResolveEventArgs args)
    {
        string simpleName = new AssemblyName(args.Name).Name ?? string.Empty;

        foreach (Assembly loaded in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (string.Equals(loaded.GetName().Name, simpleName, StringComparison.Ordinal))
            {
                return loaded;
            }
        }

        string[] directories =
        {
            AppContext.BaseDirectory,
            GameRoot,
            Path.GetDirectoryName(HarmonyPath) ?? string.Empty,
            Path.GetDirectoryName(LegionPath) ?? string.Empty,
        };

        foreach (string directory in directories)
        {
            if (directory.Length == 0)
            {
                continue;
            }

            string candidate = Path.Combine(directory, simpleName + ".dll");
            if (File.Exists(candidate))
            {
                return Assembly.LoadFrom(candidate);
            }
        }

        return null;
    }
}
