using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using DMSSR.Hediffs;
using DMSSR.Misc;
using DMSSR.Patches;
using DMSSR.UI;
using Fortified;
using HarmonyLib;
using RimWorld;
using Verse;
using WhiteGiver.DMSSRLegionCustomizationCompat;

namespace DMSSRLegionCustomizationCompat.Validation;

internal static class Tests
{
    private const string Owner = "whitegiver.dmssr.legioncustomizationcompat";
    private const string ValidationOwner = "whitegiver.dmssr.legioncustomizationcompat.validation.probe";

    private static readonly MethodInfo IsSupportedPawnMethod =
        AccessTools.Method(typeof(MechCustomizationUtility), "IsSupportedPawn", new[] { typeof(Pawn) })!;

    private static readonly MethodInfo CreateWindowForMethod =
        AccessTools.Method(typeof(MechCustomizationUtility), "CreateWindowFor", new[] { typeof(Pawn) })!;

    private static readonly MethodInfo ApplyToSpawnedMechsMethod =
        AccessTools.Method(typeof(Patch_MechCustomization_UnlockOnResearch), "ApplyToSpawnedMechs", Type.EmptyTypes)!;

    private static readonly FieldInfo WindowFactoriesField =
        AccessTools.Field(typeof(MechModificationWindowUtility), "WindowFactories")!;

    private static readonly FieldInfo WindowMechField =
        AccessTools.Field(typeof(Window_MechModification), "Mech")!;

    private static readonly MethodInfo CommitCustomSettingsMethod =
        AccessTools.Method(typeof(Window_MechCustomization), "CommitCustomSettings")!;

    private static readonly MethodInfo GetAdditionalInstallItemDefsMethod =
        AccessTools.Method(typeof(Window_MechCustomization), "GetAdditionalInstallItemDefs")!;

    private static readonly MethodInfo TryCreateCustomJobMethod =
        AccessTools.Method(typeof(Window_MechCustomization), "TryCreateCustomJob")!;

    private static readonly FieldInfo RootField =
        AccessTools.Field(typeof(Current), "rootInt")!;

    private static readonly FieldInfo UIRootWindowsField =
        AccessTools.Field(typeof(UIRoot), "windows")!;

    private static readonly FieldInfo RootUIRootField =
        AccessTools.Field(typeof(Root), "uiRoot")!;

    private static readonly IntVec3 Start = new(2, 0, 2);

    private static int checks;

    private static Fixture fx = null!;
    private static string legionRoot = "";
    private static string dmsCoreRoot = "";

    internal static void Run(
        string mode,
        Type modType,
        string gameRoot,
        string legionRootPath,
        string dmsCoreRootPath,
        string ancientCorpsRootPath)
    {
        legionRoot = legionRootPath;
        dmsCoreRoot = dmsCoreRootPath;
        fx = Fixture.Create(gameRoot, legionRoot, dmsCoreRoot, ancientCorpsRootPath);
        Probe.Install();

        try
        {
            if (mode == "red")
            {
                RunRed();
            }
            else
            {
                RunGreen(modType);
            }
        }
        finally
        {
            Probe.Remove();
        }

        Console.WriteLine();
        Console.WriteLine($"PASS: {checks} assertions.");
    }

    // ------------------------------------------------------------------ RED

