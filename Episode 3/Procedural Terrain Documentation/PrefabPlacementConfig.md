# PrefabPlacementConfig

**File:** `PrefabPlacementConfig.cs`  
**Namespace:** `Goodgulf.TerrainUtils`  
**Base class:** `ScriptableObject`

---

## Purpose

`PrefabPlacementConfig` is a **ScriptableObject** that acts as the data contract between the designer and `TerrainPrefabPlacer`. It holds a list of `PrefabPlacementRule` entries — one per type of object to scatter — along with global settings that apply to all rules. Create instances via **Assets → Create** in the Unity Project window and assign them to `TerrainPrefabPlacer.placementConfig`.

---

## `PrefabPlacementRule` (nested class)

Each rule defines how a single prefab type should be scattered across the terrain.

### Prefab Settings

| Field | Type | Description |
|---|---|---|
| `prefab` | `GameObject` | The prefab to instantiate. Rules with a null prefab are silently skipped. |

### Placement Density

| Field | Type | Description |
|---|---|---|
| `density` | `float` [0.001–10] | Number of placement *attempts* per square world unit. The actual placed count will be lower after constraint filtering. |

### Rotation Settings

| Field | Type | Description |
|---|---|---|
| `randomRotationX/Y/Z` | `bool` | Enables random 0–360° rotation on each respective axis. |
| `fixedRotation` | `Vector3` | Rotation (degrees) applied on any axis where random rotation is disabled. |

### Scale Settings

| Field | Type | Description |
|---|---|---|
| `randomScale` | `bool` | If true, applies a uniform random scale drawn from `[minScale, maxScale]`. |
| `minScale` | `float` [0.1–5] | Minimum uniform scale factor. |
| `maxScale` | `float` [0.1–5] | Maximum uniform scale factor. |

### Terrain Constraints

| Field | Type | Description |
|---|---|---|
| `minSlope` | `float` [0–90°] | Minimum terrain slope angle (from horizontal) required for placement. |
| `maxSlope` | `float` [0–90°] | Maximum terrain slope angle allowed. |
| `minHeight` | `float` | Minimum world Y position required. Defaults to `float.MinValue` (no lower bound). |
| `maxHeight` | `float` | Maximum world Y position allowed. Defaults to `float.MaxValue` (no upper bound). |

### Spacing

| Field | Type | Description |
|---|---|---|
| `minSpacing` | `float` | Minimum distance (world units) between any two instances of this prefab within the same chunk. |

### Alignment

| Field | Type | Description |
|---|---|---|
| `alignToTerrainNormal` | `bool` | If true, the prefab's up-axis is rotated to match the terrain surface normal. |
| `surfaceOffset` | `float` | Vertical offset (world units) from the terrain surface applied after normal alignment. |

### Terrain Modification

| Field | Type | Description |
|---|---|---|
| `modifyTerrain` | `bool` | Enables heightmap modification around this prefab's spawn point. |
| `terrainModificationRadius` | `float` [0.1–50] | Radius in world units within which the heightmap is modified. |
| `modificationType` | `TerrainModificationType` | How the terrain height is adjusted (see enum below). |
| `modificationSmoothness` | `float` [0–1] | Controls the softness of the transition at the radius edge. `0` = hard edge, `1` = very smooth blend. |
| `terrainHeightOffset` | `float` | Additional height offset used by `RaiseByOffset` and `FlattenToOffset` modification types. |

---

## `TerrainModificationType` (enum)

| Value | Description |
|---|---|
| `FlattenToSpawnPoint` | Blends terrain within radius to the height at which the prefab spawned. |
| `RaiseToSpawnPoint` | Only raises terrain up to the spawn height; never lowers it. |
| `LowerToSpawnPoint` | Only lowers terrain down to the spawn height; never raises it. |
| `RaiseByOffset` | Raises terrain within radius by `terrainHeightOffset`. |
| `FlattenToOffset` | Flattens within radius to `spawnHeight + terrainHeightOffset`. |

---

## Global Settings

| Field | Type | Description |
|---|---|---|
| `placementRules` | `List<PrefabPlacementRule>` | All rules to process for each chunk. |
| `seedOffset` | `int` | Added to the chunk/rule hash to shift placement patterns without changing the terrain seed. Useful to get a different-looking distribution for the same world seed. |
| `maxPlacementAttempts` | `int` | Hard cap on placement attempts per rule per chunk. Prevents very high density values from causing long frame hitches. |
| `edgeBufferDistance` | `float` [0–50] | Distance (metres) from chunk edges within which terrain modification is suppressed. Prevents visible seams between adjacent chunks. |
| `preventPlacementNearEdges` | `bool` | If `true`, prefabs that would modify terrain near a chunk edge are not placed at all. If `false`, they are placed but terrain modification is skipped. |

---

## Usage Tips

- **Trees / rocks** — low `density` (0.01–0.1), `minSpacing` 2–5 m, `maxSlope` 35°, `randomRotationY` true.
- **Buildings** — low `density` (0.001–0.01), `modifyTerrain` true with `FlattenToSpawnPoint`, `maxSlope` 5–10°, `preventPlacementNearEdges` true.
- **Alpine rocks** — `minSlope` 30°, `maxSlope` 90°, `minHeight` set to match mountain biome base height.
- Increase `seedOffset` between config assets to vary placement for different biome configs using the same world seed.
