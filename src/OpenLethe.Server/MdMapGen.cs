using OpenLethe.Server.MirrorDungeon.Data;
using OpenLethe.Server.Wire;

namespace OpenLethe.Server;

// Port of lethe-server server/src/api/md/map/map_gen.rs. RNG uses Random.Shared (non-
// deterministic like Rust's thread_rng) - callers/tests must assert structural invariants,
// never exact generated ids. See memory: mirror-dungeon-rng-is-nondeterministic.

public struct DungeonNode
{
    public long node_type;
    public long encounter_id;
}

public readonly record struct DungeonCoordinate(int x, int y);

public sealed class DungeonGraph
{
    public const int Width = 16;
    public const int Height = 3;

    public readonly DungeonNode?[,] nodes = new DungeonNode?[Width, Height];
    public readonly HashSet<(DungeonCoordinate from, DungeonCoordinate to)> edges = new();
    public readonly HashSet<DungeonCoordinate> startingNodes = new();

    // Port of DungeonGraph::add_edge - bounds + node-existence checks exactly as Rust
    // (Rust's `as usize` cast on a negative i32 wraps to a huge value, which fails the
    // `>= W`/`>= H` bound check; we get the same "false" result via an explicit guard).
    public bool AddEdge(DungeonCoordinate from, DungeonCoordinate to)
    {
        if (from.x < 0 || from.y < 0 || to.x < 0 || to.y < 0) return false;
        if (from.x >= Width || from.y >= Height) return false;
        if (to.x >= Width || to.y >= Height) return false;

        if (nodes[from.x, from.y] is null) return false;
        if (nodes[to.x, to.y] is null) return false;

        edges.Add((from, to));
        return true;
    }

    public List<Ns> EncodeAsMd(long floor)
    {
        var dict = new Dictionary<long, Ns>();

        for (var w = 0; w < Width; w++)
        {
            for (var h = 0; h < Height; h++)
            {
                var node = nodes[w, h];
                if (node is null) continue;
                var nid = MdMapGen.NodeId(floor, w + 1, h);
                dict[nid] = new Ns
                {
                    f = floor,
                    s = w + 1,
                    nid = nid,
                    e = node.Value.node_type,
                    eid = node.Value.encounter_id,
                    nnids = new List<long>(),
                };
            }
        }

        foreach (var (from, to) in edges)
        {
            var fromId = MdMapGen.NodeId(floor, from.x + 1, from.y);
            var toId = MdMapGen.NodeId(floor, to.x + 1, to.y);
            if (!dict.ContainsKey(toId)) continue;
            if (dict.TryGetValue(fromId, out var fromNs)) fromNs.nnids.Add(toId);
        }

        var startNode = new Ns
        {
            f = floor,
            s = 0,
            nid = MdMapGen.NodeId(floor, 0, 0),
            e = 0,
            eid = 0,
            nnids = startingNodes.Select(c => MdMapGen.NodeId(floor, c.x + 1, c.y)).ToList(),
        };
        dict[startNode.nid] = startNode;

        return dict.Values.ToList();
    }
}

public static class MdMapGen
{
    public static long NodeId(long floor, long x, long y) => floor * 10000 + x * 100 + y;

    // Port of random_bool_vec. Rust's `rng.gen_range(a..=b)` is inclusive on both ends;
    // Random.Shared.Next(min, max) is exclusive on max, hence the `+ 1`.
    public static bool[] RandomBoolVec(int len, int weight, int offLo, int offHi)
    {
        var offset = Math.Max(weight + Random.Shared.Next(offLo, offHi + 1), 2);
        var bools = new bool[len];
        for (var i = 0; i < len; i++) bools[i] = i < offset;

        for (var i = len - 1; i > 0; i--)
        {
            var j = Random.Shared.Next(i + 1);
            (bools[i], bools[j]) = (bools[j], bools[i]);
        }

        return bools;
    }

    // Port of DefaultMirrorDungeonMapGenerator::generate_battle_node.
    public static DungeonNode GenerateBattleNode(ThemeStatic theme)
    {
        var opt = theme.mapGenOption;
        while (true)
        {
            var p = Random.Shared.NextDouble();
            List<long> pool;
            long nodeType;
            if (p < 0.45) { pool = opt.battlePool; nodeType = 1; }
            else if (p < 0.70) { pool = opt.abBattlePool; nodeType = 5; }
            else if (p < 0.80) { pool = opt.eventPool; nodeType = 3; }
            else if (p < 0.90) { pool = opt.hardAbBattlePool; nodeType = 14; }
            else { pool = opt.hardBattlePool; nodeType = 2; }

            if (pool.Count == 0) continue;
            var id = pool[Random.Shared.Next(pool.Count)];
            return new DungeonNode { node_type = nodeType, encounter_id = id };
        }
    }

