using System.Collections;
using UnityEngine;
using System.Collections.Generic;
using UnityEngine.Events;

namespace Goodgulf.TerrainUtils
{
    public class TerrainStreamingController : MonoBehaviour
    {
        public Transform player; // Reference to the player transform
        public Terrain terrainPrefab; // Prefab for creating terrain chunks
        public int viewDistance = 2; // Number of chunks to keep loaded around the player

        Dictionary<Vector2Int, Terrain> loadedChunks = new(); // Currently loaded terrain chunks
        StreamingTerrainGeneratorJobs generator; // Reference to the terrain generator

        public UnityEvent OnFirstChunksLoaded; // Event triggered when initial chunks are completely loaded
        
        // Parent object for all instantiated terrain objects
        public GameObject terrainParent;
        
        private bool _firstChunksLoaded = false; // Flag indicating if initial chunks have been loaded
        
        private TerrainPrefabPlacer prefabPlacer; // Component responsible for placing prefabs on terrain
        
        void Awake()
        {
            // Initialize the generator and prefab placer
            generator = GetComponent<StreamingTerrainGeneratorJobs>();
            prefabPlacer = gameObject.GetComponent<TerrainPrefabPlacer>();

            if (prefabPlacer != null)
            {
                // Set base seed for prefab placement
                prefabPlacer.SetBaseSeed(generator.seed);
            }
        }
        
        void Update()
        {
            // Determine player's current chunk position
            Vector2Int playerChunk = GetPlayerChunk();

            HashSet<Vector2Int> needed = new (); // Chunks that need to be loaded

            for (int y = -viewDistance; y <= viewDistance; y++)
            for (int x = -viewDistance; x <= viewDistance; x++)
                needed.Add(playerChunk + new Vector2Int(x, y));

            // Load new chunks
            foreach (var coord in needed)
            {
                if (!loadedChunks.ContainsKey(coord))
                    LoadChunk(coord);
            }

            // Unload distant chunks
            List<Vector2Int> toRemove = new ();

            foreach (var kvp in loadedChunks)
            {
                if (!needed.Contains(kvp.Key))
                    toRemove.Add(kvp.Key);
            }

            foreach (var coord in toRemove)
            {
                UnloadChunk(coord);
            }
            
            // Update colliders of nearby terrains
            UpdateTerrainColliders(playerChunk);

            if (!_firstChunksLoaded)
            {
                _firstChunksLoaded = true;

                // Start coroutine to spawn objects after initial chunk load
                StartCoroutine(SpawnObjectsWhenReady());
            }
        }

