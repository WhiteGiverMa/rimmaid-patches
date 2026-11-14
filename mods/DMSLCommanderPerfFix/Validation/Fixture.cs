using System.Reflection;
using System.Runtime.CompilerServices;
using DMS_Legion;
using DMS_Legion.AXF12;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;

namespace WhiteGiver.DMSL.CommanderPerfFix.Validation;

/// <summary>Real RimWorld/DMS objects; no XML loading, save IO, Unity scene or game loop.</summary>
internal sealed class Fixture
{
    internal const int CompCount = 20;
    internal const int SettlementCount = 1300;

    private static readonly FieldInfo FactionField = AccessTools.DeclaredField(typeof(Thing), "factionInt")!;
    private static readonly FieldInfo MapsField = AccessTools.DeclaredField(typeof(Game), "maps")!;
    private static readonly FieldInfo WorldField = AccessTools.DeclaredField(typeof(Game), "worldInt")!;
    private static int nextId = 1;

    internal Game Game = null!;
    internal Faction PlayerFaction = null!;
    internal DMSL_ModSettings Settings = null!;

    internal ThingDef CommanderDef = null!;
    internal ThingDef NonCommanderDef = null!;
    internal Pawn Commander = null!;
    internal Pawn ColdCommander = null!;
    internal Pawn NonCommander = null!;
    internal CompOverseerSubject CommanderSubject = null!;
    internal CompOverseerSubject ColdCommanderSubject = null!;
    internal CompOverseerSubject NonCommanderSubject = null!;
    internal Pawn Overseer = null!;
    internal MechanitorControlGroup ControlGroup = null!;

    internal AXF12ReconMissionManager Manager = null!;
    internal readonly List<Settlement> Settlements = new();
    internal Settlement Observed = null!;
    internal Map ObservedMap = null!;

    internal static Fixture Create()
    {
        Fixture fixture = new();
        fixture.InitEnvironment();
        fixture.InitDefs();
        fixture.InitPawns();
        fixture.InitWorldChecks();
        return fixture;
    }

    private void InitEnvironment()
    {
        Current.ProgramState = ProgramState.Entry;
        AccessTools.DeclaredField(typeof(ModsConfig), "biotechActive")!.SetValue(null, true);

        FieldInfo bindingNow = AccessTools.DeclaredField(typeof(DefOfHelper), "bindingNow")!;
        bindingNow.SetValue(null, true);
        try
        {
            Bind(typeof(FleshTypeDefOf), "Mechanoid", new FleshTypeDef { defName = "Mechanoid", isOrganic = false });
            Bind(typeof(FleshTypeDefOf), "Normal", new FleshTypeDef { defName = "Normal", isOrganic = true });
            Bind(typeof(MechWorkModeDefOf), "Work", new MechWorkModeDef { defName = "Work", label = "Work" });
            Bind(typeof(MechWorkModeDefOf), "Recharge", new MechWorkModeDef { defName = "Recharge", label = "Recharge" });
            Bind(typeof(MechWorkModeDefOf), "SelfShutdown", new MechWorkModeDef { defName = "SelfShutdown", label = "SelfShutdown" });
            Bind(typeof(PawnRelationDefOf), "Overseer", new PawnRelationDef
            {
                defName = "Overseer",
                label = "Overseer",
                workerClass = typeof(PawnRelationWorker_Overseer),
            });
        }
        finally
        {
            bindingNow.SetValue(null, false);
        }

        Settings = new DMSL_ModSettings { enableCommanderAutonomy = true };
        DMSL_ModSettings.settings = Settings;

        Game = (Game)RuntimeHelpers.GetUninitializedObject(typeof(Game));
        MapsField.SetValue(Game, new List<Map>());
        Game.components = new List<GameComponent>();
        Game.tickManager = new TickManager { gameStartAbsTick = 0 };
        Game.uniqueIDsManager = new UniqueIDsManager();
        Current.Game = Game;

        PlayerFaction = new Faction
        {
            def = new FactionDef { defName = "DMSLCommanderPerfFix_Player", isPlayer = true },
            loadID = 1,
        };
        FactionManager factions = (FactionManager)RuntimeHelpers.GetUninitializedObject(typeof(FactionManager));
        AccessTools.DeclaredField(typeof(FactionManager), "ofPlayer")!.SetValue(factions, PlayerFaction);
        AccessTools.DeclaredField(typeof(FactionManager), "allFactions")!
            .SetValue(factions, new List<Faction> { PlayerFaction });
        World world = (World)RuntimeHelpers.GetUninitializedObject(typeof(World));
        world.factionManager = factions;
        WorldField.SetValue(Game, world);

        // Put the manager after real unrelated Legion game components so a cache miss is a genuine list walk.
        for (int i = 0; i < 24; i++)
        {
            Game.components.Add(new GameComponent_CommanderAutonomy(Game));
        }

        Manager = new AXF12ReconMissionManager(Game);
        Game.components.Add(Manager);
    }

