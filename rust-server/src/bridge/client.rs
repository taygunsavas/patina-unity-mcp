use std::sync::atomic::{AtomicBool, AtomicU64, Ordering};
use std::sync::Arc;
use std::time::Duration;

use dashmap::DashMap;
use tokio::io::{AsyncReadExt, AsyncWriteExt};
use tokio::net::tcp::{OwnedReadHalf, OwnedWriteHalf};
use tokio::net::TcpStream;
use tokio::sync::{oneshot, watch, Mutex};
use tokio::task::JoinHandle;
use tracing::{error, info, warn};
use uuid::Uuid;

use super::protocol::{BridgeRequest, BridgeResponse};

type PendingMap = Arc<DashMap<String, oneshot::Sender<BridgeResponse>>>;
const MAX_FRAME_BYTES: usize = 4 * 1024 * 1024;

struct PublishedWriter {
    epoch: u64,
    writer: OwnedWriteHalf,
}

/// A one-way lifecycle for the MCP process's single Unity TCP connection.
pub struct BridgeClient {
    writer: Mutex<Option<PublishedWriter>>,
    /// Serializes publishing a newly connected writer with shutdown.
    publication_lock: Mutex<()>,
    lifecycle_task: Mutex<Option<JoinHandle<()>>>,
    pending: PendingMap,
    port: u16,
    workspace: String,
    is_shutdown: AtomicBool,
    shutdown_tx: watch::Sender<bool>,
    reconnect_tx: watch::Sender<u64>,
    next_epoch: AtomicU64,
}

impl BridgeClient {
    pub fn new(port: u16) -> Arc<Self> {
        let workspace = std::env::current_dir()
            .ok()
            .and_then(|path| path.canonicalize().ok().or(Some(path)))
            .map(|path| path.to_string_lossy().replace('\\', "/"))
            .unwrap_or_default();
        Self::new_with_workspace(port, workspace)
    }

    /// Creates a client with an explicit workspace. The production constructor
    /// derives this from the MCP process working directory; keeping the
    /// transport constructor injectable makes default-routing behavior testable.
    pub(crate) fn new_with_workspace(port: u16, workspace: String) -> Arc<Self> {
        let (shutdown_tx, _) = watch::channel(false);
        let (reconnect_tx, _) = watch::channel(0);
        Arc::new(Self {
            writer: Mutex::new(None),
            publication_lock: Mutex::new(()),
            lifecycle_task: Mutex::new(None),
            pending: Arc::new(DashMap::new()),
            port,
            workspace,
            is_shutdown: AtomicBool::new(false),
            shutdown_tx,
            reconnect_tx,
            next_epoch: AtomicU64::new(0),
        })
    }

    pub fn port(&self) -> u16 {
        self.port
    }

    /// The canonical working directory sent to the shared broker for default
    /// workspace routing. Kept raw for diagnostics; the broker normalizes it
    /// before selecting a Unity session.
    pub fn workspace(&self) -> &str {
        &self.workspace
    }

    pub async fn start(self: &Arc<Self>) {
        if self.is_shutdown() {
            warn!("Bridge client start ignored after shutdown");
            return;
        }
        let mut task = self.lifecycle_task.lock().await;
        if task.is_none() {
            let client = self.clone();
            *task = Some(tokio::spawn(async move {
                client.run_lifecycle().await;
            }));
        }
    }

    pub async fn shutdown(&self) {
        if self.is_shutdown.swap(true, Ordering::AcqRel) {
            return;
        }
        info!("Shutting down Unity bridge client");
        let _publication = self.publication_lock.lock().await;
        let _ = self.shutdown_tx.send(true);
        self.clear_pending_requests();
        self.close_published_writer().await;
        drop(_publication);
        if let Some(task) = self.lifecycle_task.lock().await.take() {
            if let Err(error) = task.await {
                warn!("Unity bridge lifecycle task ended unexpectedly: {}", error);
            }
        }
    }

    async fn run_lifecycle(self: Arc<Self>) {
        loop {
            let (epoch, reader) = match self.connect_with_backoff().await {
                Ok(connection) => connection,
                Err(_) => return,
            };
            self.read_until_invalidated(epoch, reader).await;
            self.clear_pending_requests();
            self.close_published_writer_if_epoch(epoch).await;
            if self.is_shutdown() {
                return;
            }
            warn!("Bridge connection ended, reconnecting to Unity");
        }
    }