    // Port of DefaultMirrorDungeonMapGenerator::generate_md4_graph.
    public static DungeonGraph GenerateMd4Graph(ThemeStatic theme, long floor, int floorWidth)
    {
        var graph = new DungeonGraph();

        var layerPopulation = new[]
        {
            RandomBoolVec(floorWidth, floorWidth / 2, 0, 2),
            RandomBoolVec(floorWidth, floorWidth / 2, 1, 3),
            RandomBoolVec(floorWidth, floorWidth / 2, 0, 2),
        };

        for (var x = 0; x < floorWidth; x++)
        {
            for (var y = 0; y < 3; y++)
            {
                var middleNodeMissing = !layerPopulation[1][x] && y != 1;
                var lastMiddleNodeMissing = x > 0 && y == 1 && !layerPopulation[1][x - 1];
                var beginning = x == 0 && y != 1;
                if (layerPopulation[y][x] || middleNodeMissing || lastMiddleNodeMissing || beginning)
                {
                    graph.nodes[x, y] = GenerateBattleNode(theme);
                }
            }
        }

        for (var y = 0; y < 3; y++)
        {
            if (graph.nodes[0, y] is not null)
                graph.startingNodes.Add(new DungeonCoordinate(0, y));
        }

        var bossPool = theme.mapGenOption.bossPool;
        var bossEncounterId = bossPool.Count > 0 ? bossPool[Random.Shared.Next(bossPool.Count)] : 2060101;

        var superShopProb = floor switch
        {
            0 => 0.03,
            1 => 0.25,
            2 => 0.50,
            3 => 0.75,
            _ => 1.00,
        };
        graph.nodes[floorWidth, 1] = new DungeonNode
        {
            node_type = 10,
            encounter_id = Random.Shared.NextDouble() < superShopProb ? 1 : 0,
        };
        graph.nodes[floorWidth + 1, 1] = new DungeonNode { node_type = 6, encounter_id = bossEncounterId };

        graph.AddEdge(new DungeonCoordinate(floorWidth, 1), new DungeonCoordinate(floorWidth + 1, 1));

        for (var x = 0; x < floorWidth; x++)
        {
            for (var y = 0; y < 3; y++)
            {
                for (var dy = -1; dy <= 1; dy++)
                {
                    graph.AddEdge(new DungeonCoordinate(x, y), new DungeonCoordinate(x + 1, y + dy));
                }
            }
        }

        // Remove overlapping ("crossing") edges: for every edge that steps diagonally
        // down-right (x+1, y-1), if the crossing diagonal up-right edge also exists, drop
        // one of the two at random.
        var oldEdges = new HashSet<(DungeonCoordinate, DungeonCoordinate)>(graph.edges);
        foreach (var (from, to) in oldEdges)
        {
            if (from.x + 1 != to.x || from.y - 1 != to.y) continue;

            var crossEdge = (new DungeonCoordinate(from.x, from.y - 1), new DungeonCoordinate(from.x + 1, from.y));
            if (!graph.edges.Contains(crossEdge)) continue;

            if (Random.Shared.NextDouble() < 0.5)
                graph.edges.Remove(crossEdge);
            else
                graph.edges.Remove((from, to));
        }

        return graph;
    }

    // Fallback event ids for fixed-layout floors. Act-3 packs (theme-floor-t6, ids 1501-1520)
    // ship a CREATE_FIXED_FLOOR node list whose event slots carry encounterId 0 - the real
    // generator's FILL_EMPTY_EVENT sequence step is what gives them an id - but those packs also
    // declare an EMPTY mapGenOption.eventPool, so GenerateNewFloor used to emit e=3/eid=0.
    // The client has no event to resolve for eid 0, so the event screen opened and never
    // advanced: that is the "events get stuck on floors 10+" bug. Fall back to every event id
    // this dungeon already uses elsewhere; those come from the act-1/2 packs' pools (901xxx,
    // 971xxx, ...), so the client is guaranteed to have the static data to render them.
    private static readonly Lazy<List<long>> DungeonEventPool = new(() =>
        MdThemeData.AllThemes()
            .SelectMany(t => t.mapGenOption.eventPool)
            .Distinct()
            .OrderBy(id => id)
            .ToList());

