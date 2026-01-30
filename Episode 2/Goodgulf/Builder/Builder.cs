using System;
using System.IO;
using UnityEngine;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Profiling;
using System.Threading;
using UnityEditor;
using UnityEngine.Serialization;
using Goodgulf.TerrainUtils;
using NUnit.Framework.Internal;

namespace Goodgulf.Builder
{
    /// <summary>
    /// Central manager responsible for all building logic:
    /// - Terrain and grid management
    /// - Building area creation
    /// - Constructed item lifecycle
    /// - Player interaction with the building system
    /// </summary>
    public class Builder : MonoBehaviour
    {
        // All available build templates (prefabs + metadata)
        [SerializeField]
        private List<BuildTemplate> _buildItemTemplates;

        // Terrain/grid constants
        public const int TerrainSize = 1000;
        public const int BuildingAreaSize = 100;
        public const int GridSize = TerrainSize / BuildingAreaSize;

        // Prefab used to visually represent a building area
        [SerializeField] 
        private GameObject _buildingAreaPrefab;

        // Worker update timer (used to periodically recalc areas)
        [Header("Worker Timer")]
        [SerializeField] 
        private float _workerTimerDuration = 5.0f;
        private float _workerTimer = 0.0f;

        // Debug toggles
        [Header("Debug Mode")]
        [SerializeField]
        private bool _debugEnabled = true;

        // World and terrain references
        [Header("World")]
        [SerializeField]
        private Vector3 _worldOrigin;
        [SerializeField]
        private Terrain _activeTerrain;
        private Collider _activeTerrainCollider;

        // Build constraints and layers
        [Header("Buildable")]
        [SerializeField]
        private LayerMask _buildigBlockLayers;
        [SerializeField]
        private LayerMask _buildableLayers;
        [SerializeField]
        private List<NonBuildableArea> _nonBuildableAreas;
        [SerializeField] 
        private LayerMask _transparentBlockLayer;

        // Magnetic snapping parameters
        [SerializeField] 
        private float _magneticRadius = 0.5f;
        [SerializeField] 
        private LayerMask _magenticLayer;

        // All terrains indexed by grid coordinate
        private Dictionary<Vector2Int, Terrain> _allTerrains;

        /// <summary>
        /// Currently active terrain; also caches its collider
        /// </summary>
        public Terrain ActiveTerrain
        {
            get => _activeTerrain;
            set
            {
                _activeTerrain = value;
                _activeTerrainCollider = _activeTerrain.GetComponent<Collider>();
            }
        }

        // Building areas indexed by GUID
        private Dictionary<string, BuildingArea> _buildingAreasById;

        // All constructed items indexed by global ID
        private Dictionary<int, ConstructedItem> _allConstructedItems;

        // Player references
        private GameObject _player;
        private PlayerBuilderInventoryBridge _playerBuilderInventoryBridge;

        // Global incremental ID for constructed items
        private int _globalID = 0;

        // Singleton instance
        public static Builder Instance { get; private set; }

        /// <summary>
        /// Initialize singleton and cache all active terrains
        /// </summary>
        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;

            _buildingAreasById = new Dictionary<string, BuildingArea>();
            _allConstructedItems = new Dictionary<int, ConstructedItem>();
            _allTerrains = new Dictionary<Vector2Int, Terrain>();

            // Cache all active terrains by world index
            Terrain[] terrains = Terrain.activeTerrains;
            foreach (var terrain in terrains)
            {
                Vector2Int terPos = GetTerrainIndex(terrain.transform.position);

                if (_debugEnabled)
                    Debug.Log($"Found terrain: {terrain.name} at {terrain.transform.position} -> index {terPos}");

                _allTerrains.Add(terPos, terrain);
            }
        }

        /// <summary>
        /// Draws world origin gizmo for debugging
        /// </summary>
        void OnDrawGizmos()
        {
            Gizmos.color = Color.green;
            Gizmos.DrawLine(_worldOrigin, _worldOrigin + Vector3.up * 20f);
        }

