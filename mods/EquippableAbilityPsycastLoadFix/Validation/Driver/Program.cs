using System;
using System.IO;
using System.Reflection;

internal static class Program
{
    private const string GameRoot = @"A:\SteamLibrary\steamapps\common\RimWorld";

    private static int Main()
    {
        AppDomain.CurrentDomain.AssemblyResolve += (_, args) =>
        {
            string simpleName = new AssemblyName(args.Name).Name!;
            foreach (string? root in new[] { Environment.GetEnvironmentVariable("RW_VALIDATION_REFS"), GameRoot })
            {
                if (root == null) continue;
                string candidate = Path.Combine(root, simpleName + ".dll");
                if (File.Exists(candidate)) return Assembly.LoadFrom(candidate);
            }
            return null;
        };

        _ = Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, "EquippableAbilityPsycastLoadFix.dll"));
        return Tests.Run();
    }
}
