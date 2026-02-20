# TerrainDataPool

**File:** `TerrainDataPool.cs`  
**Namespace:** `Goodgulf.TerrainUtils`  
**Base class:** `MonoBehaviour`  
**Pattern:** Singleton + Object Pool

---

## Purpose

`TerrainDataPool` manages a reusable pool of `TerrainData` instances. Creating `TerrainData` objects at runtime via `Instantiate` is expensive; the pool pre-allocates a set of instances at startup and hands them out on demand, reclaiming and resetting them when chunks are unloaded. This avoids both runtime allocation cost and memory fragmentation during gameplay.

---

## Singleton

```csharp
public static TerrainDataPool Instance;
```

Set in `Awake()`. There should be exactly one `TerrainDataPool` in the scene. `TerrainStreamingController` accesses it via `TerrainDataPool.Instance`.

---

## Inspector Fields

| Field | Type | Description |
|---|---|---|
| `template` | `TerrainData` | The source `TerrainData` asset. All pooled instances are created as copies of this template (resolution, size, layers, etc. are inherited). |
| `prewarmCount` | `int` | Number of `TerrainData` instances created at startup and added to the pool before any chunk is loaded. Should be at least `(2 * viewDistance + 1)²`. |

---

## Methods

### `void Awake()`
Sets `Instance` and calls `Prewarm()`.

### `void Prewarm()` *(private)*
Creates `prewarmCount` instances via `CreateTerrainData()` and enqueues them into the pool.

### `TerrainData CreateTerrainData()` *(private)*
Calls `Instantiate(template)`, sets a consistent name, and explicitly copies `template.terrainLayers` to the new instance. The explicit layer copy is necessary because `Instantiate` may share the layer reference rather than duplicating it, which would cause visual artifacts if layers are modified per-chunk.

### `TerrainData Get()`
Returns the next available instance from the pool. If the pool is empty (all instances are currently in use), creates and returns a new one on demand. The caller is responsible for releasing it back when done.

### `void Release(TerrainData data)`
Clears the heightmap of the returned instance by calling `SetHeights(0, 0, new float[res, res])`, then re-enqueues it. **Clearing on release** rather than on acquisition means a brief moment of zeroed terrain is visible if a chunk is loaded before the heights are regenerated — which is acceptable because generation happens immediately after `Get()`.

---

## Usage Pattern

```csharp
// Acquire
TerrainData pooledData = TerrainDataPool.Instance.Get();
terrain.terrainData = pooledData;

// ... generate heights, place prefabs ...

// Release (on chunk unload)
TerrainDataPool.Instance.Release(terrain.terrainData);
```

---

## Notes

- **Pool sizing:** Set `prewarmCount` to at least the maximum number of simultaneously loaded chunks to avoid runtime allocations. For `viewDistance = 2`, this is 25.
- **Thread safety:** `Get()` and `Release()` are called from the main thread only (inside `TerrainStreamingController`). No locking is required.
- The unused private static helper `ClearHeights(int res)` simply returns `new float[res, res]` and may be removed without impact.
