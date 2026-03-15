use schemars::JsonSchema;
use serde::{Deserialize, Serialize};

#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct GetHierarchyArgs {
    /// Maximum depth to traverse in the hierarchy.
    pub max_depth: Option<u32>,
    /// Optional filter to match game object names.
    pub name_filter: Option<String>,
}
