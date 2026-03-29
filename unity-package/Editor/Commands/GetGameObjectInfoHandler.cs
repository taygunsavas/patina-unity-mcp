using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Patina.Editor.Commands
{
    public sealed class GetGameObjectInfoHandler : ICommandHandler
    {
        public async Task<object> HandleAsync(JObject parameters)
        {
            string gameObjectName = parameters?["game_object_name"]?.Value<string>();
            bool includeProps = parameters?["include_component_properties"]?.Value<bool>() ?? true;

            if (string.IsNullOrEmpty(gameObjectName))
                throw new ArgumentException("game_object_name is required");

            string capturedName = gameObjectName;
            bool capturedInclude = includeProps;

            JObject result = await MainThreadQueue.EnqueueAsync(() =>
            {
                GameObject go = GameObject.Find(capturedName);
                if (go == null)
                    throw new InvalidOperationException($"GameObject '{capturedName}' not found");

                var info = new JObject
                {
                    ["name"] = go.name,
                    ["instanceId"] = go.GetInstanceID(),
                    ["activeSelf"] = go.activeSelf,
                    ["activeInHierarchy"] = go.activeInHierarchy,
                    ["tag"] = go.tag,
                    ["layer"] = go.layer,
                    ["layerName"] = LayerMask.LayerToName(go.layer),
                    ["isStatic"] = go.isStatic,
                    ["scenePath"] = BuildScenePath(go.transform),
                    ["transform"] = new JObject
                    {
                        ["position"] = Vec3Json(go.transform.position),
                        ["rotation"] = Vec3Json(go.transform.eulerAngles),
                        ["localScale"] = Vec3Json(go.transform.localScale)
                    }
                };

                var components = new JArray();
                foreach (Component comp in go.GetComponents<Component>())
                {
                    if (comp == null) continue;
                    var compObj = new JObject
                    {
                        ["type"] = comp.GetType().Name,
                        ["instanceId"] = comp.GetInstanceID()
                    };
                    if (capturedInclude)
                        compObj["properties"] = SerializeProperties(comp);
                    components.Add(compObj);
                }
                info["components"] = components;

                return info;
            });

            return result;
        }

        private static string BuildScenePath(Transform t)
        {
            var parts = new List<string>();
            Transform current = t;
            while (current != null)
            {
                parts.Add(current.name);
                current = current.parent;
            }
            parts.Reverse();
            return "/" + string.Join("/", parts);
        }

        private static JArray Vec3Json(Vector3 v) => new JArray(v.x, v.y, v.z);

        private static JObject SerializeProperties(Component comp)
        {
            var props = new JObject();
            SerializedObject so;
            try { so = new SerializedObject(comp); }
            catch (Exception) { return props; }

            SerializedProperty sp;
            try { sp = so.GetIterator(); }
            catch (Exception) { return props; }

            bool enter = true;
            while (true)
            {
                bool hasNext;
                try { hasNext = sp.Next(enter); }
                catch (Exception) { break; }
                if (!hasNext) break;

                try
                {
                    JToken val = ToJson(sp);
                    if (val != null)
                        props[sp.propertyPath] = val;
                }
                catch (Exception) { /* skip this property */ }

                try { enter = sp.depth < 3 && !(sp.isArray && sp.arraySize > 32); }
                catch (Exception) { enter = false; }
            }
            return props;
        }

        private static JToken ToJson(SerializedProperty sp)
        {
            switch (sp.propertyType)
            {
                case SerializedPropertyType.Integer:    return sp.intValue;
                case SerializedPropertyType.Float:      return sp.floatValue;
                case SerializedPropertyType.Boolean:    return sp.boolValue;
                case SerializedPropertyType.String:     return sp.stringValue;
                case SerializedPropertyType.Enum:
                    return (sp.enumValueIndex >= 0 && sp.enumValueIndex < sp.enumDisplayNames.Length)
                        ? (JToken)sp.enumDisplayNames[sp.enumValueIndex]
                        : sp.enumValueIndex;
                case SerializedPropertyType.Color:
                    Color c = sp.colorValue;
                    return new JArray(c.r, c.g, c.b, c.a);
                case SerializedPropertyType.Vector2:
                    Vector2 v2 = sp.vector2Value;
                    return new JArray(v2.x, v2.y);
                case SerializedPropertyType.Vector3:
                    Vector3 v3 = sp.vector3Value;
                    return new JArray(v3.x, v3.y, v3.z);
                case SerializedPropertyType.Vector4:
                    Vector4 v4 = sp.vector4Value;
                    return new JArray(v4.x, v4.y, v4.z, v4.w);
                case SerializedPropertyType.Quaternion:
                    Quaternion q = sp.quaternionValue;
                    return new JArray(q.x, q.y, q.z, q.w);
                case SerializedPropertyType.Rect:
                    Rect r = sp.rectValue;
                    return new JArray(r.x, r.y, r.width, r.height);
                case SerializedPropertyType.ObjectReference:
                    return sp.objectReferenceValue != null ? (JToken)sp.objectReferenceValue.name : JValue.CreateNull();
                case SerializedPropertyType.LayerMask:
                    return sp.intValue;
                default:
                    return null;
            }
        }
    }
}
