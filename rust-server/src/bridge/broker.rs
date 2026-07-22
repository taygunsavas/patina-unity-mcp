use dashmap::DashMap;
use serde_json::{json, Value};
use std::{
    sync::Arc,
    time::{Duration, Instant},
};
use tokio::{
    io::{AsyncReadExt, AsyncWriteExt},
    net::{
        tcp::{OwnedReadHalf, OwnedWriteHalf},
        TcpListener,
    },
    sync::Mutex,
};
use tracing::info;

use super::protocol::{BridgeRequest, BridgeResponse};

const MAX_FRAME_BYTES: usize = 4 * 1024 * 1024;

#[derive(Clone, Debug)]
pub struct BrokerConfig {
    pub startup_grace: Duration,
    pub last_session_grace: Duration,
    pub heartbeat_interval: Duration,
    pub heartbeat_timeout: Duration,
    pub stale_after: Duration,
}

impl Default for BrokerConfig {
    fn default() -> Self {
        Self {
            startup_grace: Duration::from_secs(10),
            last_session_grace: Duration::from_secs(3),
            heartbeat_interval: Duration::from_secs(1),
            heartbeat_timeout: Duration::from_secs(45),
            stale_after: Duration::from_secs(12),
        }
    }
}

#[derive(Clone)]
struct UnitySession {
    id: String,
    connection_id: u64,
    workspace: String,
    project_name: String,
    unity_pid: Option<u32>,
    package_version: Option<String>,
    writer: Arc<Mutex<OwnedWriteHalf>>,
    last_seen: Arc<Mutex<Instant>>,
    health: Arc<Mutex<String>>,
}

#[derive(Default)]
struct BrokerState {
    sessions: DashMap<String, UnitySession>,
    agents: DashMap<u64, Arc<Mutex<OwnedWriteHalf>>>,
    pending: DashMap<String, PendingRequest>,
}

#[derive(Clone)]
struct PendingRequest {
    agent_id: u64,
    session_id: String,
    session_connection_id: u64,
}

pub async fn run(port: u16) -> anyhow::Result<()> {
    let listener = TcpListener::bind(("127.0.0.1", port)).await?;
    serve(listener, BrokerConfig::default()).await
}

pub async fn serve(listener: TcpListener, config: BrokerConfig) -> anyhow::Result<()> {
    let port = listener.local_addr()?.port();
    info!(port, "Patina shared Unity session broker listening");
    let state = Arc::new(BrokerState::default());
    let mut next_agent = 0u64;
    let started_at = Instant::now();
    let mut last_session_gone: Option<Instant> = None;
    loop {
        tokio::select! {
            accepted = listener.accept() => {
                let (stream, _) = accepted?; next_agent += 1;
                let (reader, writer) = stream.into_split();
                tokio::spawn(handle_connection(reader, writer, state.clone(), next_agent, config.clone()));
            }
            _ = tokio::time::sleep(config.heartbeat_interval) => {
                let now = Instant::now();
                let stale: Vec<(String, u64)> = state.sessions.iter().filter_map(|s| {
                    let elapsed = s.last_seen.try_lock().map(|seen| now.duration_since(*seen)).unwrap_or_default();
                    (elapsed >= config.heartbeat_timeout).then(|| (s.id.clone(), s.connection_id))
                }).collect();
                for (id, connection_id) in stale {
                    remove_session(&state, &id, connection_id, "Unity session heartbeat timed out.").await;
                }
                for session in state.sessions.iter() {
                    let heartbeat = json!({"type":"request","request":{"id":format!("heartbeat-{}", uuid::Uuid::new_v4()),"command":"__patina_broker_heartbeat","params":{"agentClientCount":state.agents.len(),"unitySessionCount":state.sessions.len()}}});
                    let _ = write_json(&mut *session.writer.lock().await, &heartbeat).await;
                }
                if state.sessions.is_empty() {
                    let gone = last_session_gone.get_or_insert(now);
                    if started_at.elapsed() >= config.startup_grace && now.duration_since(*gone) >= config.last_session_grace { info!("No active Unity sessions; shared broker exiting"); return Ok(()); }
                } else {
                    // A full grace period begins only after the final session is gone.
                    // Retaining an active tick here shortens that grace by up to one
                    // heartbeat interval.
                    last_session_gone = None;
                }
            }
        }
    }
}

