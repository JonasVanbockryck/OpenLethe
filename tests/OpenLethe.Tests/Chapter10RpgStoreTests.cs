using System.Text.Json.Nodes;
using OpenLethe.Server;

/// Chapter10RpgReplayTests covers the store against the real capture, but it starts at
/// the record AFTER the capture's hard reset - the only two resets in the capture answer
/// out of a save from before it opened, so they cannot be replayed. These cover the reset
/// branch (and `truncates`, which the capture never exercises at all) directly.
public class Chapter10RpgStoreTests
{
    private static JsonObject Seeded()
    {
        var doc = Chapter10Rpg.Load(null);
        Chapter10Rpg.Apply(doc, new JsonObject
        {
            ["save"] = new JsonArray(Row(1, new JsonObject { ["floorKey"] = "F1001A", ["enteredButton"] = "A" })),
            ["quests"] = new JsonArray(
                Row(2, new JsonObject { ["questId"] = 20, ["state"] = 1 }),
                Row(3, new JsonObject { ["questId"] = 10, ["state"] = 2 })),
            ["endings"] = new JsonArray(Row(4, new JsonObject { ["endingKey"] = "A", ["state"] = 3 })),
        });
        return doc;
    }

    private static JsonObject Row(long seq, JsonObject fields)
    {
        var row = new JsonObject { ["seq"] = seq, ["del"] = false };
        foreach (var (k, v) in fields) row[k] = v!.DeepClone();
        return row;
    }

    [Fact]
    public void Apply_CountsEveryRow_AndAdvancesLastSeq()
    {
        var doc = Chapter10Rpg.Load(null);
        var applied = Chapter10Rpg.Apply(doc, new JsonObject
        {
            ["quests"] = new JsonArray(
                Row(7, new JsonObject { ["questId"] = 20, ["state"] = 1 }),
                Row(9, new JsonObject { ["questId"] = 20, ["state"] = 2 })),
        });

        Assert.Equal(2, applied);
        Assert.Equal(9, Chapter10Rpg.LastSeq(doc));

        // Same key twice in one batch is an upsert, not two rows - and `seq`/`del` never
        // survive into the stored row.
        var quest = Assert.Single((JsonArray)Chapter10Rpg.Read(doc)["quests"]!);
        Assert.Equal("{\"questId\":20,\"state\":2}", quest!.ToJsonString());
    }

    [Fact]
    public void SoftReset_ClearsTheRun_KeepsEndingsAndSeq()
    {
        var doc = Seeded();

        var deleted = Chapter10Rpg.Reset(doc, hard: false);

        Assert.Equal(3, deleted);   // the save row + both quests; the ending stays
        Assert.Equal(4, Chapter10Rpg.LastSeq(doc));
        var read = Chapter10Rpg.Read(doc);
        Assert.Empty((JsonArray)read["save"]!);
        Assert.Empty((JsonArray)read["quests"]!);
        Assert.Single((JsonArray)read["endings"]!);
        Assert.False(Chapter10Rpg.Status(doc).isInProgress);
    }

    [Fact]
    public void HardReset_WipesEverything_AndRewindsSeq()
    {
        var doc = Seeded();

        Assert.Equal(4, Chapter10Rpg.Reset(doc, hard: true));
        Assert.Equal(0, Chapter10Rpg.LastSeq(doc));
        Assert.Empty((JsonArray)Chapter10Rpg.Read(doc)["endings"]!);
        Assert.Empty(Chapter10Rpg.Status(doc).endings);
    }

    [Fact]
    public void Truncates_ClearsNamedTablesOnly()
    {
        var doc = Seeded();

        Chapter10Rpg.Apply(doc, new JsonObject { ["truncates"] = new JsonArray("quests") });

        var read = Chapter10Rpg.Read(doc);
        Assert.Empty((JsonArray)read["quests"]!);
        Assert.Single((JsonArray)read["save"]!);
    }
}
