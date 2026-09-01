using System;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Patina.Editor.Commands
{
    internal sealed class ObjectReferenceContext
    {
        internal GameObject SearchRoot { get; set; }
        internal string SelfAssetPath { get; set; }
        internal bool AllowSceneObjects { get; set; }
    }

    internal static class ObjectReferenceResolver
    {
        internal static bool TryResolve(
            JToken token,
            Type expectedType,
            ObjectReferenceContext context,
            out UnityEngine.Object value,
            out string error
        )
        {
            value = null;
            error = null;
            context ??= new ObjectReferenceContext();

            try
            {
                if (token == null || token.Type == JTokenType.Null)
                {
                    value = null;
                    return true;
                }

                UnityEngine.Object resolved;
                string resolveError;

                if (token.Type == JTokenType.String)
                {
                    if (
                        !TryResolveFromString(
                            token.Value<string>(),
                            context,
                            out resolved,
                            out resolveError
                        )
                    )
                    {
                        error = FormatError(token, expectedType, resolveError, context);
                        return false;
                    }
                }
                else if (token.Type == JTokenType.Integer)
                {
                    if (
                        !TryResolveFromInstanceId(
                            token.Value<int>(),
                            context,
                            out resolved,
                            out resolveError
                        )
                    )
                    {
                        error = FormatError(token, expectedType, resolveError, context);
                        return false;
                    }
                }
                else if (token is JObject jObject)
                {
                    if (!TryResolveFromObject(jObject, context, out resolved, out resolveError))
                    {
                        error = FormatError(token, expectedType, resolveError, context);
                        return false;
                    }
                }
                else
                {
                    error = FormatError(
                        token,
                        expectedType,
                        $"unsupported JSON token type '{token.Type}'",
                        context
                    );
                    return false;
                }

                if (resolved == null)
                {
                    value = null;
                    return true;
                }

                if (
                    !TryNarrowToExpectedType(
                        resolved,
                        expectedType,
                        null,
                        out UnityEngine.Object narrowed,
                        out string narrowError
                    )
                )
                {
                    error = FormatError(token, expectedType, narrowError, context);
                    return false;
                }

                value = narrowed;
                return true;
            }
            catch (Exception ex)
            {
                error = FormatError(token, expectedType, ex.Message, context);
                return false;
            }
        }

        private static bool TryResolveFromString(
            string text,
            ObjectReferenceContext context,
            out UnityEngine.Object resolved,
            out string error
        )
        {
            resolved = null;
            error = null;

            if (text != null && (text.StartsWith("Assets/") || text.StartsWith("Packages/")))
            {
                if (IsSameAsset(text, context.SelfAssetPath))
                {
                    error = SelfReferenceMessage();
                    return false;
                }

                UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(text);
                if (asset == null)
                {
                    error = $"asset path '{text}' did not resolve to an asset";
                    return false;
                }
                resolved = asset;
                return true;
            }

            if (IsGuid(text))
            {
                string path = AssetDatabase.GUIDToAssetPath(text);
                if (string.IsNullOrEmpty(path))
                {
                    if (context.SearchRoot != null)
                    {
                        GameObject byPath = FindByPath(context.SearchRoot, text);
                        if (byPath != null)
                        {
                            resolved = byPath;
                            return true;
                        }

                        error =
                            $"'{text}' was interpreted as a GUID first (not found in project), then as a transform path ({TransformPathNotFoundMessage(context.SearchRoot, text)})";
                        return false;
                    }

                    error = $"GUID '{text}' not found in project";
                    return false;
                }

                if (IsSameAsset(path, context.SelfAssetPath))
                {
                    error = SelfReferenceMessage();
                    return false;
                }

                UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
                if (asset == null)
                {
                    error = $"GUID '{text}' resolved to path '{path}' but no asset was found there";
                    return false;
                }
                resolved = asset;
                return true;
            }

            if (context.SearchRoot == null)
            {
                error =
                    "value looks like a transform path but no search root is available in this context";
                return false;
            }

            GameObject found = FindByPath(context.SearchRoot, text);
            if (found == null)
            {
                error = TransformPathNotFoundMessage(context.SearchRoot, text);
                return false;
            }

            resolved = found;
            return true;
        }

        private static bool TryResolveFromInstanceId(
            int instanceId,
            ObjectReferenceContext context,
            out UnityEngine.Object resolved,
            out string error
        )
        {
            resolved = null;
            error = null;

            UnityEngine.Object obj = EditorUtility.EntityIdToObject(instanceId);
            if (obj == null)
            {
                error = $"instance_id {instanceId} did not resolve to any live object";
                return false;
            }

            GameObject ownerGo = obj as GameObject;
            if (ownerGo == null && obj is Component ownerComp)
                ownerGo = ownerComp.gameObject;

            if (ownerGo != null)
            {
                if (context.SearchRoot != null && IsWithinHierarchy(ownerGo, context.SearchRoot))
                {
                    resolved = obj;
                    return true;
                }

                if (ownerGo.scene.IsValid())
                {
                    if (!context.AllowSceneObjects)
                    {
                        error =
                            $"instance_id {instanceId} resolves to a scene object but scene objects are not allowed here";
                        return false;
                    }
                    resolved = obj;
                    return true;
                }
            }

            string assetPath = AssetDatabase.GetAssetPath(obj);
            if (!string.IsNullOrEmpty(assetPath) && IsSameAsset(assetPath, context.SelfAssetPath))
            {
                string suggestion = BuildSelfInstanceSuggestion(ownerGo, obj, context);
                error =
                    $"instance_id {instanceId} belongs to the prefab asset being edited (stale id from list_prefab_components), not to the in-memory copy being edited by this action. {suggestion}";
                return false;
            }

            if (!string.IsNullOrEmpty(assetPath))
            {
                resolved = obj;
                return true;
            }

            error =
                $"instance_id {instanceId} could not be classified as belonging to the edited hierarchy, the scene, or a project asset";
            return false;
        }

        private static bool TryResolveFromObject(
            JObject obj,
            ObjectReferenceContext context,
            out UnityEngine.Object resolved,
            out string error
        )
        {
            resolved = null;
            error = null;

            string transformPath = ReadAlias(obj, "transform_path", "transformPath");
            string assetPath = ReadAlias(obj, "asset_path", "assetPath");
            string guid = ReadAlias(obj, "guid", "guid");
            string instanceIdRaw = ReadAlias(obj, "instance_id", "instanceId");
            string componentType = ReadAlias(obj, "component_type", "componentType");

            var recognizedKeys = new[]
            {
                "transform_path",
                "transformPath",
                "asset_path",
                "assetPath",
                "guid",
                "component_type",
                "componentType",
                "instance_id",
                "instanceId",
            };

            string unknownKey = obj.Properties()
                .Select(p => p.Name)
                .FirstOrDefault(name => !recognizedKeys.Contains(name));
            if (unknownKey != null)
            {
                error = $"unknown key '{unknownKey}' in object reference descriptor";
                return false;
            }

            int primaryCount = 0;
            if (transformPath != null)
                primaryCount++;
            if (assetPath != null)
                primaryCount++;
            if (guid != null)
                primaryCount++;
            if (instanceIdRaw != null)
                primaryCount++;

            if (primaryCount == 0)
            {
                error =
                    "object reference descriptor must contain exactly one of: transform_path, asset_path, guid, instance_id";
                return false;
            }
            if (primaryCount > 1)
            {
                error =
                    "object reference descriptor must contain exactly one of: transform_path, asset_path, guid, instance_id, but more than one was given";
                return false;
            }

            UnityEngine.Object candidate;

            if (transformPath != null)
            {
                if (context.SearchRoot == null)
                {
                    error =
                        "transform_path was given but no search root is available in this context";
                    return false;
                }
                GameObject found = FindByPath(context.SearchRoot, transformPath);
                if (found == null)
                {
                    error = TransformPathNotFoundMessage(context.SearchRoot, transformPath);
                    return false;
                }
                candidate = found;
            }
            else if (assetPath != null)
            {
                if (IsSameAsset(assetPath, context.SelfAssetPath))
                {
                    error = SelfReferenceMessage();
                    return false;
                }
                UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(
                    assetPath
                );
                if (asset == null)
                {
                    error = $"asset_path '{assetPath}' did not resolve to an asset";
                    return false;
                }
                candidate = asset;
            }
            else if (guid != null)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path))
                {
                    error = $"guid '{guid}' not found in project";
                    return false;
                }
                if (IsSameAsset(path, context.SelfAssetPath))
                {
                    error = SelfReferenceMessage();
                    return false;
                }
                UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
                if (asset == null)
                {
                    error = $"guid '{guid}' resolved to path '{path}' but no asset was found there";
                    return false;
                }
                candidate = asset;
            }
            else
            {
                if (!int.TryParse(instanceIdRaw, out int instanceId))
                {
                    error = $"instance_id '{instanceIdRaw}' is not a valid integer";
                    return false;
                }
                if (
                    !TryResolveFromInstanceId(
                        instanceId,
                        context,
                        out candidate,
                        out string instanceError
                    )
                )
                {
                    error = instanceError;
                    return false;
                }
                if (!string.IsNullOrEmpty(componentType))
                {
                    error = "component_type is only meaningful with transform_path or asset_path";
                    return false;
                }
                resolved = candidate;
                return true;
            }

            if (!string.IsNullOrEmpty(componentType) && guid != null)
            {
                error = "component_type is only meaningful with transform_path or asset_path";
                return false;
            }

            if (!string.IsNullOrEmpty(componentType))
            {
                Type hint = AddComponentHandler.FindType(componentType);
                if (hint == null)
                {
                    error = $"component_type '{componentType}' could not be resolved to a type";
                    return false;
                }

                if (
                    !TryNarrowToExpectedType(
                        candidate,
                        null,
                        hint,
                        out UnityEngine.Object narrowed,
                        out string narrowError
                    )
                )
                {
                    error = narrowError;
                    return false;
                }
                resolved = narrowed;
                return true;
            }

            resolved = candidate;
            return true;
        }

        private static bool TryNarrowToExpectedType(
            UnityEngine.Object resolved,
            Type expectedType,
            Type componentHint,
            out UnityEngine.Object value,
            out string error
        )
        {
            value = null;
            error = null;

            Type targetType = componentHint ?? expectedType;

            if (resolved is GameObject go)
            {
                if (
                    targetType == null
                    || targetType == typeof(GameObject)
                    || targetType == typeof(UnityEngine.Object)
                )
                {
                    value = go;
                }
                else if (typeof(Component).IsAssignableFrom(targetType) || targetType.IsInterface)
                {
                    Component comp = go.GetComponent(targetType);
                    if (comp == null)
                    {
                        error =
                            $"GameObject '{go.name}' has no component of type '{targetType.FullName}'";
                        return false;
                    }
                    value = comp;
                }
                else
                {
                    value = go;
                }
            }
            else if (resolved is Component component)
            {
                if (targetType == typeof(GameObject))
                {
                    value = component.gameObject;
                }
                else
                {
                    value = component;
                }
            }
            else
            {
                value = resolved;
            }

            if (expectedType != null && !expectedType.IsInstanceOfType(value))
            {
                error =
                    $"field expects {expectedType.FullName}, resolved object is {value.GetType().FullName}";
                return false;
            }

            return true;
        }

        internal static string AcceptedFormats(ObjectReferenceContext context)
        {
            var sb = new StringBuilder(
                "Accepted forms: null; \"Assets/...\" asset path; 32-char GUID; "
            );
            if (context?.SearchRoot != null)
            {
                string rootLabel = context.SelfAssetPath != null ? "prefab root" : "scene root";
                sb.Append($"\"Child/Grandchild\" transform path relative to the {rootLabel}; ");
                sb.Append("{\"transform_path\":\"Child\",\"component_type\":\"Ns.Type\"}; ");
            }
            sb.Append("{\"asset_path\":\"...\"}; {\"guid\":\"...\"}; {\"instance_id\":123}.");
            return sb.ToString();
        }

        internal static void EnsureScalar(JToken token, string what)
        {
            if (token is JObject || token is JArray)
            {
                throw new ArgumentException(
                    $"{what} expects a scalar JSON value, got {(token is JObject ? "Object" : "Array")}. Provide a plain number, string, or boolean instead."
                );
            }
        }

        internal static GameObject FindByPath(GameObject root, string path)
        {
            if (root == null)
                return null;

            if (string.IsNullOrEmpty(path) || path == "/" || path == ".")
                return root;

            string[] parts = path.Split('/');
            int startIndex = 0;
            if (parts.Length > 0 && parts[0] == root.name)
                startIndex = 1;

            Transform current = root.transform;
            for (int i = startIndex; i < parts.Length; i++)
            {
                string part = parts[i];
                if (string.IsNullOrEmpty(part))
                    continue;
                Transform child = current.Find(part);
                if (child == null)
                    return null;
                current = child;
            }
            return current.gameObject;
        }

        private static string TransformPathNotFoundMessage(GameObject root, string path)
        {
            GameObject resolvedParent = FindDeepestResolvableParent(
                root,
                path,
                out string[] childNames
            );
            string childList =
                childNames.Length > 0 ? string.Join(", ", childNames.Take(20)) : "(no children)";
            string parentName = resolvedParent != null ? resolvedParent.name : "(root)";
            return $"transform path '{path}' not found under '{root.name}'. Children of '{parentName}': {childList}";
        }

        private static GameObject FindDeepestResolvableParent(
            GameObject root,
            string path,
            out string[] childNames
        )
        {
            childNames = Array.Empty<string>();
            if (root == null)
                return null;

            string[] parts = string.IsNullOrEmpty(path) ? Array.Empty<string>() : path.Split('/');
            int startIndex = 0;
            if (parts.Length > 0 && parts[0] == root.name)
                startIndex = 1;

            Transform current = root.transform;
            for (int i = startIndex; i < parts.Length; i++)
            {
                string part = parts[i];
                if (string.IsNullOrEmpty(part))
                    continue;
                Transform child = current.Find(part);
                if (child == null)
                {
                    childNames = GetChildNames(current);
                    return current.gameObject;
                }
                current = child;
            }

            childNames = GetChildNames(current);
            return current.gameObject;
        }

        private static string[] GetChildNames(Transform t)
        {
            var names = new string[t.childCount];
            for (int i = 0; i < t.childCount; i++)
                names[i] = t.GetChild(i).name;
            return names;
        }

        private static bool IsWithinHierarchy(GameObject candidate, GameObject root)
        {
            if (candidate == null || root == null)
                return false;
            Transform t = candidate.transform;
            while (t != null)
            {
                if (t.gameObject == root)
                    return true;
                t = t.parent;
            }
            return false;
        }

        private static string BuildSelfInstanceSuggestion(
            GameObject ownerGo,
            UnityEngine.Object obj,
            ObjectReferenceContext context
        )
        {
            if (context?.SearchRoot == null || ownerGo == null)
                return "Re-resolve the reference using transform_path or asset_path instead of instance_id.";

            string path = GetPathFromTopmostAncestor(ownerGo);
            string componentSuggestion = obj is Component comp
                ? $", \"component_type\":\"{comp.GetType().FullName}\""
                : string.Empty;
            return $"Use {{\"transform_path\":\"{path}\"{componentSuggestion}}} instead, resolved against the prefab hierarchy currently being edited.";
        }

        private static string GetPathFromTopmostAncestor(GameObject go)
        {
            var parts = new System.Collections.Generic.List<string>();
            Transform t = go.transform;
            while (t != null)
            {
                parts.Insert(0, t.name);
                t = t.parent;
            }
            return string.Join("/", parts);
        }

        private static bool IsSameAsset(string pathA, string pathB)
        {
            if (string.IsNullOrEmpty(pathA) || string.IsNullOrEmpty(pathB))
                return false;
            return string.Equals(
                pathA.Replace('\\', '/'),
                pathB.Replace('\\', '/'),
                StringComparison.OrdinalIgnoreCase
            );
        }

        private static string SelfReferenceMessage()
        {
            return "reference points at the same prefab asset currently being edited. Writing an asset-level PPtr to the prefab's own asset creates a self-reference once saved. Use {\"transform_path\":\"\"} for the prefab root or {\"transform_path\":\"Child\"} for a child instead.";
        }

        private static bool IsGuid(string text)
        {
            if (string.IsNullOrEmpty(text) || text.Length != 32)
                return false;
            foreach (char c in text)
            {
                bool isHex =
                    (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if (!isHex)
                    return false;
            }
            return true;
        }

        private static string ReadAlias(JObject obj, string primaryKey, string aliasKey)
        {
            JToken token = obj[primaryKey] ?? (aliasKey != primaryKey ? obj[aliasKey] : null);
            return token?.Type == JTokenType.Null ? null : token?.ToString();
        }

        private static string FormatError(
            JToken token,
            Type expectedType,
            string reason,
            ObjectReferenceContext context
        )
        {
            string typeName = expectedType != null ? expectedType.FullName : "unknown";
            string tokenSummary = SummarizeToken(token);
            string trimmedReason = reason != null ? reason.TrimEnd('.', ' ') : reason;
            return $"Could not resolve object reference for value ({typeName}) from {tokenSummary}: {trimmedReason}. {AcceptedFormats(context)}";
        }

        private static string SummarizeToken(JToken token)
        {
            if (token == null)
                return "null";
            switch (token.Type)
            {
                case JTokenType.Null:
                    return "null";
                case JTokenType.String:
                    return $"\"{token.Value<string>()}\"";
                case JTokenType.Object:
                    return token.ToString(Newtonsoft.Json.Formatting.None);
                case JTokenType.Array:
                    return token.ToString(Newtonsoft.Json.Formatting.None);
                default:
                    return token.ToString();
            }
        }
    }
}