    async fn connect_with_backoff(self: &Arc<Self>) -> Result<(u64, OwnedReadHalf), String> {
        let mut backoff = Duration::from_millis(500);
        while !self.is_shutdown() {
            match self.try_connect_once().await {
                Ok(reader) => return Ok(reader),
                Err(error) if self.is_shutdown() => return Err(error),
                Err(error) => {
                    warn!(
                        "Failed to connect to Unity bridge: {}. Retrying in {:?}",
                        error, backoff
                    );
                    if !self.wait_or_shutdown(backoff).await {
                        break;
                    }
                    backoff = (backoff * 2).min(Duration::from_secs(30));
                }
            }
        }
        Err("Unity bridge client is shutting down".to_string())
    }

    async fn try_connect_once(self: &Arc<Self>) -> Result<(u64, OwnedReadHalf), String> {
        if self.is_shutdown() {
            return Err("Unity bridge client is shutting down".to_string());
        }
        let address = format!("127.0.0.1:{}", self.port);
        info!("Connecting to Unity bridge at tcp://{}", address);
        let mut shutdown = self.shutdown_tx.subscribe();
        if *shutdown.borrow() {
            return Err("Unity bridge client is shutting down".to_string());
        }
        let mut stream = tokio::select! {
            result = TcpStream::connect(&address) => result.map_err(|error| error.to_string())?,
            _ = shutdown.changed() => return Err("Unity bridge client is shutting down".to_string()),
        };
        stream
            .set_nodelay(true)
            .map_err(|error| error.to_string())?;
        let hello =
            serde_json::to_vec(&serde_json::json!({"role":"agent", "workspace": self.workspace}))
                .map_err(|e| e.to_string())?;
        stream
            .write_all(&(hello.len() as u32).to_le_bytes())
            .await
            .map_err(|e| e.to_string())?;
        stream.write_all(&hello).await.map_err(|e| e.to_string())?;
        stream.flush().await.map_err(|e| e.to_string())?;
        let (reader, writer) = stream.into_split();

        let _publication = self.publication_lock.lock().await;
        if self.is_shutdown() || *self.shutdown_tx.borrow() {
            drop(writer);
            drop(reader);
            return Err("Unity bridge client is shutting down".to_string());
        }
        let epoch = self.next_epoch.fetch_add(1, Ordering::AcqRel) + 1;
        *self.writer.lock().await = Some(PublishedWriter { epoch, writer });
        info!("Connected to Unity bridge");
        Ok((epoch, reader))
    }

    async fn ensure_connected(&self) -> Result<(), String> {
        // A request must not turn the MCP host into a long synchronous retry loop while
        // the lifecycle task is already reconnecting in the background.
        let deadline = tokio::time::Instant::now() + Duration::from_secs(1);
        loop {
            if self.is_shutdown() {
                return Err("Unity bridge client is shutting down".to_string());
            }
            if self.writer.lock().await.is_some() {
                return Ok(());
            }
            if tokio::time::Instant::now() >= deadline {
                return Err("BROKER_UNAVAILABLE: Patina's shared broker is not connected to a Unity session yet. Keep Unity open and retry after its broker reconnects.".to_string());
            }
            if !self.wait_or_shutdown(Duration::from_millis(50)).await {
                return Err("Unity bridge client is shutting down".to_string());
            }
        }
    }

    async fn read_until_invalidated(&self, epoch: u64, mut reader: OwnedReadHalf) {
        let mut shutdown = self.shutdown_tx.subscribe();
        let mut reconnect = self.reconnect_tx.subscribe();
        if *shutdown.borrow() || !self.is_epoch_published(epoch).await {
            return;
        }
        loop {
            tokio::select! {
                _ = shutdown.changed() => return,
                _ = reconnect.changed() => return,
                frame = read_frame(&mut reader) => match frame {
                    Ok(Some(text)) => match serde_json::from_str::<BridgeResponse>(&text) {
                        Ok(response) => {
                            let id = response.id.clone();
                            if let Some((_, sender)) = self.pending.remove(&id) { let _ = sender.send(response); }
                            else { warn!("Received response for unknown request id: {}", id); }
                        }
                        Err(error) => error!("Failed to parse bridge response: {}", error),
                    },
                    Ok(None) => { warn!("Unity bridge closed the TCP connection"); return; }
                    Err(error) => { error!("Unity bridge read error: {}", error); return; }
                }
            }
        }
    }

    async fn invalidate_connection(&self, epoch: u64) {
        if self.close_published_writer_if_epoch(epoch).await {
            self.reconnect_tx
                .send_modify(|generation| *generation = generation.wrapping_add(1));
        }
    }

    async fn close_published_writer(&self) {
        if let Some(mut published) = self.writer.lock().await.take() {
            if let Err(error) = published.writer.shutdown().await {
                warn!(
                    "Failed to gracefully shut down Unity bridge TCP writer: {}",
                    error
                );
            }
        }
    }

