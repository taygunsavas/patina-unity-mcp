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
    /// JSON parameters for the command. Pass {} (or omit) for commands with no
    /// parameters. A JSON-object-encoded string (e.g. "{\"x\":1}") is also
    /// accepted and parsed; null or an empty/whitespace string is treated as {}.
    /// Any other type (number, bool, array, non-object string) is rejected.
    // Declared as an object in the schema so clients serialize it as one. The Rust
    // type stays `Value` so a client that sends a JSON-encoded string is still
    // normalized by `normalize_parameters` rather than rejected at deserialization.
    #[serde(default = "default_parameters")]
    #[schemars(with = "serde_json::Map<String, Value>")]
    pub parameters: Value,
    /// Canonical absolute path to the Unity project root. Omit to fall back to this MCP process working directory, which applies only when sessionId is also omitted; supplied together with sessionId, both must resolve to the same session.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub workspace: Option<String>,
    /// Optional exact Unity session ID from patina_sessions. Sufficient on its own -- the default workspace is not applied as a constraint. Required when one workspace has multiple open editors.
    #[serde(rename = "sessionId", skip_serializing_if = "Option::is_none")]
    pub session_id: Option<String>,
}

#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct PatinaSessionsArgs {}

fn default_parameters() -> Value {
    json!({})
}

/// Normalizes `patina_call`'s `parameters` field before it is forwarded to the
/// Unity bridge. Different MCP clients serialize an omitted/empty params
/// object differently (null, an empty string, a JSON-encoded string, etc.),
/// so this coerces the accepted shapes into a plain JSON object and rejects
/// anything else with an actionable error instead of forwarding garbage to
/// the bridge, where it would otherwise crash the connection (see #84).
fn normalize_parameters(value: Value) -> Result<Value, String> {
    match value {
        Value::Null => Ok(json!({})),
        Value::Object(_) => Ok(value),
        Value::String(s) => {
            if s.trim().is_empty() {
                return Ok(json!({}));
            }
            match serde_json::from_str::<Value>(&s) {
                Ok(Value::Object(map)) => Ok(Value::Object(map)),
                Ok(other) => Err(format!(
                    "Invalid parameters: string parsed as JSON {} but a JSON object is required.",
                    value_type_name(&other)
                )),
                Err(e) => Err(format!(
                    "Invalid parameters: string is not valid JSON ({e}). Pass a JSON object, e.g. {{}}."
                )),
            }
        }
        other => Err(format!(
            "Invalid parameters: got JSON {} but a JSON object (e.g. {{}}) is required.",
            value_type_name(&other)
        )),
    }
}