        /// <summary>
        /// Initialize terrain collider and player inventory bridge
        /// </summary>
        private void Start()
        {
            if (_activeTerrain && _activeTerrainCollider == null)
                _activeTerrainCollider = _activeTerrain.GetComponent<Collider>();

            if (_player)
            {
                _playerBuilderInventoryBridge = _player.GetComponent<PlayerBuilderInventoryBridge>();
                if (!_playerBuilderInventoryBridge)
                    Debug.LogWarning("Builder.Start(): PlayerBuilderInventoryBridge not found");
            }
        }

        public PlayerBuilderInventoryBridge GetPlayerBuilderInventoryBridge()
        {
            return _playerBuilderInventoryBridge;
        }

        /// <summary>
        /// Returns a unique global ID for constructed items
        /// </summary>
        public int GetNewGlobalID()
        {
            return _globalID++;
        }

        /// <summary>
        /// Fetch a constructed item by ID
        /// </summary>
        public ConstructedItem GetConstructedItem(int itemID)
        {
            _allConstructedItems.TryGetValue(itemID, out ConstructedItem constructedItem);
            return constructedItem;
        }

        /// <summary>
        /// Recalculates which building areas should be processed by the worker,
        /// based on player position (3x3 grid around player).
        /// </summary>
        private void UpdateWorkerAreas()
        {
            BuildableTerrain buildableTerrain = _activeTerrain.GetComponent<BuildableTerrain>();
            if (buildableTerrain == null)
            {
                Debug.LogError("Builder.UpdateWorkerAreas(): BuildableTerrain not found");
                return;
            }

            Dictionary<Vector2Int, BuildingArea> buildingAreas = buildableTerrain.BuildingAreas;
            List<BuildingArea> areas = new List<BuildingArea>();

            Vector2Int playerPos = WorldToGrid(
                _player.transform.position,
                _activeTerrain.transform.position,
                BuildingAreaSize
            );

            // Collect surrounding 3x3 areas
            for (int x = playerPos.x - 1; x <= playerPos.x + 1; x++)
            {
                for (int y = playerPos.y - 1; y <= playerPos.y + 1; y++)
                {
                    if (x >= 0 && x < GridSize && y >= 0 && y < GridSize)
                    {
                        Vector2Int gp = new Vector2Int(x, y);
                        if (buildingAreas.TryGetValue(gp, out var area))
                            areas.Add(area);
                    }
                }
            }

            if (areas.Count > 0)
                RecalculationWorker.Instance.SetWorkerInstructions(areas);
        }

        /// <summary>
        /// Periodically updates worker areas based on timer
        /// </summary>
        private void Update()
        {
            _workerTimer += Time.deltaTime;
            if (_workerTimer > _workerTimerDuration)
            {
                _workerTimer = 0.0f;
                UpdateWorkerAreas();
            }
        }

        /// <summary>
        /// Assigns the player and determines which terrain they are on
        /// </summary>
        public void SetPlayer(GameObject player)
        {
            _player = player;

            Vector2Int worldIndex = GetTerrainIndex(player.transform.position);
            Terrain t = _allTerrains[worldIndex];

            if (t == null)
                Debug.LogError("Builder.SetPlayer(): Terrain not found.");
            else
                SetTerrain(t);

            _playerBuilderInventoryBridge = _player.GetComponent<PlayerBuilderInventoryBridge>();
        }

        /// <summary>
        /// Sets the active terrain and caches its collider
        /// </summary>
        public void SetTerrain(Terrain t)
        {
            _activeTerrain = t;
            _activeTerrainCollider = _activeTerrain.GetComponent<Collider>();
        }

        /// <summary>
        /// Converts a world position into a terrain grid index
        /// </summary>
        Vector2Int GetTerrainIndex(Vector3 worldPos)
        {
            float terrainSize = 1000f;

            int x = Mathf.FloorToInt((worldPos.x - _worldOrigin.x) / terrainSize);
            int z = Mathf.FloorToInt((worldPos.z - _worldOrigin.z) / terrainSize);

            return new Vector2Int(x, z);
        }

