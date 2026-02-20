# TerrainStreamingController

**File:** `TerrainStreamingController.cs`  
**Namespace:** `Goodgulf.TerrainUtils`  
**Base class:** `MonoBehaviour`

---

## Purpose

`TerrainStreamingController` is the **central orchestrator** of the streaming terrain system. Every frame it calculates which terrain chunks should be visible based on the player's world position and a configurable view distance. It loads new chunks that have come into range and unloads chunks that have moved out of range, recycling their `TerrainData` through the pool. It also fires an event once the initial set of chunks is ready, so dependent systems (e.g., player spawning) can safely start.

---

## Inspector Fields

| Field | Type | Description |
|---|---|---|
| `player` | `Transform` | The player transform used to calculate the current chunk. |
| `terrainPrefab` | `Terrain` | Template prefab instantiated for each chunk. |
| `viewDistance` | `int` | Radius (in chunks) of the loaded area around the player. A value of `2` keeps a 5×5 grid of chunks loaded. |
| `terrainParent` | `GameObject` | Optional parent for all instantiated terrain GameObjects. |
| `OnFirstChunksLoaded` | `UnityEvent` | Fired once after the player's initial chunk loads and a collider raycast confirms terrain is present beneath the spawn point. |

---

## Key Private State

| Member | Type | Description |
|---|---|---|
| `loadedChunks` | `Dictionary<Vector2Int, Terrain>` | Maps chunk grid coordinates to their live `Terrain` instances. |
| `generator` | `StreamingTerrainGeneratorJobs` | Reference obtained from the same GameObject in `Awake()`. |
| `prefabPlacer` | `TerrainPrefabPlacer` | Optional reference obtained in `Awake()`; if present, it places prefabs after generation. |
| `_firstChunksLoaded` | `bool` | Guards the one-time `OnFirstChunksLoaded` event. |

---

## Methods

### `void Awake()`
Retrieves `StreamingTerrainGeneratorJobs` and `TerrainPrefabPlacer` from the same GameObject. If a `TerrainPrefabPlacer` is found, it passes the generator's `seed` to it so placement is synchronized with terrain generation.

### `void Update()`
Core streaming loop — called every frame:
1. Computes `playerChunk` (the chunk coordinate the player currently occupies).
2. Builds a `HashSet<Vector2Int>` of all chunks that *should* be loaded within `viewDistance`.
3. Calls `LoadChunk()` for any coords not yet in `loadedChunks`.
4. Calls `UnloadChunk()` for any loaded coords not in the needed set.
5. Calls `UpdateTerrainColliders()` to enable/disable colliders by proximity.
6. On the first frame, starts the `SpawnObjectsWhenReady` coroutine.

### `void LoadChunk(Vector2Int coord)`
Instantiates a terrain `GameObject` at the correct world position. Retrieves a `TerrainData` from `TerrainDataPool`, assigns it to both the `Terrain` and `TerrainCollider`, then calls `GenerateChunk()` followed by `PlacePrefabsOnChunk()`.

### `void UnloadChunk(Vector2Int coord)`
Detaches `TerrainData` from the terrain object and its collider, returns it to the pool via `TerrainDataPool.Instance.Release()`, destroys the `GameObject`, and calls `prefabPlacer.ClearChunk()` to destroy associated prefab instances.

### `IEnumerator SpawnObjectsWhenReady()`
A coroutine that waits until the player's spawn chunk is loaded and its `TerrainCollider` is enabled, then waits one `FixedUpdate` tick for physics to stabilize. It performs a downward raycast to confirm the terrain surface exists, then invokes `OnFirstChunksLoaded`.

### `void UpdateTerrainColliders(Vector2Int playerChunk)`
Enables `TerrainCollider` only on chunks within 1 chunk of the player (a 3×3 area) and disables them on all other loaded chunks. This reduces physics overhead for distant terrain.

### `Vector2Int GetPlayerChunk()`
Divides the player's world position by the terrain chunk size and floors to an integer grid coordinate.

### `Vector2Int WorldToChunk(Vector3 worldPos)`
Same coordinate conversion used internally by the spawn coroutine.

---

## Event: `OnFirstChunksLoaded`

This `UnityEvent` is invoked once the player's initial terrain chunk is confirmed to be physically present (via raycast). It is a suitable hook for:
- Teleporting / repositioning the player character.
- Enabling the player controller.
- Triggering a fade-in or loading screen dismissal.

Connect listeners in the Inspector or via `OnFirstChunksLoaded.AddListener(...)` in code.

---

## Notes

- `viewDistance = 2` results in `(2*2+1)² = 25` chunks loaded simultaneously. Increasing this value significantly raises memory and generation cost.
- The controller runs entirely on the **main thread**; generation is offloaded to worker threads by `StreamingTerrainGeneratorJobs`, but `LoadChunk()` itself is synchronous and will stall for the duration of job completion. Consider wrapping in a coroutine or async pattern for very large heightmaps.
- Chunk coordinates can be **negative**, allowing the world to extend in all directions from the origin.