fn value_type_name(value: &Value) -> &'static str {
    match value {
        Value::Null => "null",
        Value::Bool(_) => "boolean",
        Value::Number(_) => "number",
        Value::String(_) => "string",
        Value::Array(_) => "array",
        Value::Object(_) => "object",
    }
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

    async fn call_bridge_targeted(
        &self,
        command: &str,
        params: serde_json::Value,
        workspace: Option<&str>,
        session_id: Option<&str>,
    ) -> Result<CallToolResult, McpError> {
        match self
            .bridge
            .request_targeted(command, params, workspace, session_id)
            .await
        {
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

    async fn call_bridge(
        &self,
        command: &str,
        params: serde_json::Value,
    ) -> Result<CallToolResult, McpError> {
        self.call_bridge_targeted(command, params, None, None).await
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
    "Issues: https://github.com/taygunsavas/patina-unity-mcp/issues. ",
    "Write prefabs with edit_prefab_asset (it never opens a stage); if you do enter a prefab stage, leave it with close_prefab_stage, since a dirty stage closed by hand via StageUtility.GoToMainStage() in editor script locks Unity behind a modal dialog. ",
    "A clean compile_and_get_errors result does not by itself mean the project is clean, since the command window may contain no domain reload at all and reload-time OnEnable/OnDisable errors would never surface. To check those, call request_script_reload and then read get_console_logs."
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
        description = "Execute an internal Patina Unity command. Targets the agent working-directory workspace by default; pass workspace to target a different project, or sessionId (from patina_sessions) to target one editor directly."
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

        let parameters = match normalize_parameters(args.parameters) {
            Ok(value) => value,
            Err(message) => return Ok(CallToolResult::error(vec![ContentBlock::text(message)])),
        };

        self.call_bridge_targeted(
            &args.command,
            parameters,
            args.workspace.as_deref(),
            args.session_id.as_deref(),
        )
        .await
    }

    #[tool(
        name = "patina_sessions",
        description = "List active Unity sessions registered with the shared Patina broker, including workspace paths, health, state (connected|reloading|stale), and reloadCount. Check state=\"reloading\" before assuming a failed call means Unity disconnected -- it may just be recompiling."
    )]
    async fn patina_sessions(
        &self,
        Parameters(_args): Parameters<PatinaSessionsArgs>,
    ) -> Result<CallToolResult, McpError> {
        self.call_bridge("__patina_broker_sessions", json!({}))
            .await
    }

    #[tool(
        name = "patina_health",
        description = "Return Patina server version, compact MCP surface status, bridge port, command count, and a broker summary (agentClientCount, unitySessionCount counting both connected and reloading sessions, reloadingSessionCount, and per-session detail). Pass include_unity_state=true to prove the routed Unity session is actually responding, not just registered."
    )]
    async fn patina_health(
        &self,
        Parameters(args): Parameters<PatinaHealthArgs>,
    ) -> Result<CallToolResult, McpError> {
        let mut result = json!({
            "server": "patina",
            "version": env!("CARGO_PKG_VERSION"),
            "surface": "compact",
            "advertisedToolCount": 4,
            "internalCommandCount": catalog::command_count(),
            "bridgePort": self.bridge.port(),
            "agentWorkspace": self.bridge.workspace(),
            "categories": catalog::categories(),
        });
        if let Ok(response) = self
            .bridge
            .request("__patina_broker_health", json!({}))
            .await
        {
            if response.success {
                result["broker"] = response.result.unwrap_or(Value::Null);
            }
        }

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

#[cfg(test)]
mod normalize_parameters_tests {
    use super::normalize_parameters;
    use serde_json::json;

    #[test]
    fn null_becomes_empty_object() {
        assert_eq!(normalize_parameters(json!(null)).unwrap(), json!({}));
    }

    #[test]
    fn object_passes_through_unchanged() {
        let value = json!({"x": 1, "y": "z"});
        assert_eq!(normalize_parameters(value.clone()).unwrap(), value);
    }

    #[test]
    fn valid_json_object_string_is_parsed() {
        let value = json!("{\"x\": 1}");
        assert_eq!(normalize_parameters(value).unwrap(), json!({"x": 1}));
    }

    #[test]
    fn empty_object_string_is_parsed() {
        assert_eq!(normalize_parameters(json!("{}")).unwrap(), json!({}));
    }

    #[test]
    fn empty_string_becomes_empty_object() {
        assert_eq!(normalize_parameters(json!("")).unwrap(), json!({}));
    }

    #[test]
    fn whitespace_only_string_becomes_empty_object() {
        assert_eq!(normalize_parameters(json!("   ")).unwrap(), json!({}));
    }

    #[test]
    fn invalid_json_string_is_rejected() {
        let err = normalize_parameters(json!("not json")).unwrap_err();
        assert!(err.contains("not valid JSON"), "unexpected message: {err}");
    }

    #[test]
    fn number_is_rejected() {
        let err = normalize_parameters(json!(42)).unwrap_err();
        assert!(err.contains("number"), "unexpected message: {err}");
    }

    #[test]
    fn bool_is_rejected() {
        let err = normalize_parameters(json!(true)).unwrap_err();
        assert!(err.contains("boolean"), "unexpected message: {err}");
    }

    #[test]
    fn array_is_rejected() {
        let err = normalize_parameters(json!([1, 2, 3])).unwrap_err();
        assert!(err.contains("array"), "unexpected message: {err}");
    }

    #[test]
    fn json_string_parsing_to_array_is_rejected() {
        let err = normalize_parameters(json!("[1,2,3]")).unwrap_err();
        assert!(err.contains("array"), "unexpected message: {err}");
    }

    #[test]
    fn json_string_parsing_to_scalar_is_rejected() {
        let err = normalize_parameters(json!("42")).unwrap_err();
        assert!(err.contains("number"), "unexpected message: {err}");
    }
}
