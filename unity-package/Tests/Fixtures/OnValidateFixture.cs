using System.Diagnostics;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace Patina.Editor.Tests.Fixtures
{
    public sealed class OnValidateFixture : MonoBehaviour
    {
        private const string RunnerTypeName = "PrefabOnValidateRunner";

        public int ValidateCount;

        public int Marker;

        public bool InvokedByRunner;

        public bool ThrowOnValidate;

        private void OnValidate()
        {
            if (ThrowOnValidate)
                throw new System.InvalidOperationException("OnValidateFixture refused to validate");
            ValidateCount++;
            MethodBase[] stack = new StackTrace()
                .GetFrames()
                .Select(frame => frame.GetMethod())
                .ToArray();
            InvokedByRunner |= stack.Any(IsRunner) && stack.Any(IsReflectionInvoke);
        }

        private static bool IsRunner(MethodBase method) =>
            method?.DeclaringType?.Name == RunnerTypeName;

        private static bool IsReflectionInvoke(MethodBase method) =>
            method?.Name == "Invoke" && method.DeclaringType?.Namespace == "System.Reflection";
    }
}
