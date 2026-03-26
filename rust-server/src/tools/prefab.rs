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
