// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use crate::config::ConfigData;
use anyhow::Result;
use std::{collections::HashMap, path::Path};
use tokio::process::Command;

const SYSTEMD_CONFIG_DIR: &str = "/etc/systemd/system";
const PROXY_PREFIX: &str = "onefuzz-proxy";

fn build(data: &ConfigData) -> HashMap<String, String> {
    let mut results = HashMap::new();

    for entry in &data.forwards {
        let socket_filename = format!("{}-{}.socket", PROXY_PREFIX, entry.src_port);
        let service_filename = format!("{}-{}.service", PROXY_PREFIX, entry.src_port);
        let socket = format!(
            r##"
[Socket]
ListenStream={}:{}
BindIPv6Only=both
[Install]
WantedBy=sockets.target
"##,
            entry.src_ip, entry.src_port
        );
        let service = format!(
            r##"
[Unit]
Requires=onefuzz-proxy-{}.socket
After=onefuzz-proxy-{}.socket
[Service]
ExecStart=/lib/systemd/systemd-socket-proxyd {}:{}
"##,
            entry.src_port, entry.src_port, entry.dst_ip, entry.dst_port
        );
        results.insert(socket_filename, socket);
        results.insert(service_filename, service);
    }

    results
}

async fn stop_service(service: &str) -> Result<()> {
    Command::new("systemctl")
        .arg("stop")
        .arg(service)
        .spawn()?
        .wait_with_output()
        .await?;
    Ok(())
}

async fn start_service(service: &str) -> Result<()> {
    Command::new("systemctl")
        .arg("start")
        .arg(service)
        .spawn()?
        .wait_with_output()
        .await?;
    Ok(())
}

async fn restart_systemd() -> Result<()> {
    Command::new("systemctl")
        .arg("daemon-reload")
        .spawn()?
        .wait_with_output()
        .await?;
    Ok(())
}

pub async fn update(data: &ConfigData) -> Result<()> {
    let configs = build(data);

    let mut config_dir = tokio::fs::read_dir(SYSTEMD_CONFIG_DIR).await?;
    loop {
        let entry = match config_dir.next_entry().await {
            Ok(Some(entry)) => entry,
            Ok(None) => break,
            Err(err) => {
                error!("error listing files {}", err);
                continue;
            }
        };
        let path = entry.path();
        if !path.is_file() {
            continue;
        }

        let file_name = path.file_name().unwrap().to_string_lossy().to_string();
        if !file_name.starts_with(&PROXY_PREFIX) {
            continue;
        }

        if configs.contains_key(&file_name) {
            let raw = tokio::fs::read(&path).await?;
            let contents = String::from_utf8_lossy(&raw).to_string();
            if configs[&file_name] != contents {
                info!("updating config: {}", file_name);

                tokio::fs::remove_file(&path).await?;
                stop_service(&file_name).await?;
                restart_systemd().await?;

                tokio::fs::write(&path, configs[&file_name].clone()).await?;
                start_service(&file_name).await?;
            }
        } else {
            info!("stopping proxy {}", file_name);

            tokio::fs::remove_file(&path).await?;
            stop_service(&file_name).await?;
            restart_systemd().await?;
        }
    }

    for (file_name, content) in &configs {
        let path = Path::new(SYSTEMD_CONFIG_DIR).join(file_name);
        if !path.is_file() {
            info!("adding service {}", file_name);
            tokio::fs::write(&path, content).await?;
            start_service(file_name).await?;
        }
    }

    Ok(())
}
