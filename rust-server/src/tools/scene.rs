use schemars::JsonSchema;
use serde::{Deserialize, Serialize};

#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct GetSceneInfoArgs {
    /// If true, include the list of all loaded scenes. Defaults to false.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub include_all_scenes: Option<bool>,
}

#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct OpenSceneArgs {
    /// Project-relative path to the scene asset, e.g. "Assets/Scenes/Main.unity".
    pub scene_path: String,
    /// Load mode: "single" (default, closes current scene) or "additive" (keeps current scene open).
    #[serde(skip_serializing_if = "Option::is_none")]
    pub mode: Option<String>,
}

#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct SaveSceneArgs {
    /// Project-relative path of the scene to save. Saves the active scene when omitted.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub scene_path: Option<String>,
    /// Save the scene to a new path (Save As). When provided, saves a copy at this location.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub save_as_path: Option<String>,
}

#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct NewSceneArgs {
    /// Name for the new scene (without extension).
    pub name: String,
    /// Asset path to save the scene, e.g. "Assets/Scenes/Level2.unity". Defaults to "Assets/Scenes/<name>.unity".
    #[serde(skip_serializing_if = "Option::is_none")]
    pub save_path: Option<String>,
    /// Initial contents: "default_game_objects" (camera + light) or "empty". Defaults to "default_game_objects".
    #[serde(skip_serializing_if = "Option::is_none")]
    pub setup: Option<String>,
}

#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct GetBuildSettingsArgs {}

#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct SetBuildScenesArgs {
    /// Ordered list of project-relative scene paths to set in Build Settings, e.g. ["Assets/Scenes/Main.unity"].
    pub scene_paths: Vec<String>,
}
