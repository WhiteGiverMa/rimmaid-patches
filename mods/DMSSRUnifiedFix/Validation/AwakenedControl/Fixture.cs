using System.Reflection;
using DMSSR.Hediffs;
using Fortified;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.Grammar;

namespace DMSSRUnifiedFix.Validation.AwakenedControl;

internal sealed class Fixture
{
    internal readonly Audited.Fixture World = Audited.Fixture.Create();
    internal GameComponent_DMS Dms = null!;
    internal HediffDef Customization = null!;
    internal RulePackDef NameRule = null!;
    internal RulePackDef StoryRule = null!;

    internal Fixture()
    {
        AccessTools.Field(typeof(DefOfHelper), "bindingNow").SetValue(null, true);
        try
        {
            LoadedLanguage language = new("English") { info = new LanguageInfo { languageWorkerClass = typeof(LanguageWorker_English) } };
            LanguageDatabase.activeLanguage = language;
            LanguageDatabase.defaultLanguage = language;
            foreach (string name in new[] { "BandwidthCost", "MechBandwidth", "MechControlGroups", "WastepacksPerRecharge",
                "MoveSpeed", "ShootingAccuracyPawn", "MeleeDodgeChance", "AimingDelayFactor", "MeleeCooldownFactor",
                "MeleeDamageFactor", "MechEnergyUsageFactor", "MechEnergyLossPerHP" })
            {
                StatDef stat = DefDatabase<StatDef>.GetNamedSilentFail(name) ?? new StatDef { defName = name, label = name };
                if (!DefDatabase<StatDef>.AllDefsListForReading.Contains(stat)) DefDatabase<StatDef>.Add(stat);
                stat.defaultBaseValue = name == "WastepacksPerRecharge" ? 5f : name == "MechBandwidth" ? 8f : 1f;
                stat.minValue = 0f;
                stat.cacheable = true;
                stat.supressDisabledError = true;
                typeof(StatDefOf).GetField(name)!.SetValue(null, stat);
                stat.Worker.SetCacheability(false);
            }
            StatDefOf.WastepacksPerRecharge.postProcessStatFactors = new List<StatDef> { StatDefOf.BandwidthCost };
            World.MechDef.statBases.Single(mod => mod.stat == StatDefOf.BandwidthCost).value = 8f;
            World.MechDef.race.maxMechEnergy = 100;
            NameRule = Rules("AwakeName", "r_name->Awakened");
            StoryRule = Rules("AwakeStory", "_story->[Mech] visited [MechType].");
            RulePackDefOf.NamerTraderGeneral = NameRule;
            RulePackDefOf.GlobalUtility = Rules("GlobalUtility", "unused->unused");
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(typeof(JobDefOf).TypeHandle);
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(typeof(FFF_DefOf).TypeHandle);
            JobDefOf.MechCharge = new JobDef { defName = "MechCharge" };
            JobDefOf.AttackMelee = new JobDef { defName = "AttackMelee" };
            JobDefOf.AttackStatic = new JobDef { defName = "AttackStatic" };
            JobDefOf.Wait_Combat = new JobDef { defName = "Wait_Combat" };
            World.MechDef.comps.Add(new CompProperties_DeadManSwitch { minDelayUntilDMS = 1, nameRule = NameRule });
            foreach (string name in new[] { "Work", "Recharge", "SelfShutdown", "Escort" })
                typeof(MechWorkModeDefOf).GetField(name)!.SetValue(null, Mode(name));
            Customization = new HediffDef
            {
                defName = "DMS_MechCustomization", hediffClass = typeof(HediffWithComps),
                comps = new List<HediffCompProperties> { new HediffCompProperties_MechCustomization() },
            };
            DefDatabase<HediffDef>.Add(Customization);
            DMS_HediffDefOf.DMS_MechCustomization = Customization;
            LetterDefOf.PositiveEvent = new LetterDef { defName = "PositiveEvent", letterClass = typeof(ChoiceLetter) };
            WorldObjectDefOf.Settlement = new WorldObjectDef { defName = "Settlement", worldObjectClass = typeof(Settlement) };
            Current.Game.letterStack = new LetterStack();
            Current.Game.components = new List<GameComponent>();
            Dms = new GameComponent_DMS(Current.Game);
            Current.Game.components.Add(Dms);
            Find.World.worldObjects = new WorldObjectsHolder();
        }
        finally { AccessTools.Field(typeof(DefOfHelper), "bindingNow").SetValue(null, false); }
    }

    internal static MechWorkModeDef Mode(string name) => new() { defName = name, label = name };

    internal static RulePackDef Rules(string name, string rule)
    {
        RulePack pack = new();
        AccessTools.Field(typeof(RulePack), "rulesStrings").SetValue(pack, new List<string> { rule });
        pack.PostLoad();
        RulePackDef def = new() { defName = name };
        AccessTools.Field(typeof(RulePackDef), "rulePack").SetValue(def, pack);
        return def;
    }

    internal Pawn Mech(bool woken = false, bool dms = true)
    {
        Pawn mech = World.NewMech(new IntVec3(5, 0, 5));
        mech.jobs = null;
        CompDeadManSwitch comp = mech.GetComp<CompDeadManSwitch>();
        comp.woken = woken;
        if (!dms) mech.AllComps.Remove(comp);
        return mech;
    }

    internal static T CopyMech<T>(Pawn source) where T : Pawn, new()
    {
        T pawn = new();
        for (Type? type = typeof(Pawn); type != null; type = type.BaseType)
            foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                field.SetValue(pawn, field.GetValue(source));
        foreach (ThingComp comp in pawn.AllComps) comp.parent = pawn;
        return pawn;
    }

    internal Pawn Mechanitor(float bandwidth = 8f)
    {
        Pawn pawn = World.NewColonist(new IntVec3(6, 0, 5));
        World.GiveHediffDirect(pawn, HediffDefOf.MechlinkImplant);
        pawn.mechanitor = new Pawn_MechanitorTracker(pawn);
        // Give each actor its own Def so saturated and zero-capacity trackers coexist.
        pawn.def = new ThingDef
        {
            defName = "AwakeController" + pawn.thingIDNumber, label = "controller", category = ThingCategory.Pawn,
            thingClass = typeof(Pawn), race = World.HumanDef.race,
            statBases = new List<StatModifier>
            {
                new() { stat = StatDefOf.MechBandwidth, value = bandwidth },
                new() { stat = StatDefOf.MechControlGroups, value = 1f },
            },
        };
        return pawn;
    }

    internal static MechanitorControlGroup Enroll(Pawn actor, Pawn mech)
    {
        // Exercise the real relation worker, native group assignment and bandwidth accounting.
        actor.relations.AddDirectRelation(PawnRelationDefOf.Overseer, mech);
        actor.mechanitor.Notify_BandwidthChanged();
        return mech.GetMechControlGroup();
    }

    internal HediffComp_MechCustomization Customize(Pawn mech)
    {
        Hediff hediff = World.GiveHediffDirect(mech, Customization);
        return HediffUtility.TryGetComp<HediffComp_MechCustomization>(hediff);
    }

    internal Settlement Settlement(Faction faction)
    {
        Settlement settlement = new() { def = WorldObjectDefOf.Settlement, Name = "Settlement" };
        AccessTools.Field(typeof(WorldObject), "factionInt").SetValue(settlement, faction);
        Find.World.worldObjects.AllWorldObjects.Add(settlement);
        return settlement;
    }
}
