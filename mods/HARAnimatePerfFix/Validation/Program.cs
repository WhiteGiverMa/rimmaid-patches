using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace HARAnimatePerfFix.Validation;

/// <summary>
/// Offline validator. Loads the real RimWorld 1.7 Assembly-CSharp.dll, the real UnityStubs.dll and the
/// real Workshop HARAnimate.dll / AlienRace.dll into a plain .NET host (no game loop, no Unity scene),
/// then runs either the unpatched baseline (red) or the mod-patched build (green).
///
/// Two things are deliberately isolated from the live installation:
///  * <c>Verse.ModsConfig</c>'s static constructor is emptied in a QA copy of Assembly-CSharp, so the
///    validator can never read or rewrite the user's ModsConfig.xml. The installed game DLL and every
///    Workshop DLL are only ever opened for reading.
///  * <c>UnityEngine*</c> assembly references are resolved to the game's own UnityStubs.dll, mirroring
///    the runtime redirect RimWorld 1.7 declares in RimWorld.runtimeconfig.json
///    ("UnityEngine*=UnityStubs").
/// </summary>
internal static class Program
{
    internal const string Owner = "whitegiver.haranimate.perffix";

    private static string gameRoot = "";
    private static string harmonyPath = "";
    private static string harAnimatePath = "";
    private static string alienRacePath = "";
    private static string patchPath = "";

    private static readonly string BoundDirectory = Path.Combine(AppContext.BaseDirectory, "bound");

    private static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--help")
        {
            Console.WriteLine("Usage:");
            Console.WriteLine("  Validation bind  GAME_ROOT HARMONY_DLL HARANIMATE_DLL ALIENRACE_DLL PATCH_DLL");
            Console.WriteLine("  Validation red   GAME_ROOT HARMONY_DLL HARANIMATE_DLL ALIENRACE_DLL PATCH_DLL");
            Console.WriteLine("  Validation green GAME_ROOT HARMONY_DLL HARANIMATE_DLL ALIENRACE_DLL PATCH_DLL");
            return 0;
        }

        if (args.Length != 6 || args[0] is not ("bind" or "red" or "green"))
        {
            Console.Error.WriteLine("Expected a mode (bind|red|green) and five assembly paths; use --help.");
            return 1;
        }

        string mode = args[0];
        gameRoot = Path.GetFullPath(args[1]);
        harmonyPath = Path.GetFullPath(args[2]);
        harAnimatePath = Path.GetFullPath(args[3]);
        alienRacePath = Path.GetFullPath(args[4]);
        patchPath = Path.GetFullPath(args[5]);