        IEnumerator SpawnObjectsWhenReady()
        {
            // Determine the player's spawn location in chunk coordinates
            Vector3 spawnXZ = new Vector3(player.position.x, player.position.y, player.position.z);
            Vector2Int spawnChunk = WorldToChunk(spawnXZ);

            // Wait until the required chunk is loaded
            while (!loadedChunks.ContainsKey(spawnChunk))
                yield return null;

            Terrain terrain = loadedChunks[spawnChunk];

            // Ensure the collider is enabled before proceeding
            TerrainCollider collider = terrain.GetComponent<TerrainCollider>();
            while (collider == null || !collider.enabled)
                yield return null;

            // Wait for physics engine to update with colliders
            yield return new WaitForFixedUpdate();

            // Perform raycast to find the ground height under the player
            Vector3 rayStart = new Vector3(
                spawnXZ.x,
                2000f,   // Start cast high above the terrain
                spawnXZ.z
            );

            if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, 4000f))
            {
                OnFirstChunksLoaded?.Invoke(); // Trigger event for initial load completion
            }
            else
            {
                Debug.LogError("Failed to find terrain under spawn point!"); // Error if no collision found
            }
        }
        
        // Converts a world position to the chunk grid coordinate
        Vector2Int WorldToChunk(Vector3 worldPos)
        {
            Vector3 size = terrainPrefab.terrainData.size;

            int chunkX = Mathf.FloorToInt(worldPos.x / size.x);
            int chunkZ = Mathf.FloorToInt(worldPos.z / size.z);

            return new Vector2Int(chunkX, chunkZ);
        }

        // Enables or disables colliders for terrain chunks based on proximity to the player
        void UpdateTerrainColliders(Vector2Int playerChunk)
        {
            const int colliderDistance = 1;

            foreach (var kvp in loadedChunks)
            {
                Vector2Int chunkCoord = kvp.Key;
                Terrain terrain = kvp.Value;

                TerrainCollider collider = terrain.GetComponent<TerrainCollider>();
                if (collider == null) continue;

                int dx = Mathf.Abs(chunkCoord.x - playerChunk.x);
                int dz = Mathf.Abs(chunkCoord.y - playerChunk.y);

                bool shouldEnable = dx <= colliderDistance && dz <= colliderDistance;

                if (collider.enabled != shouldEnable)
                    collider.enabled = shouldEnable;
            }
        }

        // Determines which chunk the player is currently in
        Vector2Int GetPlayerChunk()
        {
            TerrainData data = terrainPrefab.terrainData;
            Vector3 size = data.size;

            int cx = Mathf.FloorToInt(player.position.x / size.x);
            int cy = Mathf.FloorToInt(player.position.z / size.z);

            return new Vector2Int(cx, cy);
        }

        // Loads a new terrain chunk at the specified coordinates
        void LoadChunk(Vector2Int coord)
        {
            if (loadedChunks.ContainsKey(coord))
                return;
            
            Terrain terrain;
            
            // Instantiate terrain either under a parent object or directly in scene
            if (terrainParent != null)
            {
                terrain = Instantiate(
                    terrainPrefab,
                    new Vector3(
                        coord.x * terrainPrefab.terrainData.size.x,
                        0,
                        coord.y * terrainPrefab.terrainData.size.z
                    ),
                    Quaternion.identity,
                    terrainParent.transform
                );
            }
            else
            {
                terrain = Instantiate(
                    terrainPrefab,
                    new Vector3(
                        coord.x * terrainPrefab.terrainData.size.x,
                        0,
                        coord.y * terrainPrefab.terrainData.size.z
                    ),
                    Quaternion.identity
                );
            }

            terrain.name = $"Terrain_{coord.x}_{coord.y}";

            // Get pre-allocated TerrainData and assign it to new terrain
            TerrainData pooledData = TerrainDataPool.Instance.Get();
            terrain.terrainData = pooledData;
            terrain.terrainData.name = $"TerrainData_{coord.x}_{coord.y}";

            // Update terrain collider reference
            TerrainCollider collider = terrain.GetComponent<TerrainCollider>();
            if(collider != null)
                collider.terrainData = pooledData;
            
            loadedChunks.Add(coord, terrain);

            // Generate the terrain features for this chunk
            generator.GenerateChunk(terrain, coord);
            
            // Place additional prefabs on the terrain if applicable
            if (prefabPlacer != null)
            {
                prefabPlacer.SetTerrain(terrain);
                prefabPlacer.PlacePrefabsOnChunk(coord.x, coord.y, terrainPrefab.terrainData.size.x);
            }
        }
        
        // Unloads the terrain chunk at the specified coordinates
        void UnloadChunk(Vector2Int coord)
        {
            if (!loadedChunks.TryGetValue(coord, out Terrain terrain))
                return;

            TerrainData data = terrain.terrainData;

            // Detach TerrainData before destroying the terrain object
            terrain.terrainData = null;

            TerrainCollider collider = terrain.GetComponent<TerrainCollider>();
            collider.terrainData = null;

            // Return TerrainData to pool for reuse
            TerrainDataPool.Instance.Release(data);

            Destroy(terrain.gameObject);

            loadedChunks.Remove(coord);
            
            // Clear any placed objects on the unloaded chunk
            if (prefabPlacer != null)
            {
                prefabPlacer.ClearChunk(new Vector2Int(coord.x, coord.y));
            }
        }
    }
}
