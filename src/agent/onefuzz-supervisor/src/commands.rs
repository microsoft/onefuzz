// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use crate::auth::Secret;
use anyhow::Result;
use std::process::Stdio;
use tokio::{fs, io::AsyncWriteExt, process::Command};

#[cfg(target_os = "windows")]
use std::{env, path::PathBuf};

#[cfg(target_os = "linux")]
use users::{get_current_uid, get_user_by_name, get_user_by_uid, os::unix::UserExt};

#[derive(Debug, Deserialize, Eq, PartialEq, Serialize)]
pub struct SshKeyInfo {
    pub key: Secret<String>,
    pub user: Option<String>,
    set_permissions: bool,
}

#[cfg(target_os = "windows")]
pub async fn add_ssh_key(key_info: SshKeyInfo) -> Result<()> {
    if key_info.user.is_some() {
        bail!("specifying a user is not supported on Windows at this time");
    }

    let mut ssh_path =
        PathBuf::from(env::var("ProgramData").unwrap_or_else(|_| "c:\\programdata".to_string()));
    ssh_path.push("ssh");

    let host_key_path = ssh_path.join("ssh_host_dsa_key");
    let admin_auth_keys_path = ssh_path.join("administrators_authorized_keys");
    let mut key_path = ssh_path;

    dsa_path.push("ssh_host_dsa_key");
    key_path.push("administrators_authorized_keys");

    {
        let mut file = fs::OpenOptions::new().append(true).open(&key_path).await?;
        file.write_all(key_info.key.expose_ref().as_bytes()).await?;
    }

    if key_info.set_permissions {
        verbose!("removing Authenticated Users permissions from administrators_authorized_keys");
        let result = Command::new("icacls.exe")
            .arg(&key_path)
            .arg("/remove")
            .arg("NT AUTHORITY/Authenticated Users")
            .stdin(Stdio::null())
            .stdout(Stdio::piped())
            .stderr(Stdio::piped())
            .spawn()?
            .wait_with_output()
            .await?;
        if !result.status.success() {
            bail!(
                "set authorized_keys ({}) permissions failed: {:?}",
                key_path.display(),
                result
            );
        }

        verbose!("removing inheritance");
        let result = Command::new("icacls.exe")
            .arg(&key_path)
            .arg("/inheritance:r")
            .stdin(Stdio::null())
            .stdout(Stdio::piped())
            .stderr(Stdio::piped())
            .spawn()?
            .wait_with_output()
            .await?;
        if !result.status.success() {
            bail!(
                "set authorized_keys ({}) permissions failed: {:?}",
                key_path.display(),
                result
            );
        }

        verbose!("copying ACL from ssh_host_dsa_key");
        let result = Command::new("powershell.exe")
            .args(&["-ExecutionPolicy", "Unrestricted", "-Command"])
            .arg(format!(
                "Get-Acl \"{}\" | Set-Acl \"{}\"",
                dsa_path.display(),
                key_path.display()
            ))
            .stdin(Stdio::null())
            .stdout(Stdio::piped())
            .stderr(Stdio::piped())
            .spawn()?
            .wait_with_output()
            .await?;
        if !result.status.success() {
            bail!(
                "set authorized_keys ({}) permissions failed: {:?}",
                key_path.display(),
                result
            );
        }
    }

    info!("ssh key written: {}", key_path.display());

    Ok(())
}

#[cfg(target_os = "linux")]
pub async fn add_ssh_key(key_info: SshKeyInfo) -> Result<()> {
    let user = match &key_info.user {
        Some(user) => get_user_by_name(&user),
        None => get_user_by_uid(get_current_uid()),
    }
    .ok_or_else(|| format_err!("unable to find user"))?;
    info!("adding sshkey:{:?} to user:{:?}", key_info, user);

    let mut ssh_path = user.home_dir().to_owned();
    if !ssh_path.exists() {
        bail!("unable to add SSH key to missing home directory");
    }

    ssh_path.push(".ssh");

    if !ssh_path.exists() {
        verbose!("creating ssh directory: {}", ssh_path.display());
        fs::create_dir_all(&ssh_path).await?;
    }

    if key_info.set_permissions {
        verbose!("setting ssh permissions");
        let result = Command::new("chmod")
            .arg("700")
            .arg(&ssh_path)
            .stdin(Stdio::null())
            .stdout(Stdio::piped())
            .stderr(Stdio::piped())
            .spawn()?
            .wait_with_output()
            .await?;
        if !result.status.success() {
            bail!("set $HOME/.ssh permissions failed: {:?}", result);
        }
    }

    ssh_path.push("authorized_keys");

    {
        let mut file = fs::OpenOptions::new().append(true).open(&ssh_path).await?;
        file.write_all(key_info.key.expose_ref().as_bytes()).await?;
    }

    if key_info.set_permissions {
        verbose!("setting authorized_keys permissions");
        let result = Command::new("chmod")
            .arg("600")
            .arg(&ssh_path)
            .stdin(Stdio::null())
            .stdout(Stdio::piped())
            .stderr(Stdio::piped())
            .spawn()?
            .wait_with_output()
            .await?;
        if !result.status.success() {
            bail!(
                "set authorized_keys ({}) permissions failed: {:?}",
                ssh_path.display(),
                result
            );
        }
    }

    info!("ssh key written: {}", ssh_path.display());

    Ok(())
}
