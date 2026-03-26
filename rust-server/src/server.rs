use std::sync::Arc;

use rmcp::{
    handler::server::{tool::ToolRouter, wrapper::Parameters},
    model::*,
    tool, tool_handler, tool_router, ErrorData as McpError, ServerHandler,
};

use crate::bridge::BridgeClient;
use crate::tools::{
    AddComponentArgs, CreateGameObjectArgs, CreatePrefabArgs, DeleteGameObjectArgs,
    DuplicateGameObjectArgs, FindAssetsByNameArgs, FindAssetsByTypeArgs, GetHierarchyArgs,
    GetSceneInfoArgs, InstantiatePrefabArgs, LogToConsoleArgs, RemoveComponentArgs,
    ReparentGameObjectArgs, SetPropertyArgs,
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
                            serde_json::to_string_pretty(&v).unwrap_or_else(|e| {
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
        description = "Log a message to the Unity Editor console"
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
        description = "Get the Unity scene hierarchy tree"
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
        description = "Create a new GameObject in the active Unity scene"
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
        description = "Get information about the active Unity scene including name, path, root object count, and dirty state"
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
        description = "Add a component to a GameObject by name"
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
        description = "Set a property value on a component attached to a GameObject"
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
        description = "Remove a component from a GameObject by type name"
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
        description = "Move a GameObject to a new parent in the hierarchy"
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
        description = "Delete a GameObject from the active scene"
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
        description = "Duplicate an existing GameObject in the scene"
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
        description = "Save a scene GameObject as a prefab asset"
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
        description = "Instantiate a prefab asset into the active scene"
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
        description = "Search for assets by type filter (e.g. t:Material, t:Prefab)"
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
        description = "Search for assets by name pattern"
    )]
    async fn find_assets_by_name(
        &self,
        Parameters(args): Parameters<FindAssetsByNameArgs>,
    ) -> Result<CallToolResult, McpError> {
        let params = serde_json::to_value(&args)
            .map_err(|e| McpError::internal_error(e.to_string(), None))?;
        self.call_bridge("find_assets_by_name", params).await
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
