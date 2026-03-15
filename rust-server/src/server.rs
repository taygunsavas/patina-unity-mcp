use std::sync::Arc;

use rmcp::{
    handler::server::{tool::ToolRouter, wrapper::Parameters},
    model::*,
    tool, tool_handler, tool_router, ErrorData as McpError, ServerHandler,
};

use crate::bridge::BridgeClient;
use crate::tools::{CreateGameObjectArgs, GetHierarchyArgs, LogToConsoleArgs};

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