async fn handle_connection(
    mut reader: OwnedReadHalf,
    writer: OwnedWriteHalf,
    state: Arc<BrokerState>,
    connection_id: u64,
    config: BrokerConfig,
) {
    let hello: Value = match read_json(&mut reader).await {
        Ok(Some(v)) => v,
        _ => return,
    };
    let role = hello.get("role").and_then(Value::as_str).unwrap_or("");
    if role == "unity" {
        let session_id = hello
            .get("sessionId")
            .and_then(Value::as_str)
            .unwrap_or("")
            .to_string();
        let workspace = hello
            .get("workspace")
            .and_then(Value::as_str)
            .unwrap_or("")
            .to_string();
        if session_id.is_empty() || workspace.is_empty() {
            return;
        }
        let session = UnitySession {
            id: session_id.clone(),
            connection_id,
            workspace,
            project_name: hello
                .get("projectName")
                .and_then(Value::as_str)
                .unwrap_or("Unity")
                .into(),
            unity_pid: hello
                .get("unityPid")
                .and_then(Value::as_u64)
                .map(|v| v as u32),
            package_version: hello
                .get("packageVersion")
                .and_then(Value::as_str)
                .map(str::to_owned),
            writer: Arc::new(Mutex::new(writer)),
            last_seen: Arc::new(Mutex::new(Instant::now())),
            health: Arc::new(Mutex::new("responsive".into())),
        };
        if let Some(previous) = state.sessions.insert(session_id.clone(), session) {
            let _ = previous.writer.lock().await.shutdown().await;
            fail_pending_for_connection(
                &state,
                &session_id,
                previous.connection_id,
                "Unity session was replaced by a newer connection.",
            )
            .await;
        }
        info!(session_id, "Unity session registered");
        while let Ok(Some(value)) = read_json(&mut reader).await {
            if value.get("type").and_then(Value::as_str) == Some("unregister") {
                break;
            }
            if let Ok(response) = serde_json::from_value::<BridgeResponse>(value) {
                if let Some(session) = state
                    .sessions
                    .get(&session_id)
                    .filter(|session| session.connection_id == connection_id)
                {
                    *session.last_seen.lock().await = Instant::now();
                    if response.id.starts_with("heartbeat-") {
                        let status = response
                            .result
                            .as_ref()
                            .and_then(|v| v.get("status"))
                            .and_then(Value::as_str)
                            .unwrap_or("responsive");
                        *session.health.lock().await = status.to_string();
                    }
                }
                route_response(&state, response, &session_id, connection_id).await;
            }
        }
        remove_session(
            &state,
            &session_id,
            connection_id,
            "Unity session disconnected.",
        )
        .await;
        info!(session_id, "Unity session unregistered");
    } else if role == "agent" {
        let writer = Arc::new(Mutex::new(writer));
        state.agents.insert(connection_id, writer.clone());
        while let Ok(Some(value)) = read_json(&mut reader).await {
            let request = value
                .get("request")
                .cloned()
                .and_then(|v| serde_json::from_value::<BridgeRequest>(v).ok());
            let Some(request) = request else { continue };
            if request.command == "__patina_broker_sessions" {
                let _ = write_json(
                    &mut *writer.lock().await,
                    &ok(request.id, sessions_json(&state, &config).await),
                )
                .await;
                continue;
            }
            if request.command == "__patina_broker_health" {
                let sessions = sessions_json(&state, &config).await;
                let _ = write_json(&mut *writer.lock().await, &ok(request.id, json!({"agentClientCount": state.agents.len(), "unitySessionCount": state.sessions.len(), "sessions": sessions}))).await;
                continue;
            }
            let target = select_session(
                &state,
                value.get("workspace").and_then(Value::as_str),
                value.get("sessionId").and_then(Value::as_str),
            );
            match target {
                Ok(session) => {
                    let envelope = json!({"type":"request", "request": request, "agentConnectionId":connection_id});
                    state.pending.insert(
                        envelope["request"]["id"].as_str().unwrap().to_string(),
                        PendingRequest {
                            agent_id: connection_id,
                            session_id: session.id.clone(),
                            session_connection_id: session.connection_id,
                        },
                    );
                    if write_json(&mut *session.writer.lock().await, &envelope)
                        .await
                        .is_err()
                    {
                        state
                            .pending
                            .remove(envelope["request"]["id"].as_str().unwrap());
                        let _ = write_json(
                            &mut *writer.lock().await,
                            &error(
                                request.id,
                                "SESSION_UNAVAILABLE",
                                "The selected Unity session disconnected.",
                            ),
                        )
                        .await;
                    }
                }
                Err(selection) => {
                    let _ = write_json(
                        &mut *writer.lock().await,
                        &error(request.id, selection.code(), selection.message()),
                    )
                    .await;
                }
            }
        }
        state.agents.remove(&connection_id);
        remove_pending_for_agent(&state, connection_id);
    }
}