    async fn close_published_writer_if_epoch(&self, epoch: u64) -> bool {
        let writer = {
            let mut guard = self.writer.lock().await;
            if guard.as_ref().map(|published| published.epoch) == Some(epoch) {
                guard.take()
            } else {
                None
            }
        };
        if let Some(mut published) = writer {
            if let Err(error) = published.writer.shutdown().await {
                warn!(
                    "Failed to gracefully shut down Unity bridge TCP writer: {}",
                    error
                );
            }
            true
        } else {
            false
        }
    }

    async fn is_epoch_published(&self, epoch: u64) -> bool {
        self.writer
            .lock()
            .await
            .as_ref()
            .map(|published| published.epoch)
            == Some(epoch)
    }

    fn is_shutdown(&self) -> bool {
        self.is_shutdown.load(Ordering::Acquire)
    }
    async fn wait_or_shutdown(&self, duration: Duration) -> bool {
        let mut shutdown = self.shutdown_tx.subscribe();
        if *shutdown.borrow() {
            return false;
        }
        tokio::select! { _ = tokio::time::sleep(duration) => true, _ = shutdown.changed() => false }
    }
    fn clear_pending_requests(&self) {
        self.pending.retain(|_, _| false);
    }

    pub async fn request(
        self: &Arc<Self>,
        command: &str,
        params: serde_json::Value,
    ) -> Result<BridgeResponse, String> {
        self.request_targeted(command, params, None, None).await
    }

    pub async fn request_targeted(
        self: &Arc<Self>,
        command: &str,
        params: serde_json::Value,
        workspace: Option<&str>,
        session_id: Option<&str>,
    ) -> Result<BridgeResponse, String> {
        // A real assembly reload can hold the broker's parked session for up to
        // BrokerConfig::reload_grace (120s by default); this deadline must
        // comfortably exceed that so a normal compile-triggered reload always has
        // time to finish before the client gives up.
        const RELOAD_RETRY_INTERVAL: Duration = Duration::from_millis(500);
        const RELOAD_RETRY_DEADLINE: Duration = Duration::from_secs(150);
        let deadline = tokio::time::Instant::now() + RELOAD_RETRY_DEADLINE;

        loop {
            let response = self
                .request_targeted_once(command, params.clone(), workspace, session_id)
                .await?;
            if response.success {
                return Ok(response);
            }
            let retryable = response
                .error
                .as_ref()
                .is_some_and(|error| is_reload_retryable_code(&error.code));
            if !retryable || tokio::time::Instant::now() >= deadline {
                return Ok(response);
            }
            // SESSION_RELOADING is only produced by the broker for requests it
            // never wrote to Unity, so retrying here can never run a write
            // command twice. Anything the broker already sent to Unity comes
            // back as SESSION_RELOAD_INTERRUPTED instead, which
            // `is_reload_retryable_code` deliberately excludes.
            if !self.wait_or_shutdown(RELOAD_RETRY_INTERVAL).await {
                return Err("Unity bridge client is shutting down".to_string());
            }
        }
    }

    async fn request_targeted_once(
        self: &Arc<Self>,
        command: &str,
        params: serde_json::Value,
        workspace: Option<&str>,
        session_id: Option<&str>,
    ) -> Result<BridgeResponse, String> {
        const BACKOFFS: [Duration; 3] = [
            Duration::from_millis(200),
            Duration::from_millis(400),
            Duration::from_millis(800),
        ];
        for (attempt, backoff) in BACKOFFS.iter().copied().enumerate() {
            match self
                .do_request(command, &params, workspace, session_id)
                .await
            {
                Ok(response) => return Ok(response),
                Err(error) if is_transient_bridge_error(&error) => {
                    warn!(
                        "Transient bridge error on attempt {}/{}: {}",
                        attempt + 1,
                        BACKOFFS.len(),
                        error
                    );
                    if let Some(epoch) = connection_epoch_from_error(&error) {
                        self.invalidate_connection(epoch).await;
                    }
                    if !self.wait_or_shutdown(backoff).await {
                        return Err("Unity bridge client is shutting down".to_string());
                    }
                }
                Err(error) => return Err(error),
            }
        }
        self.do_request(command, &params, workspace, session_id)
            .await
    }

