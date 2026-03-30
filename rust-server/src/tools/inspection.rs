use schemars::JsonSchema;
use serde::{Deserialize, Serialize};

#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct GetGameObjectInfoArgs {
    /// Name of the target GameObject.
    pub game_object_name: String,
    /// When true, include serialized component properties in the response. Defaults to false — call get_game_object_components first to get component types, then set this to true only for specific deep inspection.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub include_component_properties: Option<bool>,
}

#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct GetGameObjectComponentsArgs {
    /// Name of the target GameObject.
    pub game_object_name: String,
}

#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct FindGameObjectsByTagArgs {
    /// Tag string to search (e.g. "Player", "Enemy").
    pub tag: String,
    /// Maximum results to return. Defaults to 50.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub max_results: Option<u32>,
}

#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct FindGameObjectsByComponentArgs {
    /// Component type name. Short (Rigidbody) or fully qualified (UnityEngine.Rigidbody) both work.
    pub component_type: String,
    /// Maximum results to return. Defaults to 50.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub max_results: Option<u32>,
}

#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct FindGameObjectsByLayerArgs {
    /// Layer name as defined in Project Settings → Tags and Layers (e.g. "Default", "UI").
    pub layer_name: String,
    /// Maximum results to return. Defaults to 50.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub max_results: Option<u32>,
}
