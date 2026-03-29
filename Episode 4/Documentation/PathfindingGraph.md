# PathfindingGraph.cs

**Namespace:** `Goodgulf.Pathfinding`

Defines the core data types and the double-buffered graph container that underpins the entire pathfinding system. All types in this file are pure data and logic — no MonoBehaviour, no Unity lifecycle.

---

## Coordinate system

The graph operates in two distinct spaces that must never be confused.

**World space** is standard Unity XZ metres. The player stands at world position (500, 0, 200). Terrain chunks are positioned at world coordinates.

**Node-grid space** is world space scaled by `NodesPerMetre`. At the default resolution of 0.5 nodes/m, world position (500, 0, 200) maps to node-grid coordinate (250, 100). Node-grid coordinates are always integers.

The conversion formula is:

```
nodeCoord  = RoundToInt(worldPos * NodesPerMetre)
worldPos   = nodeCoord * NodeSpacing          where NodeSpacing = 1 / NodesPerMetre
arrayIndex = (nodeZ - origin.y) * width + (nodeX - origin.x)
```

All internal graph methods work in node-grid space. World-space inputs are converted at the boundary — in `WorldToIndex()` and in `AStarSearch.FindPath()`.

---

## Types defined

### `NodeData` (struct)

Blittable per-node record. One instance per graph cell, stored flat in the NativeArrays.

| Field | Type | Description |
|---|---|---|
| `WorldY` | `float` | Terrain height at the node's world-space centre, sampled from the heightmap |
| `Normal` | `float3` | Approximated surface normal, derived from heightmap partial derivatives |
| `Walkable` | `bool` | Combined flag — false if slope, height, or physics obstacle checks fail |
| `GlobalIndex` | `int` | Flat array index, cached for self-reference in Burst jobs |

Being blittable means `NodeData` lives inside a `NativeArray<NodeData>` and can be read and written by Burst jobs without marshalling.

---

### `GraphConfig` (struct, Serializable)

Inspector-serialisable configuration controlling graph resolution and walkability thresholds. Passed to rebuild jobs by value so jobs receive a snapshot at scheduling time.

| Field | Default | Description |
|---|---|---|
| `NodesPerMetre` | 0.5 | Graph density. 0.5 = one node per 2 m. 1.0 = one node per 1 m. Changing this requires a full graph rebuild. |
| `MaxSlopeAngle` | 45° | Nodes whose surface normal deviates more than this from vertical are unwalkable |
| `MinWalkableHeight` | 0 | Nodes below this world-Y are unwalkable (water exclusion) |
| `MaxWalkableHeight` | 10000 | Nodes above this world-Y are unwalkable |
| `ObstacleCheckRadius` | 0.4 | Sphere radius used by the physics obstacle overlay per node |
| `ObstacleLayers` | Everything | LayerMask for obstacle colliders. **Must not include the Terrain layer.** |
| `AllowDiagonals` | true | 8-connected vs 4-connected neighbour set |

**Derived helpers** (not serialised):

```csharp
float NodeSpacing            // = 1 / NodesPerMetre
int   WorldToNodeCount(float worldDistance)
int   WorldToNodeCoord(float worldPos)   // = RoundToInt(worldPos * NodesPerMetre)
float NodeToWorldCoord(int nodeCoord)    // = nodeCoord * NodeSpacing
```

`GraphConfig.Default` provides the values above and is used by `TerrainGraphIntegration` when no custom config is assigned.

---

### `DirtyRegion` (struct)

An inclusive AABB in node-grid space describing which cells need rebuilding. All factory methods require the active `GraphConfig` to convert world distances to node counts at the current resolution.

```csharp
// From a world-space AABB (e.g. after an explosion):
DirtyRegion r = DirtyRegion.FromWorldAABB(worldMin, worldMax, config);

// From a loaded chunk:
DirtyRegion r = DirtyRegion.FromChunk(chunkCoord, chunkSize, config);

// Expand to catch cross-chunk border nodes:
DirtyRegion r = r.Expanded(2);
```

`IsValid` returns false when `Max < Min` on either axis, which can happen after clamping to graph bounds. Always check before scheduling a partial rebuild.

---

### `PathfindingGraph` (class, IDisposable)

