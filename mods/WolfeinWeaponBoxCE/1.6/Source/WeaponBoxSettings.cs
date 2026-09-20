using System;
using Verse;

namespace Meidocho.WolfeinWeaponBoxCE;

public sealed class WeaponBoxSettings : ModSettings
{
    public const int DefaultBulk = 350;
    public const int MaxBonus = 1000;

    public int BulkBonus = DefaultBulk;
    public int WeightBonus;

    public override void ExposeData()
    {
        Scribe_Values.Look(ref BulkBonus, "bulkBonus", DefaultBulk);
        Scribe_Values.Look(ref WeightBonus, "weightBonus", 0);
        BulkBonus = Math.Max(0, Math.Min(MaxBonus, BulkBonus));
        WeightBonus = Math.Max(0, Math.Min(MaxBonus, WeightBonus));
    }
}
