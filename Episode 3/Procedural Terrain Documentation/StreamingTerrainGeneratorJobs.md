# StreamingTerrainGeneratorJobs

**File:** `StreamingTerrainGeneratorJobs.cs`  
**Namespace:** `Goodgulf.TerrainUtils`  
**Base class:** `MonoBehaviour`

---

## Purpose

`StreamingTerrainGeneratorJobs` generates the **heightmap** for a single terrain chunk. It configures and schedules a Burst-compiled `TerrainHeightJob` on Unity's worker threads, waits for completion, then writes the resulting heights back to the chunk's `TerrainData`. It is designed to be called synchronously from `TerrainStreamingController.LoadChunk()`.

See also: [`TerrainHeightJob.md`](TerrainHeightJob.md) for the full noise pipeline that runs inside the job.

---

## Inspector Fields

### Seed

| Field | Type | Description |
|---|---|---|
| `seed` | `int` | Master random seed. Shared with `TerrainPrefabPlacer` to keep terrain and prefab placement in sync. |

### Biome

| Field | Type | Description |
|---|---|---|
| `biomeScale` | `float` | Spatial frequency of the biome selection noise. Smaller values produce larger biome regions. Default: `0.0002`. |
| `biomeCount` | `int` | Number of distinct biomes. Must match the length of `biomeDefinitions`. |
| `biomeDefinitions` | `Biome[]` | Array of `Biome` structs defining each biome's height characteristics (see below). |

### Mountains

| Field | Type | Description |
|---|---|---|
| `mountainRidgeScale` | `float` | Frequency of the ridged noise used for mountain peaks. Default: `0.0006`. |
| `mountainRidgeStrength` | `float` | Maximum height contribution from ridged noise, blended in on high-biome areas. Default: `0.25`. |

### Erosion

| Field | Type | Description |
|---|---|---|
| `erosionScale` | `float` | Frequency of the gradient noise used to simulate erosion. Default: `0.0012`. |
| `erosionStrength` | `float` | Multiplier on the computed slope; higher values erode more aggressively. Default: `2.0`. |
| `erosionDepth` | `float` | Maximum height reduction caused by erosion. Default: `0.08`. |

### Rivers

| Field | Type | Description |
|---|---|---|
| `riverScale` | `float` | Frequency of the river path noise. Default: `0.0009`. |
| `riverWidth` | `float` | Threshold below which a river channel is carved (passed to `smoothstep`). Default: `0.03`. |
| `riverDepth` | `float` | Maximum height reduction in river channels. Default: `0.12`. |

---

## The `Biome` Class

```csharp
[System.Serializable]
public class Biome
{
    public string name;
    public float baseHeight;   // Constant height offset
    public float amplitude;    // Vertical scale of FBM noise
    public float noiseScale;   // Horizontal detail frequency
    public int octaves;        // FBM layers (default 5)
    public float lacunarity;   // Frequency growth per octave (default 2.0)
    public float gain;         // Amplitude decay per octave (default 0.5)
}
```

Biomes are blended smoothly by the job based on a low-frequency noise value mapped across the `biomeDefinitions` array. Biome index `0` appears in areas where the biome noise is lowest; the last index appears where it is highest.

---

## Methods

### `void GenerateChunk(Terrain terrain, Vector2Int chunkCoord)`

The single public entry point. Steps:

1. Reads `heightmapResolution` and `size` from `terrain.terrainData`.
2. Allocates a `NativeArray<float>` of size `res × res` with `Allocator.TempJob`.
3. Copies `biomeDefinitions` into a parallel `NativeArray<BiomeParams>`.
4. Constructs and schedules a `TerrainHeightJob` with a batch size of `64`.
5. Calls `handle.Complete()` — blocks the main thread until the job finishes.
6. Calls `ApplyHeights()` to write the flat array back as a 2D heightmap.
7. Disposes both `NativeArray` allocations.
8. Refreshes the `TerrainCollider.terrainData` reference.

### `void ApplyHeights(TerrainData data, NativeArray<float> flatHeights, int res)` *(private)*

Converts the flat job output array into the `float[res, res]` format required by Unity's `TerrainData.SetHeightsDelayLOD()`. Using `SetHeightsDelayLOD` instead of `SetHeights` defers the LOD update, which is more efficient when many chunks may be updating in quick succession.

---

## World-Space Coordinate Calculation

The job needs world-space coordinates so that noise is continuous across chunk boundaries. The origin passed to the job is:

```
worldOrigin = new Vector2(chunkCoord.x * size.x, chunkCoord.y * size.z)
```

Each texel's world position is then:

```
worldX = worldOrigin.x + (x / (res - 1)) * size.x
worldZ = worldOrigin.y + (y / (res - 1)) * size.z
```

This ensures identical noise values at shared edges between adjacent chunks, producing seamless terrain.

---

## Notes

- `GenerateChunk()` is **synchronous** — the main thread is blocked during `handle.Complete()`. For heightmaps larger than ~513×513 on lower-end hardware, consider splitting the call across multiple frames or using an async job approach.
- The `seed` field is not currently injected into the `TerrainHeightJob` directly; the noise functions are purely positional. The `seed` is used by `TerrainPrefabPlacer` for placement determinism.
- Burst compilation is set to `FloatPrecision.Standard, FloatMode.Strict`. Switching to `FloatMode.Fast` may improve performance at the cost of minor numerical differences across platforms.
