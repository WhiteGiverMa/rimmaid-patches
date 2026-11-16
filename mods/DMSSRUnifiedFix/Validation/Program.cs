using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace DMSSRUnifiedFix.Validation;

internal static class Program
{
    internal const string Owner = "whitegiver.dmssr.unifiedfix";
    internal const string ValidationOwner = Owner + ".validation";
    internal static string PatchPath = "";
    internal static string WorkshopRoot = "";
    internal static string GameRoot = "";
    internal static string ModRoot = "";
    private static string BoundGame => Path.Combine(AppContext.BaseDirectory, "bound", "Assembly-CSharp.dll");

    private static int Main(string[] args)
    {
        if (args.Length != 5) { Console.Error.WriteLine("Validation MODE GAME_ROOT WORKSHOP_ROOT PATCH_DLL MOD_ROOT"); return 1; }
        GameRoot = Path.GetFullPath(args[1]);
        WorkshopRoot = Path.GetFullPath(args[2]);
        PatchPath = Path.GetFullPath(args[3]);
        ModRoot = Path.GetFullPath(args[4]);
        try
        {
            if (args[0] == "bind") { Bind(); return 0; }
            AssemblyLoadContext.Default.Resolving += Resolve;
            foreach (string file in new[] { Path.Combine(GameRoot, "UnityStubs.dll"), BoundGame,
                ModAssembly("2009463077", "0Harmony"), ModAssembly("3498575851", "Fortified"),
                ModAssembly("3819799478", "DMS"), ModAssembly("3835512735", "DMSSR") })
                AssemblyLoadContext.Default.LoadFromAssemblyPath(file);
            if (args[0] != "no-legion") AssemblyLoadContext.Default.LoadFromAssemblyPath(ModAssembly("3666117000", "DMS_Legion"));
            Assembly patch = AssemblyLoadContext.Default.LoadFromAssemblyPath(PatchPath);
            if (Hash(patch.Location) != Hash(PatchPath)) throw new InvalidOperationException("Runtime patch bytes mismatch.");
            Console.WriteLine("RUNTIME SHA256=" + Hash(patch.Location));
            Run(args[0], patch);
            return 0;
        }
        catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Run(string mode, Assembly patch)
    {
        LogCapture.Install();
        StatPathPerf.UpstreamPatches.PrepareBootstrapScope();
        Type mod = patch.GetType("WhiteGiver.DMSSRUnifiedFix.DMSSRUnifiedFixMod", true)!;
        string dms = Path.Combine(WorkshopRoot, "3819799478");
        string dmssr = Path.Combine(WorkshopRoot, "3835512735");
        if (mode.StartsWith("stat-", StringComparison.Ordinal)) StatPathPerf.Tests.Run(mode[5..], mod);
        else if (mode.StartsWith("legion-", StringComparison.Ordinal))
            LegionCompat.Tests.Run(mode[7..], mod, GameRoot, Path.Combine(WorkshopRoot, "3666117000"), dms, Path.Combine(WorkshopRoot, "3469398006"));
        else if (mode.StartsWith("neuro-", StringComparison.Ordinal)) Neuroglue.Tests.Run(mode[6..], patch, dms, dmssr, ModRoot);
        else if (mode.StartsWith("death-", StringComparison.Ordinal))
        {
            ScourgeDeath.Audit.Run(BoundGame, GameRoot, WorkshopRoot, PatchPath);
            ScourgeDeath.Tests.Run(mode[6..]);
        }
        else if (mode.StartsWith("carry-", StringComparison.Ordinal)) StasisPodCorpse.Tests.Run(mode[6..]);
        else if (mode.StartsWith("audited-", StringComparison.Ordinal)) Audited.Tests.Run(mode[8..]);
        else if (mode == "no-legion") StasisPodCorpse.Tests.NoLegion();
        else if (mode == "composition") StasisPodCorpse.Tests.Composition();
        else if (mode == "isolation") StasisPodCorpse.Tests.Isolation();
        else throw new ArgumentException("Unknown mode: " + mode);
        if (!mode.EndsWith("red", StringComparison.Ordinal)) LogCapture.AssertClean();
    }

    private static void Bind()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(BoundGame)!);
        using DefaultAssemblyResolver resolver = new();
        resolver.AddSearchDirectory(GameRoot);
        using AssemblyDefinition game = AssemblyDefinition.ReadAssembly(Path.Combine(GameRoot, "Assembly-CSharp.dll"), new ReaderParameters { AssemblyResolver = resolver });
        foreach (string type in new[] { "Verse.ModsConfig", "Verse.Prefs" })
        {
            MethodDefinition? cctor = game.MainModule.GetType(type)?.Methods.SingleOrDefault(m => m.Name == ".cctor");
            if (cctor == null) continue;
            cctor.Body.Instructions.Clear(); cctor.Body.ExceptionHandlers.Clear(); cctor.Body.Variables.Clear();
            cctor.Body.GetILProcessor().Emit(OpCodes.Ret);
        }
        game.Write(BoundGame);
        Console.WriteLine("Bound real game assembly: only ModsConfig/Prefs initializers disabled; no user-config IO.");
        foreach (string file in new[] { Path.Combine(GameRoot,"Assembly-CSharp.dll"), BoundGame,
            ModAssembly("2009463077","0Harmony"), ModAssembly("3819799478","DMS"),
            ModAssembly("3498575851","Fortified"), ModAssembly("3666117000","DMS_Legion"), ModAssembly("3835512735","DMSSR"), PatchPath })
            Console.WriteLine("INPUT SHA256=" + Hash(file) + " PATH=" + file);
        StasisPodCorpse.CarryAudit.Run(Path.Combine(GameRoot,"Assembly-CSharp.dll"), BoundGame, ModAssembly("3835512735","DMSSR"));
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private static string ModAssembly(string id, string name) => Path.Combine(WorkshopRoot,id,"1.7","Assemblies",name+".dll");
    private static Assembly? Resolve(AssemblyLoadContext context, AssemblyName name)
    {
        foreach (Assembly loaded in context.Assemblies) if (loaded.GetName().Name == name.Name) return loaded;
        foreach (string directory in new[] { Path.GetDirectoryName(BoundGame)!,GameRoot,Path.GetDirectoryName(PatchPath)!,
            Path.GetDirectoryName(ModAssembly("2009463077","0Harmony"))!, Path.GetDirectoryName(ModAssembly("3819799478","DMS"))!,
            Path.GetDirectoryName(ModAssembly("3498575851","Fortified"))!, Path.GetDirectoryName(ModAssembly("3835512735","DMSSR"))! })
        {
            string file=Path.Combine(directory,name.Name+".dll");
            if(File.Exists(file)) return context.LoadFromAssemblyPath(file);
        }
        return null;
    }
}
