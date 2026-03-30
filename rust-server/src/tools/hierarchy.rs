use schemars::JsonSchema;
use serde::{Deserialize, Serialize};

#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct GetHierarchyArgs {
    /// Maximum depth to traverse. Defaults to 3 when omitted; pass a large value (e.g. 100) for the full tree.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub max_depth: Option<u32>,
    /// Optional substring filter to match game object names (case-insensitive).
    #[serde(skip_serializing_if = "Option::is_none")]
    pub name_filter: Option<String>,
    /// When true (default), each node contains only name, activeSelf, instanceId, childCount, and component type list — no properties. Set to false to include full component data.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub compact: Option<bool>,
}