async fn route_response(
    state: &BrokerState,
    response: BridgeResponse,
    session_id: &str,
    session_connection_id: u64,
) {
    let matches_connection = state.pending.get(&response.id).is_some_and(|pending| {
        pending.session_id == session_id && pending.session_connection_id == session_connection_id
    });
    if matches_connection {
        let Some((_, pending)) = state.pending.remove(&response.id) else {
            return;
        };
        if let Some(writer) = state.agents.get(&pending.agent_id) {
            let _ = write_json(&mut *writer.lock().await, &response).await;
        }
    }
}
async fn remove_session(state: &BrokerState, session_id: &str, connection_id: u64, reason: &str) {
    if let Some((_, session)) = state.sessions.remove_if(session_id, |_, session| {
        session.connection_id == connection_id
    }) {
        let _ = session.writer.lock().await.shutdown().await;
        fail_pending_for_connection(state, session_id, connection_id, reason).await;
    }
}

async fn fail_pending_for_connection(
    state: &BrokerState,
    session_id: &str,
    connection_id: u64,
    reason: &str,
) {
    let ids: Vec<String> = state
        .pending
        .iter()
        .filter(|p| p.session_id == session_id && p.session_connection_id == connection_id)
        .map(|p| p.key().clone())
        .collect();
    for id in ids {
        if let Some((_, pending)) = state.pending.remove(&id) {
            if let Some(writer) = state.agents.get(&pending.agent_id) {
                let _ = write_json(
                    &mut *writer.lock().await,
                    &error(id, "SESSION_UNAVAILABLE", reason),
                )
                .await;
            }
        }
    }
}

fn remove_pending_for_agent(state: &BrokerState, agent_id: u64) {
    state
        .pending
        .retain(|_, pending| pending.agent_id != agent_id);
}
enum SessionSelectionError {
    NotFound,
    WorkspaceMismatch,
    Ambiguous,
}

impl SessionSelectionError {
    fn code(&self) -> &'static str {
        match self {
            Self::NotFound => "SESSION_NOT_FOUND",
            Self::WorkspaceMismatch => "SESSION_WORKSPACE_MISMATCH",
            Self::Ambiguous => "SESSION_AMBIGUOUS",
        }
    }

    fn message(&self) -> &'static str {
        match self {
            Self::NotFound => "No matching Unity session. Call patina_sessions to see active workspaces, or provide workspace/sessionId.",
            Self::WorkspaceMismatch => "The requested workspace does not match the selected Unity session.",
            Self::Ambiguous => "More than one Unity session matches this workspace. Provide sessionId.",
        }
    }
}

fn select_session(
    state: &BrokerState,
    workspace: Option<&str>,
    session_id: Option<&str>,
) -> Result<UnitySession, SessionSelectionError> {
    if let Some(id) = session_id {
        let session = state
            .sessions
            .get(id)
            .ok_or(SessionSelectionError::NotFound)?
            .clone();
        if let Some(workspace) = workspace {
            if !workspace_matches(workspace, &session.workspace) {
                return Err(SessionSelectionError::WorkspaceMismatch);
            }
        }
        return Ok(session);
    };
    let workspace = workspace.ok_or(SessionSelectionError::NotFound)?;
    let candidates: Vec<UnitySession> = state
        .sessions
        .iter()
        .filter(|s| workspace_matches(workspace, &s.workspace))
        .map(|s| s.clone())
        .collect();
    let longest = candidates
        .iter()
        .map(|s| s.workspace.len())
        .max()
        .ok_or(SessionSelectionError::NotFound)?;
    let longest: Vec<UnitySession> = candidates
        .into_iter()
        .filter(|s| s.workspace.len() == longest)
        .collect();
    if longest.len() == 1 {
        Ok(longest.into_iter().next().expect("exactly one session"))
    } else {
        Err(SessionSelectionError::Ambiguous)
    }
}
fn workspace_matches(candidate: &str, workspace: &str) -> bool {
    let a = normalize_path(candidate);
    let b = normalize_path(workspace);

    if a.is_empty() || b.is_empty() || a == b {
        return !a.is_empty() && a == b;
    }

    if b == "/" || is_windows_drive_root(&b) {
        return a.starts_with(&b);
    }

    a.starts_with(&(b + "/"))
}

fn is_windows_drive_root(path: &str) -> bool {
    let bytes = path.as_bytes();
    bytes.len() == 3 && bytes[1] == b':' && bytes[2] == b'/'
}

