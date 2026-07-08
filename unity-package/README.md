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
- `patina_health` reports runtime, bridge, and editor state.

The package includes prebuilt Patina server binaries for Windows, Linux, and Apple Silicon macOS under `Plugins/<platform>/`.

## Agent Feedback

When an agent using Patina finds a bug, missing command, or workflow gap, it should ask the user before filing feedback. With approval, the agent should follow repository rules, search existing issues, avoid duplicates, and include versions, MCP host, reproduction steps, expected and actual behavior, and relevant errors.

## Links

- Repository: https://github.com/taygunsavas/patina-unity-mcp
- Issues: https://github.com/taygunsavas/patina-unity-mcp/issues
- License: MIT