        /// <summary>
        /// Converts world position to building-area grid coordinates
        /// </summary>
        public static Vector2Int WorldToGrid(Vector3 worldPos, Vector3 terrainPosition, int buildingAreaSize)
        {
            Vector3 localPos = worldPos - terrainPosition;
            return new Vector2Int(
                Mathf.FloorToInt(localPos.x / buildingAreaSize),
                Mathf.FloorToInt(localPos.z / buildingAreaSize)
            );
        }

        /// <summary>
        /// Converts grid position back to world space
        /// </summary>
        public static Vector3 GridToWorld(Vector2Int gridPos, Vector3 terrainPosition, int buildingAreaSize)
        {
            float x = gridPos.x * buildingAreaSize + buildingAreaSize * 0.5f;
            float z = gridPos.y * buildingAreaSize + buildingAreaSize * 0.5f;
            return terrainPosition + new Vector3(x, 0f, z);
        }



        /// <summary>
        /// Returns an existing BuildingArea for the given world position,
        /// or creates and registers a new one if none exists.
        /// </summary>
        public BuildingArea GetOrCreateBuildingArea(Vector3 worldPosition)
        {
            // Determine which terrain this world position belongs to
            Vector2Int terrainIndex = GetTerrainIndex(worldPosition);
            Terrain trn = _allTerrains[terrainIndex];

            // Validate terrain existence
            if (trn == null)
            {
                Debug.LogError($"Builder.GetOrCreateBuildingArea(): no terrain at position {worldPosition}");
                return null;
            }

            Dictionary<Vector2Int, BuildingArea> bas;

            // Fetch the BuildableTerrain component which holds building areas
            BuildableTerrain buildableTerrain = trn.GetComponent<BuildableTerrain>();
            if (buildableTerrain != null)
            {
                if (_debugEnabled)
                    Debug.Log($"Builder.GetOrCreateBuildingArea({trn.name}): buildableTerrain get Dictionaries");

                bas = buildableTerrain.BuildingAreas;
            }
            else
            {
                // Terrain must support building
                Debug.LogError("Builder.GetOrCreateBuildingArea(): Terrain does not contain buildableTerrain.");
                return null;
            }

            // Convert world position to grid coordinates relative to this terrain
            Vector2Int gridPos = WorldToGrid(worldPosition, trn.transform.position, BuildingAreaSize);

            if (_debugEnabled)
                Debug.Log($"Builder.GetOrCreateBuildingArea(): on terrain {trn.name}[{terrainIndex}], world position {worldPosition}, and grid position {gridPos}");

            // Ensure grid position is within valid bounds
            if (gridPos.x < 0 || gridPos.y < 0 ||
                gridPos.x >= GridSize || gridPos.y >= GridSize)
            {
                Debug.LogError($"Builder.GetOrCreateBuildingArea(): {gridPos} is out of bounds.");
                return null;
            }

            // Return existing building area if one already exists at this grid position
            if (bas.TryGetValue(gridPos, out var area))
                return area;

            if (_debugEnabled)
                Debug.Log($"Builder.GetOrCreateBuildingArea(): gridPos is OK and no area found");

            // Convert grid position back to world position for spawning
            Vector3 spawnPos = GridToWorld(gridPos, trn.transform.position, BuildingAreaSize);

            if (_debugEnabled)
                Debug.Log($"Builder.GetOrCreateBuildingArea(): spawnPos={spawnPos}");

            // Instantiate the BuildingArea visual prefab
            GameObject go = Instantiate(
                _buildingAreaPrefab,
                spawnPos,
                Quaternion.identity,
                this.transform
            );

            // Assign terrain reference to the outline component
            BuildingAreaOutline bao = go.GetComponent<BuildingAreaOutline>();
            if (bao != null)
            {
                bao.terrain = trn;
            }
            else
            {
                Debug.LogError($"Builder.GetOrCreateBuildingArea(): bao is null");
                return null;
            }

            // Create and initialize the logical BuildingArea data
            area = new BuildingArea();
            area.DebugEnabled = _debugEnabled;
            area.Initialize(gridPos, BuildingAreaSize);
            area.Id = System.Guid.NewGuid().ToString();

            if (_debugEnabled)
                Debug.Log($"Builder.GetOrCreateBuildingArea(): created area with id ={area.Id}");

            // Register area for lookup by grid position and by ID
            bas.Add(gridPos, area);
            _buildingAreasById.Add(area.Id, area);

            return area;
        }

