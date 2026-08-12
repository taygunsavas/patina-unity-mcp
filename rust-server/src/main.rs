mod bridge;
mod server;
mod tools;

use rmcp::{transport::stdio, ServiceExt};
use tracing::info;
use tracing_subscriber::fmt::writer::MakeWriterExt;
use tracing_subscriber::{fmt, EnvFilter};

use bridge::{broker, BridgeClient};
use server::UnityMcpServer;

fn parse_port() -> u16 {
    let args: Vec<String> = std::env::args().collect();
    for i in 0..args.len() {
        if args[i] == "--port" {
            if let Some(port_str) = args.get(i + 1) {
                match port_str.parse::<u16>() {
                    Ok(port) => return port,
                    Err(e) => {
                        eprintln!("Error: invalid port number '{}': {}", port_str, e);
                        std::process::exit(1);
                    }
                }
            }
        }
    }
    9800
}

/// A `RUST_LOG`-independent default filter. `EnvFilter::from_default_env()`
/// silently falls back to accepting *everything* when `RUST_LOG` is unset,
/// which used to leave the broker without any log floor: the reload park/
/// resume events added for issue #84 need to show up (at `info`/`warn`) even
/// when nobody has set `RUST_LOG`.
fn default_env_filter() -> EnvFilter {
    EnvFilter::try_from_default_env().unwrap_or_else(|_| EnvFilter::new("patina_server=info,warn"))
}

/// A `MakeWriter` that appends to a shared log file. Unity launches the broker
/// as a detached child process and never reads its stdout/stderr pipes (a full
/// pipe would block a broker that outlives Unity), so this is the only place
/// operators can find the "Unity session parked for assembly reload" /
/// "...resumed after assembly reload" diagnostics without setting `RUST_LOG`.
#[derive(Clone)]
struct BrokerLogWriter(std::sync::Arc<std::sync::Mutex<std::fs::File>>);

impl std::io::Write for BrokerLogWriter {
    fn write(&mut self, buf: &[u8]) -> std::io::Result<usize> {
        let mut file = self
            .0
            .lock()
            .unwrap_or_else(|poisoned| poisoned.into_inner());
        file.write(buf)
    }

    fn flush(&mut self) -> std::io::Result<()> {
        let mut file = self
            .0
            .lock()
            .unwrap_or_else(|poisoned| poisoned.into_inner());
        file.flush()
    }
}

fn init_broker_tracing() {
    let log_path = std::env::temp_dir().join("patina-broker.log");
    match std::fs::OpenOptions::new()
        .create(true)
        .append(true)
        .open(&log_path)
    {
        Ok(file) => {
            let writer = BrokerLogWriter(std::sync::Arc::new(std::sync::Mutex::new(file)));
            // Mirror to both stderr (unchanged behavior when a parent process does
            // read it) and the log file (the durable diagnostic trail).
            let combined = std::io::stderr.and(move || writer.clone());
            fmt()
                .with_env_filter(default_env_filter())
                .with_writer(combined)
                .with_ansi(false)
                .init();
        }
        Err(error) => {
            eprintln!(
                "[Patina] Could not open broker log file {}: {}. Logging to stderr only.",
                log_path.display(),
                error
            );
            fmt()
                .with_env_filter(default_env_filter())
                .with_writer(std::io::stderr)
                .init();
        }
    }
}

#[tokio::main]
async fn main() -> anyhow::Result<()> {
    let port = parse_port();
    let is_broker = std::env::args().any(|arg| arg == "--broker");

    if is_broker {
        // Only the broker process gets file logging; the per-agent MCP process
        // keeps logging to stderr only, matching prior behavior exactly.
        init_broker_tracing();
        return broker::run(port).await;
    }

    // All logging goes to stderr so stdout stays clean for MCP stdio transport
    fmt()
        .with_env_filter(default_env_filter())
        .with_writer(std::io::stderr)
        .init();

    info!("Starting Patina server, bridge port={}", port);

    let bridge = BridgeClient::new(port);
    bridge.start().await;

    let handler = UnityMcpServer::new(bridge.clone());
    let service_result: anyhow::Result<()> = match handler.serve(stdio()).await {
        Ok(service) => service.waiting().await.map(|_| ()).map_err(Into::into),
        Err(error) => Err(error.into()),
    };
    bridge.shutdown().await;

    service_result
}
