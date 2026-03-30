use std::sync::Arc;

use rmcp::{
    handler::server::{tool::ToolRouter, wrapper::Parameters},
    model::*,
    tool, tool_handler, tool_router, ErrorData as McpError, ServerHandler,
};

use crate::bridge::BridgeClient;
use crate::tools::{
    AddComponentArgs, ApplyPrefabOverridesArgs, AssignMaterialArgs, ClearConsoleArgs,
    CreateFolderArgs, CreateGameObjectArgs, CreateMaterialArgs, CreatePrefabArgs, CreateScriptArgs,
    DeleteAssetArgs, DeleteGameObjectArgs, DuplicateGameObjectArgs, ExecuteMenuItemArgs,
    FindAssetsByNameArgs, FindAssetsByTypeArgs, FindGameObjectsByComponentArgs,
    FindGameObjectsByLayerArgs, FindGameObjectsByTagArgs, GetAssetInfoArgs, GetBuildSettingsArgs,
    GetConsoleLogsArgs, GetEditorStateArgs, GetGameObjectInfoArgs, GetHierarchyArgs,
    GetMaterialPropertiesArgs, GetPrefabInfoArgs, GetSceneInfoArgs, InstantiatePrefabArgs,
    LogToConsoleArgs, MoveAssetArgs, NewSceneArgs, OpenSceneArgs, RefreshAssetDatabaseArgs,
    RemoveComponentArgs, RenameAssetArgs, ReparentGameObjectArgs, RevertPrefabOverridesArgs,
    GetPlayerSettingsArgs, SaveSceneArgs, SetAssetLabelsArgs, SetBuildScenesArgs,
    SetBuildTargetArgs, SetMaterialPropertyArgs, SetPlayModeArgs, SetPlayerSettingsArgs,
    SetPropertyArgs, GetSelectionArgs, SetSelectionArgs, UnpackPrefabArgs,
    GetProjectSettingsArgs, SetActiveStateArgs, SetLayerArgs, SetTagArgs, SetTransformArgs,
};

#[derive(Clone)]
pub struct UnityMcpServer {
    bridge: Arc<BridgeClient>,
    tool_router: ToolRouter<Self>,
}

impl UnityMcpServer {
    pub fn new(bridge: Arc<BridgeClient>) -> Self {
        Self {
            bridge,
            tool_router: Self::tool_router(),
        }
    }

    async fn call_bridge(
        &self,
        command: &str,
        params: serde_json::Value,
    ) -> Result<CallToolResult, McpError> {
        match self.bridge.request(command, params).await {
            Ok(response) => {
                if response.success {
                    let text = response
                        .result
                        .map(|v| {
                            serde_json::to_string(&v).unwrap_or_else(|e| {
                                tracing::warn!("Failed to serialize bridge response: {}", e);
                                format!("{:?}", v)
                            })
                        })
                        .unwrap_or_else(|| "OK".to_string());
                    Ok(CallToolResult::success(vec![Content::text(text)]))
                } else {
                    let err_msg = response
                        .error
                        .map(|e| format!("[{}] {}", e.code, e.message))
                        .unwrap_or_else(|| "Unknown bridge error".to_string());
                    Ok(CallToolResult::error(vec![Content::text(err_msg)]))
                }
            }
            Err(e) => Ok(CallToolResult::error(vec![Content::text(e)])),
        }
    }
}

#[tool_router]
impl UnityMcpServer {
    // === Phase 1 tools (existing) ===