        try
        {
            if (mode == "bind")
            {
                Bind();
                return 0;
            }

            if (!File.Exists(Path.Combine(BoundDirectory, "Assembly-CSharp.dll")))
            {
                throw new InvalidOperationException("Bound game assemblies are missing; run the bind mode first.");
            }

            AssemblyLoadContext.Default.Resolving += Resolve;

            Console.WriteLine($"mode            : {mode}");
            Console.WriteLine($"game root       : {gameRoot}");
            Console.WriteLine($"game version    : {GameVersion()}");
            Console.WriteLine($"HARAnimate.dll  : {harAnimatePath} (sha256 {Hashing.Sha256(harAnimatePath)})");
            Console.WriteLine($"AlienRace.dll   : {alienRacePath} (sha256 {Hashing.Sha256(alienRacePath)})");
            Console.WriteLine($"patch dll       : {patchPath} (sha256 {Hashing.Sha256(patchPath)})");
            Console.WriteLine();

            LoadGameAssemblies();
            Run(mode);
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine("VALIDATION FAILED");
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    /// <summary>
    /// Writes a QA-only copy of the real game assembly with <c>Verse.ModsConfig</c>'s static constructor
    /// emptied. The installed DLL is never modified. Emptying it guarantees the validator cannot create,
    /// rewrite or "repair" the user's ModsConfig.xml, and that the mod list stays out of the picture.
    /// </summary>
    private static void Bind()
    {
        Directory.CreateDirectory(BoundDirectory);

        using DefaultAssemblyResolver resolver = new();
        resolver.AddSearchDirectory(gameRoot);
        resolver.AddSearchDirectory(Path.GetDirectoryName(harmonyPath)!);
        resolver.AddSearchDirectory(Path.GetDirectoryName(harAnimatePath)!);
        resolver.AddSearchDirectory(Path.GetDirectoryName(alienRacePath)!);

        string source = Path.Combine(gameRoot, "Assembly-CSharp.dll");
        string target = Path.Combine(BoundDirectory, "Assembly-CSharp.dll");

        using (AssemblyDefinition game = AssemblyDefinition.ReadAssembly(
                   source,
                   new ReaderParameters { AssemblyResolver = resolver }))
        {
            MethodDefinition initializer = game.MainModule
                    .GetType("Verse.ModsConfig")?
                    .Methods
                    .SingleOrDefault(method => method.Name == ".cctor")
                ?? throw new InvalidOperationException("Verse.ModsConfig..cctor was not found; bind step is stale.");

            initializer.Body.Instructions.Clear();
            initializer.Body.ExceptionHandlers.Clear();
            initializer.Body.Variables.Clear();
            initializer.Body.GetILProcessor().Emit(OpCodes.Ret);

            game.Write(target);
        }

        Console.WriteLine($"bound Assembly-CSharp -> {target}");
        Console.WriteLine("  Verse.ModsConfig..cctor emptied: ModsConfig.xml is never read or written by this harness.");
        Console.WriteLine($"  sha256 {Hashing.Sha256(target)}");

        BuildUnityEngineFacets(resolver);
    }

    /// <summary>
    /// HARAnimate (and many older Workshop DLLs) reference <c>UnityEngine.CoreModule</c>, an assembly that
    /// does not exist in RimWorld 1.7: the runtime rewrites every <c>UnityEngine*</c> reference to
    /// UnityStubs (<c>WaylithClr.AssemblyNameRedirects</c> in RimWorld.runtimeconfig.json).
    ///
    /// A plain .NET host rejects that trick (a resolver must return an assembly with the requested simple
    /// name), so this writes QA-only *type-forwarder* facets named after the original UnityEngine modules.
    /// Each forwarded type points at the real UnityStubs type, so the Workshop DLL loads byte-identical and
    /// every UnityEngine type keeps the exact same identity as the one Assembly-CSharp uses.
    /// </summary>
    private static void BuildUnityEngineFacets(DefaultAssemblyResolver resolver)
    {
        string unityStubsPath = Path.Combine(gameRoot, "UnityStubs.dll");
        AssemblyName stubsName = AssemblyName.GetAssemblyName(unityStubsPath);
        HashSet<string> stubsTypes = LoadTypeNames(unityStubsPath);

        AssemblyNameReference stubsReference = new(
            stubsName.Name!,
            stubsName.Version ?? new Version(0, 0, 0, 0))
        {
            PublicKeyToken = stubsName.GetPublicKeyToken(),
        };

        Dictionary<string, SortedSet<string>> byModule = new(StringComparer.Ordinal);

        using (AssemblyDefinition upstream = AssemblyDefinition.ReadAssembly(
                   harAnimatePath,
                   new ReaderParameters { AssemblyResolver = resolver }))
        {
            foreach (TypeReference reference in upstream.MainModule.GetTypeReferences())
            {
                AssemblyNameReference? scope = reference.Scope as AssemblyNameReference;
                if (scope is null || !scope.Name.StartsWith("UnityEngine", StringComparison.Ordinal))
                {
                    continue;
                }

                string fullName = reference.FullName;
                if (!byModule.TryGetValue(scope.Name, out SortedSet<string>? names))
                {
                    names = new SortedSet<string>(StringComparer.Ordinal);
                    byModule.Add(scope.Name, names);
                }

                names.Add(fullName);
            }
        }

        if (byModule.Count == 0)
        {
            Console.WriteLine("no UnityEngine module references in HARAnimate.dll; no facet needed");
            return;
        }

        foreach ((string moduleName, SortedSet<string> typeNames) in byModule)
        {
            AssemblyDefinition facet = AssemblyDefinition.CreateAssembly(
                new AssemblyNameDefinition(moduleName, new Version(0, 0, 0, 0)),
                moduleName,
                ModuleKind.Dll);

            facet.MainModule.AssemblyReferences.Add(stubsReference);

            foreach (string fullName in typeNames)
            {
                int lastDot = fullName.LastIndexOf('.');
                string ns = lastDot < 0 ? string.Empty : fullName.Substring(0, lastDot);
                string name = lastDot < 0 ? fullName : fullName.Substring(lastDot + 1);

                if (!stubsTypes.Contains(fullName))
                {
                    throw new InvalidOperationException(
                        $"HARAnimate references {moduleName}!{fullName}, which UnityStubs does not define; the facet would dangle.");
                }

                ExportedType forwarder = new(ns, name, facet.MainModule, stubsReference)
                {
                    Attributes = Mono.Cecil.TypeAttributes.Forwarder | Mono.Cecil.TypeAttributes.Public,
                };

                facet.MainModule.ExportedTypes.Add(forwarder);
            }

            string facetPath = Path.Combine(BoundDirectory, moduleName + ".dll");
            facet.Write(facetPath);
            Console.WriteLine(
                $"bound {moduleName} facet -> {facetPath} ({typeNames.Count} type forwarders to {stubsReference.Name} {stubsReference.Version})");
        }
    }

    private static HashSet<string> LoadTypeNames(string path)
    {
        HashSet<string> names = new(StringComparer.Ordinal);

        using AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(path);

        foreach (TypeDefinition type in assembly.MainModule.Types)
        {
            Collect(type, names);
        }

        return names;

        static void Collect(TypeDefinition type, HashSet<string> into)
        {
            into.Add(type.FullName.Replace('/', '+'));
            foreach (TypeDefinition nested in type.NestedTypes)
            {
                Collect(nested, into);
            }
        }
    }

    private static void LoadGameAssemblies()
    {
        AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(gameRoot, "UnityStubs.dll"));
        AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(BoundDirectory, "Assembly-CSharp.dll"));

