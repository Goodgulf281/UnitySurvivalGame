# TerrainGraphIntegration.cs

**Namespace:** `Goodgulf.Pathfinding`  
**Requires:** `TerrainStreamingController` on the same GameObject  
**Implements:** `IDebuggable`

The central MonoBehaviour of the pathfinding system. It owns the `PathfindingGraph`, manages chunk registration, schedules Burst rebuild jobs, runs the streamed physics overlay, and exposes the public API used by agents and gameplay code.

---

## Responsibilities

| Responsibility | Method(s) |
|---|---|
| Register chunks when fully generated | `InitializeListeners()`, `OnChunkFullyLoaded()` |
| Build graph covering all loaded chunks | `BuildOrResizeGraph()` |
| Schedule Burst full rebuild | `ScheduleFullRebuild()` |
| Schedule Burst partial rebuild | `SchedulePartialRebuild()` |
| Complete job and begin overlay | `FinaliseJob()` |
| Run streamed physics overlay | `BeginOverlay()`, `TickOverlay()`, `FinishOverlay()` |
| Heightmap cache management | `GetOrCacheHeightmap()`, `InvalidateHeightmapCache()` |
| Obstacle cache invalidation | `InvalidateObstacleCache()` |
| Answer agent path requests | `RequestPath()` |
| Runtime terrain deformation | `RequestDirtyRegionRebuild()` |

---

## Inspector fields

### Graph Config

`_config` (`GraphConfig`) — see [PathfindingGraph.md](PathfindingGraph.md) for field descriptions. The most important field is `NodesPerMetre` (default 0.5). To change resolution at runtime use `RebuildWithNewConfig()`.

### Async Physics Overlay (Option A)

`_maxOverlayNodesPerFrame` — number of nodes processed per frame during the physics obstacle overlay pass. Range 500–20,000. Default 3000. Increase this to reduce the wall-clock time of the overlay pass at the cost of slightly higher per-frame CPU time. At 50,000 nodes/frame a 1.75M-node graph completes in roughly 0.6 seconds.

### Static Obstacle Cache (Option C)

`_useObstacleCache` — when enabled, `OverlapSphereNonAlloc` is skipped for nodes whose obstacle state has not been explicitly dirtied. Disable only if your scene has dynamic obstacle geometry that changes without calling `InvalidateObstacleCache()`.

### Debug

`_debugEnabled` — enables `LogInfo` and `LogWarning` output for this component.  
`_drawGizmos` — enables walkable/unwalkable node cubes in the Scene view (select the GameObject).  
`_gizmoNodeLimit` — maximum nodes drawn per `OnDrawGizmosSelected` call. The gizmo loop uses step-sampling (`i += Width/10`) to cover the full graph within the limit.

---

## Singleton

```csharp
TerrainGraphIntegration.Instance
```

Set in `Awake()`, cleared in `OnDestroy()`. Agents call `Instance.RequestPath()`. Gameplay calls `Instance.RequestDirtyRegionRebuild()` and `Instance.InvalidateObstacleCache()`.

---

## Initialisation sequence

The correct startup order is critical. Registering chunks before their heightmap is written produces all-zero `WorldY` values that corrupt the graph permanently (via the heightmap cache).

```
TerrainStreamingController.LoadInitialChunks()
    → each chunk: Instantiate → GenerateChunk (writes heightmap) → PlacePrefabs → onChunkLoadComplete
    → all chunks done → onInitialChunksLoaded fires

onInitialChunksLoaded listener:
    TerrainGraphIntegration.Initialize()
        → clears heightmap cache
        → iterates terrain children, registers all in _knownChunks, marks all _heightmapDirty
        → BuildOrResizeGraph()
        → ScheduleFullRebuild()

onChunkLoadComplete listener (runtime streaming):
    TerrainGraphIntegration.OnChunkFullyLoaded(chunkCoord)
        → registers chunk in _knownChunks
        → marks _heightmapDirty
        → resizes graph if needed, or enqueues partial rebuild dirty region
```

`InitializeListeners()` wires up both event listeners and must be called before `Initialize()`. Typically called from whichever script coordinates the startup sequence.

---

## Frame update flow

`Update()` runs four steps in order each frame (guarded by `_initialised`):

1. `SyncChunks()` — detects removed terrain children and removes them from `_knownChunks`. Chunk additions are handled by `OnChunkFullyLoaded`, not here.
2. `TickOverlay()` — if `_overlayInProgress`, processes up to `_maxOverlayNodesPerFrame` nodes of the physics obstacle pass.
3. `FinaliseJob()` — if a Burst job's `JobHandle.IsCompleted`, calls `Complete()`, disposes TempJob arrays, and starts the overlay pass.
4. Schedule pending work — if no job is in flight, no overlay is running, and `_framesSinceLastRebuild >= 3`, drains `_pendingDirtyRegions` into a single merged partial rebuild.

---

## Graph sizing — `BuildOrResizeGraph()`

Sizes the graph to cover `unloadRadius + 1` chunks around the player so normal streaming never forces a resize:

```csharp
int   margin    = _controller.UnloadRadius() + 1;
float worldMinX = (playerChunk.x - margin) * chunkSize;
float worldMaxX = (playerChunk.x + margin + 1) * chunkSize;
// same for Z

int originX = FloorToInt(worldMinX * NodesPerMetre) - 2;
int width   = CeilToInt((worldMaxX - worldMinX) * NodesPerMetre) + 4;
```

The `-2` / `+4` adds a 2-node border so cross-chunk border nodes always have their neighbours within the graph. This is not the same as the old one-chunk padding, which inflated the graph by an entire 1000m strip.

The graph is clamped to a maximum of 8192 nodes per axis. With `NodesPerMetre = 0.5` and `unloadRadius = 5`, the graph is approximately 6000 × 6000 nodes = 36M nodes — consider reducing `NodesPerMetre` to 0.25 for large load radii.

