using System;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Patina.Editor.Commands
{
    internal sealed class PrefabOnValidateRunner
    {
        private readonly string _rootName;
        private readonly HashSet<(string Path, Type ComponentType)> _touched =
            new HashSet<(string, Type)>();

        public PrefabOnValidateRunner(string rootName)
        {
            _rootName = rootName;
        }

        public void Track(string transformPath, Type componentType)
        {
            _touched.Add((Canonicalize(transformPath), componentType));
        }

        public void Untrack(string transformPath, Type componentType)
        {
            _touched.Remove((Canonicalize(transformPath), componentType));
        }

        public void RemoveSubtree(string transformPath)
        {
            string prefix = Canonicalize(transformPath);
            _touched.RemoveWhere(entry => IsSameOrDescendant(entry.Path, prefix));
        }

        public void Rename(string transformPath, string newName)
        {
            string oldPrefix = Canonicalize(transformPath);
            int lastSeparator = oldPrefix.LastIndexOf('/');
            string newPrefix =
                lastSeparator < 0 ? newName : oldPrefix.Substring(0, lastSeparator + 1) + newName;

            var renamed = new List<(string Path, Type ComponentType)>();
            foreach ((string path, Type componentType) in _touched)
            {
                renamed.Add(
                    IsSameOrDescendant(path, oldPrefix)
                        ? (newPrefix + path.Substring(oldPrefix.Length), componentType)
                        : (path, componentType)
                );
            }
            _touched.Clear();
            _touched.UnionWith(renamed);
        }

        public JArray Run(string assetPath)
        {
            var warnings = new JArray();
            if (_touched.Count == 0)
                return warnings;

            GameObject savedRoot = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (savedRoot == null)
            {
                warnings.Add($"OnValidate skipped: saved prefab not found at '{assetPath}'");
                return warnings;
            }

            bool anyInvoked = false;
            foreach ((string path, Type componentType) in _touched)
            {
                GameObject target = ObjectReferenceResolver.FindByPath(savedRoot, path);
                if (target == null)
                {
                    warnings.Add(
                        $"OnValidate skipped for '{componentType.FullName}': path '{path}' not found in saved prefab"
                    );
                    continue;
                }

                Component[] components = target.GetComponents(componentType);
                if (components.Length == 0)
                {
                    warnings.Add(
                        $"OnValidate skipped for '{componentType.FullName}': component not found at '{path}' in saved prefab"
                    );
                    continue;
                }

                MethodInfo onValidate = FindOnValidate(componentType);
                if (onValidate == null)
                    continue;

                foreach (Component component in components)
                {
                    try
                    {
                        onValidate.Invoke(component, null);
                        EditorUtility.SetDirty(component);
                        anyInvoked = true;
                    }
                    catch (Exception ex)
                    {
                        Exception cause = ex.InnerException ?? ex;
                        warnings.Add(
                            $"OnValidate failed on '{componentType.FullName}' at '{path}': {cause.Message}"
                        );
                    }
                }
            }

            if (anyInvoked)
            {
                EditorUtility.SetDirty(savedRoot);
                AssetDatabase.SaveAssets();
            }
            return warnings;
        }

        private string Canonicalize(string transformPath)
        {
            if (string.IsNullOrEmpty(transformPath) || transformPath == ".")
                return string.Empty;

            var segments = new List<string>(transformPath.Split('/'));
            segments.RemoveAll(string.IsNullOrEmpty);
            if (segments.Count > 0 && segments[0] == _rootName)
                segments.RemoveAt(0);
            return string.Join("/", segments);
        }

        private static bool IsSameOrDescendant(string path, string prefix) =>
            prefix.Length == 0
            || path == prefix
            || path.StartsWith(prefix + "/", StringComparison.Ordinal);

        private static MethodInfo FindOnValidate(Type type)
        {
            for (Type current = type; current != null; current = current.BaseType)
            {
                MethodInfo method = current.GetMethod(
                    "OnValidate",
                    BindingFlags.Instance
                        | BindingFlags.Public
                        | BindingFlags.NonPublic
                        | BindingFlags.DeclaredOnly,
                    null,
                    Type.EmptyTypes,
                    null
                );
                if (method != null)
                    return method;
            }
            return null;
        }
    }
}
