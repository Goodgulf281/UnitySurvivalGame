# Terrain Pathfinding System

A custom A\* pathfinding system for Unity 6 that builds and maintains a navigation graph at runtime over procedurally generated streaming terrain. No NavMesh, no baking, no static geometry requirement.

---

## File overview

| File | Role |
|---|---|
| [`PathfindingGraph.md`](Documentation/PathfindingGraph.md) | Core data structures — `NodeData`, `GraphConfig`, `DirtyRegion`, and the double-buffered graph container |
| [`GraphRebuildJobs.md`](GraphRebuildJobs.md) | Burst-compiled jobs for full and partial graph rebuilds, plus the physics obstacle overlay |
| [`AStarSearch.md`](AStarSearch.md) | The A\* search algorithm, endpoint snapping, heuristic, and string pulling |
| [`TerrainGraphIntegration.md`](TerrainGraphIntegration.md) | MonoBehaviour hub — chunk registration, job scheduling, streamed overlay, obstacle cache |
| [`PathfindingAgent.md`](PathfindingAgent.md) | NPC agent — move-to, patrol, LOS, leader/follower flocking, terrain height snapping |

---

## Architecture

```
TerrainStreamingController
    │  onChunkLoadComplete
    │  onInitialChunksLoaded
    ▼
TerrainGraphIntegration ──────────────────────────────────► PathfindingAgent(s)
    │  owns                                  RequestPath()  │  follows path from
    │                                                       │  AStarSearch.FindPath()
    ▼                                                       │
PathfindingGraph                                            │
    │  NativeArray<NodeData> LiveBuffer  ◄───────────────── ┘
    │  NativeArray<NodeData> StagingBuffer
    │         ▲ written by
    ▼
GraphRebuildJobs (Burst, worker threads)
    FullGraphRebuildJob
    PartialGraphRebuildJob
    CopyBufferJob
    │
    │ after Complete()
    ▼
TickOverlay() — main thread, spread across frames
    OverlapSphereNonAlloc per node
    obstacle cache read/write
    │
    ▼
FinishOverlay() → PathfindingGraph.NotifyRebuildComplete() → buffer swap
```

---

## Core design decisions

### Why not NavMesh?

NavMesh requires static geometry to bake against. This world is procedurally generated at runtime and streams in and out as the player moves. A custom graph that builds itself on the fly is the only viable approach.

### 2.5D grid — one node per configurable world interval

The graph is a flat integer grid on the XZ plane. Each cell is `NodeSpacing` metres wide (`= 1 / NodesPerMetre`). Y is not a navigation dimension — it is data stored per node, sampled from the terrain heightmap. At the default of 0.5 nodes/m, cells are 2 × 2 metres. This keeps the graph small enough for real-time rebuilding while still providing accurate slope and obstacle detection for survival game movement.

### Double-buffered NativeArrays

The graph owns two `NativeArray<NodeData>` allocations with `Allocator.Persistent`:

- **Buffer A** (live) — read by A\* searches
- **Buffer B** (staging) — written by Burst rebuild jobs

The swap is a single C# tuple reference swap — no copying, no cost. It is deferred by a reference counter until no A\* searches are reading Buffer A. This means:

- Agents never read from a buffer being written to
- No search is cancelled mid-run when terrain changes
- The Burst job never needs to wait for an in-flight search

### Partial rebuilds — dirty regions

A `DirtyRegion` is an inclusive AABB in node-grid space. When terrain changes locally (explosion, player digging, new chunk loading), only the affected region is rebuilt:

1. `CopyBufferJob` blits live → staging (untouched nodes survive)
2. `PartialGraphRebuildJob` re-samples only nodes within the AABB
3. `TickOverlay` re-checks physics obstacles in the same AABB
4. Buffer swap

Multiple dirty regions queued in the same frame are merged into a single AABB before scheduling — at most one job is ever in flight.

### Streamed physics overlay

`Physics.OverlapSphereNonAlloc` cannot be called from Burst jobs. Rather than blocking the main thread for the entire pass, the overlay iterates node-grid rows across multiple frames at a configurable budget (`_maxOverlayNodesPerFrame`). The buffer swap is deferred until the overlay finishes.

A `bool[]` obstacle cache records which nodes were blocked by physics. On subsequent rebuilds, nodes not in `_obstacleDirtyNodes` skip the `OverlapSphereNonAlloc` call entirely. Explicit invalidation via `InvalidateObstacleCache()` is required when the player places or destroys obstacles.

### Initialisation timing

The graph must not be built until all initial terrain chunks have been fully generated. The correct sequence is:

1. `TerrainStreamingController` loads and generates all initial chunks
2. `onInitialChunksLoaded` fires
3. `TerrainGraphIntegration.Initialize()` is called from that event

Calling `Initialize()` before terrain generation writes blank zeroed heightmaps into the graph, and the heightmap cache permanently stores those zeros. This is the most common integration mistake.

