// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use crate::fs::{onefuzz_etc, write_file};
use anyhow::{Context, Result};
use reqwest_retry::SendRetry;
#[cfg(target_os = "linux")]
use std::path::Path;
use std::time::Duration;
use tokio::fs;
use uuid::Uuid;

// https://docs.microsoft.com/en-us/azure/virtual-machines/windows/instance-metadata-service#tracking-vm-running-on-azure
const IMS_ID_URL: &str =
    "http://169.254.169.254/metadata/instance/compute/vmId?api-version=2020-06-01&format=text";

// The machine name has the following format <scaleset name>_<vm instance id >
const VM_NAME_URL: &str =
    "http://169.254.169.254/metadata/instance/compute/name?api-version=2020-06-01&format=text";

const VM_SCALESET_NAME: &str =
    "http://169.254.169.254/metadata/instance/compute/vmScaleSetName?api-version=2020-06-01&format=text";

pub async fn get_ims_id() -> Result<Uuid> {
    let path = onefuzz_etc()?.join("ims_id");
    let body = match fs::read_to_string(&path).await {
        Ok(body) => body,
        Err(_) => {
            let resp = reqwest::Client::new()
                .get(IMS_ID_URL)
                .timeout(Duration::from_millis(500))
                .header("Metadata", "true")
                .send_retry_default()
                .await
                .context("get_ims_id")?;
            let body = resp.text().await?;
            write_file(path, &body).await?;
            body
        }
    };

    let value = Uuid::parse_str(&body)?;
    Ok(value)
}

pub async fn get_machine_name() -> Result<String> {
    let path = onefuzz_etc()?.join("machine_name");
    let body = match fs::read_to_string(&path).await {
        Ok(body) => body,
        Err(_) => {
            let resp = reqwest::Client::new()
                .get(VM_NAME_URL)
                .timeout(Duration::from_millis(500))
                .header("Metadata", "true")
                .send_retry_default()
                .await
                .context("get_machine_name")?;
            let body = resp.text().await?;
            write_file(path, &body).await?;
            body
        }
    };

    Ok(body)
}

pub async fn get_scaleset_name() -> Result<Option<String>> {
    let path = onefuzz_etc()?.join("scaleset_name");
    if let Ok(scaleset_name) = fs::read_to_string(&path).await {
        return Ok(Some(scaleset_name));
    }

    if let Ok(resp) = reqwest::Client::new()
        .get(VM_SCALESET_NAME)
        .timeout(Duration::from_millis(500))
        .header("Metadata", "true")
        .send_retry_default()
        .await
    {
        let body = resp.text().await?;
        write_file(path, &body).await?;
        Ok(Some(body))
    } else {
        Ok(None)
    }
}

#[cfg(target_os = "linux")]
pub async fn get_os_machine_id() -> Result<Uuid> {
    let path = Path::new("/etc/machine-id");
    let contents = fs::read_to_string(&path)
        .await
        .with_context(|| format!("unable to read machine_id: {}", path.display()))?;
    let uuid = Uuid::parse_str(contents.trim())?;
    Ok(uuid)
}

#[cfg(target_os = "windows")]
pub async fn get_os_machine_id() -> Result<Uuid> {
    use winreg::enums::{HKEY_LOCAL_MACHINE, KEY_READ, KEY_WOW64_64KEY};
    use winreg::RegKey;

    let key: &str = "SOFTWARE\\Microsoft\\Cryptography";

    let hklm = RegKey::predef(HKEY_LOCAL_MACHINE);
    let crypt = if let Ok(crypt) = hklm.open_subkey_with_flags(key, KEY_READ) {
        crypt
    } else {
        hklm.open_subkey_with_flags(key, KEY_READ | KEY_WOW64_64KEY)?
    };
    let guid: String = crypt.get_value("MachineGuid")?;
    Ok(Uuid::parse_str(&guid)?)
}

async fn get_machine_id_impl() -> Result<Uuid> {
    let ims_id = get_ims_id().await;
    if ims_id.is_ok() {
        return ims_id;
    }

    let machine_id = get_os_machine_id().await;
    if machine_id.is_ok() {
        return machine_id;
    }

    Ok(Uuid::new_v4())
}

pub async fn get_machine_id() -> Result<Uuid> {
    let path = onefuzz_etc()?.join("machine_id");
    let result = match fs::read_to_string(&path).await {
        Ok(body) => Uuid::parse_str(&body)?,
        Err(_) => {
            let value = get_machine_id_impl().await?;
            write_file(path, &value.to_string()).await?;
            value
        }
    };
    Ok(result)
}

#[tokio::test]
async fn test_get_machine_id() {
    get_os_machine_id().await.unwrap();
}
