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

        private static readonly ConcurrentQueue<Action> s_queue = new();
        private static long s_lastUpdateUtcTicks = DateTime.UtcNow.Ticks;
        private static int s_pendingCount;
        private static readonly AsyncLocal<CancellationToken> s_commandCancellation =
            new AsyncLocal<CancellationToken>();
        internal static CancellationToken CurrentCommandCancellation => s_commandCancellation.Value;

        internal static IDisposable PushCommandCancellation(CancellationToken token)
        {
            CancellationToken prior = s_commandCancellation.Value;
            s_commandCancellation.Value = token;
            return new CancellationScope(prior);
        }

        static MainThreadQueue()
        {
            Initialize();
        }

        public static int PendingCount => Math.Max(0, Volatile.Read(ref s_pendingCount));

        public static TimeSpan TimeSinceLastUpdate
        {
            get
            {
                long ticks = Interlocked.Read(ref s_lastUpdateUtcTicks);
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
            if (action == null)
                return;
            Interlocked.Increment(ref s_pendingCount);
            s_queue.Enqueue(action);
        }

        public static Task<T> EnqueueAsync<T>(Func<T> func)
        {
            CancellationToken token = CurrentCommandCancellation;
            var tcs = new TaskCompletionSource<T>(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            CancellationTokenRegistration registration = token.CanBeCanceled
                ? token.Register(() => tcs.TrySetCanceled(token))
                : default;
            Enqueue(() =>
            {
                if (token.IsCancellationRequested)
                {
                    registration.Dispose();
                    return;
                }
                try
                {
                    T result = func();
                    tcs.TrySetResult(result);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
                finally
                {
                    registration.Dispose();
                }
            });
            return tcs.Task;
        }

        private sealed class CancellationScope : IDisposable
        {
            private readonly CancellationToken _prior;

            public CancellationScope(CancellationToken prior)
            {
                _prior = prior;
            }

            public void Dispose()
            {
                s_commandCancellation.Value = _prior;
            }
        }

        private static void ProcessQueue()
        {
            Interlocked.Exchange(ref s_lastUpdateUtcTicks, DateTime.UtcNow.Ticks);

            while (s_queue.TryDequeue(out Action action))
            {
                Interlocked.Decrement(ref s_pendingCount);
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