    // e==15 is the client's hidden-node type: retail draws it as the "?" that reveals into the
    // hidden boss. Verified against md-extreme-run-2 seq335/437, the two rows where
    // changedHiddenNode flips true (i.e. the client just entered it).
    public const long HiddenBattleNodeType = 15;

    // Hidden battles, driven by mirror-dungeon-common-data's hiddenBattleInfo (see
    // MdCommonTable.HiddenBattle): its pool names the hidden-boss stages (the shipped files list
    // 9000005-9000008, whose battle files each reward an exclusive E.G.O gift), minFloorCondition
    // the first floor they appear on, and probInfo each floor's chance of one.
    public static bool IsHiddenBattle(long encounterId) => MdCommonTable.HiddenBattle.pool.Contains(encounterId);

    // A hidden stage's exclusive gifts: the EGO_GIFT entries of its own battle file's rewardList.
    public static List<long> HiddenGiftsOf(long stageId) =>
        MdAbRewards.GetByNodeId(stageId)?
            .Where(r => r.rewardType == "EGO_GIFT")
            .Select(r => r.rewardId)
            .ToList() ?? new List<long>();

    // The pool's stages this run has not beaten yet - owning a stage's exclusive gift means the
    // run already won that fight, so it is not offered again.
    public static List<long> UnbeatenHiddenBattles(MdHiddenBattleInfo info, MirrorOriginSaveInfo save)
    {
        var owned = new HashSet<long>(save.currentInfo?.egs?.Select(e => e.id) ?? Enumerable.Empty<long>());
        return info.pool.Where(id => !HiddenGiftsOf(id).Any(owned.Contains)).ToList();
    }

    // One roll per floor at that floor's prob; a hit turns one of the floor's event nodes into a
    // hidden node (e==15) against a random unbeaten pool stage, keeping the node's edges. Sector-1
    // events are preferred: that is where retail put it (md-extreme-run-2 floor 10 - the start
    // fans out to an event, 100100, and the hidden node, 100102 e=15 eid 9000007).
    public static void PlaceHiddenBattle(List<Ns> graph, long floor, MirrorOriginSaveInfo save, MdHiddenBattleInfo info)
    {
        if (Random.Shared.NextDouble() >= info.ProbForFloor(floor)) return;

        var pool = UnbeatenHiddenBattles(info, save);
        if (pool.Count == 0) return;

        var slots = graph.Where(n => n.s == 1 && n.e == 3).ToList();
        if (slots.Count == 0) slots = graph.Where(n => n.e == 3).ToList();
        if (slots.Count == 0) return;

        var node = slots[Random.Shared.Next(slots.Count)];
        node.e = HiddenBattleNodeType;
        node.eid = pool[Random.Shared.Next(pool.Count)];
    }

    private static long PickFallbackEventId()
    {
        var pool = DungeonEventPool.Value;
        return pool.Count > 0 ? pool[Random.Shared.Next(pool.Count)] : 0;
    }

