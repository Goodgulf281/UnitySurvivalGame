# EP02 — Build System Documentation

> **Episode:** EP02 — Build System  
> **Namespace:** `Goodgulf.Builder`  
> **Series:** How to Create a Survival Game in Unity 3D

---

## Table of Contents

1. [Overview](#overview)
2. [Architecture Diagram](#architecture-diagram)
3. [Scripts](#scripts)
   - [BuildTemplate.cs](#buildtemplatecs)
   - [BuildingArea.cs](#buildingaraecs)
   - [ConstructedItemBehaviour.cs](#constructeditembehaviourcs)
   - [MainThreadDispatcher.cs](#mainthreaddispatchercs)
   - [RecalculationWorker.cs](#recalculationworkercs)
4. [Key Concepts](#key-concepts)
5. [Data Flow](#data-flow)
6. [Known Limitations & Future Improvements](#known-limitations--future-improvements)

---

## Overview

This episode implements the **core build system** for the survival game. It covers the placement of structured building pieces in the world, how those pieces are grouped into **Building Areas**, and how **structural integrity** is calculated using a graph algorithm running on a background thread.

The system is inspired by building mechanics found in games like **Valheim** and **Enshrouded**, where pieces must be connected to the ground (or to other grounded pieces) within a maximum distance to remain standing.

**Scripts introduced in this episode:**

| Script | Role |
|---|---|
| `BuildTemplate.cs` | ScriptableObject defining a placeable building piece |
| `BuildingArea.cs` | Groups constructed items; handles lifecycle and integrity |
| `ConstructedItemBehaviour.cs` | MonoBehaviour controlling visuals and destruction |
| `MainThreadDispatcher.cs` | Routes background thread actions to Unity's main thread |
| `RecalculationWorker.cs` | Periodically recalculates structural integrity off the main thread |

---

## Architecture Diagram

```
BuildTemplate (ScriptableObject)
        │
        │  defines
        ▼
  ConstructedItem  ◄──────────────────────────────┐
  (data model)                                     │
        │ linked to                                │
        ▼                                          │
ConstructedItemBehaviour                     BuildingArea
  (MonoBehaviour)                            (manages list of
  - Mesh visuals                              ConstructedItems)
  - Color feedback                                 │
  - Destruction logic                             │ processed by
                                                  ▼
                                        RecalculationWorker
                                        (background thread)
                                                  │
                                                  │ dispatches results via
                                                  ▼
                                        MainThreadDispatcher
```

---

## Scripts

---

### BuildTemplate.cs

**Type:** `ScriptableObject`  
**Menu Path:** `Assets → Create → Builder → Build Template`

A data container defining everything needed to place a building piece. One `BuildTemplate` asset is created per building type (e.g., wooden wall, stone floor, door frame).

#### Serialized Fields

| Field | Type | Description |
|---|---|---|
| `id` | `int` | Unique identifier for this template |
| `buildYOffset` | `float` | Vertical offset applied at placement to fix pivot/ground alignment |
| `radius` | `float` | Radius used for proximity and collision checks during placement |
| `strength` | `float` | Structural strength, used in the Dijkstra distance calculation |
| `width` | `float` | Physical width of the piece |
| `height` | `float` | Physical height of the piece |
| `depth` | `float` | Physical depth of the piece |
| `snapPoints` | `List<Vector3>` | Local-space snap points for aligning to adjacent pieces |
| `buildPrefab` | `GameObject` | The real, placed prefab |
| `transparentPrefab` | `GameObject` | Preview/ghost prefab shown during placement |

#### Usage Example

```csharp
// Accessed at placement time by the Builder system
float strength = buildTemplate.strength;
GameObject preview = Instantiate(buildTemplate.transparentPrefab);
```

---

### BuildingArea.cs

**Type:** Plain C# classes (serializable)  
**Contains:** `ConstructedItem` and `BuildingArea`

This file defines two tightly coupled classes that together represent the **data layer** of the build system.

---

#### `ConstructedItem`

A serializable data model representing one placed building piece in the world.

##### Fields

| Field | Type | Description |
|---|---|---|
| `Id` | `int` | Globally unique ID assigned at placement |
| `Grounded` | `bool` | Whether this piece is directly touching terrain or a foundation |
| `Distance` | `float` | Shortest path distance to the nearest grounded node (Dijkstra result) |
| `BuildTemplateId` | `int` | ID of the `BuildTemplate` used |
| `BuildTemplateName` | `string` | Name of the template (for debug/display) |
| `Strength` | `float` | Copied from `BuildTemplate.strength`; used as edge weight |
| `Position` | `Vector3` | World-space position at placement |
| `Rotation` | `Quaternion` | World-space rotation at placement |
| `SnapPointIndex` | `int` | Which snap point was used during placement |
| `Neighbours` | `List<int>` | IDs of all adjacent `ConstructedItem`s (graph edges) |
| `ConstructedItemBehaviour` | reference | Runtime MonoBehaviour linked to the instantiated GameObject |
| `BuildingAreaId` | `string` | ID of the `BuildingArea` this item belongs to |

##### Key Methods

```csharp
bool AddNeighbour(int id)
```
Registers a neighbour connection by ID. Returns `false` and logs a warning if the connection already exists.

```csharp
void RemoveSelfFromNeighbours()
```
When this item is destroyed, iterates through its neighbour list and removes itself from each neighbour's `Neighbours` list. Falls back to a global search if a neighbour belongs to a different `BuildingArea`.

```csharp
List<Edge> GetAllNeighbourEdges()
```
Returns a list of `Edge` objects (ID + strength) for all connected neighbours. Used to build the Dijkstra graph in `RecalculationWorker`.

---

#### `BuildingArea`

Groups all `ConstructedItem`s placed within a logical zone. Handles item registration, integrity checks, and removal of structurally unsupported pieces.

##### Fields

| Field | Type | Description |
|---|---|---|
| `Id` | `string` | Unique identifier for this area |
| `Position` | `Vector3` | World-space center of the area |
| `Range` | `float` | Effective radius |
| `GridPosition` | `Vector2Int` | Grid coordinates for spatial partitioning |
| `Size` | `int` | Grid size in units |

##### Key Methods

```csharp
ConstructedItem AddBuildItem(BuildTemplate buildTemplate, GameObject instantiatedBuildItem, bool grounded, bool debugEnabled)
```
Creates a new `ConstructedItem`, assigns it a global ID, links it to its `ConstructedItemBehaviour`, and registers it in the area's list.

```csharp
ConstructedItem GetConstructedItem(int id)
```
Finds and returns an item in this area by ID.

```csharp
void RemoveWeakConstructedItems()
```
Iterates all items and destroys those with `Distance == PositiveInfinity` (not reachable from any grounded node). Triggers color feedback (blue flash) before destruction.

```csharp
void RemoveWeakConstructedItems(List<ConstructedItem> subset)
```
Overload that only checks a provided subset of items — useful after partial rebuilds.

```csharp
void ShowDistances()
```
Calls `ShowDistance()` on each item's `ConstructedItemBehaviour`, useful for debugging integrity state visually.

---

### ConstructedItemBehaviour.cs

**Type:** `MonoBehaviour`  
**Attached to:** Every instantiated building piece prefab

Controls the **visual and physical runtime behavior** of a placed building piece. Manages mesh color feedback, debug text display, and destruction logic.

#### Serialized / Inspector Fields

| Field | Type | Description |
|---|---|---|
| `debugId` | `int` | Inspector-visible mirror of the item's ID |

#### Key Properties

| Property | Type | Description |
|---|---|---|
| `id` | `int` | Unique ID; setting it also updates `debugId` |
| `MeshObjects` | `List<GameObject>` | Child GameObjects tagged `BuildItemMesh` |
| `MeshColliders` | `List<Collider>` | Colliders attached to mesh objects |
| `MeshMaterials` | `List<Material>` | Materials used by mesh renderers |
| `buildingArea` | `BuildingArea` | The area this item belongs to |
| `constructedItem` | `ConstructedItem` | The data model backing this behaviour |
| `DebugEnabled` | `bool` | Enables verbose debug logging |

#### Child Object Tags Required

The prefab must use child GameObjects with these tags:

| Tag | Purpose |
|---|---|
| `BuildItemMesh` | Visual mesh + collider for physics and rendering |
| `BuildItemDebug` | GameObject with a `TextMeshPro` component for debug labels |

#### Awake()

Iterates all child GameObjects, categorizing them by tag. Caches mesh objects, materials, colliders, and original colors. Finds and caches the debug text object.

#### Update()

Handles smooth color interpolation back to the original mesh colors over `_resetDuration` (2.5 seconds) after `SetMeshColor()` is called.

#### Key Methods

```csharp
void SetMeshColor(Color color)
```
Immediately applies a color to all mesh materials and schedules a smooth fade back to the original color over 2.5 seconds.

```csharp
void RemoveConstructedItem(bool delayed)
```
Destroys this item. If `delayed = true`, destruction is deferred by 10 seconds (allows physics to animate the fall). Optionally enables `Rigidbody` gravity and applies a random impulse force for visual effect.

```csharp
void ShowDistance()
```
Displays the current distance value in the debug text label. Colors the mesh green if grounded, or red with intensity proportional to distance if not.

```csharp
void SetDebugText(string text)
```
Updates the `TextMeshPro` debug label if one exists.

---

### MainThreadDispatcher.cs

**Type:** `MonoBehaviour`  
**Attach to:** A persistent GameObject in the scene (e.g., `GameManager`)

Provides a **thread-safe bridge** between background worker threads and Unity's main thread. Unity's API is not thread-safe — GameObjects, Transforms, and physics calls must run on the main thread. This dispatcher queues actions from background threads and executes them during the next `Update()` frame.

#### How It Works

1. Background thread calls `MainThreadDispatcher.Enqueue(action)`.
2. The action is added to a `ConcurrentQueue<Action>`.
3. Each frame, `Update()` drains the queue and executes each action on the main thread.

#### Key Methods

```csharp
public static void Enqueue(Action action)
```
Thread-safe. Can be called from any thread. Null-checks the action before queuing.

#### Usage Example

```csharp
// From inside a worker thread:
MainThreadDispatcher.Enqueue(() =>
{
    // Safe to call Unity API here
    someGameObject.SetActive(false);
});
```

> ⚠️ **Important:** Do not call Unity API methods (e.g., `Destroy`, `GetComponent`, physics queries) directly from background threads. Always marshal them through this dispatcher.

---

### RecalculationWorker.cs

**Type:** `MonoBehaviour` (Singleton)  
**Attach to:** A persistent GameObject in the scene

Periodically runs a **Dijkstra shortest-path calculation** across all constructed items in registered `BuildingArea`s. Determines which items are within structural range of a grounded node. Items out of range are flagged for removal.

Heavy graph computation runs on a **background thread**; all Unity API calls are dispatched back to the main thread via `MainThreadDispatcher`.

#### Inspector Fields

| Field | Type | Default | Description |
|---|---|---|---|
| `_updatePeriod` | `float` | `5.0` | Seconds between recalculation runs |
| `_maxStrength` | `float` | `6.0` | Maximum allowed Dijkstra distance from a grounded node |
| `_debugEnabled` | `bool` | `true` | Enables timing and state logs |

#### Singleton Access

```csharp
RecalculationWorker.Instance
```

#### Key Methods

```csharp
void SetWorkerInstructions(List<BuildingArea> areas)
```
Provides the list of `BuildingArea`s to process on the next recalculation cycle. Called externally by the `Builder` whenever a new item is placed or destroyed.

#### Recalculation Pipeline

```
Update() — every _updatePeriod seconds:
│
├─ [Main Thread] Re-check grounded state via physics collider queries
│
├─ Start background Thread → Recalculate()
│   ├─ Build Dijkstra graph from neighbour edges
│   ├─ Run FindNodesWithinDistanceWithDistances() from each grounded node
│   └─ Merge results keeping shortest distances
│
└─ MainThreadDispatcher.Enqueue()
    ├─ Write distances back to ConstructedItems
    ├─ Set _running = false
    ├─ ShowDistances() (debug)
    └─ RemoveWeakConstructedItems()
```

#### Grounded Check (Main Thread — before thread start)

Before launching the background thread, the worker re-validates which items are still physically touching terrain or a buildable layer:

```csharp
Builder.Instance.IsTouchingAnyTerrain(collider)
Builder.Instance.IsTouchingBuildableLayer(collider)
```

These calls use Unity physics and **must** run on the main thread.

#### Distance Write-back (Main Thread — via dispatcher)

After Dijkstra completes, distances are applied:

| Condition | Distance Set |
|---|---|
| Item is grounded | `0.0f` |
| Item is within range of a grounded node | Dijkstra distance |
| Item is unreachable | `float.PositiveInfinity` |

Items with `PositiveInfinity` distance are then removed by `BuildingArea.RemoveWeakConstructedItems()`.

---

## Key Concepts

### Structural Integrity via Dijkstra

Each building piece has a `Strength` value (from its `BuildTemplate`). When connected pieces form a graph, Dijkstra's algorithm calculates the shortest weighted path from every grounded node to every other node. If a piece cannot be reached within `_maxStrength`, it is considered structurally unsupported and is removed.

### Thread Safety

All Unity API calls (physics, `Destroy`, mesh color changes) are confined to the main thread. Background threads only read cached data arrays and write to plain C# fields. Results are written back to Unity objects via `MainThreadDispatcher`.

### Neighbour Graph

`ConstructedItem.Neighbours` stores a flat list of IDs representing adjacency. Edges are created at placement time when a new item snaps onto an existing one. This graph is rebuilt into a `Dictionary<int, List<Edge>>` each recalculation cycle for Dijkstra.

---

## Data Flow

```
Player places piece
        │
        ▼
Builder.cs assigns global ID
        │
        ▼
BuildingArea.AddBuildItem()
  ├─ Creates ConstructedItem (data)
  ├─ Links ConstructedItemBehaviour (runtime)
  └─ Adds neighbour connections
        │
        ▼
RecalculationWorker.SetWorkerInstructions()
        │
  (every _updatePeriod seconds)
        │
        ▼
RecalculationWorker.Update()
  ├─ Validates grounded state (main thread, physics)
  └─ Starts background thread
        │
        ▼
Recalculate() [background thread]
  ├─ Builds graph from ConstructedItem.Neighbours
  ├─ Runs Dijkstra from all grounded nodes
  └─ Enqueues result write-back
        │
        ▼
MainThreadDispatcher executes on next frame
  ├─ Writes distances to ConstructedItems
  └─ BuildingArea.RemoveWeakConstructedItems()
        │
        ▼
ConstructedItemBehaviour.RemoveConstructedItem()
  └─ Physics impulse + delayed Destroy()
```

---

## Known Limitations & Future Improvements

- **Cross-area neighbours:** `ConstructedItem.GetAllNeighbourEdges()` currently only builds edges within a single `BuildingArea`. Cross-area connections are detected but not included in the graph, which may cause incorrect distance calculations at area boundaries.
- **No save/load:** Constructed item state is runtime-only; serialization to disk is not yet implemented.
- **Single thread:** Only one recalculation thread runs at a time. For very large builds, this could become a bottleneck.
- **Color reset always runs:** `ConstructedItemBehaviour.Update()` checks `_resetNeeded` every frame even when idle — a minor optimization opportunity.
- **Snap point validation:** The `SnapPointIndex` is stored but snap logic itself is handled elsewhere (`Builder.cs`), which is not yet documented in this episode.

---

*Last updated: EP02 — Build System*
