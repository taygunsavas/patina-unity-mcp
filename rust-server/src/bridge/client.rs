use std::sync::Arc;
use std::time::Duration;

use dashmap::DashMap;
use tokio::io::{AsyncReadExt, AsyncWriteExt};
use tokio::net::tcp::{OwnedReadHalf, OwnedWriteHalf};
use tokio::net::TcpStream;
use tokio::sync::{oneshot, Mutex};
use tracing::{error, info, warn};
use uuid::Uuid;

use super::protocol::{BridgeRequest, BridgeResponse};

type PendingMap = Arc<DashMap<String, oneshot::Sender<BridgeResponse>>>;

const MAX_FRAME_BYTES: usize = 4 * 1024 * 1024;

pub struct BridgeClient {
    writer: Mutex<Option<OwnedWriteHalf>>,
    connect_guard: Mutex<()>,
    pending: PendingMap,
    port: u16,
}

impl BridgeClient {
    pub fn new(port: u16) -> Arc<Self> {
        Arc::new(Self {
            writer: Mutex::new(None),
            connect_guard: Mutex::new(()),
            pending: Arc::new(DashMap::new()),
            port,
        })
    }

    pub async fn connect(self: &Arc<Self>) -> anyhow::Result<()> {
        let mut backoff = Duration::from_millis(500);
        let max_backoff = Duration::from_secs(30);

        loop {
            match self.try_connect_once().await {
                Ok(()) => return Ok(()),
                Err(error) => {
                    warn!(
                        "Failed to connect to Unity bridge: {}. Retrying in {:?}",
                        error, backoff
                    );
                    tokio::time::sleep(backoff).await;
                    backoff = (backoff * 2).min(max_backoff);
                }
            }
        }
    }

    async fn ensure_connected(self: &Arc<Self>) -> Result<(), String> {
        if self.writer.lock().await.is_some() {
            return Ok(());
        }

        let _guard = self.connect_guard.lock().await;
        if self.writer.lock().await.is_some() {
            return Ok(());
        }

        let deadline = tokio::time::Instant::now() + Duration::from_secs(5);
        let mut backoff = Duration::from_millis(200);

        loop {
            match self.try_connect_once().await {
                Ok(()) => return Ok(()),
                Err(error) => {
                    if tokio::time::Instant::now() + backoff >= deadline {
                        return Err(format!("Not connected to Unity bridge: {}", error));
                    }

                    tokio::time::sleep(backoff).await;
                    backoff = (backoff * 2).min(Duration::from_secs(1));
                }
            }
        }
    }

    async fn try_connect_once(self: &Arc<Self>) -> Result<(), String> {
        if self.writer.lock().await.is_some() {
            return Ok(());
        }

        let address = format!("127.0.0.1:{}", self.port);
        info!("Connecting to Unity bridge at tcp://{}", address);

        let stream = TcpStream::connect(&address)
            .await
            .map_err(|error| error.to_string())?;
        stream
            .set_nodelay(true)
            .map_err(|error| error.to_string())?;

        let (reader, writer) = stream.into_split();
        *self.writer.lock().await = Some(writer);
        self.spawn_read_loop(reader);
        info!("Connected to Unity bridge");
        Ok(())
    }

    fn spawn_read_loop(self: &Arc<Self>, mut reader: OwnedReadHalf) {
        let pending = self.pending.clone();
        let client = self.clone();

        tokio::spawn(async move {
            loop {
                match read_frame(&mut reader).await {
                    Ok(Some(text)) => match serde_json::from_str::<BridgeResponse>(&text) {
                        Ok(response) => {
                            let id = response.id.clone();
                            if let Some((_, sender)) = pending.remove(&id) {
                                let _ = sender.send(response);
                            } else {
                                warn!("Received response for unknown request id: {}", id);
                            }
                        }
                        Err(error) => {
                            error!("Failed to parse bridge response: {}", error);
                        }
                    },
                    Ok(None) => {
                        warn!("Unity bridge closed the TCP connection");
                        break;
                    }
                    Err(error) => {
                        error!("Unity bridge read error: {}", error);
                        break;
                    }
                }
            }

            warn!("Bridge read loop ended, attempting reconnect");
            client.clear_pending_requests();
            *client.writer.lock().await = None;
            if let Err(error) = client.connect().await {
                error!("Reconnect failed: {}", error);
            }
        });
    }