    // Port of DefaultMirrorDungeonMapGenerator::generate_new_floor. Throws if the theme id
    // is unknown (Rust returns diesel::NotFound); the handler layer maps that to a 500.
    // customThemes: the account's uploaded packs, so a custom pack picked after a starlight
    // detection can be entered (an uploaded id wins over a static one, as in MdThemePool).
    public static void GenerateNewFloor(long floor, long themeId, MirrorOriginSaveInfo save,
        IReadOnlyList<ThemeStatic>? customThemes = null)
    {
        var theme = customThemes?.FirstOrDefault(t => t is not null && t.id == themeId)
            ?? MdThemeData.GetThemeById(themeId)
            ?? throw new KeyNotFoundException($"MD theme {themeId} not found");

        var seq = theme.mapGenSequence.FirstOrDefault();
        List<Ns> graph;

        if (seq?.type_ == "CREATE_EMPTY_MD4_FLOOR")
        {
            var floorWidth = seq.numberList.Count > 0
                ? (int)seq.numberList[0]
                : (int)(4 + floor / 3);
            graph = GenerateMd4Graph(theme, floor, floorWidth).EncodeAsMd(floor);
        }
        else if (seq?.type_ == "CREATE_FIXED_FLOOR")
        {
            graph = seq.nodeList.Select(node =>
            {
                var eid = node.encounterId;
                if (node.encounterType == 3 && node.encounterId == 0)
                {
                    var opt = theme.mapGenOption;
                    // A pack that carries a special-event pool but no probability means "always
                    // take from that pool" - the old code required BOTH to be non-null, so such a
                    // pack fell straight through with eid still 0.
                    if (opt.specialEventPool is { Count: > 0 } specialEvents)
                    {
                        if (opt.specialEventProb is not { } prob || Random.Shared.NextDouble() < prob)
                        {
                            eid = specialEvents[Random.Shared.Next(specialEvents.Count)];
                        }
                    }

                    if (eid == 0 && opt.eventPool.Count > 0)
                    {
                        eid = opt.eventPool[Random.Shared.Next(opt.eventPool.Count)];
                    }

                    // Never emit an e=3 node with eid 0: the client cannot resolve it and the
                    // event screen hangs on entry (see DungeonEventPool above).
                    if (eid == 0) eid = PickFallbackEventId();
                }

                return new Ns
                {
                    f = floor,
                    s = node.sector,
                    nid = NodeId(floor, node.sector, node.idx),
                    e = node.encounterType,
                    eid = eid,
                    nnids = node.connectedNextNodeIdxList.Select(n => NodeId(floor, node.sector + 1, n)).ToList(),
                };
            }).ToList();
        }
        else
        {
            graph = new List<Ns>();
        }

        PlaceHiddenBattle(graph, floor, save, MdCommonTable.HiddenBattle);
        save.dungeonMap.ns.AddRange(graph);
        UpdateResult(save, floor, themeId);
    }

    // Port of update_result.
    public static void UpdateResult(MirrorOriginSaveInfo save, long floor, long selectedTheme)
    {
        if (floor != 0)
        {
            save.currentInfo.cn = new Currentnode { f = floor, s = 0, nid = NodeId(floor, 0, 0) };
            // eid is reset to 0 only when advancing onto a real floor; floor-0 select leaves the
            // injected eid (-1) untouched (capture: floor 0 eid == -1, floors 1+ eid == 0).
            save.currentInfo.eid = 0;
        }

        var selectedThemeFloor = save.currentInfo.tfps.FirstOrDefault(t => t.tfid == selectedTheme);
        var egoRewards = selectedThemeFloor?.egs ?? new List<long>();
        var upgradeRewards = selectedThemeFloor?.upegs ?? new List<long>();

        save.currentInfo.tfps.Clear();

        // idx is the act number, not a constant: floors 0-4 -> 1, 5-9 -> 2, 10-14 -> 3, etc.
        // Confirmed against 11 (floor, idx) pairs in tests/OpenLethe.Tests/fixtures/md-extreme-run.jsonl.
        var tfsIdx = floor / 5 + 1;
        save.currentInfo.tfs.Add(new Tfs { idx = tfsIdx, f = floor, tfid = selectedTheme, egs = egoRewards, upegs = upgradeRewards });

        save.isEndDungeon = 0;
        save.currentInfo.sepsCreated = 2;
        // startKeyword is the run's affinity, set upstream by AcquireStart (capture: constant
        // "Combustion" for this run) and preserved across floor selects - do NOT overwrite it.
    }

    // Port of ThemeFloorPoolSelector::random_from_pool (ego_reward_count=4, theme_floor_count=4).
    public static List<Tfps> PickThemes(long floor)
    {
        var themePool = new MdThemePool();
        // The client's theme-floor table (dumpedData/limbus_data/mirrordungeon-theme-floor/<n>.json)
        // writes exceptionConditions with the act in dungeonIdx (same 1-based value the wire uses)
        // and a 0-based offset inside that act in selectableFloors (empty = any floor of the act).
        // The run start is the one place tier 0 appears: the capture's 8-pack offer is 4 x d0[0]
        // (wire idx 0, the "easy" group) + 4 x d1[0] (wire idx 1, the "hard" one).
        var startTier = floor / 5;   // 0 only at the run start - the easy group
        var startAct = startTier + 1;   // the tier of the act being entered
        var startOffset = floor % 5;
        var pools = themePool.pools.Values
            .Where(t => t.exceptionConditions.Any(c => c.dungeonIdx == startTier
                && (c.selectableFloors.Count == 0 || c.selectableFloors.Contains(startOffset)))
                || t.exceptionConditions.Any(c => c.dungeonIdx == startAct
                && (c.selectableFloors.Count == 0 || c.selectableFloors.Contains(startOffset))))
            .ToList();

        var easy = pools.Where(t => t.exceptionConditions.Any(c => c.dungeonIdx == startTier)).ToList();
        var hard = pools.Where(t => t.exceptionConditions.Any(c => c.dungeonIdx == startAct)).ToList();

        var result = new List<Tfps>();
        foreach (var theme in ChooseMultiple(easy, 4))
            result.Add(BuildTfps(themePool, theme, floor, hard: false));
        foreach (var theme in ChooseMultiple(hard, 4))
            result.Add(BuildTfps(themePool, theme, floor, hard: true));
        return result;
    }

