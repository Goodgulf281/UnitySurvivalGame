using UnityEditor;
using UnityEngine;

namespace Goodgulf.Editor
{

    public static class LayerCreator
    {
        /// <summary>
        /// Creates a layer if it does not already exist.
        /// </summary>
        public static void CreateLayer(string layerName)
        {
            if (LayerMask.NameToLayer(layerName) != -1)
            {
                Debug.Log($"Layer '{layerName}' already exists.");
                return;
            }

            SerializedObject tagManager =
                new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);

            SerializedProperty layersProp = tagManager.FindProperty("layers");

            // Unity reserves layers 0–7
            for (int i = 8; i < layersProp.arraySize; i++)
            {
                SerializedProperty layerProp = layersProp.GetArrayElementAtIndex(i);

                if (string.IsNullOrEmpty(layerProp.stringValue))
                {
                    layerProp.stringValue = layerName;
                    tagManager.ApplyModifiedProperties();
                    Debug.Log($"Created layer '{layerName}'");
                    return;
                }
            }

            Debug.LogError("No empty layer slots available.");
        }
        
        private static void CreateTag(string tagName)
        {
            if (TagExists(tagName))
                return;

            SerializedObject tagManager =
                new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);

            SerializedProperty tagsProp = tagManager.FindProperty("tags");

            tagsProp.InsertArrayElementAtIndex(tagsProp.arraySize);
            SerializedProperty newTag = tagsProp.GetArrayElementAtIndex(tagsProp.arraySize - 1);
            newTag.stringValue = tagName;

            tagManager.ApplyModifiedProperties();

            Debug.Log($"Created tag '{tagName}'");
        }

        /// <summary>
        /// Returns true if the tag already exists in the project.
        /// </summary>
        public static bool TagExists(string tagName)
        {
            SerializedObject tagManager =
                new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);

            SerializedProperty tagsProp = tagManager.FindProperty("tags");

            for (int i = 0; i < tagsProp.arraySize; i++)
            {
                SerializedProperty tagProp = tagsProp.GetArrayElementAtIndex(i);
                if (tagProp.stringValue == tagName)
                    return true;
            }

            return false;
        }
        
        [MenuItem("Tools/Create Custom Layers")]
        private static void CreateMyLayers()
        {
            CreateLayer("Player");
            CreateLayer("BuildItemTransparent");
            CreateLayer("Terrain");
            CreateLayer("Buildable");
            CreateLayer("Magnetic");
            
            CreateTag("BuildItemMesh");
            CreateTag("BuildItemDebug");
            
        }
    }
    

    

}