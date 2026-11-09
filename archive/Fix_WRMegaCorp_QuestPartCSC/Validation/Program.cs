using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using RimWorld;
using Verse;
using WRMegaCorp;

namespace Fix_WRMegaCorp_QuestPartCSC.Validation;

internal static class Program
{
    private static int Main()
    {
        string gameRoot = Environment.GetEnvironmentVariable("RIMWORLD_HOME")
            ?? @"A:\SteamLibrary\steamapps\common\RimWorld";
        string workshopRoot = Environment.GetEnvironmentVariable("RIMWORLD_WORKSHOP")
            ?? @"A:\SteamLibrary\steamapps\workshop\content\294100";

        string[] probeDirs =
        {
            gameRoot,
            Path.Combine(workshopRoot, "3687841204", "1.6", "Assemblies"),
            Path.Combine(workshopRoot, "2009463077", "1.7", "Assemblies"),
        };

        AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            foreach (string dir in probeDirs)
            {
                string candidate = Path.Combine(dir, name.Name + ".dll");
                if (File.Exists(candidate))
                    return context.LoadFromAssemblyPath(candidate);
            }

            return null;
        };

        try
        {
            return Run();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL: unexpected exception: " + ex);
            return 1;
        }
    }

    private static int Run()
    {
        // 1. QuestPart_CSC 必须没有无参构造，否则根因判断不成立。
        Type type = typeof(QuestPart_CSC);
        ConstructorInfo? parameterless = type.GetConstructor(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            Type.EmptyTypes,
            modifiers: null);
        if (parameterless != null)
            return Fail("QuestPart_CSC already has a parameterless constructor; patch is unnecessary.");

        MethodInfo? createInstance = typeof(ScribeExtractor).GetMethod(
            "CreateInstance",
            BindingFlags.NonPublic | BindingFlags.Static);
        if (createInstance == null)
            return Fail("ScribeExtractor.CreateInstance not found.");

        MethodInfo? prefix = typeof(QuestPartCscLoadFix).GetMethod(nameof(QuestPartCscLoadFix.CreateInstance_Prefix));
        if (prefix == null)
            return Fail("QuestPartCscLoadFix.CreateInstance_Prefix not found.");

        // 2. RED：1.7 用空 ctorArgs 实例化时，当前 Mod 的构造函数匹配失败。
        object[] emptyCtorArgs = Array.Empty<object>();
        try
        {
            createInstance.Invoke(null, new object[] { type, emptyCtorArgs });
            return Fail("Empty ctorArgs unexpectedly created QuestPart_CSC.");
        }
        catch (TargetInvocationException ex) when (ex.InnerException is MissingMethodException)
        {
            Console.WriteLine("RED  ok: empty ctorArgs -> MissingMethodException.");
        }

        // 3. GREEN：补丁的 Prefix 直接走真实五参数构造函数。
        IExposable createdResult = null!;
        bool runOriginal = QuestPartCscLoadFix.CreateInstance_Prefix(type, emptyCtorArgs, ref createdResult);
        if (runOriginal)
            return Fail("Prefix did not skip the original CreateInstance for QuestPart_CSC.");

        if (createdResult is not QuestPart_CSC csc)
            return Fail("Prefix result is not QuestPart_CSC.");

        if (csc.shipChunkCount != 0 || csc.signalTag != null || csc.map != null || csc.quest != null)
            return Fail("Created QuestPart_CSC has unexpected default field values.");

        Console.WriteLine("GREEN ok: Prefix creates QuestPart_CSC through its real constructor.");

        // 4. 其他类型不拦截。
        IExposable otherResult = null!;
        bool otherRun = QuestPartCscLoadFix.CreateInstance_Prefix(typeof(QuestPart), emptyCtorArgs, ref otherResult);
        if (!otherRun)
            return Fail("Prefix intercepted an unrelated type.");

        // 5. 非空 ctorArgs 不拦截（兼容 1.6 路径）。
        IExposable existingResult = null!;
        bool existingRun = QuestPartCscLoadFix.CreateInstance_Prefix(type, new object[] { new object() }, ref existingResult);
        if (!existingRun)
            return Fail("Prefix intercepted a non-empty ctorArgs call.");

        Console.WriteLine("GREEN ok: unrelated types and non-empty ctorArgs pass through.");
        return 0;
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine("FAIL: " + message);
        return 1;
    }
}
