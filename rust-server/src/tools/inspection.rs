use schemars::JsonSchema;
use serde::{Deserialize, Serialize};

#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct GetGameObjectInfoArgs {
    /// Name of the target GameObject.
    pub game_object_name: String,
    /// When true (default), include serialized component properties in the response.
    pub include_component_properties: Option<bool>,
}

#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct FindGameObjectsByTagArgs {
    /// Tag string to search (e.g. "Player", "Enemy").
    pub tag: String,
    /// Maximum results to return. Defaults to 50.
    pub max_results: Option<u32>,
}

#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct FindGameObjectsByComponentArgs {
    /// Component type name. Short (Rigidbody) or fully qualified (UnityEngine.Rigidbody) both work.
    pub component_type: String,
    /// Maximum results to return. Defaults to 50.
    pub max_results: Option<u32>,
}

#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct FindGameObjectsByLayerArgs {
    /// Layer name as defined in Project Settings → Tags and Layers (e.g. "Default", "UI").
    pub layer_name: String,
    /// Maximum results to return. Defaults to 50.
    pub max_results: Option<u32>,
}
