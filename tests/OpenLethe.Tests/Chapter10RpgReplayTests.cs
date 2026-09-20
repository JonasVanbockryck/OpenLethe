using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using OpenLethe.Data;
using OpenLethe.Server.Auth;
using OpenLethe.Tests.Replay;

/// Replays the Chapter 10 RPG-mode capture (docs/flows(4) (1)) against the live server:
/// every captured request is re-sent in order and our response is diffed against the
/// captured one through the per-endpoint mask.
///
/// Unlike the MD and Railway replays there is no ground-truth injection, because this
/// subsystem has no server-side RNG, clock or static data - the client owns the save and
/// the server only files the rows. So a fresh account's state evolves from the captured
/// requests alone, which makes the 144 replayed records one continuous chain: every
/// getter is answered out of state our own handlers built from every earlier update.
[Collection("postgres")]
public class Chapter10RpgReplayTests(PostgresFixture db)
{
    /// The capture opens on an account carrying a previous session (lastSeq 1236, two
    /// ending rows) that it never shows being written, so records 3-6 - two status reads,
    /// a soft reset and the hard reset's deletedCount - answer out of state from before it
    /// opened. That hard reset wipes the account, and seq 7 is the status read proving it:
    /// empty everything, which is exactly a fresh account. Replay starts there.
    private const int FirstReplayable = 7;

    /// Masked in the diff for row order alone (see ReplayMasks), so their contents are
    /// checked here instead - as multisets, which catches a missing, extra or changed row.
    private static readonly string[] OrderMasked = { "dialogues", "events" };

    [SkippableFact]
    public async Task Replays_Chapter10RpgRun_Matches()
    {
        db.RequireDb();
        await using var factory = new DbWebAppFactory(db.ConnectionString);

        var name = $"ch10replay_{Guid.NewGuid():N}";
        string jwt;
        using (var scope = factory.Services.CreateScope())
        {
            var store = new AccountStore(scope.ServiceProvider.GetRequiredService<AppDbContext>());
            await store.GetOrCreateByUsernameAsync(name);
            jwt = scope.ServiceProvider.GetRequiredService<JwtService>().Mint(name);
        }
        var client = factory.CreateClient();
        var failures = new List<string>();
        var replayed = 0;

        foreach (var (runId, file) in FixtureLoader.Chapter10Runs)
        {
            foreach (var rec in FixtureLoader.Records(file))
            {
                if (rec.Seq < FirstReplayable || rec.Req is null) continue;
                replayed++;

                var req = rec.Req.DeepClone();
                if (req["userAuth"] is JsonObject ua) ua["authCode"] = jwt;
                var resp = await client.PostAsync(rec.Path, JsonContent.Create(req));
                var ours = JsonNode.Parse(await resp.Content.ReadAsStringAsync());

                var diffs = JsonDiff.Compare(ours, rec.Res, ReplayMasks.For(runId, rec.Path, rec.Seq));
                if (diffs.Count > 0)
                    failures.Add($"[{runId}] seq {rec.Seq} {rec.Path}: {string.Join(", ", diffs.Take(8))}");

                foreach (var table in OrderMasked)
                {
                    if (rec.Res?["result"]?[table] is not JsonArray theirs) continue;
                    var mine = (ours?["result"]?[table] as JsonArray ?? new JsonArray()).ToList();
                    var missing = theirs.Count(row => !RemoveMatch(mine, row));
                    if (missing > 0 || mine.Count > 0)
                        failures.Add($"[{runId}] seq {rec.Seq} {rec.Path}: {table} contents differ " +
                                     $"({missing} captured rows missing, {mine.Count} extra)");
                }
            }
        }

        Assert.Equal(144, replayed);
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    /// Takes one equal row out of `rows`, or reports there was none. DeepEquals rather
    /// than a text compare: our rows have been through jsonb, which reorders an object's
    /// keys, so the row that comes back is equal to the captured one without being the
    /// same string.
    private static bool RemoveMatch(List<JsonNode?> rows, JsonNode? row)
    {
        var at = rows.FindIndex(r => JsonNode.DeepEquals(r, row));
        if (at < 0) return false;
        rows.RemoveAt(at);
        return true;
    }
}
