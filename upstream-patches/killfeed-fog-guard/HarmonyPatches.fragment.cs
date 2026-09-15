// Apply these methods and the prefix registration to Killfeed's HarmonyPatches class.
public static void Prefix_Pawn_Kill(Pawn __instance, out bool __state)
{
    __state = false;
    if (ModData.Settings.DisplayInFog)
        return;

    if (__instance == null || !__instance.Spawned || __instance.MapHeld == null)
    {
        __state = true;
        return;
    }

    try
    {
        __state = __instance.Fogged();
    }
    catch (Exception exception)
    {
        Log.Message("KillFeed: Error checking if pawn is fogged: " + exception.Message);
        __state = true;
    }
}

// Add `bool __state` to the existing Pawn.Kill postfix and return before creating
// an announcement when __state is true.