        /// <summary>
        /// Retrieves a BuildingArea by its unique ID.
        /// </summary>
        public BuildingArea GetBuildingAreaByID(string id)
        {
            return _buildingAreasById[id];
        }

        /// <summary>
        /// Returns a BuildTemplate by index, or null if index is invalid.
        /// </summary>
        public BuildTemplate GetBuildItemTemplate(int index)
        {
            if (index < 0 || index >= _buildItemTemplates.Count)
                return null;

            return _buildItemTemplates[index];
        }

        /// <summary>
        /// Instantiates a transparent preview prefab for placement visualization.
        /// </summary>
        public GameObject InstantiateTransparentPrefab(int index, Vector3 position, Quaternion rotation)
        {
            if (index < 0 || index >= _buildItemTemplates.Count)
                return null;

            return Instantiate(_buildItemTemplates[index].transparentPrefab, position, rotation);
        }

        /// <summary>
        /// Debug method that spawns a 10x10 grid of building prefabs for testing.
        /// </summary>
        public GameObject InstantiateBuildingPrefabDebug(int index, Vector3 position, Quaternion rotation)
        {
            BuildTemplate b = _buildItemTemplates[index];

            // Spawn a grid of buildings to test spacing and alignment
            for (int i = 0; i < 10; i++)
            {
                for (int j = 0; j < 10; j++)
                {
                    Vector3 pos = new Vector3(
                        position.x + b.width * i,
                        position.y + b.height * j - j * 0.001f, // slight offset to avoid z-fighting
                        position.z
                    );

                    InstantiateBuildingPrefab(index, pos, rotation, 0);
                }
            }

            return null;
        }

        /// <summary>
        /// Finds and marks constructed items within the bounds of a transparent
        /// placement prefab so they can be removed or weakened.
        /// </summary>
        /// <param name="instantiatedTransparentPrefab">
        /// The temporary placement prefab used to define the destruction area.
        /// </param>
        public void DestructConstructedItemsAtPos(GameObject instantiatedTransparentPrefab)
        {
            // Safety check: nothing to process
            if (instantiatedTransparentPrefab == null) return;

            // Expecting the prefab to have at least one child containing a collider
            if (instantiatedTransparentPrefab.transform.childCount == 0) return;

            // Use the first child's collider as the spatial reference
            Collider collider1 = instantiatedTransparentPrefab
                .transform
                .GetChild(0)
                .GetComponent<Collider>();

            // Collect all colliders within a sphere that fully encloses the collider bounds
            Collider[] hits = Physics.OverlapSphere(
                collider1.bounds.center,
                collider1.bounds.extents.magnitude
            );

            // Iterate over all detected colliders
            for (int i = 0; i < hits.Length; i++)
            {
                Collider collider = hits[i];

                // Constructed items are expected to live on the parent GameObject
                ConstructedItemBehaviour cib =
                    collider.gameObject.transform.parent.GetComponent<ConstructedItemBehaviour>();

                if (cib != null)
                {
                    // Retrieve the building area this constructed item belongs to
                    BuildingArea area = GetBuildingAreaByID(cib.constructedItem.BuildingAreaId);
                    if (area != null)
                    {
                        // Mark the item as infinitely distant so it is guaranteed to be removed
                        cib.constructedItem.Distance = float.PositiveInfinity;

                        // Remove or weaken the constructed item in its building area
                        area.RemoveWeakConstructedItem(cib.constructedItem);

                        // Visual feedback: mark the affected item
                        cib.SetMeshColor(Color.magenta);
                    }
                    else
                    {
                        Debug.LogError(
                            "Builder.DestructConstructedItemsAtPos(): cannot find buildingArea"
                        );
                    }
                }
            }
        }

