using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Patina.Editor
{
    public enum BridgeRuntimeState
    {
        Stopped,
        Starting,
        Running,
        Stopping,
        DetachedAlive,
        StaleInProcess,
        PoisonedPort,
        PortOwnedByOtherProcess,
        Error,
    }

    public sealed class BridgeStatusSnapshot
    {
        public BridgeStatusSnapshot(
            BridgeRuntimeState state,
            int port,
            bool managedRunning,
            int trackedClientCount,
            int? listenerPid,
            string listenerProcessName,
            bool listenerOwnedByCurrentUnity,
            bool pingSucceeded,
            string pingMessage,
            bool restartRequired,
            string message
        )
        {
            State = state;
            Port = port;
            ManagedRunning = managedRunning;
            TrackedClientCount = trackedClientCount;
            ListenerPid = listenerPid;
            ListenerProcessName = listenerProcessName;
            ListenerOwnedByCurrentUnity = listenerOwnedByCurrentUnity;
            PingSucceeded = pingSucceeded;
            PingMessage = pingMessage;
            RestartRequired = restartRequired;
            Message = message;
        }

        public BridgeRuntimeState State { get; }
        public int Port { get; }
        public bool ManagedRunning { get; }
        public int TrackedClientCount { get; }
        public int? ListenerPid { get; }
        public string ListenerProcessName { get; }
        public bool ListenerOwnedByCurrentUnity { get; }
        public bool PingSucceeded { get; }
        public string PingMessage { get; }
        public bool RestartRequired { get; }
        public string Message { get; }
        public bool IsBridgeUsable => State == BridgeRuntimeState.Running;
    }

    /// <summary>Unity is a client of the single local Patina broker. It never owns TCP port 9800.</summary>
    [InitializeOnLoad]
    public static class McpBridgeServer
    {
        private const int DefaultPort = 9800,
            MaxFrameBytes = 4 * 1024 * 1024;
        private const string ExplicitStopSessionStateKey = "Patina.SharedBroker.ExplicitStop";
        private const string SessionIdSessionStateKey = "Patina.SharedBroker.SessionId";
        private const string ReloadCountSessionStateKey = "Patina.SharedBroker.ReloadCount";
        private static readonly object s_lock = new object();

        // SessionState survives a domain reload within the same editor process, so a
        // pinned session id (and reload counter) let the broker recognize this editor
        // across compiles instead of treating every reload as a brand-new session.
        // MUST stay a static field initializer: it runs on the main thread during
        // Unity's [InitializeOnLoad] static-constructor pass. Never move this call into
        // CaptureMetadata() or the connect loop -- those can run off the main thread,
        // and SessionState is only safe to touch from the main thread.
        private static readonly string s_sessionId = ResolveSessionId();
        private static int s_reloadCount = SessionState.GetInt(ReloadCountSessionStateKey, 0);
        private static CancellationTokenSource s_cancel;
        private static Task s_connectTask;
        private static TcpClient s_client;
        private static volatile bool s_running;
        private static volatile string s_lastError;
        private static volatile string s_health = "starting";
        private static volatile int s_agentClientCount;
        private static volatile int s_unitySessionCount;
        private static long s_currentGeneration;
        private static DateTime s_nextBrokerLaunchUtc = DateTime.MinValue;
        private static DateTime s_nextSupervisorCheckUtc = DateTime.MinValue;
        private static bool s_autoStartEnabled = true;
        private static int s_lifecycleHooksRegistered;
        private const int MaxConsecutiveBrokerLaunchFailures = 5;
        private static int s_consecutiveBrokerLaunchFailures;
        private static volatile bool s_brokerRelaunchSuspended;
        private static bool s_loggedBrokerRelaunchSuspended;
        private static bool s_sawSigkillInFailureSeries;

        public static readonly bool IsAutomatedMode =
            Array.IndexOf(Environment.GetCommandLineArgs(), "-automated") >= 0;

        private sealed class SessionMetadata
        {
            public string Workspace;
            public string ProjectName;
            public string BrokerBinary;
            public int UnityPid;
            public string PackageVersion;
            public bool Automated;
        }

        public static int Port => DefaultPort;
        public static bool IsRunning => s_running;
        public static int ConnectedClients => s_agentClientCount;
        public static int ActiveUnitySessions => s_unitySessionCount;
        public static string LocalSessionHealth => s_health;
        public static string LastError => s_lastError;
        public static BridgeRuntimeState RuntimeState => GetStatusSnapshot().State;

        static McpBridgeServer()
        {
            RegisterLifecycleHooks();
        }

        /// <summary>
        /// Resolves the session id for this editor process, reusing the value stashed in
        /// <see cref="SessionState"/> across domain reloads. Must only be called from the
        /// static field initializer above -- see the comment on <see cref="s_sessionId"/>.
        /// </summary>
        private static string ResolveSessionId()
        {
            string existing = SessionState.GetString(SessionIdSessionStateKey, string.Empty);
            if (!string.IsNullOrEmpty(existing))
                return existing;
            string generated = Guid.NewGuid().ToString("N");
            SessionState.SetString(SessionIdSessionStateKey, generated);
            return generated;
        }

        [InitializeOnLoadMethod]
        private static void InitializeOnLoad()
        {
            RegisterLifecycleHooks();
        }

        private static void RegisterLifecycleHooks()
        {
            if (Interlocked.Exchange(ref s_lifecycleHooksRegistered, 1) != 0)
                return;
            s_autoStartEnabled = !SessionState.GetBool(ExplicitStopSessionStateKey, false);
            CommandDispatcher.RegisterBuiltInHandlers();
            AssemblyReloadEvents.beforeAssemblyReload += StopForAssemblyReload;
            EditorApplication.quitting += StopForEditorShutdown;
            EditorApplication.delayCall += StartIfAutoStartEnabled;
            EditorApplication.update += EnsureConnectionSupervisor;
            UnityEngine.Debug.Log("[Patina] Shared broker lifecycle initialized.");
        }

        public static void Start()
        {
            bool refreshRuntime;
            lock (s_lock)
            {
                if (s_cancel != null && s_connectTask != null && !s_connectTask.IsCompleted)
                {
                    if (!RequiresRuntimeRefreshLocked())
                        return;
                    refreshRuntime = true;
                }
                else
                {
                    refreshRuntime = false;
                }
            }
            if (refreshRuntime)
            {
                UnityEngine.Debug.Log(
                    "[Patina] Refreshing shared broker supervisor after a runtime launch error."
                );
                StopInternal(intentional: false);
            }
            lock (s_lock)
            {
                DisposeCompletedConnectionLoopLocked();
                s_autoStartEnabled = true;
                SessionState.SetBool(ExplicitStopSessionStateKey, false);
                s_lastError = null;
                s_health = "starting";
                s_consecutiveBrokerLaunchFailures = 0;
                s_brokerRelaunchSuspended = false;
                s_loggedBrokerRelaunchSuspended = false;
                s_sawSigkillInFailureSeries = false;
                s_cancel = new CancellationTokenSource();
                CancellationToken startToken = s_cancel.Token;
                SessionMetadata metadata = CaptureMetadata();
                long generation = Interlocked.Increment(ref s_currentGeneration);
                s_connectTask = RunConnectionSupervisorAsync(startToken, metadata, generation);
                UnityEngine.Debug.Log(
                    "[Patina] Starting shared broker supervisor for " + metadata.ProjectName + "."
                );
            }
        }

        private static bool RequiresRuntimeRefreshLocked()
        {
            return s_brokerRelaunchSuspended
                || s_lastError?.StartsWith("Patina runtime is missing", StringComparison.Ordinal)
                    == true
                || s_lastError?.StartsWith(
                    "Could not start Patina shared broker",
                    StringComparison.Ordinal
                ) == true
                || s_lastError?.StartsWith(
                    "Patina shared broker exited before accepting connections",
                    StringComparison.Ordinal
                ) == true;
        }

        private static void StartIfAutoStartEnabled()
        {
            if (!s_autoStartEnabled || SessionState.GetBool(ExplicitStopSessionStateKey, false))
                return;
            Start();
        }

        public static void Stop()
        {
            StopInternal(intentional: true);
        }

        private static void StopForAssemblyReload()
        {
            s_reloadCount++;
            SessionState.SetInt(ReloadCountSessionStateKey, s_reloadCount);
            StopInternal(intentional: false, disconnectReason: "assemblyReload");
        }

        private static void StopForEditorShutdown()
        {
            StopInternal(intentional: false, disconnectReason: "editorShutdown");
        }

        private static void StopInternal(bool intentional, string disconnectReason = null)
        {
            Interlocked.Increment(ref s_currentGeneration);
            CancellationTokenSource cancel;
            TcpClient client;
            lock (s_lock)
            {
                cancel = s_cancel;
                s_cancel = null;
                s_connectTask = null;
                client = s_client;
                s_client = null;
                s_running = false;
                s_health = "stopped";
                if (intentional)
                {
                    s_autoStartEnabled = false;
                    SessionState.SetBool(ExplicitStopSessionStateKey, true);
                }
            }
            try
            {
                if (client != null && client.Connected)
                {
                    JObject unregister = new JObject
                    {
                        ["type"] = "unregister",
                        ["sessionId"] = s_sessionId,
                    };
                    if (disconnectReason != null)
                        unregister["reason"] = disconnectReason;
                    // Stays synchronous: the frame must be fully on the wire before the
                    // socket closes below, otherwise the broker never sees the reason and
                    // treats this like an unannounced disconnect.
                    WriteFrame(client.GetStream(), unregister.ToString(Formatting.None));
                }
            }
            catch { }
            try
            {
                cancel?.Cancel();
                client?.Close();
            }
            catch { }
            finally
            {
                cancel?.Dispose();
            }
            UnityEngine.Debug.Log(
                intentional ? "[Patina] Shared broker supervisor stopped by user."
                : disconnectReason == "assemblyReload"
                    ? "[Patina] Shared broker supervisor stopped for assembly reload."
                : "[Patina] Shared broker supervisor stopped for editor shutdown."
            );
        }

        public static void Restart()
        {
            Stop();
            Start();
        }

        private static void EnsureConnectionSupervisor()
        {
            if (!s_autoStartEnabled || EditorApplication.isCompiling)
                return;
            DateTime now = DateTime.UtcNow;
            if (now < s_nextSupervisorCheckUtc)
                return;
            s_nextSupervisorCheckUtc = now.AddSeconds(2);
            lock (s_lock)
            {
                if (s_cancel != null && s_connectTask != null && !s_connectTask.IsCompleted)
                    return;
            }
            UnityEngine.Debug.Log("[Patina] Shared broker supervisor was inactive; restarting it.");
            Start();
        }

        public static BridgeStatusSnapshot GetStatusSnapshot(bool forceRefresh = false)
        {
            bool connecting;
            lock (s_lock)
                connecting =
                    s_cancel != null && s_connectTask != null && !s_connectTask.IsCompleted;
            BridgeRuntimeState state =
                s_running ? BridgeRuntimeState.Running
                : !string.IsNullOrEmpty(s_lastError) ? BridgeRuntimeState.Error
                : connecting ? BridgeRuntimeState.Starting
                : BridgeRuntimeState.Stopped;
            string message = s_running
                ? "Connected to the shared Patina broker. Unity sessions and agents may share tcp://127.0.0.1:9800/."
                : (
                    s_lastError
                    ?? (
                        connecting
                            ? "Connecting to the shared Patina broker."
                            : "Patina broker client is stopped."
                    )
                );
            return new BridgeStatusSnapshot(
                state,
                Port,
                s_running,
                ConnectedClients,
                null,
                "patina-server broker",
                false,
                s_running,
                s_health,
                false,
                message
            );
        }

        private static async Task ConnectLoopAsync(
            CancellationToken token,
            SessionMetadata metadata,
            long generation
        )
        {
            while (!token.IsCancellationRequested)
            {
                TcpClient client = null;
                try
                {
                    client = new TcpClient();
                    Task connect = client.ConnectAsync("127.0.0.1", Port);
                    if (
                        await Task.WhenAny(connect, Task.Delay(1000, token)).ConfigureAwait(false)
                        != connect
                    )
                    {
                        ObserveFault(connect);
                        throw new TimeoutException("Broker connect timed out.");
                    }
                    await connect.ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                    if (!IsCurrentGeneration(generation))
                        throw new OperationCanceledException(token);
                    client.NoDelay = true;
                    WriteFrame(
                        client.GetStream(),
                        JsonConvert.SerializeObject(
                            new
                            {
                                role = "unity",
                                sessionId = s_sessionId,
                                workspace = metadata.Workspace,
                                projectName = metadata.ProjectName,
                                unityPid = metadata.UnityPid,
                                packageVersion = metadata.PackageVersion,
                                reloadCount = s_reloadCount,
                                automated = metadata.Automated,
                            }
                        )
                    );
                    lock (s_lock)
                    {
                        token.ThrowIfCancellationRequested();
                        if (!IsCurrentGeneration(generation))
                            throw new OperationCanceledException(token);
                        s_client = client;
                        s_running = true;
                        s_lastError = null;
                        s_health = "responsive";
                        s_consecutiveBrokerLaunchFailures = 0;
                        s_brokerRelaunchSuspended = false;
                        s_loggedBrokerRelaunchSuspended = false;
                        s_sawSigkillInFailureSeries = false;
                        UnityEngine.Debug.Log(
                            "[Patina] Connected to shared broker on port " + Port + "."
                        );
                    }
                    await ReadRequestsAsync(client, token, generation, metadata)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    if (!token.IsCancellationRequested && IsCurrentGeneration(generation))
                    {
                        // Locked together with RegisterBrokerLaunchFailure's writes so a
                        // suspension decided concurrently on another thread can never be
                        // clobbered by this generic "connection dropped" message.
                        lock (s_lock)
                        {
                            if (!s_brokerRelaunchSuspended)
                            {
                                s_lastError =
                                    "Patina broker connection dropped: "
                                    + ex.GetType().Name
                                    + ": "
                                    + ex.Message;
                                s_health = "reconnecting";
                                UnityEngine.Debug.LogWarning("[Patina] " + s_lastError);
                            }
                        }
                        TryLaunchBroker(metadata, generation);
                    }
                }
                finally
                {
                    lock (s_lock)
                    {
                        if (ReferenceEquals(s_client, client) && IsCurrentGeneration(generation))
                        {
                            s_client = null;
                            s_running = false;
                        }
                    }
                    try
                    {
                        client?.Close();
                    }
                    catch { }
                }
                try
                {
                    await Task.Delay(500, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { }
            }
        }

        private static async Task RunConnectionSupervisorAsync(
            CancellationToken token,
            SessionMetadata metadata,
            long generation
        )
        {
            string completion = "completed";
            try
            {
                await ConnectLoopAsync(token, metadata, generation).ConfigureAwait(false);
                if (!token.IsCancellationRequested && IsCurrentGeneration(generation))
                {
                    s_lastError = "Patina broker supervisor completed unexpectedly.";
                    s_health = "error";
                    UnityEngine.Debug.LogError(
                        "[Patina] Shared broker supervisor completed unexpectedly."
                    );
                }
            }
            catch (Exception ex)
            {
                completion = "faulted";
                if (IsCurrentGeneration(generation))
                {
                    s_lastError = "Patina broker supervisor faulted: " + ex.Message;
                    s_health = "error";
                    UnityEngine.Debug.LogException(ex);
                }
                throw;
            }
            finally
            {
                if (IsCurrentGeneration(generation))
                {
                    if (token.IsCancellationRequested)
                        completion = "canceled";
                    UnityEngine.Debug.Log(
                        "[Patina] Shared broker supervisor "
                            + completion
                            + " for "
                            + metadata.ProjectName
                            + "."
                    );
                }
            }
        }

        private static void DisposeCompletedConnectionLoopLocked()
        {
            if (s_connectTask == null || !s_connectTask.IsCompleted)
                return;
            if (s_connectTask.IsFaulted)
                s_lastError =
                    "Patina broker connection loop stopped: "
                    + s_connectTask.Exception?.GetBaseException().Message;
            s_connectTask = null;
            s_cancel?.Dispose();
            s_cancel = null;
        }

        private static bool IsCurrentGeneration(long generation) =>
            Interlocked.Read(ref s_currentGeneration) == generation;

        private static async Task ReadRequestsAsync(
            TcpClient client,
            CancellationToken token,
            long generation,
            SessionMetadata metadata
        )
        {
            NetworkStream stream = client.GetStream();
            using (
                var connectionCancellation = CancellationTokenSource.CreateLinkedTokenSource(token)
            )
            {
                CancellationToken connectionToken = connectionCancellation.Token;
                // These are disposed only after every dispatch has completed. A timed-out
                // drain leaves ownership with its completion continuation to avoid races.
                var dispatchSlots = new SemaphoreSlim(4, 4);
                var writeGate = new SemaphoreSlim(1, 1);
                var inFlight = new ConcurrentBag<Task>();
                try
                {
                    while (!connectionToken.IsCancellationRequested && client.Connected)
                    {
                        string json = await ReadFrameAsync(stream).ConfigureAwait(false);
                        if (json == null)
                            return;
                        BridgeRequest request;
                        try
                        {
                            JObject envelope = JsonConvert.DeserializeObject<JObject>(json);
                            request = envelope?["request"]?.ToObject<BridgeRequest>();
                            if (request == null)
                                continue;
                        }
                        catch (Exception parseEx)
                        {
                            // A malformed frame (e.g. "params" sent as a scalar instead of a
                            // JSON object) must never take down the whole connection -- report
                            // it to the caller and keep reading. Best-effort recovery of the
                            // request id lets the agent correlate the failure with its call.
                            string failedId = null;
                            try
                            {
                                JObject rawEnvelope = JsonConvert.DeserializeObject<JObject>(json);
                                failedId = rawEnvelope?["request"]?["id"]?.Value<string>();
                            }
                            catch { }
                            UnityEngine.Debug.LogWarning(
                                "[Patina] Failed to parse incoming request frame: "
                                    + parseEx.Message
                            );
                            if (failedId != null)
                            {
                                await WriteResponseAsync(
                                        client,
                                        stream,
                                        writeGate,
                                        BridgeResponse.Fail(
                                            failedId,
                                            "Request could not be parsed: params must be a JSON object. "
                                                + parseEx.Message,
                                            "BAD_REQUEST"
                                        )
                                    )
                                    .ConfigureAwait(false);
                            }
                            continue;
                        }
                        if (request.Command == "__patina_bridge_ping")
                        {
                            await WriteResponseAsync(
                                    client,
                                    stream,
                                    writeGate,
                                    BridgeResponse.Ok(
                                        request.Id,
                                        new
                                        {
                                            bridge = "patina-unity",
                                            sessionId = s_sessionId,
                                            unityProcessId = metadata.UnityPid,
                                            packageVersion = metadata.PackageVersion,
                                            managedRunning = s_running,
                                            trackedClientCount = s_agentClientCount,
                                            activeUnitySessionCount = s_unitySessionCount,
                                            port = Port,
                                            health = s_health,
                                        }
                                    )
                                )
                                .ConfigureAwait(false);
                            continue;
                        }
                        if (request.Command == "__patina_broker_heartbeat")
                        {
                            s_agentClientCount =
                                request.Parameters?["agentClientCount"]?.Value<int>()
                                ?? s_agentClientCount;
                            s_unitySessionCount =
                                request.Parameters?["unitySessionCount"]?.Value<int>()
                                ?? s_unitySessionCount;
                            s_health = IsEditorLikelyBlocked() ? "blocked" : "responsive";
                            await WriteResponseAsync(
                                    client,
                                    stream,
                                    writeGate,
                                    BridgeResponse.Ok(
                                        request.Id,
                                        new
                                        {
                                            status = IsEditorLikelyBlocked()
                                                ? "blocked"
                                                : "responsive",
                                            mainThreadUpdateAgeSeconds = MainThreadQueue
                                                .TimeSinceLastUpdate
                                                .TotalSeconds,
                                        }
                                    )
                                )
                                .ConfigureAwait(false);
                            continue;
                        }
                        await dispatchSlots.WaitAsync(connectionToken).ConfigureAwait(false);
                        Task dispatchTask = Task.Run(async () =>
                        {
                            try
                            {
                                BridgeResponse response =
                                    IsEditorLikelyBlocked() && request.Command != "get_editor_state"
                                        ? BridgeResponse.Fail(
                                            request.Id,
                                            "Unity editor is blocked by a modal dialog or blocking operation.",
                                            "EDITOR_BLOCKED"
                                        )
                                        : await CommandDispatcher
                                            .Dispatch(request, connectionToken)
                                            .ConfigureAwait(false);
                                if (!connectionToken.IsCancellationRequested)
                                    await WriteResponseAsync(client, stream, writeGate, response)
                                        .ConfigureAwait(false);
                            }
                            catch (OperationCanceledException) { }
                            catch (Exception ex)
                            {
                                if (IsCurrentGeneration(generation))
                                    RecordTransientFailure(ex.Message);
                            }
                            finally
                            {
                                dispatchSlots.Release();
                            }
                        });
                        inFlight.Add(dispatchTask);
                    }
                }
                finally
                {
                    connectionCancellation.Cancel();
                    Task all = Task.WhenAll(inFlight.ToArray());
                    if (await Task.WhenAny(all, Task.Delay(1000)).ConfigureAwait(false) == all)
                    {
                        try
                        {
                            await all.ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            if (IsCurrentGeneration(generation))
                                RecordTransientFailure(ex.Message);
                        }
                        dispatchSlots.Dispose();
                        writeGate.Dispose();
                    }
                    else
                    {
                        _ = all.ContinueWith(
                            task =>
                            {
                                if (task.IsFaulted)
                                    if (IsCurrentGeneration(generation))
                                        RecordTransientFailure(
                                            task.Exception?.GetBaseException().Message
                                        );
                                dispatchSlots.Dispose();
                                writeGate.Dispose();
                            },
                            TaskScheduler.Default
                        );
                    }
                }
            }
        }

        private static async Task WriteResponseAsync(
            TcpClient client,
            NetworkStream stream,
            SemaphoreSlim writeGate,
            BridgeResponse response
        )
        {
            await writeGate.WaitAsync().ConfigureAwait(false);
            try
            {
                lock (s_lock)
                {
                    if (s_client == client)
                        WriteFrame(stream, JsonConvert.SerializeObject(response, Formatting.None));
                }
            }
            finally
            {
                writeGate.Release();
            }
        }

        private static void RecordTransientFailure(string message)
        {
            s_health = "reconnecting";
            UnityEngine.Debug.LogWarning(
                "[Patina] Shared broker connection will retry: " + message
            );
        }

        private static void TryLaunchBroker(SessionMetadata metadata, long generation)
        {
            if (s_brokerRelaunchSuspended)
                return;
            DateTime now = DateTime.UtcNow;
            lock (s_lock)
            {
                if (now < s_nextBrokerLaunchUtc)
                    return;
                s_nextBrokerLaunchUtc = now.AddSeconds(2);
            }
            // ProcessManager reads Unity EditorPrefs, so the active runtime must be
            // resolved on Start's main-thread path and never from this supervisor.
            string binary = metadata.BrokerBinary;
            if (string.IsNullOrEmpty(binary))
            {
                s_lastError = "Patina runtime is missing; broker cannot be started.";
                s_health = "error";
                return;
            }
            try
            {
                UnityEngine.Debug.Log("[Patina] Launching shared broker from " + binary + ".");
                Process process = Process.Start(
                    new ProcessStartInfo
                    {
                        FileName = binary,
                        Arguments = "--broker --port " + Port,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        WorkingDirectory = Path.GetDirectoryName(binary),
                    }
                );
                if (process == null)
                {
                    RegisterBrokerLaunchFailure(
                        "Could not start Patina shared broker.",
                        null,
                        generation
                    );
                }
                else
                {
                    UnityEngine.Debug.Log(
                        "[Patina] Shared broker process started (PID " + process.Id + ")."
                    );
                    ObserveBrokerExit(process, generation);
                }
            }
            catch (Exception ex)
            {
                RegisterBrokerLaunchFailure(
                    "Could not start Patina shared broker: " + ex.Message,
                    null,
                    generation
                );
            }
        }

        private static void ObserveBrokerExit(Process process, long generation)
        {
            process.Exited += (_, __) =>
            {
                int exitCode;
                try
                {
                    exitCode = process.ExitCode;
                }
                catch
                {
                    exitCode = -1;
                }
                finally
                {
                    process.Dispose();
                }
                if (exitCode != 0)
                    _ = Task.Run(() => RecordBrokerExitIfUnavailableAsync(exitCode, generation));
            };
            process.EnableRaisingEvents = true;
        }

        private static async Task RecordBrokerExitIfUnavailableAsync(int exitCode, long generation)
        {
            await Task.Delay(100).ConfigureAwait(false);
            if (
                !IsCurrentGeneration(generation)
                || s_running
                || await CanConnectToBrokerAsync().ConfigureAwait(false)
            )
                return;
            RegisterBrokerLaunchFailure(
                "Patina shared broker exited before accepting connections (exit code "
                    + exitCode
                    + ").",
                exitCode,
                generation
            );
        }

        /// <summary>
        /// Single choke point for every broker-launch failure path (failed to spawn the
        /// process, spawn threw, or the spawned process exited before accepting
        /// connections). Counts consecutive failures and, once
        /// <see cref="MaxConsecutiveBrokerLaunchFailures"/> is reached, suspends automatic
        /// relaunching so <see cref="TryLaunchBroker"/> stops spawning new processes.
        /// Runs under <see cref="s_lock"/> so the suspension decision, the counter, and
        /// <see cref="s_lastError"/>/<see cref="s_health"/> stay consistent with the
        /// connect loop's own error reporting, which checks <see cref="s_brokerRelaunchSuspended"/>
        /// under the same lock before writing a generic reconnect message. The generation
        /// check also runs inside the lock: a stale supervisor generation's exit handler can
        /// still be in flight (e.g. racing an async connectivity probe) after the user hits
        /// Start/Restart and a fresh generation has already reset the failure state, so this
        /// must never write anything for a generation that is no longer current.
        /// </summary>
        private static void RegisterBrokerLaunchFailure(
            string message,
            int? exitCode,
            long generation
        )
        {
            lock (s_lock)
            {
                if (!IsCurrentGeneration(generation))
                    return;
                s_lastError = message;
                s_health = "error";
                if (exitCode == 137)
                    s_sawSigkillInFailureSeries = true;
                int consecutiveFailures = ++s_consecutiveBrokerLaunchFailures;
                if (consecutiveFailures < MaxConsecutiveBrokerLaunchFailures)
                    return;
                s_brokerRelaunchSuspended = true;
                string hint = string.Empty;
                if (s_sawSigkillInFailureSeries)
                    hint =
                        " At least one of these exits was code 137 (SIGKILL), which usually "
                        + "means the runtime binary was overwritten while it was running (same "
                        + "inode, so code-signing verification breaks); re-publishing the "
                        + "runtime should fix it.";
                s_lastError =
                    message
                    + " ("
                    + consecutiveFailures
                    + " consecutive failures); automatic relaunch has been stopped. Restart the "
                    + "broker manually (Start) once the underlying issue is fixed."
                    + hint;
                if (s_loggedBrokerRelaunchSuspended)
                    return;
                s_loggedBrokerRelaunchSuspended = true;
                UnityEngine.Debug.LogError("[Patina] " + s_lastError);
            }
        }

        private static async Task<bool> CanConnectToBrokerAsync()
        {
            try
            {
                using (var client = new TcpClient())
                {
                    Task connect = client.ConnectAsync("127.0.0.1", Port);
                    if (
                        await Task.WhenAny(connect, Task.Delay(250)).ConfigureAwait(false)
                        != connect
                    )
                    {
                        ObserveFault(connect);
                        return false;
                    }
                    await connect.ConfigureAwait(false);
                    return client.Connected;
                }
            }
            catch (SocketException)
            {
                return false;
            }
            catch (ObjectDisposedException)
            {
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        private static void ObserveFault(Task task)
        {
            _ = task.ContinueWith(
                completed => _ = completed.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted
                    | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default
            );
        }

        private static SessionMetadata CaptureMetadata()
        {
            string assets = Application.dataPath;
            string workspace = CanonicalWorkspace(assets);
            return new SessionMetadata
            {
                Workspace = workspace,
                ProjectName = ProjectNameFromWorkspace(workspace),
                BrokerBinary = ProcessManager.FindServerBinary(),
                UnityPid = Process.GetCurrentProcess().Id,
                PackageVersion =
                    UnityEditor
                        .PackageManager.PackageInfo.FindForAssembly(
                            typeof(McpBridgeServer).Assembly
                        )
                        ?.version
                    ?? "unknown",
                Automated = IsAutomatedMode,
            };
        }

        private static string CanonicalWorkspace(string assetsPath)
        {
            try
            {
                return Path.GetFullPath(Path.Combine(assetsPath, ".."))
                    .TrimEnd(Path.DirectorySeparatorChar)
                    .Replace('\\', '/');
            }
            catch
            {
                return assetsPath.Replace('\\', '/');
            }
        }

        private static string ProjectNameFromWorkspace(string workspace)
        {
            try
            {
                string name = new DirectoryInfo(workspace).Name;
                return string.IsNullOrEmpty(name) ? Application.productName : name;
            }
            catch
            {
                return Application.productName;
            }
        }

        private static bool IsEditorLikelyBlocked() =>
            MainThreadQueue.TimeSinceLastUpdate.TotalSeconds
            >= MainThreadQueue.BlockedThresholdSeconds;

        private static void WriteFrame(NetworkStream stream, string text)
        {
            byte[] b = Encoding.UTF8.GetBytes(text);
            if (b.Length == 0 || b.Length > MaxFrameBytes)
                throw new InvalidOperationException("Invalid frame length.");
            byte[] h = BitConverter.GetBytes(b.Length);
            stream.Write(h, 0, h.Length);
            stream.Write(b, 0, b.Length);
            stream.Flush();
        }

        private static async Task<string> ReadFrameAsync(NetworkStream stream)
        {
            byte[] h = await ReadExactlyAsync(stream, 4).ConfigureAwait(false);
            if (h == null)
                return null;
            int length = BitConverter.ToInt32(h, 0);
            if (length <= 0 || length > MaxFrameBytes)
                throw new InvalidOperationException("Invalid broker frame length: " + length);
            byte[] body = await ReadExactlyAsync(stream, length).ConfigureAwait(false);
            return body == null ? null : Encoding.UTF8.GetString(body);
        }

        private static async Task<byte[]> ReadExactlyAsync(NetworkStream stream, int length)
        {
            byte[] b = new byte[length];
            int o = 0;
            while (o < length)
            {
                int r = await stream.ReadAsync(b, o, length - o).ConfigureAwait(false);
                if (r == 0)
                    return null;
                o += r;
            }
            return b;
        }
    }
}
