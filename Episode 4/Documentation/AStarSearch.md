# AStarSearch.cs

**Namespace:** `Goodgulf.Pathfinding`

Implements the A* path search algorithm. Runs synchronously on the main thread against the graph's live buffer. Participates in the double-buffer reference-counting protocol so searches never read from a buffer being written to by a Burst rebuild job.

---

## Types defined

### `PathStatus` (enum)

| Value | Meaning |
|---|---|
| `Success` | A path was found. `PathResult.Waypoints` is populated and ordered start → goal |
| `NoPath` | Both endpoints are valid but no connected path exists between them |
| `InvalidEndpoints` | One or both endpoints are outside the graph, or no walkable node exists within the snap radius |

### `PathResult` (struct)

| Field | Description |
|---|---|
| `Status` | Outcome of the search |
| `Waypoints` | World-space positions along the path. Y = terrain height at each node. Null unless Status is Success |

---

## `AStarSearch` (static class)

### Static scratch state

```csharp
private static readonly Dictionary<int, AStarNode> _openMap   = new(4096);
private static readonly Dictionary<int, AStarNode> _closedMap = new(4096);
private static readonly MinHeap<AStarNode>         _openHeap  = new(4096);
```

All three are allocated once and cleared at the start of each search. This eliminates GC pressure from repeated per-search allocations. The consequence is that `AStarSearch` is **not thread-safe** — only one search may run at a time. If concurrent searches are needed, give each caller its own scratch collections.

---

### `FindPath()` — public entry point

```csharp
PathResult result = AStarSearch.FindPath(graph, startWorld, goalWorld, maxIterations);
```

| Parameter | Default | Description |
|---|---|---|
| `graph` | — | The `PathfindingGraph` to search. Must not be null |
| `startWorld` | — | Agent's current world position. Y is ignored |
| `goalWorld` | — | Desired destination in world space. Y is ignored |
| `maxIterations` | 100,000 | Safety cap on the search loop |

**Coordinate conversion.** Both positions are converted to node-grid space via `GraphConfig.WorldToNodeCoord()` before any graph lookups:

```csharp
int startNodeX = graph.Config.WorldToNodeCoord(startWorld.x);
// = RoundToInt(startWorld.x * NodesPerMetre)
```

**Endpoint snapping.** If either endpoint lands on an unwalkable cell, `FindNearestWalkable()` searches outward in expanding rings up to 5 node cells to find the closest walkable neighbour. This handles agents spawning inside colliders or targets specified on cliff edges. If no walkable node is found within the snap radius, `InvalidEndpoints` is returned.

**Buffer reference counting.** `graph.IncrementSearchCount()` is called before the search begins. `graph.DecrementSearchCount()` is called in a `finally` block so it always fires even on exception. This is what allows `NotifyRebuildComplete()` to defer the buffer swap until no searches are in flight.

---

### The search loop

Standard A* with terrain-specific adjustments:

**Movement cost:**
```
edgeCost = baseCost + |heightDiff| × VerticalScale
baseCost = NodeSpacing          for cardinal moves
baseCost = NodeSpacing × √2     for diagonal moves
VerticalScale = 0.5
```

Multiplying by `NodeSpacing` ensures costs are in world metres regardless of graph resolution, so the heuristic and the actual cost remain comparable.

**Heuristic — octile distance:**
```
dx   = |nodeAX - nodeBX|
dz   = |nodeAZ - nodeBZ|
H    = (√2 × min(dx, dz) + (max(dx, dz) - min(dx, dz))) × NodeSpacing
```

Admissible (never overestimates) and consistent (satisfies triangle inequality) for any 8-connected grid with uniform step costs. Consistent means no node is ever re-expanded, so the closed set check is strictly correct.

**Stale-entry guard.** Because `MinHeap` does not support decrease-key, improved paths push new heap entries rather than updating in place. When a node is popped, it is checked against both maps:

```csharp
if (_closedMap.ContainsKey(current.Index)) continue;
if (_openMap.TryGetValue(current.Index, out var best) && best.G < current.G) continue;
```

The second check catches nodes that are still in the open set but have since been improved — the current heap entry is stale.

