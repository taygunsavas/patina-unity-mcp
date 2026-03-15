mod bridge;
mod server;
mod tools;

use rmcp::{transport::stdio, ServiceExt};
use tracing::info;
use tracing_subscriber::{fmt, EnvFilter};

use bridge::BridgeClient;
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

#[tokio::main]
async fn main() -> anyhow::Result<()> {
    // All logging goes to stderr so stdout stays clean for MCP stdio transport
    fmt()
        .with_env_filter(EnvFilter::from_default_env())
        .with_writer(std::io::stderr)
        .init();

    let port = parse_port();
    info!("Starting Patina server, bridge port={}", port);

    let bridge = BridgeClient::new(port);
    let bridge_for_connect = bridge.clone();
    tokio::spawn(async move {
        if let Err(error) = bridge_for_connect.connect().await {
            tracing::error!("Initial Unity bridge connection failed: {}", error);
        }
    });

    let handler = UnityMcpServer::new(bridge);
    let service = handler.serve(stdio()).await?;
    service.waiting().await?;

    Ok(())
}

