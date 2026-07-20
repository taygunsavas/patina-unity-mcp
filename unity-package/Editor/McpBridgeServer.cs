using System;
using System.Collections.Generic;
using System.Net;
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
        Error
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
            string message)
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

        public bool IsBridgeUsable => State == BridgeRuntimeState.Running || State == BridgeRuntimeState.DetachedAlive;
    }

    [InitializeOnLoad]
    public static class McpBridgeServer
    {
        private const string BridgePingCommand = "__patina_bridge_ping";
        private const string PortPrefsKey = "Patina.Port";
        private const int DefaultPort = 9800;
        private const int MaxFrameBytes = 4 * 1024 * 1024;
        private const int ProbeTimeoutMilliseconds = 250;

        private static TcpListener _listener;
        private static CancellationTokenSource _cts;
        private static Thread _listenerThread;
        private static readonly object s_clientsLock = new object();
        private static readonly HashSet<TcpClient> s_clients = new HashSet<TcpClient>();
        private static readonly string s_sessionId = Guid.NewGuid().ToString("N");
        private static readonly object s_snapshotLock = new object();
        private static BridgeStatusSnapshot s_cachedSnapshot;
        private static DateTime s_cachedSnapshotAtUtc = DateTime.MinValue;

        private static int _isRunning;
        public static bool IsRunning => Interlocked.CompareExchange(ref _isRunning, 0, 0) == 1;

        private static volatile string _lastError;
        public static string LastError => _lastError;

        public static int Port => EditorPrefs.GetInt(PortPrefsKey, DefaultPort);
        public static int ConnectedClients
        {
            get
            {
                lock (s_clientsLock)
                {
                    return s_clients.Count;
                }
            }
        }

        public static BridgeRuntimeState RuntimeState => GetStatusSnapshot().State;

        private static readonly JsonSerializerSettings s_jsonSettings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore
        };

        static McpBridgeServer()
        {
            CommandDispatcher.RegisterBuiltInHandlers();
            AssemblyReloadEvents.beforeAssemblyReload += Stop;
            EditorApplication.quitting += Stop;
            EditorApplication.delayCall += () =>
            {
                if (!IsRunning)
                    Start();
            };
        }

        public static void SetPort(int port)
        {
            EditorPrefs.SetInt(PortPrefsKey, port);
            InvalidateStatusSnapshot();
        }

        public static void Start()
        {
            if (IsRunning)
                return;

            _lastError = null;
            InvalidateStatusSnapshot();

            int port = Port;
            if (TryStartOnPort(port, out Exception error))
                return;

            if (IsAddressAlreadyInUse(error))
            {
                Debug.LogWarning($"[Patina] TCP bridge port {port} is already in use. Trying to release the existing listener before retrying.");
                PortReleaseResult releaseResult = TryReleasePortOwner(port);

                Exception retryError = null;
                if (releaseResult.Released && TryStartOnPort(port, out retryError))
                    return;

                if (releaseResult.DetachedAlive)
                {
                    _lastError = null;
                    InvalidateStatusSnapshot();
                    Debug.LogWarning("[Patina] " + releaseResult.Message);
                    return;
                }

                SetLastError(BuildPortUnavailableMessage(port, releaseResult, retryError));
                return;
            }

            SetLastError($"Failed to start TCP bridge on port {port}: {error.Message}");
        }

        public static void Restart()
        {
            Stop();
            Start();
        }

        private static void InvalidateStatusSnapshot()
        {
            lock (s_snapshotLock)
            {
                s_cachedSnapshot = null;
                s_cachedSnapshotAtUtc = DateTime.MinValue;
            }
        }

        public static BridgeStatusSnapshot GetStatusSnapshot(bool forceRefresh = false)
        {
            lock (s_snapshotLock)
            {
                if (!forceRefresh
                    && s_cachedSnapshot != null
                    && (DateTime.UtcNow - s_cachedSnapshotAtUtc).TotalMilliseconds < 500)
                {
                    return s_cachedSnapshot;
                }

                s_cachedSnapshot = BuildStatusSnapshot();
                s_cachedSnapshotAtUtc = DateTime.UtcNow;
                return s_cachedSnapshot;
            }
        }

        private static BridgeStatusSnapshot BuildStatusSnapshot()
        {
            int port = Port;
            int trackedClients = ConnectedClients;
            bool managedRunning = IsRunning;
            int currentPid = System.Diagnostics.Process.GetCurrentProcess().Id;
            int? listenerPid = FindTcpListenerPid(port);
            string ownerProcessName = listenerPid.HasValue ? GetProcessName(listenerPid.Value) : null;
            BridgeProbeResult probe = BridgeProbeResult.NotAttempted();
            BridgeRuntimeState state;
            string message;
            bool restartRequired = false;

            if (managedRunning)
            {
                state = BridgeRuntimeState.Running;
                message = trackedClients > 0
                    ? "Unity bridge is listening on local TCP and has an attached Patina client."
                    : "Unity bridge is listening on local TCP and waiting for a Patina client connection.";
            }
            else if (!listenerPid.HasValue)
            {
                state = BridgeRuntimeState.Stopped;
                message = string.IsNullOrEmpty(_lastError)
                    ? "Bridge server is stopped. Start it manually only when debugging transport issues."
                    : _lastError;
            }
            else if (listenerPid.Value == currentPid)
            {
                probe = ProbeBridge(port);
                if (probe.Success)
                {
                    state = BridgeRuntimeState.DetachedAlive;
                    message = "A Patina bridge listener is alive inside this Unity Editor, but the current managed bridge instance does not own its listener handle. Restart Unity after the active host session if controls cannot stop it.";
                }
                else
                {
                    state = BridgeRuntimeState.StaleInProcess;
                    restartRequired = true;
                    message = "This Unity Editor owns the Patina bridge port, but the managed bridge state no longer owns the listener and protocol ping failed. Restart Unity to clear the stale in-process listener.";
                }
            }
            else if (string.IsNullOrWhiteSpace(ownerProcessName))
            {
                state = BridgeRuntimeState.PoisonedPort;
                restartRequired = true;
                message = listenerPid.HasValue
                    ? BuildPoisonedPortMessage(port, listenerPid.Value)
                    : "Windows reports the Patina bridge port as occupied, but Patina could not resolve the owning PID.";
            }
            else
            {
                state = BridgeRuntimeState.PortOwnedByOtherProcess;
                restartRequired = IsUnityProcess(ownerProcessName);
                message = IsUnityProcess(ownerProcessName)
                    ? $"The port is owned by another Unity Editor PID {listenerPid.Value}. Close that editor or change its Patina port, then retry."
                    : $"The port is owned by PID {listenerPid.Value} ({DescribeProcess(ownerProcessName)}). Patina will not terminate unrelated processes automatically.";
            }

            if (state == BridgeRuntimeState.Stopped && !string.IsNullOrEmpty(_lastError))
                state = BridgeRuntimeState.Error;

            return new BridgeStatusSnapshot(
                state,
                port,
                managedRunning,
                trackedClients,
                listenerPid,
                ownerProcessName,
                listenerPid.HasValue && listenerPid.Value == currentPid,
                probe.Success,
                probe.Message,
                restartRequired,
                message);
        }

        private static void SetLastError(string message)
        {
            _lastError = message;
            InvalidateStatusSnapshot();
            Debug.LogError("[Patina] " + message);
        }

        private static bool TryStartOnPort(int port, out Exception error)
        {
            error = null;
            CleanupStartAttempt();

            CancellationTokenSource cts = new CancellationTokenSource();
            TcpListener listener = null;

            try
            {
                listener = new TcpListener(IPAddress.Loopback, port);
                listener.Server.NoDelay = true;
                listener.Start();

                _cts = cts;
                _listener = listener;
                Interlocked.Exchange(ref _isRunning, 1);
                InvalidateStatusSnapshot();

                _listenerThread = new Thread(() => ListenLoop(cts.Token));
                _listenerThread.IsBackground = true;
                _listenerThread.Name = "Patina-TcpBridge";
                _listenerThread.Start();

                Debug.Log($"[Patina] Bridge server started on tcp://127.0.0.1:{port}/");
                return true;
            }
            catch (Exception ex)
            {
                error = ex;
                Interlocked.Exchange(ref _isRunning, 0);
                try
                {
                    if (listener != null)
                        listener.Stop();
                }
                catch
                {
                }

                cts.Dispose();
                CleanupStartAttempt();
                return false;
            }
        }

        private static BridgeProbeResult ProbeBridge(int port)
        {
            try
            {
                using (TcpClient client = new TcpClient())
                {
                    IAsyncResult connect = client.BeginConnect(IPAddress.Loopback, port, null, null);
                    if (!connect.AsyncWaitHandle.WaitOne(ProbeTimeoutMilliseconds))
                        return BridgeProbeResult.Failed("Bridge ping connect timed out.");

                    client.EndConnect(connect);
                    client.NoDelay = true;
                    client.ReceiveTimeout = ProbeTimeoutMilliseconds;
                    client.SendTimeout = ProbeTimeoutMilliseconds;

                    using (NetworkStream stream = client.GetStream())
                    {
                        BridgeRequest request = new BridgeRequest
                        {
                            Id = Guid.NewGuid().ToString("N"),
                            Command = BridgePingCommand,
                            Parameters = new JObject()
                        };

                        string requestJson = JsonConvert.SerializeObject(request, Formatting.None, s_jsonSettings);
                        WriteFrame(stream, requestJson);
                        string responseJson = ReadFrame(stream);
                        BridgeResponse response = JsonConvert.DeserializeObject<BridgeResponse>(responseJson);
                        if (response != null && response.Success)
                            return BridgeProbeResult.Ok("Bridge ping succeeded.");

                        string error = response?.Error?.Message ?? "Bridge ping returned no success response.";
                        return BridgeProbeResult.Failed(error);
                    }
                }
            }
            catch (Exception ex)
            {
                return BridgeProbeResult.Failed(ex.Message);
            }
        }

        private static void WriteFrame(NetworkStream stream, string payload)
        {
            byte[] body = Encoding.UTF8.GetBytes(payload);
            if (body.Length <= 0 || body.Length > MaxFrameBytes)
                throw new InvalidOperationException($"Invalid frame length: {body.Length}");

            int len = body.Length;
            byte[] header = new byte[4];
            header[0] = (byte)(len & 0xFF);
            header[1] = (byte)((len >> 8) & 0xFF);
            header[2] = (byte)((len >> 16) & 0xFF);
            header[3] = (byte)((len >> 24) & 0xFF);
            stream.Write(header, 0, header.Length);
            stream.Write(body, 0, body.Length);
            stream.Flush();
        }

        private static string ReadFrame(NetworkStream stream)
        {
            byte[] header = ReadExactly(stream, 4);
            int length = header[0] | (header[1] << 8) | (header[2] << 16) | (header[3] << 24);
            if (length <= 0 || length > MaxFrameBytes)
                throw new InvalidOperationException($"Invalid frame length: {length}");

            byte[] payload = ReadExactly(stream, length);
            return Encoding.UTF8.GetString(payload, 0, payload.Length);
        }

        private static byte[] ReadExactly(NetworkStream stream, int length)
        {
            byte[] buffer = new byte[length];
            int offset = 0;
            while (offset < length)
            {
                int read = stream.Read(buffer, offset, length - offset);
                if (read == 0)
                    throw new InvalidOperationException("Bridge ping connection closed.");

                offset += read;
            }

            return buffer;
        }

        public static void Stop()
        {
            bool managedRunning = IsRunning;
            if (!managedRunning && ConnectedClients == 0)
                return;

            if (!managedRunning)
            {
                BridgeStatusSnapshot snapshot = GetStatusSnapshot(true);
                if (snapshot.State == BridgeRuntimeState.DetachedAlive)
                {
                    SetLastError("Patina cannot stop the current bridge listener because it is detached from the managed listener handle. Restart Unity after the active host session to fully reset it.");
                    return;
                }

                if (snapshot.State == BridgeRuntimeState.StaleInProcess)
                {
                    SetLastError("Patina cannot stop the stale in-process bridge listener because the managed listener handle is unavailable. Restart Unity to clear it.");
                    return;
                }
            }

            Interlocked.Exchange(ref _isRunning, 0);
            InvalidateStatusSnapshot();

            try
            {
                if (_cts != null)
                    _cts.Cancel();
                if (_listener != null)
                    _listener.Stop();
                CloseActiveClients();
            }
            catch
            {
            }
            finally
            {
                _listener = null;
                if (_cts != null)
                    _cts.Dispose();
                _cts = null;
                _listenerThread = null;
                CloseActiveClients();
                InvalidateStatusSnapshot();
                Debug.Log("[Patina] Bridge server stopped.");
            }
        }

        private static void CleanupStartAttempt()
        {
            try
            {
                if (_listener != null)
                    _listener.Stop();
            }
            catch
            {
            }
            finally
            {
                _listener = null;
                if (_cts != null)
                    _cts.Dispose();
                _cts = null;
                _listenerThread = null;
                CloseActiveClients();
                InvalidateStatusSnapshot();
            }
        }

        private static void CloseActiveClients()
        {
            TcpClient[] clients;
            lock (s_clientsLock)
            {
                clients = new TcpClient[s_clients.Count];
                s_clients.CopyTo(clients);
                s_clients.Clear();
            }

            foreach (TcpClient client in clients)
            {
                try
                {
                    client.Close();
                }
                catch
                {
                }
            }
        }

        private static bool IsAddressAlreadyInUse(Exception error)
        {
            SocketException socketException = error as SocketException;
            if (socketException != null)
                return socketException.SocketErrorCode == SocketError.AddressAlreadyInUse;

            return error != null
                   && !string.IsNullOrEmpty(error.Message)
                   && error.Message.IndexOf("address already in use", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static PortReleaseResult TryReleasePortOwner(int port)
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
                return PortReleaseResult.Failed("Automatic listener cleanup is only supported on Windows.");

            int? pid = FindTcpListenerPid(port);
            if (!pid.HasValue)
                return PortReleaseResult.Failed("Windows reports the port as occupied, but Patina could not resolve the owning PID.");

            int currentPid = System.Diagnostics.Process.GetCurrentProcess().Id;
            if (pid.Value == currentPid)
            {
                BridgeProbeResult probe = ProbeBridge(port);
                if (probe.Success)
                    return PortReleaseResult.Detached("A Patina bridge listener is already alive inside this Unity Editor. Start skipped rebinding and will report it as detached alive.");

                return PortReleaseResult.Failed("The port is owned by the current Unity Editor process, but Patina could not ping a live bridge on it. Restart Unity to clear the stale in-process listener.");
            }

            if (pid.Value <= 4)
                return PortReleaseResult.Failed($"The port is owned by protected Windows PID {pid.Value}; Patina will not terminate it automatically.");

            string processName = GetProcessName(pid.Value);
            if (string.IsNullOrWhiteSpace(processName))
                return PortReleaseResult.Poisoned(BuildPoisonedPortMessage(port, pid.Value));

            if (IsUnityProcess(processName))
                return PortReleaseResult.Failed($"The port is owned by Unity PID {pid.Value}. Patina will not force-close another Unity Editor automatically; close that editor or change its Patina port, then retry.");

            if (!IsPatinaBridgeProcess(processName))
                return PortReleaseResult.Failed($"The port is owned by PID {pid.Value} ({DescribeProcess(processName)}), which does not look like a Patina Unity bridge process. Patina will not terminate unrelated processes automatically.");

            ProcessResult killResult = RunProcess("taskkill.exe", "/PID " + pid.Value + " /F");
            if (killResult.ExitCode != 0)
                return PortReleaseResult.Failed($"Patina found PID {pid.Value} on the bridge port but taskkill failed: {killResult.Output}");

            Thread.Sleep(500);
            Debug.LogWarning($"[Patina] Terminated process {pid.Value} to release TCP bridge port {port}.");
            return PortReleaseResult.Success(pid.Value);
        }

        private static string GetProcessName(int pid)
        {
            try
            {
                using (System.Diagnostics.Process process = System.Diagnostics.Process.GetProcessById(pid))
                {
                    return process.ProcessName;
                }
            }
            catch
            {
                return null;
            }
        }

        private static bool IsUnityProcess(string processName)
        {
            if (string.IsNullOrWhiteSpace(processName))
                return false;

            return processName.Equals("Unity", StringComparison.OrdinalIgnoreCase)
                   || processName.Equals("Unity.exe", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsPatinaBridgeProcess(string processName)
        {
            if (string.IsNullOrWhiteSpace(processName))
                return false;

            return processName.Equals("patina-server", StringComparison.OrdinalIgnoreCase)
                   || processName.Equals("patina-server.exe", StringComparison.OrdinalIgnoreCase);
        }

        private static string DescribeProcess(string processName)
        {
            return string.IsNullOrWhiteSpace(processName) ? "unresolved process name" : processName;
        }

        private static string BuildPoisonedPortMessage(int port, int pid)
        {
            return $"Windows reports tcp://127.0.0.1:{port}/ as LISTENING under PID {pid}, but that PID is not visible as a running process. This is a poisoned or orphaned TCP listener outside Patina's safe user-mode cleanup path. Close MCP hosts using Patina and restart Windows to release the port; restarting Unity alone may not be enough.";
        }

        private static int? FindTcpListenerPid(int port)
        {
            ProcessResult result = RunProcess("netstat.exe", "-ano -p tcp");
            if (result.ExitCode != 0)
                return null;

            string marker = ":" + port;
            string[] lines = result.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string line in lines)
            {
                string trimmed = line.Trim();
                if (!trimmed.StartsWith("TCP", StringComparison.OrdinalIgnoreCase)
                    || trimmed.IndexOf(marker, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                string[] parts = trimmed.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 5)
                    continue;

                string localAddress = parts[1];
                string state = parts[3];
                string pidText = parts[4];
                if (!localAddress.Equals("127.0.0.1" + marker, StringComparison.OrdinalIgnoreCase)
                    || !state.Equals("LISTENING", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (int.TryParse(pidText, out int pid))
                    return pid;
            }

            return null;
        }

        private static ProcessResult RunProcess(string fileName, string arguments)
        {
            try
            {
                System.Diagnostics.ProcessStartInfo startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using (System.Diagnostics.Process process = System.Diagnostics.Process.Start(startInfo))
                {
                    if (process == null)
                        return new ProcessResult(-1, "Process did not start.");

                    Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
                    Task<string> errorTask = process.StandardError.ReadToEndAsync();

                    if (!process.WaitForExit(5000))
                    {
                        try
                        {
                            process.Kill();
                        }
                        catch
                        {
                        }

                        return new ProcessResult(-1, "Process timed out.");
                    }

                    if (!Task.WaitAll(new Task[] { outputTask, errorTask }, 1000))
                        return new ProcessResult(-1, "Process output timed out.");

                    string output = outputTask.Result;
                    string error = errorTask.Result;
                    return new ProcessResult(process.ExitCode, (output + " " + error).Trim());
                }
            }
            catch (Exception ex)
            {
                return new ProcessResult(-1, ex.Message);
            }
        }

        private static string BuildPortUnavailableMessage(int port, PortReleaseResult releaseResult, Exception retryError)
        {
            string retryMessage = retryError == null ? string.Empty : " Retry failed: " + retryError.Message;
            if (releaseResult.IsPoisonedPort)
                return $"Patina could not bind tcp://127.0.0.1:{port}/. {releaseResult.Message}{retryMessage}";

            return $"Patina could not bind tcp://127.0.0.1:{port}/ and could not automatically release the existing listener. {releaseResult.Message}{retryMessage} Close the process using the port or restart Unity, then retry.";
        }

        private sealed class PortReleaseResult
        {
            private PortReleaseResult(bool released, int? pid, string message, bool isPoisonedPort, bool detachedAlive)
            {
                Released = released;
                Pid = pid;
                Message = message;
                IsPoisonedPort = isPoisonedPort;
                DetachedAlive = detachedAlive;
            }

            public bool Released { get; }
            public int? Pid { get; }
            public string Message { get; }
            public bool IsPoisonedPort { get; }
            public bool DetachedAlive { get; }

            public static PortReleaseResult Success(int pid)
            {
                return new PortReleaseResult(true, pid, "Released the existing bridge listener.", false, false);
            }

            public static PortReleaseResult Failed(string message)
            {
                return new PortReleaseResult(false, null, message, false, false);
            }

            public static PortReleaseResult Poisoned(string message)
            {
                return new PortReleaseResult(false, null, message, true, false);
            }

            public static PortReleaseResult Detached(string message)
            {
                return new PortReleaseResult(false, null, message, false, true);
            }
        }

        private readonly struct BridgeProbeResult
        {
            private BridgeProbeResult(bool success, string message)
            {
                Success = success;
                Message = message;
            }

            public bool Success { get; }
            public string Message { get; }

            public static BridgeProbeResult Ok(string message)
            {
                return new BridgeProbeResult(true, message);
            }

            public static BridgeProbeResult Failed(string message)
            {
                return new BridgeProbeResult(false, message);
            }

            public static BridgeProbeResult NotAttempted()
            {
                return new BridgeProbeResult(false, string.Empty);
            }
        }

        private readonly struct ProcessResult
        {
            public ProcessResult(int exitCode, string output)
            {
                ExitCode = exitCode;
                Output = output;
            }

            public int ExitCode { get; }
            public string Output { get; }
        }

        private static void ListenLoop(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    TcpListener currentListener = _listener;
                    if (currentListener == null) break;

                    TcpClient client;
                    try
                    {
                        client = currentListener.AcceptTcpClient();
                    }
                    catch (SocketException)
                    {
                        break;
                    }
                    catch (ObjectDisposedException)
                    {
                        break;
                    }

                    RegisterClient(client);
                    try
                    {
                        client.NoDelay = true;
                        Task.Run(() => HandleClientAsync(client, token));
                    }
                    catch
                    {
                        UnregisterClient(client);
                        try
                        {
                            client.Close();
                        }
                        catch
                        {
                        }

                        throw;
                    }
                }
            }
            catch (Exception ex)
            {
                if (!token.IsCancellationRequested)
                {
                    SetLastError(ex.Message);
                }
            }
            finally
            {
                if (!token.IsCancellationRequested)
                {
                    Interlocked.Exchange(ref _isRunning, 0);
                    InvalidateStatusSnapshot();
                }
            }
        }

        private static async Task HandleClientAsync(TcpClient client, CancellationToken token)
        {
            // NOTE: CancellationToken is not propagated to ReadAsync/WriteAsync calls.
            // The socket is closed on Stop() which unblocks pending reads.
            try
            {
                using (client)
                using (NetworkStream stream = client.GetStream())
                {
                    while (!token.IsCancellationRequested && client.Connected)
                    {
                        string json = await ReadFrameAsync(stream).ConfigureAwait(false);
                        if (json == null)
                            break;

                        BridgeResponse response;
                        try
                        {
                            BridgeRequest request = JsonConvert.DeserializeObject<BridgeRequest>(json);
                            if (request != null && request.Command == BridgePingCommand)
                            {
                                response = BridgeResponse.Ok(request.Id, CreateBridgePingResult());
                            }
                            else if (request == null || string.IsNullOrEmpty(request.Command) || !CommandDispatcher.HasHandler(request.Command))
                            {
                                response = await CommandDispatcher.Dispatch(request).ConfigureAwait(false);
                            }
                            else if (IsEditorLikelyBlocked())
                            {
                                response = request.Command == "get_editor_state"
                                    ? BridgeResponse.Ok(request.Id, CreateBlockedEditorState())
                                    : CreateEditorBlockedResponse(request.Id);
                            }
                            else
                            {
                                response = await CommandDispatcher.Dispatch(request).ConfigureAwait(false);
                            }
                        }
                        catch (Exception ex)
                        {
                            response = BridgeResponse.Fail(null, $"Invalid request: {ex.Message}");
                        }

                        string responseJson = JsonConvert.SerializeObject(
                            response,
                            Formatting.None,
                            s_jsonSettings);

                        await WriteFrameAsync(stream, responseJson).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex)
            {
                if (!IsExpectedClientClose(ex, token))
                {
                    _lastError = ex.Message;
                    Debug.LogError($"[Patina] TCP client error: {ex.Message}");
                }
            }
            finally
            {
                UnregisterClient(client);
            }
        }

        private static JObject CreateBridgePingResult()
        {
            return new JObject
            {
                ["bridge"] = "patina-unity",
                ["sessionId"] = s_sessionId,
                ["unityProcessId"] = System.Diagnostics.Process.GetCurrentProcess().Id,
                ["packageVersion"] = GetPackageVersion(),
                ["managedRunning"] = IsRunning,
                ["trackedClientCount"] = ConnectedClients,
                ["port"] = Port
            };
        }

        private static string GetPackageVersion()
        {
            return UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(McpBridgeServer).Assembly)?.version ?? "unknown";
        }

        private static void RegisterClient(TcpClient client)
        {
            lock (s_clientsLock)
            {
                s_clients.Add(client);
            }
        }

        private static void UnregisterClient(TcpClient client)
        {
            lock (s_clientsLock)
            {
                s_clients.Remove(client);
            }
        }

        private static bool IsExpectedClientClose(Exception ex, CancellationToken token)
        {
            if (!token.IsCancellationRequested && IsRunning)
                return false;

            return ex is ObjectDisposedException
                   || ex is SocketException
                   || ex is InvalidOperationException
                   || ex is System.IO.IOException;
        }

        private static bool IsEditorLikelyBlocked()
        {
            return MainThreadQueue.TimeSinceLastUpdate.TotalSeconds >= MainThreadQueue.BlockedThresholdSeconds;
        }

        private static BridgeResponse CreateEditorBlockedResponse(string id)
        {
            TimeSpan age = MainThreadQueue.TimeSinceLastUpdate;
            int pendingCount = MainThreadQueue.PendingCount;
            string message =
                $"Unity has not processed editor updates for {age.TotalSeconds:F1}s, so Patina cannot safely service queued editor commands. " +
                "Unity may be waiting for input in a modal dialog, such as a save-changes prompt. " +
                $"Resolve the Unity popup or blocking operation, then retry. Pending Patina main-thread actions: {pendingCount}.";
            return BridgeResponse.Fail(id, message, "EDITOR_BLOCKED");
        }

        private static JObject CreateBlockedEditorState()
        {
            TimeSpan age = MainThreadQueue.TimeSinceLastUpdate;
            return new JObject
            {
                ["isServiceable"] = false,
                ["blockedByModalDialogLikely"] = true,
                ["mainThreadUpdateAgeSeconds"] = age.TotalSeconds,
                ["mainThreadQueuePendingCount"] = MainThreadQueue.PendingCount,
                ["status"] = "blocked",
                ["message"] = "Unity has stopped processing editor updates. It may be waiting for input in a modal dialog, such as a save-changes prompt. Resolve the Unity popup, then retry Patina commands."
            };
        }

        private static async Task<string> ReadFrameAsync(NetworkStream stream)
        {
            byte[] header = await ReadExactlyAsync(stream, 4).ConfigureAwait(false);
            if (header == null)
                return null;

            int length = header[0] | (header[1] << 8) | (header[2] << 16) | (header[3] << 24);
            if (length <= 0 || length > MaxFrameBytes)
                throw new InvalidOperationException($"Invalid frame length: {length}");

            byte[] payload = await ReadExactlyAsync(stream, length).ConfigureAwait(false);
            if (payload == null)
                return null;

            return Encoding.UTF8.GetString(payload, 0, payload.Length);
        }

        private static async Task WriteFrameAsync(NetworkStream stream, string payload)
        {
            byte[] body = Encoding.UTF8.GetBytes(payload);
            int len = body.Length;
            byte[] header = new byte[4];
            header[0] = (byte)(len & 0xFF);
            header[1] = (byte)((len >> 8) & 0xFF);
            header[2] = (byte)((len >> 16) & 0xFF);
            header[3] = (byte)((len >> 24) & 0xFF);
            await stream.WriteAsync(header, 0, header.Length).ConfigureAwait(false);
            await stream.WriteAsync(body, 0, body.Length).ConfigureAwait(false);
            await stream.FlushAsync().ConfigureAwait(false);
        }

        private static async Task<byte[]> ReadExactlyAsync(NetworkStream stream, int length)
        {
            byte[] buffer = new byte[length];
            int offset = 0;

            while (offset < length)
            {
                int read = await stream.ReadAsync(buffer, offset, length - offset).ConfigureAwait(false);
                if (read == 0)
                    return null;
                offset += read;
            }

            return buffer;
        }
    }
}
