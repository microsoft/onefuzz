// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::{fmt, path::PathBuf};

use anyhow::Result;
use reqwest::Url;
use serde::{de, Serialize, Serializer};

#[derive(Clone, Eq, PartialEq)]
pub enum BlobUrl {
    AzureBlob(Url),
    LocalFile(PathBuf),
}

impl BlobUrl {
    pub fn new(url: Url) -> Result<Self> {
        if possible_blob_storage_url(&url, false) {
            if let Ok(path) = url.to_file_path() {
                return Ok(Self::LocalFile(path));
            } else {
                return Ok(Self::AzureBlob(url));
            }
        }
        bail!("Invalid blob URL: {}", url)
    }

    pub fn parse(url: impl AsRef<str>) -> Result<Self> {
        let url = Url::parse(url.as_ref())?;

        Self::new(url)
    }

    pub fn url(&self) -> Url {
        match self {
            Self::LocalFile(path) => {
                Url::from_file_path(path).expect("Could not convert path to url")
            }
            Self::AzureBlob(url) => url.clone(),
        }
    }

    pub fn account(&self) -> Option<String> {
        match self {
            Self::AzureBlob(url) => {
                // Ctor checks that domain has at least one subdomain.
                Some(url.domain().unwrap().split('.').next().unwrap().to_owned())
            }
            Self::LocalFile(_) => None,
        }
    }

    pub fn container(&self) -> Option<String> {
        match self {
            Self::AzureBlob(url) => {
                // Segment existence checked in ctor, so we can unwrap.
                Some(url.path_segments().unwrap().next().unwrap().to_owned())
            }
            Self::LocalFile(_) => None,
        }
    }

    pub fn name(&self) -> String {
        let name_segments: Vec<_> = self
            .url()
            .path_segments()
            .unwrap()
            .skip(match self {
                Self::AzureBlob(_) => 1,
                _ => 0,
            })
            .map(|s| s.to_owned())
            .collect();
        name_segments.join("/")
    }
}

impl fmt::Debug for BlobUrl {
    fn fmt(&self, f: &mut fmt::Formatter) -> fmt::Result {
        write!(f, "{}", redact_query_sas_sig(&self.url()))
    }
}

impl fmt::Display for BlobUrl {
    fn fmt(&self, f: &mut fmt::Formatter) -> fmt::Result {
        match self {
            Self::AzureBlob(_) => write!(
                f,
                "{}:{}/{}",
                self.account().unwrap_or_default(),
                self.container().unwrap_or_default(),
                self.name()
            ),
            Self::LocalFile(path) => write!(f, "{}", path.display()),
        }
    }
}

/// URL for accessing an Azure Blob Storage container.
///
/// Use to validate a URL and address contained blobs.
#[derive(Clone, Eq, PartialEq)]
pub struct BlobContainerUrl {
    url: Url,
}

impl BlobContainerUrl {
    pub fn new(url: Url) -> Result<Self> {
        if !possible_blob_container_url(&url) {
            bail!("Invalid container URL: {}", url);
        }

        Ok(Self { url })
    }

    pub fn as_file_path(&self) -> Option<PathBuf> {
        self.url.to_file_path().ok()
    }

    pub fn parse(url: impl AsRef<str>) -> Result<Self> {
        let url = Url::parse(url.as_ref())?;

        Self::new(url)
    }

    pub fn url(&self) -> &Url {
        &self.url
    }

    pub fn account(&self) -> Option<String> {
        if self.as_file_path().is_some() {
            None
        } else {
            // Ctor checks that domain has at least one subdomain.
            Some(
                self.url
                    .domain()
                    .unwrap()
                    .split('.')
                    .next()
                    .unwrap()
                    .to_owned(),
            )
        }
    }

    pub fn container(&self) -> Option<String> {
        if self.as_file_path().is_some() {
            None
        } else {
            Some(self.url.path_segments().unwrap().next().unwrap().to_owned())
        }
    }

    pub fn blob(&self, name: impl AsRef<str>) -> BlobUrl {
        let mut url = self.url.clone();
        name.as_ref().split('/').fold(
            &mut url.path_segments_mut().unwrap(), // Checked in ctor
            |segments, current| segments.push(current),
        );

        BlobUrl::new(url).expect("invalid blob URL from valid container")
    }
}

impl AsRef<Url> for BlobContainerUrl {
    fn as_ref(&self) -> &Url {
        self.url()
    }
}

impl fmt::Debug for BlobContainerUrl {
    fn fmt(&self, f: &mut fmt::Formatter) -> fmt::Result {
        write!(f, "{}", redact_query_sas_sig(self.url()))
    }
}