    private static void RunRed()
    {
        Section("RED - upstream DMSSR behaviour before the compatibility layer");
        Check(fx.LegionMechs.Count == 20,
            $"the installed Legion 1.7 tree has {fx.LegionMechs.Count} DMSL_Mech_* defs, expected the audited 20.");
        Note("DMSL_Mech_* defs: " + string.Join(", ", fx.LegionMechs.Select(entry => entry.DefName)));

        Dictionary<string, ThingDef> mechDefs = fx.LegionMechs.ToDictionary(
            entry => entry.DefName,
            entry => fx.MechDef(entry),
            StringComparer.Ordinal);

        int falsePredicate = 0;
        int nullWindow = 0;
        foreach (LegionMechEntry entry in fx.LegionMechs)
        {
            Pawn pawn = fx.NewPawn(mechDefs[entry.DefName], Start);
            Check(pawn.RaceProps.IsMechanoid, entry.DefName + " is not a mechanoid in the fixture.");
            Check(pawn.RaceProps.body.AllParts.Count > 0, entry.DefName + " has an empty body.");

            bool supported = IsSupportedPawn(pawn);
            if (!supported)
            {
                falsePredicate++;
            }

            Window? window = CreateWindowFor(pawn);
            if (window == null)
            {
                nullWindow++;
            }

            Check(!supported, entry.DefName + ": upstream IsSupportedPawn returned true, expected false.");
            Check(window == null, entry.DefName + ": upstream CreateWindowFor returned " + Describe(window) + ", expected null.");
        }

        Check(falsePredicate == fx.LegionMechs.Count, "not all Legion mechs were rejected by the upstream predicate.");
        Check(nullWindow == fx.LegionMechs.Count, "not all Legion mechs fell through the upstream factory.");
        Console.WriteLine($"       {falsePredicate}/{fx.LegionMechs.Count} DMSL_Mech_* pawns: IsSupportedPawn=false, CreateWindowFor=null");
        Console.WriteLine("       => Fortified.MechModificationWindowUtility falls back to Window_MechModification (no parameter panel).");

        // Controls: the upstream predicate is purely name-based, so a DMS_Mech_/Cinder_Mech_ name
        // must still produce Window_MechCustomization.
        Pawn krepost = fx.NewPawn(fx.ControlDef("DMS_Mech_Krepost"), Start);
        Check(IsSupportedPawn(krepost), "DMS_Mech_Krepost control was rejected by upstream.");
        Check(CreateWindowFor(krepost) is Window_MechCustomization, "DMS_Mech_Krepost did not create Window_MechCustomization.");

        Pawn cinder = fx.NewPawn(fx.ControlDef("Cinder_Mech_Test"), Start);
        Check(IsSupportedPawn(cinder), "Cinder_Mech_ char control was rejected by upstream.");
        Check(CreateWindowFor(cinder) is Window_MechCustomization, "Cinder_Mech_ control did not create Window_MechCustomization.");

        Pawn centipede = fx.NewPawn(fx.PlainDef("Mech_Centipede", fx.MechFlesh, Intelligence.ToolUser), Start);
        Pawn human = fx.NewPawn(fx.PlainDef("Human", fx.OrganicFlesh, Intelligence.Humanlike), Start);
        Pawn chicken = fx.NewPawn(fx.PlainDef("Chicken", fx.OrganicFlesh, Intelligence.Animal), Start);
        foreach (Pawn pawn in new[] { centipede, human, chicken })
        {
            Check(!IsSupportedPawn(pawn), pawn.def.defName + " was accepted by upstream.");
            Check(CreateWindowFor(pawn) == null, pawn.def.defName + " created a customization window.");
        }

        Check(fx.DroneDefName != null, "DMSL_Drone_CommandingBird was not found in the Legion tree.");
        Pawn drone = fx.NewPawn(fx.DefWithBody(fx.DroneDefName!, "DMS_Chop"), Start);
        Check(!IsSupportedPawn(drone), fx.DroneDefName + " was accepted by upstream.");
        Check(CreateWindowFor(drone) == null, fx.DroneDefName + " created a customization window.");

        // The real Fortified entry point must fall back to the plain window for a Legion pawn.
        Root? savedRoot = (Root?)RootField.GetValue(null);
        try
        {
            InstallFakeWindowStack();
            Pawn fallbackLeopard = fx.NewPawn(mechDefs["DMSL_Mech_leopard"], Start);
            fx.PrepareOpenable(fallbackLeopard);
            Check(MechModificationWindowUtility.CanOpenFor(fallbackLeopard),
                "Fortified CanOpenFor rejected the prepared Legion pawn.");
            Check(!MechCustomizationUtility.CanOpenFor(fallbackLeopard),
                "upstream DMSSR CanOpenFor unexpectedly accepted the Legion pawn.");
            MechModificationWindowUtility.OpenFor(fallbackLeopard);
            Check(Find.WindowStack.Count == 1, "OpenFor did not add exactly one fallback window.");
            Check(Find.WindowStack[0].GetType() == typeof(Window_MechModification),
                "upstream OpenFor added " + Describe(Find.WindowStack[0]) + " instead of the plain fallback.");
        }
        finally
        {
            RootField.SetValue(null, savedRoot);
        }

        Console.WriteLine("       upstream OpenFor on a prepared leopard: plain Window_MechModification (no parameter panel).");

        // The cap system itself is not the blocker: it already works on a Legion body.
        Pawn leopard = fx.NewPawn(mechDefs["DMSL_Mech_leopard"], Start);
        HediffComp_MechCustomization? comp = MechCustomizationUtility.GetOrCreateCustomizationComp(leopard);
        Check(comp != null, "GetOrCreateCustomizationComp returned null for DMSL_Mech_leopard.");
        Check(HediffCount(leopard, fx.CustomizationDef) == 1, "GetOrCreateCustomizationComp did not add exactly one hediff.");
        GetCaps(leopard, out float supply, out float compute, out float electro, out float drive, out float subsystem);
        CheckCaps(supply, compute, electro, drive, subsystem, 1.25f, 1.25f, 1.25f, 1.25f, 1.25f,
            "base caps for DMSL_Mech_leopard");

        Console.WriteLine("       the only blocker is the name predicate: caps and the customization hediff already work on the Legion body.");
        Note("RED conclusion: upstream opens the plain Fortified modification window for every DMSL_Mech_* pawn.");
    }

