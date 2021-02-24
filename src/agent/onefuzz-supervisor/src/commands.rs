// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use crate::auth::Secret;
use anyhow::{Context, Result};
use onefuzz::machine_id::get_scaleset_name;
use std::process::Stdio;
use tokio::{fs, io::AsyncWriteExt, process::Command};

#[cfg(target_os = "windows")]
use std::{env, path::PathBuf};

#[cfg(target_os = "linux")]
use users::{get_user_by_name, os::unix::UserExt};

#[cfg(target_os = "linux")]
const ONEFUZZ_SERVICE_USER: &str = "onefuzz";

#[derive(Debug, Deserialize, Eq, PartialEq, Serialize)]
pub struct SshKeyInfo {
    pub public_key: Secret<String>,
}

#[cfg(target_os = "windows")]
pub async fn add_ssh_key(key_info: SshKeyInfo) -> Result<()> {
    if get_scaleset_name().await?.is_none() {
        warn!("adding ssh keys only supported on managed nodes");
        return Ok(());
    }

    let mut ssh_path =
        PathBuf::from(env::var("ProgramData").unwrap_or_else(|_| "c:\\programdata".to_string()));
    ssh_path.push("ssh");

    let host_key_path = ssh_path.join("ssh_host_dsa_key");
    let admin_auth_keys_path = ssh_path.join("administrators_authorized_keys");

    {
        let mut file = fs::OpenOptions::new()
            .append(true)
            .open(&admin_auth_keys_path)
            .await?;
        file.write_all(key_info.public_key.expose_ref().as_bytes())
            .await?;
    }

    debug!("removing Authenticated Users permissions from administrators_authorized_keys");
    let result = Command::new("icacls.exe")
        .arg(&admin_auth_keys_path)
        .arg("/remove")
        .arg("NT AUTHORITY/Authenticated Users")
        .stdin(Stdio::null())
        .stdout(Stdio::piped())
        .stderr(Stdio::piped())
        .spawn()
        .context("icacls failed to start")?
        .wait_with_output()
        .await
        .context("icalcs failed to run")?;
    if !result.status.success() {
        bail!(
            "set authorized_keys ({}) permissions failed: {:?}",
            admin_auth_keys_path.display(),
            result
        );
    }

    debug!("removing inheritance");
    let result = Command::new("icacls.exe")
        .arg(&admin_auth_keys_path)
        .arg("/inheritance:r")
        .stdin(Stdio::null())
        .stdout(Stdio::piped())
        .stderr(Stdio::piped())
        .spawn()
        .context("icacls failed to start")?
        .wait_with_output()
        .await
        .context("icacls failed to run")?;
    if !result.status.success() {
        bail!(
            "set authorized_keys ({}) permissions failed: {:?}",
            admin_auth_keys_path.display(),
            result
        );
    }

    debug!("copying ACL from ssh_host_dsa_key");
    let result = Command::new("powershell.exe")
        .args(&["-ExecutionPolicy", "Unrestricted", "-Command"])
        .arg(format!(
            "Get-Acl \"{}\" | Set-Acl \"{}\"",
            admin_auth_keys_path.display(),
            host_key_path.display()
        ))
        .stdin(Stdio::null())
        .stdout(Stdio::piped())
        .stderr(Stdio::piped())
        .spawn()
        .context("Powershell Get-ACL | Set-ACL failed to start")?
        .wait_with_output()
        .await
        .context("Powershell Get-ACL | Set-ACL failed to run")?;
    if !result.status.success() {
        bail!(
            "set authorized_keys ({}) permissions failed: {:?}",
            admin_auth_keys_path.display(),
            result
        );
    }

    info!("ssh key written: {}", admin_auth_keys_path.display());

    Ok(())
}

#[cfg(target_os = "linux")]
pub async fn add_ssh_key(key_info: SshKeyInfo) -> Result<()> {
    if get_scaleset_name().await?.is_none() {
        warn!("adding ssh keys only supported on managed nodes");
        return Ok(());
    }

    let user =
        get_user_by_name(ONEFUZZ_SERVICE_USER).ok_or_else(|| format_err!("unable to find user"))?;
    info!("adding sshkey:{:?} to user:{:?}", key_info, user);

    let home_path = user.home_dir().to_owned();
    if !home_path.exists() {
        bail!("unable to add SSH key to missing home directory");
    }

    let mut ssh_path = home_path.join(".ssh");
    if !ssh_path.exists() {
        debug!("creating ssh directory: {}", ssh_path.display());
        fs::create_dir_all(&ssh_path).await?;
    }

    debug!("setting ssh permissions");
    let result = Command::new("chmod")
        .arg("700")
        .arg(&ssh_path)
        .stdin(Stdio::null())
        .stdout(Stdio::piped())
        .stderr(Stdio::piped())
        .spawn()
        .context("chmod failed to start")?
        .wait_with_output()
        .await
        .context("chmod failed to run")?;
    if !result.status.success() {
        bail!("set $HOME/.ssh permissions failed: {:?}", result);
    }

    ssh_path.push("authorized_keys");

    {
        let mut file = fs::OpenOptions::new().append(true).open(&ssh_path).await?;
        file.write_all(key_info.public_key.expose_ref().as_bytes())
            .await?;
    }

    debug!("setting authorized_keys permissions");
    let result = Command::new("chmod")
        .arg("600")
        .arg(&ssh_path)
        .stdin(Stdio::null())
        .stdout(Stdio::piped())
        .stderr(Stdio::piped())
        .spawn()
        .context("chmod failed to start")?
        .wait_with_output()
        .await
        .context("chmod failed to run")?;
    if !result.status.success() {
        bail!(
            "set authorized_keys ({}) permissions failed: {:?}",
            ssh_path.display(),
            result
        );
    }

    info!("ssh key written: {}", ssh_path.display());

    Ok(())
}