/// Normalizes a workspace identifier at the broker boundary before any routing
/// decision. `std::fs::canonicalize` returns Win32 verbatim paths (for example
/// `\\?\E:\Projects\BottleDown`) on Windows, while Unity reports ordinary
/// drive or UNC paths. Both forms must identify the same workspace without
/// relaxing the descendant-boundary check in `workspace_matches`.
fn normalize_path(path: &str) -> String {
    let normalized = path.trim().replace('\\', "/").to_ascii_lowercase();
    let normalized = normalized
        .strip_prefix("//?/unc/")
        .map(|unc_path| format!("//{unc_path}"))
        .or_else(|| normalized.strip_prefix("//?/").map(str::to_owned))
        .unwrap_or(normalized);

    if normalized == "/" || is_windows_drive_root(&normalized) {
        normalized
    } else {
        normalized.trim_end_matches('/').to_owned()
    }
}
async fn sessions_json(state: &BrokerState, config: &BrokerConfig) -> Value {
    let sessions: Vec<UnitySession> = state.sessions.iter().map(|s| s.clone()).collect();
    let mut result = Vec::with_capacity(sessions.len());
    for s in sessions {
        let age = s.last_seen.lock().await.elapsed();
        let status = if age > config.stale_after {
            "stale".into()
        } else {
            s.health.lock().await.clone()
        };
        result.push(json!({"sessionId":s.id,"workspace":s.workspace,"projectName":s.project_name,"unityPid":s.unity_pid,"packageVersion":s.package_version,"status":status,"lastSeenAgeSeconds":age.as_secs_f64()}));
    }
    Value::Array(result)
}
fn ok(id: String, result: Value) -> BridgeResponse {
    BridgeResponse {
        id,
        success: true,
        result: Some(result),
        error: None,
    }
}
fn error(id: String, code: &str, message: &str) -> BridgeResponse {
    BridgeResponse {
        id,
        success: false,
        result: None,
        error: Some(super::protocol::BridgeError {
            code: code.into(),
            message: message.into(),
        }),
    }
}
async fn read_json(reader: &mut OwnedReadHalf) -> Result<Option<Value>, String> {
    let mut h = [0; 4];
    match reader.read_exact(&mut h).await {
        Ok(_) => (),
        Err(e) if e.kind() == std::io::ErrorKind::UnexpectedEof => return Ok(None),
        Err(e) => return Err(e.to_string()),
    };
    let n = u32::from_le_bytes(h) as usize;
    if n == 0 || n > MAX_FRAME_BYTES {
        return Err("invalid frame".into());
    };
    let mut b = vec![0; n];
    reader.read_exact(&mut b).await.map_err(|e| e.to_string())?;
    serde_json::from_slice(&b)
        .map(Some)
        .map_err(|e| e.to_string())
}
pub async fn write_json(
    writer: &mut OwnedWriteHalf,
    value: &impl serde::Serialize,
) -> Result<(), String> {
    let b = serde_json::to_vec(value).map_err(|e| e.to_string())?;
    writer
        .write_all(&(b.len() as u32).to_le_bytes())
        .await
        .map_err(|e| e.to_string())?;
    writer.write_all(&b).await.map_err(|e| e.to_string())?;
    writer.flush().await.map_err(|e| e.to_string())
}

#[cfg(test)]
mod tests {
    use super::*;
    use tokio::{net::TcpStream, task::JoinHandle};

    fn test_config() -> BrokerConfig {
        BrokerConfig {
            startup_grace: Duration::from_millis(500),
            last_session_grace: Duration::from_millis(80),
            heartbeat_interval: Duration::from_millis(20),
            heartbeat_timeout: Duration::from_millis(90),
            stale_after: Duration::from_millis(40),
        }
    }

    async fn start_broker(
        config: BrokerConfig,
    ) -> (std::net::SocketAddr, JoinHandle<anyhow::Result<()>>) {
        let listener = TcpListener::bind("127.0.0.1:0").await.unwrap();
        let address = listener.local_addr().unwrap();
        (address, tokio::spawn(serve(listener, config)))
    }

    async fn connect_agent(
        address: std::net::SocketAddr,
        workspace: &str,
    ) -> (OwnedReadHalf, OwnedWriteHalf) {
        let stream = TcpStream::connect(address).await.unwrap();
        let (mut reader, mut writer) = stream.into_split();
        write_json(&mut writer, &json!({"role":"agent", "workspace":workspace}))
            .await
            .unwrap();
        // Keep the read half mutable at the call site; this prevents a fake agent from
        // accidentally sharing a reader across concurrent request assertions.
        let _ = &mut reader;
        (reader, writer)
    }

