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
use tracing::{info, warn};

use super::protocol::{BridgeRequest, BridgeResponse};
use crate::tools::catalog::is_reload_replay_safe;

const MAX_FRAME_BYTES: usize = 4 * 1024 * 1024;
/// Upper bound on requests queued for a single reloading session before new
/// arrivals are rejected outright with SESSION_RELOADING. Prevents an agent in
/// a retry storm from growing unbounded memory while Unity is mid-reload.
const MAX_HELD_REQUESTS_PER_SESSION: usize = 64;

#[derive(Clone, Debug)]
pub struct BrokerConfig {
    pub startup_grace: Duration,
    pub last_session_grace: Duration,
    pub heartbeat_interval: Duration,
    pub heartbeat_timeout: Duration,
    pub stale_after: Duration,
    /// How long a Unity session may stay parked after an assembly-reload
    /// unregister before its held requests are failed outright. Must comfortably
    /// exceed a real domain reload; the bridge client's reload retry deadline
    /// (rust-server/src/bridge/client.rs) is tuned to outlast this value.
    pub reload_grace: Duration,
}

impl Default for BrokerConfig {
    fn default() -> Self {
        Self {
            startup_grace: Duration::from_secs(10),
            last_session_grace: Duration::from_secs(3),
            heartbeat_interval: Duration::from_secs(1),
            heartbeat_timeout: Duration::from_secs(45),
            stale_after: Duration::from_secs(12),
            reload_grace: Duration::from_secs(120),
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
    reload_count: u32,
    connected_at: Instant,
}

/// A Unity session that unregistered with reason "assemblyReload" and has not
/// yet reconnected. Kept separate from `BrokerState::sessions` so a parked
/// session is never mistaken for a live one by heartbeat or dispatch code.
struct ReloadingSession {
    workspace: String,
    project_name: String,
    unity_pid: Option<u32>,
    package_version: Option<String>,
    since: Instant,
    reload_count: u32,
    held: Vec<HeldRequest>,
}

/// A request parked while its Unity session reloads. `dispatched` records
/// whether the broker had already written this request to Unity before the
/// reload began: if Unity never reconnects, `dispatched` requests fail with
/// SESSION_RELOAD_INTERRUPTED (may have partially run) while never-dispatched
/// ones fail with SESSION_UNAVAILABLE (definitely never ran).
struct HeldRequest {
    id: String,
    agent_id: u64,
    envelope: Value,
    dispatched: bool,
}

#[derive(Default)]
struct BrokerState {
    sessions: DashMap<String, UnitySession>,
    agents: DashMap<u64, Arc<Mutex<OwnedWriteHalf>>>,
    pending: DashMap<String, PendingRequest>,
    reloading: DashMap<String, ReloadingSession>,
}

#[derive(Clone)]
struct PendingRequest {
    agent_id: u64,
    session_id: String,
    session_connection_id: u64,
    command: String,
    envelope: Value,
}

enum SelectedTarget {
    Live(UnitySession),
    Reloading(String),
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
                send_heartbeats(&state).await;
                expire_reload_grace(&state, &config).await;
                // A session parked for an assembly reload lives in `reloading`, not
                // `sessions` -- it must count as "still around" here. Otherwise a
                // single-editor setup's broker exits mid-reload (last_session_grace
                // is seconds; a real reload can take much longer), stranding every
                // held request with no broker left to answer it when Unity returns.
                if state.sessions.is_empty() && state.reloading.is_empty() {
                    let gone = last_session_gone.get_or_insert(now);
                    if started_at.elapsed() >= config.startup_grace && now.duration_since(*gone) >= config.last_session_grace { info!("No active Unity sessions; shared broker exiting"); return Ok(()); }
                } else {
                    // A full grace period begins only after the final session (live or
                    // reloading) is gone. Retaining an active tick here shortens that
                    // grace by up to one heartbeat interval.
                    last_session_gone = None;
                }
            }
        }
    }
}

/// Fans a heartbeat request out to every live Unity session and evicts any
/// session whose write fails. The recipient list is collected up front and the
/// `sessions` DashMap iterator is fully dropped before any write happens:
/// `remove_session` takes a write lock on the same shard via `remove_if`, and
/// calling it while the iterator's read guard is still held would deadlock.
async fn send_heartbeats(state: &BrokerState) {
    let recipients: Vec<(String, u64, Arc<Mutex<OwnedWriteHalf>>)> = state
        .sessions
        .iter()
        .map(|s| (s.id.clone(), s.connection_id, s.writer.clone()))
        .collect();
    let agent_count = state.agents.len();
    let session_count = state.sessions.len();
    for (id, connection_id, writer) in recipients {
        let heartbeat = json!({"type":"request","request":{"id":format!("heartbeat-{}", uuid::Uuid::new_v4()),"command":"__patina_broker_heartbeat","params":{"agentClientCount":agent_count,"unitySessionCount":session_count}}});
        if write_json(&mut *writer.lock().await, &heartbeat)
            .await
            .is_err()
        {
            remove_session(
                state,
                &id,
                connection_id,
                "Unity session heartbeat write failed.",
            )
            .await;
        }
    }
}