### Seamless cross-chunk connectivity

The graph is a single unified array covering all loaded chunks and the borders between them. There are no per-chunk graphs and no seam stitching logic. The Burst job finds the correct chunk for each node position by testing each loaded chunk's AABB — whichever chunk contains the world position provides the heightmap sample.

---

## Setup

### Required packages

All ship with Unity 6:

- `com.unity.burst` (1.8+)
- `com.unity.collections` (2.x)
- `com.unity.mathematics`

### Scene setup

1. Locate the GameObject holding `TerrainStreamingController`.
2. Add `TerrainGraphIntegration` to the **same** GameObject.
3. Add `TerrainStreamingController.UnloadRadius()` and `ChunkSize()` accessor methods to `TerrainStreamingController` if not already present.
4. Configure `GraphConfig` in the Inspector. Start with defaults (`NodesPerMetre = 0.5`).
5. **Exclude the Terrain layer** from `ObstacleLayers`.
6. Wire the startup sequence — in whatever script coordinates initialisation:

```csharp
_controller.onInitialChunksLoaded.AddListener(() =>
{
    _graphIntegration.InitializeListeners();
    _graphIntegration.Initialize();
});
```

### Adding agents

1. Add `PathfindingAgent` to any NPC GameObject.
2. Set `StartingMode` in the Inspector.
3. For `MoveTo`: assign `MoveTarget`.
4. For `Patrol`: populate `PatrolPoints`.
5. For `FollowLeader`: assign `Leader` and set `IsLeader = false`. Set `StartingMode = FollowLeader`.
6. Set `TerrainLayerMask` to include your terrain physics layer (and nothing else).
7. Optionally assign `PlayerTransform` and set `LOSBehaviour` for reactive enemies.

---

## Runtime terrain deformation

After modifying terrain at runtime:

```csharp
// After an explosion or digging:
TerrainGraphIntegration.Instance.RequestDirtyRegionRebuild(worldPos, blastRadius);

// After placing or demolishing a building:
TerrainGraphIntegration.Instance.InvalidateObstacleCache(buildingPos, radius);

// After StreamingTerrainGeneratorJobs re-generates a chunk's heightmap:
TerrainGraphIntegration.Instance.InvalidateHeightmapCache(chunkCoord);
```

---

## Performance tuning

| Setting | Effect |
|---|---|
| `NodesPerMetre` | Most impactful. 0.25 = 16× fewer nodes than 1.0. For open-world survival games 0.25–0.5 is usually sufficient. |
| `_maxOverlayNodesPerFrame` | Higher values complete the overlay faster but cost more per frame. 50,000 is a good starting point for 60 fps. |
| `loadRadius` in `TerrainStreamingController` | Smaller radius = smaller graph. Reducing from 3 to 2 halves the node count. |
| `PathCooldownTime` in `PathfindingAgent` | Limits search frequency per agent. Increase for large agent counts. |

**Memory estimate (×2 for double buffer, 24 bytes per `NodeData`):**

| NodesPerMetre | Span (km) | Nodes | Memory |
|---|---|---|---|
| 0.5 | 7 km (loadR=3) | ~12.25 M | ~588 MB |
| 0.25 | 7 km (loadR=3) | ~3.06 M | ~147 MB |
| 0.25 | 5 km (loadR=2) | ~1.56 M | ~75 MB |

---

## Common bugs and solutions

| Symptom | Most likely cause | Fix |
|---|---|---|
| All nodes Y=0, all unwalkable | `Initialize()` called before terrain generation | Call from `onInitialChunksLoaded` |
| All nodes Y=0, all unwalkable | Heightmap cache storing stale zeros | `_heightmapDirty.Add(chunkCoord)` in `Initialize()` |
| Nodes at wrong world position | `ChunkSize()` returns prefab `terrainData.size` not runtime size | Implement `ChunkSize()` to return `chunkSize` field |
| Graph covers correct area but all unwalkable | Terrain layer included in `ObstacleLayers` | Remove terrain layer from the mask |
| Gizmos only at graph origin, all red | Graph sized too small, terrain outside bounds | Check `BuildOrResizeGraph` debug log; verify `UnloadRadius()` accessor |
| Agent oscillates between two waypoints | `HandleMoveTo` re-pathing while path is valid | Guard `RequestPath` with `pathExhausted && _pathCooldown <= 0f` |
| Agent shoots upward | `GetTerrainY()` falling through to `position.y` fallback | Set `TerrainLayerMask` to include terrain layer |
| Agent does not move (FollowLeader) | `StartingMode` not set to `FollowLeader` in Inspector | Set mode, assign `Leader`, set `IsLeader = false` |
| Overlay takes minutes | 2D iteration not used, iterating all 42M flat indices | Use `_overlayNodeX`/`_overlayNodeZ` 2D cursor in `TickOverlay()` |
