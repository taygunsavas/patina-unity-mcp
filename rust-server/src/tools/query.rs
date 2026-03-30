use schemars::JsonSchema;
use serde::{Deserialize, Serialize};

#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct QueryGameObjectsArgs {
    /// Filter by tag names. Only objects matching ALL listed tags are returned (AND logic).
    #[serde(skip_serializing_if = "Option::is_none")]
    pub tags: Option<Vec<String>>,
    /// Filter by component type names (short names OK). Object must have ALL listed components.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub components: Option<Vec<String>>,
    /// Filter by Unity layer name (e.g. "Default", "UI").
    #[serde(skip_serializing_if = "Option::is_none")]
    pub layer_name: Option<String>,
    /// Filter by scene hierarchy path prefix (e.g. "/Canvas/HUD"). Objects at or under this path are included.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub path_prefix: Option<String>,
    /// When true (default), include only active GameObjects. Set false to include inactive ones.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub active_only: Option<bool>,
    /// Maximum number of results (default 50, max 200).
    #[serde(skip_serializing_if = "Option::is_none")]
    pub max_results: Option<u32>,
}

#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct FindGameObjectsByPathArgs {
    /// Scene hierarchy path prefix to search under, e.g. "/Canvas/HUD". Leading slash is optional.
    pub path_prefix: String,
    /// Maximum number of results (default 50, max 200).
    #[serde(skip_serializing_if = "Option::is_none")]
    pub max_results: Option<u32>,
}