        Assembly harAnimate = AssemblyLoadContext.Default.LoadFromAssemblyPath(harAnimatePath);
        Console.WriteLine(
            $"loaded HARAnimate {harAnimate.GetName().Version} from {harAnimate.Location}");

        try
        {
            Assembly alienRace = AssemblyLoadContext.Default.LoadFromAssemblyPath(alienRacePath);
            Type worker = alienRace.GetType("AlienRace.AlienPawnRenderNodeWorker_BodyAddon", throwOnError: true)!;
            bool hasOffset = worker.GetMethod(
                "OffsetFor",
                BindingFlags.Public | BindingFlags.Instance,
                new[] { typeof(Verse.PawnRenderNode), typeof(Verse.PawnDrawParms), typeof(UnityEngine.Vector3).MakeByRefType() }) is not null;
            bool hasScale = worker.GetMethod(
                "ScaleFor",
                BindingFlags.Public | BindingFlags.Instance,
                new[] { typeof(Verse.PawnRenderNode), typeof(Verse.PawnDrawParms) }) is not null;

            Tests.Check(hasOffset && hasScale, "real AlienRace body-addon worker lost OffsetFor/ScaleFor overrides");
            Console.WriteLine(
                $"loaded AlienRace {alienRace.GetName().Version}; AlienPawnRenderNodeWorker_BodyAddon overrides OffsetFor/ScaleFor");
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException("Failed to load the real AlienRace.dll", exception);
        }
    }

    private static void Run(string mode)
    {
        Assembly patch = AssemblyLoadContext.Default.LoadFromAssemblyPath(patchPath);

        if (mode == "red")
        {
            Tests.RunRed();
            return;
        }

        Type modType = patch.GetType("WhiteGiver.HARAnimatePerfFix.HARAnimatePerfFixMod", throwOnError: true)!;
        Tests.RunGreen(modType);
    }

    private static string GameVersion()
    {
        string versionFile = Path.Combine(gameRoot, "Version.txt");
        return File.Exists(versionFile) ? File.ReadAllText(versionFile).Trim() : "unknown";
    }

    private static Assembly? Resolve(AssemblyLoadContext context, AssemblyName name)
    {
        string? requested = name.Name;

        if (requested is null)
        {
            return null;
        }

        // RimWorld 1.7 redirects every UnityEngine* reference to UnityStubs
        // (WaylithClr.AssemblyNameRedirects in RimWorld.runtimeconfig.json). The bind step writes a
        // type-forwarder facet named after each original module, so prefer that file when it exists.
        string facet = Path.Combine(BoundDirectory, requested + ".dll");
        if (requested.StartsWith("UnityEngine", StringComparison.Ordinal) && File.Exists(facet))
        {
            return Load(facet);
        }

        if (requested.StartsWith("UnityEngine", StringComparison.Ordinal))
        {
            return Load(Path.Combine(gameRoot, "UnityStubs.dll"));
        }

        foreach (Assembly loaded in AssemblyLoadContext.Default.Assemblies)
        {
            if (string.Equals(loaded.GetName().Name, requested, StringComparison.Ordinal))
            {
                return loaded;
            }
        }

        string[] directories =
        {
            BoundDirectory,
            gameRoot,
            Path.GetDirectoryName(patchPath) ?? "",
            Path.GetDirectoryName(harAnimatePath) ?? "",
            Path.GetDirectoryName(alienRacePath) ?? "",
            Path.GetDirectoryName(harmonyPath) ?? "",
        };

        foreach (string directory in directories)
        {
            if (directory.Length == 0)
            {
                continue;
            }

            string candidate = Path.Combine(directory, requested + ".dll");
            if (File.Exists(candidate))
            {
                return Load(candidate);
            }
        }

        return null;
    }

    private static Assembly Load(string path)
    {
        foreach (Assembly loaded in AssemblyLoadContext.Default.Assemblies)
        {
            if (string.Equals(loaded.Location, path, StringComparison.OrdinalIgnoreCase))
            {
                return loaded;
            }
        }

        return AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
    }
}

internal static class Hashing
{
    internal static string Sha256(string path)
    {
        using FileStream stream = File.OpenRead(path);
        using System.Security.Cryptography.SHA256 sha = System.Security.Cryptography.SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
    }
}
