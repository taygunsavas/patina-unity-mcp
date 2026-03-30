using Newtonsoft.Json.Linq;
using System;
using System.Threading.Tasks;
using UnityEditor.Animations;
using UnityEngine;

namespace Patina.Editor.Commands
{
    public sealed class SetAnimatorParameterHandler : ICommandHandler
    {
        public async Task<object> HandleAsync(JObject parameters)
        {
            string gameObjectName = parameters?["game_object"]?.Value<string>();
            string paramName      = parameters?["parameter"]?.Value<string>();
            JToken value          = parameters?["value"];

            if (string.IsNullOrEmpty(gameObjectName))
                throw new ArgumentException("game_object is required");
            if (string.IsNullOrEmpty(paramName))
                throw new ArgumentException("parameter is required");

            string capturedGO    = gameObjectName;
            string capturedParam = paramName;
            JToken capturedValue = value;

            return await MainThreadQueue.EnqueueAsync(() =>
            {
                if (!Application.isPlaying)
                    throw new InvalidOperationException(
                        "set_animator_parameter requires play mode to be active");

                var go = UnityEngine.GameObject.Find(capturedGO);
                if (go == null)
                    throw new InvalidOperationException($"GameObject not found: {capturedGO}");

                var animator = go.GetComponent<Animator>();
                if (animator == null)
                    throw new InvalidOperationException($"'{capturedGO}' has no Animator component");

                // Determine parameter type from the controller
                var controller = animator.runtimeAnimatorController as AnimatorController;
                AnimatorControllerParameterType? paramType = null;
                if (controller != null)
                {
                    foreach (var p in controller.parameters)
                    {
                        if (p.name == capturedParam) { paramType = p.type; break; }
                    }
                }

                if (paramType == null)
                    throw new InvalidOperationException(
                        $"Parameter '{capturedParam}' not found on Animator Controller");

                object appliedValue = capturedValue?.ToObject<object>();

                switch (paramType.Value)
                {
                    case AnimatorControllerParameterType.Float:
                        animator.SetFloat(capturedParam, capturedValue?.Value<float>() ?? 0f);
                        break;
                    case AnimatorControllerParameterType.Int:
                        animator.SetInteger(capturedParam, capturedValue?.Value<int>() ?? 0);
                        break;
                    case AnimatorControllerParameterType.Bool:
                        animator.SetBool(capturedParam, capturedValue?.Value<bool>() ?? false);
                        break;
                    case AnimatorControllerParameterType.Trigger:
                        animator.SetTrigger(capturedParam);
                        appliedValue = "triggered";
                        break;
                }

                return new JObject
                {
                    ["gameObject"] = capturedGO,
                    ["parameter"]  = capturedParam,
                    ["value"]      = appliedValue?.ToString() ?? "",
                    ["success"]    = true
                };
            });
        }
    }
}
