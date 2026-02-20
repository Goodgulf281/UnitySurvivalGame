# TerrainPositionObjects

**File:** `TerrainPositionObjects.cs`  
**Namespace:** `Goodgulf.TerrainUtils`  
**Base class:** `MonoBehaviour`

---

## Purpose

`TerrainPositionObjects` is a **utility component** that snaps a list of scene GameObjects down onto the terrain surface using a Physics raycast. It is intended to be called once after the initial terrain chunks have loaded — for example, from the `OnFirstChunksLoaded` UnityEvent on `TerrainStreamingController` — to correctly position the player character, spawn markers, or any other scene object that needs to sit on the procedurally generated terrain.

---

## Inspector Fields

| Field | Type | Description |
|---|---|---|
| `objectsToBePlacedOnTerrain` | `List<GameObject>` | Objects to reposition. Processed in order when `RePosition()` is called. |
| `layerMask` | `LayerMask` | Restricts the downward raycast to specific layers (typically the Terrain layer). |

---

## Methods

### `void Start()`
Empty in the current implementation (the `Invoke` call is commented out). Objects are not automatically repositioned at startup; `RePosition()` must be triggered externally.

### `void RePosition()`
Iterates over `objectsToBePlacedOnTerrain` and for each object:

1. Reads the object's current XZ position.
2. Moves the ray origin 5000 units above the object (`pos.y += 5000f`) to ensure the cast starts above any terrain height.
3. Fires `Physics.Raycast` straight down with `Mathf.Infinity` range, filtered by `layerMask`.
4. If a hit is found:
   - If the object has a `ThirdPersonController` component, calls `controller.TeleportCharacter(hit.point)` to move the character controller without causing a physics conflict.
   - Otherwise, sets `transform.position` directly to `hit.point`.
5. Logs a warning if no terrain is found beneath the object.

---

## Integration

Connect `RePosition()` to `TerrainStreamingController.OnFirstChunksLoaded` in the Inspector, or call it from any script after confirmed terrain load:

```csharp
terrainStreamingController.OnFirstChunksLoaded.AddListener(
    terrainPositionObjects.RePosition
);
```

---

## Notes

- The `ThirdPersonController.TeleportCharacter()` path handles character controllers that cannot simply have their `transform.position` set (doing so bypasses the `CharacterController` component and can cause jitter or physics errors on the next frame).
- If `layerMask` is not set, the raycast will hit all colliders including non-terrain objects (e.g., other characters). Always restrict this to the terrain layer.
- The ray starts at `+5000` in Y. If terrain `maxHeight × terrainData.size.y` exceeds 5000 world units, increase this offset accordingly.
