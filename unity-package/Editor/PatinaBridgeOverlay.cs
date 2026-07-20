using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine;
using UnityEngine.UIElements;

namespace Patina.Editor
{
    [Overlay(typeof(SceneView), "Patina Bridge", true)]
    public sealed class PatinaBridgeOverlay : Overlay
    {
        private Label _statusDot;
        private Label _statusLabel;
        private Button _startButton;
        private Button _stopButton;
        private Button _restartButton;

        public override VisualElement CreatePanelContent()
        {
            VisualElement root = new VisualElement();
            root.style.flexDirection = FlexDirection.Row;
            root.style.alignItems = Align.Center;
            root.style.paddingLeft = 6;
            root.style.paddingRight = 6;
            root.style.paddingTop = 4;
            root.style.paddingBottom = 4;

            _statusDot = new Label("●");
            _statusDot.style.fontSize = 14;
            _statusDot.style.marginRight = 4;
            root.Add(_statusDot);

            _statusLabel = new Label("Patina");
            _statusLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _statusLabel.style.marginRight = 6;
            root.Add(_statusLabel);

            _startButton = CreateButton("Start", McpBridgeServer.Start);
            _stopButton = CreateButton("Stop", McpBridgeServer.Stop);
            _restartButton = CreateButton("Restart", McpBridgeServer.Restart);
            Button openButton = CreateButton("Open", McpBridgeWindow.ShowWindow);

            root.Add(_startButton);
            root.Add(_stopButton);
            root.Add(_restartButton);
            root.Add(openButton);

            Refresh();
            root.schedule.Execute(Refresh).Every(750);
            return root;
        }

        private static Button CreateButton(string text, System.Action action)
        {
            Button button = new Button(action) { text = text };
            button.style.height = 22;
            button.style.marginLeft = 2;
            button.style.marginRight = 2;
            return button;
        }

        private void Refresh()
        {
            if (_statusLabel == null)
                return;

            BridgeStatusSnapshot status = McpBridgeServer.GetStatusSnapshot();
            _statusDot.style.color = GetDotColor(status.State);
            _statusLabel.text = GetShortLabel(status.State);
            _statusLabel.tooltip = status.Message;
            _startButton.SetEnabled(CanStart(status.State));
            _stopButton.SetEnabled(CanStop(status.State));
            _restartButton.SetEnabled(true);
        }

        private static bool CanStart(BridgeRuntimeState state)
        {
            return state == BridgeRuntimeState.Stopped
                   || state == BridgeRuntimeState.Error
                   || state == BridgeRuntimeState.PortOwnedByOtherProcess
                   || state == BridgeRuntimeState.PoisonedPort
                   || state == BridgeRuntimeState.StaleInProcess;
        }

        private static bool CanStop(BridgeRuntimeState state)
        {
            return state == BridgeRuntimeState.Running;
        }

        private static string GetShortLabel(BridgeRuntimeState state)
        {
            switch (state)
            {
                case BridgeRuntimeState.Running:
                    return "Patina";
                case BridgeRuntimeState.DetachedAlive:
                    return "Patina*";
                case BridgeRuntimeState.PortOwnedByOtherProcess:
                    return "Busy";
                case BridgeRuntimeState.StaleInProcess:
                    return "Stale";
                case BridgeRuntimeState.PoisonedPort:
                    return "Poisoned";
                case BridgeRuntimeState.Error:
                    return "Error";
                default:
                    return "Stopped";
            }
        }

        private static Color GetDotColor(BridgeRuntimeState state)
        {
            switch (state)
            {
                case BridgeRuntimeState.Running:
                    return new Color(0.2f, 0.85f, 0.25f);
                case BridgeRuntimeState.DetachedAlive:
                case BridgeRuntimeState.Starting:
                case BridgeRuntimeState.Stopping:
                case BridgeRuntimeState.PortOwnedByOtherProcess:
                    return new Color(0.95f, 0.7f, 0.2f);
                default:
                    return new Color(0.9f, 0.25f, 0.25f);
            }
        }
    }
}
