use schemars::JsonSchema;
use serde::{Deserialize, Serialize};

#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct GetAnimatorInfoArgs {
    /// Name of the GameObject that has an Animator component.
    pub game_object: String,
    /// Maximum number of states to return per layer. Default: 50.
    pub max_states: Option<u32>,
}

#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct SetAnimatorParameterArgs {
    /// Name of the GameObject that has an Animator component.
    pub game_object: String,
    /// Exact parameter name as defined in the Animator Controller.
    pub parameter: String,
    /// Value to set. Float → number, Bool → bool, Int → integer, Trigger → true.
    pub value: serde_json::Value,
}

#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct ListAnimationClipsArgs {
    /// Folder to search under, e.g. "Assets/Animations". Searches entire project if omitted.
    pub search_path: Option<String>,
}
