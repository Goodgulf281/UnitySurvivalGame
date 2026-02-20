# Streaming Terrain Generation Solution — Overview

**Namespace:** `Goodgulf.TerrainUtils`

## Summary

This solution implements a **chunk-based, streaming terrain system** for Unity. As a player moves through the world, terrain chunks are loaded around them and unloaded when they move out of range. Each chunk is procedurally generated using a multi-biome noise pipeline executed via Unity's **Jobs System** and **Burst Compiler** for high performance. After generation, prefabs (trees, rocks, buildings, etc.) are deterministically placed on each chunk according to configurable rules.

---

## Architecture Overview

```
TerrainStreamingController
    │
    ├── Monitors player position each Update()
    ├── Loads / Unloads Terrain chunks as needed
    │
    ├──► TerrainDataPool          (provides/recycles TerrainData assets)
    ├──► StreamingTerrainGeneratorJobs  (generates heightmap per chunk via Jobs)
    │        └──► TerrainHeightJob  (Burst-compiled IJobParallelFor)
    └──► TerrainPrefabPlacer      (places prefabs on each generated chunk)
             └──► PrefabPlacementConfig  (ScriptableObject — rules & settings)

TerrainPositionObjects            (utility: snaps objects to terrain after load)
```

---

## Key Classes

### `TerrainStreamingController` *(central orchestrator)*
The heart of the system. Each `Update()` it computes which chunks should be visible given the player's position and a `viewDistance` parameter. It calls `LoadChunk()` for any new chunks and `UnloadChunk()` for any that have gone out of range. It delegates terrain generation to `StreamingTerrainGeneratorJobs` and prefab placement to `TerrainPrefabPlacer`. It also fires a `UnityEvent` (`OnFirstChunksLoaded`) once the initial set of chunks is ready, which can be used to reposition the player or trigger game-start logic.

### `StreamingTerrainGeneratorJobs` *(terrain height generation)*
Generates the heightmap for a single chunk using a Unity **Job** scheduled on worker threads. The job (`TerrainHeightJob`) is Burst-compiled for near-native performance. The height pipeline consists of:
1. **Biome blending** — smooth interpolation between configurable biome definitions using FBM noise.
2. **Mountain ridges** — ridged noise layered onto high-biome areas.
3. **Erosion** — gradient-based erosion that carves slopes.
4. **Rivers** — smooth-stepped channels cut into low-lying areas.

---

## Data Flow: Loading a Chunk

1. `TerrainStreamingController.LoadChunk()` is called with a `Vector2Int` chunk coordinate.
2. A `Terrain` GameObject is instantiated at the correct world position.
3. A `TerrainData` object is retrieved from **`TerrainDataPool`** (avoiding expensive `ScriptableObject` creation at runtime).
4. **`StreamingTerrainGeneratorJobs.GenerateChunk()`** schedules a `TerrainHeightJob`, waits for it to complete, and writes the heights back to `TerrainData`.
5. **`TerrainPrefabPlacer.PlacePrefabsOnChunk()`** iterates over all rules in `PrefabPlacementConfig` and deterministically scatters prefabs using a seeded `System.Random`.
6. The `TerrainCollider` is refreshed.

## Data Flow: Unloading a Chunk

1. `TerrainStreamingController.UnloadChunk()` detaches `TerrainData` from the terrain object.
2. The `TerrainData` is returned to **`TerrainDataPool`** (cleared for reuse).
3. The terrain `GameObject` is destroyed.
4. `TerrainPrefabPlacer.ClearChunk()` destroys all prefab instances that belonged to that chunk.

---

## Determinism

A key design goal is **deterministic generation**: the same chunk always looks identical regardless of load order or platform. This is achieved by:
- Using a fixed integer `seed` in `StreamingTerrainGeneratorJobs`.
- Deriving per-chunk, per-rule seeds in `TerrainPrefabPlacer` via a hash of `(baseSeed, chunkX, chunkZ, ruleInstanceID, seedOffset)`.
- Using `System.Random` (not `UnityEngine.Random`) for placement so results are platform-independent.

---

## Performance Considerations

| Concern | Solution |
|---|---|
| Heightmap generation cost | Burst-compiled `IJobParallelFor` on worker threads |
| TerrainData allocation | Pooled via `TerrainDataPool` |
| Collider updates | Only enabled for the 3×3 chunks nearest the player |
| Prefab instance tracking | Per-chunk `Dictionary<Vector2Int, List<GameObject>>` for O(1) bulk cleanup |

---

## Component Setup (Inspector)

Add to a single GameObject in the scene:

- `TerrainStreamingController` — assign `player`, `terrainPrefab`, `viewDistance`, `terrainParent`.
- `StreamingTerrainGeneratorJobs` — configure `seed`, biome definitions, mountain/erosion/river parameters.
- `TerrainPrefabPlacer` *(optional)* — assign a `PrefabPlacementConfig` ScriptableObject.
- `TerrainDataPool` — assign a `TerrainData` template; set `prewarmCount`.

Separately, a `TerrainPositionObjects` component can be placed on any GameObject to reposition scene objects (e.g. the player) onto terrain surfaces after initial load.

---

## Files in This Solution

| File | Purpose |
|---|---|
| `TerrainStreamingController.cs` | Chunk load/unload orchestration, player tracking |
| `StreamingTerrainGeneratorJobs.cs` | Schedules Jobs-based heightmap generation per chunk |
| `TerrainHeightJob.cs` | Burst-compiled parallel job — full height pipeline |
| `TerrainDataPool.cs` | Object pool for `TerrainData` instances |
| `TerrainPrefabPlacer.cs` | Deterministic prefab scattering on chunks |
| `PrefabPlacementConfig.cs` | ScriptableObject — placement rules and constraints |
| `TerrainPositionObjects.cs` | Utility — raycast-snaps objects to terrain surface |