    #[tool(
        name = "log_to_console",
        description = "Emit a message to the Unity Editor Console. Use level=warning or level=error to colour the entry; defaults to info. Returns OK."
    )]
    async fn log_to_console(
        &self,
        Parameters(args): Parameters<LogToConsoleArgs>,
    ) -> Result<CallToolResult, McpError> {
        let params = serde_json::to_value(&args)
            .map_err(|e| McpError::internal_error(e.to_string(), None))?;
        self.call_bridge("log_to_console", params).await
    }

    #[tool(
        name = "get_hierarchy",
        description = "Return the active scene's GameObject tree as nested JSON. Use max_depth (e.g. 2) on large scenes to limit output; omit for the full tree. Use name_filter to narrow to matching subtrees."
    )]
    async fn get_hierarchy(
        &self,
        Parameters(args): Parameters<GetHierarchyArgs>,
    ) -> Result<CallToolResult, McpError> {
        let params = serde_json::to_value(&args)
            .map_err(|e| McpError::internal_error(e.to_string(), None))?;
        self.call_bridge("get_hierarchy", params).await
    }

    #[tool(
        name = "create_game_object",
        description = "Create an empty GameObject or a built-in primitive (Cube, Sphere, Capsule, Cylinder, Plane, Quad) at an optional world position. Returns the created object's name."
    )]
    async fn create_game_object(
        &self,
        Parameters(args): Parameters<CreateGameObjectArgs>,
    ) -> Result<CallToolResult, McpError> {
        let params = serde_json::to_value(&args)
            .map_err(|e| McpError::internal_error(e.to_string(), None))?;
        self.call_bridge("create_game_object", params).await
    }

    // === Phase 2 tools (new) ===

    #[tool(
        name = "get_scene_info",
        description = "Return metadata for the active scene: name, path, buildIndex, rootCount, isDirty. Pass include_all_scenes=true to also list all currently loaded scenes."
    )]
    async fn get_scene_info(
        &self,
        Parameters(args): Parameters<GetSceneInfoArgs>,
    ) -> Result<CallToolResult, McpError> {
        let params = serde_json::to_value(&args)
            .map_err(|e| McpError::internal_error(e.to_string(), None))?;
        self.call_bridge("get_scene_info", params).await
    }

    #[tool(
        name = "add_component",
        description = "Add a Unity component to a named GameObject. Accepts short names (Rigidbody) or fully qualified names (UnityEngine.Rigidbody). Returns {gameObject, component, instanceId} on success."
    )]
    async fn add_component(
        &self,
        Parameters(args): Parameters<AddComponentArgs>,
    ) -> Result<CallToolResult, McpError> {
        let params = serde_json::to_value(&args)
            .map_err(|e| McpError::internal_error(e.to_string(), None))?;
        self.call_bridge("add_component", params).await
    }

    #[tool(
        name = "set_property",
        description = "Set a serialized property on a component attached to a named GameObject. Returns {gameObject, component, property, success} on success."
    )]
    async fn set_property(
        &self,
        Parameters(args): Parameters<SetPropertyArgs>,
    ) -> Result<CallToolResult, McpError> {
        let params = serde_json::to_value(&args)
            .map_err(|e| McpError::internal_error(e.to_string(), None))?;
        self.call_bridge("set_property", params).await
    }

    #[tool(
        name = "remove_component",
        description = "Remove a component from a named GameObject by type name. Returns OK on success."
    )]
    async fn remove_component(
        &self,
        Parameters(args): Parameters<RemoveComponentArgs>,
    ) -> Result<CallToolResult, McpError> {
        let params = serde_json::to_value(&args)
            .map_err(|e| McpError::internal_error(e.to_string(), None))?;
        self.call_bridge("remove_component", params).await
    }

    #[tool(
        name = "reparent_game_object",
        description = "Move a GameObject under a new parent. Pass null new_parent_name to promote to scene root. Preserves world position by default."
    )]
    async fn reparent_game_object(
        &self,
        Parameters(args): Parameters<ReparentGameObjectArgs>,
    ) -> Result<CallToolResult, McpError> {
        let params = serde_json::to_value(&args)
            .map_err(|e| McpError::internal_error(e.to_string(), None))?;
        self.call_bridge("reparent_game_object", params).await
    }

    #[tool(
        name = "delete_game_object",
        description = "Permanently delete a named GameObject and all its children from the active scene."
    )]
    async fn delete_game_object(
        &self,
        Parameters(args): Parameters<DeleteGameObjectArgs>,
    ) -> Result<CallToolResult, McpError> {
        let params = serde_json::to_value(&args)
            .map_err(|e| McpError::internal_error(e.to_string(), None))?;
        self.call_bridge("delete_game_object", params).await
    }

    #[tool(
        name = "duplicate_game_object",
        description = "Duplicate a named GameObject and all its children. Optionally provide new_name; Unity derives one from the original if omitted."
    )]
    async fn duplicate_game_object(
        &self,
        Parameters(args): Parameters<DuplicateGameObjectArgs>,
    ) -> Result<CallToolResult, McpError> {
        let params = serde_json::to_value(&args)
            .map_err(|e| McpError::internal_error(e.to_string(), None))?;
        self.call_bridge("duplicate_game_object", params).await
    }

    #[tool(
        name = "create_prefab",
        description = "Save a scene GameObject as a prefab asset at the given Assets/… path. The source object stays in the scene. Returns the saved asset path."
    )]
    async fn create_prefab(
        &self,
        Parameters(args): Parameters<CreatePrefabArgs>,
    ) -> Result<CallToolResult, McpError> {
        let params = serde_json::to_value(&args)
            .map_err(|e| McpError::internal_error(e.to_string(), None))?;
        self.call_bridge("create_prefab", params).await
    }

    #[tool(
        name = "instantiate_prefab",
        description = "Instantiate a prefab from an Assets/… path into the active scene at an optional world position. Returns the name of the new scene object."
    )]
    async fn instantiate_prefab(
        &self,
        Parameters(args): Parameters<InstantiatePrefabArgs>,
    ) -> Result<CallToolResult, McpError> {
        let params = serde_json::to_value(&args)
            .map_err(|e| McpError::internal_error(e.to_string(), None))?;
        self.call_bridge("instantiate_prefab", params).await
    }

    #[tool(
        name = "get_prefab_info",
        description = "Inspect a prefab asset or scene prefab instance. Returns prefabAssetPath, prefabAssetType, isInstance, hasOverrides, and an overrides list with propertyPath/instanceValue/prefabValue per entry. Pass target as an asset path (\"Assets/…\") or scene GameObject name; target_type is auto-detected if omitted."
    )]
    async fn get_prefab_info(
        &self,
        Parameters(args): Parameters<GetPrefabInfoArgs>,
    ) -> Result<CallToolResult, McpError> {
        let params = serde_json::to_value(&args)
            .map_err(|e| McpError::internal_error(e.to_string(), None))?;
        self.call_bridge("get_prefab_info", params).await
    }

    #[tool(
        name = "unpack_prefab",
        description = "Unpack a scene prefab instance, severing its link to the prefab asset. mode \"outermost\" (default) leaves nested prefabs intact; \"completely\" unpacks all nesting levels. Registers undo. Returns {gameObject, mode, success}."
    )]
    async fn unpack_prefab(
        &self,
        Parameters(args): Parameters<UnpackPrefabArgs>,
    ) -> Result<CallToolResult, McpError> {
        let params = serde_json::to_value(&args)
            .map_err(|e| McpError::internal_error(e.to_string(), None))?;
        self.call_bridge("unpack_prefab", params).await
    }

    #[tool(
        name = "apply_prefab_overrides",
        description = "Apply all overrides from a scene prefab instance back to the source prefab asset on disk. Requires the GameObject to be a prefab instance. Registers undo. Returns {gameObject, appliedToPath, success}."
    )]
    async fn apply_prefab_overrides(
        &self,
        Parameters(args): Parameters<ApplyPrefabOverridesArgs>,
    ) -> Result<CallToolResult, McpError> {
        let params = serde_json::to_value(&args)
            .map_err(|e| McpError::internal_error(e.to_string(), None))?;
        self.call_bridge("apply_prefab_overrides", params).await
    }

    #[tool(
        name = "revert_prefab_overrides",
        description = "Revert all overrides on a scene prefab instance, restoring it to match the source prefab asset. Requires the GameObject to be a prefab instance. Registers undo. Returns {gameObject, success}."
    )]
    async fn revert_prefab_overrides(
        &self,
        Parameters(args): Parameters<RevertPrefabOverridesArgs>,
    ) -> Result<CallToolResult, McpError> {
        let params = serde_json::to_value(&args)
            .map_err(|e| McpError::internal_error(e.to_string(), None))?;
        self.call_bridge("revert_prefab_overrides", params).await
    }

    #[tool(
        name = "find_assets_by_type",
        description = "Search the Asset Database by Unity type filter (t:Material, t:Prefab, t:Texture2D, t:AudioClip, t:ScriptableObject, t:Mesh). Returns a list of matching asset paths."
    )]
    async fn find_assets_by_type(
        &self,
        Parameters(args): Parameters<FindAssetsByTypeArgs>,
    ) -> Result<CallToolResult, McpError> {
        let params = serde_json::to_value(&args)
            .map_err(|e| McpError::internal_error(e.to_string(), None))?;
        self.call_bridge("find_assets_by_type", params).await
    }

    #[tool(
        name = "find_assets_by_name",
        description = "Search the Asset Database by partial name match. Returns matching asset paths. Combine with find_assets_by_type for finer results."
    )]
    async fn find_assets_by_name(
        &self,
        Parameters(args): Parameters<FindAssetsByNameArgs>,
    ) -> Result<CallToolResult, McpError> {
        let params = serde_json::to_value(&args)
            .map_err(|e| McpError::internal_error(e.to_string(), None))?;
        self.call_bridge("find_assets_by_name", params).await
    }

    // === Phase 5 tools — Asset database operations ===

    #[tool(
        name = "create_folder",
        description = "Create a new folder in the Asset Database. Returns {path, guid, success}."
    )]
    async fn create_folder(
        &self,
        Parameters(args): Parameters<CreateFolderArgs>,
    ) -> Result<CallToolResult, McpError> {
        let params = serde_json::to_value(&args)
            .map_err(|e| McpError::internal_error(e.to_string(), None))?;
        self.call_bridge("create_folder", params).await
    }

    #[tool(
        name = "move_asset",
        description = "Move an asset to a new project-relative path. Destination folder must already exist. Returns {sourcePath, destinationPath, success}."
    )]
    async fn move_asset(
        &self,
        Parameters(args): Parameters<MoveAssetArgs>,
    ) -> Result<CallToolResult, McpError> {
        let params = serde_json::to_value(&args)
            .map_err(|e| McpError::internal_error(e.to_string(), None))?;
        self.call_bridge("move_asset", params).await
    }

    #[tool(
        name = "rename_asset",
        description = "Rename an asset in-place (no extension in new_name). Returns {oldPath, newPath, success}."
    )]
    async fn rename_asset(
        &self,
        Parameters(args): Parameters<RenameAssetArgs>,
    ) -> Result<CallToolResult, McpError> {
        let params = serde_json::to_value(&args)
            .map_err(|e| McpError::internal_error(e.to_string(), None))?;
        self.call_bridge("rename_asset", params).await
    }

    #[tool(
        name = "delete_asset",
        description = "Delete an asset at the given project-relative path. Returns error for non-existent paths. Returns {deletedPath, success}."
    )]
    async fn delete_asset(
        &self,
        Parameters(args): Parameters<DeleteAssetArgs>,
    ) -> Result<CallToolResult, McpError> {
        let params = serde_json::to_value(&args)
            .map_err(|e| McpError::internal_error(e.to_string(), None))?;
        self.call_bridge("delete_asset", params).await
    }

    #[tool(
        name = "get_asset_info",
        description = "Return metadata for an asset at a project-relative path. Returns {path, guid, assetType, fileSize, labels, importer, importerSettings} where importerSettings contains scalar importer property values (int, float, bool, string)."
    )]
    async fn get_asset_info(
        &self,
        Parameters(args): Parameters<GetAssetInfoArgs>,
    ) -> Result<CallToolResult, McpError> {
        let params = serde_json::to_value(&args)
            .map_err(|e| McpError::internal_error(e.to_string(), None))?;
        self.call_bridge("get_asset_info", params).await
    }

    #[tool(
        name = "refresh_asset_database",
        description = "Trigger AssetDatabase.Refresh. import_options: \"default\" (incremental) or \"force_update\" (reimport all). Returns {success}."
    )]
    async fn refresh_asset_database(
        &self,
        Parameters(args): Parameters<RefreshAssetDatabaseArgs>,
    ) -> Result<CallToolResult, McpError> {
        let params = serde_json::to_value(&args)
            .map_err(|e| McpError::internal_error(e.to_string(), None))?;
        self.call_bridge("refresh_asset_database", params).await
    }

    #[tool(
        name = "set_asset_labels",
        description = "Replace the label list on an asset. Call get_asset_info first to inspect current labels. Returns {path, labels, success}."
    )]
    async fn set_asset_labels(
        &self,
        Parameters(args): Parameters<SetAssetLabelsArgs>,
    ) -> Result<CallToolResult, McpError> {
        let params = serde_json::to_value(&args)
            .map_err(|e| McpError::internal_error(e.to_string(), None))?;
        self.call_bridge("set_asset_labels", params).await
    }

    // === Phase 4 tools — Inspection and spatial search ===

    #[tool(
        name = "get_game_object_info",
        description = "Return full details for a named GameObject: transform, tag, layer, static flag, scene path, and all attached components with their serialized properties. Set include_component_properties=false to get component names only. Properties use SerializedObject paths (depth ≤ 3, arrays ≤ 32 elements)."
    )]
    async fn get_game_object_info(
        &self,
        Parameters(args): Parameters<GetGameObjectInfoArgs>,
    ) -> Result<CallToolResult, McpError> {
        let params = serde_json::to_value(&args)
            .map_err(|e| McpError::internal_error(e.to_string(), None))?;
        self.call_bridge("get_game_object_info", params).await
    }

    #[tool(
        name = "find_game_objects_by_tag",
        description = "Find all active GameObjects with the given tag. Returns {tag, count, objects:[{name, instanceId, scenePath}]}. max_results caps output (default 50)."
    )]
    async fn find_game_objects_by_tag(
        &self,
        Parameters(args): Parameters<FindGameObjectsByTagArgs>,
    ) -> Result<CallToolResult, McpError> {
        let params = serde_json::to_value(&args)
            .map_err(|e| McpError::internal_error(e.to_string(), None))?;
        self.call_bridge("find_game_objects_by_tag", params).await
    }

    #[tool(
        name = "find_game_objects_by_component",
        description = "Find all scene objects that have the given component type. Accepts short (Rigidbody) or fully qualified (UnityEngine.Rigidbody) names. Returns {componentType, count, objects:[{name, instanceId, scenePath}]}. max_results caps output (default 50)."
    )]
    async fn find_game_objects_by_component(
        &self,
        Parameters(args): Parameters<FindGameObjectsByComponentArgs>,
    ) -> Result<CallToolResult, McpError> {
        let params = serde_json::to_value(&args)
            .map_err(|e| McpError::internal_error(e.to_string(), None))?;
        self.call_bridge("find_game_objects_by_component", params)
            .await
    }

    #[tool(
        name = "find_game_objects_by_layer",
        description = "Find all scene objects on the given layer by name (e.g. \"Default\", \"UI\"). Returns {layerName, layerIndex, count, objects:[{name, instanceId, scenePath}]}. Returns an error if the layer name is not defined. max_results caps output (default 50)."
    )]
    async fn find_game_objects_by_layer(
        &self,
        Parameters(args): Parameters<FindGameObjectsByLayerArgs>,
    ) -> Result<CallToolResult, McpError> {
        let params = serde_json::to_value(&args)
            .map_err(|e| McpError::internal_error(e.to_string(), None))?;
        self.call_bridge("find_game_objects_by_layer", params).await
    }

    // === Phase 3 tools — Scene lifecycle ===

    #[tool(
        name = "open_scene",
        description = "Open a scene by project-relative path (e.g. Assets/Scenes/Main.unity). mode=\"single\" (default) closes the current scene; mode=\"additive\" keeps it open. Returns an error with dirty_warning if the active scene has unsaved changes in single mode."
    )]
    async fn open_scene(
        &self,
        Parameters(args): Parameters<OpenSceneArgs>,
    ) -> Result<CallToolResult, McpError> {
        let params = serde_json::to_value(&args)
            .map_err(|e| McpError::internal_error(e.to_string(), None))?;
        self.call_bridge("open_scene", params).await
    }

    #[tool(
        name = "save_scene",
        description = "Save the active scene, or the scene at scene_path. Provide save_as_path to write a copy to a new location (Save As). Returns {savedPath, success}."
    )]
    async fn save_scene(
        &self,
        Parameters(args): Parameters<SaveSceneArgs>,
    ) -> Result<CallToolResult, McpError> {
        let params = serde_json::to_value(&args)
            .map_err(|e| McpError::internal_error(e.to_string(), None))?;
        self.call_bridge("save_scene", params).await
    }

    #[tool(
        name = "new_scene",
        description = "Create and save a new scene. save_path defaults to Assets/Scenes/<name>.unity. setup=\"empty\" omits the default Camera and Directional Light; default is \"default_game_objects\". Returns {name, path, success}."
    )]
    async fn new_scene(
        &self,
        Parameters(args): Parameters<NewSceneArgs>,
    ) -> Result<CallToolResult, McpError> {
        let params = serde_json::to_value(&args)
            .map_err(|e| McpError::internal_error(e.to_string(), None))?;
        self.call_bridge("new_scene", params).await
    }

    #[tool(
        name = "get_build_settings",
        description = "Return the project's Build Settings: activeBuildTarget, scriptingBackend, and the full scene list including disabled scenes. Returns {activeBuildTarget, scriptingBackend, scenes:[{path, enabled, buildIndex}]}."
    )]
    async fn get_build_settings(
        &self,
        Parameters(args): Parameters<GetBuildSettingsArgs>,
    ) -> Result<CallToolResult, McpError> {
        let params = serde_json::to_value(&args)
            .map_err(|e| McpError::internal_error(e.to_string(), None))?;
        self.call_bridge("get_build_settings", params).await
    }

    #[tool(
        name = "set_build_scenes",
        description = "Replace the Build Settings scene list with the provided ordered scene_paths. All listed scenes are enabled; any previously listed scenes not in the new list are removed. scene_paths must be a JSON array of strings — passing a plain string causes a -32602 parse error. Returns {sceneCount, success}."
    )]
    async fn set_build_scenes(
        &self,
        Parameters(args): Parameters<SetBuildScenesArgs>,
    ) -> Result<CallToolResult, McpError> {
        let params = serde_json::to_value(&args)
            .map_err(|e| McpError::internal_error(e.to_string(), None))?;
        self.call_bridge("set_build_scenes", params).await
    }

    #[tool(
        name = "create_script",
        description = "Create a new C# script file in the Unity project. Supply a template (monobehaviour, scriptableobject, editor_window, plain_class, interface) or pass content for verbatim output. The folder_path must already exist. Returns {path, className, template, success}."
    )]
    async fn create_script(
        &self,
        Parameters(args): Parameters<CreateScriptArgs>,
    ) -> Result<CallToolResult, McpError> {
        let params = serde_json::to_value(&args)
            .map_err(|e| McpError::internal_error(e.to_string(), None))?;
        self.call_bridge("create_script", params).await
    }

    #[tool(
        name = "create_material",
        description = "Create a new Material asset in the Asset Database with an optional shader. Defaults to Universal Render Pipeline/Lit. Returns {path, shader, success}."
    )]
    async fn create_material(
        &self,
        Parameters(args): Parameters<CreateMaterialArgs>,
    ) -> Result<CallToolResult, McpError> {
        let params = serde_json::to_value(&args)
            .map_err(|e| McpError::internal_error(e.to_string(), None))?;
        self.call_bridge("create_material", params).await
    }

    #[tool(
        name = "set_material_property",
        description = "Set a shader property on a Material asset. Dispatches by value type: float, bool, [r,g,b,a] color, [x,y,z,w] vector, or texture path string. Validates property existence before writing. Returns {materialPath, property, success}."
    )]
    async fn set_material_property(
        &self,
        Parameters(args): Parameters<SetMaterialPropertyArgs>,
    ) -> Result<CallToolResult, McpError> {
        let params = serde_json::to_value(&args)
            .map_err(|e| McpError::internal_error(e.to_string(), None))?;
        self.call_bridge("set_material_property", params).await
    }

    #[tool(
        name = "get_material_properties",
        description = "Read all exposed shader properties from a Material asset including names, types, and current values. Uses ShaderUtil Editor API. Returns {materialPath, shader, properties:[{name, type, value}]}."
    )]
    async fn get_material_properties(
        &self,
        Parameters(args): Parameters<GetMaterialPropertiesArgs>,
    ) -> Result<CallToolResult, McpError> {
        let params = serde_json::to_value(&args)
            .map_err(|e| McpError::internal_error(e.to_string(), None))?;
        self.call_bridge("get_material_properties", params).await
    }

    #[tool(
        name = "assign_material",
        description = "Assign a Material asset to a specific slot on a GameObject's Renderer. Use material_index for multi-material meshes; defaults to slot 0. Records undo. Returns {gameObject, materialPath, materialIndex, success}."
    )]
    async fn assign_material(
        &self,
        Parameters(args): Parameters<AssignMaterialArgs>,
    ) -> Result<CallToolResult, McpError> {
        let params = serde_json::to_value(&args)
            .map_err(|e| McpError::internal_error(e.to_string(), None))?;
        self.call_bridge("assign_material", params).await
    }

    // === Phase 3 tools — editor state and control ===

    #[tool(
        name = "get_editor_state",
        description = "Return the current Unity Editor state. Call this before any mutation to guard against compile-in-progress errors. Returns {isCompiling, isPlaying, isPaused, isUpdating, hasCompileErrors, unityVersion, projectPath}."
    )]
    async fn get_editor_state(
        &self,
        Parameters(args): Parameters<GetEditorStateArgs>,
    ) -> Result<CallToolResult, McpError> {
        let params = serde_json::to_value(&args)
            .map_err(|e| McpError::internal_error(e.to_string(), None))?;
        self.call_bridge("get_editor_state", params).await
    }

    #[tool(
        name = "set_play_mode",
        description = "Enter, exit, pause, unpause, or step play mode. mode accepts: enter, exit, pause, unpause, step. Note: enter is asynchronous — returns after setting the flag, not after play mode is fully active. Note: exit triggers a domain reload which drops the MCP connection briefly; always follow exit with get_editor_state to confirm isPlaying is false before proceeding. Returns {requestedMode, success}."
    )]
    async fn set_play_mode(
        &self,
        Parameters(args): Parameters<SetPlayModeArgs>,
    ) -> Result<CallToolResult, McpError> {
        let params = serde_json::to_value(&args)
            .map_err(|e| McpError::internal_error(e.to_string(), None))?;
        self.call_bridge("set_play_mode", params).await
    }

    #[tool(
        name = "get_console_logs",
        description = "Read buffered Unity console log entries. filter accepts: all, errors, warnings, logs (default all). max_results caps the returned count (default 50). Returns {totalReturned, entries:[{type,message,stackTrace}]}."
    )]
    async fn get_console_logs(
        &self,
        Parameters(args): Parameters<GetConsoleLogsArgs>,
    ) -> Result<CallToolResult, McpError> {
        let params = serde_json::to_value(&args)
            .map_err(|e| McpError::internal_error(e.to_string(), None))?;
        self.call_bridge("get_console_logs", params).await
    }

    #[tool(
        name = "execute_menu_item",
        description = "Execute a Unity Editor menu item by its full path, e.g. Assets/Refresh. Returns {menuPath, success} where success is false if the item does not exist or is disabled."
    )]
    async fn execute_menu_item(
        &self,
        Parameters(args): Parameters<ExecuteMenuItemArgs>,
    ) -> Result<CallToolResult, McpError> {
        let params = serde_json::to_value(&args)
            .map_err(|e| McpError::internal_error(e.to_string(), None))?;
        self.call_bridge("execute_menu_item", params).await
    }

    #[tool(
        name = "clear_console",
        description = "Clear all Unity Editor console log entries. Returns {success}."
    )]
    async fn clear_console(
        &self,
        Parameters(args): Parameters<ClearConsoleArgs>,
    ) -> Result<CallToolResult, McpError> {
        let params = serde_json::to_value(&args)
            .map_err(|e| McpError::internal_error(e.to_string(), None))?;
        self.call_bridge("clear_console", params).await
    }

    // === Issue #27 — Build and player settings tools ===

    #[tool(
        name = "get_player_settings",
        description = "Read Unity Player Settings for a build target group. build_target_group accepts: Standalone (default), Android, iOS, WebGL. Returns {productName, companyName, bundleVersion, applicationIdentifier, scriptingBackend, apiCompatibilityLevel, colorSpace}."
    )]
    async fn get_player_settings(
        &self,
        Parameters(args): Parameters<GetPlayerSettingsArgs>,
    ) -> Result<CallToolResult, McpError> {
        let params = serde_json::to_value(&args)
            .map_err(|e| McpError::internal_error(e.to_string(), None))?;
        self.call_bridge("get_player_settings", params).await
    }

    #[tool(
        name = "set_player_settings",
        description = "Write Unity Player Settings fields. Only non-null fields are written; unspecified fields are unchanged. build_target_group accepts: Standalone (default), Android, iOS, WebGL. Calls AssetDatabase.SaveAssets() after any mutation. Returns {changed: [...fieldNames], success}."
    )]
    async fn set_player_settings(
        &self,
        Parameters(args): Parameters<SetPlayerSettingsArgs>,
    ) -> Result<CallToolResult, McpError> {
        let params = serde_json::to_value(&args)
            .map_err(|e| McpError::internal_error(e.to_string(), None))?;
        self.call_bridge("set_player_settings", params).await
    }

    #[tool(
        name = "set_build_target",
        description = "Switch the active Unity build target. Accepts build_target strings like StandaloneWindows64, Android, WebGL, iOS. WARNING: blocks the main thread for 30–120 seconds on large projects. Returns {previousTarget, newTarget, success}."
    )]
    async fn set_build_target(
        &self,
        Parameters(args): Parameters<SetBuildTargetArgs>,
    ) -> Result<CallToolResult, McpError> {
        let params = serde_json::to_value(&args)
            .map_err(|e| McpError::internal_error(e.to_string(), None))?;
        self.call_bridge("set_build_target", params).await
    }

    // === Issue #28 — Editor selection tools ===

    #[tool(
        name = "get_selection",
        description = "Return the current Editor selection. gameObjects lists scene objects with name and instanceId; assetPaths lists selected project assets by path; count is the total selection count. Returns {gameObjects:[{name,instanceId}], assetPaths:[...], count}."
    )]
    async fn get_selection(
        &self,
        Parameters(args): Parameters<GetSelectionArgs>,
    ) -> Result<CallToolResult, McpError> {
        let params = serde_json::to_value(&args)
            .map_err(|e| McpError::internal_error(e.to_string(), None))?;
        self.call_bridge("get_selection", params).await
    }

    #[tool(
        name = "set_selection",
        description = "Set the Editor selection to the specified GameObjects and/or asset paths. Provide game_object_names for scene objects and/or asset_paths for project assets; both lists are combined into a single multi-selection. Returns an error for any unresolvable name or path. Returns {selectedCount, success}."
    )]
    async fn set_selection(
        &self,
        Parameters(args): Parameters<SetSelectionArgs>,
    ) -> Result<CallToolResult, McpError> {
        let params = serde_json::to_value(&args)
            .map_err(|e| McpError::internal_error(e.to_string(), None))?;
        self.call_bridge("set_selection", params).await
    }

    // === Issue #29 — Convenience GameObject operation tools ===

    #[tool(
        name = "set_active_state",
        description = "Set the active state of a named GameObject. Unlike set_property, this calls SetActive() directly since activeSelf is not a serialized property. Returns {gameObject, active, success}."
    )]
    async fn set_active_state(
        &self,
        Parameters(args): Parameters<SetActiveStateArgs>,
    ) -> Result<CallToolResult, McpError> {
        let params = serde_json::to_value(&args)
            .map_err(|e| McpError::internal_error(e.to_string(), None))?;
        self.call_bridge("set_active_state", params).await
    }

    #[tool(
        name = "set_tag",
        description = "Set the tag on a named GameObject. The tag must be registered in Project Settings > Tags and Layers; returns a descriptive error if unregistered. Records undo. Returns {gameObject, tag, success}."
    )]
    async fn set_tag(
        &self,
        Parameters(args): Parameters<SetTagArgs>,
    ) -> Result<CallToolResult, McpError> {
        let params = serde_json::to_value(&args)
            .map_err(|e| McpError::internal_error(e.to_string(), None))?;
        self.call_bridge("set_tag", params).await
    }

    #[tool(
        name = "set_layer",
        description = "Set the layer on a named GameObject by layer name (e.g. \"Default\", \"UI\"). Returns an error if the layer is not defined. Set apply_to_children=true to also apply to all descendants. Records undo. Returns {gameObject, layerName, layerIndex, success}."
    )]
    async fn set_layer(
        &self,
        Parameters(args): Parameters<SetLayerArgs>,
    ) -> Result<CallToolResult, McpError> {
        let params = serde_json::to_value(&args)
            .map_err(|e| McpError::internal_error(e.to_string(), None))?;
        self.call_bridge("set_layer", params).await
    }

    #[tool(
        name = "set_transform",
        description = "Set position, rotation (Euler degrees), and/or scale on a named GameObject in one call. Only non-null fields are applied; omit a field to leave it unchanged. space=\"world\" (default) uses world-space transforms; space=\"local\" uses local-space. Records undo. Returns {gameObject, position, rotationEuler, scale, success}."
    )]
    async fn set_transform(
        &self,
        Parameters(args): Parameters<SetTransformArgs>,
    ) -> Result<CallToolResult, McpError> {
        let params = serde_json::to_value(&args)
            .map_err(|e| McpError::internal_error(e.to_string(), None))?;
        self.call_bridge("set_transform", params).await
    }

    #[tool(
        name = "get_project_settings",
        description = "Return a read-only snapshot of the current Unity project settings. Intended as an orientation call at the start of a session. Returns {unityVersion, productName, companyName, activeBuildTarget, colorSpace, isPlaying, isCompiling, scriptingBackend, physicsGravity:[x,y,z]}."
    )]
    async fn get_project_settings(
        &self,
        Parameters(args): Parameters<GetProjectSettingsArgs>,
    ) -> Result<CallToolResult, McpError> {
        let params = serde_json::to_value(&args)
            .map_err(|e| McpError::internal_error(e.to_string(), None))?;
        self.call_bridge("get_project_settings", params).await
    }
}

#[tool_handler]
impl ServerHandler for UnityMcpServer {
    fn get_info(&self) -> ServerInfo {
        let mut info = ServerInfo::default();
        info.server_info = Implementation::default();
        info.server_info.name = "patina".into();
        info.server_info.version = env!("CARGO_PKG_VERSION").into();
        info.instructions = Some("Patina: a lean, extensible Unity MCP for agentic tools".into());
        info.capabilities = ServerCapabilities::builder().enable_tools().build();
        info
    }
}