/// Fails every held request belonging to a reloading session that has not
/// reconnected within `config.reload_grace`. Requests the broker had already
/// written to Unity before the reload (`dispatched: true`) fail with
/// SESSION_RELOAD_INTERRUPTED; requests only ever queued during the reload
/// window fail with SESSION_UNAVAILABLE.
async fn expire_reload_grace(state: &BrokerState, config: &BrokerConfig) {
    let expired: Vec<String> = state
        .reloading
        .iter()
        .filter(|entry| entry.since.elapsed() >= config.reload_grace)
        .map(|entry| entry.key().clone())
        .collect();
    for id in expired {
        let Some((_, reloading)) = state.reloading.remove(&id) else {
            continue;
        };
        let held_count = reloading.held.len();
        for held in reloading.held {
            let (code, message) = if held.dispatched {
                (
                    "SESSION_RELOAD_INTERRUPTED",
                    "Unity started an assembly reload while this command was running. The command may have partially applied; verify Unity state before retrying.".to_string(),
                )
            } else {
                (
                    "SESSION_UNAVAILABLE",
                    format!(
                        "Unity did not reconnect within {}s after an assembly reload.",
                        config.reload_grace.as_secs()
                    ),
                )
            };
            if let Some(writer) = state.agents.get(&held.agent_id) {
                let _ =
                    write_json(&mut *writer.lock().await, &error(held.id, code, &message)).await;
            }
        }
        warn!(
            session_id = %id,
            held = held_count,
            "Reload grace period expired; held requests failed"
        );
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
        let unity_pid = hello
            .get("unityPid")
            .and_then(Value::as_u64)
            .map(|v| v as u32);
        let reload_count = hello
            .get("reloadCount")
            .and_then(Value::as_u64)
            .unwrap_or(0) as u32;
        let session = UnitySession {
            id: session_id.clone(),
            connection_id,
            workspace: workspace.clone(),
            project_name: hello
                .get("projectName")
                .and_then(Value::as_str)
                .unwrap_or("Unity")
                .into(),
            unity_pid,
            package_version: hello
                .get("packageVersion")
                .and_then(Value::as_str)
                .map(str::to_owned),
            writer: Arc::new(Mutex::new(writer)),
            last_seen: Arc::new(Mutex::new(Instant::now())),
            health: Arc::new(Mutex::new("responsive".into())),
            reload_count,
            connected_at: Instant::now(),
        };
        if let Some(previous) = state.sessions.insert(session_id.clone(), session) {
            let _ = previous.writer.lock().await.shutdown().await;
            fail_pending_for_connection(
                &state,
                &session_id,
                previous.connection_id,
                &previous.workspace,
                "Unity session was replaced by a newer connection.",
            )
            .await;
        }
        resume_after_reload(&state, &session_id, unity_pid, &workspace, connection_id).await;
        info!(session_id, "Unity session registered");
        let mut unregister_reason: Option<String> = None;
        while let Ok(Some(value)) = read_json(&mut reader).await {
            if value.get("type").and_then(Value::as_str) == Some("unregister") {
                unregister_reason = value
                    .get("reason")
                    .and_then(Value::as_str)
                    .map(str::to_owned);
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
        // Invariant relied on by the client's retry logic (see client.rs): only an
        // explicit reason=="assemblyReload" parks the session for replay. Every
        // other disconnect -- including an old UPM 1.1.13 client's unregister with
        // no "reason" field at all -- takes the ordinary removal path so pending
        // requests fail immediately instead of waiting out the reload grace period.
        if unregister_reason.as_deref() == Some("assemblyReload") {
            park_session_for_reload(&state, &session_id, connection_id).await;
        } else {
            remove_session(
                &state,
                &session_id,
                connection_id,
                "Unity session disconnected.",
            )
            .await;
        }
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
                let _ = write_json(&mut *writer.lock().await, &ok(request.id, json!({"agentClientCount": state.agents.len(), "unitySessionCount": state.sessions.len() + state.reloading.len(), "reloadingSessionCount": state.reloading.len(), "sessions": sessions}))).await;
                continue;
            }
            let target = select_session(
                &state,
                value.get("workspace").and_then(Value::as_str),
                value.get("sessionId").and_then(Value::as_str),
            );
            match target {
                Ok(SelectedTarget::Live(session)) => {
                    let envelope = json!({"type":"request", "request": &request, "agentConnectionId":connection_id});
                    let request_id = request.id.clone();
                    state.pending.insert(
                        request_id.clone(),
                        PendingRequest {
                            agent_id: connection_id,
                            session_id: session.id.clone(),
                            session_connection_id: session.connection_id,
                            command: request.command.clone(),
                            envelope: envelope.clone(),
                        },
                    );
                    let write_failed = write_json(&mut *session.writer.lock().await, &envelope)
                        .await
                        .is_err();
                    if write_failed {
                        warn!(
                            session_id = %session.id,
                            workspace = %session.workspace,
                            connection_id = session.connection_id,
                            request_id = %request_id,
                            "Failed to dispatch request to Unity session"
                        );
                        finalize_dispatch(
                            &state,
                            &request_id,
                            &session,
                            "Unity session write failed.",
                        )
                        .await;
                    } else if !session_still_registered(&state, &session) {
                        // The write itself succeeded, but a concurrent sweep (heartbeat
                        // eviction, reload park) claimed this exact session between our
                        // insert into `pending` and this check. Nothing else will ever
                        // clean up this pending entry: `park_session_for_reload` already
                        // took its snapshot before we inserted, `remove_if` in a later
                        // sweep won't match a connection_id it doesn't recognize anymore,
                        // and `expire_reload_grace` only looks at `held`, not `pending`.
                        // Left alone this request would sit until the client's own
                        // request timeout (up to 180s for long-running commands).
                        warn!(
                            session_id = %session.id,
                            workspace = %session.workspace,
                            connection_id = session.connection_id,
                            request_id = %request_id,
                            "Unity session was parked or replaced immediately after a successful dispatch write"
                        );
                        finalize_dispatch(
                            &state,
                            &request_id,
                            &session,
                            "Unity session disconnected immediately after this request was sent.",
                        )
                        .await;
                    }
                }
                Ok(SelectedTarget::Reloading(id)) => {
                    if is_reload_replay_safe(&request.command) {
                        let envelope = json!({"type":"request", "request": &request, "agentConnectionId":connection_id});
                        let queued = state
                            .reloading
                            .get_mut(&id)
                            .map(|mut reloading| {
                                if reloading.held.len() >= MAX_HELD_REQUESTS_PER_SESSION {
                                    false
                                } else {
                                    reloading.held.push(HeldRequest {
                                        id: request.id.clone(),
                                        agent_id: connection_id,
                                        envelope,
                                        dispatched: false,
                                    });
                                    true
                                }
                            })
                            .unwrap_or(false);
                        if !queued {
                            let _ = write_json(
                                &mut *writer.lock().await,
                                &error(
                                    request.id,
                                    "SESSION_RELOADING",
                                    "Unity is reloading assemblies after a script compile and too many commands are already queued. Retry once patina_sessions reports state=connected.",
                                ),
                            )
                            .await;
                        }
                    } else {
                        let _ = write_json(
                            &mut *writer.lock().await,
                            &error(
                                request.id,
                                "SESSION_RELOADING",
                                "Unity is reloading assemblies after a script compile. This command is not safe to replay automatically; retry once patina_sessions reports state=connected.",
                            ),
                        )
                        .await;
                    }
                }
                Err(selection) => {
                    let requested_workspace = value.get("workspace").and_then(Value::as_str);
                    let requested_session_id = value.get("sessionId").and_then(Value::as_str);
                    warn!(
                        workspace = requested_workspace,
                        session_id = requested_session_id,
                        reason = selection.code(),
                        live_sessions = state.sessions.len(),
                        reloading_sessions = state.reloading.len(),
                        "Session selection failed for agent request"
                    );
                    let message = selection.message();
                    let _ = write_json(
                        &mut *writer.lock().await,
                        &error(request.id, selection.code(), &message),
                    )
                    .await;
                }
            }
        }
        state.agents.remove(&connection_id);
        remove_pending_for_agent(&state, connection_id);
    }
}

/// Removes a Unity session from `sessions` and moves any of its in-flight
/// requests into `reloading` for later replay. Requests that are not
/// replay-safe (writes, menu items, etc.) are failed immediately with
/// SESSION_RELOAD_INTERRUPTED rather than held -- they were already sent to
/// Unity, so retrying them automatically could run a write command twice.
async fn park_session_for_reload(state: &BrokerState, session_id: &str, connection_id: u64) {
    let Some((_, session)) = state.sessions.remove_if(session_id, |_, session| {
        session.connection_id == connection_id
    }) else {
        return;
    };
    let _ = session.writer.lock().await.shutdown().await;

    let pending_ids: Vec<String> = state
        .pending
        .iter()
        .filter(|p| p.session_id == session_id && p.session_connection_id == connection_id)
        .map(|p| p.key().clone())
        .collect();

    let mut held = Vec::new();
    for id in pending_ids {
        let Some((_, pending)) = state.pending.remove(&id) else {
            continue;
        };
        if is_reload_replay_safe(&pending.command) {
            held.push(HeldRequest {
                id,
                agent_id: pending.agent_id,
                envelope: pending.envelope,
                dispatched: true,
            });
        } else if let Some(writer) = state.agents.get(&pending.agent_id) {
            let _ = write_json(
                &mut *writer.lock().await,
                &error(
                    id,
                    "SESSION_RELOAD_INTERRUPTED",
                    "Unity started an assembly reload while this command was running. The command may have partially applied; verify Unity state before retrying.",
                ),
            )
            .await;
        }
    }

    let held_count = held.len();
    state.reloading.insert(
        session_id.to_string(),
        ReloadingSession {
            workspace: session.workspace.clone(),
            project_name: session.project_name.clone(),
            unity_pid: session.unity_pid,
            package_version: session.package_version.clone(),
            since: Instant::now(),
            reload_count: session.reload_count,
            held,
        },
    );
    warn!(
        session_id,
        workspace = %session.workspace,
        connection_id,
        held = held_count,
        "Unity session parked for assembly reload"
    );
}

