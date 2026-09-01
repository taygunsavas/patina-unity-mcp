# Patina

[![npm version](https://img.shields.io/npm/v/com.taygunsavas.patina-unity-mcp.svg)](https://www.npmjs.com/package/com.taygunsavas.patina-unity-mcp)
[![MCP Registry](https://img.shields.io/badge/MCP%20Registry-io.github.taygunsavas%2Fpatina--unity--mcp-blue)](https://registry.modelcontextprotocol.io/v0.1/servers?search=io.github.taygunsavas/patina-unity-mcp)
[![Glama MCP server](https://glama.ai/mcp/servers/taygunsavas/patina-unity-mcp/badges/score.svg)](https://glama.ai/mcp/servers/taygunsavas/patina-unity-mcp)
[![Indexed on TensorBlock MCP Index](https://mcp-index.tensorblock.co/v1/servers/github-taygunsavas-patina-unity-mcp-8c8f04b5/badge.svg)](https://tensorblock.co/mcp/servers/github-taygunsavas-patina-unity-mcp-8c8f04b5)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

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
In Unity, open **Project Settings > Package Manager**, add a scoped registry, then install the package by name.

Scoped registry:

| Field | Value |
|------|-------|
| Name | `npmjs` |
| URL | `https://registry.npmjs.org` |
| Scope(s) | `com.taygunsavas` |

Then open the **Package Manager** (Window > Package Manager), click the **+** icon in the top left, select **Add package by name...**, and enter:

```text
com.taygunsavas.patina-unity-mcp
```

Registry releases are signed during the release workflow for Unity Package Manager verification.

Alternatively, open the **Package Manager**, click the **+** icon, select **Add package from git URL...**, and enter:

```text
https://github.com/taygunsavas/patina-unity-mcp.git?path=/unity-package
```

Patina is distributed as a complete Unity package artifact with the editor code, native Rust runtime binaries under `Plugins/<platform>/`, and the Unity metadata needed for import. End users do not need the Rust toolchain or a Git checkout of this repository.

### 2. Run One-Click Setup
Open **Window > Patina Unity MCP** and click **One-Click Setup**.

The setup flow verifies the binary, starts the Unity bridge, auto-configures every detected host, replaces stale entries, and shows restart guidance where needed.

Patina writes host configs to a stable user-level runtime path instead of a per-project Unity package cache path. When the package is updated, the Unity editor package refreshes that managed runtime automatically, so supported MCP hosts do not need to be reconfigured just because the package cache path changed.

### 3. Start building
Open your MCP host and try:
- *"Log hello to Unity console"*
- *"Show me the scene hierarchy"*
- *"Create a cube at position 0, 2, 0"*

## Available Capabilities

Patina keeps the advertised MCP surface compact so hosts do not need to load every Unity command schema into context. Agents should use:

| MCP tool | What it does |
|------|-------------|
| `patina_capabilities` | Search or browse the Unity command catalog; request schemas only for specific commands |
| `patina_call` | Execute a catalog command with JSON parameters |
| `patina_health` | Inspect Patina version, command count, bridge port, optional Unity editor state, and bridge diagnostics |

The 87 commands below are available through `patina_capabilities` and `patina_call`.

### Scene

| Tool | What it does |
|------|-------------|
| `get_hierarchy` | Retrieve the active scene's GameObject tree as nested JSON; supports `max_depth` and `name_filter` |
| `get_scene_info` | Active scene metadata (name, path, build index, root count, dirty state); pass `include_all_scenes` for all loaded scenes |
| `open_scene` | Open a scene by project-relative path; `mode` single (default) or additive |
| `save_scene` | Save the active scene or any loaded scene; supports Save As |
| `new_scene` | Create and save a new scene with optional empty or default-game-objects setup |

### GameObjects

| Tool | What it does |
|------|-------------|
| `create_game_object` | Spawn an empty GameObject or a built-in primitive (Cube, Sphere, Capsule, Cylinder, Plane, Quad) |
| `delete_game_object` | Permanently delete a GameObject and all its children |
| `duplicate_game_object` | Duplicate a GameObject and its children |
| `reparent_game_object` | Move a GameObject under a new parent; pass `null` to promote to scene root |
| `get_game_object_info` | Full details for a named GameObject: transform, tag, layer, and all component properties |
| `set_active_state` | Show or hide a GameObject via `SetActive()` |
| `set_tag` | Set the tag on a GameObject (tag must be registered in Tags & Layers) |
| `set_layer` | Set the layer by name; optionally apply to all children |
| `set_transform` | Set position, rotation (Euler), and/or scale in world or local space in one call |

### Components & Properties

| Tool | What it does |
|------|-------------|
| `add_component` | Add a component by short name (`Rigidbody`) or fully qualified name |
| `remove_component` | Remove a component by type name |
| `set_property` | Set any serialized property on a component using its SerializedObject path |
| `get_game_object_components` | Return a lightweight component list for a GameObject |

### Batch Operations

| Tool | What it does |
|------|-------------|
| `batch_set_properties` | Apply serialized property changes across multiple GameObjects |
| `batch_add_components` | Add components to multiple GameObjects |
| `batch_set_transform` | Apply transform changes to multiple GameObjects |

### Prefabs

| Tool | What it does |
|------|-------------|
| `create_prefab` | Save a scene GameObject as a prefab asset |
| `instantiate_prefab` | Instantiate a prefab into the scene at an optional world position |
| `get_prefab_info` | Inspect a prefab asset or scene instance; returns asset type, overrides list, and instance status |
| `unpack_prefab` | Sever a prefab instance link; `outermost` (default) or `completely` |
| `apply_prefab_overrides` | Apply all instance overrides back to the source prefab asset on disk |
| `revert_prefab_overrides` | Restore a prefab instance to match its source asset |
| `list_prefab_components` | List component types and instance IDs on a prefab asset; optionally includes child GameObjects with transform paths |
| `edit_prefab_asset` | Perform a batch of edit operations (add/remove component, add/remove child, set field) on a prefab asset, including object-reference fields |
| `open_prefab_stage` | Open a prefab asset in Unity's prefab stage for editing; exit using `close_prefab_stage` |
| `close_prefab_stage` | Close the active prefab stage; `save_changes` parameter resolves dirty stages without Unity's blocking save prompt, giving save or discard decision |

**Example: Set a component reference within a prefab.**

To point a serialized field at another component inside the same prefab, pass an `edit_prefab_asset` action with `set_field` and an object reference value. The `transform_path` inside `value` is resolved against the prefab root, not against the action's own `transform_path`:

```json
{
  "asset_path": "Assets/Prefabs/MyPrefab.prefab",
  "actions": [
    {
      "action_type": "set_field",
      "component_type": "MyNamespace.MyComponent",
      "field_name": "targetReference",
      "value": {"transform_path": "Container/Button", "component_type": "MyNamespace.TargetComponent"}
    }
  ]
}
```

### Assets

| Tool | What it does |
|------|-------------|
| `find_assets_by_type` | Search the Asset Database by type filter (`t:Material`, `t:Prefab`, `t:Texture2D`, etc.) |
| `find_assets_by_name` | Search the Asset Database by partial name match |
| `get_asset_info` | Metadata for an asset: GUID, type, file size, labels, and importer settings |
| `create_folder` | Create a new folder in the Asset Database |
| `move_asset` | Move an asset to a new project-relative path |
| `rename_asset` | Rename an asset in-place |
| `delete_asset` | Delete an asset by project-relative path |
| `refresh_asset_database` | Trigger `AssetDatabase.Refresh`; incremental or force-reimport |
| `set_asset_labels` | Replace the full label list on an asset |

### Materials

| Tool | What it does |
|------|-------------|
| `create_material` | Create a new Material asset; defaults to URP/Lit |
| `get_material_properties` | Read all exposed shader properties with names, types, and current values |
| `set_material_property` | Set a shader property (float, bool, color, vector, or texture path) |
| `assign_material` | Assign a Material to a specific Renderer slot |

### Scripts

| Tool | What it does |
|------|-------------|
| `create_script` | Create a new C# script from a template (`monobehaviour`, `scriptableobject`, `editor_window`, `plain_class`, `interface`) or verbatim content |
| `resolve_script_type` | Resolve a MonoScript GUID and asset path by its fully qualified C# type |
| `force_recompile` | Trigger a Unity script recompile via `AssetDatabase.Refresh(ForceUpdate)` |
| `compile_and_get_errors` | Trigger script recompile and return compiler errors only; also returns `compilationRan`, `domainReloadObserved`, and `reloadCount` |
| `get_compilation_errors` | Get the list of current compiler errors and warnings |
| `get_script_content` | Read the content of a script file in the project |
| `get_assembly_types` | List all types declared in a specific assembly |
| `request_script_reload` | Request a domain reload via `EditorUtility.RequestScriptReload()` and return once it completes, enabling observation of reload-time errors |

### Scriptable Objects

| Tool | What it does |
|------|-------------|
| `get_scriptable_object` | Read serialized fields from a ScriptableObject asset |
| `set_scriptable_object_field` | Set one serialized ScriptableObject field |

### Validation & Health

| Tool | What it does |
|------|-------------|
| `validate_scene` | Scan the active scene for quality issues (missing script references, null serialized fields, and broken prefab connections) |
| `validate_assets` | Validate a single prefab asset or a folder recursively for missing scripts, broken object references, and unassigned required serialized fields |
| `get_scene_stats` | Return lightweight statistics for the active scene (object count, component count, unique type counts, max depth, etc.) |

### Search & Query

| Tool | What it does |
|------|-------------|
| `find_game_objects_by_tag` | Find all active GameObjects with a given tag |
| `find_game_objects_by_component` | Find all scene objects that have a given component type |
| `find_game_objects_by_layer` | Find all scene objects on a given layer by name |
| `query_game_objects` | Find GameObjects matching compound filters |
| `find_game_objects_by_path` | Find GameObjects by hierarchy path prefix |

### Console

| Tool | What it does |
|------|-------------|
| `log_to_console` | Emit a message to the Unity Console (`info`, `warning`, or `error`) |
| `get_console_logs` | Read buffered console entries with per-entry `phase` field (`normal`, `reloadTeardown`, `reloadStartup`); filterable by type, capped by `max_results`; includes `reloadWindowEntryCount` |
| `clear_console` | Clear all console log entries |

### Editor State & Control

| Tool | What it does |
|------|-------------|
| `get_editor_state` | Current editor flags and main-thread responsiveness; returns a limited blocked state with `blockedByModalDialogLikely` if Unity is not processing editor updates |
| `get_project_settings` | Read-only snapshot of key project settings (version, build target, color space, physics gravity, etc.) |
| `set_play_mode` | Enter, exit, pause, unpause, or step play mode |
| `execute_menu_item` | Execute any Editor menu item by full path (e.g. `Assets/Refresh`) |
| `get_selection` | Return the current Editor selection (scene objects and/or asset paths) |
| `set_selection` | Set the Editor selection to specific GameObjects and/or asset paths |

### Undo

| Tool | What it does |
|------|-------------|
| `begin_undo_group` | Open a named Unity Undo group |
| `end_undo_group` | Collapse operations into the current Undo group |
| `undo` | Perform one or more Undo steps |
| `redo` | Perform one or more Redo steps |
| `get_undo_stack` | Return current Undo and Redo stack entry names |

### Build & Player Settings

| Tool | What it does |
|------|-------------|
| `get_build_settings` | Build Settings snapshot: active target, scripting backend, and full scene list |
| `set_build_scenes` | Replace the Build Settings scene list with an ordered list of scene paths |
| `get_player_settings` | Read Player Settings for a build target group (Standalone, Android, iOS, WebGL) |
| `set_player_settings` | Write Player Settings fields; only non-null fields are changed |
| `set_build_target` | Switch the active build target (blocks the main thread on large projects) |

### Test Runner

| Tool | What it does |
|------|-------------|
| `run_tests` | Start a Unity Test Runner execution |
| `get_test_results` | Return results from the most recent test run |
| `get_test_list` | List available Unity tests |

### Animation

| Tool | What it does |
|------|-------------|
| `get_animator_info` | Read Animator Controller parameters and state information |
| `set_animator_parameter` | Set an Animator parameter in play mode |
| `list_animation_clips` | List AnimationClip assets in the project |

## Supported Hosts

| Host | Setup |
|------|-------|
| Antigravity CLI (agy) | Automatic |
| Claude Code (Anthropic CLI) | Automatic (`~/.claude.json`) |
| Claude Desktop | Automatic |
| Cursor | Automatic |
| Visual Studio Code | Automatic |
| GitHub Copilot (VS Code) | Linked via VS Code config |
| Gemini CLI | Automatic |
| JetBrains Rider / Junie | Automatic |
| Codex CLI | Automatic |

The setup window also detects stale entries, missing hosts, and provides a clean **Remove Patina From Hosts** action.

## Troubleshooting

If a Patina call reports `EDITOR_BLOCKED`, or mentions that Unity may be waiting on a modal dialog, check the Unity Editor for a save-changes prompt or other blocking popup. Patina cannot safely run queued editor commands while Unity is waiting for user input. Resolve the Unity prompt, then retry the MCP command or run `patina_health` with `{"include_unity_state": true}`.

### Prefab Stage Save Dialogs

Patina addresses dirty prefab stages by responding to the save question programmatically rather than blocking on a modal. When you call `close_prefab_stage`, the `save_changes` parameter directly determines the outcome: `true` saves, `false` (default) discards changes. This applies only to that command call and does not alter Editor behavior when a human is working manually.

Once a modal dialog appears, Unity's main thread is blocked and remains blocked until a human closes it. Every queued command processes through the same blocked thread, so prevention is the only real solution. For agent-written editor scripts, the safe pattern is to write prefabs using `PrefabUtility.LoadPrefabContents`, apply changes to a `SerializedObject`, save with `PrefabUtility.SaveAsPrefabAsset`, then `PrefabUtility.UnloadPrefabContents`. This approach never opens a prefab stage. If a stage must be opened, close it with `close_prefab_stage`. Leaving a dirty stage and manually calling `StageUtility.GoToMainStage()` will lock the Editor.

Unity offers a startup flag, `-automated`, which disables dialogs process-wide. While this may be suitable for an Editor driven entirely by agents, it creates problems in hybrid workflows where a human and agent share the same Editor: unsaved changes trigger no prompts, and some operations silently cancel. Patina does not require `-automated`. Per-command dialog answering works reliably without it and behaves correctly even in an Editor opened with `-automated`.

To check the current Editor state and dialog automation status, call `patina_health` and examine `broker.sessions[].automated` in the default output, or `isAutomatedMode` and `dialogAutomationAvailable` when passing `{"include_unity_state": true}`.

## Roadmap

| Phase | Focus |
|-------|-------|
| **Phase 1** ✓ | Core tools: console, hierarchy, object creation |
| **Phase 2** ✓ | Expanded coverage: scene management, asset operations, component editing |
| **Phase 3** (current) | Distribution and reach: Git URL installation docs, package layout, release pipeline |

## Local Development

### Contributor source checkout
Use this when you are editing `unity-package/` or `rust-server/` directly from the repository:

1. Point Unity at the local package checkout.
   - `file:<your-clone-path>/unity-package`
2. Build the Rust server.
```bash
cd rust-server && cargo build --release
```
3. Publish the current binary into the local development runtime path.
```powershell
# Windows
pwsh -File scripts/publish-dev-runtime.ps1
```
```bash
# macOS / Linux
./scripts/publish-dev-runtime.sh
```
4. In Unity, open **Window > Patina Unity MCP**, enable **Use Local Runtime (Contributor)**, and click **One-Click Setup**.

This writes host configs against the local dev runtime instead of the managed packaged runtime. Re-run **One-Click Setup** after every new `cargo build --release` + `publish-dev-runtime` pass, and use **Remove Patina From Hosts** before switching back to the packaged flow.

### Stage a local UPM test package
Use this when you want to test the package as it will be published, not the raw source checkout:
```powershell
pwsh -File scripts/stage-local-upm.ps1
```
Then add the staged package from disk in Unity:
- `dist/local-upm/com.taygunsavas.patina-unity-mcp/package.json`

Prefer the staged local package when you are validating package layout, import behavior, or release packaging. Prefer the source checkout path when you are actively editing source and want the fastest edit-build-run loop.

See [CONTRIBUTING.md](CONTRIBUTING.md) for the full contributor workflow.

## Community and Contributing

- Start with [CONTRIBUTING.md](CONTRIBUTING.md) for the local development loop, validation expectations, and pull request guidance.
- Use GitHub issue forms for reproducible bugs, feature proposals, and usage questions so maintainers get the context they need.
- Read [.github/SUPPORT.md](.github/SUPPORT.md) before opening a help request.
- Read [.github/SECURITY.md](.github/SECURITY.md) for private vulnerability reporting.
- Read [.github/CODE_OF_CONDUCT.md](.github/CODE_OF_CONDUCT.md) before participating in issues and pull requests.
- Pull requests targeting `main` are expected to pass CI and go through CODEOWNERS + Copilot review once repository rules are enabled.

## Requirements

- Unity 6 (6000.3 LTS+)
- A supported MCP host
- Rust 1.75+ (contributors only)

## License

[MIT](LICENSE)
