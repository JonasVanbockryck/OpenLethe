using System.Text.Json.Nodes;

namespace OpenLethe.Server.Handlers;

/// Chapter 10's RPG mode (capture: docs/flows(4) (1)). Four routes, no game logic - the
/// client owns the save and streams row-log deltas into Account.Chapter10RpgSaveInfo.
/// See OpenLethe.Server.Chapter10Rpg for the store and the table spec.
///
/// There are no ResPacket_Chapter10RPG* types in packets/ - this mode postdates the
/// bundled client header dump - so these use PacketRouting.PacketId directly, the same as
/// EnterMirrordungeonMapNodeBattleAfterChoice. The client never reads the value.
public static class Chapter10RpgEndpoints
{
    public static IEndpointRouteBuilder MapChapter10Rpg(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/Chapter10RPGGetGameStatus", async (HttpContext ctx) =>
        {
            var account = await HandlerContext.ResolveAsync(ctx, SaveColumn.Chapter10Rpg);
            if (account is null) return Results.Unauthorized();

            var status = Chapter10Rpg.Status(
                Chapter10Rpg.Load(account.Chapter10RpgSaveInfo));
            return Json(status);
        });

        app.MapPost("/api/Chapter10RPGGetSaveData", async (HttpContext ctx) =>
        {
            var account = await HandlerContext.ResolveAsync(ctx, SaveColumn.Chapter10Rpg);
            if (account is null) return Results.Unauthorized();

            // `nonce` is the client's request stamp; the real server echoes nothing of it.
            var body = Chapter10Rpg.Read(
                Chapter10Rpg.Load(account.Chapter10RpgSaveInfo));
            return Json(body);
        });

        app.MapPost("/api/Chapter10RPGUpdateSaveData", async (HttpContext ctx) =>
        {
            var account = await HandlerContext.ResolveAsync(ctx, SaveColumn.Chapter10Rpg);
            if (account is null) return Results.Unauthorized();
            var p = await HandlerContext.ReadParamsAsync<JsonObject>(ctx);
            if (p is null) return Results.BadRequest();

            var doc = Chapter10Rpg.Load(account.Chapter10RpgSaveInfo);
            var applied = Chapter10Rpg.Apply(doc, p);
            account.Chapter10RpgSaveInfo = AccountFields.Set(doc);
            await HandlerContext.SaveAsync(ctx);

            return Json(new JsonObject
            {
                ["appliedCount"] = applied,
                ["lastSeq"] = Chapter10Rpg.LastSeq(doc),
            });
        });

        app.MapPost("/api/Chapter10RPGResetSaveData", async (HttpContext ctx) =>
        {
            var account = await HandlerContext.ResolveAsync(ctx, SaveColumn.Chapter10Rpg);
            if (account is null) return Results.Unauthorized();
            var p = await HandlerContext.ReadParamsAsync<JsonObject>(ctx);
            if (p is null) return Results.BadRequest();

            var doc = Chapter10Rpg.Load(account.Chapter10RpgSaveInfo);
            var deleted = Chapter10Rpg.Reset(doc, (bool?)p["isHardReset"] == true);
            account.Chapter10RpgSaveInfo = AccountFields.Set(doc);
            await HandlerContext.SaveAsync(ctx);

            return Json(new JsonObject
            {
                ["deletedCount"] = deleted,
                ["lastSeq"] = Chapter10Rpg.LastSeq(doc),
            });
        });

        return app;
    }

    private static IResult Json<T>(T result) => Results.Json(
        global::ResponsePacket<T>.Ok(result, global::PacketRouting.PacketId), global::PacketJson.Options);
}
