use schemars::JsonSchema;
use serde::{Deserialize, Serialize};

#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct LogToConsoleArgs {
    /// The message to log to the Unity console.
    pub message: String,
    /// Log level: "info", "warning", or "error". Defaults to "info".
    #[serde(default = "default_level")]
    pub level: String,
}

fn default_level() -> String {
    "info".to_string()
}
