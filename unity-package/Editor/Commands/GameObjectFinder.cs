using UnityEditor;
using UnityEngine;

namespace Patina.Editor.Commands
{
    internal static class GameObjectFinder
    {
        /// <summary>
        /// Finds a scene GameObject by instanceId (preferred) or by name.
        /// Unlike GameObject.Find, this locates inactive objects and objects
        /// that have not yet been indexed after instantiation.
        /// </summary>
        internal static GameObject Find(string name, int instanceId = 0)
        {
            if (instanceId != 0)
            {
                var obj = EditorUtility.EntityIdToObject(instanceId) as GameObject;
                if (obj != null && obj.scene.IsValid())
                    return obj;
            }

            foreach (
                GameObject go in UnityEngine.Object.FindObjectsByType<GameObject>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None
                )
            )
            {
                if (go == null)
                    continue;
                if (go.hideFlags != HideFlags.None)
                    continue;
                if (go.scene.IsValid() && go.name == name)
                    return go;
            }

            return null;
        }

        /// <summary>
        /// Computes the full scene hierarchy path for a GameObject.
        /// Returns a leading-slash path such as "/Canvas/HUD/HealthBar".
        /// </summary>
        internal static string GetScenePath(GameObject go)
        {
            if (go == null)
                return string.Empty;
            var parts = new System.Collections.Generic.List<string>();
            Transform t = go.transform;
            while (t != null)
            {
                parts.Insert(0, t.name);
                t = t.parent;
            }
            return "/" + string.Join("/", parts);
        }
    }
}
