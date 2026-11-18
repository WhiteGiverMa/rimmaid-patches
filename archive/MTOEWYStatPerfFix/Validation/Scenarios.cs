using System;
using System.Collections.Generic;
using WhiteGiver.MTOEWYStatPerfFix;
using Verse;

namespace MTOEWYStatPerfFix.Validation;

internal sealed class Scenario
{
    internal readonly string Name;
    internal readonly Pawn Pawn;
    internal readonly Action<Fixture> Prepare;

    internal Scenario(string name, Pawn pawn, Action<Fixture> prepare)
    {
        Name = name;
        Pawn = pawn;
        Prepare = prepare;
    }
}

internal static class Scenarios
{
    internal static List<Scenario> CreateAll(Fixture f)
    {
        List<Scenario> list = new();

        void Add(string name, Pawn pawn, Action<Fixture> prepare) => list.Add(new Scenario(name, pawn, prepare));

        Add("ideal_backstory", f.Ideal, fx =>
        {
            fx.Reset();
            fx.SetIdealBackstory(true);
        });

        Add("ideal_no_backstory", f.Ideal, fx =>
        {
            fx.Reset();
            fx.SetIdealBackstory(false);
        });

        Add("ideal_unspawned", f.Ideal, fx =>
        {
            fx.Reset();
            fx.SetIdealBackstory(true);
            fx.AddTrait(fx.Ideal, UpstreamLogic.TraitSkystriker);
            fx.AddTrait(fx.Ideal, UpstreamLogic.TraitDawnAegis);
            fx.SetBurstActive(true);
            fx.SetSpawned(fx.Ideal, false);
        });

        Add("ideal_dead", f.Ideal, fx =>
        {
            fx.Reset();
            fx.SetIdealBackstory(true);
            fx.AddTrait(fx.Ideal, UpstreamLogic.TraitSkystriker);
            fx.GiveWeapon(fx.Ideal, UpstreamLogic.ItemStarlightLance);
            fx.WearApparel(fx.Ideal, UpstreamLogic.ItemNightwingSuit);
            fx.SetDead(fx.Ideal);
        });

        Add("ideal_discarded", f.Ideal, fx =>
        {
            fx.Reset();
            fx.SetIdealBackstory(true);
            fx.SetDiscarded(fx.Ideal);
        });

        Add("ideal_kit", f.Ideal, fx =>
        {
            fx.Reset();
            fx.SetIdealBackstory(true);
            fx.AddTrait(fx.Ideal, UpstreamLogic.TraitSkystriker);
            fx.AddTrait(fx.Ideal, UpstreamLogic.TraitDawnAegis);
            fx.AddTrait(fx.Ideal, UpstreamLogic.TraitStarlightTinkerer);
            fx.AddTrait(fx.Ideal, UpstreamLogic.TraitInquisitiveMind);
            fx.SetBurstActive(true);
            fx.SetStudy(true);
            fx.SetRepairJob(fx.Ideal);
            fx.GiveWeapon(fx.Ideal, UpstreamLogic.ItemStarlightLance);
            fx.WearApparel(fx.Ideal, UpstreamLogic.ItemNightwingSuit);
        });

        Add("ideal_lance", f.Ideal, fx =>
        {
            fx.Reset();
            fx.SetIdealBackstory(true);
            fx.AddTrait(fx.Ideal, UpstreamLogic.TraitDawnAegis);
            fx.GiveWeapon(fx.Ideal, UpstreamLogic.ItemStarlightLance);
        });

        Add("plain_gear", f.Plain, fx =>
        {
            fx.Reset();
            fx.AddTrait(fx.Plain, UpstreamLogic.TraitSkystriker);
            fx.AddTrait(fx.Plain, UpstreamLogic.TraitInquisitiveMind);
            fx.SetBurstActive(true);
            fx.SetStudy(true);
            fx.GiveWeapon(fx.Plain, UpstreamLogic.ItemStarlightRailgun);
            fx.WearApparel(fx.Plain, UpstreamLogic.ItemNightwingSuit);
        });

        Add("plain_railgun", f.Plain, fx =>
        {
            fx.Reset();
            fx.GiveWeapon(fx.Plain, UpstreamLogic.ItemStarlightRailgun);
        });

        Add("plain_bare", f.Plain, fx => fx.Reset());

        Add("animal", f.Animal, fx => fx.Reset());

        Add("mech", f.Mech, fx => fx.Reset());

        Add("saving_ideal_railgun", f.Ideal, fx =>
        {
            fx.Reset();
            fx.SetIdealBackstory(true);
            fx.AddTrait(fx.Ideal, UpstreamLogic.TraitDawnAegis);
            fx.SetBurstActive(true);
            fx.GiveWeapon(fx.Ideal, UpstreamLogic.ItemStarlightRailgun);
            Scribe.mode = LoadSaveMode.Saving;
        });

        Add("world_self", f.Ideal, fx =>
        {
            fx.Reset();
            fx.SetIdealBackstory(true);
            fx.AddWorldEffect(fx.Ideal, fx.Map, new IntVec3(10, 0, 10));
        });

        Add("world_enemy", f.Enemy, fx =>
        {
            fx.Reset();
            fx.SetIdealBackstory(true);
            fx.AddWorldEffect(fx.Ideal, fx.Map, new IntVec3(10, 0, 10));
        });

        Add("world_far", f.Ideal, fx =>
        {
            fx.Reset();
            fx.SetIdealBackstory(true);
            fx.SetPosition(fx.Ideal, new IntVec3(35, 0, 35));
            fx.AddWorldEffect(fx.Ideal, fx.Map, new IntVec3(10, 0, 10));
        });

        Add("nan_move_speed", f.Ideal, fx =>
        {
            fx.Reset();
            fx.SetIdealBackstory(true);
            fx.AddStatBase(fx.Ideal, fx.Stats[StatDispatch.MoveSpeed], float.NaN);
        });

        Add("inf_construction", f.Ideal, fx =>
        {
            fx.Reset();
            fx.SetIdealBackstory(true);
            fx.AddTrait(fx.Ideal, UpstreamLogic.TraitStarlightTinkerer);
            fx.SetRepairJob(fx.Ideal);
            fx.AddStatBase(fx.Ideal, fx.Stats[StatDispatch.ConstructionSpeed], float.PositiveInfinity);
        });

        return list;
    }
}
