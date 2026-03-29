# PathfindingAgent.cs

**Namespace:** `Goodgulf.Pathfinding`  
**Attribute:** `[SelectionBase]`  
**Implements:** `IDebuggable`

Self-contained agent MonoBehaviour. Drives NPC movement across procedurally generated terrain by following paths produced by `AStarSearch`. Does not use Unity's NavMesh.

---

## Behaviour priority

Every `Update()` evaluates behaviours in priority order. Higher-priority behaviours short-circuit lower ones:

```
1. Line-of-sight (if LOSBehaviour ≠ None and PlayerTransform assigned)
       ↓ if active, HandleLOSMovement() runs and Update() returns early
2. Current AgentMode (Idle / MoveTo / Patrol / FollowLeader)
3. FollowCurrentPath() — always called last; advances the agent along _currentPath
```

---

## Inspector reference

### Movement

| Field | Default | Description |
|---|---|---|
| `MoveSpeed` | 5 | World units per second |
| `RotationSpeed` | 360 | Degrees per second toward movement direction |
| `WaypointReachDistance` | 0.5 | XZ distance at which a waypoint is considered reached |
| `HeightOffset` | 0.1 | Y offset above sampled terrain height — prevents Z-fighting |

`WaypointReachDistance` should be at least half of `NodeSpacing` (at 0.5 n/m that is 1.0 m). Too small a value causes agents to oscillate past waypoints; too large causes them to cut corners.

### Behaviour Mode

`StartingMode` — the `AgentMode` the agent enters in `Start()`. Must be set correctly in the Inspector; `HandleFollowLeader` and others only run when `_currentMode` matches.

### Move To

`MoveTarget` — the `Transform` the agent paths toward in `MoveTo` mode. If null when `MoveTo()` is called at runtime, a new GameObject named `AgentMoveTarget` is created automatically.

### Patrol

| Field | Default | Description |
|---|---|---|
| `PatrolPoints` | (empty) | List of `Transform` waypoints visited in sequence |
| `PatrolPingPong` | false | If true, reverses direction at both ends. If false, wraps to index 0 |

### Line of Sight

| Field | Default | Description |
|---|---|---|
| `LOSBehaviour` | None | `None` disables LOS entirely. `Approach` paths toward the player. `Flee` paths away |
| `PlayerTransform` | — | The transform to react to |
| `LOSActivationDistance` | 20 | Distance within which the LOS raycast is attempted |
| `LOSOcclusionMask` | — | LayerMask for occlusion check. Should include walls and terrain |
| `ApproachStopDistance` | 3 | Approach mode stops pathing when this close to the player |
| `FleeDistance` | 15 | Flee mode maintains at least this distance from the player |

**LOS does not activate in `MoveTo` mode** — add a mode guard in `EvaluateLOS()` if you want to prevent LOS from interrupting explicit movement commands:

```csharp
if (_currentMode == AgentMode.MoveTo || _currentMode == AgentMode.FollowLeader) return false;
```

### Flocking

| Field | Default | Description |
|---|---|---|
| `IsLeader` | false | Leaders pick their own targets. Followers track a leader |
| `Leader` | — | The `PathfindingAgent` to follow in `FollowLeader` mode |
| `FlockRadius` | 5 | Followers path to `Leader.position + _flockOffset`. Re-path when drift > `FlockRadius * 0.5` |
| `FollowerSeparation` | 1.5 | Minimum spacing between followers enforced by overlap-sphere push |

---

## Agent modes

### `AgentMode.Idle`

The agent does nothing. `_currentPath` is preserved — switching back to an active mode resumes from the last waypoint index.

### `AgentMode.MoveTo`

`HandleMoveTo()` checks whether the current path is exhausted and requests a new one:

```csharp
bool pathExhausted = _currentPath.Count == 0 || _pathIndex >= _currentPath.Count;
if (pathExhausted && _pathCooldown <= 0f)
    RequestPath(MoveTarget.position);
```

A new path is only requested when the current one is finished, not on every cooldown expiry. This prevents the agent from resetting to waypoint 0 mid-journey.

### `AgentMode.Patrol`

`HandlePatrol()` advances to the next patrol point only when the current path is exhausted:

```csharp
bool pathExhausted = _currentPath.Count == 0 || _pathIndex >= _currentPath.Count;
if (pathExhausted)
{
    AdvancePatrolIndex();
    RequestPath(PatrolPoints[_patrolIndex].position);
}
```

**Loop mode** (`PatrolPingPong = false`): index advances 0 → 1 → 2 → … → N → 0.  
**Ping-pong mode** (`PatrolPingPong = true`): direction reverses at both ends: 0 → N → 0 → N.

### `AgentMode.FollowLeader`

`HandleFollowLeader()` computes the desired position as `Leader.transform.position + _flockOffset` and re-paths when the follower has drifted beyond `FlockRadius * 0.5` from that position and the current path is exhausted.

The flock offset is computed once in `ComputeFlockOffset()` using a deterministic angular slot based on `GetInstanceID() % 8`. This distributes up to 8 followers evenly around the leader without runtime negotiation. If `_flockOffset` is still `Vector3.zero` when `HandleFollowLeader()` runs (e.g. because the leader was assigned after `Start()`), `ComputeFlockOffset()` is called lazily.

`ApplyFollowerSeparation()` runs every frame regardless of path state, pushing the agent away from any nearby `PathfindingAgent` instances within `FollowerSeparation` units.

---

## Path following — `FollowCurrentPath()`

