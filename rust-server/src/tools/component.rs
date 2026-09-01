use schemars::JsonSchema;
use serde::{Deserialize, Serialize};

#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct AddComponentArgs {
    /// Name of the target GameObject.
    pub game_object_name: String,
    /// Component type name. Short name (e.g. "Rigidbody") or fully qualified (e.g. "UnityEngine.Rigidbody") both work.
    pub component_type: String,
}

#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct SetPropertyArgs {
    /// Name of the target GameObject.
    pub game_object_name: String,
    /// Component type name (e.g. "Transform", "Rigidbody").
    pub component_type: String,
    /// Property name to set (e.g. "mass", "useGravity", "position").
    pub property_name: String,
    /// Value as JSON matching the property type: float → 1.5, bool → true, int → 42, string → "text",
    /// Vector2 → [0.0,0.0], Vector3 → [0.0,0.0,0.0], Color → [1.0,0.0,0.0,1.0], Quaternion → [0.0,0.0,0.0,1.0].
    /// Object reference fields accept null, an "Assets/..." path, a 32-char GUID, a transform path, or {"transform_path": "Child", "component_type": "Ns.Type"}. A transform path is resolved against the root of the target GameObject's own hierarchy.
    pub value: serde_json::Value,
}

#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct RemoveComponentArgs {
    /// Name of the target GameObject.
    pub game_object_name: String,
    /// Component type name to remove.
    pub component_type: String,
}