/// Called every time a Unity "hello" registers, whether or not this session
/// was previously parked. If `session_id` has a matching `ReloadingSession`,
/// this replays every held request onto the new connection (or fails them if
/// the reconnecting process does not match, e.g. a full Unity restart rather
/// than a domain reload).
async fn resume_after_reload(
    state: &BrokerState,
    session_id: &str,
    unity_pid: Option<u32>,
    workspace: &str,
    connection_id: u64,
) {
    let Some((_, reloading)) = state.reloading.remove(session_id) else {
        return;
    };
    let matches = reloading.unity_pid.is_some()
        && reloading.unity_pid == unity_pid
        && workspace_matches(workspace, &reloading.workspace);
    let held_count = reloading.held.len();
    if !matches {
        for held in reloading.held {
            if let Some(writer) = state.agents.get(&held.agent_id) {
                let _ = write_json(
                    &mut *writer.lock().await,
                    &error(
                        held.id,
                        "SESSION_UNAVAILABLE",
                        "Unity restarted while an assembly reload was pending.",
                    ),
                )
                .await;
            }
        }
        warn!(
            session_id,
            held = held_count,
            "Reloading session replaced by a mismatched Unity reconnect; held requests failed"
        );
        return;
    }

    let mut replayed = 0usize;
    for held in reloading.held {
        state.pending.insert(
            held.id.clone(),
            PendingRequest {
                agent_id: held.agent_id,
                session_id: session_id.to_string(),
                session_connection_id: connection_id,
                command: held
                    .envelope
                    .get("request")
                    .and_then(|r| r.get("command"))
                    .and_then(Value::as_str)
                    .unwrap_or("")
                    .to_string(),
                envelope: held.envelope.clone(),
            },
        );
        let sent = match state.sessions.get(session_id) {
            Some(session) => write_json(&mut *session.writer.lock().await, &held.envelope)
                .await
                .is_ok(),
            None => false,
        };
        if sent {
            replayed += 1;
        } else {
            state.pending.remove(&held.id);
            if let Some(writer) = state.agents.get(&held.agent_id) {
                let _ = write_json(
                    &mut *writer.lock().await,
                    &error(
                        held.id,
                        "SESSION_UNAVAILABLE",
                        "Unity session disconnected while replaying a held request.",
                    ),
                )
                .await;
            }
        }
    }
    info!(
        session_id,
        replayed,
        reload_count = reloading.reload_count,
        "Unity session resumed after assembly reload"
    );
}

/// Whether `session` is still the live, registered session for its id --
/// i.e. no concurrent sweep (heartbeat eviction, reload park, replacement by a
/// newer connection) has claimed it since it was selected for dispatch.
fn session_still_registered(state: &BrokerState, session: &UnitySession) -> bool {
    state
        .sessions
        .get(&session.id)
        .is_some_and(|current| current.connection_id == session.connection_id)
}

/// Claims ownership of a dead dispatch (a write that failed, or one whose
/// session vanished immediately after a successful write) and reports whether
/// *this* call was the one that claimed it. `state.pending.remove` is the
/// ownership token: a concurrent sweep (heartbeat eviction, reload park) may
/// have already claimed and responded to `request_id` before this call runs,
/// in which case this is a no-op and returns `false`. Only the winner evicts
/// the session -- a broken pipe or an already-parked session means every
/// other pending request for it is equally dead, not just this one -- and
/// writes the single response frame for `request_id`. This is what prevents a
/// duplicate response for the same request id.
async fn finalize_dispatch(
    state: &BrokerState,
    request_id: &str,
    session: &UnitySession,
    reason: &str,
) -> bool {
    let Some((_, pending)) = state.pending.remove(request_id) else {
        return false;
    };
    remove_session(state, &session.id, session.connection_id, reason).await;
    if let Some(writer) = state.agents.get(&pending.agent_id) {
        let _ = write_json(
            &mut *writer.lock().await,
            &error(request_id.to_string(), "SESSION_UNAVAILABLE", reason),
        )
        .await;
    }
    true
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
        fail_pending_for_connection(state, session_id, connection_id, &session.workspace, reason)
            .await;
    }
}

async fn fail_pending_for_connection(
    state: &BrokerState,
    session_id: &str,
    connection_id: u64,
    workspace: &str,
    reason: &str,
) {
    let ids: Vec<String> = state
        .pending
        .iter()
        .filter(|p| p.session_id == session_id && p.session_connection_id == connection_id)
        .map(|p| p.key().clone())
        .collect();
    if !ids.is_empty() {
        warn!(
            session_id,
            workspace,
            connection_id,
            reason,
            pending_count = ids.len(),
            live_sessions = state.sessions.len(),
            reloading_sessions = state.reloading.len(),
            "Failing pending requests for a disconnected Unity session"
        );
    }
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

/// Drops everything a disconnected agent is still owed: both its ordinary
/// `pending` entries and any `HeldRequest`s it queued against a reloading
/// session. Without the second half, a dead agent's requests stay in a
/// `ReloadingSession::held` list forever -- eating into the per-session
/// MAX_HELD_REQUESTS_PER_SESSION budget other agents share, and on resume
/// Unity would actually run them only for the response to be written to an
/// agent connection that no longer exists.
fn remove_pending_for_agent(state: &BrokerState, agent_id: u64) {
    state
        .pending
        .retain(|_, pending| pending.agent_id != agent_id);
    for mut reloading in state.reloading.iter_mut() {
        reloading.held.retain(|held| held.agent_id != agent_id);
    }
}
#[derive(Debug)]
enum SessionSelectionError {
    NotFound { reloading_count: usize },
    WorkspaceMismatch,
    Ambiguous { candidates: Vec<(String, String)> },
}

impl SessionSelectionError {
    fn code(&self) -> &'static str {
        match self {
            Self::NotFound { .. } => "SESSION_NOT_FOUND",
            Self::WorkspaceMismatch => "SESSION_WORKSPACE_MISMATCH",
            Self::Ambiguous { .. } => "SESSION_AMBIGUOUS",
        }
    }

    fn message(&self) -> String {
        match self {
            Self::NotFound { reloading_count } if *reloading_count > 0 => format!(
                "No matching Unity session, but {reloading_count} session(s) are mid assembly-reload. Call patina_sessions to see active/reloading workspaces, or provide workspace/sessionId."
            ),
            Self::NotFound { .. } => "No matching Unity session. Call patina_sessions to see active workspaces, or provide workspace/sessionId.".to_string(),
            Self::WorkspaceMismatch => "The requested workspace does not match the selected Unity session.".to_string(),
            Self::Ambiguous { candidates } => format!(
                "More than one Unity session matches this workspace. Provide sessionId. Candidates: {}",
                candidates
                    .iter()
                    .map(|(id, workspace)| format!("{id} ({workspace})"))
                    .collect::<Vec<_>>()
                    .join(", ")
            ),
        }
    }
}

