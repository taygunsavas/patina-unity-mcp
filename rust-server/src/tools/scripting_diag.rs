use schemars::JsonSchema;
use serde::{Deserialize, Serialize};

#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct GetCompilationErrorsArgs {}

#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct GetScriptContentArgs {
    /// Project-relative asset path, e.g. Assets/Scripts/MyScript.cs. Must start with "Assets/".
    pub asset_path: String,
}

#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct GetAssemblyTypesArgs {
    /// Assembly simple name, e.g. "Assembly-CSharp" or "Assembly-CSharp-Editor".
    pub assembly_name: String,
    /// Maximum number of types to return. Defaults to 200.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub max_results: Option<u32>,
}

#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct ForceRecompileArgs {}
