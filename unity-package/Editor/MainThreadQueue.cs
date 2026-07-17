using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Patina.Editor
{
    [InitializeOnLoad]
    public static class MainThreadQueue
    {
        public const double BlockedThresholdSeconds = 5.0;

        private static readonly ConcurrentQueue<Action> _queue = new();
        private static long _lastUpdateUtcTicks = DateTime.UtcNow.Ticks;
        private static int _pendingCount;

        static MainThreadQueue()
        {
            Initialize();
        }

        public static int PendingCount => Math.Max(0, Volatile.Read(ref _pendingCount));

        public static TimeSpan TimeSinceLastUpdate
        {
            get
            {
                long ticks = Interlocked.Read(ref _lastUpdateUtcTicks);
                return DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc);
            }
        }

        private static void Initialize()
        {
            EditorApplication.update -= ProcessQueue;
            EditorApplication.update += ProcessQueue;
        }

        public static void Enqueue(Action action)
        {
            if (action == null) return;
            Interlocked.Increment(ref _pendingCount);
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
            Interlocked.Exchange(ref _lastUpdateUtcTicks, DateTime.UtcNow.Ticks);

            while (_queue.TryDequeue(out Action action))
            {
                Interlocked.Decrement(ref _pendingCount);
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
