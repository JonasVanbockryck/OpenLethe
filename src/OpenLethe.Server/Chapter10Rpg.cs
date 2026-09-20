using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenLethe.Server;

/// Chapter 10's RPG mode save store (capture: docs/flows(4) (1)).
///
/// There is no game logic here, and there is none upstream either: the client owns the
/// whole save and ships it as a row log. Chapter10RPGUpdateSaveData streams rows tagged
/// with a `seq` from one global, gapless counter and a `del` flag; the server upserts or
/// deletes each by its table's key and answers with how many rows it applied plus the
/// highest seq it has seen. Chapter10RPGGetSaveData hands the rows back with `seq`/`del`
/// stripped. Nothing below reads a row's payload, so the rows stay JsonObject rather than
/// becoming eighteen wire types that would only be echoed.
///
/// The column holds { "lastSeq": n, "tables": { "<table>": [ row, ... ] } }.
public static class Chapter10Rpg
{
    /// Shared with Status, which re-shapes this one table rather than echoing it. Declared
    /// BEFORE Spec: static field initializers run in declaration order, so Spec would read
    /// a null here and key the endings table on whole rows instead.
    private static readonly string[] EndingKey = { "endingKey" };

    /// Every table the client streams, in the order Chapter10RPGGetSaveData answers with.
    ///
    /// Sort: the capture proves two different read orders, one per table, and they hold
    /// across all 22 captured getters - `quests`/`cutscenes`/etc. come back ordered by key
    /// while `items`/`npcKills`/`dialogues`/`events` come back in insertion order. We match
    /// each table's own rule (16 of 18 byte-verify; see Chapter10RpgReplayTests for the two
    /// that cannot).
    ///
    /// Echo: `battleInfos` rows carry seqs in the same stream, so they are stored and
    /// counted like any other table, but the real server never returns them.
    private static readonly (string Table, string[]? Key, bool Sort, bool Echo)[] Spec =
    {
        // Key = [] is a singleton (one row, replaced wholesale); Key = null means the row
        // IS its own key - used for the two tables the capture never carries a row for, so
        // that an unknown real key can dedupe but never drop a distinct row.
        ("save",           [],                                               false, true),
        ("currency",       [],                                               false, true),
        ("items",          new[] { "instanceId" },                           false, true),
        ("quests",         new[] { "questId" },                              true,  true),
        ("qgoals",         new[] { "questId", "stepIndex", "goalIndex" },    true,  true),
        ("cutscenes",      new[] { "cutsceneId" },                           true,  true),
        ("dialogues",      new[] { "dialogueId" },                           false, true),
        ("stationaryObjs", new[] { "objectId" },                             true,  true),
        ("stationaryEvts", new[] { "eventId" },                              true,  true),
        ("npcKills",       new[] { "spawnId" },                              false, true),
        ("shops",          new[] { "shopId", "itemId" },                     true,  true),
        ("events",         new[] { "eventId" },                              false, true),
        ("deadScenes",     new[] { "deadSceneId" },                          true,  true),
        ("fieldDrops",     null,                                             false, true),
        ("visitedFloors",  new[] { "floorKey" },                             true,  true),
        ("endings",        EndingKey,                                        true,  true),
        ("player",         new[] { "playerId" },                             true,  true),
        ("egoStocks",      new[] { "attributeType" },                        true,  true),
        ("battleInfos",    null,                                             false, false),
    };

    /// GetGameStatus's ending record. It carries one field the client never sends -
    /// `carryShop`, between carryItems and carryTotalSpent - so this is the one place a
    /// stored row is re-shaped rather than echoed (capture seq 3 and seq 187).
    public sealed class Ending
    {
        public string endingKey = "";
        public long state;
        public string carryItems = "";
        public string carryShop = "";
        public string carryTotalSpent = "0";
        public string carryCurrency = "0";
    }

    public sealed class GameStatus
    {
        public bool isInProgress;
        public List<Ending> endings = new();
        public string enteredButton = "";
    }

    /// The stored document, normalized so callers never null-check it.
    public static JsonObject Load(string? json)
    {
        var doc = AccountFields.Get<JsonObject>(json) ?? new JsonObject();
        doc["lastSeq"] ??= 0L;
        doc["tables"] ??= new JsonObject();
        return doc;
    }

    public static long LastSeq(JsonObject doc) => (long)doc["lastSeq"]!;

