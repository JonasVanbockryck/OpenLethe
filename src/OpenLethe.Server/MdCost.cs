using System;
using System.Collections.Generic;
using System.Linq;
using OpenLethe.Resources;
using OpenLethe.Server.MirrorDungeon.Model;

namespace OpenLethe.Server;

// Port of lethe-server/server/src/api/md/md_cost.rs CostManager::get_default_cost.
// load_from_dir is dead code in Rust (never called) - not ported.
//
// The payout the client shows on the map comes from mirrordungeon-acquire-cost-in-battle
// ({dungeonId, typeIndex, stageList: [{id, cost}]}), while the server only paid a fixed amount per
// node type (0 for e=6 past floor 3). Look the stage up first; fall back to the fixed amount.
public static class MdCost
{
    private static readonly long[] BossFloorCosts = { 200, 240, 300, 400 };

    // stage id -> cost (one table per typeIndex 0..3; the first occurrence of an id wins).
    private static readonly Lazy<Dictionary<long, long>> StageCosts = new(LoadStageCosts);

    private static Dictionary<long, long> LoadStageCosts()
    {
        var map = new Dictionary<long, long>();
        try
        {
            foreach (var g in StaticData.GetList<MdCostGroup>("static-data/mirrordungeon-acquire-cost-in-battle"))
            {
                if (g?.stageList is null) continue;
                foreach (var s in g.stageList)
                {
                    if (s is null) continue;
                    if (!map.ContainsKey(s.id)) map[s.id] = s.cost;
                }
            }
        }
        catch { }
        return map;
    }

    /// The stage's cost from the client table, or -1 when unlisted (caller falls back to GetDefaultCost).
    public static long CostForStage(long stageId)
        => StageCosts.Value.TryGetValue(stageId, out var c) ? c : -1;

    public static long GetDefaultCost(long nodeE, long floor) => nodeE switch
    {
        1 => 60,
        2 => 120,
        5 => 100,
        6 => floor is >= 0 and <= 3 ? BossFloorCosts[floor] : 0,
        14 => 150,
        _ => 0,
    };

    // Two retail gifts scale battle payouts (text from limbus_locale EN/egoGiftData.json):
    //   9079 Golden Urn   「Gain +20% Cost after winning a combat Encounter.」
    //   9242 Bongy Plush  「Multiply Cost owned by 1.5 upon clearing a Floor」
    private const long GoldenUrnGift = 9079;
    private const long BongyPlushGift = 9242;

    public static long ApplyCostGifts(Run run, long nodeE, long cost)
    {
        if (cost <= 0) return cost;
        if (run.Gifts.Items.Any(g => g.Id == GoldenUrnGift)) cost = cost * 120 / 100;
        if (nodeE == 6 && run.Gifts.Items.Any(g => g.Id == BongyPlushGift)) cost = cost * 3 / 2;
        return cost;
    }
}

// One row of mirrordungeon-acquire-cost-in-battle (public fields, matching the client table).
public sealed class MdCostGroup
{
    public long dungeonId;
    public long typeIndex;
    public List<MdCostStage> stageList = new();
}

public sealed class MdCostStage
{
    public long id;
    public long cost;
}
