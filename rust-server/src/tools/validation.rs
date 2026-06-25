use schemars::JsonSchema;
use serde::{Deserialize, Serialize};

#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct ValidateSceneArgs {
    /// Severity filter: "all" (default), "error", or "warning".
    #[serde(skip_serializing_if = "Option::is_none")]
    pub severity_filter: Option<String>,
}

#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct GetSceneStatsArgs {
    /// When true, includes a componentTypeCounts map in the response. Increases output size. Defaults to false.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub include_per_type_counts: Option<bool>,
}

#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct ValidateAssetsArgs {
    /// Asset or folder path, e.g. "Assets/Prefabs/MyPrefab.prefab" or "Assets/Prefabs".
    pub path: String,
    /// Severity filter: "all" (default), "error", or "warning".
    #[serde(skip_serializing_if = "Option::is_none")]
    pub severity_filter: Option<String>,
    /// Recursive search if target is a folder. Defaults to true.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub recursive: Option<bool>,
}
