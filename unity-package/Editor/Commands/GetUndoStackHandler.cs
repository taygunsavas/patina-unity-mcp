using Newtonsoft.Json.Linq;
using System.Threading.Tasks;
using UnityEditor;

namespace Patina.Editor.Commands
{
    public sealed class GetUndoStackHandler : ICommandHandler
    {
        public async Task<object> HandleAsync(JObject parameters)
        {
            return await MainThreadQueue.EnqueueAsync(() =>
            {
                var undoNames = new JArray();
                var redoNames = new JArray();

                // Unity exposes only the current step name directly; we collect up to 20
                // by reading the internal stacks via reflection when available.
                try
                {
                    var undoStackProp = typeof(Undo).GetProperty(
                        "undoSteps",
                        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                    var redoStackProp = typeof(Undo).GetProperty(
                        "redoSteps",
                        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);

                    if (undoStackProp != null)
                    {
                        var steps = undoStackProp.GetValue(null) as string[];
                        if (steps != null)
                        {
                            int start = System.Math.Max(0, steps.Length - 20);
                            for (int i = steps.Length - 1; i >= start; i--)
                                undoNames.Add(steps[i]);
                        }
                    }
                    else
                    {
                        // Fallback: only current step name is available
                        string name = Undo.GetCurrentGroupName();
                        if (!string.IsNullOrEmpty(name))
                            undoNames.Add(name);
                    }

                    if (redoStackProp != null)
                    {
                        var steps = redoStackProp.GetValue(null) as string[];
                        if (steps != null)
                        {
                            int start = System.Math.Max(0, steps.Length - 20);
                            for (int i = steps.Length - 1; i >= start; i--)
                                redoNames.Add(steps[i]);
                        }
                    }

                    return new JObject
                    {
                        ["undoNames"] = undoNames,
                        ["redoNames"] = redoNames
                    };
                }
                catch
                {
                    return new JObject
                    {
                        ["undoNames"] = undoNames,
                        ["redoNames"] = redoNames,
                        ["note"] = "stack inspection unavailable"
                    };
                }
            });
        }
    }
}
