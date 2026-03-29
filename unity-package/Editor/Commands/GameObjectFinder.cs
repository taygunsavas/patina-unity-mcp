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
        /// <param name="name">GameObject name (used when instanceId is 0 or lookup by id fails).</param>
        /// <param name="instanceId">Optional instance ID returned by a prior create/instantiate call.</param>
        /// <returns>The matching scene GameObject, or null if not found.</returns>
        internal static GameObject Find(string name, int instanceId = 0)
        {
            if (instanceId != 0)
            {
                var obj = EditorUtility.InstanceIDToObject(instanceId) as GameObject;
                if (obj != null && obj.scene.IsValid())
                    return obj;
            }

            foreach (GameObject go in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (go == null) continue;
                if (go.hideFlags != HideFlags.None) continue;
                if (go.scene.IsValid() && go.name == name)
                    return go;
            }

            return null;
        }
    }
}
