use schemars::JsonSchema;
use serde::{Deserialize, Serialize};

#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct CreatePrefabArgs {
    /// Name of the scene GameObject to save as a prefab.
    pub game_object_name: String,
    /// Asset path where the prefab will be saved (e.g. "Assets/Prefabs/MyPrefab.prefab").
    pub save_path: String,
}

#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct InstantiatePrefabArgs {
    /// Asset path of the prefab to instantiate (e.g. "Assets/Prefabs/MyPrefab.prefab").
    pub prefab_path: String,
    /// Optional world position as [x, y, z].
    pub position: Option<[f32; 3]>,
    /// Optional name for the instantiated object.
    pub name: Option<String>,
}

#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct GetPrefabInfoArgs {
    /// Asset path ("Assets/…") or scene GameObject name to inspect.
    pub target: String,
    /// Hint for target type: "asset" or "instance". Auto-detected if omitted.
    pub target_type: Option<String>,
}

#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct UnpackPrefabArgs {
    /// Name of the scene GameObject to unpack.
    pub game_object_name: String,
    /// Unpack depth: "outermost" (default) leaves nested prefabs intact; "completely" unpacks all levels.
    pub mode: Option<String>,
}

#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct ApplyPrefabOverridesArgs {
    /// Name of the scene GameObject whose overrides to apply back to the prefab asset.
    pub game_object_name: String,
}

#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct RevertPrefabOverridesArgs {
    /// Name of the scene GameObject whose overrides to revert to prefab defaults.
    pub game_object_name: String,
}