    /// Applies one Chapter10RPGUpdateSaveData batch; returns the row count the response
    /// reports as `appliedCount`. Every row in the batch counts, deletes included - the
    /// capture's 119 updates all report exactly the number of rows sent.
    public static int Apply(JsonObject doc, JsonObject parameters)
    {
        var tables = (JsonObject)doc["tables"]!;

        // ponytail: never exercised - `truncates` is [] on all 119 captured updates, so the
        // element type is a guess. Matching table names by value can only ever no-op if it
        // turns out to carry something else.
        foreach (var t in (parameters["truncates"] as JsonArray) ?? new JsonArray())
            if (t is JsonValue v && v.TryGetValue<string>(out var name)) tables.Remove(name);

        var applied = 0;
        var last = LastSeq(doc);
        foreach (var (table, key, _, _) in Spec)
        {
            if (parameters[table] is not JsonArray rows) continue;
            foreach (var row in rows.OfType<JsonObject>())
            {
                applied++;
                if ((long?)row["seq"] is long seq && seq > last) last = seq;
                Upsert(tables, table, key, row);
            }
        }
        doc["lastSeq"] = last;
        return applied;
    }

    /// Chapter10RPGResetSaveData. Returns `deletedCount`: the rows removed. A soft reset
    /// clears the run but keeps the ending records and the seq counter; a hard reset wipes
    /// both (capture seq 4 -> deletedCount 0 / lastSeq kept, seq 6 -> the 2 endings / 0).
    public static int Reset(JsonObject doc, bool hard)
    {
        var tables = (JsonObject)doc["tables"]!;
        var deleted = 0;
        foreach (var (table, _, _, _) in Spec)
        {
            if (!hard && table == "endings") continue;
            if (tables[table] is JsonArray rows) deleted += rows.Count;
            tables.Remove(table);
        }
        if (hard) doc["lastSeq"] = 0L;
        return deleted;
    }

    /// The Chapter10RPGGetSaveData result body.
    public static JsonObject Read(JsonObject doc)
    {
        var tables = (JsonObject)doc["tables"]!;
        var result = new JsonObject { ["lastSeq"] = LastSeq(doc) };
        foreach (var (table, key, sort, echo) in Spec)
        {
            if (!echo) continue;
            result[table] = new JsonArray(Rows(tables, table, key, sort).ToArray());
        }
        return result;
    }

    /// The Chapter10RPGGetGameStatus result body. A run is in progress once the client has
    /// written its `save` row; `enteredButton` is that row's, and is "" when there is none.
    public static GameStatus Status(JsonObject doc)
    {
        var tables = (JsonObject)doc["tables"]!;
        var save = (tables["save"] as JsonArray)?.FirstOrDefault() as JsonObject;
        return new GameStatus
        {
            isInProgress = save is not null,
            enteredButton = (string?)save?["enteredButton"] ?? "",
            endings = Rows(tables, "endings", EndingKey, sort: true)
                .Select(r => r.Deserialize<Ending>(global::PacketJson.Options)!)
                .ToList(),
        };
    }

    private static List<JsonNode> Rows(JsonObject tables, string table, string[]? key, bool sort)
    {
        var rows = (tables[table] as JsonArray ?? new JsonArray())
            .Select(r => r!.DeepClone()).ToList();
        if (sort) rows.Sort((a, b) => Compare(a, b, key!));
        return rows;
    }

    private static void Upsert(JsonObject tables, string table, string[]? key, JsonObject row)
    {
        if (tables[table] is not JsonArray rows) tables[table] = rows = new JsonArray();

        // `seq` and `del` are transport, not save data: the getter never echoes them.
        var stored = new JsonObject();
        foreach (var (name, value) in row)
            if (name is not ("seq" or "del")) stored[name] = value?.DeepClone();

        var at = IndexOf(rows, key, stored);
        if ((bool?)row["del"] == true) { if (at >= 0) rows.RemoveAt(at); return; }
        if (at >= 0) rows[at] = stored; else rows.Add(stored);
    }

    private static int IndexOf(JsonArray rows, string[]? key, JsonObject row)
    {
        if (key is { Length: 0 }) return rows.Count > 0 ? 0 : -1;
        for (var i = 0; i < rows.Count; i++)
        {
            if (rows[i] is not JsonObject stored) continue;
            if (key is null
                ? JsonNode.DeepEquals(stored, row)
                : key.All(k => JsonNode.DeepEquals(stored[k], row[k]))) return i;
        }
        return -1;
    }

    private static int Compare(JsonNode? a, JsonNode? b, string[] key)
    {
        foreach (var k in key)
        {
            if (a?[k] is not JsonValue x || b?[k] is not JsonValue y) continue;
            var c = x.GetValueKind() == JsonValueKind.String
                ? string.CompareOrdinal(x.GetValue<string>(), y.GetValue<string>())
                : x.GetValue<long>().CompareTo(y.GetValue<long>());
            if (c != 0) return c;
        }
        return 0;
    }
}
