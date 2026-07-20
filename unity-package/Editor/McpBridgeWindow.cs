using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Patina.Editor
{
    public class McpBridgeWindow : EditorWindow
    {
        private const string PortPrefsKey = "Patina.Port";
        private const int DefaultPort = 9800;
        private const string AutoOpenSessionKey = "Patina.WindowAutoOpened";

        private Label _summaryLabel;
        private Label _statusLabel;
        private Label _portDisplayLabel;
        private Label _clientsLabel;
        private Label _runtimeSourceLabel;
        private Label _binaryPathLabel;
        private Label _configPathLabel;
        private HelpBox _serverHelpBox;
        private HelpBox _registrationHelpBox;
        private HelpBox _setupHelpBox;
        private HelpBox _developmentHelpBox;
        private IntegerField _portField;
        private Toggle _localRuntimeToggle;
        private Button _startButton;
        private Button _stopButton;
        private Button _restartButton;
        private Button _setupButton;
        private Button _removeButton;
        private ScrollView _resultsContainer;
        private IReadOnlyList<HostSetupResult> _lastSetupResults;

        static McpBridgeWindow()
        {
            EditorApplication.delayCall += AutoOpenOnFirstSession;
        }

        [MenuItem("Window/Patina Unity MCP")]
        public static void ShowWindow()
        {
            McpBridgeWindow window = GetWindow<McpBridgeWindow>();
            window.titleContent = new GUIContent("Patina Unity MCP");
            window.minSize = new Vector2(320, 420);
        }

        private static void AutoOpenOnFirstSession()
        {
            if (SessionState.GetBool(AutoOpenSessionKey, false))
                return;

            SessionState.SetBool(AutoOpenSessionKey, true);
            ShowWindow();
        }

        public void CreateGUI()
        {
            VisualElement root = rootVisualElement;
            root.Clear();
            root.style.flexDirection = FlexDirection.Column;
            root.style.flexGrow = 1;
            root.style.paddingTop = 10;
            root.style.paddingLeft = 10;
            root.style.paddingRight = 10;
            root.style.paddingBottom = 10;

            root.Add(CreateHeader());
            root.Add(CreateSetupSection());
            root.Add(CreateResultsSection());
            root.Add(CreateDiagnosticsSection());
            root.Add(CreateAdvancedSection());

            RefreshUI();
            root.schedule.Execute(RefreshUI).Every(750);
        }

        private VisualElement CreateHeader()
        {
            VisualElement container = new VisualElement();
            container.style.marginBottom = 8;

            Label title = new Label("Patina Unity MCP");
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.fontSize = 16;
            container.Add(title);

            Label subtitle = new Label("Install once via UPM, then run a single setup flow for supported MCP hosts.");
            subtitle.style.whiteSpace = WhiteSpace.Normal;
            subtitle.style.color = new Color(0.75f, 0.75f, 0.75f);
            subtitle.style.marginTop = 2;
            container.Add(subtitle);

            return container;
        }

        private VisualElement CreateSetupSection()
        {
            GroupBox box = new GroupBox { text = "One-Click Setup" };
            box.style.marginBottom = 8;

            _summaryLabel = new Label("Ready to configure local host integrations automatically.");
            _summaryLabel.style.whiteSpace = WhiteSpace.Normal;
            _summaryLabel.style.marginBottom = 6;
            box.Add(_summaryLabel);

            _setupHelpBox = new HelpBox(
                "Automatic setup updates supported local MCP hosts, refreshes stale Patina entries, and keeps linked integrations aligned through shared host config files.",
                HelpBoxMessageType.Info);
            _setupHelpBox.style.marginBottom = 6;
            box.Add(_setupHelpBox);

            _localRuntimeToggle = new Toggle("Use Local Runtime (Contributor)");
            _localRuntimeToggle.value = ProcessManager.IsLocalRuntimeOverrideRequested;
            _localRuntimeToggle.RegisterValueChangedCallback(OnLocalRuntimeChanged);
            _localRuntimeToggle.style.marginBottom = 6;
            box.Add(_localRuntimeToggle);

            _developmentHelpBox = new HelpBox(string.Empty, HelpBoxMessageType.Info);
            _developmentHelpBox.style.marginBottom = 6;
            box.Add(_developmentHelpBox);

            VisualElement actionsRow = new VisualElement();
            actionsRow.style.flexDirection = FlexDirection.Row;
            actionsRow.style.flexWrap = Wrap.Wrap;

            _setupButton = new Button(OnOneClickSetup) { text = "One-Click Setup" };
            _setupButton.style.height = 32;
            _setupButton.style.flexGrow = 1;
            _setupButton.style.minWidth = 150;
            _setupButton.style.marginBottom = 6;
            actionsRow.Add(_setupButton);

            _removeButton = new Button(OnRemovePatinaFromHosts) { text = "Remove Patina From Hosts" };
            _removeButton.style.height = 32;
            _removeButton.style.flexGrow = 1;
            _removeButton.style.minWidth = 150;
            _removeButton.style.marginBottom = 6;
            actionsRow.Add(_removeButton);

            box.Add(actionsRow);

            return box;
        }

        private VisualElement CreateResultsSection()
        {
            GroupBox box = new GroupBox { text = "Host Results" };
            box.style.marginBottom = 8;

            _resultsContainer = new ScrollView();
            _resultsContainer.style.flexGrow = 1;
            _resultsContainer.style.maxHeight = 280;
            _resultsContainer.style.minHeight = 120;
            box.Add(_resultsContainer);

            return box;
        }

        private VisualElement CreateDiagnosticsSection()
        {
            GroupBox box = new GroupBox { text = "Diagnostics" };
            box.style.marginBottom = 8;
            box.Add(CreateInfoRow("Runtime Source", out _runtimeSourceLabel));
            box.Add(CreateInfoRow("Host Runtime", out _binaryPathLabel));
            box.Add(CreateInfoRow("Package Id", out _configPathLabel));

            _registrationHelpBox = new HelpBox(string.Empty, HelpBoxMessageType.Info);
            _registrationHelpBox.style.marginTop = 6;
            box.Add(_registrationHelpBox);

            return box;
        }

        private VisualElement CreateAdvancedSection()
        {
            Foldout foldout = new Foldout
            {
                text = "Advanced Bridge Controls",
                value = false
            };

            VisualElement statusBox = new VisualElement();
            statusBox.style.marginTop = 4;
            statusBox.Add(CreateInfoRow("Bridge Status", out _statusLabel));
            statusBox.Add(CreateInfoRow("Port", out _portDisplayLabel));
            statusBox.Add(CreateInfoRow("Connected Clients", out _clientsLabel));

            _serverHelpBox = new HelpBox(string.Empty, HelpBoxMessageType.Info);
            _serverHelpBox.style.marginTop = 6;
            statusBox.Add(_serverHelpBox);

            VisualElement configRow = new VisualElement();
            configRow.style.flexDirection = FlexDirection.Row;
            configRow.style.flexWrap = Wrap.Wrap;
            configRow.style.alignItems = Align.Center;
            configRow.style.marginTop = 6;

            int savedPort = EditorPrefs.GetInt(PortPrefsKey, DefaultPort);
            _portField = new IntegerField("Port")
            {
                value = savedPort
            };
            _portField.style.flexGrow = 1;

            Button applyButton = new Button(OnApplyPort) { text = "Apply" };
            applyButton.style.width = 70;
            applyButton.style.minWidth = 70;
            applyButton.style.marginBottom = 4;

            configRow.Add(_portField);
            configRow.Add(applyButton);
            statusBox.Add(configRow);

            VisualElement controlsRow = new VisualElement();
            controlsRow.style.flexDirection = FlexDirection.Row;
            controlsRow.style.flexWrap = Wrap.Wrap;
            controlsRow.style.marginTop = 6;

            _startButton = new Button(McpBridgeServer.Start) { text = "Start Server" };
            _startButton.style.flexGrow = 1;
            _startButton.style.height = 28;
            _startButton.style.minWidth = 140;
            _startButton.style.marginBottom = 4;

            _stopButton = new Button(McpBridgeServer.Stop) { text = "Stop Server" };
            _stopButton.style.flexGrow = 1;
            _stopButton.style.height = 28;
            _stopButton.style.minWidth = 140;
            _stopButton.style.marginBottom = 4;

            _restartButton = new Button(McpBridgeServer.Restart) { text = "Restart Server" };
            _restartButton.style.flexGrow = 1;
            _restartButton.style.height = 28;
            _restartButton.style.minWidth = 140;
            _restartButton.style.marginBottom = 4;

            controlsRow.Add(_startButton);
            controlsRow.Add(_stopButton);
            controlsRow.Add(_restartButton);
            statusBox.Add(controlsRow);

            foldout.Add(statusBox);
            return foldout;
        }

        private void RefreshUI()
        {
            BridgeStatusSnapshot bridgeStatus = McpBridgeServer.GetStatusSnapshot();
            bool running = bridgeStatus.IsBridgeUsable;
            string binaryPath = ProcessManager.FindServerBinary();
            bool hasBinary = !string.IsNullOrEmpty(binaryPath);
            string runtimeStatusMessage = ProcessManager.GetRuntimeStatusMessage();
            ProcessManager.RuntimeSourceKind runtimeSourceKind = ProcessManager.GetRuntimeSourceKind();

            _summaryLabel.text = _lastSetupResults == null
                ? "Ready to configure local host integrations automatically."
                : HostSetupManager.BuildOverallSummary(_lastSetupResults);

            _statusLabel.text = GetBridgeStateLabel(bridgeStatus.State);
            _statusLabel.style.color = GetBridgeStateColor(bridgeStatus.State);

            _portDisplayLabel.text = bridgeStatus.Port.ToString();
            _clientsLabel.text = bridgeStatus.TrackedClientCount.ToString();
            _runtimeSourceLabel.text = ProcessManager.GetRuntimeSourceLabel();
            _binaryPathLabel.text = hasBinary
                ? binaryPath
                : runtimeStatusMessage;
            _configPathLabel.text = ProcessManager.GetPackageId();

            _registrationHelpBox.text = "Per-host setup state is shown in Host Results. " + runtimeStatusMessage + " One-Click Setup writes supported hosts to the stable Patina runtime and replaces stale entries when needed.";
            _registrationHelpBox.messageType = HelpBoxMessageType.Info;

            bool contributorModeAvailable = ProcessManager.IsContributorModeAvailable();
            if (!contributorModeAvailable && ProcessManager.IsLocalRuntimeOverrideRequested)
            {
                ProcessManager.IsLocalRuntimeOverrideRequested = false;
            }

            _setupButton.SetEnabled(hasBinary);
            _removeButton.SetEnabled(true);
            _startButton.SetEnabled(CanStartBridge(bridgeStatus.State));
            _stopButton.SetEnabled(CanStopBridge(bridgeStatus.State));
            _restartButton.SetEnabled(true);
            _localRuntimeToggle.style.display = contributorModeAvailable ? DisplayStyle.Flex : DisplayStyle.None;
            _localRuntimeToggle.SetValueWithoutNotify(ProcessManager.IsLocalRuntimeOverrideRequested);
            _developmentHelpBox.text = contributorModeAvailable
                ? runtimeStatusMessage
                : "Packaged runtime is active. Local runtime overrides are only available from a local source checkout or a project with a published dev runtime.";
            _developmentHelpBox.messageType = runtimeSourceKind == ProcessManager.RuntimeSourceKind.Contributor
                ? HelpBoxMessageType.Warning
                : runtimeSourceKind == ProcessManager.RuntimeSourceKind.Missing
                    ? HelpBoxMessageType.Error
                    : HelpBoxMessageType.Info;

            _serverHelpBox.text = BuildBridgeHelpText(bridgeStatus);
            _serverHelpBox.messageType = GetBridgeHelpType(bridgeStatus.State);

            if (_lastSetupResults == null)
            {
                _setupHelpBox.text = hasBinary
                    ? "Automatic setup updates supported local MCP hosts, removes stale Patina entries, and keeps linked integrations aligned through the stable Patina runtime."
                    : runtimeStatusMessage;
                _setupHelpBox.messageType = hasBinary ? HelpBoxMessageType.Info : HelpBoxMessageType.Error;
            }
        }

        private static bool CanStartBridge(BridgeRuntimeState state)
        {
            return state == BridgeRuntimeState.Stopped
                   || state == BridgeRuntimeState.Error
                   || state == BridgeRuntimeState.PortOwnedByOtherProcess
                   || state == BridgeRuntimeState.PoisonedPort
                   || state == BridgeRuntimeState.StaleInProcess;
        }

        private static bool CanStopBridge(BridgeRuntimeState state)
        {
            return state == BridgeRuntimeState.Running;
        }

        private static string GetBridgeStateLabel(BridgeRuntimeState state)
        {
            switch (state)
            {
                case BridgeRuntimeState.Running:
                    return "Running";
                case BridgeRuntimeState.DetachedAlive:
                    return "Detached Alive";
                case BridgeRuntimeState.StaleInProcess:
                    return "Stale In-Process";
                case BridgeRuntimeState.PoisonedPort:
                    return "Poisoned Port";
                case BridgeRuntimeState.PortOwnedByOtherProcess:
                    return "Port Busy";
                case BridgeRuntimeState.Error:
                    return "Error";
                case BridgeRuntimeState.Starting:
                    return "Starting";
                case BridgeRuntimeState.Stopping:
                    return "Stopping";
                default:
                    return "Stopped";
            }
        }

        private static Color GetBridgeStateColor(BridgeRuntimeState state)
        {
            switch (state)
            {
                case BridgeRuntimeState.Running:
                    return new Color(0.2f, 0.8f, 0.2f);
                case BridgeRuntimeState.DetachedAlive:
                case BridgeRuntimeState.Starting:
                case BridgeRuntimeState.Stopping:
                case BridgeRuntimeState.PortOwnedByOtherProcess:
                    return new Color(0.9f, 0.7f, 0.2f);
                default:
                    return new Color(0.85f, 0.3f, 0.3f);
            }
        }

        private static HelpBoxMessageType GetBridgeHelpType(BridgeRuntimeState state)
        {
            switch (state)
            {
                case BridgeRuntimeState.Running:
                    return HelpBoxMessageType.Info;
                case BridgeRuntimeState.DetachedAlive:
                case BridgeRuntimeState.Starting:
                case BridgeRuntimeState.Stopping:
                case BridgeRuntimeState.PortOwnedByOtherProcess:
                    return HelpBoxMessageType.Warning;
                default:
                    return HelpBoxMessageType.Error;
            }
        }

        private static string BuildBridgeHelpText(BridgeStatusSnapshot status)
        {
            string owner = status.ListenerPid.HasValue
                ? $" Listener PID: {status.ListenerPid.Value} ({(string.IsNullOrEmpty(status.ListenerProcessName) ? "unknown" : status.ListenerProcessName)})."
                : string.Empty;
            string ping = string.IsNullOrEmpty(status.PingMessage) ? string.Empty : " Ping: " + status.PingMessage;
            return status.Message + owner + ping;
        }

        private void OnOneClickSetup()
        {
            if (!McpBridgeServer.IsRunning)
                McpBridgeServer.Start();

            _lastSetupResults = HostSetupManager.RunOneClickSetup();
            RebuildResults();

            bool failed = false;
            bool restartRequired = false;
            foreach (HostSetupResult result in _lastSetupResults)
            {
                failed |= result.Status == HostSetupStatus.Failed;
                restartRequired |= result.RequiresRestart;
            }

            _setupHelpBox.text = restartRequired
                ? "Setup completed. Restart any configured host before testing tool calls."
                : "Setup completed.";
            _setupHelpBox.messageType = failed ? HelpBoxMessageType.Warning : HelpBoxMessageType.Info;

            RefreshUI();
        }

        private void OnRemovePatinaFromHosts()
        {
            _lastSetupResults = HostSetupManager.RemovePatinaFromHosts();
            RebuildResults();

            bool failed = false;
            bool removed = false;
            foreach (HostSetupResult result in _lastSetupResults)
            {
                failed |= result.Status == HostSetupStatus.Failed;
                removed |= result.Status == HostSetupStatus.Removed;
            }

            _setupHelpBox.text = removed
                ? "Patina was removed from detected host configs. Restart affected hosts before testing again."
                : "No Patina entries were present to remove.";
            _setupHelpBox.messageType = failed ? HelpBoxMessageType.Warning : HelpBoxMessageType.Info;

            RefreshUI();
        }

        private void OnLocalRuntimeChanged(ChangeEvent<bool> evt)
        {
            ProcessManager.IsLocalRuntimeOverrideRequested = evt.newValue;
            _lastSetupResults = null;
            RefreshUI();
        }

        private void RebuildResults()
        {
            _resultsContainer.Clear();

            if (_lastSetupResults == null || _lastSetupResults.Count == 0)
            {
                _resultsContainer.Add(new Label("No setup results yet."));
                return;
            }

            foreach (HostSetupResult result in _lastSetupResults)
                _resultsContainer.Add(CreateResultCard(result));
        }

        private VisualElement CreateResultCard(HostSetupResult result)
        {
            Box card = new Box();
            card.style.marginBottom = 6;
            card.style.paddingTop = 6;
            card.style.paddingBottom = 6;
            card.style.paddingLeft = 6;
            card.style.paddingRight = 6;

            VisualElement titleRow = new VisualElement();
            titleRow.style.flexDirection = FlexDirection.Row;
            titleRow.style.flexWrap = Wrap.Wrap;

            Label title = new Label(result.DisplayName);
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.flexGrow = 1;
            title.style.whiteSpace = WhiteSpace.Normal;
            title.style.marginBottom = 4;
            titleRow.Add(title);

            Label badge = new Label(result.Status.ToString());
            badge.style.color = GetStatusColor(result.Status);
            badge.style.unityFontStyleAndWeight = FontStyle.Bold;
            badge.style.unityTextAlign = TextAnchor.MiddleLeft;
            badge.style.marginBottom = 4;
            titleRow.Add(badge);

            card.Add(titleRow);

            Label summary = new Label(result.Summary);
            summary.style.whiteSpace = WhiteSpace.Normal;
            summary.style.marginTop = 4;
            card.Add(summary);

            if (!string.IsNullOrEmpty(result.ConfigPath))
            {
                Label pathLabel = new Label("Path: " + result.ConfigPath);
                pathLabel.style.whiteSpace = WhiteSpace.Normal;
                pathLabel.style.marginTop = 4;
                card.Add(pathLabel);
            }

            if (!string.IsNullOrEmpty(result.ExportSnippet))
            {
                TextField snippet = new TextField("Export Snippet")
                {
                    multiline = true,
                    value = result.ExportSnippet,
                    isReadOnly = true
                };
                snippet.style.marginTop = 6;
                snippet.style.minHeight = 84;
                snippet.style.flexGrow = 1;
                card.Add(snippet);
            }

            if (result.RequiresRestart)
            {
                HelpBox restart = new HelpBox(GetRestartHelpText(result.Status), HelpBoxMessageType.Warning);
                restart.style.marginTop = 6;
                card.Add(restart);
            }

            return card;
        }

        private static Color GetStatusColor(HostSetupStatus status)
        {
            switch (status)
            {
                case HostSetupStatus.Configured:
                    return new Color(0.2f, 0.8f, 0.2f);
                case HostSetupStatus.UpdatedStaleEntry:
                    return new Color(0.25f, 0.75f, 0.95f);
                case HostSetupStatus.Removed:
                    return new Color(0.95f, 0.55f, 0.2f);
                case HostSetupStatus.NotPresent:
                    return new Color(0.65f, 0.65f, 0.65f);
                case HostSetupStatus.NotDetected:
                    return new Color(0.8f, 0.7f, 0.2f);
                default:
                    return new Color(0.85f, 0.3f, 0.3f);
            }
        }

        private static string GetRestartHelpText(HostSetupStatus status)
        {
            switch (status)
            {
                case HostSetupStatus.Removed:
                    return "Restart this host to unload the removed Patina MCP entry.";
                default:
                    return "Restart this host after applying the config snippet or automatic setup.";
            }
        }

        private void OnApplyPort()
        {
            int newPort = _portField.value;
            if (newPort > 0 && newPort < 65536)
            {
                EditorPrefs.SetInt(PortPrefsKey, newPort);
                McpBridgeServer.SetPort(newPort);

                if (McpBridgeServer.IsRunning)
                {
                    McpBridgeServer.Stop();
                    McpBridgeServer.Start();
                }

                Debug.Log($"[Patina] Port set to {newPort}.");
                RefreshUI();
            }
            else
            {
                Debug.LogWarning("[Patina] Invalid port number. Must be between 1 and 65535.");
                _portField.value = EditorPrefs.GetInt(PortPrefsKey, DefaultPort);
            }
        }

        private static VisualElement CreateInfoRow(string label, out Label valueLabel)
        {
            VisualElement row = new VisualElement();
            row.style.flexDirection = FlexDirection.Column;
            row.style.marginBottom = 6;

            Label nameLabel = new Label(label + ":");
            nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            nameLabel.style.marginBottom = 2;
            row.Add(nameLabel);

            valueLabel = new Label(string.Empty);
            valueLabel.style.whiteSpace = WhiteSpace.Normal;
            valueLabel.style.flexGrow = 1;
            row.Add(valueLabel);

            return row;
        }
    }
}



