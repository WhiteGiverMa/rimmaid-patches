using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using DMSEMissileBaseFixValidation;

string gameRoot = @"A:\SteamLibrary\steamapps\common\RimWorld";
string workshopRoot = @"A:\SteamLibrary\steamapps\workshop\content\294100";
string dmseRoot = Path.Combine(workshopRoot, @"3689879335\1.7");
string modRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, @"..\..\..\.."));
string harmonyDll = Path.Combine(workshopRoot, @"2009463077\1.7\Assemblies\0Harmony.dll");
string dmseDll = Path.Combine(dmseRoot, @"Assemblies\DMSE.dll");
string dmsDll = Path.Combine(workshopRoot, @"3819799478\1.7\Assemblies\DMS.dll");
string fffDll = Path.Combine(workshopRoot, @"3498575851\1.7\Assemblies\Fortified.dll");
string fixDll = Path.Combine(modRoot, @"1.7\Assemblies\DMSEMissileBaseFix.dll");
string boundDir = @"G:\dev\rimmaid-patches\mods\DMSEWorldPerfFix\Validation\bin\Release\net10.0\bound";

if (args.Length > 1)
{
    Console.Error.WriteLine("Usage: Validation [red|green]");
    return 2;
}

string mode = args.Length == 0 ? "green" : args[0].ToLowerInvariant();
if (mode is not ("red" or "green"))
{
    Console.Error.WriteLine("Mode must be 'red' or 'green'.");
    return 2;
}

foreach (string required in new[]
{
    Path.Combine(boundDir, "Assembly-CSharp.dll"),
    Path.Combine(gameRoot, "UnityStubs.dll"),
    harmonyDll,
    dmseDll,
    dmsDll,
    fffDll,
    fixDll,
})
{
    if (!File.Exists(required))
    {
        Console.Error.WriteLine("Required real/bound assembly is missing: " + required);
        return 3;
    }
}

Directory.SetCurrentDirectory(gameRoot);
AssemblyLoadContext.Default.Resolving += Resolve;
Tests.Run(mode, dmseRoot, modRoot);
return 0;

Assembly? Resolve(AssemblyLoadContext context, AssemblyName name)
{
    string n = name.Name ?? string.Empty;
    if (n == "Assembly-CSharp")
    {
        return context.LoadFromAssemblyPath(Path.Combine(boundDir, "Assembly-CSharp.dll"));
    }

    if (n.StartsWith("UnityEngine", StringComparison.Ordinal))
    {
        string forwarded = Path.Combine(boundDir, n + ".dll");
        if (File.Exists(forwarded))
        {
            return context.LoadFromAssemblyPath(forwarded);
        }
    }

    string mapped = n switch
    {
        "UnityStubs" => Path.Combine(gameRoot, "UnityStubs.dll"),
        "0Harmony" => harmonyDll,
        "DMSE" => dmseDll,
        "DMS" => dmsDll,
        "Fortified" => fffDll,
        "DMSEMissileBaseFix" => fixDll,
        _ => Path.Combine(gameRoot, n + ".dll"),
    };
    return File.Exists(mapped) ? context.LoadFromAssemblyPath(mapped) : null;
}