    async fn connect_unity(
        address: std::net::SocketAddr,
        id: &str,
        workspace: &str,
        heartbeat_status: Option<&str>,
    ) -> JoinHandle<()> {
        let stream = TcpStream::connect(address).await.unwrap();
        let (mut reader, mut writer) = stream.into_split();
        write_json(
            &mut writer,
            &json!({
                "role":"unity", "sessionId":id, "workspace":workspace,
                "projectName":id, "unityPid":1, "packageVersion":"test"
            }),
        )
        .await
        .unwrap();
        let status = heartbeat_status.map(str::to_owned);
        tokio::spawn(async move {
            while let Ok(Some(value)) = read_json(&mut reader).await {
                let request = value.get("request").cloned().unwrap_or_default();
                let id = request.get("id").and_then(Value::as_str).unwrap_or("");
                let command = request.get("command").and_then(Value::as_str).unwrap_or("");
                if command == "__patina_broker_heartbeat" {
                    if let Some(status) = &status {
                        let response = ok(id.to_string(), json!({"status":status}));
                        if write_json(&mut writer, &response).await.is_err() {
                            break;
                        }
                    }
                } else {
                    let response = ok(
                        id.to_string(),
                        json!({"session": value.get("agentConnectionId"), "command":command}),
                    );
                    if write_json(&mut writer, &response).await.is_err() {
                        break;
                    }
                }
            }
        })
    }

    async fn request(
        writer: &mut OwnedWriteHalf,
        id: &str,
        command: &str,
        workspace: Option<&str>,
        session_id: Option<&str>,
    ) {
        write_json(writer, &json!({"type":"request", "workspace":workspace, "sessionId":session_id, "request":{"id":id,"command":command,"params":{}}})).await.unwrap();
    }

    async fn wait_for_sessions(
        reader: &mut OwnedReadHalf,
        writer: &mut OwnedWriteHalf,
        expected: usize,
    ) -> Value {
        for attempt in 0..30 {
            request(
                writer,
                &format!("sessions-{attempt}"),
                "__patina_broker_sessions",
                None,
                None,
            )
            .await;
            let value = read_json(reader).await.unwrap().unwrap();
            if value["result"].as_array().map_or(0, Vec::len) == expected {
                return value;
            }
            tokio::time::sleep(Duration::from_millis(10)).await;
        }
        panic!("did not observe {expected} sessions")
    }

    async fn wait_for_session_status(
        reader: &mut OwnedReadHalf,
        writer: &mut OwnedWriteHalf,
        session_id: &str,
        expected_status: &str,
    ) -> Value {
        for attempt in 0..30 {
            request(
                writer,
                &format!("session-status-{attempt}"),
                "__patina_broker_sessions",
                None,
                None,
            )
            .await;
            let value = read_json(reader).await.unwrap().unwrap();
            if value["result"]
                .as_array()
                .and_then(|sessions| {
                    sessions.iter().find(|session| {
                        session["sessionId"] == session_id && session["status"] == expected_status
                    })
                })
                .is_some()
            {
                return value;
            }
            tokio::time::sleep(Duration::from_millis(5)).await;
        }
        panic!("Timed out waiting for session {session_id} to become {expected_status}");
    }

    #[test]
    fn workspace_matching_is_windows_case_separator_and_boundary_safe() {
        assert!(workspace_matches(
            "E:\\Projects\\BottleDown\\Assets",
            "e:/projects/bottledown"
        ));
        assert!(workspace_matches(
            "E:/Projects/BottleDown",
            "e:/projects/bottledown"
        ));
        assert!(!workspace_matches("E:/Projects/Barista", "e:/projects/Bar"));
        assert!(!workspace_matches("E:/Projects/Other", "e:/projects/Bar"));
    }

    #[test]
    fn normalize_path_handles_windows_verbatim_drive_and_unc_paths() {
        assert_eq!(
            normalize_path("\\\\?\\C:\\WORK\\BottleDown\\"),
            "c:/work/bottledown"
        );
        assert_eq!(
            normalize_path("\\\\?\\UNC\\Build-Server\\Unity\\BottleDown\\"),
            "//build-server/unity/bottledown"
        );
        assert!(workspace_matches(
            "\\\\?\\E:\\Projeler\\BottleDown\\Assets",
            "e:/projeler/bottledown"
        ));
        assert!(workspace_matches(
            "\\\\?\\UNC\\Build-Server\\Unity\\BottleDown\\Assets",
            "//build-server/unity/bottledown"
        ));
        assert!(!workspace_matches(
            "\\\\?\\E:\\Projeler\\BottleDownBackup",
            "e:/projeler/bottledown"
        ));
    }

