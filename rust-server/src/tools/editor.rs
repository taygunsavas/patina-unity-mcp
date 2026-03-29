use schemars::JsonSchema;
use serde::{Deserialize, Serialize};

#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct GetEditorStateArgs {}

#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct SetPlayModeArgs {
    /// Play mode action: "enter", "exit", "pause", "unpause", or "step".
    pub mode: String,
}

#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct GetConsoleLogsArgs {
    /// Log type filter: "all", "errors", "warnings", or "logs". Defaults to "all".
    #[serde(default = "default_filter")]
    pub filter: String,
    /// Maximum number of entries to return. Defaults to 50.
    #[serde(default = "default_max_results")]
    pub max_results: u32,
}

fn default_filter() -> String {
    "all".to_string()
}

fn default_max_results() -> u32 {
    50
}

#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct ExecuteMenuItemArgs {
    /// Full Unity menu path, e.g. "Assets/Refresh" or "Edit/Play".
    pub menu_path: String,
}

#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct ClearConsoleArgs {}