    // ---------------------------------------------------------------- GREEN

    private static void RunGreen(Type modType)
    {
        Section("GREEN - applying DMSSRLegionCustomizationCompat");
        object mod = Activator.CreateInstance(modType, new object?[] { null })!;
        Check(mod != null, "Mod constructor returned null.");

        Check(DMSSRLegionCustomizationCompatMod.IsSupportedPawnPatchInstalled, "IsSupportedPawn postfix was not installed.");
        Check(DMSSRLegionCustomizationCompatMod.UnlockOnResearchPatchInstalled, "ApplyToSpawnedMechs postfix was not installed.");
        Check(HasOwnerPatch(IsSupportedPawnMethod, postfix: true), "IsSupportedPawn does not carry the compatibility postfix.");
        Check(HasOwnerPatch(ApplyToSpawnedMechsMethod, postfix: true), "ApplyToSpawnedMechs does not carry the compatibility postfix.");

        HashSet<MethodBase> owned = OwnedMethods();
        Check(owned.Count == 2, $"the mod patched {owned.Count} methods instead of 2: {string.Join(", ", owned.Select(Describe))}");
        Console.WriteLine("       patch wiring: 2 targets (IsSupportedPawn postfix, ApplyToSpawnedMechs postfix).");
        foreach (MethodBase patched in owned)
        {
            Patches? info = Harmony.GetPatchInfo(patched);
            Console.WriteLine($"       patched {Describe(patched)}: prefixes=[{string.Join(", ", info!.Prefixes.Select(p => p.PatchMethod.Name + "@" + p.owner))}]"
                + $" postfixes=[{string.Join(", ", info.Postfixes.Select(p => p.PatchMethod.Name + "@" + p.owner))}]");
        }

        Dictionary<string, ThingDef> mechDefs = fx.LegionMechs.ToDictionary(
            entry => entry.DefName,
            entry => fx.MechDef(entry),
            StringComparer.Ordinal);

        int customWindows = 0;
        foreach (LegionMechEntry entry in fx.LegionMechs)
        {
            Pawn pawn = fx.NewPawn(mechDefs[entry.DefName], Start);
            Check(IsSupportedPawn(pawn), entry.DefName + ": IsSupportedPawn is still false after the patch.");

            Window? window = CreateWindowFor(pawn);
            Check(window is Window_MechCustomization, entry.DefName + ": factory returned " + Describe(window) + ".");
            if (window is Window_MechCustomization)
            {
                customWindows++;
                window.PreOpen();
                Check(MechCustomizationUtility.GetOrCreateCustomizationComp(pawn) != null,
                    entry.DefName + ": PreOpen did not create the customization comp.");
            }
        }

        Check(customWindows == fx.LegionMechs.Count,
            $"{customWindows}/{fx.LegionMechs.Count} Legion mechs created Window_MechCustomization.");
        Console.WriteLine($"       {customWindows}/{fx.LegionMechs.Count} DMSL_Mech_* pawns: IsSupportedPawn=true, factory=Window_MechCustomization, PreOpen succeeded.");

        // Non-DMS pawns and the Legion drone must remain untouched.
        Pawn centipede = fx.NewPawn(fx.PlainDef("Mech_Centipede", fx.MechFlesh, Intelligence.ToolUser), Start);
        Pawn human = fx.NewPawn(fx.PlainDef("Human", fx.OrganicFlesh, Intelligence.Humanlike), Start);
        Pawn chicken = fx.NewPawn(fx.PlainDef("Chicken", fx.OrganicFlesh, Intelligence.Animal), Start);
        foreach (Pawn pawn in new[] { centipede, human, chicken })
        {
            Check(!IsSupportedPawn(pawn), pawn.def.defName + " became supported after the patch.");
            Check(CreateWindowFor(pawn) == null, pawn.def.defName + " created a customization window after the patch.");
        }

        Check(fx.DroneDefName != null, "DMSL_Drone_CommandingBird was not found in the Legion tree.");
        Pawn drone = fx.NewPawn(fx.DefWithBody(fx.DroneDefName!, "DMS_Chop"), Start);
        Check(!IsSupportedPawn(drone), fx.DroneDefName + " became supported; drones must stay out of the parameter UI.");
        Check(CreateWindowFor(drone) == null, fx.DroneDefName + " created a customization window.");

        Pawn krepost = fx.NewPawn(fx.ControlDef("DMS_Mech_Krepost"), Start);
        Check(IsSupportedPawn(krepost), "DMS_Mech_Krepost control regressed.");
        Check(CreateWindowFor(krepost) is Window_MechCustomization, "DMS_Mech_Krepost control no longer gets the customization window.");

        Pawn cinder = fx.NewPawn(fx.ControlDef("Cinder_Mech_Test"), Start);
        Check(IsSupportedPawn(cinder), "Cinder_Mech_ control regressed.");
        Check(CreateWindowFor(cinder) is Window_MechCustomization, "Cinder_Mech_ control no longer gets the customization window.");

        Section("GREEN - parameter caps, persistence and the window's own commit path");
        Pawn leopard = fx.NewPawn(mechDefs["DMSL_Mech_leopard"], Start);
        HediffComp_MechCustomization? comp = MechCustomizationUtility.GetOrCreateCustomizationComp(leopard);
        Check(comp != null, "GetOrCreateCustomizationComp returned null for DMSL_Mech_leopard.");
        Check(HediffCount(leopard, fx.CustomizationDef) == 1, "GetOrCreateCustomizationComp did not add exactly one hediff.");
        Check(ReferenceEquals(comp, MechCustomizationUtility.GetOrCreateCustomizationComp(leopard)),
            "GetOrCreateCustomizationComp created a second comp/hediff.");
        Check(HediffCount(leopard, fx.CustomizationDef) == 1, "a second call added a second customization hediff.");

        GetCaps(leopard, out float supply, out float compute, out float electro, out float drive, out float subsystem);
        CheckCaps(supply, compute, electro, drive, subsystem, 1.25f, 1.25f, 1.25f, 1.25f, 1.25f,
            "base caps for DMSL_Mech_leopard");

        fx.AddModification(leopard, "DMS_SyntheticTendon", "MechanicalLeg");
        fx.AddModification(leopard, "DMS_ReinforcedFrame", "DMS_HeavyChassis");
        fx.AddModification(leopard, "DMS_NuclearBattery", "DMS_Battery");
        GetCaps(leopard, out supply, out compute, out electro, out drive, out subsystem);
        CheckCaps(supply, compute, electro, drive, subsystem, 1.75f, 1.30f, 1.30f, 1.55f, 1.30f,
            "caps with tendon/frame/battery installed on the leopard");

        CheckClose(MechCustomizationHeteroUtility.GetBaseImmunity(leopard), 1.1f, "hetero base immunity");
        Check(MechCustomizationHeteroUtility.GetTotalDeathCheatCharges(leopard) == 0, "leopard has unexpected death-cheat charges.");
        Check(MechCustomizationHeteroUtility.CountInstalledMods(leopard) == 3, "CountInstalledMods did not see the 3 installed modifications.");
        CheckClose(MechCustomizationHeteroUtility.GetEffectiveImmunity(leopard), 1.1f - 3 * 0.02f, "hetero effective immunity");
        Check(ProtocolSimulationUtility.GetRegularDeathCheatCharges(leopard) == 0, "protocol charges should be 0 for a plain Legion mech.");

        // SetParams clamps to the computed caps.
        comp!.SetParams(3f, 3f, 3f, 3f, 3f);
        CheckClose(comp.supply, 1.75f, "SetParams supply clamp");
        CheckClose(comp.computation, 1.30f, "SetParams computation clamp");
        CheckClose(comp.electro, 1.30f, "SetParams electro clamp");
        CheckClose(comp.drive, 1.55f, "SetParams drive clamp");
        CheckClose(comp.subsystem, 1.30f, "SetParams subsystem clamp");

        comp.SetParams(1.75f, 1.30f, 1.30f, 1.55f, 1.30f);
        CheckClose(comp.supply, 1.75f, "supply");
        CheckClose(comp.computation, 1.30f, "computation");
        CheckClose(comp.electro, 1.30f, "electro");
        CheckClose(comp.drive, 1.55f, "drive");
        CheckClose(comp.subsystem, 1.30f, "subsystem");
        CheckClose(comp.wastepacksMult, 1.75f, "wastepacks coefficient");
        CheckClose(comp.bandwidthMult, 1.225f, "bandwidth coefficient");
        CheckClose(comp.energyUseMult, 0.625f, "energy-use coefficient");
        CheckClose(comp.energyMaxMult, 1.75f, "energy-max coefficient");
        CheckClose(comp.consciousnessMult, 1.15f, "consciousness coefficient");
        CheckClose(comp.shootAccMult, 1.30f, "shoot-accuracy coefficient");
        CheckClose(comp.meleeDodgeMult, 1.30f, "dodge coefficient");
        CheckClose(comp.manipulationMult, 1.15f, "manipulation coefficient");
        CheckClose(comp.meleeCooldownMult, 0.85f, "melee cooldown coefficient");
        CheckClose(comp.aimTimeMult, 0.85f, "aim-time coefficient");
        CheckClose(comp.moveSpeedMult, 1.55f, "move-speed coefficient");
        CheckClose(comp.meleeDamageMult, 1.55f, "melee-damage coefficient");
        CheckClose(comp.bodySizeOffset, 0.30f, "body-size offset");
        Console.WriteLine("       caps: supply 1.75 / compute 1.30 / electro 1.30 / drive 1.55 / subsystem 1.30 (tendon + frame + battery).");

        CheckScribeRoundTrip(comp);

        // Window.CommitCustomSettings is the real UI save path; exercise it on a fresh pawn.
        Pawn uiLeopard = fx.NewPawn(mechDefs["DMSL_Mech_leopard"], Start);
        Window_MechCustomization uiWindow = (Window_MechCustomization)CreateWindowFor(uiLeopard)!;
        uiWindow.PreOpen();
        SetSlider(uiWindow, "supplyParam", 1.2f);
        SetSlider(uiWindow, "computeParam", 1.1f);
        SetSlider(uiWindow, "electroParam", 1.05f);
        SetSlider(uiWindow, "driveParam", 1.25f);
        SetSlider(uiWindow, "subsystemParam", 1.15f);
        CommitCustomSettingsMethod.Invoke(uiWindow, null);
        HediffComp_MechCustomization uiComp = MechCustomizationUtility.GetOrCreateCustomizationComp(uiLeopard)!;
        CheckClose(uiComp.supply, 1.2f, "CommitCustomSettings supply");
        CheckClose(uiComp.computation, 1.1f, "CommitCustomSettings computation");
        CheckClose(uiComp.electro, 1.05f, "CommitCustomSettings electro");
        CheckClose(uiComp.drive, 1.25f, "CommitCustomSettings drive");
        CheckClose(uiComp.subsystem, 1.15f, "CommitCustomSettings subsystem");
        Console.WriteLine("       Window_MechCustomization.CommitCustomSettings wrote the 5 parameters through the upstream comp.");

        // The protocol button must not throw for a Legion mech, and must remain research-gated.
        MechModificationQueueEntry protocolEntry = new()
        {
            kind = MechModificationOperationKind.Custom,
            custom = true,
            customId = "DMSSR.ProtocolSimulation",
        };
        object?[] jobArgs = { protocolEntry, null };
        bool protocolHandled = (bool)TryCreateCustomJobMethod.Invoke(uiWindow, jobArgs)!;
        Check(protocolHandled, "TryCreateCustomJob did not handle DMSSR.ProtocolSimulation for a Legion mech.");
        Check(jobArgs[1] == null, "protocol simulation created a job while DMSSR_PulsatedEmbryte is not researched.");
        Console.WriteLine("       protocol simulation entry is accepted and correctly stays locked behind DMSSR_PulsatedEmbryte.");

        // Synthetic bionics are body-structure scoped in upstream; Terra intentionally keeps the
        // vanilla humanlike body and is not added to the wetware whitelist.
        Pawn terra = fx.NewPawn(mechDefs["DMSL_Mech_Terra"], Start);
        Check(IsSupportedPawn(terra), "DMSL_Mech_Terra is not supported after the patch.");
        Check(CreateWindowFor(terra) is Window_MechCustomization, "DMSL_Mech_Terra did not create Window_MechCustomization.");
        Window_MechCustomization terraWindow = (Window_MechCustomization)CreateWindowFor(terra)!;
        terraWindow.PreOpen();
        Check(!SyntheticBionicUtility.IsSupportedSyntheticBody(terra),
            "DMSL_Mech_Terra was unexpectedly treated as a wetware synthetic body.");
        IEnumerable<ThingDef> terraInstallItems = (IEnumerable<ThingDef>)GetAdditionalInstallItemDefsMethod.Invoke(terraWindow, null)!;
        Check(!terraInstallItems.Any(), "Terra unexpectedly exposed synthetic install items.");
        fx.AddModification(terra, "DMS_SyntheticTendon", "Leg");
        GetCaps(terra, out _, out compute, out electro, out drive, out subsystem);
        CheckClose(drive, 1.50f, "Terra drive cap with a tendon on a vanilla Leg");
        CheckClose(compute, 1.25f, "Terra compute cap with a tendon");
        CheckClose(electro, 1.25f, "Terra electro cap with a tendon");
        CheckClose(subsystem, 1.25f, "Terra subsystem cap with a tendon");

        Pawn synthroid = fx.NewPawn(fx.DefWithBody("DMS_Mech_SynthroidControl", "DMS_Synthroid"), Start);
        Check(SyntheticBionicUtility.IsSupportedSyntheticBody(synthroid),
            "the DMS_Synthroid control body is not recognised by the upstream synthetic whitelist.");
        Console.WriteLine("       Terra: customization window + vanilla body caps; not added to the wetware synthetic whitelist (by design).");

        Section("GREEN - research unlock, MechModificationWindowUtility and the plain fallback");
        Pawn unlockLeopard = fx.NewPawn(mechDefs["DMSL_Mech_leopard"], Start);
        Pawn unlockDrone = fx.NewPawn(fx.DefWithBody(fx.DroneDefName!, "DMS_Chop"), Start);
        fx.AddToMap(unlockLeopard);
        fx.AddToMap(unlockDrone);
        ApplyToSpawnedMechsMethod.Invoke(null, null);
        Check(unlockLeopard.health.hediffSet.HasHediff(fx.CustomizationDef, false),
            "ApplyToSpawnedMechs did not grant the customization hediff to a spawned DMSL_Mech_.");
        Check(!unlockDrone.health.hediffSet.HasHediff(fx.CustomizationDef, false),
            "ApplyToSpawnedMechs granted the customization hediff to the Legion drone.");
        int afterFirst = HediffCount(unlockLeopard, fx.CustomizationDef);
        ApplyToSpawnedMechsMethod.Invoke(null, null);
        Check(HediffCount(unlockLeopard, fx.CustomizationDef) == afterFirst,
            "ApplyToSpawnedMechs added a duplicate hediff on the second run.");
        fx.RemoveFromMap(unlockLeopard);
        fx.RemoveFromMap(unlockDrone);
        Console.WriteLine("       MicroelectronicsBasics scan: exactly one hediff on spawned DMSL_Mech_*, none on DMSL_Drone_*.");

        // The factory list is what Fortified.OpenFor iterates. Mirror its selection order.
        List<Func<Pawn, Window>> factories = (List<Func<Pawn, Window>>)WindowFactoriesField.GetValue(null)!;
        Pawn factoryLeopard = fx.NewPawn(mechDefs["DMSL_Mech_leopard"], Start);
        Window? selected = null;
        foreach (Func<Pawn, Window> factory in factories)
        {
            selected = factory(factoryLeopard);
            if (selected != null)
            {
                break;
            }
        }

        Check(selected is Window_MechCustomization, "Fortified's factory list did not select Window_MechCustomization.");
        Console.WriteLine($"       Fortified factory list ({factories.Count} factories): first non-null result is {Describe(selected)}.");

        // Full OpenFor path with a fake WindowStack (no Unity scene).
        Root? savedRoot = (Root?)RootField.GetValue(null);
        try
        {
            InstallFakeWindowStack();
            Pawn openLeopard = fx.NewPawn(mechDefs["DMSL_Mech_leopard"], Start);
            fx.PrepareOpenable(openLeopard);
            Check(MechModificationWindowUtility.CanOpenFor(openLeopard), "Fortified CanOpenFor rejected the prepared Legion pawn.");
            Check(MechCustomizationUtility.CanOpenFor(openLeopard), "DMSSR CanOpenFor rejected the prepared Legion pawn.");
            MechModificationWindowUtility.OpenFor(openLeopard);
            Check(Find.WindowStack.Count == 1, "OpenFor did not add exactly one window for the Legion pawn.");
            Check(Find.WindowStack[0] is Window_MechCustomization,
                "OpenFor added " + Describe(Find.WindowStack[0]) + " instead of Window_MechCustomization.");
            Check(ReferenceEquals(WindowMechField.GetValue(Find.WindowStack[0]), openLeopard),
                "the opened window is not bound to the requested Legion pawn.");

            InstallFakeWindowStack();
            Pawn publicLeopard = fx.NewPawn(mechDefs["DMSL_Mech_leopard"], Start);
            fx.PrepareOpenable(publicLeopard);
            MechCustomizationUtility.OpenFor(publicLeopard);
            Check(Find.WindowStack.Count == 1, "MechCustomizationUtility.OpenFor did not add exactly one window.");
            Check(Find.WindowStack[0] is Window_MechCustomization,
                "MechCustomizationUtility.OpenFor added " + Describe(Find.WindowStack[0]) + ".");

            InstallFakeWindowStack();
            Pawn openPlain = fx.NewPawn(fx.PlainDef("Mech_Centipede", fx.MechFlesh, Intelligence.ToolUser), Start);
            fx.PrepareOpenable(openPlain);
            Check(MechModificationWindowUtility.CanOpenFor(openPlain), "Fortified CanOpenFor rejected the control mechanoid.");
            Check(!MechCustomizationUtility.CanOpenFor(openPlain), "DMSSR CanOpenFor accepted a non-DMS mechanoid.");
            MechModificationWindowUtility.OpenFor(openPlain);
            Check(Find.WindowStack.Count == 1, "OpenFor did not add exactly one fallback window.");
            Check(Find.WindowStack[0].GetType() == typeof(Window_MechModification),
                "non-DMS mechanoid did not fall back to the plain Window_MechModification.");
        }
        finally
        {
            RootField.SetValue(null, savedRoot);
        }

        Console.WriteLine("       MechModificationWindowUtility.OpenFor: Legion -> Window_MechCustomization; Mech_Centipede -> plain Window_MechModification.");
        Note("GREEN conclusion: the customization UI, caps, persistence and research grant all work for DMSL_Mech_*; vanilla and drone pawns are unchanged.");
    }

