use schemars::JsonSchema;
use serde::{Deserialize, Serialize};

#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct CreateGameObjectArgs {
    /// Name of the new game object.
    pub name: String,
    /// Optional primitive type (e.g. "Cube", "Sphere", "Capsule", "Cylinder", "Plane", "Quad").
    #[serde(skip_serializing_if = "Option::is_none")]
    pub primitive_type: Option<String>,
    /// Optional world position as [x, y, z].
    #[serde(skip_serializing_if = "Option::is_none")]
    pub position: Option<[f32; 3]>,
}
