#![allow(clippy::if_same_then_else)]
#![allow(dead_code)]

use anyhow::{anyhow, Result};
use async_trait::async_trait;
use azure_core::HttpError;
use azure_storage::core::prelude::*;
use azure_storage_blobs::prelude::*;
use onefuzz_telemetry::LoggingEvent;
use reqwest::{StatusCode, Url};
use std::{path::PathBuf, sync::Arc, time::Duration};
use uuid::Uuid;

use tokio::sync::broadcast::Receiver;

const LOGS_BUFFER_SIZE: usize = 100;
const MAX_LOG_SIZE: u64 = 100000000; // 100 MB
const DEFAULT_LOGGING_INTERVAL: Duration = Duration::from_secs(60);
const DEFAULT_POLLING_INTERVAL: Duration = Duration::from_secs(5);

#[derive(Debug, Deserialize)]
#[serde(rename_all = "PascalCase")]
#[serde(rename = "Error")]
struct RequestError {
    code: String,
    message: String,
}

#[derive(PartialEq, Debug)]
enum WriteLogResponse {
    Success,
    /// The message needs to be split into multiple parts.
    MessageTooLarge,
    /// the log file is full we need a new file
    MaxSizeReached,
}

/// Abstracts the operation needed to write logs
#[async_trait]
trait LogWriter<T>: Send + Sync {
    async fn write_logs(&self, logs: &[LoggingEvent]) -> Result<WriteLogResponse>;
    /// creates a new blob file and returns the logWriter associated with it
    async fn get_next_writer(&self) -> Result<Box<dyn LogWriter<T>>>;
}

/// Writes logs on azure blobs
pub struct BlobLogWriter {
    container_client: Arc<ContainerClient>,
    task_id: Uuid,
    machine_id: Uuid,
    blob_id: usize,
    max_log_size: u64,
}

impl BlobLogWriter {
    fn get_blob_name(&self) -> String {
        format!("{}/{}/{}.log", self.task_id, self.machine_id, self.blob_id)
    }

    pub async fn create(
        task_id: Uuid,
        machine_id: Uuid,
        log_container: Url,
        max_log_size: u64,
    ) -> Result<Self> {
        let container_client = TaskLogger::create_container_client(&log_container)?;
        let prefix = format!("{}/{}", task_id, machine_id);
        let blob_list = container_client
            .list_blobs()
            .prefix(prefix.as_str())
            .execute()
            .await
            .map_err(|e| anyhow!(e.to_string()))?;
        let mut blob_ids = blob_list
            .blobs
            .blobs
            .iter()
            .filter_map(|b| {
                b.name
                    .strip_prefix(&prefix)
                    .map(PathBuf::from)
                    .filter(|file_name| {
                        file_name.extension().and_then(|f| f.to_str()) == Some("log")
                    })
                    .map(|file_name| file_name.with_extension(""))
                    .and_then(|file_name| {
                        file_name
                            .with_extension("")
                            .to_str()
                            .and_then(|f| f.parse::<usize>().ok())
                    })
            })
            .collect::<Vec<_>>();
        blob_ids.sort_unstable();

        let blob_id = match blob_ids.into_iter().last() {
            Some(id) => id,
            None => {
                let blob_client = container_client.as_blob_client(format!("{}/1.log", prefix));
                blob_client
                    .put_append_blob()
                    .execute()
                    .await
                    .map_err(|e| anyhow!(e.to_string()))?;
                1
            }
        };

        Ok(Self {
            container_client,
            task_id,
            machine_id,
            blob_id,
            max_log_size,
        })
    }
}

