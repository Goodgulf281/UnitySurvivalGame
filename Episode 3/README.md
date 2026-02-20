# Documentation

Start here [Overview](./Overview_StreamingTerrainSolution.md)

Each file is documented in the Procedural Terrain Documentation Folder

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
