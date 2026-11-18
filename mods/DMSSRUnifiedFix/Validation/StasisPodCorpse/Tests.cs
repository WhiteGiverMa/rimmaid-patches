using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using DMSSR.Content;
using DMSSR.Gene;
using DMSSR.Hediffs;
using DMSSR.UI;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using WhiteGiver.DMSSRUnifiedFix;
using WhiteGiver.DMSSRUnifiedFix.Neuroglue;
using WhiteGiver.DMSSRUnifiedFix.StatPathPerf;
using WhiteGiver.DMSSRUnifiedFix.StasisPodCorpse;
using WhiteGiver.DMSSRUnifiedFix.LegionCompat;
using WhiteGiver.DMSSRUnifiedFix.ScourgeDeath;

namespace DMSSRUnifiedFix.Validation.StasisPodCorpse;

internal static class Tests
{
    private static Neuroglue.Fixture fx = null!;
    private static int assertions;
    private static readonly Harmony Harness = new(Program.ValidationOwner + ".carry");
    private static readonly Selector Selection = new();
    private static string errorStack = "";
    private static bool guardedRemoval;

    internal static void Run(string mode)
    {
        Setup();
        bool green = mode != "red";
        if (green) DMSSRUnifiedFixMod.ApplyPatches();
        Check(CorpseCarryPatches.Installed == green, "corpse subsystem install state");
        Scenario(green, reservations: true, forced: true, queued: false);
        if (green)
        {
            Scenario(true, reservations: true, forced: false, queued: true);
            Scenario(true, reservations: false, forced: false, queued: false);
            foreach (int count in new[] { 0, -7, 1, 3 })
            {
                var (driver, _, _) = NewJob(count, false);
                Check(driver.TryMakePreToilReservations(false) && driver.job.count == (count <= 0 ? 1 : count), "reservation count boundary " + count);
                driver.job.count = count;
                IEnumerable<Toil> iterator = (IEnumerable<Toil>)AccessTools.Method(driver.GetType(),"MakeNewToils").Invoke(driver,null)!;
                Check(driver.job.count == (count <= 0 ? 1 : count), "iterator factory eagerly normalizes count " + count);
                _ = iterator.ToList();
                Check(driver.job.count == (count <= 0 ? 1 : count), "reload count boundary " + count);
            }
            SharedInvalidation();
            JobScribeReload();
        }
        Console.WriteLine($"PASS CARRY {mode.ToUpperInvariant()}: {assertions} assertions.");
    }

