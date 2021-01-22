// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use std::path::{Path, PathBuf};

use anyhow::Result;
use async_trait::async_trait;
use reqwest::Url;
use storage_queue::{Message, QueueClient};

#[async_trait]
pub trait Queue<M> {
    async fn pop(&mut self) -> Result<Option<M>>;

    async fn delete(&mut self, msg: M) -> Result<()>;
}

pub trait Parser<M> {
    fn parse(&mut self, msg: &M) -> Result<Url>;
}

#[async_trait]
pub trait Downloader {
    async fn download(&mut self, url: Url, dir: &Path) -> Result<PathBuf>;
}

#[async_trait]
pub trait Processor {
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
    pub fn new(queue_url: Url, processor: P) -> Self {
        let queue = QueueClient::new(queue_url);
        Self { queue, processor }
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
        self.queue.delete(msg).await
    }
}

impl<P> Parser<Message> for CallbackImpl<P>
where
    P: Processor + Send,
{
    fn parse(&mut self, msg: &Message) -> Result<Url> {
        let text = std::str::from_utf8(msg.data())?;
        let url = Url::parse(text)?;

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