    fn clear_pending_requests(&self) {
        self.pending.retain(|_, _| false);
    }

    pub async fn request(
        self: &Arc<Self>,
        command: &str,
        params: serde_json::Value,
    ) -> Result<BridgeResponse, String> {
        const MAX_RETRIES: u32 = 3;
        let backoffs = [
            Duration::from_millis(200),
            Duration::from_millis(400),
            Duration::from_millis(800),
        ];

        let mut last_error = String::new();

        for attempt in 0..=MAX_RETRIES {
            match self.do_request(command, &params).await {
                Ok(response) => return Ok(response),
                Err(error) if attempt < MAX_RETRIES && is_transient_bridge_error(&error) => {
                    warn!(
                        "Transient bridge error on attempt {}/{}: {}. Retrying in {:?}",
                        attempt + 1,
                        MAX_RETRIES,
                        error,
                        backoffs[attempt as usize]
                    );
                    // Clear the writer so ensure_connected triggers a fresh reconnect.
                    *self.writer.lock().await = None;
                    tokio::time::sleep(backoffs[attempt as usize]).await;
                    last_error = error;
                }
                Err(error) => return Err(error),
            }
        }

        Err(last_error)
    }

    async fn do_request(
        self: &Arc<Self>,
        command: &str,
        params: &serde_json::Value,
    ) -> Result<BridgeResponse, String> {
        self.ensure_connected().await?;

        let id = Uuid::new_v4().to_string();
        let request = BridgeRequest {
            id: id.clone(),
            command: command.to_string(),
            params: params.clone(),
        };

        let payload =
            serde_json::to_vec(&request).map_err(|error| format!("Serialize error: {}", error))?;

        let (sender, receiver) = oneshot::channel();
        self.pending.insert(id.clone(), sender);

        {
            let mut writer_guard = self.writer.lock().await;
            let writer = match writer_guard.as_mut() {
                Some(writer) => writer,
                None => {
                    self.pending.remove(&id);
                    return Err("Not connected to Unity bridge".to_string());
                }
            };

            if let Err(error) = write_frame(writer, &payload).await {
                self.pending.remove(&id);
                return Err(format!("TCP send error: {}", error));
            }
        }

        match tokio::time::timeout(Duration::from_secs(30), receiver).await {
            Ok(Ok(response)) => Ok(response),
            Ok(Err(_)) => {
                self.pending.remove(&id);
                Err("Response channel closed unexpectedly".to_string())
            }
            Err(_) => {
                self.pending.remove(&id);
                Err("Request timed out after 30 seconds".to_string())
            }
        }
    }
}

fn is_transient_bridge_error(error: &str) -> bool {
    error.contains("Response channel closed unexpectedly")
        || error.contains("TCP send error")
        || error.contains("Not connected")
}

async fn read_frame(reader: &mut OwnedReadHalf) -> Result<Option<String>, String> {
    let mut header = [0u8; 4];
    match reader.read_exact(&mut header).await {
        Ok(_) => {}
        Err(error) if error.kind() == std::io::ErrorKind::UnexpectedEof => return Ok(None),
        Err(error) if error.kind() == std::io::ErrorKind::ConnectionReset => return Ok(None),
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

    let header = (payload.len() as u32).to_le_bytes();
    writer
        .write_all(&header)
        .await
        .map_err(|error| error.to_string())?;
    writer
        .write_all(payload)
        .await
        .map_err(|error| error.to_string())?;
    writer.flush().await.map_err(|error| error.to_string())
}
