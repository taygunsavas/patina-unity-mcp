use schemars::JsonSchema;
use serde::{Deserialize, Serialize};

#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct RunTestsArgs {
    /// Test mode: "EditMode" or "PlayMode".
    pub mode: String,
    /// Optional filter string to match test names (substring match).
    pub filter: Option<String>,
}

#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct GetTestResultsArgs {
    /// Test mode to retrieve results for: "EditMode" or "PlayMode". Defaults to "EditMode".
    pub mode: Option<String>,
}

#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct GetTestListArgs {
    /// Test mode: "EditMode" or "PlayMode".
    pub mode: String,
}