impl fmt::Display for BlobContainerUrl {
    fn fmt(&self, f: &mut fmt::Formatter) -> fmt::Result {
        if let Some(file_path) = self.as_file_path() {
            write!(f, "{:?}", file_path)
        } else if let (Some(account), Some(container)) = (self.account(), self.container()) {
            write!(f, "{}:{}", account, container)
        } else {
            panic!("invalid blob url")
        }
    }
}

impl From<BlobContainerUrl> for Url {
    fn from(container: BlobContainerUrl) -> Self {
        container.url
    }
}

fn redact_query_sas_sig(url: &Url) -> Url {
    let mut redacted = url.clone();
    redacted.set_query(None);

    for (k, v) in url.query_pairs() {
        let is_secret = &k == "sig";
        let v = if is_secret { "REDACTED" } else { &v };
        redacted.query_pairs_mut().append_pair(&k, v);
    }

    redacted
}

// Weak check of necessary conditions for a storage blob or container URL.
fn possible_blob_storage_url(url: &Url, container: bool) -> bool {
    if url.scheme() == "file" {
        return true;
    }

    if url.scheme() != "https" {
        return false;
    }

    // Must have a domain name, not an IP address.
    if url.domain().is_none() {
        return false;
    }

    // Rough check that we have at least have one subdomain.
    //
    // Unwrap ok due to last check.
    if !url.domain().unwrap().contains('.') {
        return false;
    }

    match url.path_segments() {
        Some(segments) => {
            let segments: Vec<_> = segments.collect();

            match segments.len() {
                // We always require _at least_ a container name.
                0 => return false,
                // If we aren't checking for a container URL, then there
                // should be at least 2 segments: 1 for the container, 1
                // or more for the blob name.
                1 => {
                    if !container {
                        return false;
                    }
                }
                // In this case, `n > 1`, and container blobs must have
                // _exactly_ 1 segment (the container name).
                _n => {
                    if container {
                        return false;
                    }
                }
            }

            // The container path segment must always be nonempty.
            //
            // If here, `segments.len() > 0`.
            if segments[0].is_empty() {
                return false;
            }
        }
        None => {
            return false;
        }
    }

    true
}

fn possible_blob_container_url(url: &Url) -> bool {
    possible_blob_storage_url(url, true)
}

impl Serialize for BlobContainerUrl {
    fn serialize<S>(&self, serializer: S) -> std::result::Result<S::Ok, S::Error>
    where
        S: Serializer,
    {
        let url = self.url.to_string();
        serializer.serialize_str(&url)
    }
}

impl<'de> de::Deserialize<'de> for BlobContainerUrl {
    fn deserialize<D>(de: D) -> std::result::Result<BlobContainerUrl, D::Error>
    where
        D: de::Deserializer<'de>,
    {
        de.deserialize_any(BlobContainerUrlVisitor)
    }
}

struct BlobContainerUrlVisitor;

impl<'de> de::Visitor<'de> for BlobContainerUrlVisitor {
    type Value = BlobContainerUrl;

    fn expecting(&self, f: &mut fmt::Formatter) -> fmt::Result {
        write!(f, "a valid blob storage container URL")
    }

    fn visit_str<E>(self, s: &str) -> Result<Self::Value, E>
    where
        E: de::Error,
    {
        BlobContainerUrl::parse(s).map_err(de::Error::custom)
    }
}

impl<'de> de::Deserialize<'de> for BlobUrl {
    fn deserialize<D>(de: D) -> std::result::Result<BlobUrl, D::Error>
    where
        D: de::Deserializer<'de>,
    {
        de.deserialize_any(BlobUrlVisitor)
    }
}

struct BlobUrlVisitor;

impl<'de> de::Visitor<'de> for BlobUrlVisitor {
    type Value = BlobUrl;

    fn expecting(&self, f: &mut fmt::Formatter) -> fmt::Result {
        write!(f, "a valid blob storage URL")
    }

    fn visit_str<E>(self, s: &str) -> Result<Self::Value, E>
    where
        E: de::Error,
    {
        BlobUrl::parse(s).map_err(de::Error::custom)
    }
}

#[cfg(test)]
mod tests {
    use reqwest::Url;

    use super::*;

    fn into_urls(urls: &[&str]) -> Vec<Url> {
        urls.iter().map(|s| Url::parse(s).unwrap()).collect()
    }

    // URLs that cannot be either container or blob URLs.
    fn invalid_blob_storage_urls() -> Vec<Url> {
        into_urls(&[
            // Not valid HTTPS URLs.
            "data:text/plain,hello",
            // Valid HTTP URLs, but invalid as storage URLs.
            "https://127.0.0.1",
            "https://localhost",
            "https://contoso.com",
        ])
    }