---

### Path reconstruction and string pulling

`ReconstructPath()` walks `ParentIdx` references backward from the goal node through `_closedMap`, collecting waypoints, then calls `waypoints.Reverse()`. The first element after reversal is the start node (at or very near the agent's position); the last is the goal.

A string-pull pass then removes collinear intermediate waypoints:

```csharp
if (Vector3.Dot((B - A).normalized, (C - A).normalized) > 0.999f)
    remove B
```

This reduces waypoint count on straight runs across flat terrain, making agent movement smoother and reducing the frequency of waypoint-reach checks in `FollowCurrentPath()`.

**Note:** Waypoint 0 is the snapped start node, which is at or very close to the agent's current position. `PathfindingAgent.FollowCurrentPath()` will consume it immediately on the first frame. This is expected behaviour — `_pathIndex` advances to 1 within one Update tick.

---

### `FindNearestWalkable()` — endpoint snapping

Iterates the perimeter of concentric square rings in node-grid space, radius 1 to `radius` (default 5). Returns the flat index of the first walkable node found, or -1 if none exists within the search radius.

```
Ring r=1: 8 cells  (3×3 perimeter)
Ring r=2: 16 cells (5×5 perimeter)
Ring r=3: 24 cells (7×7 perimeter)
...
```

Only the perimeter of each ring is checked — inner cells were already checked in the previous ring. At `NodeSpacing = 2 m` and `radius = 5`, the maximum snap distance is 10 world metres.

---

## `AStarNode` (internal struct)

Heap element and closed-map value.

| Field | Description |
|---|---|
| `Index` | Flat graph array index |
| `NodeX` | Node-grid X coordinate (not world metres) |
| `NodeZ` | Node-grid Z coordinate (not world metres) |
| `G` | Actual cost from start, in world metres |
| `H` | Heuristic cost to goal, in world metres |
| `F` | G + H — heap sort key |
| `ParentIdx` | Flat index of parent node, or -1 for the start node |

`NodeX` and `NodeZ` store node-grid coordinates so that neighbour expansion can use integer arithmetic (`nnodeX = current.NodeX + offset.x`) without repeated floating-point conversion. World-space waypoints are computed only once during `ReconstructPath()`.

---

## `MinHeap<T>` (internal class)

Generic binary min-heap backed by `List<T>`. O(log n) push and pop. No decrease-key operation — stale entries are handled by the guard in the search loop.

| Method | Description |
|---|---|
| `Push(T item)` | Adds item and sifts up |
| `Pop()` | Removes and returns the minimum item, sifts down |
| `Clear()` | Empties the list without deallocating |
| `Count` | Current number of elements |

---

## Performance characteristics

| Path length | Typical main-thread cost |
|---|---|
| ~50 nodes (short run, flat terrain) | < 0.1 ms |
| ~500 nodes (medium distance) | 0.5 – 2 ms |
| ~5000 nodes (long distance) | 5 – 20 ms |

The 0.5-second path cooldown in `PathfindingAgent` bounds the search frequency per agent. For games with large numbers of agents, consider a request queue that processes one or two searches per frame rather than searching synchronously in each agent's `Update()`.

---

## Known pitfalls

**Agent oscillates near its starting position** — `HandleMoveTo()` or `HandleLOSMovement()` is calling `RequestPath()` while a valid path is still being followed. Ensure re-path calls are guarded by a `pathExhausted` check — only request a new path when `_pathIndex >= _currentPath.Count`.

**`InvalidEndpoints` returned despite valid world position** — The graph has not been built yet (`Graph == null`), or the world position falls outside the graph bounds. Check that `TerrainGraphIntegration.Initialize()` has been called and the graph origin/size covers the agent's location. Use the `BuildOrResizeGraph` debug log to verify bounds.

**Path found but waypoints all at Y = 0** — The Burst rebuild job wrote `WorldY = 0` for every node because no loaded chunk matched the world positions being sampled. This usually means `ChunkSizeXZ` in `TerrainHeightSample` does not match the actual runtime chunk footprint, or the graph origin is offset so that loaded terrain falls outside the sampled range.
