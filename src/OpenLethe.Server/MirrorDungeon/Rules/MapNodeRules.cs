using OpenLethe.Server.MirrorDungeon.Data;
using OpenLethe.Server.MirrorDungeon.Model;
using OpenLethe.Server.Wire;

namespace OpenLethe.Server.MirrorDungeon.Rules;

// Node-entry lifecycle. Verbatim port of the EnterMirrorDungeonMapNode handler body
// (Handlers/MirrorDungeonMap.cs :244-306). ExitMapNode (including its reward/rre engine and the
// LevelOffsetEngine methods it drives - sbmlos/egmlos/snft/csnft) is Task 7, deliberately NOT
// touched here: EnterMapNode never reads or writes run.LevelOffsets, so no LevelOffsetEngine
// method is warranted in this task (YAGNI - see the Task 6 brief).
public static class MapNodeRules
{
    // Port of the EnterMirrorDungeonMapNode handler body. Caller (the handler) is responsible for
    // validating the entered node exists in run.Floor.Nodes before calling this - request
    // validation, not game logic.
    public static (bool changedHiddenNode, long nr) EnterNode(Run run, Currentnode enteredNode)
    {
        var node = run.Floor.Nodes.First(n => n.Nid == enteredNode.nid);

        run.Floor.Current = new CurrentPosition { F = enteredNode.f, S = enteredNode.s, Nid = enteredNode.nid };
        // Capture-confirmed (md-extreme seq316/317 -> seq321): entering a node updates eid the
        // same way ExitMirrorDungeonMapNode does on exit, just to the ENTERED node's own value.
        run.Eid = node.Eid;
        // Event/shop nodes (e==3/10) are worth 3 "node results"; every battle-type node
        // (e in {1,2,5,6,14}) is worth 4. Capture-verified 77/77.
        // The hidden node (e==15) is the exception: entering one records 5 - md-extreme-run-2 seq335/437
        // are the only rows whose changedHiddenNode flips true, and both carry "nr": 5.
        var nr = node.E == MdMapGen.HiddenBattleNodeType ? 5
               : (node.E == 3 || node.E == 10) ? 3
               : 4;
        run.Nr = nr;

        // Shop is only (re)rolled on entering a shop node (e==10) - other node types leave the
        // tracked shop untouched. Fresh ShopState, not a slots-only mutation: capture-verified
        // (77/77) every scalar (rc/fre/fkre/cf/aec/aesp) is 0 on entry.
        if (node.E == 10)
        {
            // Slot counts come from the retail table (shopInfoByType): 4 gift slots in a
            // regular shop and 7 in the super shop, preceded by its skill-replacement slots - one
            // named-sinner "up" slot per skillSlotNum (id = the personality id the up-branch of
            // PurchaseUpgradePersonality matches on, i.e. "replace a skill of this sinner") and one
            // "upt" slot per skillTargetingSlotNum, which is the one that lets the buyer pick the
            // skill and can be paid with the run's starlight instead of funds (super shop only).
            // PurchaseEgoGift's idx skips those leading non-eg slots, per the reference captures.
            var super = SharedRules.IsSuperShop(run) ?? false;
            var type = MdShopTable.ShopType(super);

            var slots = new List<ShopSlotState>();
            // Captured super shops list the targeting slot first (upt:0, then up:<pid> x2, then the
            // eg slots) - see md-extreme-run seq67/96.
            for (var i = 0; i < (type?.skillTargetingSlotNum ?? 0); i++)
                slots.Add(new ShopSlotState { T = "upt", Id = 0, S = 1 });
            var namedUpgradeSlots = (int)(type?.skillSlotNum ?? 0);
            for (var i = 0; i < namedUpgradeSlots && i < run.Party.Count; i++)
                slots.Add(new ShopSlotState { T = "up", Id = run.Party[i].PersonalityId, S = 1 });

            slots.AddRange(new MdThemePool().SelectRandomShopEgos(
                    SharedRules.ThemePackId(run), (int)SharedRules.ShopGiftCount(run), SharedRules.CurrentFloor(run), null,
                    SharedRules.OwnedGiftIds(run))
                .Select(id => new ShopSlotState { T = "eg", Id = id, S = 1 }));

            run.Shop = new ShopState { Slots = slots };
        }

        // The retail capture flips changedHiddenNode to true on exactly the two rows that ENTER a
        // hidden node (md-extreme-run-2 seq335/437: both "e": 15, both "nr": 5); every other entry is
        // false. The client reads this flag to present the node as the hidden one, so hardcoding
        // false here made an e=15 node look like an ordinary fight.
        return (node.E == MdMapGen.HiddenBattleNodeType, nr);
    }
}