        /// <summary>
        /// Legacy / unreliable grounding check.
        /// Uses collider bounds intersection with the active terrain collider.
        /// </summary>
        /// <remarks>
        /// This method is discouraged because terrain collider bounds
        /// often extend to the highest terrain point, causing false positives.
        /// </remarks>
        public bool IsGrounded(Collider collider)
        {
            Debug.LogWarning("Builder.IsGrounded(): do not use this method");

            // Terrain collider bounds include the highest terrain point,
            // so many objects below that height will appear "grounded".
            if (_activeTerrainCollider)
            {
                return collider.bounds.Intersects(_activeTerrainCollider.bounds);
            }
            else
            {
                Debug.LogWarning("Builder.IsGrounded(): terrainCollider == null");
                return false;
            }
        }

        /// <summary>
        /// Checks whether a collider is touching the currently active terrain
        /// by comparing its lowest Y bound to the terrain height.
        /// </summary>
        public bool IsTouchingTerrain(Collider col)
        {
            if (_activeTerrain)
            {
                // Use the collider's center position for terrain sampling
                Vector3 pos = col.bounds.center;

                // Sample terrain height in world space
                float terrainHeight =
                    ActiveTerrain.SampleHeight(pos) + ActiveTerrain.transform.position.y;

                // Touching if the collider bottom is at or below the terrain surface
                return col.bounds.min.y <= terrainHeight;
            }
            else
            {
                Debug.LogWarning("Builder.IsTouchingTerrain(): _terrain == null");
                return false;
            }
        }

        /// <summary>
        /// Checks whether a collider is touching the terrain at its current world position,
        /// taking terrain tiling into account.
        /// </summary>
        public bool IsTouchingAnyTerrain(Collider col)
        {
            Vector3 pos = col.bounds.center;

            // Determine which terrain tile this position belongs to
            Vector2Int index = GetTerrainIndex(pos);
            Terrain terrain = _allTerrains[index];

            if (terrain != null)
            {
                // Sample terrain height for the specific terrain tile
                float terrainHeight =
                    terrain.SampleHeight(pos) + terrain.transform.position.y;

                // Collider is touching terrain if its bottom is below the surface
                return col.bounds.min.y <= terrainHeight;
            }

            return false;

            /*
            // Previous approach: iterate over all terrains (less efficient)
            foreach (var kvp in _allTerrains)
            {
                Terrain terrain = kvp.Value;

                Vector3 pos = col.bounds.center;
                float terrainHeight =
                    terrain.SampleHeight(pos) + terrain.transform.position.y;

                Debug.Log($"{terrain.name}: TH={terrainHeight} and {col.bounds.min.y}");

                isTouchingTerrain = (col.bounds.min.y <= terrainHeight);
            }
            */
        }

