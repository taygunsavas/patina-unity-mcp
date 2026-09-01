# Patina Unity MCP

Patina connects MCP hosts to the Unity Editor through a local Rust sidecar and a C# editor bridge. After installation, supported AI coding tools can inspect scenes, create GameObjects, read console logs, edit assets, and run editor commands through a compact MCP surface.

## Installation

In Unity Package Manager, choose **Add package by name** when using a scoped registry that contains this package, or install from the GitHub repository:

```text
https://github.com/taygunsavas/patina-unity-mcp.git?path=/unity-package
```

Open **Window > Patina Unity MCP** and click **One-Click Setup**. The setup flow verifies the bundled runtime, starts the Unity bridge, and configures detected MCP hosts.

## Requirements

- Unity 6 / 6000.3 LTS or newer
- A supported MCP host such as Claude Code, Cursor, VS Code, Gemini CLI, JetBrains Rider, or Codex CLI

## Capabilities

Patina exposes a compact MCP interface:

- `patina_capabilities` searches and describes available Unity commands.
- `patina_call` executes a selected Unity command.
- `patina_health` reports runtime, bridge, and editor state, including `blockedByModalDialogLikely` hints when Unity may be blocked by a modal dialog. Prefab stage save dialogs are handled programmatically via the `save_changes` parameter in `close_prefab_stage`.

The package includes prebuilt Patina server binaries for Windows, Linux, and Apple Silicon macOS under `Plugins/<platform>/`.

## Agent Feedback

When an agent using Patina finds a bug, missing command, or workflow gap, it should ask the user before filing feedback. With approval, the agent should follow repository rules, search existing issues, avoid duplicates, and include versions, MCP host, reproduction steps, expected and actual behavior, and relevant errors.

## Troubleshooting

If a command returns `EDITOR_BLOCKED`, check Unity for a save-changes prompt or other modal popup. For prefab stages, use `close_prefab_stage` with the `save_changes` parameter to resolve the situation. Resolve other prompts manually, then retry the command or run `patina_health` with `{"include_unity_state": true}`.

## Links

- Repository: https://github.com/taygunsavas/patina-unity-mcp
- Issues: https://github.com/taygunsavas/patina-unity-mcp/issues
- License: MIT