    async fn do_request(
        self: &Arc<Self>,
        command: &str,
        params: &serde_json::Value,
        workspace: Option<&str>,
        session_id: Option<&str>,
    ) -> Result<BridgeResponse, String> {
        self.ensure_connected().await?;
        let id = Uuid::new_v4().to_string();
        let request = BridgeRequest {
            id: id.clone(),
            command: command.to_string(),
            params: params.clone(),
        };
        let envelope_workspace = envelope_workspace(&self.workspace, workspace, session_id);
        let payload = serde_json::to_vec(&serde_json::json!({"type":"request", "workspace":envelope_workspace, "sessionId":session_id, "request":request}))
        .map_err(|error| format!("Serialize error: {}", error))?;
        let (sender, receiver) = oneshot::channel();
        self.pending.insert(id.clone(), sender);
        let write_result = {
            let mut guard = self.writer.lock().await;
            match guard.as_mut() {
                Some(published) => {
                    let epoch = published.epoch;
                    write_frame(&mut published.writer, &payload)
                        .await
                        .map_err(|error| {
                            format!("TCP send error [connection epoch {}]: {}", epoch, error)
                        })
                }
                None => Err("Not connected to Unity bridge".to_string()),
            }
        };
        if let Err(error) = write_result {
            self.pending.remove(&id);
            return Err(error);
        }
        let timeout = request_timeout_for(command);
        match tokio::time::timeout(timeout, receiver).await {
            Ok(Ok(response)) => Ok(response),
            Ok(Err(_)) => {
                self.pending.remove(&id);
                Err("Response channel closed unexpectedly".to_string())
            }
            Err(_) => {
                self.pending.remove(&id);
                Err(format!("Request '{}' timed out after {} seconds. Unity may be waiting for input in a modal dialog or save-changes prompt, or may still be compiling; resolve any Unity popup and retry.", command, timeout.as_secs()))
            }
        }
    }
}

/// Commands allowed to run for up to 180 seconds instead of the 60 second
/// default. These are the operations that can legitimately keep Unity's main
/// thread busy for a while: a full compile, a test run, or a scene/asset
/// operation that triggers one. A held request replayed after an assembly
/// reload (see broker.rs) needs this room too, since it does not start
/// running until the reload itself has already finished.
const LONG_RUNNING_COMMANDS: [&str; 10] = [
    "compile_and_get_errors",
    "force_recompile",
    "refresh_asset_database",
    "run_tests",
    "open_scene",
    "save_scene",
    "execute_menu_item",
    "validate_assets",
    "create_script",
    "request_script_reload",
];

/// Which workspace, if any, to put on the envelope. The MCP process working
/// directory is a *default target*, not a constraint: sent alongside an explicit
/// `session_id` it makes the broker cross-check a workspace the caller never
/// asked for and return SESSION_WORKSPACE_MISMATCH (issue #92). An explicit
/// workspace is always forwarded, so a caller supplying both still gets the
/// mismatch check.
fn envelope_workspace<'a>(
    default_workspace: &'a str,
    workspace: Option<&'a str>,
    session_id: Option<&str>,
) -> Option<&'a str> {
    match (workspace, session_id) {
        (Some(explicit), _) => Some(explicit),
        (None, Some(_)) => None,
        (None, None) => Some(default_workspace),
    }
}

fn request_timeout_for(command: &str) -> Duration {
    if LONG_RUNNING_COMMANDS.contains(&command) {
        Duration::from_secs(180)
    } else {
        Duration::from_secs(60)
    }
}

/// SESSION_RELOADING is the only broker error code that means "a Unity session
/// matching this request is present but mid assembly-reload; the broker never
/// wrote this request to it" (see broker.rs' `SelectedTarget::Reloading`
/// path), so it is the only code safe to retry automatically here.
///
/// SESSION_NOT_FOUND is deliberately excluded even though the broker also
/// never writes anything to Unity for it: it fires whenever there is no
/// matching Unity session at all (Unity closed, wrong sessionId/workspace, or
/// a pinned sessionId that really is gone after a full Unity restart), not
/// just during a reload window (a session mid-reload reports
/// SESSION_RELOADING instead, from the `SelectedTarget::Reloading` branch of
/// `select_session`). Retrying it here would turn an immediate, correct
/// "nothing to talk to" error into a mandatory 150s hang for every call made
/// while Unity is simply not running -- including `patina_health` with
/// `include_unity_state=true`.
///
/// Every other broker error (SESSION_RELOAD_INTERRUPTED, SESSION_UNAVAILABLE,
/// SESSION_AMBIGUOUS, SESSION_WORKSPACE_MISMATCH) must never be retried by
/// this loop either -- retrying could run a write command twice or mask a
/// caller mistake.
fn is_reload_retryable_code(code: &str) -> bool {
    matches!(code, "SESSION_RELOADING")
}

fn is_transient_bridge_error(error: &str) -> bool {
    error.contains("Response channel closed unexpectedly")
        || error.contains("TCP send error")
        || error.contains("Not connected")
}

