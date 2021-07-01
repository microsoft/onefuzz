// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use anyhow::{Context, Result};
use onefuzz::{auth::Secret, machine_id::get_scaleset_name};
use std::process::Stdio;
use tokio::{fs, io::AsyncWriteExt, process::Command};

#[cfg(target_family = "windows")]
use std::{env, path::PathBuf};

#[cfg(target_family = "windows")]
use tokio::sync::{OnceCell, SetError};

#[cfg(target_family = "unix")]
use users::{get_user_by_name, os::unix::UserExt};

#[cfg(target_family = "unix")]
const ONEFUZZ_SERVICE_USER: &str = "onefuzz";

// On Windows, removing permissions that have already been removed fails.  As
// such, this needs to happen once and only once.  NOTE: SSH keys are added as
// node commands, which are processed serially.  As such, this should never get
// called concurrently.
#[cfg(target_family = "windows")]
static SET_PERMISSION_ONCE: OnceCell<()> = OnceCell::const_new();

#[derive(Debug, Deserialize, Eq, PartialEq, Serialize)]
pub struct SshKeyInfo {
    pub public_key: Secret<String>,
}

#[cfg(target_family = "windows")]
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

    match SET_PERMISSION_ONCE.set(()) {
        Ok(_) => {
            debug!("removing Authenticated Users permissions from administrators_authorized_keys");

            let result = Command::new("icacls.exe")
                .arg(&admin_auth_keys_path)
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
                    "checking permissions failed: '{}' failed: {:?}",
                    admin_auth_keys_path.display(),
                    result
                );
            }

            if result.stdout.to_string().contains("NT AUTHORITY\\SYSTEM") {
                let result = Command::new("icacls.exe")
                    .arg(&admin_auth_keys_path)
                    .arg("/remove")
                    .arg("NT AUTHORITY/Authenticated Users")
                    .stdin(Stdio::null())
                    .stdout(Stdio::piped())
                    .stderr(Stdio::piped())
                    .spawn()
                    .context("icacls remove failed to start")?
                    .wait_with_output()
                    .await
                    .context("icalcs remove failed to run")?;
                if !result.status.success() {
                    warn!(
                    "removing 'NT AUTHORITY/Authenticated Users' permissions to '{}' failed: {:?}",
                    admin_auth_keys_path.display(),
                    result
                );
                }
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
                    "removing permission inheretence to '{}' failed: {:?}",
                    admin_auth_keys_path.display(),
                    result
                );
            }

            debug!("copying ACL from ssh_host_dsa_key");
            let result = Command::new("powershell.exe")
                .args(&["-ExecutionPolicy", "Unrestricted", "-Command"])
                .arg(format!(
                    "Get-Acl \"{}\" | Set-Acl \"{}\"",
                    host_key_path.display(),
                    admin_auth_keys_path.display(),
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
                    "copying ACL from '{}' to '{}' permissions failed: {:?}",
                    host_key_path.display(),
                    admin_auth_keys_path.display(),
                    result
                );
            }
        }
        Err(SetError::InitializingError(())) => {
            bail!("add_ssh_key must not be called concurrently");
        }
        // do nothing if already initialized
        Err(SetError::AlreadyInitializedError(())) => {}
    }

    info!("ssh key written: {}", admin_auth_keys_path.display());

    Ok(())
}

#[cfg(target_family = "unix")]
pub async fn add_ssh_key(key_info: SshKeyInfo) -> Result<()> {
    if get_scaleset_name().await?.is_none() {
        warn!("adding ssh keys only supported on managed nodes");
        return Ok(());
    }

    let user =
        get_user_by_name(ONEFUZZ_SERVICE_USER).ok_or_else(|| format_err!("unable to find user"))?;
    info!("adding ssh key:{:?} to user:{:?}", key_info, user);

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
