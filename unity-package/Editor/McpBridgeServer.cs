using System;
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
    [InitializeOnLoad]
    public static class McpBridgeServer
    {
        private const string PortPrefsKey = "Patina.Port";
        private const int DefaultPort = 9800;
        private const int MaxFrameBytes = 4 * 1024 * 1024;

        private static TcpListener _listener;
        private static CancellationTokenSource _cts;
        private static Thread _listenerThread;
        private static int _connectedClients;

        private static int _isRunning;
        public static bool IsRunning => Interlocked.CompareExchange(ref _isRunning, 0, 0) == 1;

        private static volatile string _lastError;
        public static string LastError => _lastError;

        public static int Port => EditorPrefs.GetInt(PortPrefsKey, DefaultPort);
        public static int ConnectedClients => _connectedClients;

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
        }

        public static void Start()
        {
            if (IsRunning)
                return;

            _lastError = null;

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

                _lastError = BuildPortUnavailableMessage(port, releaseResult, retryError);
                Debug.LogError("[Patina] " + _lastError);
                return;
            }

            _lastError = error.Message;
            Debug.LogError($"[Patina] Failed to start TCP bridge on port {port}: {error.Message}");
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

        public static void Stop()
        {
            if (!IsRunning)
                return;

            Interlocked.Exchange(ref _isRunning, 0);

            try
            {
                if (_cts != null)
                    _cts.Cancel();
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
                _connectedClients = 0;
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
                _connectedClients = 0;
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
                return PortReleaseResult.Failed("The port is owned by the current Unity Editor process. Restart Unity to clear the stale in-process listener.");

            if (pid.Value <= 4)
                return PortReleaseResult.Failed($"The port is owned by protected Windows PID {pid.Value}; Patina will not terminate it automatically.");

            string processName = GetProcessName(pid.Value);
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
            return $"Patina could not bind tcp://127.0.0.1:{port}/ and could not automatically release the existing listener. {releaseResult.Message}{retryMessage} Close the process using the port or restart Unity, then retry.";
        }

        private sealed class PortReleaseResult
        {
            private PortReleaseResult(bool released, int? pid, string message)
            {
                Released = released;
                Pid = pid;
                Message = message;
            }

            public bool Released { get; }
            public int? Pid { get; }
            public string Message { get; }

            public static PortReleaseResult Success(int pid)
            {
                return new PortReleaseResult(true, pid, "Released the existing bridge listener.");
            }

            public static PortReleaseResult Failed(string message)
            {
                return new PortReleaseResult(false, null, message);
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

                    client.NoDelay = true;
                    Task.Run(() => HandleClientAsync(client, token));
                }
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                if (!token.IsCancellationRequested)
                    Debug.LogError($"[Patina] TCP listener error: {ex.Message}");
            }
        }

        private static async Task HandleClientAsync(TcpClient client, CancellationToken token)
        {
            // NOTE: CancellationToken is not propagated to ReadAsync/WriteAsync calls.
            // The socket is closed on Stop() which unblocks pending reads.
            Interlocked.Increment(ref _connectedClients);
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
                            if (request == null || string.IsNullOrEmpty(request.Command) || !CommandDispatcher.HasHandler(request.Command))
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
                _lastError = ex.Message;
                Debug.LogError($"[Patina] TCP client error: {ex.Message}");
            }
            finally
            {
                Interlocked.Decrement(ref _connectedClients);
            }
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
