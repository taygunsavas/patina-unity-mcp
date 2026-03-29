use std::sync::Arc;

use rmcp::{
    handler::server::{tool::ToolRouter, wrapper::Parameters},
    model::*,
    tool, tool_handler, tool_router, ErrorData as McpError, ServerHandler,
};

use crate::bridge::BridgeClient;
use crate::tools::{
    AddComponentArgs, CreateGameObjectArgs, CreatePrefabArgs, DeleteGameObjectArgs,
    DuplicateGameObjectArgs, FindAssetsByNameArgs, FindAssetsByTypeArgs, GetBuildSettingsArgs,
    GetHierarchyArgs, GetSceneInfoArgs, InstantiatePrefabArgs, LogToConsoleArgs, NewSceneArgs,
    OpenSceneArgs, RemoveComponentArgs, ReparentGameObjectArgs, SaveSceneArgs, SetBuildScenesArgs,
    SetPropertyArgs,
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
        description = "Replace the Build Settings scene list with the provided ordered scene_paths. All listed scenes are enabled; any previously listed scenes not in the new list are removed. Returns {sceneCount, success}."
    )]
    async fn set_build_scenes(
        &self,
        Parameters(args): Parameters<SetBuildScenesArgs>,
    ) -> Result<CallToolResult, McpError> {
        let params = serde_json::to_value(&args)
            .map_err(|e| McpError::internal_error(e.to_string(), None))?;
        self.call_bridge("set_build_scenes", params).await
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
