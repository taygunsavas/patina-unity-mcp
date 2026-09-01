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
    #[serde(skip_serializing_if = "Option::is_none")]
    pub position: Option<[f32; 3]>,
    /// Optional name for the instantiated object.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub name: Option<String>,
}

#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct GetPrefabInfoArgs {
    /// Asset path ("Assets/…") or scene GameObject name to inspect.
    pub target: String,
    /// Hint for target type: "asset" or "instance". Auto-detected if omitted.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub target_type: Option<String>,
}

#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct UnpackPrefabArgs {
    /// Name of the scene GameObject to unpack.
    pub game_object_name: String,
    /// Unpack depth: "outermost" (default) leaves nested prefabs intact; "completely" unpacks all levels.
    #[serde(skip_serializing_if = "Option::is_none")]
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

#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct EditPrefabAssetArgs {
    /// Asset path, e.g. "Assets/Prefabs/MyPrefab.prefab".
    pub asset_path: String,
    /// Actions to run sequentially on the loaded prefab.
    pub actions: Vec<EditPrefabAssetAction>,
}

#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct EditPrefabAssetAction {
    /// Action type: "add_component", "remove_component", "add_child", "remove_child", "set_field".
    pub action_type: String,
    /// Relative transform path, e.g. "Child/Grandchild" (optional, default root).
    #[serde(skip_serializing_if = "Option::is_none")]
    pub transform_path: Option<String>,
    /// Component type (required for add_component, remove_component, set_field).
    #[serde(skip_serializing_if = "Option::is_none")]
    pub component_type: Option<String>,
    /// Field name (required for set_field).
    #[serde(skip_serializing_if = "Option::is_none")]
    pub field_name: Option<String>,
    /// Field value (required for set_field). Scalar types: float → 1.5, bool → true, int → 42, string → "text";
    /// arrays: Vector2 → [0.0,0.0], Vector3 → [0.0,0.0,0.0], Color → [1.0,0.0,0.0,1.0], Quaternion → [0.0,0.0,0.0,1.0].
    /// Object reference fields accept null to clear, "Assets/..." or "Packages/..." asset path, 32-char GUID,
    /// "Child/Grandchild" transform path relative to prefab root (resolves to GameObject or its component),
    /// or {"transform_path": "Child", "component_type": "Namespace.Type"}, {"asset_path": "Assets/..."}, {"guid": "..."}, or {"instance_id": 123}.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub value: Option<serde_json::Value>,
    /// Child GameObject name (required for add_child).
    #[serde(skip_serializing_if = "Option::is_none")]
    pub child_name: Option<String>,
}

/// The returned instanceId identifies the prefab asset object. It is not usable as a reference
/// value in edit_prefab_asset, which edits an in-memory copy: bind references with transform_path.
#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct ListPrefabComponentsArgs {
    /// Asset path, e.g. "Assets/Prefabs/MyPrefab.prefab".
    pub asset_path: String,
    /// Relative transform path, e.g. "Child/Grandchild" (optional, default root).
    #[serde(skip_serializing_if = "Option::is_none")]
    pub transform_path: Option<String>,
    /// Also list child GameObjects with their transform paths and component types. Defaults to false.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub include_children: Option<bool>,
    /// Maximum child depth when include_children is true. 0 or omitted means unlimited.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub max_depth: Option<u32>,
}

#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct OpenPrefabStageArgs {
    /// Asset path, e.g. "Assets/Prefabs/MyPrefab.prefab".
    pub asset_path: String,
}

#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct ClosePrefabStageArgs {
    /// Save changes before closing the stage. Defaults to false.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub save_changes: Option<bool>,
}
