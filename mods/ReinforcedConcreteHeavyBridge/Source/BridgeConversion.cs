using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using Verse;

namespace WhiteGiver.ReinforcedConcreteHeavyBridge;

internal static class BridgeConversion
{
    private static readonly FieldInfo? TerrainVerbsField = typeof(Pawn_MeleeVerbs)
        .GetField("terrainVerbs", BindingFlags.Instance | BindingFlags.NonPublic);

    internal static void Confirm()
    {
        if (!Ready())
        {
            return;
        }
        Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("RCHB_Confirm".Translate(), () =>
        {
            if (!Ready())
            {
                return;
            }
            try
            {
                Map[] maps = Find.Maps.ToArray();
                var result = Convert(maps, CollectPawns(maps));
                Find.WindowStack.Add(new Dialog_MessageBox("RCHB_Converted".Translate(result.Bridges, result.Sites, result.Pawns)));
            }
            catch (Exception exception)
            {
                Log.Error($"[ReinforcedConcreteHeavyBridge] Conversion failed. Do not remove the mod. {exception}");
                Find.WindowStack.Add(new Dialog_MessageBox("RCHB_Failed".Translate()));
            }
        }, destructive: true, title: "RCHB_ConfirmTitle".Translate()));
    }

    private static bool Ready()
    {
        if (Current.Game != null && Current.ProgramState == ProgramState.Playing && Scribe.mode == LoadSaveMode.Inactive)
        {
            return true;
        }
        Messages.Message("RCHB_LoadGame".Translate(), MessageTypeDefOf.RejectInput, false);
        return false;
    }

    internal static (int Bridges, int Sites, int Pawns) Convert(Map[] maps, IEnumerable<Pawn> pawns)
    {
        if (Current.ProgramState != ProgramState.Playing || Scribe.mode != LoadSaveMode.Inactive)
        {
            throw new InvalidOperationException("Conversion requires a loaded, idle game so frame cancellation can refund materials.");
        }
        if (TerrainVerbsField?.FieldType != typeof(Pawn_MeleeVerbs_TerrainSource))
        {
            throw new MissingFieldException(typeof(Pawn_MeleeVerbs).FullName, "terrainVerbs");
        }
        TerrainDef custom = DefDatabase<TerrainDef>.GetNamed(BridgeCost.DefName);
        TerrainDef vanilla = DefDatabase<TerrainDef>.GetNamed("HeavyBridge");
        var plans = new List<(Map Map, int[] Cells)>();
        foreach (Map map in maps)
        {
            var cells = new List<int>();
            for (int i = 0; i < map.cellIndices.NumGridCells; i++)
            {
                TerrainGrid grid = map.terrainGrid;
                if (ReferenceEquals(grid.topGrid[i], custom) || ReferenceEquals(grid.UnderTerrainAt(i), custom) ||
                    ReferenceEquals(grid.TempTerrainAt(i), custom))
                {
                    throw new InvalidOperationException($"Custom bridge outside the foundation layer on {map}, cell {i}; no conversion performed.");
                }
                if (ReferenceEquals(grid.foundationGrid[i], custom)) cells.Add(i);
            }
            plans.Add((map, cells.ToArray()));
        }
        Pawn[] allPawns = pawns.Distinct<Pawn>(ReferenceEqualityComparer.Instance).ToArray();
        Pawn_MeleeVerbs_TerrainSource[] sources = allPawns
            .Select(pawn => pawn.meleeVerbs == null ? null : TerrainVerbsField.GetValue(pawn.meleeVerbs) as Pawn_MeleeVerbs_TerrainSource)
            .Where(source => source != null && ReferenceEquals(source.def, custom))
            .Cast<Pawn_MeleeVerbs_TerrainSource>().Distinct().ToArray();

        int sites = 0;
        foreach (Map map in maps)
        {
            foreach (Thing thing in map.listerThings.AllThings.ToArray())
            {
                if (IsCustomSite(thing, custom) && thing.Spawned && thing.Map == map)
                {
                    thing.Destroy(DestroyMode.Cancel);
                    sites++;
                }
            }
        }
        foreach (var plan in plans)
        {
            foreach (int index in plan.Cells) plan.Map.terrainGrid.foundationGrid[index] = vanilla;
        }
        foreach (Pawn_MeleeVerbs_TerrainSource source in sources) source.def = vanilla;

        foreach (var plan in plans)
        {
            foreach (int index in plan.Cells)
            {
                IntVec3 cell = plan.Map.cellIndices.IndexToCell(index);
                // ponytail: 同属性地基替换；不用会移除临时层/地板指定的 setter。地形机制改变时复核这些通知。
                plan.Map.mapDrawer.MapMeshDirty(cell, MapMeshFlagDefOf.Terrain, true, false);
                plan.Map.pathing.RecalculatePerceivedPathCostAt(cell);
                plan.Map.regionGrid.GetRegionAt_NoRebuild_InvalidAllowed(cell)?.District?.Notify_TerrainChanged();
                plan.Map.events.Notify_TerrainChanged(cell);
            }
            if (plan.Map.terrainGrid.foundationGrid.Any(terrain => ReferenceEquals(terrain, custom)) ||
                plan.Map.listerThings.AllThings.Any(thing => IsCustomSite(thing, custom)))
            {
                throw new InvalidOperationException("Custom bridge references remain after conversion. Do not remove the mod.");
            }
        }
        if (allPawns.Any(pawn => pawn.meleeVerbs != null &&
            TerrainVerbsField.GetValue(pawn.meleeVerbs) is Pawn_MeleeVerbs_TerrainSource source && ReferenceEquals(source.def, custom)))
        {
            throw new InvalidOperationException("A pawn still references the custom bridge. Do not remove the mod.");
        }
        return (plans.Sum(plan => plan.Cells.Length), sites, sources.Length);
    }

    private static bool IsCustomSite(Thing thing, TerrainDef custom) =>
        !thing.Destroyed && thing is Blueprint_Build or Frame && ReferenceEquals(thing.def.entityDefToBuild, custom);

    private static HashSet<Pawn> CollectPawns(Map[] maps)
    {
        var pawns = new HashSet<Pawn>(PawnsFinder.All_AliveOrDead, ReferenceEqualityComparer.Instance);
        var things = new List<Thing>();
        var holders = new List<IThingHolder>();
        foreach (Map map in maps) Collect(map);
        Collect(Find.World);
        foreach (TransportShip ship in Find.TransportShipManager.AllTransportShips)
        {
            if (ship.shipThing != null) CollectThings(new[] { ship.shipThing });
        }
        if (Find.CurrentGravship != null) CollectThings(Find.CurrentGravship.Things.Concat(Find.CurrentGravship.Pawns).ToArray());
        return pawns;

        void Collect(IThingHolder holder)
        {
            ThingOwnerUtility.GetAllThingsRecursively(holder, things, allowUnreal: true);
            foreach (Pawn pawn in things.OfType<Pawn>()) pawns.Add(pawn);
        }
        void CollectThings(Thing[] roots)
        {
            foreach (Pawn pawn in roots.OfType<Pawn>()) pawns.Add(pawn);
            holders.Clear();
            ThingOwnerUtility.AppendThingHoldersFromThings(holders, roots);
            foreach (IThingHolder holder in holders) Collect(holder);
        }
    }
}
