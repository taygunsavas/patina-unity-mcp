use std::sync::Arc;

use rmcp::{
    handler::server::wrapper::Parameters, model::*, tool, tool_handler, tool_router,
    ErrorData as McpError, ServerHandler,
};
use schemars::JsonSchema;
use serde::{Deserialize, Serialize};
use serde_json::{json, Value};

use crate::bridge::BridgeClient;
use crate::tools::catalog;

#[derive(Clone)]
pub struct UnityMcpServer {
    bridge: Arc<BridgeClient>,
}

#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct PatinaCapabilitiesArgs {
    /// Optional exact category filter, e.g. "prefab", "asset", "scene", "script", or "validation".
    #[serde(skip_serializing_if = "Option::is_none")]
    pub category: Option<String>,
    /// Optional text search over command names, categories, and descriptions.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub search: Option<String>,
    /// Optional exact command name. Use with include_schema=true before calling patina_call.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub command: Option<String>,
    /// Include JSON parameter schema for returned commands. Defaults to false to keep context small.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub include_schema: Option<bool>,
}

#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct PatinaCallArgs {
    /// Internal Patina command name returned by patina_capabilities.
    pub command: String,
    /// JSON parameters for the command. Pass {} for commands with no parameters.
    #[serde(default = "default_parameters")]
    pub parameters: Value,
}

fn default_parameters() -> Value {
    json!({})
}

#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct PatinaHealthArgs {
    /// When true, also calls Unity get_editor_state through the bridge.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub include_unity_state: Option<bool>,
    /// When true, calls Unity's bridge-level ping without touching Unity main-thread APIs.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub include_bridge_diagnostics: Option<bool>,
}

impl UnityMcpServer {
    pub fn new(bridge: Arc<BridgeClient>) -> Self {
        Self { bridge }
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
                    Ok(CallToolResult::success(vec![ContentBlock::text(text)]))
                } else {
                    let err_msg = response
                        .error
                        .map(|e| format!("[{}] {}", e.code, e.message))
                        .unwrap_or_else(|| "Unknown bridge error".to_string());
                    Ok(CallToolResult::error(vec![ContentBlock::text(err_msg)]))
                }
            }
            Err(e) => Ok(CallToolResult::error(vec![ContentBlock::text(e)])),
        }
    }

    fn json_result(value: Value) -> Result<CallToolResult, McpError> {
        let text = serde_json::to_string(&value)
            .map_err(|e| McpError::internal_error(e.to_string(), None))?;
        Ok(CallToolResult::success(vec![ContentBlock::text(text)]))
    }
}

const SERVER_INSTRUCTIONS: &str = concat!(
    "Patina exposes a compact Unity MCP surface. ",
    "Use patina_capabilities to discover commands, then patina_call to execute them. ",
    "For Patina bugs, missing commands, or workflow gaps, ask the user before filing feedback. ",
    "With approval, follow repository rules, search existing issues, avoid duplicates, and include versions, host, repro steps, expected/actual behavior, and relevant errors. ",
    "Issues: https://github.com/taygunsavas/patina-unity-mcp/issues"
);

#[tool_router]
impl UnityMcpServer {
    #[tool(
        name = "patina_capabilities",
        description = "Browse Patina's Unity command catalog without loading every command schema. Filter by category/search/command; set include_schema=true only for commands you intend to call."
    )]
    async fn patina_capabilities(
        &self,
        Parameters(args): Parameters<PatinaCapabilitiesArgs>,
    ) -> Result<CallToolResult, McpError> {
        if let Some(command) = args.command.as_deref() {
            if catalog::find_command(command).is_none() {
                return Ok(CallToolResult::error(vec![ContentBlock::text(format!(
                    "Unknown Patina command: {}",
                    command
                ))]));
            }
        }

        let result = catalog::query(
            args.category.as_deref(),
            args.search.as_deref(),
            args.command.as_deref(),
            args.include_schema.unwrap_or(false),
        );
        Self::json_result(result)
    }

    #[tool(
        name = "patina_call",
        description = "Execute an internal Patina Unity command by name. Use patina_capabilities first to find commands and request a schema for the specific command."
    )]
    async fn patina_call(
        &self,
        Parameters(args): Parameters<PatinaCallArgs>,
    ) -> Result<CallToolResult, McpError> {
        if catalog::find_command(&args.command).is_none() {
            return Ok(CallToolResult::error(vec![ContentBlock::text(format!(
                "Unknown Patina command: {}",
                args.command
            ))]));
        }

        let parameters = if args.parameters.is_null() {
            json!({})
        } else {
            args.parameters
        };

        self.call_bridge(&args.command, parameters).await
    }

    #[tool(
        name = "patina_health",
        description = "Return Patina server version, compact MCP surface status, bridge port, command count, and optionally Unity editor state or bridge diagnostics."
    )]
    async fn patina_health(
        &self,
        Parameters(args): Parameters<PatinaHealthArgs>,
    ) -> Result<CallToolResult, McpError> {
        let mut result = json!({
            "server": "patina",
            "version": env!("CARGO_PKG_VERSION"),
            "surface": "compact",
            "advertisedToolCount": 3,
            "internalCommandCount": catalog::command_count(),
            "bridgePort": self.bridge.port(),
            "categories": catalog::categories(),
        });

        if args.include_unity_state.unwrap_or(false) {
            match self.bridge.request("get_editor_state", json!({})).await {
                Ok(response) if response.success => {
                    result["unityState"] = response.result.unwrap_or(Value::Null);
                }
                Ok(response) => {
                    result["unityStateError"] = response
                        .error
                        .map(|error| json!({ "code": error.code, "message": error.message }))
                        .unwrap_or_else(|| json!({ "message": "Unknown bridge error" }));
                }
                Err(error) => {
                    result["unityStateError"] = json!({ "message": error });
                }
            }
        }

        if args.include_bridge_diagnostics.unwrap_or(false) {
            match self.bridge.request("__patina_bridge_ping", json!({})).await {
                Ok(response) if response.success => {
                    result["bridgeDiagnostics"] = response.result.unwrap_or(Value::Null);
                }
                Ok(response) => {
                    result["bridgeDiagnosticsError"] = response
                        .error
                        .map(|error| json!({ "code": error.code, "message": error.message }))
                        .unwrap_or_else(|| json!({ "message": "Unknown bridge error" }));
                }
                Err(error) => {
                    result["bridgeDiagnosticsError"] = json!({ "message": error });
                }
            }
        }

        Self::json_result(result)
    }
}

#[tool_handler]
impl ServerHandler for UnityMcpServer {
    fn get_info(&self) -> ServerInfo {
        let mut info = ServerInfo::default();
        info.server_info = Implementation::default();
        info.server_info.name = "patina".into();
        info.server_info.version = env!("CARGO_PKG_VERSION").into();
        info.instructions = Some(SERVER_INSTRUCTIONS.into());
        info.capabilities = ServerCapabilities::builder().enable_tools().build();
        info
    }
}
