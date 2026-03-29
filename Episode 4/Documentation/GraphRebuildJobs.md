# GraphRebuildJobs.cs

**Namespace:** `Goodgulf.Pathfinding`

Contains all Burst-compiled jobs that populate the graph's staging buffer with fresh `NodeData`, plus the main-thread physics obstacle overlay that runs after the Burst jobs complete.

---

## Why three separate pieces?

| Piece | Thread | When used |
|---|---|---|
| `CopyBufferJob` | Worker (Burst) | Stage 1 of every partial rebuild |
| `FullGraphRebuildJob` | Workers (Burst, parallel) | Initial graph build; major chunk changes; resolution change |
| `PartialGraphRebuildJob` | Workers (Burst, parallel) | New chunk loaded; runtime terrain deformation |
| `PhysicsObstacleOverlay` | Main thread | After every Burst job, streamed across frames by `TickOverlay()` |

`Physics.OverlapSphereNonAlloc` is a Unity engine API that can only be called from the main thread. Everything else — heightmap sampling, normal derivation, slope and height checks — is pure maths and runs in Burst. Keeping them separate means the expensive physics work never blocks the Burst parallelism.

---

## `TerrainHeightSample` (struct)

Blittable snapshot of one chunk's heightmap metadata. Built on the main thread from `Terrain.terrainData` in `BuildHeightArrays()`, then passed to jobs as `NativeArray<TerrainHeightSample>`.

| Field | Description |
|---|---|
| `ChunkOriginXZ` | World-space XZ position of the chunk's bottom-left corner (= `terrain.transform.position.xz`) |
| `ChunkSizeXZ` | World-space XZ dimensions (= `terrainData.size.xz`, must match `ChunkSize()` in `TerrainStreamingController`) |
| `TerrainHeight` | `terrainData.size.y` — the Y scale applied to normalised [0..1] heightmap values |
| `HeightmapRes` | Heightmap resolution. The heightmap is square: `HeightmapRes × HeightmapRes` samples |
| `ChunkIndex` | Offset into the flat `Heights` NativeArray. `Heights[ChunkIndex + z * res + x]` gives the normalised height at texel (x, z) |

The flat heights array packs all loaded chunks end-to-end. `TerrainGraphIntegration.BuildHeightArrays()` fills it and creates the `NativeArray<float>` before scheduling any job.

---

## `CopyBufferJob` (IJob, Burst)

A single-threaded bulk copy of the live buffer into staging. No logic — just a flat loop.

```
Live buffer (Buffer A) ──────────────────────> Staging buffer (Buffer B)
```

**Why this is needed:** `PartialGraphRebuildJob` only rewrites nodes inside the dirty region. The remaining nodes must carry their previous values. Rather than having each node check whether it is inside the dirty region before deciding to copy, a single prior blit is simpler and Burst-optimised.

The two jobs are chained via `JobHandle`:

```csharp
JobHandle copyHandle  = copyJob.Schedule();
JobHandle patchHandle = patchJob.Schedule(TotalNodes, 64, copyHandle);
```

`FullGraphRebuildJob` does not need a copy step — it writes every node unconditionally.

---

## `FullGraphRebuildJob` (IJobParallelFor, Burst)

Rewrites every node in the graph. Scheduled with `innerBatchCount = 64`.

### Per-node pipeline

For each flat array index the job:

1. Converts the index to node-grid XZ: `nodeX = index % Width + Origin.x`
2. Converts node-grid to world space: `worldX = nodeX / NodesPerMetre`
3. Iterates loaded chunks to find one whose AABB contains `(worldX, worldZ)`
4. Computes heightmap UV within that chunk: `u = relX / ChunkSizeXZ.x`
5. Bilinear-samples four adjacent heightmap texels (h00, h10, h01, h11)
6. Multiplies the normalised result by `TerrainHeight` to get `worldY`
7. Approximates the surface normal from partial derivatives:

```
dh/dx = (h10 - h00) * TerrainHeight / cellSizeX
dh/dz = (h01 - h00) * TerrainHeight / cellSizeZ
normal = normalize(float3(-dh/dx, 1, -dh/dz))
```

