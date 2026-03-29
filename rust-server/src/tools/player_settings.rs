use schemars::JsonSchema;
use serde::{Deserialize, Serialize};

#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct GetPlayerSettingsArgs {
    /// Build target group to read. Accepts: "Standalone" (default), "Android", "iOS", "WebGL".
    pub build_target_group: Option<String>,
}

#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct SetPlayerSettingsArgs {
    /// Product name shown in launchers and About dialogs.
    pub product_name: Option<String>,
    /// Company name used in PlayerPrefs path and default save paths.
    pub company_name: Option<String>,
    /// Application version string, e.g. "1.0.0".
    pub bundle_version: Option<String>,
    /// Platform-specific bundle/application identifier, e.g. "com.company.game".
    pub application_identifier: Option<String>,
    /// Build target group for platform-specific fields. Accepts: "Standalone" (default), "Android", "iOS", "WebGL".
    pub build_target_group: Option<String>,
}

#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct SetBuildTargetArgs {
    /// Build target string, e.g. "StandaloneWindows64", "Android", "WebGL", "iOS". WARNING: blocks the main thread for 30–120 seconds on large projects.
    pub build_target: String,
}