#[async_trait]
impl LogWriter<BlobLogWriter> for BlobLogWriter {
    async fn write_logs(&self, logs: &[LoggingEvent]) -> Result<WriteLogResponse> {
        let blob_name = self.get_blob_name();
        let blob_client = self.container_client.as_blob_client(blob_name);
        let data_stream = logs
            .iter()
            .flat_map(|log_event| match log_event {
                // LoggingEvent::Flush => format!("End of log").into_bytes(),
                LoggingEvent::Event(log_event) => format!(
                    "[{}] {}: {}\n",
                    log_event.timestamp,
                    log_event.event.as_str(),
                    log_event
                        .data
                        .iter()
                        .map(|p| p.as_values())
                        .map(|(name, val)| format!("{} {}", name, val))
                        .collect::<Vec<_>>()
                        .join(", ")
                )
                .into_bytes(),
                LoggingEvent::Trace(log_trace) => format!(
                    "[{}] {}: {}\n",
                    log_trace.timestamp,
                    log_trace.level.as_str(),
                    log_trace.message
                )
                .into_bytes(),
            })
            .collect::<Vec<_>>();

        let result = blob_client
            .append_block(data_stream)
            .condition_max_size(self.max_log_size)
            .execute()
            .await;

        match result {
            Ok(_r) => Ok(WriteLogResponse::Success),
            Err(e) => match e.downcast_ref::<HttpError>() {
                Some(HttpError::StatusCode { status: s, body: b }) => {
                    if s == &StatusCode::PRECONDITION_FAILED
                        && b.contains("MaxBlobSizeConditionNotMet")
                    {
                        Ok(WriteLogResponse::MaxSizeReached)
                    } else if s == &StatusCode::CONFLICT && b.contains("BlockCountExceedsLimit") {
                        Ok(WriteLogResponse::MaxSizeReached)
                    } else if s == &StatusCode::PAYLOAD_TOO_LARGE {
                        Ok(WriteLogResponse::MessageTooLarge)
                    } else {
                        Err(anyhow!(e.to_string()))
                    }
                }
                _ => Err(anyhow!(e.to_string())),
            },
        }
    }
    async fn get_next_writer(&self) -> Result<Box<dyn LogWriter<BlobLogWriter>>> {
        let new_writer = Self {
            blob_id: self.blob_id + 1,
            container_client: self.container_client.clone(),
            task_id: self.task_id,
            machine_id: self.machine_id,
            max_log_size: self.max_log_size,
        };

        let blob_client = self
            .container_client
            .as_blob_client(new_writer.get_blob_name());
        blob_client
            .put_append_blob()
            .execute()
            .await
            .map_err(|e| anyhow!(e.to_string()))?;

        Ok(Box::new(new_writer))
    }
}

#[derive(Debug, Clone, Copy)]
pub struct TaskLogger {
    job_id: Uuid,
    task_id: Uuid,
    machine_id: Uuid,
    logging_interval: Duration,
    log_buffer_size: usize,
    polling_interval: Duration,
}

enum LoopState {
    Receive,
    InitLog {
        start: usize,
        count: usize,
        flush: bool,
    },
    Send {
        start: usize,
        count: usize,
        flush: bool,
    },
    Done,
}

struct LoopContext<T: Sized> {
    pub log_writer: Box<dyn LogWriter<T>>,
    pub pending_logs: Vec<LoggingEvent>,
    pub state: LoopState,
    pub event: Receiver<LoggingEvent>,
}

impl TaskLogger {
    pub fn new(job_id: Uuid, task_id: Uuid, machine_id: Uuid) -> Self {
        Self {
            job_id,
            task_id,
            machine_id,
            logging_interval: DEFAULT_LOGGING_INTERVAL,
            log_buffer_size: LOGS_BUFFER_SIZE,
            polling_interval: DEFAULT_POLLING_INTERVAL,
        }
    }

    fn create_container_client(log_container: &Url) -> Result<Arc<ContainerClient>> {
        let account = log_container
            .domain()
            .and_then(|d| d.split('.').next())
            .ok_or(anyhow!("Invalid log container"))?
            .to_owned();
        let container = log_container
            .path_segments()
            .and_then(|mut ps| ps.next())
            .ok_or(anyhow!("Invalid log container"))?
            .to_owned();
        let sas_token = log_container
            .query()
            .ok_or(anyhow!("Invalid log container"))?;

        let http_client = azure_core::new_http_client();
        let storage_account_client =
            StorageAccountClient::new_sas_token(http_client, account, sas_token)?;
        Ok(storage_account_client.as_container_client(container))
    }

