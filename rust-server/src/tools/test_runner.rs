use schemars::JsonSchema;
use serde::{Deserialize, Serialize};

#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct RunTestsArgs {
    /// Test mode: "EditMode" or "PlayMode".
    pub mode: String,
    /// Optional case-sensitive substring matched against each test name or full name. Matching tests are run. A filter that matches no tests fails with an error and starts no run. Omit to run all tests in the mode. The response carries runId and matchedCount, which is null when no filter is given.
    pub filter: Option<String>,
}

#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct GetTestResultsArgs {
    /// Test mode to retrieve results for: "EditMode" or "PlayMode". Defaults to "EditMode". The response carries runId, startedAt and finishedAt (UTC ISO 8601) to tell a fresh run from a previous one. finishedAt is null while the run is in progress, and all three are null before any run has happened. A domain reload during a run (for example PlayMode with domain reload, or a recompile) resets the buffer: runId, startedAt and finishedAt come back null and results are empty, so a caller polling for a specific runId should treat that as a lost run.
    pub mode: Option<String>,
}

#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct GetTestListArgs {
    /// Test mode: "EditMode" or "PlayMode".
    pub mode: String,
}
