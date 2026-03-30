use schemars::JsonSchema;
use serde::{Deserialize, Serialize};

#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct BeginUndoGroupArgs {
    /// Label for the undo group shown in Unity's Edit > Undo menu.
    pub label: String,
}

#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct EndUndoGroupArgs {
    /// Group index returned by begin_undo_group. All operations recorded since that index are collapsed into one undoable step.
    pub group_index: i32,
}

#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct UndoArgs {
    /// Number of undo steps to perform. Defaults to 1.
    #[serde(default = "default_one")]
    pub count: u32,
}

#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct RedoArgs {
    /// Number of redo steps to perform. Defaults to 1.
    #[serde(default = "default_one")]
    pub count: u32,
}

fn default_one() -> u32 {
    1
}

#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct GetUndoStackArgs {}