        /// <summary>
        /// Checks whether a collider overlaps with any collider on a buildable layer.
        /// Trigger colliders are ignored.
        /// </summary>
        public bool IsTouchingBuildableLayer(Collider col)
        {
            return Physics.OverlapBox(
                col.bounds.center,
                col.bounds.extents,
                col.transform.rotation,
                _buildableLayers,
                QueryTriggerInteraction.Ignore
            ).Length > 0;
        }

        
        /// <summary>
        /// Instantiates a building prefab, registers it in a BuildingArea,
        /// detects grounding, snap points, neighbours, and updates distances.
        /// </summary>
        public GameObject InstantiateBuildingPrefab(int index, Vector3 position, Quaternion rotation, int snapPointIndex)
        {
            // Validate template index and player availability
            if (index < 0 || index >= _buildItemTemplates.Count || _player == null)
                return null;

            // Instantiate the building prefab under this Builder transform
            GameObject builtItem = Instantiate(
                _buildItemTemplates[index].buildPrefab,
                position,
                rotation,
                this.transform
            );

            // Rename the mesh child for clarity in the hierarchy
            builtItem.transform.GetChild(0).gameObject.name = builtItem.name + " (mesh)";

            // Retrieve the collider from the mesh child
            Collider collider1 = builtItem.transform.GetChild(0).GetComponent<Collider>();

            // Debug grounding checks
            Debug.Log($"Builder.InstantiateBuildingPrefab(): Is touching buildable layer {IsTouchingBuildableLayer(collider1)}");
            Debug.Log($"Builder.InstantiateBuildingPrefab(): Is touching terrain {IsTouchingAnyTerrain(collider1)}");

            // Determine whether the object is grounded
            bool grounded = IsTouchingAnyTerrain(collider1) || IsTouchingBuildableLayer(collider1);

            if (grounded && _debugEnabled)
                Debug.Log($"Builder.InstantiateBuildingPrefab(): <color=yellow>{builtItem.name} is grounded!</color>");

            ConstructedItem constructedItem;
            int cid = -1;

            // Get or create the BuildingArea for this position
            BuildingArea buildingArea = GetOrCreateBuildingArea(position);
            if (buildingArea != null)
            {
                // Register constructed item in the building area
                constructedItem = buildingArea.AddBuildItem(
                    _buildItemTemplates[index],
                    builtItem,
                    grounded,
                    _debugEnabled
                );

                cid = constructedItem.Id;

                // Rename instance to include its unique ID
                builtItem.name = _buildItemTemplates[index].name + " (" + constructedItem.Id + ")";

                // Store snap point index used
                constructedItem.SnapPointIndex = snapPointIndex;

                // Apply snap point offsets to all submeshes if applicable
                if (constructedItem.ConstructedItemBehaviour != null && snapPointIndex > 0)
                {
                    List<GameObject> submeshes = constructedItem.ConstructedItemBehaviour.MeshObjects;
                    BuildTemplate bt = _buildItemTemplates[constructedItem.BuildTemplateId];

                    for (int i = 0; i < snapPointIndex; i++)
                    {
                        if (i < bt.snapPoints.Count)
                        {
                            for (int j = 0; j < submeshes.Count; j++)
                            {
                                submeshes[j].transform.localPosition += bt.snapPoints[i];
                            }
                        }
                    }
                }

                // Register globally
                _allConstructedItems.Add(constructedItem.Id, constructedItem);
            }
            else
            {
                // Safety check for terrain mismatch or missing building area
                Vector2Int tpos = GetTerrainIndex(position);
                Terrain t = _allTerrains[tpos];

                if (t == _activeTerrain)
                    Debug.LogError("Builder.InstantiateBuildingPrefab(): No BuildingArea created.");
                else
                    Debug.LogError("Builder.InstantiateBuildingPrefab(): crossed into another terrain.");

                return null;
            }

            // --------------------------------------------------
            // Neighbour detection
            // --------------------------------------------------

            // Find nearby colliders within build radius
            Collider[] hitColliders = Physics.OverlapSphere(
                position,
                _buildItemTemplates[index].radius,
                _buildigBlockLayers,
                QueryTriggerInteraction.Ignore
            );

            for (int i = 0; i < hitColliders.Length; i++)
            {
                Collider collider2 = hitColliders[i];

                // Check bounding-box intersection
                if (collider1.bounds.Intersects(collider2.bounds))
                {
                    // Attempt to retrieve neighbour constructed item
                    ConstructedItemBehaviour cib =
                        collider2.gameObject.transform.parent.GetComponent<ConstructedItemBehaviour>();

                    if (cib != null && cib.id != cid)
                    {
                        // Register mutual neighbours
                        constructedItem.AddNeighbour(cib.constructedItem.Id);
                        cib.constructedItem.AddNeighbour(constructedItem.Id);

                        // Update distance propagation if not grounded
                        if (!constructedItem.Grounded)
                        {
                            float distance = cib.constructedItem.Distance + constructedItem.Strength;
                            if (distance < constructedItem.Distance)
                                constructedItem.Distance = distance;
                        }
                    }
                }
            }

            // Visualize distance if debugging
            if (_debugEnabled)
                buildingArea.ShowDistance(constructedItem);

            return builtItem;
        }

