use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, Serialize)]
pub struct BridgeRequest {
    pub id: String,
    pub command: String,
    pub params: serde_json::Value,
}

#[derive(Debug, Clone, Deserialize)]
pub struct BridgeResponse {
    pub id: String,
    pub success: bool,
    pub result: Option<serde_json::Value>,
    pub error: Option<BridgeError>,
}

#[derive(Debug, Clone, Deserialize)]
pub struct BridgeError {
    pub code: String,
    pub message: String,
}
