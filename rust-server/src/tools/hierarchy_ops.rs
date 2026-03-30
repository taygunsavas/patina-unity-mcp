use schemars::JsonSchema;
use serde::{Deserialize, Serialize};

#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct ReparentGameObjectArgs {
    /// Name of the GameObject to reparent.
    pub game_object_name: String,
    /// Name of the new parent GameObject. Use null or empty string to move to scene root.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub new_parent_name: Option<String>,
    /// If true, maintain world position. Defaults to true.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub world_position_stays: Option<bool>,
}

#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct DeleteGameObjectArgs {
    /// Name of the GameObject to delete.
    pub game_object_name: String,
}

#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct DuplicateGameObjectArgs {
    /// Name of the GameObject to duplicate.
    pub game_object_name: String,
    /// Optional name for the duplicated object. If omitted, Unity generates a name.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub new_name: Option<String>,
}