        /// <summary>
        /// Checks whether a position overlaps any defined non-buildable area.
        /// </summary>
        public bool IsInNonBuildableArea(Vector3 position)
        {
            bool result = false;
            int i = 0;

            // Iterate through all non-buildable zones
            while (i < _nonBuildableAreas.Count && !result)
            {
                NonBuildableArea nba = _nonBuildableAreas[i];

                // Check for overlaps with transparent block layer
                Collider[] hitColliders = Physics.OverlapSphere(
                    nba.transform.position,
                    nba.Range,
                    _transparentBlockLayer,
                    QueryTriggerInteraction.Ignore
                );

                result = hitColliders.Length > 0;
                i++;
            }

            return result;
        }

        /// <summary>
        /// Pushes terrain downward at the given position.
        /// </summary>
        public void DepressTerrainAtPosition(Vector3 position)
        {
            if (_debugEnabled)
                Debug.Log($"DepressTerrainAtPosition {position}");

            TerrainUtils.TerrainUtils.PushDownTerrain(_activeTerrain, position, 3.0f, 1f);
        }

        /// <summary>
        /// Raises terrain at the given position.
        /// </summary>
        public void RaiseTerrainAtPosition(Vector3 position)
        {
            if (_debugEnabled)
                Debug.Log($"RaiseTerrainAtPosition {position}");

            TerrainUtils.TerrainUtils.PushDownTerrain(_activeTerrain, position, 10.0f, -2f);
        }

        /// <summary>
        /// Saves all BuildingAreas to JSON files.
        /// </summary>
        public void SaveAllBuildingAreas()
        {
            foreach (var area in _buildingAreasById)
            {
                string json = JsonUtility.ToJson(area.Value, true);
                string path = Path.Combine(Application.persistentDataPath, area.Key + ".json");

                File.WriteAllText(path, json);
            }
        }

        /// <summary>
        /// Loads predefined BuildingArea JSON files and reconstructs buildings.
        /// </summary>
        public void LoadBuildingArea()
        {
            string[] files = { "1.json", "2.json", "3.json" };

            foreach (string filename in files)
            {
                string path = Path.Combine(Application.persistentDataPath, filename);
                if (!File.Exists(path))
                    continue;

                Debug.Log($"Builder.LoadBuildingArea(): loading <color=yellow>{filename}</color>");

                string json = File.ReadAllText(path);
                BuildingArea area = JsonUtility.FromJson<BuildingArea>(json);

                _buildingAreasById.Add(area.Id, area);

                // Preserve constructed items before clearing
                ConstructedItem[] constructedItems = area.ConstructedItems;
                area.ClearAllConstructedItems();

                // Re-instantiate all items
                foreach (ConstructedItem c in constructedItems)
                {
                    InstantiateBuildingPrefab(c.BuildTemplateId, c.Position, c.Rotation, 0);
                }
            }
        }

        /// <summary>
        /// Checks whether a magnetic snap point is close to any magnetic collider.
        /// </summary>
        public bool IsMagenticPointClose(GameObject prefabT, out Vector3 distanceClosestPoint)
        {
            // Validate hierarchy
            if (prefabT.transform.childCount > 0)
            {
                GameObject child = prefabT.transform.GetChild(0).gameObject;

                int c = child.transform.childCount;
                for (int i = 0; i < c; i++)
                {
                    GameObject go = child.transform.GetChild(i).gameObject;

                    // Check magnetic overlap
                    Collider[] hitColliders = Physics.OverlapSphere(
                        go.transform.position,
                        _magneticRadius,
                        _magenticLayer,
                        QueryTriggerInteraction.Collide
                    );

                    if (hitColliders.Length > 0)
                    {
                        distanceClosestPoint =
                            go.transform.position - hitColliders[0].transform.position;
                        return true;
                    }
                }
            }

            distanceClosestPoint = Vector3.zero;
            return false;
        }
        
        
    }

}