    fn valid_container_urls() -> Vec<Url> {
        into_urls(&[
            "https://myaccount.blob.core.windows.net/mycontainer",
            "https://myaccount.blob.core.windows.net/mycontainer?x=1&y=2",
        ])
    }

    fn invalid_container_urls() -> Vec<Url> {
        let mut urls = invalid_blob_storage_urls();

        // No blob URL is a container URL.
        urls.extend(valid_blob_urls());

        urls
    }

    fn invalid_blob_urls() -> Vec<Url> {
        let mut urls = invalid_blob_storage_urls();

        // No container URL is a blob URL.
        urls.extend(valid_container_urls());

        urls
    }

    fn valid_blob_urls() -> Vec<Url> {
        into_urls(&[
            "https://myaccount.blob.core.windows.net/mycontainer/myblob",
            "https://myaccount.blob.core.windows.net/mycontainer/myblob?x=1&y=2",
        ])
    }

    #[test]
    fn test_blob_container_url() {
        for url in invalid_container_urls() {
            assert!(BlobContainerUrl::new(url).is_err());
        }

        for url in valid_container_urls() {
            let url = BlobContainerUrl::new(url).expect("invalid blob container URL");
            assert_eq!(url.account(), Some("myaccount".into()));
            assert_eq!(url.container(), Some("mycontainer".into()));
        }
    }

    #[test]
    fn test_blob_url() {
        for url in invalid_blob_urls() {
            println!("{:?}", url);
            assert!(BlobUrl::new(url).is_err());
        }

        for url in valid_blob_urls() {
            let url = BlobUrl::new(url).expect("invalid blob URL");

            assert_eq!(url.account(), Some("myaccount".into()));
            assert_eq!(url.container(), Some("mycontainer".into()));
            assert_eq!(url.name(), "myblob");
        }
    }

    #[test]
    fn test_blob_url_simulated_dir() {
        let url = "https://myaccount.blob.core.windows.net/mycontainer/mydir/myblob";
        let url = into_urls(&[url]).remove(0);

        let url = BlobUrl::new(url).expect("invalid blob URL");

        assert_eq!(url.account(), Some("myaccount".into()));
        assert_eq!(url.container(), Some("mycontainer".into()));
        assert_eq!(url.name(), "mydir/myblob");
    }

    #[test]
    fn test_blob_container_url_parse() {
        let container_url = "https://myaccount.blob.core.windows.net/mycontainer";
        let blob_url = "https://myaccount.blob.core.windows.net/mycontainer/myblob";

        assert!(BlobContainerUrl::parse(container_url).is_ok());
        assert!(BlobContainerUrl::parse(blob_url).is_err());
    }

    #[test]
    fn test_blob_url_parse() {
        let container_url = "https://myaccount.blob.core.windows.net/mycontainer";
        let blob_url = "https://myaccount.blob.core.windows.net/mycontainer/myblob";

        assert!(BlobUrl::parse(container_url).is_err());
        assert!(BlobUrl::parse(blob_url).is_ok());
    }

    #[test]
    fn test_blob_url_display_redacted() {
        let url_no_query = "https://myaccount.blob.core.windows.net/mycontainer/myblob";
        let url_sas_query = "https://myaccount.blob.core.windows.net/mycontainer/myblob?x=public1&sig=secret&y=public2";

        assert_eq!(
            url_no_query,
            &format!("{:?}", BlobUrl::parse(url_no_query).unwrap())
        );

        let redacted = "https://myaccount.blob.core.windows.net/mycontainer/myblob?x=public1&sig=REDACTED&y=public2".to_owned();
        assert_eq!(
            redacted,
            format!("{:?}", BlobUrl::parse(url_sas_query).unwrap())
        );
    }

    #[test]
    fn test_blob_container_url_display_redacted() {
        let url_no_query = "https://myaccount.blob.core.windows.net/mycontainer";
        let url_sas_query =
            "https://myaccount.blob.core.windows.net/mycontainer?x=public1&sig=secret&y=public2";

        assert_eq!(
            url_no_query,
            &format!("{:?}", BlobContainerUrl::parse(url_no_query).unwrap())
        );

        let redacted =
            "https://myaccount.blob.core.windows.net/mycontainer?x=public1&sig=REDACTED&y=public2"
                .to_owned();
        assert_eq!(
            redacted,
            format!("{:?}", BlobContainerUrl::parse(url_sas_query).unwrap())
        );
    }
}