---

## Streamed physics overlay (Option A)

The overlay is the main-thread pass that marks physics-blocked nodes unwalkable. Rather than blocking until all nodes are processed, it spreads across frames using a 2D iteration cursor:

```
_overlayNodeX, _overlayNodeZ  — current position in node-grid space
_overlayRegion                — the clamped dirty region being processed
_maxOverlayNodesPerFrame      — budget per frame
```

`TickOverlay()` advances the cursor by up to `budget` steps per call, iterating the dirty region row by row. When the cursor passes `_overlayRegion.Max.y`, `FinishOverlay()` is called which triggers the buffer swap.

The buffer swap is deferred until `FinishOverlay()` — agents continue reading the previous live buffer during the overlay pass, which may span many frames. This is safe because the Burst job has already completed and is no longer writing to staging.

---

## Static obstacle cache (Option C)

Two data structures implement the obstacle cache:

```csharp
private bool[]           _obstacleCache;       // one bool per node — is there a collider here?
private HashSet<int>     _obstacleDirtyNodes;  // node indices that need re-checking
private bool             _obstacleCacheFullDirty; // true after graph resize
```

During `TickOverlay()`, for each node in the dirty region:

1. If `_obstacleCacheFullDirty` is false and the node index is not in `_obstacleDirtyNodes` — apply the cached result without calling `OverlapSphereNonAlloc`.
2. Otherwise — call `OverlapSphereNonAlloc`, update the cache, and write back if blocked.

Nodes already unwalkable from the Burst job (slope/height failure) are skipped entirely — no cache update, no physics check.

**Important:** Call `InvalidateObstacleCache()` after placing or destroying any static obstacle:

```csharp
// After placing a building:
TerrainGraphIntegration.Instance.InvalidateObstacleCache(buildingPos, buildingRadius);
```

This marks the affected node indices dirty, queues a partial rebuild, and ensures the next overlay pass re-checks those nodes.

---

## Heightmap cache (Option C)

`GetOrCacheHeightmap()` stores the result of `terrainData.GetHeights()` per chunk coordinate, avoiding repeated managed array allocation on partial rebuilds:

```csharp
if (!_heightmapDirty.Contains(chunkCoord) && _heightmapCache.TryGetValue(chunkCoord, out cached))
    return cached;
```

The cache is invalidated when:
- A chunk is first registered in `OnChunkFullyLoaded()` — `_heightmapDirty.Add(chunkCoord)`
- `StreamingTerrainGeneratorJobs.GenerateChunk()` modifies a chunk's terrain data — call `InvalidateHeightmapCache(chunkCoord)` manually
- `RebuildWithNewConfig()` or `Initialize()` is called — `_heightmapCache.Clear()`

If `GetHeights()` is called before `SetHeightsDelayLOD()` is flushed, it returns stale data. Add `terrainData.SyncHeightmap()` at the end of `GenerateChunk()` if this is observed.

---

## Public API

### Chunk and graph management

```csharp
// Must be called before Initialize() to wire up event listeners
Instance.InitializeListeners();

// Call from the onInitialChunksLoaded listener — registers all existing chunks
// and schedules the first full rebuild
Instance.Initialize();

// Change resolution at runtime (rebuilds graph)
Instance.RebuildWithNewConfig(new GraphConfig { NodesPerMetre = 0.25f, ... });

// Blocking full rebuild — use only for editor tools / loading screens
Instance.ForceFullRebuildSync();
```

### Runtime terrain deformation

```csharp
// After an explosion, digging, or terrain brush:
Instance.RequestDirtyRegionRebuild(explosionPos, blastRadius);
Instance.RequestDirtyRegionRebuild(worldMin, worldMax);
```

Multiple calls in the same frame are merged into one AABB before the next partial rebuild is scheduled.

### Obstacle invalidation

```csharp
// After placing or destroying a static obstacle:
Instance.InvalidateObstacleCache(buildingPos, radius);
Instance.InvalidateObstacleCache(worldMin, worldMax);

// After terrain generator modifies a chunk's heightmap:
Instance.InvalidateHeightmapCache(chunkCoord);
```

### Path requests (called by agents)

```csharp
PathResult result = Instance.RequestPath(from, to);
PathResult result = Instance.RequestPath(from, to, maxIterations: 200_000);
```

Returns `PathStatus.NoPath` immediately if the graph is null.

---

## Known pitfalls

**Graph built before terrain is generated** — `Initialize()` must be called from the `onInitialChunksLoaded` event, not from `Start()` or `Awake()`. If called too early, `GetHeights()` reads blank pooled `TerrainData` and the heightmap cache stores zeroed data permanently.

**`ChunkSize()` vs `terrainData.size`** — `GetChunkSize()` calls `_controller.ChunkSize()` which returns the `chunkSize` field from `TerrainStreamingController`. This must match the actual runtime chunk footprint. The template asset's `terrainData.size` is often a different value (e.g. 1000 in the asset but `chunkSize = 100` in the controller). These must be consistent or the Burst job's heightmap UV calculation will be wrong.

**Graph clamp at 8192** — If `_config.NodesPerMetre * worldSpan > 8192` the graph is silently truncated. Terrain in the truncated area will have all unwalkable nodes. Reduce `NodesPerMetre` or reduce `UnloadRadius` to stay within the budget.

**Overlay takes too long** — At default `_maxOverlayNodesPerFrame = 3000` and a graph of 36M nodes, the overlay takes ~200 seconds. Increase the budget to 50,000–100,000 nodes/frame, and/or reduce `NodesPerMetre`. The 2D iteration fix means only nodes inside the dirty region are actually physics-checked; the rest are skipped cheaply.
