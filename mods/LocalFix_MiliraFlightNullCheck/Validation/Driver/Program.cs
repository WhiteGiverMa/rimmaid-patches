using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace LocalFix.MiliraFlightNullCheck.Validation
{
    /// <summary>
    /// Offline validation driver for LocalFix_MiliraFlightNullCheck.
    ///
    /// Loads the real RimWorld and Milira assemblies into a throwaway .NET Framework process,
    /// uses Harmony exactly the way the game does, and asserts the fix. It never starts
    /// RimWorld, never writes into the game directory and never touches a save.
    ///
    ///   red     - register Milira's upstream prefix and show it NREs on a Milira-body pawn
    ///             without CompFlightControl (the failure chain this patch exists for)
    ///   inspect - check that a patch DLL contains a [StaticConstructorOnStartup] bootstrap
    ///             plus its [HarmonyPatch] class (the pre-fix DLL has neither)
    ///   green   - register Milira first, then run the DLL's bootstrap; assert exactly-once
    ///             registration, priority ordering and the crash-free result
    /// </summary>
    internal static class Program
    {
        private static readonly List<string> ProbeDirectories = new List<string>();

        private static int Main(string[] args)
        {
            // Must run before any method that references HarmonyLib is JIT-compiled.
            AppDomain.CurrentDomain.AssemblyResolve += ResolveAssembly;

            if (args.Length < 2)
            {
                PrintUsage();
                return 2;
            }

            string mode = args[0].ToLowerInvariant();
            try
            {
                // Probe directories must exist before the validation methods are JIT-compiled,
                // because their code resolves HarmonyLib through the AssemblyResolve handler.
                switch (mode)
                {
                    case "red":
                        AddProbeDirectory(args[1]);
                        AddProbeDirectory(Path.GetDirectoryName(Path.GetFullPath(args[2])));
                        AddProbeDirectory(Path.GetDirectoryName(Path.GetFullPath(args[3])));
                        return Validation.RunRed(args[1], args[2], args[3]);
                    case "inspect":
                    case "green":
                        AddProbeDirectory(Path.GetDirectoryName(Path.GetFullPath(args[1])));
                        AddProbeDirectory(args[2]);
                        AddProbeDirectory(Path.GetDirectoryName(Path.GetFullPath(args[3])));
                        AddProbeDirectory(Path.GetDirectoryName(Path.GetFullPath(args[4])));
                        return mode == "inspect"
                            ? Validation.RunInspect(args[1], args[2], args[3], args[4])
                            : Validation.RunGreen(args[1], args[2], args[3], args[4]);
                    default:
                        PrintUsage();
                        return 2;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("ERROR: " + ex);
                return 2;
            }
        }

        internal static Assembly LoadAssemblyFrom(string path)
        {
            return Assembly.LoadFrom(Path.GetFullPath(path));
        }

        internal static void AddProbeDirectory(string directory)
        {
            string full = Path.GetFullPath(directory);
            if (!ProbeDirectories.Contains(full))
            {
                ProbeDirectories.Add(full);
            }
        }

        private static Assembly ResolveAssembly(object sender, ResolveEventArgs args)
        {
            string simpleName = new AssemblyName(args.Name).Name;
            foreach (string directory in ProbeDirectories)
            {
                string candidate = Path.Combine(directory, simpleName + ".dll");
                if (File.Exists(candidate))
                {
                    return Assembly.LoadFrom(candidate);
                }
            }
            return null;
        }

        private static void PrintUsage()
        {
            Console.WriteLine("Usage:");
            Console.WriteLine("  red     <managedDir> <0Harmony.dll> <Milira.dll>");
            Console.WriteLine("  inspect <patch.dll>  <managedDir> <0Harmony.dll> <Milira.dll>");
            Console.WriteLine("  green   <patch.dll>  <managedDir> <0Harmony.dll> <Milira.dll>");
        }
    }
}
