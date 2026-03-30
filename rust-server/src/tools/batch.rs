use schemars::JsonSchema;
use serde::{Deserialize, Serialize};
use serde_json::Value;

#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct BatchPropertyOperation {
    /// Target GameObject name.
    pub game_object_name: String,
    /// Component type name (short or fully qualified, e.g. Rigidbody or UnityEngine.Rigidbody).
    pub component_type: String,
    /// Property or field name on the component.
    pub property_name: String,
    /// Value as JSON matching the property type: float → 1.5, bool → true, Vector3 → [0.0,0.0,0.0].
    pub value: Value,
}

#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct BatchSetPropertiesArgs {
    /// Array of property-set operations. Max 100 items per call.
    pub operations: Vec<BatchPropertyOperation>,
    /// Undo group label displayed in Edit > Undo. Defaults to "Patina Batch SetProperties".
    #[serde(skip_serializing_if = "Option::is_none")]
    pub undo_label: Option<String>,
}

#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct BatchComponentOperation {
    /// Target GameObject name.
    pub game_object_name: String,
    /// Component type to add (short or fully qualified).
    pub component_type: String,
}

#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct BatchAddComponentsArgs {
    /// Array of add-component operations. Max 100 items per call.
    pub operations: Vec<BatchComponentOperation>,
    /// Undo group label. Defaults to "Patina Batch AddComponents".
    #[serde(skip_serializing_if = "Option::is_none")]
    pub undo_label: Option<String>,
}

#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct TransformOverride {
    /// Target GameObject name.
    pub game_object_name: String,
    /// Position as [x, y, z]. Omit to leave unchanged.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub position: Option<[f32; 3]>,
    /// Rotation in Euler degrees as [x, y, z]. Omit to leave unchanged.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub rotation_euler: Option<[f32; 3]>,
    /// Scale as [x, y, z]. Omit to leave unchanged.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub scale: Option<[f32; 3]>,
}

#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct BatchSetTransformArgs {
    /// Array of transform operations. Max 100 items per call.
    pub operations: Vec<TransformOverride>,
    /// Coordinate space for position and rotation: "world" (default) or "local".
    #[serde(skip_serializing_if = "Option::is_none")]
    pub space: Option<String>,
    /// Undo group label. Defaults to "Patina Batch SetTransform".
    #[serde(skip_serializing_if = "Option::is_none")]
    pub undo_label: Option<String>,
}
