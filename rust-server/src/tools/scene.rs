use schemars::JsonSchema;
use serde::{Deserialize, Serialize};

#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct GetSceneInfoArgs {
    /// If true, include the list of all loaded scenes. Defaults to false.
    pub include_all_scenes: Option<bool>,
}
