use schemars::JsonSchema;
use serde::{Deserialize, Serialize};

#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct GetSelectionArgs {}

#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct SetSelectionArgs {
    /// GameObject names to select in the scene. Uses GameObject.Find; returns error if any name cannot be resolved.
    pub game_object_names: Option<Vec<String>>,
    /// Project-relative asset paths to select (e.g. "Assets/Textures/Logo.png"). Returns error if any path cannot be loaded.
    pub asset_paths: Option<Vec<String>>,
}