fn connection_epoch_from_error(error: &str) -> Option<u64> {
    const PREFIX: &str = "TCP send error [connection epoch ";
    let epoch = error.strip_prefix(PREFIX)?.split_once(']')?.0;
    epoch.parse().ok()
}
async fn read_frame(reader: &mut OwnedReadHalf) -> Result<Option<String>, String> {
    let mut header = [0u8; 4];
    match reader.read_exact(&mut header).await {
        Ok(_) => {}
        Err(error)
            if matches!(
                error.kind(),
                std::io::ErrorKind::UnexpectedEof
                    | std::io::ErrorKind::ConnectionReset
                    | std::io::ErrorKind::ConnectionAborted
            ) =>
        {
            return Ok(None)
        }
        Err(error) => return Err(error.to_string()),
    }
    let length = u32::from_le_bytes(header) as usize;
    if length == 0 || length > MAX_FRAME_BYTES {
        return Err(format!("Invalid frame length: {}", length));
    }
    let mut payload = vec![0u8; length];
    reader
        .read_exact(&mut payload)
        .await
        .map_err(|error| error.to_string())?;
    String::from_utf8(payload)
        .map(Some)
        .map_err(|error| error.to_string())
}
async fn write_frame(writer: &mut OwnedWriteHalf, payload: &[u8]) -> Result<(), String> {
    if payload.is_empty() || payload.len() > MAX_FRAME_BYTES {
        return Err(format!("Invalid frame length: {}", payload.len()));
    }
    writer
        .write_all(&(payload.len() as u32).to_le_bytes())
        .await
        .map_err(|error| error.to_string())?;
    writer
        .write_all(payload)
        .await
        .map_err(|error| error.to_string())?;
    writer.flush().await.map_err(|error| error.to_string())
}

#[cfg(test)]
mod tests {
    use super::*;
    use tokio::io::{AsyncReadExt, AsyncWriteExt};
    use tokio::net::TcpListener;

    async fn read_request_id(stream: &mut TcpStream) -> String {
        let mut header = [0; 4];
        stream.read_exact(&mut header).await.unwrap();
        let mut body = vec![0; u32::from_le_bytes(header) as usize];
        stream.read_exact(&mut body).await.unwrap();
        serde_json::from_slice::<serde_json::Value>(&body).unwrap()["request"]["id"]
            .as_str()
            .unwrap()
            .to_string()
    }
    async fn read_request_envelope(stream: &mut TcpStream) -> serde_json::Value {
        let mut header = [0; 4];
        stream.read_exact(&mut header).await.unwrap();
        let mut body = vec![0; u32::from_le_bytes(header) as usize];
        stream.read_exact(&mut body).await.unwrap();
        serde_json::from_slice(&body).unwrap()
    }
    async fn drain_hello(stream: &mut TcpStream) {
        let mut header = [0; 4];
        stream.read_exact(&mut header).await.unwrap();
        let mut body = vec![0; u32::from_le_bytes(header) as usize];
        stream.read_exact(&mut body).await.unwrap();
        assert_eq!(
            serde_json::from_slice::<serde_json::Value>(&body).unwrap()["role"],
            "agent"
        );
    }
    async fn write_response(stream: &mut TcpStream, id: String) {
        let body =
            serde_json::to_vec(&serde_json::json!({"id": id, "success": true, "result": {}}))
                .unwrap();
        stream
            .write_all(&(body.len() as u32).to_le_bytes())
            .await
            .unwrap();
        stream.write_all(&body).await.unwrap();
        stream.flush().await.unwrap();
    }
    async fn write_error_response(stream: &mut TcpStream, id: String, code: &str, message: &str) {
        let body = serde_json::to_vec(&serde_json::json!({
            "id": id,
            "success": false,
            "result": null,
            "error": {"code": code, "message": message}
        }))
        .unwrap();
        stream
            .write_all(&(body.len() as u32).to_le_bytes())
            .await
            .unwrap();
        stream.write_all(&body).await.unwrap();
        stream.flush().await.unwrap();
    }

    #[tokio::test]
    async fn shutdown_closes_peer_is_idempotent_and_cannot_publish_late_connection() {
        let listener = TcpListener::bind("127.0.0.1:0").await.unwrap();
        let client = BridgeClient::new(listener.local_addr().unwrap().port());
        client.start().await;
        let (mut peer, _) = listener.accept().await.unwrap();
        drain_hello(&mut peer).await;
        client.shutdown().await;
        client.shutdown().await;
        let mut byte = [0; 1];
        assert_eq!(peer.read(&mut byte).await.unwrap(), 0);
        assert!(client.lifecycle_task.lock().await.is_none());
    }

