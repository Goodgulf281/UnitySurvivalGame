using System.Collections.Generic;
using UnityEngine;

namespace Goodgulf.TerrainUtils
{


    public class TerrainPositionObjects : MonoBehaviour
    {
        public List<GameObject> objectsToBePlacedOnTerrain;
        public LayerMask layerMask;
        
        void Awake()
        {
            foreach (GameObject obj in objectsToBePlacedOnTerrain)
            {
                Debug.Log($"Placing object {obj.name} with position {obj.transform.position}");
                
                Vector3 pos = obj.transform.position;
                pos.y += 2000.0f;

                RaycastHit hit;
                if (Physics.Raycast(pos, Vector3.down, out hit, Mathf.Infinity, layerMask, QueryTriggerInteraction.Ignore))
                {
                    Debug.Log($"Place object {obj.name} at {hit.point}");
                    obj.transform.position = hit.point;
                }
            }
        }
    }


}