    #[test]
    fn workspace_matching_preserves_posix_and_windows_roots() {
        assert_eq!(normalize_path("/"), "/");
        assert_eq!(normalize_path("E:/"), "e:/");
        assert!(workspace_matches("/workspace/bottledown", "/"));
        assert!(workspace_matches("E:/Projects/BottleDown", "e:/"));
        assert!(!workspace_matches("relative/workspace", "/"));
        assert!(!workspace_matches("E:/Projects/BottleDown", "/"));
        assert!(!workspace_matches("", "/"));
    }

    #[tokio::test]
    async fn tcp_registry_tracks_two_unity_sessions_and_two_agents() {
        let (address, broker) = start_broker(test_config()).await;
        let unity_a = connect_unity(address, "a", "E:/Projects/A", Some("responsive")).await;
        let unity_b = connect_unity(address, "b", "E:/Projects/B", Some("responsive")).await;
        let (mut reader_a, mut writer_a) = connect_agent(address, "E:/Projects/A").await;
        let (_reader_b, _writer_b) = connect_agent(address, "E:/Projects/B").await;
        let sessions = wait_for_sessions(&mut reader_a, &mut writer_a, 2).await;
        request(
            &mut writer_a,
            "health",
            "__patina_broker_health",
            None,
            None,
        )
        .await;
        let health = read_json(&mut reader_a).await.unwrap().unwrap();
        assert_eq!(sessions["result"].as_array().unwrap().len(), 2);
        assert_eq!(health["result"]["agentClientCount"], 2);
        assert_eq!(health["result"]["unitySessionCount"], 2);
        unity_a.abort();
        unity_b.abort();
        broker.abort();
    }

    #[tokio::test]
    async fn tcp_routes_default_workspace_and_correlates_response() {
        let (address, broker) = start_broker(test_config()).await;
        let unity = connect_unity(
            address,
            "bottle",
            "E:/Projects/BottleDown",
            Some("responsive"),
        )
        .await;
        let agent_workspace = "//?/E:/Projects/BottleDown/Assets";
        let (mut reader, mut writer) = connect_agent(address, agent_workspace).await;
        wait_for_sessions(&mut reader, &mut writer, 1).await;
        request(
            &mut writer,
            "default-route",
            "get_editor_state",
            Some(agent_workspace),
            None,
        )
        .await;
        let response = read_json(&mut reader).await.unwrap().unwrap();
        assert_eq!(response["id"], "default-route");
        assert_eq!(response["success"], true);
        unity.abort();
        broker.abort();
    }

    #[tokio::test]
    async fn bridge_client_default_workspace_routes_verbatim_windows_path() {
        let (address, broker) = start_broker(test_config()).await;
        let unity = connect_unity(
            address,
            "bottle",
            "E:/Projects/BottleDown",
            Some("responsive"),
        )
        .await;
        let client = crate::bridge::BridgeClient::new_with_workspace(
            address.port(),
            "//?/E:/Projects/BottleDown".to_string(),
        );
        client.start().await;

        let response = tokio::time::timeout(Duration::from_secs(1), async {
            loop {
                let response = client.request("get_editor_state", json!({})).await.unwrap();
                if response.success {
                    return response;
                }
                tokio::time::sleep(Duration::from_millis(10)).await;
            }
        })
        .await
        .expect("default workspace should route to the matching Unity session");

        assert_eq!(response.result.unwrap()["command"], "get_editor_state");
        client.shutdown().await;
        unity.abort();
        broker.abort();
    }

    #[tokio::test]
    async fn tcp_same_session_reconnect_ignores_old_connection_finalizer_and_updates() {
        let (address, broker) = start_broker(test_config()).await;
        let old_stream = TcpStream::connect(address).await.unwrap();
        let (mut old_reader, mut old_writer) = old_stream.into_split();
        write_json(
            &mut old_writer,
            &json!({"role":"unity","sessionId":"same","workspace":"E:/Projects/A"}),
        )
        .await
        .unwrap();

        let new_unity = connect_unity(address, "same", "E:/Projects/A", Some("responsive")).await;
        assert!(
            tokio::time::timeout(Duration::from_millis(200), read_json(&mut old_reader))
                .await
                .unwrap()
                .unwrap()
                .is_none()
        );

        write_json(
            &mut old_writer,
            &ok("heartbeat-late".to_string(), json!({"status":"blocked"})),
        )
        .await
        .unwrap();
        write_json(&mut old_writer, &json!({"type":"unregister"}))
            .await
            .unwrap();

        let (mut reader, mut writer) = connect_agent(address, "E:/Projects/A").await;
        let sessions =
            wait_for_session_status(&mut reader, &mut writer, "same", "responsive").await;
        assert_eq!(sessions["result"][0]["sessionId"], "same");
        assert_eq!(sessions["result"][0]["status"], "responsive");

        request(
            &mut writer,
            "new-writer",
            "get_editor_state",
            Some("E:/Projects/A"),
            Some("same"),
        )
        .await;
        let response = read_json(&mut reader).await.unwrap().unwrap();
        assert_eq!(response["id"], "new-writer");
        assert_eq!(response["result"]["command"], "get_editor_state");

        new_unity.abort();
        broker.abort();
    }

