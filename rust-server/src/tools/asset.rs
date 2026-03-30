use schemars::JsonSchema;
use serde::{Deserialize, Serialize};

#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct FindAssetsByTypeArgs {
    /// Unity asset type filter (e.g. "t:Material", "t:Prefab", "t:Texture2D", "t:AudioClip").
    pub type_filter: String,
    /// Optional folder to limit search (e.g. "Assets/Materials"). Searches all Assets if omitted.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub search_folder: Option<String>,
    /// Maximum number of results to return. Defaults to 50.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub max_results: Option<u32>,
}

#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct FindAssetsByNameArgs {
    /// Name pattern to search for (partial match supported).
    pub name_pattern: String,
    /// Optional folder to limit search.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub search_folder: Option<String>,
    /// Maximum number of results to return. Defaults to 50.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub max_results: Option<u32>,
}

#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct CreateFolderArgs {
    /// Parent folder path (e.g. "Assets/Textures"). Must start with "Assets".
    pub parent_path: String,
    /// Name of the new folder (no slashes).
    pub folder_name: String,
}

#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct MoveAssetArgs {
    /// Project-relative source path (e.g. "Assets/Old/Foo.mat").
    pub source_path: String,
    /// Project-relative destination path including filename (e.g. "Assets/New/Foo.mat").
    pub destination_path: String,
}

#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct RenameAssetArgs {
    /// Project-relative path of the asset to rename (e.g. "Assets/Foo.mat").
    pub asset_path: String,
    /// New base name without extension (e.g. "Bar").
    pub new_name: String,
}

#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct DeleteAssetArgs {
    /// Project-relative path of the asset to delete (e.g. "Assets/Foo.mat").
    pub asset_path: String,
}

#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct GetAssetInfoArgs {
    /// Project-relative path of the asset (e.g. "Assets/Textures/Hero.png").
    pub asset_path: String,
    /// When true, include serialized importer settings in the response. Defaults to false to keep output compact.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub include_importer_settings: Option<bool>,
}

#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct RefreshAssetDatabaseArgs {
    /// Import options: "default" (incremental) or "force_update" (reimport all). Defaults to "default".
    #[serde(skip_serializing_if = "Option::is_none")]
    pub import_options: Option<String>,
}

#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct SetAssetLabelsArgs {
    /// Project-relative path of the asset (e.g. "Assets/Foo.mat").
    pub asset_path: String,
    /// Labels to assign. Replaces the existing label list.
    pub labels: Vec<String>,
}