8. Evaluates walkability: `worldY ∈ [Min, Max]` AND `slopeAngle ≤ MaxSlopeAngle`
9. Writes the resulting `NodeData` to `Output[index]`

Nodes outside all loaded chunks are written as `Walkable = false`, `WorldY = 0`.

### Resolution note

At `NodesPerMetre = 0.5`, node-grid coord -1500 maps to world position `-1500 * 2.0 = -3000 m`. The conversion must use `nodeSpacing = 1f / NodesPerMetre` rather than directly multiplying by `NodesPerMetre` (which would give the wrong direction).

---

## `PartialGraphRebuildJob` (IJobParallelFor, Burst)

Identical pipeline to `FullGraphRebuildJob` with one additional guard at the top of `Execute()`:

```csharp
if (nodeX < DirtyMin.x || nodeX > DirtyMax.x ||
    nodeZ < DirtyMin.y || nodeZ > DirtyMax.y)
    return;
```

The job still iterates all `TotalNodes` indices because `IJobParallelFor` requires a fixed count. The early return for out-of-region nodes is essentially free — one comparison and a return with no memory write.

`DirtyMin` and `DirtyMax` are in **node-grid space**, not world metres. They are produced by `DirtyRegion.FromChunk()` or `DirtyRegion.FromWorldAABB()`, both of which apply `NodesPerMetre` during construction.

---

## `PhysicsObstacleOverlay` (static class, main thread)

Retained for use by `ForceFullRebuildSync()` only. The normal runtime path uses the streamed `TickOverlay()` in `TerrainGraphIntegration` instead, which spreads the same work across multiple frames.

`Apply()` iterates the staging buffer over the clamped dirty region, calls `Physics.OverlapSphereNonAlloc` at each walkable node, and marks blocked nodes unwalkable.

```csharp
PhysicsObstacleOverlay.Apply(
    graph.StagingBuffer,
    graph,
    region,
    config.ObstacleCheckRadius,
    config.ObstacleLayers);
```

The sphere is centred at `(worldX, worldY + overlapRadius, worldZ)` — just above the terrain surface. Nodes already marked unwalkable by the Burst job (slope or height failure) are skipped; only walkable nodes need the physics check.

---

## Job dependency chains

### Full rebuild

```
FullGraphRebuildJob.Schedule(TotalNodes, 64)
    → _currentJobHandle

[Next frame, IsCompleted == true]
    → JobHandle.Complete()
    → TerrainGraphIntegration.TickOverlay() spreads overlay across N frames
    → TerrainGraphIntegration.FinishOverlay() → Graph.NotifyRebuildComplete() → buffer swap
```

### Partial rebuild

```
CopyBufferJob.Schedule()
    → copyHandle

PartialGraphRebuildJob.Schedule(TotalNodes, 64, copyHandle)
    → _currentJobHandle

[Next frame, IsCompleted == true]
    → JobHandle.Complete()
    → TickOverlay() over dirty region only
    → FinishOverlay() → buffer swap
```

### NativeArray lifetime

`_jobHeights` and `_jobChunks` are allocated with `Allocator.TempJob` in `BuildHeightArrays()` immediately before scheduling. They must not be disposed before `JobHandle.Complete()` is called. `DisposeJobNativeArrays()` is called inside `FinaliseJob()` after `Complete()`, and also defensively inside `CompleteCurrentJob(forceComplete: true)`.

---

## Common failure modes

**All nodes unwalkable, WorldY = 0** — The Burst job is not finding any matching chunk for any node. Check that `ChunkSizeXZ` matches the runtime chunk footprint (not the template asset's `terrainData.size`), and that the graph origin in world space actually overlaps the loaded terrain area.

**Nodes correct but then replaced with zeros** — A second full rebuild was scheduled while the first was still in flight, or `BuildOrResizeGraph()` was called which disposes the graph and allocates fresh zeroed buffers. Check `_debugEnabled` logs for unexpected `ScheduleFullRebuild` calls.

**Partial rebuild leaves stale data at region edges** — The dirty region was not expanded by `Expanded(2)` before enqueuing. The 2-node expansion ensures cross-chunk border nodes whose neighbours changed are also refreshed.
