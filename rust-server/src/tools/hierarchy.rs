use schemars::JsonSchema;
use serde::{Deserialize, Serialize};

#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct GetHierarchyArgs {
    /// Maximum depth to traverse in the hierarchy.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub max_depth: Option<u32>,
    /// Optional filter to match game object names.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub name_filter: Option<String>,
}
