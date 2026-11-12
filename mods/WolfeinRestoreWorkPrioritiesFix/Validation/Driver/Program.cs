using System;
using System.IO;
using System.Reflection;

internal static class Program
{
    private const string GameRoot = @"A:\SteamLibrary\steamapps\common\RimWorld";
    private const string WolfeinPath = @"A:\SteamLibrary\steamapps\workshop\content\294100\3473140562\1.6\Assemblies\Wolfein.dll";

    private static int Main()
    {
        AppDomain.CurrentDomain.AssemblyResolve += (_, args) =>
        {
            string simpleName = new AssemblyName(args.Name).Name!;
            string candidate = Path.Combine(GameRoot, simpleName + ".dll");
            return File.Exists(candidate) ? Assembly.LoadFrom(candidate) : null;
        };

        _ = Assembly.LoadFrom(WolfeinPath);
        return Tests.Run();
    }
}