    // ------------------------------------------------------------- helpers

    private static void CheckScribeRoundTrip(HediffComp_MechCustomization comp)
    {
        string path = Path.Combine(Path.GetTempPath(), "dmssr-legion-compat-scribe-" + Guid.NewGuid().ToString("N") + ".xml");
        try
        {
            Scribe.saver.InitSaving(path, "root");
            Scribe.EnterNode("comp");
            comp.CompExposeData();
            Scribe.ExitNode();
            Scribe.saver.FinalizeSaving();

            HediffComp_MechCustomization loaded = new()
            {
                props = new HediffCompProperties_MechCustomization(),
            };
            Scribe.loader.InitLoading(path);
            Scribe.EnterNode("comp");
            loaded.CompExposeData();
            Scribe.ExitNode();
            Scribe.loader.FinalizeLoading();

            string[] fields =
            {
                "energyFactor", "supply", "computation", "electro", "drive", "subsystem",
                "nonSupplyCapBonusFactor", "heteroImmunityBase", "wastepacksMult", "bandwidthMult",
                "energyUseMult", "energyMaxMult", "consciousnessMult", "shootAccMult",
                "meleeDodgeMult", "manipulationMult", "meleeCooldownMult", "aimTimeMult",
                "moveSpeedMult", "meleeDamageMult", "skillCooldownMult", "bodySizeOffset",
                "protocolCooldownEndTick", "protocolSkillBonusLevels", "protocolSimulationCount",
                "lastDeathCheatCharges",
            };

            foreach (string fieldName in fields)
            {
                FieldInfo field = typeof(HediffComp_MechCustomization).GetField(fieldName)!;
                object? expected = field.GetValue(comp);
                object? actual = field.GetValue(loaded);
                Check(Equals(expected, actual), $"Scribe round-trip changed {fieldName}: {expected} -> {actual}.");
            }

            Console.WriteLine($"       Scribe round-trip: {fields.Length} fields (parameters + all derived coefficients) restored exactly.");
        }
        finally
        {
            Scribe.ForceStop();
            try
            {
                File.Delete(path);
            }
            catch
            {
                // Best effort; the temp file is harmless.
            }
        }
    }

