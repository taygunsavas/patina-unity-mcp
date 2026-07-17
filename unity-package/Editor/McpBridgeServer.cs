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

            int port = Port;
            _cts = new CancellationTokenSource();
            _lastError = null;

            try
            {
                _listener = new TcpListener(IPAddress.Loopback, port);
                _listener.Server.NoDelay = true;
                _listener.Start();
                Interlocked.Exchange(ref _isRunning, 1);

                _listenerThread = new Thread(() => ListenLoop(_cts.Token));
                _listenerThread.IsBackground = true;
                _listenerThread.Name = "Patina-TcpBridge";
                _listenerThread.Start();

                Debug.Log($"[Patina] Bridge server started on tcp://127.0.0.1:{port}/");
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                Debug.LogError($"[Patina] Failed to start TCP bridge on port {port}: {ex.Message}");
                Interlocked.Exchange(ref _isRunning, 0);
                if (_listener != null)
                    _listener.Stop();
                _listener = null;
                if (_cts != null)
                    _cts.Dispose();
                _cts = null;
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
