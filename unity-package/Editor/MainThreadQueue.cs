using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Patina.Editor
{
    [InitializeOnLoad]
    public static class MainThreadQueue
    {
        private static readonly ConcurrentQueue<Action> _queue = new();

        static MainThreadQueue()
        {
            Initialize();
        }

        private static void Initialize()
        {
            EditorApplication.update -= ProcessQueue;
            EditorApplication.update += ProcessQueue;
        }

        public static void Enqueue(Action action)
        {
            if (action == null) return;
            _queue.Enqueue(action);
        }

        public static Task<T> EnqueueAsync<T>(Func<T> func)
        {
            var tcs = new TaskCompletionSource<T>();
            Enqueue(() =>
            {
                try
                {
                    T result = func();
                    tcs.SetResult(result);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });
            return tcs.Task;
        }

        private static void ProcessQueue()
        {
            while (_queue.TryDequeue(out Action action))
            {
                try
                {
                    action.Invoke();
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                }
            }
        }
    }
}
