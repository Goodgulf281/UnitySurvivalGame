# TerrainPrefabPlacer

**File:** `TerrainPrefabPlacer.cs`  
**Namespace:** `Goodgulf.TerrainUtils`  
**Base class:** `MonoBehaviour`

---

## Purpose

`TerrainPrefabPlacer` deterministically scatters prefabs (trees, rocks, buildings, etc.) across a terrain chunk. For each placement rule in a `PrefabPlacementConfig`, it generates a seeded `System.Random`, attempts a configurable number of placements within the chunk's bounds, and tests each candidate position against terrain constraints (slope, height range, spacing). Optionally it modifies the heightmap around placed prefabs to flatten or raise ground — useful for buildings or flat clearings.

---

## Inspector Fields

| Field | Type | Description |
|---|---|---|
| `placementConfig` | `PrefabPlacementConfig` | ScriptableObject containing all placement rules and global settings. |
| `terrain` | `Terrain` | The terrain to place objects on. Updated each chunk load via `SetTerrain()`. |
| `baseSeed` | `int` | Base seed for deterministic placement. Set at runtime to match the terrain generator's seed. |
| `spawnParent` | `Transform` | Parent transform for all spawned instances. Defaults to `this.transform` if left null. |

---

## Key Internal State

| Member | Type | Description |
|---|---|---|
| `placedObjectsPerChunk` | `Dictionary<Vector2Int, List<GameObject>>` | Tracks every instantiated object per chunk so they can be bulk-destroyed on unload. |

---

## Public Methods

### `void PlacePrefabsOnChunk(int chunkX, int chunkZ, float chunkSize)`
Entry point called by `TerrainStreamingController` after terrain generation. Iterates over every `PrefabPlacementRule` in `placementConfig` and calls the private `PlacePrefabsForRule()` for each.

### `void ClearChunk(Vector2Int chunkCoord)`
Destroys all `GameObject` instances registered for the given chunk coordinate and removes them from the tracking dictionary. Called during `UnloadChunk()`.

### `void ClearAllChunks()`
Destroys all tracked instances across every chunk.

### `void SetBaseSeed(int seed)`
Updates `baseSeed`. Called by `TerrainStreamingController.Awake()` to synchronize with the terrain generator seed.

### `void SetPlacementConfig(PrefabPlacementConfig config)`
Replaces the active `PrefabPlacementConfig` at runtime.

### `void SetTerrain(Terrain terrainRef)`
Updates the active terrain reference before each `PlacePrefabsOnChunk()` call.

---

## Placement Algorithm

### Seed Derivation — `GetDeterministicSeed()`

A per-rule, per-chunk seed is computed as:

```
seed = ((((baseSeed * 31 + chunkX) * 31 + chunkZ) * 31 + rule.prefab.GetInstanceID()) * 31 + seedOffset
```

Using `System.Random` (not `UnityEngine.Random`) ensures platform-independent results.

### Attempt Count

```
attempts = min(ceil(chunkSize² × rule.density), maxPlacementAttempts)
```

`density` is expressed in placements per square world unit.

### Per-Candidate Checks (in order)

1. **Terrain sample** — `SampleTerrain()` queries `terrain.terrainData` to get the height and surface normal at the candidate XZ position. Fails if outside terrain bounds.
2. **Terrain constraints** — `MeetsTerrainConstraints()` checks `minSlope`/`maxSlope` (from the surface normal's angle to `Vector3.up`) and `minHeight`/`maxHeight`.
3. **Spacing** — `MeetsSpacingConstraint()` ensures the candidate is at least `minSpacing` meters from every already-placed instance in this rule.
4. **Edge buffer** — If `rule.modifyTerrain` is true, `IsPositionNearChunkEdge()` tests whether the placement is within `terrainModificationRadius + edgeBufferDistance` of the chunk border. Depending on `preventPlacementNearEdges`, the candidate is either skipped entirely or placed without terrain modification.

### Instantiation

Passed candidates are instantiated with:
- **Rotation** from `CalculateRotation()`: per-axis random rotations or fixed values, optionally blended with the terrain normal when `alignToTerrainNormal` is true.
- **Scale** from `CalculateScale()`: uniform random scale in `[minScale, maxScale]` if `randomScale` is enabled, otherwise `Vector3.one`.
- **Y position** adjusted by `surfaceOffset`.

---

## Terrain Modification

When `rule.modifyTerrain` is true and the placement is not near a chunk edge, `ModifyTerrainAroundPrefab()` adjusts heightmap values within `terrainModificationRadius` of the prefab. The five available modes are defined in `PrefabPlacementConfig.TerrainModificationType`:

| Mode | Behaviour |
|---|---|
| `FlattenToSpawnPoint` | Blends all terrain within radius to the spawn height. |
| `RaiseToSpawnPoint` | Only raises terrain below spawn height; never lowers. |
| `LowerToSpawnPoint` | Only lowers terrain above spawn height; never raises. |
| `RaiseByOffset` | Raises terrain by `terrainHeightOffset`. |
| `FlattenToOffset` | Flattens to `spawnHeight + terrainHeightOffset`. |

Transitions are smoothed using a `smoothstep`-based blend factor controlled by `modificationSmoothness`. Modification is always skipped if the radius extends to within 1 heightmap texel of the terrain boundary.

---

## Notes

- Terrain modification writes directly to `TerrainData.SetHeights()`, which means it persists for the lifetime of that pooled `TerrainData` instance. Since `TerrainDataPool.Release()` clears the heightmap, modifications are correctly removed when a chunk is unloaded.
- The edge buffer prevents visible seams between chunks that would otherwise appear if terrain modification near a border altered heights on one side without a matching modification on the adjacent chunk.
- For dense placements with large `minSpacing`, the linear search in `MeetsSpacingConstraint()` can become slow. If needed, replace the `List<Vector3>` with a spatial grid or k-d tree.
