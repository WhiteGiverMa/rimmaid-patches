using System;
using System.IO;
using System.Reflection;
using System.Xml;
using CEOverpenetration;
using Verse;

public static class OverpenetrationSettingsValidation
{
    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception("FAIL: " + description);
        Console.WriteLine("PASS: " + description);
    }

    public static void Run()
    {
        string path = Path.Combine(Path.GetTempPath(), "CEOverpenetration-" + Guid.NewGuid() + ".xml");
        var property = typeof(CEOverpenetrationMod).GetProperty("Settings", BindingFlags.Static | BindingFlags.NonPublic);
        var enabled = typeof(CEOverpenetrationMod).GetProperty("LogContinuationsEnabled", BindingFlags.Static | BindingFlags.NonPublic);
        using (Log.LockMessages())
        {
            DeepProfiler.enabled = false;
            try
            {
                Check(!(bool)enabled.GetValue(null), "logging defaults off before settings load");
                var settings = new CEOverpenetrationSettings();
                property.SetValue(null, settings);
                Check(!(bool)enabled.GetValue(null), "logging defaults off after settings load");
                settings.LogContinuations = true;
                Check((bool)enabled.GetValue(null), "enabling takes effect immediately");
                settings.LogContinuations = false;
                Check(!(bool)enabled.GetValue(null), "disabling takes effect immediately");
                settings.LogContinuations = true;
                Scribe.saver.InitSaving(path, "Settings");
                settings.ExposeData();
                Scribe.saver.FinalizeSaving();
                var xml = new XmlDocument();
                xml.Load(path);
                Check(xml.DocumentElement["logContinuations"].InnerText == "True", "enabled choice is persisted");
                var loaded = new CEOverpenetrationSettings();
                Scribe.mode = LoadSaveMode.LoadingVars;
                Scribe.loader.curXmlParent = xml.DocumentElement;
                loaded.ExposeData();
                property.SetValue(null, loaded);
                Check((bool)enabled.GetValue(null), "saved choice loads as enabled");
                xml.LoadXml("<Settings />");
                Scribe.loader.curXmlParent = xml.DocumentElement;
                loaded.ExposeData();
                Check(!(bool)enabled.GetValue(null), "missing choice loads as off");
            }
            finally
            {
                Scribe.mode = LoadSaveMode.Inactive;
                property.SetValue(null, null);
                if (File.Exists(path)) File.Delete(path);
            }
        }
    }
}
