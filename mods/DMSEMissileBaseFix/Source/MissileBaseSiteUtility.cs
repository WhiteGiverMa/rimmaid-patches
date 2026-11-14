using System;
using System.Collections.Generic;
using DMSE;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace WhiteGiver.DMSEMissileBaseFix;

internal static class MissileBaseSiteUtility
{
    private static readonly string[] KnownMissileBaseDefNames =
    {
        "DMSE_MissileBase",
        "DMSE_FieldLaunchSite",
        "DMSE_EarlyLaunchSite",
    };

    internal static bool IsMissileBaseSite(MapParent? parent)
    {
        if (parent == null)
        {
            return false;
        }

        List<WorldObjectComp> comps = parent.AllComps;
        if (comps != null)
        {
            for (int i = 0; i < comps.Count; i++)
            {
                if (comps[i] is WorldObjectComp_MissileBase)
                {
                    return true;
                }
            }
        }

        return parent.def != null && IsMissileBaseDef(parent.def);
    }

    internal static bool IsMissileBaseMap(Map? map)
    {
        return map != null && IsMissileBaseSite(map.Parent);
    }

    internal static bool IsMissileBaseDef(WorldObjectDef def)
    {
        List<WorldObjectCompProperties>? comps = def.comps;
        if (comps != null)
        {
            for (int i = 0; i < comps.Count; i++)
            {
                if (comps[i] is CompProperties_MissileBase)
                {
                    return true;
                }
            }
        }

        string? defName = def.defName;
        if (!string.IsNullOrEmpty(defName))
        {
            for (int i = 0; i < KnownMissileBaseDefNames.Length; i++)
            {
                if (string.Equals(defName, KnownMissileBaseDefNames[i], StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }

    internal static int EnableMissileBaseLaunchTargets()
    {
        List<WorldObjectDef> defs = DefDatabase<WorldObjectDef>.AllDefsListForReading;
        if (defs == null)
        {
            return 0;
        }

        int enabledCount = 0;
        for (int i = 0; i < defs.Count; i++)
        {
            WorldObjectDef? def = defs[i];
            if (def == null || !IsMissileBaseDef(def))
            {
                continue;
            }

            def.validLaunchTarget = true;
            enabledCount++;
        }

        return enabledCount;
    }

    /// <summary>
    /// A dry ground cannon does not make a loaded, powered SAM launcher or a fueled CIWS safe to
    /// dismantle remotely. This check deliberately uses DMSE's own operational predicates.
    /// </summary>
    internal static bool HasOperationalAirDefense(Building building)
    {
        CompMissileLauncher_Interceptor? interceptor = building.GetComp<CompMissileLauncher_Interceptor>();
        if (interceptor != null && interceptor.Active)
        {
            return true;
        }

        CompTerminalCIWS? ciws = building.GetComp<CompTerminalCIWS>();
        if (ciws == null || !ciws.Active)
        {
            return false;
        }

        CompRefuelable? refuelable = building.GetComp<CompRefuelable>();
        return refuelable == null || refuelable.Fuel >= ciws.Props.fuelPerShot;
    }
}
