use schemars::JsonSchema;
use serde::{Deserialize, Serialize};

#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct AddComponentArgs {
    /// Name of the target GameObject.
    pub game_object_name: String,
    /// Fully qualified component type name (e.g. "UnityEngine.Rigidbody", "UnityEngine.BoxCollider").
    pub component_type: String,
}

#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct SetPropertyArgs {
    /// Name of the target GameObject.
    pub game_object_name: String,
    /// Component type name (e.g. "Transform", "Rigidbody").
    pub component_type: String,
    /// Property name to set (e.g. "mass", "position").
    pub property_name: String,
    /// Property value as JSON.
    pub value: serde_json::Value,
}

#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct RemoveComponentArgs {
    /// Name of the target GameObject.
    pub game_object_name: String,
    /// Component type name to remove.
    pub component_type: String,
}
