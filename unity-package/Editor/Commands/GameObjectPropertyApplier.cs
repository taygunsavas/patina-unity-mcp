using System;
using Newtonsoft.Json.Linq;
using UnityEditorInternal;
using UnityEngine;

namespace Patina.Editor.Commands
{
    internal static class GameObjectPropertyApplier
    {
        private const string SupportedFields = "layer, tag, isStatic, name, activeSelf";

        internal static bool IsGameObjectTarget(string componentType)
        {
            return string.IsNullOrEmpty(componentType)
                || string.Equals(componentType, "GameObject", StringComparison.OrdinalIgnoreCase)
                || string.Equals(
                    componentType,
                    "UnityEngine.GameObject",
                    StringComparison.OrdinalIgnoreCase
                );
        }

        internal static void ApplyField(
            GameObject go,
            string fieldName,
            JToken value,
            bool isRoot,
            bool componentTypeEmpty
        )
        {
            switch (fieldName.ToLowerInvariant())
            {
                case "layer":
                    ApplyLayer(go, value);
                    break;
                case "tag":
                    ApplyTag(go, ReadString(value, "tag"));
                    break;
                case "isstatic":
                    ApplyStatic(go, ReadBool(value, "isStatic"));
                    break;
                case "name":
                    if (isRoot)
                        throw new InvalidOperationException(
                            "The prefab root name follows the asset file name and cannot be set. Rename the asset instead."
                        );
                    go.name = ReadString(value, "name");
                    break;
                case "activeself":
                    go.SetActive(ReadBool(value, "activeSelf"));
                    break;
                default:
                    string hint = componentTypeEmpty
                        ? " If this is a component field, set component_type."
                        : string.Empty;
                    throw new ArgumentException(
                        $"Unknown GameObject field '{fieldName}'. Supported fields: {SupportedFields}.{hint}"
                    );
            }
        }

        internal static void ApplyOptional(GameObject go, JObject action)
        {
            JToken layer = action["layer"];
            if (layer != null && layer.Type != JTokenType.Null)
                ApplyLayer(go, layer);

            JToken tag = action["tag"];
            if (tag != null && tag.Type != JTokenType.Null)
                ApplyTag(go, ReadString(tag, "tag"));

            JToken isStatic = action["is_static"];
            if (isStatic != null && isStatic.Type != JTokenType.Null)
                ApplyStatic(go, ReadBool(isStatic, "is_static"));
        }

        internal static void ApplyLayer(GameObject go, JToken value)
        {
            go.layer = ResolveLayer(value);
        }

        internal static int ResolveLayer(JToken value)
        {
            if (value == null || value.Type == JTokenType.Null)
                throw new ArgumentException("layer requires an int (0-31) or a layer name");

            if (value.Type == JTokenType.Integer)
            {
                try
                {
                    return CheckLayerIndex(value.Value<long>());
                }
                catch (OverflowException)
                {
                    throw new ArgumentException("Layer index is out of range, expected 0-31");
                }
            }

            if (value.Type != JTokenType.String)
                throw new ArgumentException("layer requires an int (0-31) or a layer name");

            string layerName = value.Value<string>();
            if (long.TryParse(layerName, out long parsed))
                return CheckLayerIndex(parsed);

            int layerIndex = LayerMask.NameToLayer(layerName);
            if (layerIndex == -1)
                throw new InvalidOperationException(
                    $"Layer '{layerName}' is not defined. Add it in Project Settings > Tags and Layers."
                );
            return layerIndex;
        }

        internal static void ApplyTag(GameObject go, string tag)
        {
            if (Array.IndexOf(InternalEditorUtility.tags, tag) < 0)
                throw new InvalidOperationException(
                    $"Tag '{tag}' is not registered. Add it in Project Settings > Tags and Layers."
                );
            go.tag = tag;
        }

        internal static void ApplyStatic(GameObject go, bool isStatic)
        {
            go.isStatic = isStatic;
        }

        private static int CheckLayerIndex(long index)
        {
            if (index < 0 || index > 31)
                throw new ArgumentException($"Layer index {index} is out of range, expected 0-31");
            return (int)index;
        }

        private static string ReadString(JToken value, string fieldName)
        {
            if (value == null || value.Type != JTokenType.String)
                throw new ArgumentException($"{fieldName} requires a string value");
            string text = value.Value<string>();
            if (string.IsNullOrEmpty(text))
                throw new ArgumentException($"{fieldName} cannot be empty");
            return text;
        }

        private static bool ReadBool(JToken value, string fieldName)
        {
            if (value != null && value.Type == JTokenType.String)
            {
                string text = value.Value<string>();
                if (string.Equals(text, "true", StringComparison.OrdinalIgnoreCase))
                    return true;
                if (string.Equals(text, "false", StringComparison.OrdinalIgnoreCase))
                    return false;
            }
            if (value == null || value.Type != JTokenType.Boolean)
                throw new ArgumentException($"{fieldName} requires a boolean value");
            return value.Value<bool>();
        }
    }
}
