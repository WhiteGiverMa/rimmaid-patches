using HarmonyLib;
using Verse;

namespace CEBreachingFix;

public class CEBreachingFixMod : Mod
{
    public CEBreachingFixMod(ModContentPack content) : base(content)
    {
        var harmony = new Harmony("WhiteGiverMa.CEBreachingFix");
        harmony.PatchAll();
        Log.Message("[CE Breaching Fix] Initialized");
    }
}