    async fn event_loop<T: Send + Sized>(
        self,
        context: LoopContext<T>,
        flush_and_close: bool,
    ) -> Result<LoopContext<T>> {
        match context.state {
            LoopState::Send {
                start,
                count,
                flush,
            } => {
                match context
                    .log_writer
                    .write_logs(&context.pending_logs[start..start + count])
                    .await?
                {
                    WriteLogResponse::Success => {
                        if start + count >= context.pending_logs.len() {
                            if flush {
                                bail!("done");
                            } else {
                                Result::<_, anyhow::Error>::Ok(LoopContext {
                                    pending_logs: vec![],
                                    state: LoopState::Receive,
                                    ..context
                                })
                            }
                        } else {
                            let new_start = start + 1;
                            let new_count = context.pending_logs.len() - new_start;
                            Result::<_, anyhow::Error>::Ok(LoopContext {
                                state: LoopState::Send {
                                    start: new_start,
                                    count: new_count,
                                    flush,
                                },
                                ..context
                            })
                        }
                    }

                    WriteLogResponse::MaxSizeReached => {
                        Result::<_, anyhow::Error>::Ok(LoopContext {
                            state: LoopState::InitLog {
                                start,
                                count,
                                flush,
                            },
                            ..context
                        })
                    }
                    WriteLogResponse::MessageTooLarge => {
                        // split the logs here
                        Result::<_, anyhow::Error>::Ok(LoopContext {
                            state: LoopState::Send {
                                start,
                                count: count / 2,
                                flush,
                            },
                            ..context
                        })
                    }
                }
            }
            LoopState::InitLog {
                start,
                count,
                flush,
            } => {
                let new_writer = context.log_writer.get_next_writer().await?;
                Result::<_, anyhow::Error>::Ok(LoopContext {
                    log_writer: new_writer,
                    state: LoopState::Send {
                        start,
                        count,
                        flush,
                    },
                    ..context
                })
            }
            LoopState::Receive => {
                let mut event = context.event;
                let mut data = Vec::with_capacity(self.log_buffer_size);
                let now = tokio::time::Instant::now();

                loop {
                    if data.len() >= self.log_buffer_size {
                        break;
                    }

                    if tokio::time::Instant::now() - now > self.logging_interval {
                        break;
                    }
                    match event.try_recv() {
                        Ok(v) => {
                            data.push(v);
                        }
                        Err(_) => {
                            tokio::time::sleep(self.polling_interval).await;
                        }
                    }
                }

                if !data.is_empty() {
                    Result::<_, anyhow::Error>::Ok(LoopContext {
                        state: LoopState::Send {
                            start: 0,
                            count: data.len(),
                            flush: flush_and_close,
                        },
                        pending_logs: data,
                        event,
                        ..context
                    })
                } else {
                    Result::<_, anyhow::Error>::Ok(LoopContext { event, ..context })
                }
            }
            LoopState::Done => Result::<_, anyhow::Error>::Ok(context),
        }
    }

    pub async fn start(
        &self,
        event: Receiver<LoggingEvent>,
        log_container: Url,
    ) -> Result<SpawnedLogger> {
        let blob_writer =
            BlobLogWriter::create(self.task_id, self.machine_id, log_container, MAX_LOG_SIZE)
                .await?;

        self._start(event, Box::new(blob_writer))
    }

    fn _start<T: 'static + Send>(
        &self,
        event: Receiver<LoggingEvent>,
        log_writer: Box<dyn LogWriter<T>>,
    ) -> Result<SpawnedLogger> {
        let (flush_and_close_sender, mut flush_and_close_receiver) =
            tokio::sync::oneshot::channel::<()>();

        let this = *self;

        let logger_handle = tokio::spawn(async move {
            let initial_state = LoopContext {
                log_writer,
                pending_logs: vec![],
                state: LoopState::Receive,
                event,
            };

            let mut context = initial_state;

            loop {
                let flush_and_close = flush_and_close_receiver
                    .try_recv()
                    .ok()
                    .map(|_| true)
                    .unwrap_or_default();

                context = match this.event_loop(context, flush_and_close).await {
                    Ok(LoopContext {
                        log_writer: _,
                        pending_logs: _,
                        state: LoopState::Done,
                        event: _,
                    }) => break,
                    Ok(c) => c,
                    Err(e) => {
                        error!("{}", e);
                        break;
                    }
                };
            }
            Ok(())
        });

        Ok(SpawnedLogger {
            logger_handle,
            flush_and_close_sender,
        })
    }
}

pub struct SpawnedLogger {
    logger_handle: tokio::task::JoinHandle<Result<()>>,
    flush_and_close_sender: tokio::sync::oneshot::Sender<()>,
}