    #[tokio::test]
    async fn shutdown_cancels_backoff_before_a_listener_can_accept() {
        let reserved = TcpListener::bind("127.0.0.1:0").await.unwrap();
        let port = reserved.local_addr().unwrap().port();
        drop(reserved);
        let client = BridgeClient::new(port);
        client.start().await;
        tokio::time::sleep(Duration::from_millis(25)).await;
        client.shutdown().await;
        let listener = TcpListener::bind(("127.0.0.1", port)).await.unwrap();
        assert!(
            tokio::time::timeout(Duration::from_millis(150), listener.accept())
                .await
                .is_err()
        );
    }

    #[tokio::test]
    async fn unavailable_broker_request_is_bounded_while_reconnect_continues() {
        let reserved = TcpListener::bind("127.0.0.1:0").await.unwrap();
        let port = reserved.local_addr().unwrap().port();
        drop(reserved);
        let client = BridgeClient::new(port);
        client.start().await;

        let result = tokio::time::timeout(
            Duration::from_secs(2),
            client.request("get_editor_state", serde_json::json!({})),
        )
        .await
        .expect("unavailable broker request should not hang")
        .expect_err("request should fail without a broker");
        assert!(result.starts_with("BROKER_UNAVAILABLE:"));
        assert!(client.lifecycle_task.lock().await.is_some());
        client.shutdown().await;
    }

    #[tokio::test]
    async fn shutdown_wins_when_a_connection_is_waiting_to_publish() {
        let listener = TcpListener::bind("127.0.0.1:0").await.unwrap();
        let client = BridgeClient::new(listener.local_addr().unwrap().port());
        let publication = client.publication_lock.lock().await;
        let connecting = {
            let client = client.clone();
            tokio::spawn(async move { client.try_connect_once().await })
        };
        let (mut peer, _) = listener.accept().await.unwrap();
        drain_hello(&mut peer).await;
        let stopping = {
            let client = client.clone();
            tokio::spawn(async move { client.shutdown().await })
        };
        while !client.is_shutdown() {
            tokio::task::yield_now().await;
        }
        drop(publication);
        assert!(connecting.await.unwrap().is_err());
        stopping.await.unwrap();
        let mut byte = [0; 1];
        assert_eq!(peer.read(&mut byte).await.unwrap(), 0);
        assert!(client.writer.lock().await.is_none());
    }

    #[tokio::test]
    async fn delayed_old_epoch_invalidation_keeps_new_writer() {
        let listener = TcpListener::bind("127.0.0.1:0").await.unwrap();
        let client = BridgeClient::new(listener.local_addr().unwrap().port());
        client.start().await;
        let (first, _) = listener.accept().await.unwrap();
        let old_epoch = tokio::time::timeout(Duration::from_secs(1), async {
            loop {
                if let Some(epoch) = client
                    .writer
                    .lock()
                    .await
                    .as_ref()
                    .map(|published| published.epoch)
                {
                    return epoch;
                }
                tokio::task::yield_now().await;
            }
        })
        .await
        .unwrap();

        client.invalidate_connection(old_epoch).await;
        drop(first);
        let (_second, _) = listener.accept().await.unwrap();
        let new_epoch = tokio::time::timeout(Duration::from_secs(1), async {
            loop {
                if let Some(epoch) = client
                    .writer
                    .lock()
                    .await
                    .as_ref()
                    .map(|published| published.epoch)
                {
                    if epoch != old_epoch {
                        return epoch;
                    }
                }
                tokio::task::yield_now().await;
            }
        })
        .await
        .unwrap();
        assert_ne!(old_epoch, new_epoch);

        client.invalidate_connection(old_epoch).await;
        assert_eq!(
            client.writer.lock().await.as_ref().unwrap().epoch,
            new_epoch
        );
        client.shutdown().await;
    }

    #[tokio::test]
    async fn request_reconnects_after_peer_drop() {
        let listener = TcpListener::bind("127.0.0.1:0").await.unwrap();
        let client = BridgeClient::new(listener.local_addr().unwrap().port());
        client.start().await;
        let server = tokio::spawn(async move {
            let (mut first, _) = listener.accept().await.unwrap();
            drain_hello(&mut first).await;
            drop(first);
            let (mut second, _) = listener.accept().await.unwrap();
            drain_hello(&mut second).await;
            let request_id = read_request_id(&mut second).await;
            write_response(&mut second, request_id).await;
        });
        let response = tokio::time::timeout(
            Duration::from_secs(5),
            client.request("test", serde_json::json!({})),
        )
        .await
        .unwrap()
        .unwrap();
        assert!(response.success);
        server.await.unwrap();
        client.shutdown().await;
    }