    private void InitDefs()
    {
        CommanderDef = NewMechDef("DMSL_Mech_Commander", hasMarker: true);
        NonCommanderDef = NewMechDef("DMSLCommanderPerfFix_OtherMech", hasMarker: false);
        DefDatabase<ThingDef>.Add(CommanderDef);
        DefDatabase<ThingDef>.Add(NonCommanderDef);
    }

    private void InitPawns()
    {
        Commander = NewPawn(CommanderDef);
        ColdCommander = NewPawn(CommanderDef);
        NonCommander = NewPawn(NonCommanderDef);
        CommanderSubject = Commander.GetComp<CompOverseerSubject>();
        ColdCommanderSubject = ColdCommander.GetComp<CompOverseerSubject>();
        NonCommanderSubject = NonCommander.GetComp<CompOverseerSubject>();

        Overseer = NewPawn(NewMechDef("DMSLCommanderPerfFix_Mechanitor", hasMarker: false));
        Overseer.mechanitor = new Pawn_MechanitorTracker(Overseer);
        ControlGroup = new MechanitorControlGroup(Overseer.mechanitor);
        Overseer.mechanitor.controlGroups.Add(ControlGroup);
        ControlGroup.AssignedMechs.Add(new AssignedMech { pawn = Commander });
        Overseer.mechanitor.ControlledPawns.Add(Commander);

        // Insert the real relation directly: this avoids unrelated DefOf/worker initialization while using the
        // exact DirectPawnRelation list consumed by upstream FindOverseer and vanilla get_Overseer.
        Commander.relations.DirectRelations.Add(new DirectPawnRelation(PawnRelationDefOf.Overseer, Overseer, 0));
    }

    private void InitWorldChecks()
    {
        for (int i = 0; i < SettlementCount; i++)
        {
            Settlement settlement = new() { ID = 1000 + i };
            Settlements.Add(settlement);
        }

        Observed = Settlements[777];
        Manager.SetObservedMap(Observed);
        ObservedMap = (Map)RuntimeHelpers.GetUninitializedObject(typeof(Map));
        ObservedMap.uniqueID = 1;
        ObservedMap.info = new MapInfo
        {
            parent = Observed,
            Size = new IntVec3(1, 1, 1),
        };
        Game.Maps.Add(ObservedMap);
    }

    internal Pawn CreateDynamicMarkerPawn()
    {
        ThingDef def = NewMechDef("DMSLCommanderPerfFix_FutureCommander", hasMarker: true);
        DefDatabase<ThingDef>.Add(def);
        return NewPawn(def);
    }

    private static ThingDef NewMechDef(string defName, bool hasMarker)
    {
        RaceProperties race = new()
        {
            intelligence = Intelligence.ToolUser,
            baseBodySize = 1f,
        };
        AccessTools.DeclaredField(typeof(RaceProperties), "fleshType")!.SetValue(race, FleshTypeDefOf.Mechanoid);

        ThingDef def = new()
        {
            defName = defName,
            label = defName,
            category = ThingCategory.Pawn,
            thingClass = typeof(Pawn),
            race = race,
            comps = new List<CompProperties>(),
            statBases = new List<StatModifier>(),
        };
        def.comps.Add(new CompProperties_OverseerSubject());
        if (hasMarker)
        {
            def.comps.Add(new CompProperties_MechCommanderMarker());
        }

        int fillerCount = CompCount - def.comps.Count;
        for (int i = 0; i < fillerCount; i++)
        {
            def.comps.Add(new CompProperties { compClass = typeof(ValidationComp) });
        }

        return def;
    }

    private Pawn NewPawn(ThingDef def)
    {
        Pawn pawn = new()
        {
            def = def,
            kindDef = new PawnKindDef { defName = def.defName + "_Kind", race = def },
            thingIDNumber = nextId++,
        };
        FactionField.SetValue(pawn, PlayerFaction);
        pawn.health = new Pawn_HealthTracker(pawn);
        pawn.relations = new Pawn_RelationsTracker(pawn);
        pawn.mindState = new Pawn_MindState(pawn);
        pawn.InitializeComps();
        return pawn;
    }

    private static void Bind(Type defOfType, string fieldName, Def value)
    {
        AccessTools.DeclaredField(defOfType, fieldName)!.SetValue(null, value);
    }
}

/// <summary>Concrete no-op filler used to make the real 20-comp fixture; never loaded by the runtime mod.</summary>
public sealed class ValidationComp : ThingComp
{
    public ValidationComp()
    {
    }
}