    #[tokio::test]
    async fn tcp_explicit_routing_reports_mismatch_not_found_and_ambiguity() {
        let (address, broker) = start_broker(test_config()).await;
        let unity_a = connect_unity(address, "a", "E:/Projects/A", Some("responsive")).await;
        let unity_b = connect_unity(address, "b", "E:/Projects/B", Some("responsive")).await;
        let duplicate = connect_unity(address, "a2", "E:/Projects/A", Some("responsive")).await;
        let (mut reader, mut writer) = connect_agent(address, "E:/Projects/A").await;
        wait_for_sessions(&mut reader, &mut writer, 3).await;
        request(
            &mut writer,
            "cross",
            "get_hierarchy",
            Some("E:/Projects/B"),
            Some("b"),
        )
        .await;
        assert_eq!(
            read_json(&mut reader).await.unwrap().unwrap()["id"],
            "cross"
        );
        request(
            &mut writer,
            "mismatch",
            "x",
            Some("E:/Projects/A"),
            Some("b"),
        )
        .await;
        assert_eq!(
            read_json(&mut reader).await.unwrap().unwrap()["error"]["code"],
            "SESSION_WORKSPACE_MISMATCH"
        );
        request(
            &mut writer,
            "missing",
            "x",
            Some("E:/Projects/Missing"),
            None,
        )
        .await;
        assert_eq!(
            read_json(&mut reader).await.unwrap().unwrap()["error"]["code"],
            "SESSION_NOT_FOUND"
        );
        request(&mut writer, "ambiguous", "x", Some("E:/Projects/A"), None).await;
        assert_eq!(
            read_json(&mut reader).await.unwrap().unwrap()["error"]["code"],
            "SESSION_AMBIGUOUS"
        );
        unity_a.abort();
        unity_b.abort();
        duplicate.abort();
        broker.abort();
    }

    #[tokio::test]
    async fn tcp_concurrent_requests_do_not_mix_response_ids() {
        let (address, broker) = start_broker(test_config()).await;
        let unity_a = connect_unity(address, "a", "E:/Projects/A", Some("responsive")).await;
        let unity_b = connect_unity(address, "b", "E:/Projects/B", Some("responsive")).await;
        let (mut reader, mut writer) = connect_agent(address, "E:/Projects/A").await;
        wait_for_sessions(&mut reader, &mut writer, 2).await;
        request(
            &mut writer,
            "same-1",
            "one",
            Some("E:/Projects/A"),
            Some("a"),
        )
        .await;
        request(
            &mut writer,
            "same-2",
            "two",
            Some("E:/Projects/A"),
            Some("a"),
        )
        .await;
        request(
            &mut writer,
            "other",
            "three",
            Some("E:/Projects/B"),
            Some("b"),
        )
        .await;
        let mut ids = std::collections::BTreeSet::new();
        for _ in 0..3 {
            ids.insert(
                read_json(&mut reader).await.unwrap().unwrap()["id"]
                    .as_str()
                    .unwrap()
                    .to_owned(),
            );
        }
        assert_eq!(
            ids,
            ["other", "same-1", "same-2"]
                .into_iter()
                .map(str::to_owned)
                .collect()
        );
        unity_a.abort();
        unity_b.abort();
        broker.abort();
    }

