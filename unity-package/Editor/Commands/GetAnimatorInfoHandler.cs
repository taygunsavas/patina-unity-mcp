using Newtonsoft.Json.Linq;
using System;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Patina.Editor.Commands
{
    public sealed class GetAnimatorInfoHandler : ICommandHandler
    {
        public async Task<object> HandleAsync(JObject parameters)
        {
            string gameObjectName = parameters?["game_object"]?.Value<string>();
            int maxStates = parameters?["max_states"]?.Value<int>() ?? 50;

            if (string.IsNullOrEmpty(gameObjectName))
                throw new ArgumentException("game_object is required");

            string capturedName = gameObjectName;
            int capturedMax     = maxStates;

            return await MainThreadQueue.EnqueueAsync(() =>
            {
                var go = UnityEngine.GameObject.Find(capturedName);
                if (go == null)
                    throw new InvalidOperationException($"GameObject not found: {capturedName}");

                var animator = go.GetComponent<Animator>();
                if (animator == null)
                    throw new InvalidOperationException($"'{capturedName}' has no Animator component");

                var controller = animator.runtimeAnimatorController as AnimatorController;
                if (controller == null)
                    throw new InvalidOperationException(
                        $"Animator on '{capturedName}' has no AnimatorController assigned");

                // Parameters
                var paramsArr = new JArray();
                foreach (var p in controller.parameters)
                {
                    paramsArr.Add(new JObject
                    {
                        ["name"]         = p.name,
                        ["type"]         = p.type.ToString(),
                        ["defaultValue"] = GetDefaultValue(p)
                    });
                }

                // Layers + states
                var layersArr = new JArray();
                foreach (var layer in controller.layers)
                {
                    var statesArr = new JArray();
                    int stateCount = 0;

                    foreach (var state in layer.stateMachine.states)
                    {
                        if (stateCount >= capturedMax) break;
                        stateCount++;

                        var transArr = new JArray();
                        foreach (var t in state.state.transitions)
                        {
                            transArr.Add(new JObject
                            {
                                ["destinationState"] = t.destinationState != null
                                    ? t.destinationState.name : null,
                                ["hasExitTime"] = t.hasExitTime,
                                ["exitTime"]    = t.exitTime
                            });
                        }

                        statesArr.Add(new JObject
                        {
                            ["name"]        = state.state.name,
                            ["motion"]      = state.state.motion != null ? state.state.motion.name : null,
                            ["transitions"] = transArr
                        });
                    }

                    bool truncated = layer.stateMachine.states.Length > capturedMax;

                    layersArr.Add(new JObject
                    {
                        ["name"]      = layer.name,
                        ["truncated"] = truncated,
                        ["states"]    = statesArr
                    });
                }

                return new JObject
                {
                    ["gameObject"]     = capturedName,
                    ["controllerPath"] = AssetDatabase.GetAssetPath(controller),
                    ["parameters"]     = paramsArr,
                    ["layers"]         = layersArr
                };
            });
        }

        private static JToken GetDefaultValue(AnimatorControllerParameter p)
        {
            switch (p.type)
            {
                case AnimatorControllerParameterType.Float:   return p.defaultFloat;
                case AnimatorControllerParameterType.Int:     return p.defaultInt;
                case AnimatorControllerParameterType.Bool:    return p.defaultBool;
                case AnimatorControllerParameterType.Trigger: return false;
                default:                                      return JValue.CreateNull();
            }
        }
    }
}
