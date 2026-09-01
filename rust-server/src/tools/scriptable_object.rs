use schemars::JsonSchema;
use serde::{Deserialize, Serialize};

#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct GetScriptableObjectArgs {
    /// Asset path relative to project root, e.g. "Assets/Data/EnemyConfig.asset".
    pub asset_path: String,
}

#[derive(Debug, Deserialize, Serialize, JsonSchema)]
pub struct SetScriptableObjectFieldArgs {
    /// Asset path relative to project root, e.g. "Assets/Data/EnemyConfig.asset".
    pub asset_path: String,
    /// Exact field name as declared in the C# class (case-sensitive).
    pub field_name: String,
    /// Value to set. Accepted types: number, bool, string, [r,g,b,a] array for Color,
    /// [x,y,z] or [x,y,z,w] array for Vector3/Vector4. Object reference fields accept null, an "Assets/..." path, a 32-char GUID, or exactly one of {"asset_path": "Assets/..."}, {"guid": "..."}, {"instance_id": 123}.
    pub value: serde_json::Value,
}