    #[tokio::test]
    async fn session_reloading_response_is_retried_until_session_returns() {
        let listener = TcpListener::bind("127.0.0.1:0").await.unwrap();
        let client = BridgeClient::new(listener.local_addr().unwrap().port());
        client.start().await;
        let server = tokio::spawn(async move {
            let (mut stream, _) = listener.accept().await.unwrap();
            drain_hello(&mut stream).await;
            let first_id = read_request_id(&mut stream).await;
            write_error_response(
                &mut stream,
                first_id,
                "SESSION_RELOADING",
                "Unity is reloading assemblies after a script compile.",
            )
            .await;
            let second_id = read_request_id(&mut stream).await;
            write_response(&mut stream, second_id).await;
        });
        let response = tokio::time::timeout(
            Duration::from_secs(5),
            client.request("compile_and_get_errors", serde_json::json!({})),
        )
        .await
        .unwrap()
        .unwrap();
        assert!(response.success);
        server.await.unwrap();
        client.shutdown().await;
    }

    #[tokio::test]
    async fn session_reload_interrupted_response_is_not_retried() {
        let listener = TcpListener::bind("127.0.0.1:0").await.unwrap();
        let client = BridgeClient::new(listener.local_addr().unwrap().port());
        client.start().await;
        let server = tokio::spawn(async move {
            let (mut stream, _) = listener.accept().await.unwrap();
            drain_hello(&mut stream).await;
            let id = read_request_id(&mut stream).await;
            write_error_response(
                &mut stream,
                id,
                "SESSION_RELOAD_INTERRUPTED",
                "Unity started an assembly reload while this command was running.",
            )
            .await;
            // Nothing else should ever arrive on this connection: a retry here
            // would mean the client resent a command that may have already run.
            let mut probe = [0u8; 1];
            let saw_more =
                tokio::time::timeout(Duration::from_millis(300), stream.read(&mut probe)).await;
            assert!(
                saw_more.is_err(),
                "client retried a response that must never be retried"
            );
        });
        let response = tokio::time::timeout(
            Duration::from_secs(2),
            client.request("set_property", serde_json::json!({})),
        )
        .await
        .unwrap()
        .unwrap();
        assert!(!response.success);
        assert_eq!(response.error.unwrap().code, "SESSION_RELOAD_INTERRUPTED");
        server.await.unwrap();
        client.shutdown().await;
    }

    #[tokio::test]
    async fn session_not_found_response_is_not_retried() {
        // SESSION_NOT_FOUND fires whenever there is no matching Unity session at
        // all (Unity closed, wrong workspace/sessionId, or a full restart), not
        // just during a reload window -- a session mid-reload reports
        // SESSION_RELOADING instead. Retrying this code would turn a correct,
        // immediate "nothing to talk to" error into a mandatory ~150s hang for
        // every call made while Unity simply is not running.
        let listener = TcpListener::bind("127.0.0.1:0").await.unwrap();
        let client = BridgeClient::new(listener.local_addr().unwrap().port());
        client.start().await;
        let server = tokio::spawn(async move {
            let (mut stream, _) = listener.accept().await.unwrap();
            drain_hello(&mut stream).await;
            let id = read_request_id(&mut stream).await;
            write_error_response(
                &mut stream,
                id,
                "SESSION_NOT_FOUND",
                "No matching Unity session. Call patina_sessions to see active workspaces, or provide workspace/sessionId.",
            )
            .await;
            let mut probe = [0u8; 1];
            let saw_more =
                tokio::time::timeout(Duration::from_millis(300), stream.read(&mut probe)).await;
            assert!(
                saw_more.is_err(),
                "client retried a response that must never be retried"
            );
        });
        let response = tokio::time::timeout(
            Duration::from_secs(2),
            client.request("get_hierarchy", serde_json::json!({})),
        )
        .await
        .unwrap()
        .unwrap();
        assert!(!response.success);
        assert_eq!(response.error.unwrap().code, "SESSION_NOT_FOUND");
        server.await.unwrap();
        client.shutdown().await;
    }

    #[tokio::test]
    async fn session_unavailable_response_is_not_retried() {
        let listener = TcpListener::bind("127.0.0.1:0").await.unwrap();
        let client = BridgeClient::new(listener.local_addr().unwrap().port());
        client.start().await;
        let server = tokio::spawn(async move {
            let (mut stream, _) = listener.accept().await.unwrap();
            drain_hello(&mut stream).await;
            let id = read_request_id(&mut stream).await;
            write_error_response(
                &mut stream,
                id,
                "SESSION_UNAVAILABLE",
                "The selected Unity session disconnected.",
            )
            .await;
            let mut probe = [0u8; 1];
            let saw_more =
                tokio::time::timeout(Duration::from_millis(300), stream.read(&mut probe)).await;
            assert!(
                saw_more.is_err(),
                "client retried a response that must never be retried"
            );
        });
        let response = tokio::time::timeout(
            Duration::from_secs(2),
            client.request("get_editor_state", serde_json::json!({})),
        )
        .await
        .unwrap()
        .unwrap();
        assert!(!response.success);
        assert_eq!(response.error.unwrap().code, "SESSION_UNAVAILABLE");
        server.await.unwrap();
        client.shutdown().await;
    }

