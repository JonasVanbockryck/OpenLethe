namespace OpenLethe.Server.Handlers;

/// Port of dungeon/get_dungeon_save_info_all.rs. Aggregates the dungeon save-info
/// columns; empty column -> sentinel (Rust's unwrap_or_else fallback). The client
/// ResPacket drifts from the Rust type: no railwaySaveInfo, plus mirrorDungeonHistories.
public static class DungeonSaveInfoEndpoints
{
    public static IEndpointRouteBuilder MapGetDungeonSaveInfoAll(this IEndpointRouteBuilder app)
    {
        var packetId = global::PacketRouting.ResolvePacketId<global::ResPacket_GetDungeonSaveInfoAll>();

        // The 2026-10-15 client fetches the rental formation history when the MD entrance opens.
        // Unregistered, that was a 404 (retried three times) and the Enter flow stalled. Answer a
        // sentinel-filled empty packet: empty collections, nested objects one level deep, ints -1,
        // so nothing the client dereferences is null.
        var rentalHistoryId = global::PacketRouting.ResolvePacketId<global::ResPacket_GetMirrorDungeonRentalFormationHistory>();
        app.MapPost("/api/GetMirrorDungeonRentalFormationHistory", async (HttpContext ctx) =>
        {
            if (!await HandlerContext.ExistsAsync(ctx)) return Results.Unauthorized();
            var payload = new global::ResPacket_GetMirrorDungeonRentalFormationHistory();
            FillSentinels(payload, 0);
            return Results.Json(global::ResponsePacket<global::ResPacket_GetMirrorDungeonRentalFormationHistory>.Ok(payload, rentalHistoryId), global::PacketJson.Options);
        });
        app.MapPost("/api/GetDungeonSaveInfoAll", async (HttpContext ctx) =>
        {
            var account = await HandlerContext.ResolveAsync(ctx, SaveColumn.Md, SaveColumn.StoryMd, SaveColumn.Story);
            if (account is null) return Results.Unauthorized();

            // ponytail: Rust used `!x.dungeonMap.ns.is_empty()` to detect a populated save;
            // AccountFields.Get already treats "{}"/"[]"/null as absent, which is the same
            // "empty column -> sentinel" test without transliterating a Rust field the
            // drifted client type may not carry. A future subsystem stores a full save or
            // clears the column, so the null check is sufficient.
            // ponytail: sentinels set only the fields Rust set explicitly; Rust's
            // ..Default::default() yields empty collections where C# leaves nested reference
            // fields null - tolerated by the client for the -1 "no dungeon" marker.
            var md = OpenLethe.Server.AccountFields.Get<global::MirrorDungeonSaveInfoFormat>(account.MdSaveInfo)
                ?? new global::MirrorDungeonSaveInfoFormat
                {
                    // -1/-1 is the "no run" marker, as in both captures. The port briefly sent the
                    // current season (8/0) so the entrance plate showed a number instead of 00, but
                    // the client then treats the account as mid-run: after a forfeit it shows
                    // "unclaimed rewards" for a run the server has already ended, and neither
                    // claiming nor re-entering gets out of it.
                    dungeonId = -1,
                    idx = -1,
                    currentInfo = new global::MirrorDungeonCurrentInfoFormat { eid = -1 },
                };

            var storyMd = OpenLethe.Server.AccountFields.Get<global::StoryMirrorDungeonSaveInfoFormat>(account.StoryMdSaveInfo)
                ?? new global::StoryMirrorDungeonSaveInfoFormat
                {
                    dungeonid = -1,
                    currentinfo = new global::StoryMirrorDungeonCurrentInfoFormat { eid = -1 },
                };

            var story = OpenLethe.Server.AccountFields.Get<global::StoryDungeonSaveInfoFormat>(account.StorySaveInfo)
                ?? SentinelStorySave();

            var result = new global::ResPacket_GetDungeonSaveInfoAll
            {
                mirrorOriginSaveInfo = md,
                storyMirrorSaveInfo = storyMd,
                storySaveInfo = story,
                mirrorDungeonClearInfos = new List<global::MirrorDungeonClearInfoFormat>
                {
                    new() { dungeonid = 4, idx = 0, clearnumber = 9999, defeatnumber = 9999 },
                    new() { dungeonid = 4, idx = 1, clearnumber = 9999, defeatnumber = 9999 },
                },
                mirrorDungeonHistories = new List<global::MirrorDungeonHistoryFormat>(),
            };
            return Results.Json(global::ResponsePacket<global::ResPacket_GetDungeonSaveInfoAll>.Ok(result, packetId), global::PacketJson.Options);
        });
        return app;
    }

    // The 2026-10-15 client walks all three saves in the entrance UI; a bare new() storySaveInfo
    // leaves every nested reference null and MirrorDungeonEntranceUIObject.UpdateUI NREs on it.
    // Fill by field type (int -1 / "" / empty list / nested object one level down) so this keeps
    // working when the generated packet types change shape.
    private static global::StoryDungeonSaveInfoFormat SentinelStorySave()
    {
        var o = new global::StoryDungeonSaveInfoFormat();
        FillSentinels(o, 0);
        return o;
    }

    private static void FillSentinels(object target, int depth)
    {
        if (target is null || depth > 2) return;
        var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance;
        foreach (var f in target.GetType().GetFields(flags))
        {
            var t = f.FieldType;
            try
            {
                if (t == typeof(string)) { if (f.GetValue(target) is null) f.SetValue(target, ""); }
                else if (t == typeof(int)) f.SetValue(target, -1);
                else if (t == typeof(long)) f.SetValue(target, -1L);
                else if (t == typeof(short)) f.SetValue(target, (short)-1);
                else if (f.GetValue(target) is null)
                {
                    if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>))
                        f.SetValue(target, System.Activator.CreateInstance(t));
                    else if (t.IsClass && t.GetConstructor(System.Type.EmptyTypes) is not null)
                    {
                        var child = System.Activator.CreateInstance(t)!;
                        FillSentinels(child, depth + 1);
                        f.SetValue(target, child);
                    }
                }
            }
            catch { }
        }
    }
}
