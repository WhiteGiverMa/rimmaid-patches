using HarmonyLib;
using UnityEngine;
using Verse;

namespace MjRimMods.WhileYouAreNearby;

public sealed class ModMain : Mod
{
    internal const string HarmonyId = "PureMJ.MjRimMods.WhileYouAreNearby";

    public ModMain(ModContentPack content) : base(content)
    {
        Utils.ThisMod = this;
        ApplyPatches();
        Log.Message("[While You Are Nearby Maintained 1.0.0] Loaded: distance-first cells, work giver metadata, discarded-job pooling; vanilla scheduling.");
    }

    internal static void ApplyPatches() => new Harmony(HarmonyId).PatchAll(typeof(ModMain).Assembly);

    public override string SettingsCategory() => "While You Are Nearby - Maintained";

    public override void DoSettingsWindowContents(Rect inRect) => Utils.settings.Draw(inRect);
}

internal static class Utils
{
    internal static SimpleSettings? _settings;
    internal static Mod ThisMod = null!;
    // Like the original, load Def-backed settings only when settings/work is first used, after definitions are ready.
    public static SimpleSettings settings => _settings ??= ThisMod.GetSettings<SimpleSettings>();

    internal static void Debug(string message)
    {
        if (Prefs.DevMode && settings.debug) Log.Message("[While You Are Nearby Maintained] " + message);
    }
}
