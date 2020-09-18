// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use anyhow::Result;
use async_trait::async_trait;
use reqwest::Url;
use std::path::Path;
use tempfile::{tempdir, TempDir};

use super::*;

type Msg = u64;

#[derive(Default)]
struct TestQueue {
    pending: Vec<Msg>,
    popped: Vec<Msg>,
    deleted: Vec<Msg>,
}

#[async_trait]
impl Queue<Msg> for TestQueue {
    async fn pop(&mut self) -> Result<Option<Msg>> {
        let msg = self.pending.pop();

        if let Some(msg) = msg {
            self.popped.push(msg);
        }

        Ok(msg)
    }

    async fn delete(&mut self, msg: Msg) -> Result<()> {
        self.deleted.push(msg);

        Ok(())
    }
}

pub struct TestQueueAlwaysFails;

#[async_trait]
impl Queue<Msg> for TestQueueAlwaysFails {
    async fn pop(&mut self) -> Result<Option<Msg>> {
        bail!("simulated `Queue::pop()` failure")
    }

    async fn delete(&mut self, _msg: Msg) -> Result<()> {
        bail!("simulated `Queue::delete()` failure")
    }
}

#[derive(Default)]
struct TestParser {
    urls: Vec<Url>,
}

impl Parser<Msg> for TestParser {
    fn parse(&mut self, msg: &Msg) -> Result<Url> {
        // By returning the `Url` at index `msg`, we witness that `parse()` was
        // called with `msg`, and simulate a valid input.
        let url = self.urls[*msg as usize].clone();

        Ok(url)
    }
}

#[derive(Default)]
struct TestDownloader {
    downloaded: Vec<Url>,
}

#[async_trait]
impl Downloader for TestDownloader {
    async fn download(&mut self, url: Url, dir: &Path) -> Result<PathBuf> {
        let name = url_input_name(&url);
        let dst = dir.join(name);

        self.downloaded.push(url);

        Ok(dst)
    }
}

#[derive(Default)]
struct TestProcessor {
    processed: Vec<(Url, PathBuf)>,
}

#[async_trait]
impl Processor for TestProcessor {
    async fn process(&mut self, url: Url, input: &Path) -> Result<()> {
        self.processed.push((url, input.to_owned()));

        Ok(())
    }
}

fn url_input_name(url: &Url) -> String {
    url.path_segments().unwrap().last().unwrap().to_owned()
}

fn fixture() -> (TempDir, InputPoller<Msg>) {
    let dir = tempdir().unwrap();
    let task = InputPoller::new(dir.path());

    (dir, task)
}

fn url_fixture(msg: Msg) -> Url {
    Url::parse(&format!("https://azure.com/c/{}", msg)).unwrap()
}

fn input_fixture(dir: &Path, msg: Msg) -> PathBuf {
    let name = msg.to_string();
    dir.join(name)
}

#[tokio::test]
async fn test_ready_poll() {
    let (_, mut task) = fixture();

    let msg: Msg = 0;

    let mut queue = TestQueue::default();
    queue.pending = vec![msg];

    task.trigger(Event::Poll(&mut queue)).await.unwrap();

    assert_eq!(task.state(), &State::Polled(Some(msg)));
    assert_eq!(queue.popped, vec![msg]);
}

#[tokio::test]
async fn test_polled_some_parse() {
    let (_, mut task) = fixture();

    let msg: Msg = 0;
    let url = url_fixture(msg);

    task.set_state(State::Polled(Some(msg)));

    let mut parser = TestParser::default();
    parser.urls = vec![url.clone()]; // at index `msg`

    task.trigger(Event::Parse(&mut parser)).await.unwrap();

    assert_eq!(task.state(), &State::Parsed(msg, url));
}

#[tokio::test]
async fn test_polled_none_parse() {
    let (_, mut task) = fixture();

    task.set_state(State::Polled(None));

    let mut parser = TestParser::default();
    parser.urls = vec![];

    task.trigger(Event::Parse(&mut parser)).await.unwrap();

    assert_eq!(task.state(), &State::Ready);
}

#[tokio::test]
async fn test_parsed_download() {
    let (dir, mut task) = fixture();

    let msg: Msg = 0;
    let url = url_fixture(msg);
    let input = input_fixture(dir.path(), msg);

    task.set_state(State::Parsed(msg, url.clone()));

    let mut downloader = TestDownloader::default();

    task.trigger(Event::Download(&mut downloader))
        .await
        .unwrap();

    assert_eq!(task.state(), &State::Downloaded(msg, url.clone(), input));
    assert_eq!(downloader.downloaded, vec![url]);
}

#[tokio::test]
async fn test_downloaded_process() {
    let (dir, mut task) = fixture();

    let msg: Msg = 0;
    let url = url_fixture(msg);
    let input = input_fixture(dir.path(), msg);

    task.set_state(State::Downloaded(msg, url.clone(), input.clone()));

    let mut processor = TestProcessor::default();

    task.trigger(Event::Process(&mut processor)).await.unwrap();

    assert_eq!(task.state(), &State::Processed(msg));
    assert_eq!(processor.processed, vec![(url, input)]);
}

#[tokio::test]
async fn test_processed_finish() {
    let (_, mut task) = fixture();

    let msg: Msg = 0;

    task.set_state(State::Processed(msg));

    let mut queue = TestQueue::default();

    task.trigger(Event::Finish(&mut queue)).await.unwrap();

    assert_eq!(task.state(), &State::Ready);
    assert_eq!(queue.deleted, vec![msg]);
}

#[tokio::test]
async fn test_invalid_trigger() {
    let (_, mut task) = fixture();

    let mut queue = TestQueue::default();

    // Invalid transition: `(Ready, Finish)`.
    let result = task.trigger(Event::Finish(&mut queue)).await;

    assert!(result.is_err());
    assert_eq!(task.state(), &State::Ready);
}

#[tokio::test]
async fn test_valid_trigger_failed_action() {
    let (_, mut task) = fixture();

    let mut queue = TestQueueAlwaysFails;

    // Valid transition, but `queue.popo()` will return `Err`.
    let result = task.trigger(Event::Poll(&mut queue)).await;

    assert!(result.is_err());
    assert_eq!(task.state(), &State::Ready);
}
