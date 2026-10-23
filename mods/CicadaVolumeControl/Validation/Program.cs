using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Xml;
using Meidocho.CicadaVolumeControl;
using Mono.Cecil;
using UnityEngine;
using Verse;
using Verse.Sound;

internal static class Program
{
    private static readonly string[] Targets =
        ["Ambient_NightInsects_Standard", "Ambient_NightInsects_Jungle", "Ambient_DayInsects_Jungle"];

    private static int Main(string[] args)
    {
        if (args is ["--help"])
        {
            Console.WriteLine("Usage: Validation bind|run GAME_ROOT MOD_ROOT MOD_DLL QA_ROOT");
            Console.WriteLine("QA_ROOT must be a disposable directory outside the game and real save/config folders.");
            return 0;
        }
        if (args.Length != 5 || args[0] is not ("bind" or "run"))
        {
            Console.Error.WriteLine("Expected a mode and four paths; use --help.");
            return 1;
        }
        string gameRoot = Path.GetFullPath(args[1]);
        string modRoot = Path.GetFullPath(args[2]);
        string modDll = Path.GetFullPath(args[3]);
        string qaRoot = Path.GetFullPath(args[4]);
        string bound = Path.Combine(qaRoot, "bound");
        AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            foreach (string directory in new[] { bound, gameRoot })
            {
                string path = Path.Combine(directory, name.Name + ".dll");
                if (File.Exists(path)) return context.LoadFromAssemblyPath(path);
            }
            return null;
        };
        try
        {
            if (args[0] == "bind")
            {
                Directory.CreateDirectory(bound);
                using var resolver = new DefaultAssemblyResolver();
                resolver.AddSearchDirectory(gameRoot);
                using var game = AssemblyDefinition.ReadAssembly(Path.Combine(gameRoot, "Assembly-CSharp.dll"),
                    new ReaderParameters { AssemblyResolver = resolver });
                MethodDefinition initializer = game.MainModule.GetType("Verse.ModsConfig").Methods.Single(m => m.Name == ".cctor");
                initializer.Body.Instructions.Clear();
                initializer.Body.ExceptionHandlers.Clear();
                initializer.Body.Variables.Clear();
                initializer.Body.GetILProcessor().Emit(Mono.Cecil.Cil.OpCodes.Ret);
                game.Write(Path.Combine(bound, "Assembly-CSharp.dll"));
                Console.WriteLine("PASS: QA-only game copy disables ModsConfig initialization; all tested methods remain unchanged.");
            }
            else
            {
                Assembly patch = AssemblyLoadContext.Default.LoadFromAssemblyPath(modDll);
                Run(gameRoot, modRoot, qaRoot, patch);
            }
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Run(string gameRoot, string modRoot, string qaRoot, Assembly patch)
    {
        string sandbox = Path.Combine(qaRoot, "save-data");
        Directory.CreateDirectory(Path.Combine(sandbox, "Config"));
        SetField(typeof(GenFilePaths), "saveDataPath", null, sandbox);
        var prefs = (PrefsData)RuntimeHelpers.GetUninitializedObject(typeof(PrefsData));
        prefs.volumeAmbient = 0.4f;
        SetField(typeof(Prefs), "data", null, prefs);
        var content = (ModContentPack)RuntimeHelpers.GetUninitializedObject(typeof(ModContentPack));
        SetField(typeof(ModContentPack), "rootDirInt", content, new DirectoryInfo(modRoot));
        SetField(typeof(ModContentPack), "nameInt", content, "Cicada Volume");
        content.assemblies = new ModAssemblyHandler(content);
        content.assemblies.loadedAssemblies.Add(patch);
        LoadedModManager.RunningModsListForReading.Add(content);
        var mod = new CicadaVolumeMod(content);
        CicadaVolumeSettings settings = mod.GetSettings<CicadaVolumeSettings>();
        Check(settings.VolumePercent == 100, "First-run volume differs from 100%.");
        Check(patch.GetReferencedAssemblies().All(a => a.Name != "0Harmony"), "Unexpected Harmony dependency.");
        RuntimeHelpers.PrepareMethod(typeof(CicadaVolumeMod).GetMethod(nameof(Mod.DoSettingsWindowContents))!.MethodHandle);

        var document = new XmlDocument();
        document.Load(Path.Combine(gameRoot, "Data/Core/Defs/SoundDefs/World_Sustainers_Ambiences.xml"));
        var before = document.DocumentElement!.ChildNodes.Cast<XmlNode>().Where(n => n.NodeType == XmlNodeType.Element)
            .ToDictionary(n => n["defName"]!.InnerText, n => n.OuterXml);
        var patchXml = new XmlDocument();
        patchXml.Load(Path.Combine(modRoot, "Patches/CicadaVolume.xml"));
        XmlNode operationNode = patchXml.SelectSingleNode("Patch/Operation")!;
        string xpath = operationNode["xpath"]!.InnerText;
        Check(document.SelectNodes(xpath)!.Count == Targets.Length, "Patch does not match exactly three Main mapping lists.");
        PatchOperation operation = DirectXmlToObject.ObjectFromXml<PatchOperation>(operationNode, false);
        Check(operation.Apply(document), "Actual PatchOperationAdd failed.");
        foreach (XmlNode node in document.DocumentElement.ChildNodes)
        {
            if (node.NodeType != XmlNodeType.Element) continue;
            string name = node["defName"]!.InnerText;
            if (!Targets.Contains(name)) Check(node.OuterXml == before[name], "Unrelated SoundDef changed: " + name);
            else
            {
                XmlNode mapping = node.SelectSingleNode("subSounds/li[name='Main']/paramMappings")!.LastChild!;
                mapping.ParentNode!.RemoveChild(mapping);
                Check(node.OuterXml == before[name], "Target changed beyond the appended mapping: " + name);
                node.SelectSingleNode("subSounds/li[name='Main']/paramMappings")!.AppendChild(mapping);
            }
        }
        Console.WriteLine("PASS: actual XML patch changes only three intended mapping lists; every original node is preserved.");

        foreach (string name in Targets)
        {
            XmlNode node = document.SelectSingleNode($"Defs/SoundDef[defName='{name}']")!;
            var def = (SoundDef)DirectXmlToObjectNew.DefFromNodeNew(node, null);
            SubSoundDef original = def.subSounds.Single(s => s.name == "Main");
            original.parentDef = def;
            SoundParameterMapping added = original.paramMappings.Single(m => m.inParam is SoundParamSource_CicadaVolume);
            Check(added.paramUpdateMode == SoundParamUpdateMode.Constant && added.outParam is SoundParamTarget_Volume,
                "New parser did not create the intended constant volume mapping.");
            SoundParameterMapping ambient = original.paramMappings.Single(m => m.inParam is SoundParamSource_AmbientVolume);
            // 测试替身只隔离地图/天气/音乐条件；真实 XML 映射、Sample.Update 与 AudioSource 属性保留。
            var probeDef = new SubSoundDef { parentDef = def, paramMappings = [ambient, added] };
            var sample = new ProbeSample(probeDef, original.volumeRange.min / 100f);
            foreach (int percent in new[] { 100, 50, 0, 25, 100, 33, 33, 100 })
            {
                settings.VolumePercent = percent;
                sample.Update();
                float expected = sample.resolvedVolume * 0.75f * prefs.volumeAmbient * percent / 100f;
                Check(Math.Abs(sample.source.volume - expected) < 0.000001f, $"Live volume differs: {name} at {percent}%.");
                Check(IsMuted(sample.source) == (expected < 0.001f), "Mute/unmute state differs.");
            }
            prefs.volumeAmbient = 0.2f;
            sample.Update();
            Check(Math.Abs(sample.source.volume - sample.resolvedVolume * 0.75f * 0.2f) < 0.000001f,
                "Original global ambience setting no longer applies.");
            prefs.volumeAmbient = 0.4f;
            var control = new ProbeSample(new SubSoundDef { parentDef = def, paramMappings = [ambient] }, sample.resolvedVolume);
            settings.VolumePercent = 0;
            control.Update();
            Check(control.source.volume > 0 && !IsMuted(control.source), "Sound without custom mapping was muted.");
            Console.WriteLine($"PASS: {name} new-parser binding, same-sample 100/50/0/25/100%, repeated changes, mute recovery, original ambience multiplier.");
        }

        settings.VolumePercent = 37;
        mod.WriteSettings();
        string filename = Path.Combine(sandbox, "Config", $"Mod_{content.FolderName}_{nameof(CicadaVolumeMod)}.xml");
        Check(File.ReadAllText(filename).Contains("<volumePercent>37</volumePercent>", StringComparison.Ordinal), "37% was not saved.");
        Check(new CicadaVolumeMod(content).GetSettings<CicadaVolumeSettings>().VolumePercent == 37, "37% did not round-trip.");
        foreach ((string text, int expected) in new[] { ("<volumePercent>-5</volumePercent>", 0),
            ("<volumePercent>105</volumePercent>", 100), ("", 100) })
        {
            File.WriteAllText(filename, "<SettingsBlock><ModSettings>" + text + "</ModSettings></SettingsBlock>");
            Check(new CicadaVolumeMod(content).GetSettings<CicadaVolumeSettings>().VolumePercent == expected,
                "Persisted setting default/clamp differs.");
        }
        Check(GenFilePaths.SaveDataFolderPath == sandbox, "Save-data isolation was lost.");
        Console.WriteLine("PASS: actual Mod constructor/GetSettings/WriteSettings/Scribe round-trip; missing key and out-of-range inputs.");
        Console.WriteLine("NOT TESTED: rendered settings UI, full map conditions, actual audio playback/listening; no game was started.");
    }

    private sealed class ProbeSample : Sample
    {
        public ProbeSample(SubSoundDef def, float volume) : base(def)
        {
            source = (AudioSource)RuntimeHelpers.GetUninitializedObject(typeof(AudioSource));
            resolvedVolume = volume;
            resolvedPitch = 1;
        }
        public override float ParentStartRealTime => 0;
        public override float ParentStartTick => 0;
        public override float ParentHashCode => 0;
        public override SoundParams ExternalParams { get; } = new();
        public override SoundInfo Info => new() { testPlay = true, volumeFactor = 0.75f, pitchFactor = 1 };
    }

    private static void SetField(Type type, string name, object? target, object value) =>
        type.GetField(name, BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(target, value);

    private static bool IsMuted(AudioSource source) =>
        (bool)typeof(AudioSource).GetField("_mute", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(source)!;

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
