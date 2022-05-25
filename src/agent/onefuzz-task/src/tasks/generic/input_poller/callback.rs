// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::path::{Path, PathBuf};

use anyhow::Result;
use async_trait::async_trait;
use reqwest::Url;
use storage_queue::Message;
use storage_queue::QueueClient;

#[async_trait]
pub trait Queue<M>: Send {
    async fn pop(&mut self) -> Result<Option<M>>;

    async fn delete(&mut self, msg: M) -> Result<()>;
}

pub trait Parser<M>: Send {
    fn parse(&mut self, msg: &M) -> Result<Url>;
}

#[async_trait]
pub trait Downloader: Send {
    async fn download(&mut self, url: Url, dir: &Path) -> Result<PathBuf>;
}

#[async_trait]
pub trait Processor: Send {
    async fn process(&mut self, url: Option<Url>, input: &Path) -> Result<()>;
}

pub trait Callback<M> {
    fn queue(&mut self) -> &mut dyn Queue<M>;

    fn parser(&mut self) -> &mut dyn Parser<M>;

    fn downloader(&mut self) -> &mut dyn Downloader;

    fn processor(&mut self) -> &mut dyn Processor;
}

pub struct CallbackImpl<P>
where
    P: Processor + Send,
{
    queue: QueueClient,
    pub processor: P,
}

impl<P> Callback<Message> for CallbackImpl<P>
where
    P: Processor + Send,
{
    fn queue(&mut self) -> &mut dyn Queue<Message> {
        self
    }

    fn parser(&mut self) -> &mut dyn Parser<Message> {
        self
    }

    fn downloader(&mut self) -> &mut dyn Downloader {
        self
    }

    fn processor(&mut self) -> &mut dyn Processor {
        &mut self.processor
    }
}

impl<P> CallbackImpl<P>
where
    P: Processor + Send,
{
    pub fn new(queue: QueueClient, processor: P) -> Result<Self> {
        Ok(Self { queue, processor })
    }
}

#[async_trait]
impl<P> Queue<Message> for CallbackImpl<P>
where
    P: Processor + Send,
{
    async fn pop(&mut self) -> Result<Option<Message>> {
        self.queue.pop().await
    }

    async fn delete(&mut self, msg: Message) -> Result<()> {
        msg.delete().await
    }
}

impl<P> Parser<Message> for CallbackImpl<P>
where
    P: Processor + Send,
{
    fn parse(&mut self, msg: &Message) -> Result<Url> {
        let url = msg.parse(|data| {
            let data = std::str::from_utf8(data)?;
            Ok(Url::parse(data)?)
        })?;
        Ok(url)
    }
}

#[async_trait]
impl<P> Downloader for CallbackImpl<P>
where
    P: Processor + Send,
{
    async fn download(&mut self, url: Url, dir: &Path) -> Result<PathBuf> {
        use crate::tasks::utils::download_input;

        let input = download_input(url, dir).await?;

        Ok(input)
    }
}