Called every frame after the mode handler. Advances `_pathIndex` when the current waypoint is within `WaypointReachDistance` (XZ only), and translates the agent toward the current waypoint:

```csharp
Vector3 dir  = (flat - transform.position).normalized;  // flat = target with agent's Y
Vector3 move = dir * (MoveSpeed * Time.deltaTime);
transform.position = new Vector3(
    transform.position.x + move.x,
    GetTerrainY() + HeightOffset,
    transform.position.z + move.z);
```

Y is snapped to terrain each frame via `GetTerrainY()` rather than interpolated from the waypoint — waypoints carry their own Y (the node's `WorldY`) but the agent uses raycasting for precise ground contact.

---

## Terrain height snapping — `GetTerrainY()`

```csharp
private float GetTerrainY()
{
    if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 500f, TerrainLayerMask))
        return hit.point.y;

    // Fallback: read WorldY from the pathfinding graph
    if (TerrainGraphIntegration.Instance?.Graph != null)
    {
        NodeData nd = TerrainGraphIntegration.Instance.Graph.GetNode(
            transform.position.x, transform.position.z);
        if (nd.WorldY > 0f) return nd.WorldY;
    }

    return transform.position.y;  // last resort — no accumulation
}
```

**Do not use `Terrain.activeTerrain`** in a multi-chunk streaming setup. `activeTerrain` returns one arbitrary chunk and `SampleHeight()` on the wrong chunk gives incorrect heights. The graph fallback is preferred because it uses exactly the same height data that was used to build the path.

**`TerrainLayerMask` must not be `~0`** — include only your Terrain physics layer. The fallback chain must be robust because chunk boundaries can produce brief raycast misses as terrain loads.

**The Y-accumulation bug:** if `GetTerrainY()` falls through to `return transform.position.y` on every frame, the agent rises at `HeightOffset` per frame (0.1 units × 60 fps = 6 units/second). Always provide a `TerrainLayerMask` that includes the terrain layer, and keep the graph fallback active.

---

## Path cooldown

`_pathCooldown` gates `RequestPath()` calls — a new path is requested at most once every `PathCooldownTime` (0.5 seconds) per agent. The cooldown decrements in `Update()` and is reset to `PathCooldownTime` inside `RequestPath()`.

For large numbers of agents, increase `PathCooldownTime` or implement a request queue so not all agents search in the same frame.

---

## Public API

```csharp
agent.MoveTo(new Vector3(500, 0, 200));  // enters MoveTo mode, requests path
agent.StartPatrol();                      // enters Patrol mode, paths to PatrolPoints[0]
agent.StartFollowing(leaderAgent);        // enters FollowLeader mode, computes flock offset
agent.Stop();                             // enters Idle, clears _currentPath
```

---

## Scene view gizmos (`OnDrawGizmos`)

All gizmos draw unconditionally when the agent is visible in the Hierarchy, not just when selected.

| Gizmo | Colour | Description |
|---|---|---|
| Patrol spheres | Cyan | One sphere per `PatrolPoint`, labelled P0, P1… |
| Patrol connections | Cyan | Lines between consecutive patrol points |
| Current patrol target | Yellow wire sphere | Highlights the active patrol target |
| Path lines | Orange | Lines connecting remaining waypoints from `_pathIndex` onward |
| Current waypoint | Red sphere | The waypoint the agent is currently moving toward |
| LOS activation radius | Yellow (inactive) / Red (active) | Wire sphere at `LOSActivationDistance` |
| Approach stop radius | Green wire sphere | Visible when LOS approach is active |
| Flee distance | Red wire sphere | Visible when LOS flee is active |
| Leader flock radius | Semi-transparent gold | Wire sphere around the leader showing `FlockRadius` |
| Follower line | Blue line | Line from follower to its leader |

---

## Custom Inspector — `PathfindingAgentEditor`

Adds runtime buttons in the Inspector when the game is playing:

| Button | Action |
|---|---|
| Start Patrol | `agent.StartPatrol()` |
| Stop | `agent.Stop()` |
| Move to MoveTarget | `agent.MoveTo(MoveTarget.position)` |
| Force Full Graph Rebuild | `TerrainGraphIntegration.Instance.ForceFullRebuildSync()` |
| Dirty Region Around Agent | `RequestDirtyRegionRebuild(agent.position, 10f)` |

---

## Known pitfalls

**Agent constantly re-requests paths** — `HandleMoveTo()`, `HandlePatrol()`, `HandleFollowLeader()`, and `HandleLOSMovement()` must all guard path requests with `pathExhausted && _pathCooldown <= 0f`. Without the `pathExhausted` check, a new path is requested every cooldown tick, `_pathIndex` resets to 0, and the agent oscillates near its start position.

**LOS overrides MoveTo unexpectedly** — `EvaluateLOS()` runs before the mode switch and will hijack any mode if `PlayerTransform` is assigned and the agent is in range. Add a mode guard or set `LOSBehaviour = None` for agents that should never react to the player.

**Follower does not move** — Check that `StartingMode` is `FollowLeader` in the Inspector, that `Leader` is assigned, and that `IsLeader` is false. If all three are correct but the agent still does not move, `_flockOffset` may be zero because `Leader` was assigned after `Start()` ran. The lazy computation in `HandleFollowLeader()` handles this, but only if the mode is entered correctly.

**Agent shoots upward** — `GetTerrainY()` is returning `transform.position.y` every frame because the raycast is missing. Check `TerrainLayerMask` is set and includes the terrain layer. Confirm the agent is not already far above the terrain (past the 500-unit raycast range).
