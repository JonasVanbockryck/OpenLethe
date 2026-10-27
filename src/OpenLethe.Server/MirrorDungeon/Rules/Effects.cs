using OpenLethe.Server.MirrorDungeon.Model;

namespace OpenLethe.Server.MirrorDungeon.Rules;

// Relocation home for the hardcoded effect constants the old wire handlers hid inline.
// Every table/value here is copied VERBATIM from its wire twin - no behavior change, only a
// Run/scalar signature. See the handler-migration Task 2 brief for the source file:line map.
public static class Effects
{
    // Verbatim port of Handlers/MirrorDungeonMap.cs HiddenGiftLevelBump (:107-113).
    public static long HiddenGiftLevelBump(long giftId) => giftId switch
    {
        993003 => 2,
        993005 => 3,
        _ => 0,
    };

    // Verbatim port of Handlers/MirrorDungeonShop.cs ApplyShopDiscount (:29-31), retargeted to Run.
    public static long ApplyShopDiscount(Run run, long discountGiftId, long price) =>
        run.Gifts.Items.Any(g => g.Id == discountGiftId) ? price * 70 / 100 : price;

    // The "shop prices up" Adversities (hidden-ego-gift-mirrordungeon tag RAISED_MARKER_PRICE):
    // 995014 "every shop function except selling costs 2x", 995024 the same at 3x. A picked
    // Adversity sits in egs like any hidden gift, and the client prices the shop off it, so the
    // server must charge the same. Holding both applies the stronger one, not the product.
    private static readonly (long giftId, long multiplier)[] RaisedMarketPriceGifts = { (995024, 3), (995014, 2) };

    public static long ApplyRaisedMarketPrice(Run run, long price)
    {
        foreach (var (giftId, multiplier) in RaisedMarketPriceGifts)
            if (run.Gifts.Items.Any(g => g.Id == giftId)) return price * multiplier;
        return price;
    }

    // Verbatim port of Handlers/MirrorDungeon.cs StartingCost (:189-190).
    public static long StartingCost(long dungeonId) => dungeonId >= 7 ? 200 : 500; // 7 and the current season 8 share the MD7 rules

    // Verbatim port of Handlers/MirrorDungeonMap.cs IsHiddenConfirmedGift (:156). Confirmed-gift
    // ids split into "normal" (9xxx, deferred to ExitMapNode's e==3 realization) and "hidden"
    // (993xxx level-cap gifts, land immediately at UpdateMapNode).
    public static bool IsHiddenConfirmedGift(long id) => id >= 990000;
}
