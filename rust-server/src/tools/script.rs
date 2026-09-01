use schemars::JsonSchema;
use serde::{Deserialize, Serialize};

#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct CreateScriptArgs {
    /// Class name and filename without the .cs extension.
    pub script_name: String,
    /// Destination folder path, e.g. "Assets/Scripts". The folder must already exist.
    pub folder_path: String,
    /// Template to use: "monobehaviour" (default), "scriptableobject", "editor_window", "plain_class", or "interface".
    #[serde(skip_serializing_if = "Option::is_none")]
    pub template: Option<String>,
    /// Optional namespace to wrap the class in, e.g. "MyGame.Gameplay".
    #[serde(skip_serializing_if = "Option::is_none")]
    pub namespace: Option<String>,
    /// If provided, written verbatim to the file and template is ignored.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub content: Option<String>,
}

#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct ResolveScriptTypeArgs {
    /// Fully qualified class name, e.g. "MyNamespace.MyComponent".
    pub type_name: String,
}

#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct RequestScriptReloadArgs {
    /// Wait budget in milliseconds for the domain reload to complete. Defaults to 60000; clamped by Unity to [1000, 180000].
    #[serde(skip_serializing_if = "Option::is_none")]
    pub timeout_ms: Option<u32>,
}