    #[tokio::test]
    async fn tcp_disconnect_fails_pending_request_immediately() {
        let (address, broker) = start_broker(test_config()).await;
        let stream = TcpStream::connect(address).await.unwrap();
        let (mut unity_reader, mut unity_writer) = stream.into_split();
        write_json(
            &mut unity_writer,
            &json!({"role":"unity","sessionId":"a","workspace":"E:/Projects/A"}),
        )
        .await
        .unwrap();
        let (mut reader, mut writer) = connect_agent(address, "E:/Projects/A").await;
        wait_for_sessions(&mut reader, &mut writer, 1).await;
        request(
            &mut writer,
            "pending",
            "long",
            Some("E:/Projects/A"),
            Some("a"),
        )
        .await;
        let _ = read_json(&mut unity_reader).await.unwrap();
        drop(unity_writer);
        drop(unity_reader);
        let response = tokio::time::timeout(Duration::from_millis(200), read_json(&mut reader))
            .await
            .unwrap()
            .unwrap()
            .unwrap();
        assert_eq!(response["id"], "pending");
        assert_eq!(response["error"]["code"], "SESSION_UNAVAILABLE");
        broker.abort();
    }

    #[tokio::test]
    async fn tcp_heartbeat_reports_blocked_then_evicts_unresponsive_session() {
        let (address, broker) = start_broker(test_config()).await;
        let blocked = connect_unity(address, "blocked", "E:/Projects/A", Some("blocked")).await;
        let silent = connect_unity(address, "silent", "E:/Projects/B", None).await;
        let (mut reader, mut writer) = connect_agent(address, "E:/Projects/A").await;
        let initial = wait_for_sessions(&mut reader, &mut writer, 2).await;
        assert!(initial["result"]
            .as_array()
            .unwrap()
            .iter()
            .any(|s| s["sessionId"] == "blocked"));
        tokio::time::sleep(Duration::from_millis(55)).await;
        let state = wait_for_sessions(&mut reader, &mut writer, 1).await;
        assert_eq!(state["result"][0]["sessionId"], "blocked");
        assert_eq!(state["result"][0]["status"], "blocked");
        blocked.abort();
        silent.abort();
        broker.abort();
    }

    #[tokio::test]
    async fn tcp_startup_and_last_session_graces_control_broker_lifetime() {
        let mut config = test_config();
        config.startup_grace = Duration::from_millis(120);
        config.last_session_grace = Duration::from_millis(300);
        let (address, mut broker) = start_broker(config).await;
        tokio::time::sleep(Duration::from_millis(60)).await;
        let unity_a = connect_unity(address, "a", "E:/Projects/A", Some("responsive")).await;
        let unity_b = connect_unity(address, "b", "E:/Projects/B", Some("responsive")).await;
        let (mut reader, mut writer) = connect_agent(address, "E:/Projects/A").await;
        wait_for_sessions(&mut reader, &mut writer, 2).await;
        unity_a.abort();
        wait_for_sessions(&mut reader, &mut writer, 1).await;
        assert!(!broker.is_finished());
        unity_b.abort();
        wait_for_sessions(&mut reader, &mut writer, 0).await;
        assert!(tokio::time::timeout(Duration::from_millis(80), &mut broker)
            .await
            .is_err());
        assert!(!broker.is_finished());
        assert!(tokio::time::timeout(Duration::from_millis(600), broker)
            .await
            .unwrap()
            .unwrap()
            .is_ok());
    }

    #[tokio::test]
    async fn tcp_last_session_grace_starts_after_disconnect_not_previous_heartbeat() {
        let mut config = test_config();
        config.startup_grace = Duration::ZERO;
        config.heartbeat_interval = Duration::from_millis(20);
        config.last_session_grace = Duration::from_millis(100);
        let (address, mut broker) = start_broker(config).await;
        let unity = connect_unity(address, "a", "E:/Projects/A", Some("responsive")).await;
        tokio::time::sleep(Duration::from_millis(25)).await;
        unity.abort();
        // The disconnect lands just after an active heartbeat tick. The previous
        // implementation measured grace from that tick and could exit ~20ms early.
        assert!(tokio::time::timeout(Duration::from_millis(90), &mut broker)
            .await
            .is_err());
        assert!(tokio::time::timeout(Duration::from_millis(180), broker)
            .await
            .unwrap()
            .unwrap()
            .is_ok());
    }

    #[test]
    fn agent_disconnect_removes_its_pending_requests() {
        let state = BrokerState::default();
        state.pending.insert(
            "closed-agent".into(),
            PendingRequest {
                agent_id: 7,
                session_id: "unity".into(),
                session_connection_id: 1,
            },
        );
        state.pending.insert(
            "live-agent".into(),
            PendingRequest {
                agent_id: 8,
                session_id: "unity".into(),
                session_connection_id: 1,
            },
        );

        remove_pending_for_agent(&state, 7);

        assert!(!state.pending.contains_key("closed-agent"));
        assert!(state.pending.contains_key("live-agent"));
    }
}