Owns the two persistent `NativeArray<NodeData>` allocations and manages the double-buffer swap. Created and disposed by `TerrainGraphIntegration`. Not a MonoBehaviour.

#### Construction

```csharp
// All dimensions are in node-grid units, not world metres
var graph = new PathfindingGraph(
    origin: new int2(originNodeX, originNodeZ),
    width:  3504,
    height: 3504,
    config: GraphConfig.Default);
```

#### Double-buffer protocol

The graph owns two identically-sized NativeArrays with `Allocator.Persistent`:

- **Buffer A** (live) — read by A* searches
- **Buffer B** (staging) — written by Burst rebuild jobs

The swap is a single C# tuple assignment and costs nothing. It is deferred until no searches are in flight, tracked by `_activeSearchCount`.

| Method | Called by | Purpose |
|---|---|---|
| `IncrementSearchCount()` | `AStarSearch.FindPath()` | Marks a search as reading Buffer A |
| `DecrementSearchCount()` | `AStarSearch.FindPath()` finally block | Marks search complete; triggers deferred swap if pending |
| `NotifyRebuildComplete()` | `TerrainGraphIntegration.FinishOverlay()` | Swaps immediately if safe, or sets `_swapPending` |

#### Coordinate conversion

```csharp
// World → index
int  idx  = graph.WorldToIndex(worldX, worldZ);      // float overload
int  idx  = graph.NodeToIndex(nodeX, nodeZ);          // node-grid overload
int  idx  = graph.NodeToIndex(int2 nodeXZ);

// Index → coordinates
int2    node  = graph.IndexToNode(int index);         // returns node-grid XZ
Vector2 world = graph.IndexToWorld(int index);        // returns world XZ (centre of cell)

// Bounds checks
bool ok = graph.InBoundsWorld(float worldX, float worldZ);
bool ok = graph.InBoundsNode(int nodeX, int nodeZ);
bool ok = graph.InBoundsNode(int2 nodeXZ);
```

All out-of-bounds calls return `-1` or `false` rather than throwing. The A* search and overlay pass rely on this — they check `idx >= 0` rather than validating bounds separately.

#### Read helpers (main thread only)

```csharp
NodeData nd     = graph.GetNode(float worldX, float worldZ);
bool     walk   = graph.IsWalkable(float worldX, float worldZ);
Vector3  centre = graph.WorldCenter();                // world-space centre of graph coverage
DirtyRegion c   = graph.ClampRegion(DirtyRegion r);   // clamps to graph bounds
```

#### Disposal

`PathfindingGraph` implements `IDisposable`. Call `Dispose()` when the graph is no longer needed. `TerrainGraphIntegration.OnDestroy()` handles this automatically.

```csharp
graph.Dispose();  // frees bufferA, bufferB, neighborOffsets
```

---

## Memory budget

| `NodesPerMetre` | Terrain span | Nodes per axis | Total nodes | Memory (×2 buffers) |
|---|---|---|---|---|
| 0.5 | 7000 m (loadRadius 3) | 3500 | ~12.25 M | ~588 MB |
| 0.5 | 5000 m (loadRadius 2) | 2500 | ~6.25 M | ~300 MB |
| 0.25 | 7000 m (loadRadius 3) | 1750 | ~3.06 M | ~147 MB |

`NodeData` is 24 bytes: 4 (float) + 12 (float3, aligned) + 1 (bool, padded to 4) + 4 (int) = 24 bytes.

Keep `NodesPerMetre` at 0.25–0.5 for open-world survival games. 1.0 nodes/m is only warranted if the game has narrow corridors or tight doorways requiring precise obstacle avoidance.

---

## Known pitfalls

**Do not include the Terrain layer in `ObstacleLayers`.** The `TerrainCollider` is present at every node. Including it in the obstacle mask marks the entire graph unwalkable.

**`NodeSpacing` is a derived property, not serialised.** Changing `NodesPerMetre` in the Inspector does not automatically update any cached value — call `TerrainGraphIntegration.RebuildWithNewConfig()` to apply the change and resize the graph.

**`DirtyRegion` factory methods require `GraphConfig`.** The old `FromWorldAABB(worldMin, worldMax)` two-argument overload no longer exists. Always pass `_config` as the third argument.