    #[test]
    fn long_running_commands_get_extended_request_timeout() {
        for command in LONG_RUNNING_COMMANDS {
            assert_eq!(request_timeout_for(command), Duration::from_secs(180));
        }
        assert_eq!(
            request_timeout_for("get_editor_state"),
            Duration::from_secs(60)
        );
        assert_eq!(request_timeout_for("set_property"), Duration::from_secs(60));
    }

    #[test]
    fn request_script_reload_gets_extended_request_timeout() {
        assert_eq!(
            request_timeout_for("request_script_reload"),
            Duration::from_secs(180)
        );
    }

    #[test]
    fn envelope_workspace_applies_default_only_without_session_id() {
        assert_eq!(
            envelope_workspace("D:/default", None, None),
            Some("D:/default")
        );
        assert_eq!(envelope_workspace("D:/default", None, Some("sess")), None);
        assert_eq!(
            envelope_workspace("D:/default", Some("D:/other"), None),
            Some("D:/other")
        );
        assert_eq!(
            envelope_workspace("D:/default", Some("D:/other"), Some("sess")),
            Some("D:/other")
        );
    }

    #[tokio::test]
    async fn session_id_without_workspace_omits_default_workspace_from_envelope() {
        let listener = TcpListener::bind("127.0.0.1:0").await.unwrap();
        let client = BridgeClient::new(listener.local_addr().unwrap().port());
        client.start().await;
        let server = tokio::spawn(async move {
            let (mut stream, _) = listener.accept().await.unwrap();
            drain_hello(&mut stream).await;
            let envelope = read_request_envelope(&mut stream).await;
            assert!(envelope["workspace"].is_null());
            assert_eq!(envelope["sessionId"], "sess-1");
            let id = envelope["request"]["id"].as_str().unwrap().to_string();
            write_response(&mut stream, id).await;
        });
        let response = tokio::time::timeout(
            Duration::from_secs(5),
            client.request_targeted("test", serde_json::json!({}), None, Some("sess-1")),
        )
        .await
        .unwrap()
        .unwrap();
        assert!(response.success);
        server.await.unwrap();
        client.shutdown().await;
    }

    #[tokio::test]
    async fn explicit_workspace_with_session_id_is_forwarded_unchanged() {
        let listener = TcpListener::bind("127.0.0.1:0").await.unwrap();
        let client = BridgeClient::new(listener.local_addr().unwrap().port());
        client.start().await;
        let server = tokio::spawn(async move {
            let (mut stream, _) = listener.accept().await.unwrap();
            drain_hello(&mut stream).await;
            let envelope = read_request_envelope(&mut stream).await;
            assert_eq!(envelope["workspace"], "D:/other");
            assert_eq!(envelope["sessionId"], "sess-1");
            let id = envelope["request"]["id"].as_str().unwrap().to_string();
            write_response(&mut stream, id).await;
        });
        let response = tokio::time::timeout(
            Duration::from_secs(5),
            client.request_targeted(
                "test",
                serde_json::json!({}),
                Some("D:/other"),
                Some("sess-1"),
            ),
        )
        .await
        .unwrap()
        .unwrap();
        assert!(response.success);
        server.await.unwrap();
        client.shutdown().await;
    }

    #[tokio::test]
    async fn untargeted_request_still_sends_default_workspace() {
        let listener = TcpListener::bind("127.0.0.1:0").await.unwrap();
        let client = BridgeClient::new(listener.local_addr().unwrap().port());
        let default_workspace = client.workspace().to_string();
        client.start().await;
        let server = tokio::spawn(async move {
            let (mut stream, _) = listener.accept().await.unwrap();
            drain_hello(&mut stream).await;
            let envelope = read_request_envelope(&mut stream).await;
            assert_eq!(envelope["workspace"], default_workspace);
            assert!(envelope["sessionId"].is_null());
            let id = envelope["request"]["id"].as_str().unwrap().to_string();
            write_response(&mut stream, id).await;
        });
        let response = tokio::time::timeout(
            Duration::from_secs(5),
            client.request("test", serde_json::json!({})),
        )
        .await
        .unwrap()
        .unwrap();
        assert!(response.success);
        server.await.unwrap();
        client.shutdown().await;
    }
}