    // RecreateThemeFloorPool re-rolls the pool for selecting the NEXT floor (cn.f + 1). Unlike
    // the start-of-run pool (PickThemes returns 8 = 4 easy + 4 hard, idx = difficulty tier),
    // a re-roll returns exactly 4 themes for the upcoming floor, each idx = that floor's act
    // number, drawn from the tier whose dungeonIdx equals the act (act-1 conditions list
    // explicit selectableFloors; act>=2 conditions leave selectableFloors empty = any floor in
    // the act). Verified against all 19 captured RecreateThemeFloorPool records (floors 0-9):
    // count == 4, idx == act, and dungeonIdx-eligibility hold every time. tfid/egs/upegs are
    // RNG (masked in replay) - only count and idx are byte-verified.
    public static List<Tfps> RecreateThemes(long selectionFloor)
    {
        // Conventions (settled against the capture + the client's own table):
        //   * the wire idx IS the price-1 act: floor / 5 + 1 (1-based) - byte-verified;
        //   * the data's dungeonIdx is the SAME value, NOT a 0-based act: the pool prepared at
        //     floor 4 for floor 5 is 1023/1404/1408/1414 (all d2[]) and the one prepared at floor 9
        //     for floor 10 is 1501/1508/1518/1112 (all d3[]) - comparing against floor/5 pulled the
        //     previous act's tier, so the 11-15 floor packs never showed up;
        //   * selectableFloors is the 0-based offset inside that act (floor 1 -> d1[1]: 1005 and
        //     1204 are d1[0,1], 1303 is d1[1]; empty = any floor of the act).
        // Tier 0 exists only for the run-start "easy" group (PickThemes) - those packs carry a d1
        // condition too, so they stay reachable once the run is under way.
        var act = selectionFloor / 5 + 1;   // wire idx AND dungeonIdx
        var offset = selectionFloor % 5;    // selectableFloors
        var themePool = new MdThemePool();
        var eligible = themePool.pools.Values
            .Where(t => t.exceptionConditions.Any(c => c.dungeonIdx == act
                && (c.selectableFloors.Count == 0 || c.selectableFloors.Contains(offset))))
            .ToList();

        return ChooseMultiple(eligible, 4)
            .Select(theme => new Tfps
            {
                idx = act,
                tfid = theme.id,
                egs = themePool.SelectRandomEgosFromPool(theme.id, 4, selectionFloor),
                upegs = new List<long> { 9051, 9048, 9001, 9066 },
            })
            .ToList();
    }

    private static Tfps BuildTfps(MdThemePool pool, ThemeStatic theme, long floor, bool hard) => new()
    {
        tfid = theme.id,
        // Capture-verified (AcquireStartEgoGiftsAndCreateThemePool, 8/8 entries): the easy
        // (idx 0) themes carry 3 reward egs, the hard (idx 1) themes carry 4 - NOT a flat 4
        // like RecreateThemes' later re-rolls.
        egs = pool.SelectRandomEgosFromPool(theme.id, hard ? 4 : 3, floor),
        idx = hard ? 1 : 0,
        upegs = new List<long> { 9051, 9048, 9001, 9066 },
    };

    // Port of Rust's `choose_multiple` usage - random k distinct elements, or all (shuffled)
    // if the source has k or fewer.
    public static List<T> ChooseMultiple<T>(IEnumerable<T> src, int k)
    {
        var list = src.ToList();
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = Random.Shared.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }

        return list.Take(Math.Min(k, list.Count)).ToList();
    }
}
