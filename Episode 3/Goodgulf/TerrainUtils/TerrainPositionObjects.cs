using System.Collections.Generic;
using Goodgulf.Controller;
using UnityEngine;

namespace Goodgulf.TerrainUtils
{


    public class TerrainPositionObjects : MonoBehaviour
    {
        public List<GameObject> objectsToBePlacedOnTerrain;
        public LayerMask layerMask;
        
        void Start()
        {
            // Invoke("RePosition", 0.2f);
        }

        public void RePosition()
        {
            foreach (GameObject obj in objectsToBePlacedOnTerrain)
            {
                
                Debug.Log($"Placing object {obj.name} with position {obj.transform.position}");
                
                Vector3 pos = obj.transform.position;
                pos.y += 5000.0f;

                RaycastHit hit;
                if (Physics.Raycast(pos, Vector3.down, out hit, Mathf.Infinity, layerMask, QueryTriggerInteraction.Ignore))
                {
                    Debug.Log($"Place object {obj.name} at {hit.point}");
                    
                    if (obj.TryGetComponent<ThirdPersonController>(out ThirdPersonController controller))
                    {
                        controller.TeleportCharacter(hit.point);
                    }
                    else obj.transform.position = hit.point;
                }
                else Debug.Log($"Cannot place object {obj.name}");
            }
        }
        
    }


}