impl SpawnedLogger {
    pub async fn flush_and_stop(self, timeout: Duration) -> Result<()> {
        let _ = self.flush_and_close_sender.send(());
        let _ = tokio::time::timeout(timeout, self.logger_handle).await;
        Ok(())
    }
}

#[cfg(test)]
mod tests {
    use std::{collections::HashMap, sync::RwLock};

    use super::*;
    use onefuzz_telemetry::LogTrace;
    use reqwest::Url;

    fn create_log_trace(level: log::Level, message: String) -> LogTrace {
        LogTrace {
            timestamp: chrono::Utc::now(),
            level,
            message,
        }
    }

    #[tokio::test]
    #[ignore]
    async fn test_get_blob() -> Result<()> {
        let url = std::env::var("test_blob_logger_container")?;
        let log_container = Url::parse(&url)?;
        let client = TaskLogger::create_container_client(&log_container)?;

        let response = client
            .list_blobs()
            .prefix(format!("job1/tak1/1"))
            .execute()
            .await
            .map_err(|e| anyhow!(e.to_string()))?;

        println!("blob prefix {:?}", response.blobs.blob_prefix);
        for blob in response.blobs.blobs {
            println!("{}", blob.name);
        }
        Ok(())
    }

    #[tokio::test]
    #[ignore]
    async fn test_write_log() -> Result<()> {
        let url = std::env::var("test_blob_logger_container")?;
        let log_container = Url::parse(&url)?;
        let blob_logger = TaskLogger::new(Uuid::new_v4(), Uuid::new_v4(), Uuid::new_v4());

        let (tx, rx) = tokio::sync::broadcast::channel(16);

        tx.send(LoggingEvent::Trace(create_log_trace(
            log::Level::Info,
            "test".into(),
        )))?;

        blob_logger.start(rx, log_container).await?;
        Ok(())
    }

    pub struct TestLogWriter {
        events: Arc<RwLock<HashMap<usize, Vec<LoggingEvent>>>>,
        id: usize,
        max_size: usize,
    }

    #[async_trait]
    impl LogWriter<TestLogWriter> for TestLogWriter {
        async fn write_logs(&self, logs: &[LoggingEvent]) -> Result<WriteLogResponse> {
            let mut events = self.events.write().unwrap();
            let entry = &mut *events.entry(self.id).or_insert(Vec::new());
            if entry.len() >= self.max_size {
                Ok(WriteLogResponse::MaxSizeReached)
            } else if logs.len() > 1 {
                Ok(WriteLogResponse::MessageTooLarge)
            } else {
                for v in logs {
                    entry.push(v.clone());
                }
                Ok(WriteLogResponse::Success)
            }
        }
        async fn get_next_writer(&self) -> Result<Box<dyn LogWriter<TestLogWriter>>> {
            Ok(Box::new(Self {
                events: self.events.clone(),
                id: self.id + 1,
                ..*self
            }))
        }
    }

    #[tokio::test]
    async fn test_task_logger_normal_messages() -> Result<()> {
        let events = Arc::new(RwLock::new(HashMap::new()));
        let log_writer = Box::new(TestLogWriter {
            id: 0,
            events: events.clone(),
            max_size: 1,
        });

        let blob_logger = TaskLogger {
            job_id: Uuid::new_v4(),
            task_id: Uuid::new_v4(),
            machine_id: Uuid::new_v4(),
            logging_interval: Duration::from_secs(1),
            log_buffer_size: 1,
            polling_interval: Duration::from_secs(1),
        };

        let (tx, rx) = tokio::sync::broadcast::channel(16);
        tx.send(LoggingEvent::Trace(create_log_trace(
            log::Level::Info,
            "test1".into(),
        )))?;
        tx.send(LoggingEvent::Trace(create_log_trace(
            log::Level::Info,
            "test2".into(),
        )))?;
        tx.send(LoggingEvent::Trace(create_log_trace(
            log::Level::Info,
            "test3".into(),
        )))?;
        tx.send(LoggingEvent::Trace(create_log_trace(
            log::Level::Info,
            "test4".into(),
        )))?;
        tx.send(LoggingEvent::Trace(create_log_trace(
            log::Level::Info,
            "test5".into(),
        )))?;

        let _res = blob_logger
            ._start(rx, log_writer)?
            .flush_and_stop(Duration::from_secs(5))
            .await;

        let x = events.read().unwrap();

        for (k, values) in x.iter() {
            println!("{}", k);
            for v in values {
                println!(" {:?}", v);
            }
        }

        assert_eq!(x.keys().len(), 5, "expected 5 groups of messages");
        Ok(())
    }

