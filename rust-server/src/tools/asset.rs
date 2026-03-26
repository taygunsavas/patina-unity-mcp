use schemars::JsonSchema;
use serde::{Deserialize, Serialize};

#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct FindAssetsByTypeArgs {
    /// Unity asset type filter (e.g. "t:Material", "t:Prefab", "t:Texture2D", "t:AudioClip").
    pub type_filter: String,
    /// Optional folder to limit search (e.g. "Assets/Materials"). Searches all Assets if omitted.
    pub search_folder: Option<String>,
    /// Maximum number of results to return. Defaults to 50.
    pub max_results: Option<u32>,
}

#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct FindAssetsByNameArgs {
    /// Name pattern to search for (partial match supported).
    pub name_pattern: String,
    /// Optional folder to limit search.
    pub search_folder: Option<String>,
    /// Maximum number of results to return. Defaults to 50.
    pub max_results: Option<u32>,
}
