use schemars::JsonSchema;
use serde::{Deserialize, Serialize};

#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct SetActiveStateArgs {
    /// Name of the target GameObject.
    pub game_object_name: String,
    /// Active state to set (true = active, false = inactive).
    pub active: bool,
}

#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct SetTagArgs {
    /// Name of the target GameObject.
    pub game_object_name: String,
    /// Tag string; must be registered in Project Settings > Tags. Returns an error if unregistered.
    pub tag: String,
}

#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct SetLayerArgs {
    /// Name of the target GameObject.
    pub game_object_name: String,
    /// Layer name as defined in Project Settings > Tags and Layers (e.g. "Default", "UI"). Returns an error if not defined.
    pub layer_name: String,
    /// If true, also sets the layer on all child GameObjects. Default: false.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub apply_to_children: Option<bool>,
}

#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct SetTransformArgs {
    /// Name of the target GameObject.
    pub game_object_name: String,
    /// World or local position as [x, y, z]. Omit to leave unchanged.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub position: Option<[f32; 3]>,
    /// Rotation in Euler angles (degrees) as [x, y, z]. Omit to leave unchanged.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub rotation_euler: Option<[f32; 3]>,
    /// Scale as [x, y, z]. Omit to leave unchanged.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub scale: Option<[f32; 3]>,
    /// Coordinate space: "world" (default) uses world-space position and eulerAngles; "local" uses localPosition and localEulerAngles.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub space: Option<String>,
}

#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct GetProjectSettingsArgs {}