    #[tokio::test]
    async fn test_task_logger_big_messages() -> Result<()> {
        let events = Arc::new(RwLock::new(HashMap::new()));
        let log_writer = Box::new(TestLogWriter {
            id: 0,
            events: events.clone(),
            max_size: 2,
        });

        let blob_logger = TaskLogger {
            job_id: Uuid::new_v4(),
            task_id: Uuid::new_v4(),
            machine_id: Uuid::new_v4(),
            logging_interval: Duration::from_secs(3),
            log_buffer_size: 2,
            polling_interval: Duration::from_secs(1),
        };

        let (tx, rx) = tokio::sync::broadcast::channel(16);
        tx.send(LoggingEvent::Trace(create_log_trace(
            log::Level::Info,
            "test1".into(),
        )))?;
        tx.send(LoggingEvent::Trace(create_log_trace(
            log::Level::Info,
            "test2".into(),
        )))?;
        tx.send(LoggingEvent::Trace(create_log_trace(
            log::Level::Info,
            "test3".into(),
        )))?;
        tx.send(LoggingEvent::Trace(create_log_trace(
            log::Level::Info,
            "test4".into(),
        )))?;
        tx.send(LoggingEvent::Trace(create_log_trace(
            log::Level::Info,
            "test5".into(),
        )))?;

        let _res = blob_logger
            ._start(rx, log_writer)?
            .flush_and_stop(Duration::from_secs(5))
            .await;

        let x = events.read().unwrap();

        for (k, values) in x.iter() {
            println!("{}", k);
            for v in values {
                println!(" {:?}", v);
            }
        }

        assert_eq!(x.keys().len(), 3, "expected 3 groups of messages");
        Ok(())
    }

    #[tokio::test]
    #[ignore]
    async fn test_blob_writer_create() -> Result<()> {
        let url = std::env::var("test_blob_logger_container")?;
        let blob_writer =
            BlobLogWriter::create(Uuid::new_v4(), Uuid::new_v4(), Url::parse(&url)?, 15).await?;

        let blob_prefix = format!("{}/{}", blob_writer.task_id, blob_writer.machine_id);

        print!("blob prefix {}", &blob_prefix);

        let container_client = blob_writer.container_client.clone();

        let blobs = container_client
            .list_blobs()
            .prefix(blob_prefix.clone())
            .execute()
            .await
            .map_err(|e| anyhow!(e.to_string()))?;

        // test initial blob creation
        assert_eq!(blobs.blobs.blobs.len(), 1, "expected exactly one blob");
        assert_eq!(
            blobs.blobs.blobs[0].name,
            format!("{}/1.log", &blob_prefix),
            "Wrong file name"
        );
        println!("logging test event");
        let result = blob_writer
            .write_logs(&[LoggingEvent::Trace(create_log_trace(
                log::Level::Info,
                "test".into(),
            ))])
            .await
            .map_err(|e| anyhow!(e.to_string()))?;

        assert_eq!(result, WriteLogResponse::Success, "expected success");

        // testing that we return MaxSizeReached when the size is exceeded
        let result = blob_writer
            .write_logs(&[LoggingEvent::Trace(create_log_trace(
                log::Level::Info,
                "test".into(),
            ))])
            .await
            .map_err(|e| anyhow!(e.to_string()))?;

        assert_eq!(
            result,
            WriteLogResponse::MaxSizeReached,
            "expected MaxSizeReached"
        );

        // testing the creation of new blob when we call get_next_writer()
        let _blob_writer = blob_writer.get_next_writer().await?;

        let blobs = container_client
            .list_blobs()
            .prefix(blob_prefix.clone())
            .execute()
            .await
            .map_err(|e| anyhow!(e.to_string()))?;

        assert_eq!(blobs.blobs.blobs.len(), 2, "expected exactly 2 blob");
        let blob_names = blobs
            .blobs
            .blobs
            .iter()
            .map(|b| b.name.clone())
            .collect::<Vec<_>>();

        assert!(
            blob_names.contains(&format!("{}/2.log", &blob_prefix)),
            "expected 2.log"
        );

        Ok(())
    }
}
