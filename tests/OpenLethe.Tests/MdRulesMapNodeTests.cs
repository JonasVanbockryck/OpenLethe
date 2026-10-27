using OpenLethe.Server.MirrorDungeon.Model;
using OpenLethe.Server.MirrorDungeon.Rules;
using OpenLethe.Server.Wire;

namespace OpenLethe.Tests;

// Component tests for the Task 6 domain primitive: MapNodeRules.EnterNode, ported from
// Handlers/MirrorDungeonMap.cs EnterMirrorDungeonMapNode (:244-306). Structure/behavior only -
// the shop slot COMPOSITION is RNG (masked in the replay); only its count is byte-guaranteed.
// LevelOffsetEngine is deliberately NOT extracted here: EnterMapNode never touches
// run.LevelOffsets (sbmlos/egmlos/snft/csnft belong to SelectThemeFloor/ExitMapNode) - see the
// Task 6 brief's YAGNI clause.
public class MdRulesMapNodeTests
{
    private static Run RunWithNode(long nid, long e, long eid = 0)
    {
        var run = new Run();
        run.Floor.Nodes.Add(new MapNode { Nid = nid, E = e, Eid = eid });
        return run;
    }

    [Theory]
    [InlineData(3)]  // event node
    [InlineData(10)] // shop node
    public void EnterNode_EventOrShopNode_NrIsThree(long e)
    {
        var run = RunWithNode(nid: 100, e: e);

        var (changedHiddenNode, nr) = MapNodeRules.EnterNode(run, new Currentnode { f = 0, s = 0, nid = 100 });

        Assert.Equal(3, nr);
        Assert.Equal(3, run.Nr);
        Assert.False(changedHiddenNode);
    }

    [Theory]
    [InlineData(1)]  // normal battle
    [InlineData(2)]  // abno battle
    [InlineData(5)]  // hard battle
    [InlineData(6)]  // boss
    [InlineData(14)] // hard abno
    public void EnterNode_BattleTypeNode_NrIsFour(long e)
    {
        var run = RunWithNode(nid: 100, e: e);

        var (_, nr) = MapNodeRules.EnterNode(run, new Currentnode { f = 0, s = 0, nid = 100 });

        Assert.Equal(4, nr);
        Assert.Equal(4, run.Nr);
    }

    [Fact]
    public void EnterNode_UpdatesFloorCurrentPositionToEnteredNode()
    {
        var run = RunWithNode(nid: 200, e: 1);

        MapNodeRules.EnterNode(run, new Currentnode { f = 2, s = 1, nid = 200 });

        Assert.Equal(2, run.Floor.Current.F);
        Assert.Equal(1, run.Floor.Current.S);
        Assert.Equal(200, run.Floor.Current.Nid);
    }

    [Fact]
    public void EnterNode_SetsEidSideEffectFromEnteredNode()
    {
        var run = RunWithNode(nid: 300, e: 1, eid: 4242);

        MapNodeRules.EnterNode(run, new Currentnode { f = 0, s = 0, nid = 300 });

        Assert.Equal(4242, run.Eid);
    }

    [Fact]
    public void EnterNode_AlwaysReturnsChangedHiddenNodeFalse()
    {
        var run = RunWithNode(nid: 100, e: 1);

        var (changedHiddenNode, _) = MapNodeRules.EnterNode(run, new Currentnode { f = 0, s = 0, nid = 100 });

        Assert.False(changedHiddenNode);
    }

    [Fact]
    public void EnterNode_NonShopNode_ShopUntouched()
    {
        var run = RunWithNode(nid: 100, e: 1);
        run.Shop.Rc = 7; // pre-existing shop state must survive a non-shop entry untouched

        MapNodeRules.EnterNode(run, new Currentnode { f = 0, s = 0, nid = 100 });

        Assert.Equal(7, run.Shop.Rc);
        Assert.Empty(run.Shop.Slots);
    }

    [Fact]
    public void EnterNode_NormalShopNode_UsesRetailSlotTable()
    {
        // eid==0 -> normal shop: shopInfoByType[0] = 1 named "up" slot + 4 gift slots.
        var run = RunWithNode(nid: 100, e: 10, eid: 0);
        run.Party.Add(new PartyUnit { PersonalityId = 11 });
        run.Party.Add(new PartyUnit { PersonalityId = 12 });

        MapNodeRules.EnterNode(run, new Currentnode { f = 0, s = 0, nid = 100 });

        Assert.Equal(new[] { "up", "eg", "eg", "eg", "eg" }, run.Shop.Slots.Select(s => s.T));
        Assert.Equal(11, run.Shop.Slots[0].Id);
        Assert.All(run.Shop.Slots, s => Assert.Equal(1, s.S));
    }

    [Fact]
    public void EnterNode_ShopNode_NeverOffersOwnedGifts()
    {
        // Own the whole shop pool except three gifts: those three are all that can show up.
        var pool = new OpenLethe.Server.MirrorDungeon.Data.MdThemePool().ShopEgoGiftsPool(1001);
        var run = RunWithNode(nid: 100, e: 10, eid: 0);
        run.Gifts.Items.AddRange(pool.Skip(3).Select(id => new EgoGift { Id = id }));

        MapNodeRules.EnterNode(run, new Currentnode { f = 0, s = 0, nid = 100 });

        var offered = run.Shop.Slots.Where(s => s.T == "eg").Select(s => s.Id).ToList();
        Assert.NotEmpty(offered);
        Assert.All(offered, id => Assert.Contains(id, pool.Take(3)));
    }

    [Fact]
    public void EnterNode_SuperShopNode_UsesRetailSlotTable()
    {
        // eid!=0 -> super shop: shopInfoByType[1] = 1 "upt" + 2 named "up" + 7 gift slots,
        // targeting slot first (md-extreme-run seq67/96).
        var run = RunWithNode(nid: 100, e: 10, eid: 1);
        run.Party.Add(new PartyUnit { PersonalityId = 11 });
        run.Party.Add(new PartyUnit { PersonalityId = 12 });
        run.Party.Add(new PartyUnit { PersonalityId = 13 });

        MapNodeRules.EnterNode(run, new Currentnode { f = 0, s = 0, nid = 100 });

        Assert.Equal(new[] { "upt", "up", "up" }, run.Shop.Slots.Take(3).Select(s => s.T));
        Assert.Equal(new long[] { 11, 12 }, run.Shop.Slots.Skip(1).Take(2).Select(s => s.Id));
        Assert.Equal(7, run.Shop.Slots.Count(s => s.T == "eg"));
    }

    [Fact]
    public void EnterNode_ShopNode_FreshShopResetsPriorScalars()
    {
        var run = RunWithNode(nid: 100, e: 10, eid: 0);
        run.Shop.Rc = 3;
        run.Shop.Fre = 2;

        MapNodeRules.EnterNode(run, new Currentnode { f = 0, s = 0, nid = 100 });

        Assert.Equal(0, run.Shop.Rc);
        Assert.Equal(0, run.Shop.Fre);
    }
}