    internal static void NoLegion()
    {
        Setup();
        DMSSRUnifiedFixMod.ApplyPatches();
        Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a=>a.GetName().Name == "DMS_Legion"), "Legion DLL was not loaded");
        Check(!typeof(DMSSRUnifiedFixMod).Assembly.GetReferencedAssemblies().Any(a=>a.Name == "DMS_Legion"), "no hard Legion assembly reference");
        Scenario(true, true, true, false);
        SharedInvalidation();
        Console.WriteLine($"PASS NO-LEGION: {assertions} assertions.");
    }

    internal static void Isolation()
    {
        Setup();
        Harness.Patch(AccessTools.Method(typeof(DeathHediffRemoval),"Apply"), prefix:new HarmonyMethod(typeof(Tests),nameof(MissingDeathSignature)));
        LogCapture.ExpectedWarningFragments.Add("Scourge death installation failed; other subsystems continue.");
        DMSSRUnifiedFixMod.ApplyPatches();
        Check(PathCostPatches.SingleClampInstalled && MechCustomizationPatches.IsSupportedPawnInstalled
            && UseNeurogluePatches.MakeNewToilsPatched && CorpseCarryPatches.Installed,"one missing subsystem leaves the other four installed");
        Check(!Harmony.GetAllPatchedMethods().Any(m=>Harmony.GetPatchInfo(m)!.Transpilers.Any(p=>p.owner==Program.Owner && p.PatchMethod.DeclaringType==typeof(DeathHediffRemoval))),"failed death subsystem leaves its upstream methods untouched");
        Scenario(true,true,true,false);
        SharedInvalidation();
        Check(LogCapture.Warnings.Count==1,"exactly one clear subsystem failure warning");
        Console.WriteLine($"PASS ISOLATION: {assertions} assertions.");
    }

    internal static void Composition()
    {
        Setup();
        foreach (string name in new[] { "MoveSpeed","ShootingAccuracyPawn","MeleeDodgeChance","AimingDelayFactor","MeleeCooldownFactor",
            "MeleeDamageFactor","MechEnergyUsageFactor","MechEnergyLossPerHP","WastepacksPerRecharge","BandwidthCost" })
        {
            StatDef stat=new() {defName=name,defaultBaseValue=1f,workerClass=typeof(StatWorker),cacheable=false};
            DefDatabase<StatDef>.Add(stat); typeof(StatDefOf).GetField(name)!.SetValue(null,stat);
        }
        HediffDef customization=new() {defName="DMS_MechCustomization",hediffClass=typeof(HediffWithComps),comps=new(){new HediffCompProperties_MechCustomization()}};
        DefDatabase<HediffDef>.Add(customization); DMS_HediffDefOf.DMS_MechCustomization=customization;
        DMSSRUnifiedFixMod.ApplyPatches();
        int owned = Harmony.GetAllPatchedMethods().Count(m=>Harmony.GetPatchInfo(m)!.Owners.Contains(Program.Owner));
        Check(owned==52,"combined installer owns exactly 52 reviewed methods (actual " + owned + ")");
        Pawn mech=fx.SpawnColonyMech(new IntVec3(9,0,9));
        mech.def.defName="DMSL_Mech_Validation";
        Check((bool)AccessTools.Method(typeof(MechCustomizationUtility),"IsSupportedPawn").Invoke(null,new object[]{mech})!,"Legion predicate admits shared composition pawn");
        HediffComp_MechCustomization comp=MechCustomizationUtility.GetOrCreateCustomizationComp(mech)!;
        comp.moveSpeedMult=1.5f;
        Check(mech.GetStatValue(StatDefOf.MoveSpeed).Equals(1.5f),"real stat entry applies live Legion customization through health snapshot");
        fx.EnsureReservoirWithFuel(mech,90f); fx.AddDegenerativeToParts(mech,2);
        ThingWithComps gel=fx.SpawnNeuroglue(new IntVec3(10,0,9),3);
        JobGiver_AutoUseNeuroglue giver=new();
        Job? auto=(Job?)AccessTools.Method(typeof(JobGiver_AutoUseNeuroglue),"TryGiveJob").Invoke(giver,new object[]{mech});
        Check(auto?.targetB.Thing==gel && auto.count==1,"real auto-neuroglue giver cures customized Legion pawn at 90 fuel");
        Thing dose=fx.TakeOneToCarry(mech,gel);
        Neuroglue.Fixture.BuildFinalUseNeuroglueToil(mech,mech,dose,out _).initAction();
        Neuroglue.Fixture.TickHediffsOnce(mech);
        Check(!mech.health.hediffSet.HasHediff(fx.DegenerativeDef) && mech.health.hediffSet.HasHediff(fx.NeuroglueHighDef),"real neuroglue final toil cures shared pawn");
        Check(ReferenceEquals(HediffFlagCache.GetMechCustomization(mech),comp) && mech.GetStatValue(StatDefOf.MoveSpeed).Equals(1.5f),"neuroglue invalidation retains live stat customization");
        HediffDef born=new() {defName="DMSSR_BornFromDeath",hediffClass=typeof(HediffWithComps)};
        DefDatabase<HediffDef>.Add(born); mech.def.defName="DMS_Mech_Scourge"; mech.health.AddHediff(born);
        Harness.Patch(AccessTools.Method(typeof(Pawn_HealthTracker),nameof(Pawn_HealthTracker.RemoveHediff)), prefix:new HarmonyMethod(typeof(Tests),nameof(ObserveRemoval)));
        AccessTools.Method(typeof(Patch_ScourgeNextDeath),"Prefix").Invoke(null,new object[]{mech});
        Check(guardedRemoval && !mech.health.isBeingKilled && !mech.health.hediffSet.HasHediff(born),"real Scourge prefix removal is scoped and invalidates shared health normally");
        Scenario(true,true,true,false);
        SharedInvalidation();
        Console.WriteLine($"PASS COMPOSITION: {assertions} assertions across all five subsystems.");
    }

    private static void Setup()
    {
        foreach ((Type type,string name,string prefix) in new[]
        {
            (typeof(Thing), "DeSpawn", nameof(Despawn)),
            (typeof(Pawn), "DeSpawn", nameof(Despawn)),
            (typeof(Thing), "SpawnSetup", nameof(Spawn)),
            (typeof(ResourceCounter), "UpdateResourceCounts", nameof(Skip)),
            (typeof(Corpse), "NotifyColonistBar", nameof(Skip)),
            (typeof(ThingOwner), "NotifyColonistBarIfColonistCorpse", nameof(Skip)),
            (typeof(PawnRenderer), "SetAllGraphicsDirty", nameof(Skip)),
        })
            Harness.Patch(AccessTools.Method(type,name), prefix:new HarmonyMethod(typeof(Tests),prefix));
        Harness.Patch(AccessTools.PropertyGetter(typeof(Find),nameof(Find.Selector)), prefix:new HarmonyMethod(typeof(Tests),nameof(GetSelector)));
        foreach (MethodInfo sound in AccessTools.GetDeclaredMethods(typeof(Verse.Sound.SoundStarter)).Where(m => m.Name == "PlayOneShot"))
            Harness.Patch(sound, prefix: new HarmonyMethod(typeof(Tests), nameof(Skip)));
        Harness.Patch(AccessTools.Method(typeof(Toils_Haul),nameof(Toils_Haul.ErrorCheckForCarry)), prefix:new HarmonyMethod(typeof(Tests),nameof(CheckCountStack)));
        fx=Neuroglue.Fixture.Create();
        fx.Map.spawnedThings=new ThingOwner<Thing>(fx.Map);
        fx.Map.resourceCounter=new ResourceCounter(fx.Map);
        AccessTools.Field(typeof(DefOfHelper),"bindingNow").SetValue(null,true);
        RuntimeHelpers.RunClassConstructor(typeof(ThingDefOf).TypeHandle);
        RecordDefOf.ThingsHauled=new RecordDef { defName="ThingsHauled",type=RecordType.Int };
        DefDatabase<RecordDef>.Add(RecordDefOf.ThingsHauled);
        AccessTools.Field(typeof(DefOfHelper),"bindingNow").SetValue(null,false);
    }

    private static void Scenario(bool green, bool reservations, bool forced, bool queued)
    {
        var (driver,corpse,comp)=NewJob(-1,forced);
        Pawn hauler=driver.pawn;
        if (queued)
        {
            JobQueue queue=new();
            queue.EnqueueLast(driver.job);
            driver.job=queue.Dequeue().job;
            hauler.jobs.curJob=driver.job;
            Check(driver.job.count == -1,"queued job preserves upstream unset count until dispatch");
        }
        Check(driver.job.count == -1,"new job defaults to count=-1");
        if(reservations)
        {
            Check(driver.TryMakePreToilReservations(false),"real pod and corpse reservations succeed");
            Check(driver.job.count == (green ? 1 : -1),"reservation entry normalizes only with patch");
        }
        else
        {
            // Reload restores an already reserved running job; it need not re-enter the driver's
            // reservation method. Populate that persisted reservation state with the real API.
            Check(hauler.Reserve(driver.job.targetA,driver.job) && hauler.Reserve(driver.job.targetB,driver.job),"reload retains its target reservations");
        }
        List<Toil> toils=Toils(driver);
        Check(toils.Count==4,"real driver yields four unchanged toils");
        Check(driver.job.count==(green ? 1 : -1),"MakeNewToils reload entry normalizes before carry");
        int errorsBefore=LogCapture.Errors.Count;
        toils[1].actor=hauler;
        toils[1].initAction();
        if(!green)
        {
            string error=LogCapture.Errors.Skip(errorsBefore).Single();
            Check(error.StartsWith("Invalid count: -1, setting to 1. Job was DMSSR_CarryCorpseToStasisPod ",StringComparison.Ordinal),"exact reported RED error");
            Check(errorStack.Contains("Toils_Haul.ErrorCheckForCarry",StringComparison.Ordinal),"error originates inside real ErrorCheckForCarry");
            Console.WriteLine("RED STACK\n"+errorStack);
        }
        else Check(LogCapture.Errors.Count==errorsBefore,"carry emits zero Log.Error");
        Check(hauler.carryTracker.CarriedThing==corpse && !corpse.Spawned,"real carry toil transfers corpse into carry tracker");
        Check(driver.job.count==1,"carry keeps normalized count 1");
        toils[3].actor=hauler;
        toils[3].initAction();
        Check(comp.HasCorpse && comp.Busy && comp.WorkLeft==600f,"real matrix dissolution accepts corpse and starts work");
        Check(comp.GetDirectlyHeldThings().Contains(corpse) && hauler.carryTracker.CarriedThing==null && !corpse.Spawned,"corpse has one owning pod container after real final toil");
        Check(LogCapture.Errors.Count==errorsBefore+(green?0:1),"no extra errors during final acceptance");
    }

    private static (JobDriver_CarryCorpseToStasisPod,Corpse,CompMatrixDissolution) NewJob(int count,bool forced)
    {
        Pawn hauler=fx.SpawnColonist(new IntVec3(3,0,3));
        hauler.records=new Pawn_RecordsTracker(hauler);
        Pawn inner=fx.SpawnColonist(new IntVec3(4,0,3));
        fx.DespawnThing(inner);
        inner.health.SetDead();
        Corpse corpse=new()
        {
            def=new ThingDef {defName="Corpse_Human",label="corpse",thingClass=typeof(Corpse),category=ThingCategory.Item,
                stackLimit=1,comps=new(),useHitPoints=false,passability=Traversability.Standable},
            thingIDNumber=900+assertions,
        };
        corpse.InnerPawn=inner;
        Register(corpse,new IntVec3(4,0,3));
        Building_DMSGeneFittingPod pod=new()
        {
            def=new ThingDef {defName="DMS_GeneFittingPod",label="pod",thingClass=typeof(Building_DMSGeneFittingPod),category=ThingCategory.Building,
                size=new IntVec2(1,1),comps=new() { new CompProperties_MatrixDissolution {corpseWork=600f} }},
            thingIDNumber=1900+assertions,
        };
        pod.InitializeComps();
        Register(pod,new IntVec3(5,0,3));
        Job job=JobMaker.MakeJob(new JobDef {defName="DMSSR_CarryCorpseToStasisPod",driverClass=typeof(JobDriver_CarryCorpseToStasisPod)},pod,corpse);
        job.count=count; job.playerForced=forced;
        job.jobGiver = new ThinkNode_QueuedJob();
        JobDriver_CarryCorpseToStasisPod driver=new() {pawn=hauler,job=job};
        hauler.jobs.curJob=job; hauler.jobs.curDriver=driver;
        return (driver,corpse,pod.GetComp<CompMatrixDissolution>());
    }

    private static List<Toil> Toils(JobDriver driver) => ((IEnumerable<Toil>)AccessTools.Method(driver.GetType(),"MakeNewToils").Invoke(driver,null)!).ToList();

    private static void SharedInvalidation()
    {
        Pawn pawn=fx.SpawnColonyMech(new IntVec3(8,0,8));
        _=HediffFlagCache.GetMechCustomization(pawn);
        MechBiogelCache.Entry entry=MechBiogelCache.GetSnapshot(pawn)!;
        int generation=HediffFlagCache.Generation;
        entry.NextSearchTick=int.MaxValue;
        pawn.health.hediffSet.DirtyCache();
        Check(HediffFlagCache.Generation==generation+1 && entry.Dirty && entry.NextSearchTick==int.MinValue,"single DirtyCache signal invalidates both health caches and search backoff");
        var info=Harmony.GetPatchInfo(AccessTools.Method(typeof(HediffSet),nameof(HediffSet.DirtyCache),Type.EmptyTypes))!;
        Check(info.Postfixes.Count(p=>p.owner==Program.Owner)==1,"exactly one unified DirtyCache postfix");
    }

    private static void JobScribeReload()
    {
        string path=Path.Combine(Path.GetTempPath(),"dmssr-unified-validation","job-scribe-fixture.xml");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        JobDef def=new() {defName="Validation_CorpseJob",driverClass=typeof(JobDriver_CarryCorpseToStasisPod)};
        DefDatabase<JobDef>.Add(def);
        Job original=JobMaker.MakeJob(def);
        original.count=-1; original.playerForced=true;
        try
        {
            Scribe.saver.InitSaving(path,"fixture"); Scribe_Deep.Look(ref original,"job"); Scribe.saver.FinalizeSaving();
            Job loaded=null!;
            Scribe.loader.InitLoading(path); Scribe_Deep.Look(ref loaded,"job"); Scribe.loader.FinalizeLoading();
            Check(loaded.count==-1 && loaded.playerForced && loaded.def==def,"real Job.ExposeData round-trip preserves legacy count=-1");
            var (driver,_,_)=NewJob(-1,true);
            loaded.targetA=driver.job.targetA; loaded.targetB=driver.job.targetB;
            driver.job=loaded; driver.pawn.jobs.curJob=loaded;
            _=Toils(driver);
            Check(loaded.count==1,"Scribe-loaded legacy job is repaired through MakeNewToils without reservation replay");
        }
        finally {Scribe.ForceStop();}
    }

    private static void Register(Thing thing,IntVec3 cell)
    {
        AccessTools.Field(typeof(Thing),"positionInt").SetValue(thing,cell);
        AccessTools.Field(typeof(Thing),"mapIndexOrState").SetValue(thing,(sbyte)0);
        fx.Map.spawnedThings.TryAdd(thing,false);
        fx.Map.thingGrid.Register(thing); fx.Map.listerThings.Add(thing);
    }
    private static bool Spawn(Thing __instance,Map map) { Register(__instance,__instance.Position); return false; }
    private static bool Despawn(Thing __instance)
    {
        fx.Map.spawnedThings.Remove(__instance);
        fx.DespawnThing(__instance);
        return false;
    }
    private static bool GetSelector(ref Selector __result) {__result=Selection;return false;}
    private static bool Skip()=>false;
    private static void MissingDeathSignature()=>throw new MissingMethodException("QA simulated upstream death signature change");
    private static void ObserveRemoval(Pawn_HealthTracker __instance,Hediff hediff)
    {
        if(hediff.def.defName=="DMSSR_BornFromDeath") guardedRemoval=__instance.isBeingKilled;
    }
    private static void CheckCountStack(Pawn pawn)
    {
        if(pawn.CurJob.count<=0) errorStack=new StackTrace(true).ToString();
    }
    private static void Check(bool value,string label)
    {
        if(!value) throw new InvalidOperationException("CARRY FAIL "+label);
        assertions++; Console.WriteLine("PASS "+label);
    }
}
