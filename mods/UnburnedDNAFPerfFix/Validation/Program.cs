using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Mono.Cecil;

namespace Validation;

internal static class Program
{
    internal const string Game = @"A:\SteamLibrary\steamapps\common\RimWorld";
    internal const string Workshop = @"A:\SteamLibrary\steamapps\workshop\content\294100";
    internal const string Logs = @"C:\Users\A1337\AppData\Local\Temp\opencode\unburned-dnaf-perf-20261114";
    internal static readonly string Bound = Path.Combine(AppContext.BaseDirectory, "bound");
    internal static Assembly Unburned = null!;
    internal static Assembly? Dnaf;

    private static int Main(string[] args)
    {
        try
        {
            Directory.CreateDirectory(Logs);
            if (args.Contains("bind")) { Bind(); return 0; }
            AssemblyLoadContext.Default.Resolving += Resolve;
            AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(Game, "UnityStubs.dll"));
            AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(Bound, "Assembly-CSharp.dll"));
            Unburned = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(Bound, "FireIsNotMyNightmare.dll"));
            if (!args.Contains("--no-dnaf"))
                Dnaf = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(Bound, "DoNotAvoidFire.dll"));
            return Run(args);
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Run(string[] args) => Audit.Run(args);

    private static Assembly? Resolve(AssemblyLoadContext context, AssemblyName name)
    {
        foreach (string dir in new[] { Bound, Path.Combine(Workshop, "2009463077/1.7/Assemblies"), Game, AppContext.BaseDirectory, Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../1.7/Assemblies")) })
        {
            string p = Path.Combine(dir, name.Name + ".dll");
            if (File.Exists(p)) return context.LoadFromAssemblyPath(p);
        }
        return null;
    }

    private static void Bind()
    {
        Directory.CreateDirectory(Bound);
        File.Copy(@"G:\dev\rimmaid-patches\mods\DMSEWorldPerfFix\Validation\bin\Release\net10.0\bound\Assembly-CSharp.dll", Path.Combine(Bound, "Assembly-CSharp.dll"), true);
        using (var real = AssemblyDefinition.ReadAssembly(Path.Combine(Game, "Assembly-CSharp.dll")))
        using (var safe = AssemblyDefinition.ReadAssembly(Path.Combine(Bound, "Assembly-CSharp.dll")))
        {
            foreach (string type in new[] { "RimWorld.GenHostility", "Verse.AI.AttackTargetsCache", "Verse.AI.JobDriver_Wait", "Verse.PathFinder", "Verse.PathGridJob", "Verse.AI.PathGrid", "Verse.PersistentDangerSource", "RimWorld.Fire" })
            {
                TypeDefinition rt = real.MainModule.GetType(type), st = safe.MainModule.GetType(type);
                foreach (MethodDefinition m in rt.Methods.Where(m => m.HasBody))
                {
                    MethodDefinition other = st.Methods.Single(x => x.FullName == m.FullName);
                    string left = string.Join("\n", m.Body.Instructions.Select(i => i.ToString()));
                    string right = string.Join("\n", other.Body.Instructions.Select(i => i.ToString()));
                    if (left != right) throw new Exception("Bound IL differs: " + m.FullName);
                }
                Console.WriteLine("Bound vs installed IL identical: " + type);
            }
            MethodDefinition cctor = safe.MainModule.GetType("Verse.ModsConfig").Methods.Single(m => m.Name == ".cctor");
            if (cctor.Body.Instructions.Count != 1 || cctor.Body.Instructions[0].OpCode != Mono.Cecil.Cil.OpCodes.Ret)
                throw new Exception("ModsConfig binding is not config-safe");
        }
        foreach (string path in new[] { "3538336699/1.7/Assemblies/FireIsNotMyNightmare.dll", "3303359387/1.6/Assemblies/DoNotAvoidFire.dll" })
        {
            using AssemblyDefinition a = AssemblyDefinition.ReadAssembly(Path.Combine(Workshop, path));
            foreach (AssemblyNameReference r in a.MainModule.AssemblyReferences)
                if (r.Name.StartsWith("Unity", StringComparison.Ordinal)) { r.Name = "UnityStubs"; r.Version = new Version(1, 0, 0, 0); }
            a.Write(Path.Combine(Bound, Path.GetFileName(path)));
            Console.WriteLine("QA binding only (IL unchanged): " + path);
        }
    }
}
