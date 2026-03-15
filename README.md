# Patina

Control the Unity Editor from any MCP host. One package install, one click, and your AI assistant can see your scene, create objects, and talk to the console.

Patina is a Rust MCP server paired with a C# Unity bridge. It connects your favorite AI coding tool directly to the Unity Editor over a local TCP channel, with zero manual config.

## Why Patina?

- **One-click setup.** Install the UPM package, click a button, and every supported host is configured automatically.
- **No Rust required.** Release packages ship pre-built binaries for Windows, Linux, and macOS. Just install and go.
- **Built for speed.** The Rust sidecar keeps the MCP layer fast and lightweight while Unity stays on the main thread.
- **Multi-host.** Works with Claude Code, Cursor, VS Code, Gemini CLI, JetBrains Rider, Codex CLI, and more.

## How It Works

```
MCP Host  <-- stdio -->  Patina Server  <-- local TCP -->  Unity Editor
```

The host launches the Rust binary over stdio MCP. The Rust server forwards tool calls into Unity through a local loopback TCP bridge. Unity executes them on the main thread and returns the result.

## Quick Start

### 1. Install the Unity package
In Unity 6, add the Patina scoped registry for your chosen release channel, then install `com.taygunsavas.patina-unity-mcp` from Package Manager.

Patina is distributed as a complete Unity package artifact with the editor code, native Rust runtime binaries under `Plugins/<platform>/`, and the Unity metadata needed for import. End users do not need the Rust toolchain or a Git checkout of this repository.

### 2. Run One-Click Setup
Open **Window > Patina Unity MCP** and click **One-Click Setup**.

The setup flow verifies the binary, starts the Unity bridge, auto-configures every detected host, replaces stale entries, and shows restart guidance where needed.

### 3. Start building
Open your MCP host and try:
- *"Log hello to Unity console"*
- *"Show me the scene hierarchy"*
- *"Create a cube at position 0, 2, 0"*

## Available Tools

| Tool | What it does |
|------|-------------|
| `log_to_console` | Send messages to the Unity Console |
| `get_hierarchy` | Retrieve the full scene GameObject tree |
| `create_game_object` | Spawn GameObjects or built-in primitives |

## Supported Hosts

| Host | Setup |
|------|-------|
| Claude Code (Anthropic CLI) | Automatic |
| Cursor | Automatic |
| Visual Studio Code | Automatic |
| GitHub Copilot (VS Code) | Linked via VS Code config |
| Gemini CLI | Automatic |
| JetBrains Rider / Junie | Automatic |
| Codex CLI | Automatic |

The setup window also detects stale entries, missing hosts, and provides a clean **Remove Patina From Hosts** action.

## Roadmap

| Phase | Focus |
|-------|-------|
| **Phase 1** (current) | Core tools: console, hierarchy, object creation |
| **Phase 2** | Expanded coverage: scene management, asset operations, component editing |
| **Phase 3** | Distribution and reach: more hosts, registry publishing |

For the public contributor-facing roadmap, see [ROADMAP.md](ROADMAP.md).

## Release Flow

1. Keep `rust-server/Cargo.toml` and `unity-package/package.json` on the same semantic version.
2. Run the **release** workflow with a version tag like `v1.0.0`.
3. CI builds cross-platform binaries, assembles a complete Unity package artifact, validates the package layout, publishes the package to registry channels, and uploads secondary GitHub Release assets.
4. End users consume the published package artifact through registry-backed Unity Package Manager channels. No Rust toolchain or Git install path is required.

## Local Development

### Build and publish a contributor runtime
```bash
cd rust-server && cargo build --release
```
```powershell
pwsh -File scripts/publish-dev-runtime.ps1
```

In Unity, load the package from disk, open **Window > Patina Unity MCP**, enable **Use Local Runtime (Contributor)**, and click **One-Click Setup**. Host configs will point to the local dev runtime instead of the packaged binary.

### Stage a local UPM test package
```powershell
pwsh -File scripts/stage-local-upm.ps1
```
Then add the package from disk in Unity: `dist/local-upm/com.taygunsavas.patina-unity-mcp/package.json`.

See [CONTRIBUTING.md](CONTRIBUTING.md) for the full contributor workflow.

## Community and Contributing

- Start with [CONTRIBUTING.md](CONTRIBUTING.md) for the local development loop, validation expectations, and pull request guidance.
- Use GitHub issue forms for reproducible bugs, feature proposals, and usage questions so maintainers get the context they need.
- Read [.github/SUPPORT.md](.github/SUPPORT.md) before opening a help request.
- Read [.github/SECURITY.md](.github/SECURITY.md) for private vulnerability reporting.
- Read [.github/CODE_OF_CONDUCT.md](.github/CODE_OF_CONDUCT.md) before participating in issues and pull requests.
- Read [ROADMAP.md](ROADMAP.md) for current public priorities and direction.
- Pull requests targeting `main` are expected to pass CI and go through CODEOWNERS + Copilot review once repository rules are enabled.

## Requirements

- Unity 6 (6000.3 LTS+)
- Unity `6000.3.5f2+` is recommended for reliable signed-package behavior in Package Manager
- A supported MCP host
- Rust 1.75+ (contributors only)

## Distribution Channels

- OpenUPM and npm-compatible scoped registries are the primary install channels.
- GitHub Releases provide secondary downloadable package artifacts and release notes.
- Unity Asset Store is a separate release target and is not the primary install path for technical users.

## License

[MIT](LICENSE)
