use schemars::JsonSchema;
use serde::{Deserialize, Serialize};

#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct CreateMaterialArgs {
    /// Name of the new material asset (without extension).
    pub material_name: String,
    /// Project-relative save path, e.g. "Assets/Materials". Folder must exist.
    pub save_path: String,
    /// Shader to use. Defaults to "Universal Render Pipeline/Lit" when omitted.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub shader_name: Option<String>,
}

#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct SetMaterialPropertyArgs {
    /// Project-relative path to the material asset, e.g. "Assets/Materials/MyMat.mat".
    pub material_path: String,
    /// Shader property name, e.g. "_BaseColor", "_Metallic".
    pub property_name: String,
    /// Value as JSON: float → 1.5, bool → true, RGBA color → [r,g,b,a] floats 0-1, Vector4 → [x,y,z,w], texture path → "Assets/Textures/T.png".
    pub value: serde_json::Value,
}

#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct GetMaterialPropertiesArgs {
    /// Project-relative path to the material asset, e.g. "Assets/Materials/MyMat.mat".
    pub material_path: String,
}

#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct AssignMaterialArgs {
    /// Name of the GameObject that has a Renderer component.
    pub game_object_name: String,
    /// Project-relative path to the material asset, e.g. "Assets/Materials/MyMat.mat".
    pub material_path: String,
    /// Zero-based material slot index on the Renderer. Defaults to 0 when omitted.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub material_index: Option<u32>,
}
