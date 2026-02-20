# Documentation

Start here [Overview](./Overview_StreamingTerrainSolution.md)

## Files in This Solution

| File | Purpose | Documentation
|---|---|---|
| `TerrainStreamingController.cs` | Chunk load/unload orchestration, player tracking | [readme](./Procedural Terrain Documentation/TerrainStreamingController.md)
| `StreamingTerrainGeneratorJobs.cs` | Schedules Jobs-based heightmap generation per chunk |
| `TerrainHeightJob.cs` | Burst-compiled parallel job — full height pipeline |
| `TerrainDataPool.cs` | Object pool for `TerrainData` instances |
| `TerrainPrefabPlacer.cs` | Deterministic prefab scattering on chunks |
| `PrefabPlacementConfig.cs` | ScriptableObject — placement rules and constraints |
| `TerrainPositionObjects.cs` | Utility — raycast-snaps objects to terrain surface |