    private static void InstallFakeWindowStack()
    {
        object root = RuntimeHelpers.GetUninitializedObject(typeof(Root_Play));
        object uiRoot = RuntimeHelpers.GetUninitializedObject(typeof(UIRoot_Play));
        UIRootWindowsField.SetValue(uiRoot, new WindowStack());
        RootUIRootField.SetValue(root, uiRoot);
        RootField.SetValue(null, root);
    }

    private static void SetSlider(Window_MechCustomization window, string fieldName, float value)
    {
        FieldInfo field = typeof(Window_MechCustomization).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance)!;
        field.SetValue(window, value);
    }

    private static bool IsSupportedPawn(Pawn pawn)
    {
        return (bool)IsSupportedPawnMethod.Invoke(null, new object?[] { pawn })!;
    }

    private static Window? CreateWindowFor(Pawn pawn)
    {
        return (Window?)CreateWindowForMethod.Invoke(null, new object?[] { pawn });
    }

    private static void GetCaps(
        Pawn pawn,
        out float supply,
        out float compute,
        out float electro,
        out float drive,
        out float subsystem)
    {
        MechCustomizationCapUtility.GetParamCaps(pawn, out supply, out compute, out electro, out drive, out subsystem);
    }

    private static void CheckCaps(
        float supply,
        float compute,
        float electro,
        float drive,
        float subsystem,
        float expectedSupply,
        float expectedCompute,
        float expectedElectro,
        float expectedDrive,
        float expectedSubsystem,
        string label)
    {
        CheckClose(supply, expectedSupply, label + " supply");
        CheckClose(compute, expectedCompute, label + " compute");
        CheckClose(electro, expectedElectro, label + " electro");
        CheckClose(drive, expectedDrive, label + " drive");
        CheckClose(subsystem, expectedSubsystem, label + " subsystem");
    }

    private static int HediffCount(Pawn pawn, HediffDef def)
    {
        return pawn.health.hediffSet.hediffs.Count(hediff => hediff.def == def);
    }

    private static bool HasOwnerPatch(MethodInfo method, bool postfix)
    {
        Patches? info = Harmony.GetPatchInfo(method);
        if (info == null)
        {
            return false;
        }

        return (postfix ? info.Postfixes : info.Prefixes).Any(patch => patch.owner == Owner);
    }

    private static HashSet<MethodBase> OwnedMethods()
    {
        return Harmony.GetAllPatchedMethods()
            .Where(method =>
            {
                Patches? info = Harmony.GetPatchInfo(method);
                return info != null
                    && info.Prefixes.Concat(info.Postfixes).Any(patch => patch.owner == Owner);
            })
            .ToHashSet();
    }

    private static string Describe(MethodBase method)
    {
        return method.DeclaringType?.Name + "." + method.Name;
    }

    private static string Describe(object? value)
    {
        return value == null ? "null" : value.GetType().Name;
    }

    private static void Section(string title)
    {
        Console.WriteLine();
        Console.WriteLine("== " + title + " ==");
    }

    private static void Note(string text)
    {
        Console.WriteLine("       " + text);
    }

    private static void Check(bool condition, string message)
    {
        checks++;
        if (!condition)
        {
            throw new InvalidOperationException("CHECK FAILED: " + message);
        }
    }

    private static void CheckClose(float actual, float expected, string label)
    {
        checks++;
        if (Math.Abs(actual - expected) > 0.0001f)
        {
            throw new InvalidOperationException(
                $"CHECK FAILED: {label}: expected {expected.ToString("0.####", CultureInfo.InvariantCulture)}, got {actual.ToString("0.####", CultureInfo.InvariantCulture)}.");
        }
    }
}