fn select_session(
    state: &BrokerState,
    workspace: Option<&str>,
    session_id: Option<&str>,
) -> Result<SelectedTarget, SessionSelectionError> {
    if let Some(id) = session_id {
        if let Some(session) = state.sessions.get(id) {
            let session = session.clone();
            if let Some(workspace) = workspace {
                if !workspace_matches(workspace, &session.workspace) {
                    return Err(SessionSelectionError::WorkspaceMismatch);
                }
            }
            return Ok(SelectedTarget::Live(session));
        }
        if let Some(reloading) = state.reloading.get(id) {
            if let Some(workspace) = workspace {
                if !workspace_matches(workspace, &reloading.workspace) {
                    return Err(SessionSelectionError::WorkspaceMismatch);
                }
            }
            return Ok(SelectedTarget::Reloading(id.to_string()));
        }
        return Err(SessionSelectionError::NotFound {
            reloading_count: state.reloading.len(),
        });
    };
    let workspace = workspace.ok_or(SessionSelectionError::NotFound {
        reloading_count: state.reloading.len(),
    })?;
    let candidates: Vec<UnitySession> = state
        .sessions
        .iter()
        .filter(|s| workspace_matches(workspace, &s.workspace))
        .map(|s| s.clone())
        .collect();
    if !candidates.is_empty() {
        let longest = candidates
            .iter()
            .map(|s| s.workspace.len())
            .max()
            .expect("candidates is non-empty");
        let longest: Vec<UnitySession> = candidates
            .into_iter()
            .filter(|s| s.workspace.len() == longest)
            .collect();
        return if longest.len() == 1 {
            Ok(SelectedTarget::Live(
                longest.into_iter().next().expect("exactly one session"),
            ))
        } else {
            Err(SessionSelectionError::Ambiguous {
                candidates: longest.into_iter().map(|s| (s.id, s.workspace)).collect(),
            })
        };
    }
    // No live session matches this workspace; fall back to a reloading one so
    // the caller sees SESSION_RELOADING instead of a misleading SESSION_NOT_FOUND.
    let reloading_candidates: Vec<(String, String)> = state
        .reloading
        .iter()
        .filter(|entry| workspace_matches(workspace, &entry.workspace))
        .map(|entry| (entry.key().clone(), entry.workspace.clone()))
        .collect();
    if reloading_candidates.is_empty() {
        return Err(SessionSelectionError::NotFound {
            reloading_count: state.reloading.len(),
        });
    }
    let longest = reloading_candidates
        .iter()
        .map(|(_, workspace)| workspace.len())
        .max()
        .expect("reloading_candidates is non-empty");
    let longest: Vec<(String, String)> = reloading_candidates
        .into_iter()
        .filter(|(_, workspace)| workspace.len() == longest)
        .collect();
    if longest.len() == 1 {
        Ok(SelectedTarget::Reloading(
            longest
                .into_iter()
                .next()
                .expect("exactly one reloading session")
                .0,
        ))
    } else {
        Err(SessionSelectionError::Ambiguous {
            candidates: longest,
        })
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
    let mut result = Vec::with_capacity(sessions.len() + state.reloading.len());
    for s in sessions {
        let age = s.last_seen.lock().await.elapsed();
        let is_stale = age > config.stale_after;
        // "status" and "lastSeenAgeSeconds" keep their pre-existing semantics
        // exactly (protocol addition, not a change) so older MCP clients reading
        // this response are unaffected. "state"/"reloadCount"/"connectedForSeconds"
        // are additive fields.
        let status: String = if is_stale {
            "stale".into()
        } else {
            s.health.lock().await.clone()
        };
        result.push(json!({
            "sessionId": s.id,
            "workspace": s.workspace,
            "projectName": s.project_name,
            "unityPid": s.unity_pid,
            "packageVersion": s.package_version,
            "status": status,
            "lastSeenAgeSeconds": age.as_secs_f64(),
            "state": if is_stale { "stale" } else { "connected" },
            "reloadCount": s.reload_count,
            "connectedForSeconds": s.connected_at.elapsed().as_secs_f64(),
        }));
    }
    for entry in state.reloading.iter() {
        let age = entry.since.elapsed();
        result.push(json!({
            "sessionId": entry.key(),
            "workspace": entry.workspace,
            "projectName": entry.project_name,
            "unityPid": entry.unity_pid,
            "packageVersion": entry.package_version,
            "status": "reloading",
            "lastSeenAgeSeconds": age.as_secs_f64(),
            "state": "reloading",
            "reloadCount": entry.reload_count,
            "heldRequestCount": entry.held.len(),
            "reloadingForSeconds": age.as_secs_f64(),
        }));
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
            reload_grace: Duration::from_millis(300),
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

    /// Opens a raw Unity-role connection and returns the split halves without
    /// spawning a response loop. Used by reload/reconnect tests that need to
    /// hand-drive the hello, request reads, unregister, and reconnect sequence
    /// instead of the fire-and-forget auto-responder `connect_unity` runs.
    async fn connect_unity_raw(
        address: std::net::SocketAddr,
        id: &str,
        workspace: &str,
        pid: u32,
        reload_count: u32,
    ) -> (OwnedReadHalf, OwnedWriteHalf) {
        let stream = TcpStream::connect(address).await.unwrap();
        let (reader, mut writer) = stream.into_split();
        write_json(
            &mut writer,
            &json!({
                "role":"unity", "sessionId":id, "workspace":workspace,
                "projectName":id, "unityPid":pid, "packageVersion":"test",
                "reloadCount": reload_count
            }),
        )
        .await
        .unwrap();
        (reader, writer)
    }

    /// Reads frames from `reader` until one carries `request.id == id`,
    /// discarding anything else (heartbeats in particular) along the way.
    async fn read_matching_request(reader: &mut OwnedReadHalf, id: &str) -> Value {
        loop {
            let value = read_json(reader).await.unwrap().unwrap();
            let request_id = value
                .get("request")
                .and_then(|r| r.get("id"))
                .and_then(Value::as_str)
                .unwrap_or("");
            if request_id == id {
                return value;
            }
        }
    }

    /// Polls `__patina_broker_sessions` until `session_id` is present and no
    /// longer reports `state == "reloading"`. More robust than waiting for a
    /// specific status string, since a resumed session's "responsive" vs
    /// "stale" status depends on whether the test also drives heartbeats.
    async fn wait_for_resumed(
        reader: &mut OwnedReadHalf,
        writer: &mut OwnedWriteHalf,
        session_id: &str,
    ) -> Value {
        // Polls up to a ~3s deadline (200 attempts * 15ms). The condition is
        // checked on every attempt, so a fast/idle runner still returns almost
        // immediately -- the generous ceiling only matters when the test
        // process is starved of CPU by other concurrently-running tests on a
        // loaded CI host, where a tight deadline previously caused spurious
        // timeouts unrelated to the broker logic being tested.
        for attempt in 0..200 {
            request(
                writer,
                &format!("resume-check-{attempt}"),
                "__patina_broker_sessions",
                None,
                None,
            )
            .await;
            let value = read_json(reader).await.unwrap().unwrap();
            if let Some(sessions) = value["result"].as_array() {
                if let Some(session) = sessions.iter().find(|s| s["sessionId"] == session_id) {
                    if session["state"] != "reloading" {
                        return value;
                    }
                }
            }
            tokio::time::sleep(Duration::from_millis(15)).await;
        }
        panic!("Timed out waiting for session {session_id} to resume from reload");
    }

    async fn connect_unity_pid(
        address: std::net::SocketAddr,
        id: &str,
        workspace: &str,
        heartbeat_status: Option<&str>,
        pid: u32,
    ) -> JoinHandle<()> {
        let stream = TcpStream::connect(address).await.unwrap();
        let (mut reader, mut writer) = stream.into_split();
        write_json(
            &mut writer,
            &json!({
                "role":"unity", "sessionId":id, "workspace":workspace,
                "projectName":id, "unityPid":pid, "packageVersion":"test"
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

    async fn connect_unity(
        address: std::net::SocketAddr,
        id: &str,
        workspace: &str,
        heartbeat_status: Option<&str>,
    ) -> JoinHandle<()> {
        connect_unity_pid(address, id, workspace, heartbeat_status, 1).await
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
        // See the comment on `wait_for_resumed`: ~3s deadline (200 * 15ms) to
        // absorb CPU starvation on a loaded CI runner without slowing down the
        // common case, which returns as soon as the condition first holds.
        for attempt in 0..200 {
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
            tokio::time::sleep(Duration::from_millis(15)).await;
        }
        panic!("did not observe {expected} sessions")
    }

    async fn wait_for_session_status(
        reader: &mut OwnedReadHalf,
        writer: &mut OwnedWriteHalf,
        session_id: &str,
        expected_status: &str,
    ) -> Value {
        // See the comment on `wait_for_resumed`: ~3s deadline (200 * 15ms) to
        // absorb CPU starvation on a loaded CI runner without slowing down the
        // common case, which returns as soon as the condition first holds.
        for attempt in 0..200 {
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
            tokio::time::sleep(Duration::from_millis(15)).await;
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
        loop {
            let envelope = read_json(&mut unity_reader).await.unwrap().unwrap();
            if envelope["request"]["id"] == "pending" {
                break;
            }
        }
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
                command: "get_editor_state".into(),
                envelope: json!({}),
            },
        );
        state.pending.insert(
            "live-agent".into(),
            PendingRequest {
                agent_id: 8,
                session_id: "unity".into(),
                session_connection_id: 1,
                command: "get_editor_state".into(),
                envelope: json!({}),
            },
        );

        remove_pending_for_agent(&state, 7);

        assert!(!state.pending.contains_key("closed-agent"));
        assert!(state.pending.contains_key("live-agent"));
    }

    #[test]
    fn agent_disconnect_drops_its_held_reload_requests() {
        let state = BrokerState::default();
        state.reloading.insert(
            "reload-id".into(),
            ReloadingSession {
                workspace: "E:/Projects/A".into(),
                project_name: "A".into(),
                unity_pid: Some(1),
                package_version: None,
                since: Instant::now(),
                reload_count: 0,
                held: vec![
                    HeldRequest {
                        id: "closed-agent-read".into(),
                        agent_id: 7,
                        envelope: json!({}),
                        dispatched: false,
                    },
                    HeldRequest {
                        id: "live-agent-read".into(),
                        agent_id: 8,
                        envelope: json!({}),
                        dispatched: true,
                    },
                ],
            },
        );

        remove_pending_for_agent(&state, 7);

        let held_ids: Vec<String> = state
            .reloading
            .get("reload-id")
            .unwrap()
            .held
            .iter()
            .map(|held| held.id.clone())
            .collect();
        assert_eq!(held_ids, vec!["live-agent-read".to_string()]);
    }

    #[tokio::test]
    async fn assembly_reload_parks_session_and_replays_read_request() {
        let (address, broker) = start_broker(test_config()).await;
        let (mut unity_reader, mut unity_writer) =
            connect_unity_raw(address, "a", "E:/Projects/A", 7, 0).await;
        let (mut agent_reader, mut agent_writer) = connect_agent(address, "E:/Projects/A").await;
        wait_for_sessions(&mut agent_reader, &mut agent_writer, 1).await;

        request(
            &mut agent_writer,
            "read-1",
            "get_editor_state",
            Some("E:/Projects/A"),
            Some("a"),
        )
        .await;
        read_matching_request(&mut unity_reader, "read-1").await;

        write_json(
            &mut unity_writer,
            &json!({"type":"unregister","sessionId":"a","reason":"assemblyReload"}),
        )
        .await
        .unwrap();
        wait_for_session_status(&mut agent_reader, &mut agent_writer, "a", "reloading").await;

        let (mut resumed_reader, mut resumed_writer) =
            connect_unity_raw(address, "a", "E:/Projects/A", 7, 1).await;
        let replayed = read_matching_request(&mut resumed_reader, "read-1").await;
        assert_eq!(replayed["request"]["command"], "get_editor_state");
        write_json(
            &mut resumed_writer,
            &ok("read-1".to_string(), json!({"echo": true})),
        )
        .await
        .unwrap();

        let response = read_json(&mut agent_reader).await.unwrap().unwrap();
        assert_eq!(response["id"], "read-1");
        assert_eq!(response["success"], true);
        assert_eq!(response["result"]["echo"], true);

        wait_for_resumed(&mut agent_reader, &mut agent_writer, "a").await;
        broker.abort();
    }

    #[tokio::test]
    async fn assembly_reload_fails_in_flight_write_command_with_reload_interrupted() {
        let (address, broker) = start_broker(test_config()).await;
        let (mut unity_reader, mut unity_writer) =
            connect_unity_raw(address, "a", "E:/Projects/A", 7, 0).await;
        let (mut agent_reader, mut agent_writer) = connect_agent(address, "E:/Projects/A").await;
        wait_for_sessions(&mut agent_reader, &mut agent_writer, 1).await;

        request(
            &mut agent_writer,
            "write-1",
            "set_property",
            Some("E:/Projects/A"),
            Some("a"),
        )
        .await;
        read_matching_request(&mut unity_reader, "write-1").await;

        write_json(
            &mut unity_writer,
            &json!({"type":"unregister","sessionId":"a","reason":"assemblyReload"}),
        )
        .await
        .unwrap();

        let response = read_json(&mut agent_reader).await.unwrap().unwrap();
        assert_eq!(response["id"], "write-1");
        assert_eq!(response["error"]["code"], "SESSION_RELOAD_INTERRUPTED");
        broker.abort();
    }

    #[tokio::test]
    async fn request_during_reload_window_is_held_for_read_and_rejected_for_write() {
        let (address, broker) = start_broker(test_config()).await;
        let (_unity_reader, mut unity_writer) =
            connect_unity_raw(address, "a", "E:/Projects/A", 7, 0).await;
        let (mut agent_reader, mut agent_writer) = connect_agent(address, "E:/Projects/A").await;
        wait_for_sessions(&mut agent_reader, &mut agent_writer, 1).await;

        write_json(
            &mut unity_writer,
            &json!({"type":"unregister","sessionId":"a","reason":"assemblyReload"}),
        )
        .await
        .unwrap();
        wait_for_session_status(&mut agent_reader, &mut agent_writer, "a", "reloading").await;

        // A write command never touches Unity while reloading; it fails right away.
        request(
            &mut agent_writer,
            "write-2",
            "set_property",
            Some("E:/Projects/A"),
            Some("a"),
        )
        .await;
        let write_response = read_json(&mut agent_reader).await.unwrap().unwrap();
        assert_eq!(write_response["id"], "write-2");
        assert_eq!(write_response["error"]["code"], "SESSION_RELOADING");

        // A read command is held silently until Unity resumes.
        request(
            &mut agent_writer,
            "read-2",
            "get_editor_state",
            Some("E:/Projects/A"),
            Some("a"),
        )
        .await;
        assert!(
            tokio::time::timeout(Duration::from_millis(80), read_json(&mut agent_reader))
                .await
                .is_err()
        );

        let (mut resumed_reader, mut resumed_writer) =
            connect_unity_raw(address, "a", "E:/Projects/A", 7, 1).await;
        let replayed = read_matching_request(&mut resumed_reader, "read-2").await;
        assert_eq!(replayed["request"]["command"], "get_editor_state");
        write_json(&mut resumed_writer, &ok("read-2".to_string(), json!({})))
            .await
            .unwrap();

        let response = read_json(&mut agent_reader).await.unwrap().unwrap();
        assert_eq!(response["id"], "read-2");
        assert_eq!(response["success"], true);
        broker.abort();
    }

    #[tokio::test]
    async fn broker_survives_while_a_session_is_parked_for_reload() {
        // `park_session_for_reload` moves the session out of `state.sessions` into
        // `state.reloading`. The idle-exit check in `serve` used to look only at
        // `state.sessions.is_empty()`, so in a single-editor setup the broker would
        // start counting down its last-session grace the instant a reload began --
        // and could exit the process entirely well before a real reload (seconds to
        // tens of seconds) finishes, stranding every held request with no broker
        // left to answer it.
        let mut config = test_config();
        config.startup_grace = Duration::from_millis(30);
        config.last_session_grace = Duration::from_millis(30);
        config.reload_grace = Duration::from_secs(5);
        let (address, broker) = start_broker(config).await;
        let (_unity_reader, mut unity_writer) =
            connect_unity_raw(address, "a", "E:/Projects/A", 7, 0).await;
        let (mut agent_reader, mut agent_writer) = connect_agent(address, "E:/Projects/A").await;
        wait_for_sessions(&mut agent_reader, &mut agent_writer, 1).await;

        write_json(
            &mut unity_writer,
            &json!({"type":"unregister","sessionId":"a","reason":"assemblyReload"}),
        )
        .await
        .unwrap();
        wait_for_session_status(&mut agent_reader, &mut agent_writer, "a", "reloading").await;

        // Comfortably longer than startup_grace + last_session_grace, but still
        // well inside reload_grace.
        tokio::time::sleep(Duration::from_millis(200)).await;

        assert!(
            !broker.is_finished(),
            "broker exited while a session was still parked for reload"
        );
        let sessions =
            wait_for_session_status(&mut agent_reader, &mut agent_writer, "a", "reloading").await;
        assert_eq!(sessions["result"].as_array().unwrap().len(), 1);
        broker.abort();
    }

    #[tokio::test]
    async fn reload_grace_expiry_fails_held_requests() {
        let mut config = test_config();
        config.reload_grace = Duration::from_millis(60);
        let (address, broker) = start_broker(config).await;
        let (mut unity_reader, mut unity_writer) =
            connect_unity_raw(address, "a", "E:/Projects/A", 7, 0).await;
        let (mut agent_reader, mut agent_writer) = connect_agent(address, "E:/Projects/A").await;
        wait_for_sessions(&mut agent_reader, &mut agent_writer, 1).await;

        // Already dispatched to Unity before the reload begins.
        request(
            &mut agent_writer,
            "read-dispatched",
            "get_editor_state",
            Some("E:/Projects/A"),
            Some("a"),
        )
        .await;
        read_matching_request(&mut unity_reader, "read-dispatched").await;

        write_json(
            &mut unity_writer,
            &json!({"type":"unregister","sessionId":"a","reason":"assemblyReload"}),
        )
        .await
        .unwrap();
        wait_for_session_status(&mut agent_reader, &mut agent_writer, "a", "reloading").await;

        // Only ever queued during the reload window; Unity never saw it.
        request(
            &mut agent_writer,
            "read-queued",
            "get_editor_state",
            Some("E:/Projects/A"),
            Some("a"),
        )
        .await;

        let mut responses = std::collections::HashMap::new();
        for _ in 0..2 {
            let response =
                tokio::time::timeout(Duration::from_millis(500), read_json(&mut agent_reader))
                    .await
                    .unwrap()
                    .unwrap()
                    .unwrap();
            responses.insert(response["id"].as_str().unwrap().to_string(), response);
        }

        assert_eq!(
            responses["read-dispatched"]["error"]["code"],
            "SESSION_RELOAD_INTERRUPTED"
        );
        assert_eq!(
            responses["read-queued"]["error"]["code"],
            "SESSION_UNAVAILABLE"
        );
        broker.abort();
    }

    #[tokio::test]
    async fn reloading_session_is_reported_as_reloading_by_sessions_and_health() {
        let (address, broker) = start_broker(test_config()).await;
        let (_unity_reader, mut unity_writer) =
            connect_unity_raw(address, "a", "E:/Projects/A", 7, 3).await;
        let (mut agent_reader, mut agent_writer) = connect_agent(address, "E:/Projects/A").await;
        wait_for_sessions(&mut agent_reader, &mut agent_writer, 1).await;

        write_json(
            &mut unity_writer,
            &json!({"type":"unregister","sessionId":"a","reason":"assemblyReload"}),
        )
        .await
        .unwrap();
        let sessions =
            wait_for_session_status(&mut agent_reader, &mut agent_writer, "a", "reloading").await;
        let session = &sessions["result"][0];
        assert_eq!(session["state"], "reloading");
        assert_eq!(session["reloadCount"], 3);
        assert!(session["heldRequestCount"].is_number());

        request(
            &mut agent_writer,
            "health",
            "__patina_broker_health",
            None,
            None,
        )
        .await;
        let health = read_json(&mut agent_reader).await.unwrap().unwrap();
        assert_eq!(health["result"]["reloadingSessionCount"], 1);
        assert_eq!(health["result"]["unitySessionCount"], 1);
        broker.abort();
    }

    #[tokio::test]
    async fn unregister_without_reason_still_fails_pending_immediately() {
        let (address, broker) = start_broker(test_config()).await;
        let (mut unity_reader, mut unity_writer) =
            connect_unity_raw(address, "a", "E:/Projects/A", 7, 0).await;
        let (mut agent_reader, mut agent_writer) = connect_agent(address, "E:/Projects/A").await;
        wait_for_sessions(&mut agent_reader, &mut agent_writer, 1).await;

        request(
            &mut agent_writer,
            "legacy-1",
            "get_editor_state",
            Some("E:/Projects/A"),
            Some("a"),
        )
        .await;
        read_matching_request(&mut unity_reader, "legacy-1").await;

        // Old UPM 1.1.13 never sends a "reason" field on unregister.
        write_json(&mut unity_writer, &json!({"type":"unregister"}))
            .await
            .unwrap();

        let response = read_json(&mut agent_reader).await.unwrap().unwrap();
        assert_eq!(response["id"], "legacy-1");
        assert_eq!(response["error"]["code"], "SESSION_UNAVAILABLE");

        wait_for_sessions(&mut agent_reader, &mut agent_writer, 0).await;
        broker.abort();
    }

    #[tokio::test]
    async fn pinned_session_id_survives_assembly_reload() {
        let (address, broker) = start_broker(test_config()).await;
        let (_unity_reader, mut unity_writer) =
            connect_unity_raw(address, "pinned", "E:/Projects/A", 9, 0).await;
        let (mut agent_reader, mut agent_writer) = connect_agent(address, "E:/Projects/A").await;
        wait_for_sessions(&mut agent_reader, &mut agent_writer, 1).await;

        write_json(
            &mut unity_writer,
            &json!({"type":"unregister","sessionId":"pinned","reason":"assemblyReload"}),
        )
        .await
        .unwrap();
        wait_for_session_status(&mut agent_reader, &mut agent_writer, "pinned", "reloading").await;

        let (_resumed_reader, _resumed_writer) =
            connect_unity_raw(address, "pinned", "E:/Projects/A", 9, 1).await;
        let sessions = wait_for_resumed(&mut agent_reader, &mut agent_writer, "pinned").await;
        let live = sessions["result"].as_array().unwrap();
        assert_eq!(live.len(), 1);
        assert_eq!(live[0]["sessionId"], "pinned");
        assert_ne!(live[0]["state"], "reloading");
        broker.abort();
    }

    #[tokio::test]
    async fn heartbeat_write_failure_evicts_session() {
        let state = BrokerState::default();
        let listener = TcpListener::bind("127.0.0.1:0").await.unwrap();
        let address = listener.local_addr().unwrap();
        let (client, server) = tokio::join!(TcpStream::connect(address), listener.accept());
        let client = client.unwrap();
        let (_server_reader, server_writer) = server.unwrap().0.into_split();
        drop(client); // close the peer so a write on server_writer eventually fails

        let session = UnitySession {
            id: "unity".into(),
            connection_id: 1,
            workspace: "E:/Projects/A".into(),
            project_name: "A".into(),
            unity_pid: Some(1),
            package_version: None,
            writer: Arc::new(Mutex::new(server_writer)),
            last_seen: Arc::new(Mutex::new(Instant::now())),
            health: Arc::new(Mutex::new("responsive".into())),
            reload_count: 0,
            connected_at: Instant::now(),
        };
        state.sessions.insert(session.id.clone(), session);

        // A closed peer does not guarantee the very first write observes an
        // error (the OS can absorb a frame or two), so retry briefly.
        for _ in 0..50 {
            if state.sessions.is_empty() {
                break;
            }
            send_heartbeats(&state).await;
            tokio::time::sleep(Duration::from_millis(10)).await;
        }
        assert!(state.sessions.is_empty());
    }

    #[tokio::test]
    async fn dispatch_finalization_fails_pending_when_session_vanished() {
        let unity_listener = TcpListener::bind("127.0.0.1:0").await.unwrap();
        let unity_address = unity_listener.local_addr().unwrap();
        let (unity_client, unity_server) =
            tokio::join!(TcpStream::connect(unity_address), unity_listener.accept());
        let _unity_client = unity_client.unwrap();
        let (_unity_server_reader, unity_writer) = unity_server.unwrap().0.into_split();

        let agent_listener = TcpListener::bind("127.0.0.1:0").await.unwrap();
        let agent_address = agent_listener.local_addr().unwrap();
        let (agent_client, agent_server) =
            tokio::join!(TcpStream::connect(agent_address), agent_listener.accept());
        let (mut agent_reader, _agent_client_writer) = agent_client.unwrap().into_split();
        let (_agent_server_reader, agent_server_writer) = agent_server.unwrap().0.into_split();

        let state = BrokerState::default();
        let session = UnitySession {
            id: "unity".into(),
            connection_id: 1,
            workspace: "E:/Projects/A".into(),
            project_name: "A".into(),
            unity_pid: Some(1),
            package_version: None,
            writer: Arc::new(Mutex::new(unity_writer)),
            last_seen: Arc::new(Mutex::new(Instant::now())),
            health: Arc::new(Mutex::new("responsive".into())),
            reload_count: 0,
            connected_at: Instant::now(),
        };
        state.sessions.insert(session.id.clone(), session.clone());
        state
            .agents
            .insert(42, Arc::new(Mutex::new(agent_server_writer)));
        state.pending.insert(
            "req-1".into(),
            PendingRequest {
                agent_id: 42,
                session_id: session.id.clone(),
                session_connection_id: session.connection_id,
                command: "get_editor_state".into(),
                envelope: json!({}),
            },
        );

        assert!(finalize_dispatch(&state, "req-1", &session, "Unity session write failed.").await);
        assert!(state.sessions.get("unity").is_none());

        let response = read_json(&mut agent_reader).await.unwrap().unwrap();
        assert_eq!(response["id"], "req-1");
        assert_eq!(response["error"]["code"], "SESSION_UNAVAILABLE");

        // A second caller racing on the same request id must not observe (or
        // resend a response for) the already-claimed pending entry.
        assert!(!finalize_dispatch(&state, "req-1", &session, "Unity session write failed.").await);
    }

    /// Guards the `SelectedTarget::Live` dispatch arm's post-write
    /// `else if !session_still_registered(...)` check: if that check is ever
    /// removed or broken, a request whose session gets parked/replaced *between*
    /// the successful `write_json` to Unity and this re-check would sit forever
    /// with no error and no result (see `dispatch_after_successful_write_fails_when_session_was_parked`
    /// below for the deterministic, helper-level regression test of the same
    /// logic -- that one calls `session_still_registered`/`finalize_dispatch`
    /// directly and stays green even if the `handle_connection` dispatch arm
    /// itself is broken, which is why this end-to-end test exists too).
    ///
    /// Ignored by default: this test races two real OS threads (a fresh
    /// `get_editor_state` request against a fresh `unregister`) up to 500 times
    /// to try to land the exact interleaving, because there is no externally
    /// observable "in progress" signal to synchronize on -- both sides are
    /// ordinary, fast async operations, and on the machine this was written on
    /// even a near-MAX_FRAME_BYTES forwarded write completed synchronously, so
    /// there is no reliable way to force a suspension point. On CI (ubuntu
    /// runner, 2026-08-12, PR #91) the race was never observed in 500 attempts
    /// and the test ran for ~87s pegging 4 worker threads, which starved
    /// neighboring tests' millisecond-scale polling deadlines and caused an
    /// unrelated, unreproducible failure in `reload_grace_expiry_fails_held_requests`.
    /// Anyone changing the `SelectedTarget::Live` dispatch arm must run this
    /// test manually (it is not part of `cargo test` or CI):
    ///   cargo test -- --ignored agent_request_fails_when_session_is_parked_immediately_after_successful_dispatch_write
    #[ignore]
    #[tokio::test(flavor = "multi_thread", worker_threads = 4)]
    async fn agent_request_fails_when_session_is_parked_immediately_after_successful_dispatch_write(
    ) {
        // Drives the *actual* production dispatch path in `handle_connection` end
        // to end (start_broker + connect_unity_raw + connect_agent), unlike the
        // helper-level `dispatch_after_successful_write_fails_when_session_was_parked`
        // test above, which calls `session_still_registered`/`finalize_dispatch`
        // directly and therefore cannot detect a regression in the `handle_connection`
        // dispatch arm itself (a reviewer confirmed this by deleting that arm's
        // `else if !session_still_registered(...)` branch entirely and observing the
        // full suite stay green).
        //
        // The race this targets -- Unity's connection task running
        // `park_session_for_reload` between the dispatch task's successful
        // `write_json` and its `session_still_registered` check -- has no
        // externally observable "in progress" signal to synchronize on (both
        // sides are ordinary, fast async operations; on this host even a
        // near-MAX_FRAME_BYTES forwarded write was observed completing
        // synchronously, so there's no reliable way to force a suspension
        // point to hang a deterministic interleaving off). A multi-threaded
        // runtime is used so the two connections' tasks can run truly
        // concurrently on separate OS threads instead of only cooperatively
        // interleaving at explicit `.await` yield points, and each reload
        // cycle races a fresh request against a fresh unregister; the loop
        // below repeats this until the exact race is observed. This mirrors
        // the retry-with-bounded-attempts pattern already used by
        // `heartbeat_write_failure_evicts_session` above for the same kind of
        // OS-timing-dependent condition.
        let mut config = test_config();
        // Keep unrelated eviction paths (heartbeat timeout, reload grace expiry)
        // from ever firing during a single attempt's short window, so any
        // SESSION_UNAVAILABLE/SESSION_RELOAD_INTERRUPTED response we observe can
        // only have come from the two code paths this test cares about: the
        // dispatch code's post-write check (what we're looking for) or
        // `park_session_for_reload`'s own immediate pending-scoop (not a match,
        // but also not a hang -- just means this attempt raced the other way).
        config.heartbeat_timeout = Duration::from_secs(30);
        config.reload_grace = Duration::from_secs(30);
        let (address, broker) = start_broker(config).await;
        let (mut agent_reader, mut agent_writer) = connect_agent(address, "E:/Projects/A").await;

        const ATTEMPTS: u32 = 500;
        let mut observed_target_race = false;
        for attempt in 0..ATTEMPTS {
            let (_unity_reader, mut unity_writer) =
                connect_unity_raw(address, "a", "E:/Projects/A", 7, attempt).await;
            wait_for_sessions(&mut agent_reader, &mut agent_writer, 1).await;

            let id = format!("race-{attempt}");
            // Fire the dispatchable request and the reload trigger back to back
            // with no synchronization between them, relying on true OS-thread
            // concurrency (see the runtime flavor above) to occasionally land the
            // unregister in the exact window this fix targets.
            request(
                &mut agent_writer,
                &id,
                "get_editor_state",
                Some("E:/Projects/A"),
                Some("a"),
            )
            .await;
            write_json(
                &mut unity_writer,
                &json!({"type":"unregister","sessionId":"a","reason":"assemblyReload"}),
            )
            .await
            .unwrap();

            if let Ok(Ok(Some(response))) =
                tokio::time::timeout(Duration::from_millis(200), read_json(&mut agent_reader)).await
            {
                if response["id"] == id
                    && response["error"]["code"] == "SESSION_UNAVAILABLE"
                    && response["error"]["message"]
                        == "Unity session disconnected immediately after this request was sent."
                {
                    observed_target_race = true;
                    break;
                }
                // Any other outcome for this attempt (most commonly
                // SESSION_RELOAD_INTERRUPTED from `park_session_for_reload`'s own
                // pending-scoop, when the unregister happened to be processed
                // first) is not a hang and not a bug -- it just means this
                // attempt's race landed the other way. Move on to the next one.
            }
            // Either outcome leaves the session parked or evicted; the next
            // iteration reconnects Unity fresh with an incremented reload count,
            // mirroring a real repeated-compile sequence.
        }

        assert!(
            observed_target_race,
            "did not observe the post-write session-parked race in {ATTEMPTS} attempts; \
             this either means the dispatch code's post-write registration check \
             (`else if !session_still_registered(...)` in the `SelectedTarget::Live` arm) \
             is missing/broken, or this race has become effectively unreachable on this host \
             and the test needs a new way to force the interleaving"
        );
        broker.abort();
    }

    #[tokio::test]
    async fn dispatch_after_successful_write_fails_when_session_was_parked() {
        // Reproduces the W6 race a live-Unity reviewer found: `park_session_for_reload`
        // snapshots pending ids *before* the agent task's `pending.insert`, and a plain
        // `remove_if` on a later sweep only matches a connection_id it still recognizes.
        // If the dispatch path did not re-check `session_still_registered` after a
        // successful write, a request whose session got parked/replaced in that exact
        // window would sit forever with no error and no result -- worse now that
        // `request_timeout_for` extended some client timeouts to 180s.
        let unity_listener = TcpListener::bind("127.0.0.1:0").await.unwrap();
        let unity_address = unity_listener.local_addr().unwrap();
        let (unity_client, unity_server) =
            tokio::join!(TcpStream::connect(unity_address), unity_listener.accept());
        let _unity_client = unity_client.unwrap();
        let (_unity_server_reader, unity_writer) = unity_server.unwrap().0.into_split();

        let agent_listener = TcpListener::bind("127.0.0.1:0").await.unwrap();
        let agent_address = agent_listener.local_addr().unwrap();
        let (agent_client, agent_server) =
            tokio::join!(TcpStream::connect(agent_address), agent_listener.accept());
        let (mut agent_reader, _agent_client_writer) = agent_client.unwrap().into_split();
        let (_agent_server_reader, agent_server_writer) = agent_server.unwrap().0.into_split();

        let state = BrokerState::default();
        let session = UnitySession {
            id: "unity".into(),
            connection_id: 1,
            workspace: "E:/Projects/A".into(),
            project_name: "A".into(),
            unity_pid: Some(1),
            package_version: None,
            writer: Arc::new(Mutex::new(unity_writer)),
            last_seen: Arc::new(Mutex::new(Instant::now())),
            health: Arc::new(Mutex::new("responsive".into())),
            reload_count: 0,
            connected_at: Instant::now(),
        };
        // The session is registered when dispatch selects it...
        state.sessions.insert(session.id.clone(), session.clone());
        state
            .agents
            .insert(42, Arc::new(Mutex::new(agent_server_writer)));
        state.pending.insert(
            "req-1".into(),
            PendingRequest {
                agent_id: 42,
                session_id: session.id.clone(),
                session_connection_id: session.connection_id,
                command: "get_editor_state".into(),
                envelope: json!({}),
            },
        );

        // The write to Unity succeeds -- the peer is alive and reads the frame.
        assert!(write_json(
            &mut *session.writer.lock().await,
            &json!({"type":"request","request":{"id":"req-1","command":"get_editor_state","params":{}}}),
        )
        .await
        .is_ok());

        // ...but immediately after, a concurrent sweep parks/replaces it, so the
        // dispatching task's local `session` handle (connection_id 1) no longer
        // matches what is registered under this id.
        state.sessions.remove(&session.id);

        assert!(!session_still_registered(&state, &session));
        assert!(
            finalize_dispatch(
                &state,
                "req-1",
                &session,
                "Unity session disconnected immediately after this request was sent.",
            )
            .await
        );

        let response = read_json(&mut agent_reader).await.unwrap().unwrap();
        assert_eq!(response["id"], "req-1");
        assert_eq!(response["error"]["code"], "SESSION_UNAVAILABLE");
        assert!(!state.pending.contains_key("req-1"));
    }

    #[test]
    fn select_session_reports_reloading_instead_of_not_found() {
        let state = BrokerState::default();
        state.reloading.insert(
            "reload-id".into(),
            ReloadingSession {
                workspace: "E:/Projects/A".into(),
                project_name: "A".into(),
                unity_pid: Some(1),
                package_version: None,
                since: Instant::now(),
                reload_count: 2,
                held: Vec::new(),
            },
        );

        let by_workspace = select_session(&state, Some("E:/Projects/A"), None).unwrap();
        assert!(matches!(by_workspace, SelectedTarget::Reloading(id) if id == "reload-id"));

        let by_id = select_session(&state, None, Some("reload-id")).unwrap();
        assert!(matches!(by_id, SelectedTarget::Reloading(id) if id == "reload-id"));
    }
}
