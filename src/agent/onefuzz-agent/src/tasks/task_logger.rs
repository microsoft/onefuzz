#![allow(clippy::if_same_then_else)]
#![allow(dead_code)]

use anyhow::{anyhow, Result};
use async_trait::async_trait;
use azure_core::HttpError;
use azure_storage::core::prelude::*;
use azure_storage_blobs::prelude::*;
use futures::{StreamExt, TryStreamExt};
use onefuzz_telemetry::LogEvent;
use reqwest::{StatusCode, Url};
use std::{path::PathBuf, sync::Arc, time::Duration};
use uuid::Uuid;

use tokio::sync::broadcast::Receiver;

const LOGS_BUFFER_SIZE: usize = 100;
const MAX_LOG_SIZE: u64 = 100000000; // 100 MB
const DEFAULT_LOGGING_PERIOD: Duration = Duration::from_secs(60);
const DEFAULT_POLLING_PERIOD: Duration = Duration::from_secs(5);

#[derive(Debug, Deserialize)]
#[serde(rename_all = "PascalCase")]
#[serde(rename = "Error")]
struct RequestError {
    code: String,
    message: String,
}

enum WriteLogResponse {
    Success,
    MessageTooLarge,
    MaxSizeReached,
}

/// Abstracts the operation needed to write logs
#[async_trait]
trait LogWriter<T>: Send + Sync {
    async fn write_logs(&self, logs: &[LogEvent]) -> Result<WriteLogResponse>;
    async fn get_next_writer(&self) -> Result<Box<dyn LogWriter<T>>>;
}

pub struct BlobLogWriter {
    container_client: Arc<ContainerClient>,
    job_id: Uuid,
    task_id: Uuid,
    machine_id: Uuid,
    blob_id: usize,
    max_log_size: u64,
}

impl BlobLogWriter {
    fn get_blob_name(&self) -> String {
        format!(
            "{}/{}/{}",
            self.task_id, self.machine_id, self.blob_id
        )
    }

    pub async fn create(
        job_id: Uuid,
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
            job_id,
            task_id,
            machine_id,
            blob_id,
            max_log_size,
        })
    }
}

#[async_trait]
impl LogWriter<BlobLogWriter> for BlobLogWriter {
    async fn write_logs(&self, logs: &[LogEvent]) -> Result<WriteLogResponse> {
        let blob_name = self.get_blob_name();
        let blob_client = self.container_client.as_blob_client(blob_name);
        let data_stream = logs
            .iter()
            .flat_map(|log_event| match log_event {
                LogEvent::Event((ev, data)) => format!(
                    "{}: {}\n",
                    ev.as_str(),
                    data.iter()
                        .map(|p| p.as_values())
                        .map(|(name, val)| format!("{} {}", name, val))
                        .collect::<Vec<_>>()
                        .join(", ")
                )
                .into_bytes(),
                LogEvent::Trace((level, msg)) => {
                    format!("{}: {}\n", level.as_str(), msg).into_bytes()
                }
            })
            .collect::<Vec<_>>();

        let result = blob_client
            .append_block(data_stream)
            .condition_max_size(self.max_log_size)
            .execute()
            .await
            .map_err(|e| anyhow!(e.to_string()));

        match result {
            Ok(_r) => Ok(WriteLogResponse::Success),
            Err(e) => {
                let zz = Arc::new(e.source());
                match zz.map(|e| e.downcast_ref::<HttpError>().unwrap()) {
                    Some(HttpError::StatusCode { status: s, body: b }) => {
                        // StatusCode::PRECONDITION_FAILED
                        // StatusCode::CONFLICT
                        if let Ok(RequestError { code, message: _ }) = serde_xml_rs::from_str(b) {
                            if s == &StatusCode::PRECONDITION_FAILED
                                && code == "MaxBlobSizeConditionNotMet"
                            {
                                return Ok(WriteLogResponse::MaxSizeReached);
                            } else if s == &StatusCode::CONFLICT && code == "BlockCountExceedsLimit"
                            {
                                return Ok(WriteLogResponse::MaxSizeReached);
                            } else if s == &StatusCode::PAYLOAD_TOO_LARGE {
                                return Ok(WriteLogResponse::MessageTooLarge);
                            } else {
                                return Err(e);
                            }
                        } else {
                            return Err(e);
                        }

                        // The log is too large, so we need to split it up
                    }
                    _ => return Err(e),
                }
            }
        }
    }
    async fn get_next_writer(&self) -> Result<Box<dyn LogWriter<BlobLogWriter>>> {
        let new_writer = Self {
            blob_id: self.blob_id + 1,
            container_client: self.container_client.clone(),
            job_id: self.job_id,
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

#[derive(Debug, Clone)]
pub struct TaskLogger {
    job_id: Uuid,
    task_id: Uuid,
    machine_id: Uuid,
    logging_period: Duration,
    log_buffer_size: usize,
    // max_log_size: u64,
    polling_period: Duration,
}

enum LoopState {
    Receive,
    InitLog,
    Send { start: usize, count: usize },
}

struct LoopContext<T: Sized> {
    pub log_writer: Box<dyn LogWriter<T>>,
    pub pending_logs: Vec<LogEvent>,
    pub state: LoopState,
    pub event: Receiver<LogEvent>,
}

impl TaskLogger {
    pub fn new(job_id: Uuid, task_id: Uuid, machine_id: Uuid) -> Self {
        Self {
            job_id,
            task_id,
            machine_id,
            logging_period: DEFAULT_LOGGING_PERIOD,
            log_buffer_size: LOGS_BUFFER_SIZE,
            polling_period: DEFAULT_POLLING_PERIOD,
        }
    }

    fn create_container_client(log_container: &Url) -> Result<Arc<ContainerClient>> {
        let account = log_container
            .domain()
            .unwrap()
            .split('.')
            .next()
            .ok_or(anyhow!("Invalid log container"))?
            .to_owned();
        let container = log_container
            .path_segments()
            .unwrap()
            .next()
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
        log_writer: Box<dyn LogWriter<T>>,
        event: Receiver<LogEvent>,
    ) -> Result<()> {
        let initial_state = LoopContext {
            log_writer,
            pending_logs: vec![],
            state: LoopState::Receive,
            event,
        };

        let _loop_result = futures::stream::repeat(123)
            .map(Ok)
            .try_fold(initial_state, |context, _i| async {
                match context.state {
                    LoopState::Send { start, count } => {
                        match context
                            .log_writer
                            .write_logs(&context.pending_logs[start..start + count])
                            .await?
                        {
                            WriteLogResponse::Success => {
                                if start + count >= context.pending_logs.len() {
                                    Result::<_, anyhow::Error>::Ok(LoopContext {
                                        // log_writer: context.log_writer,
                                        pending_logs: vec![],
                                        state: LoopState::Receive,
                                        ..context
                                    })
                                } else {
                                    Result::<_, anyhow::Error>::Ok(LoopContext {
                                        // log_writer: context.log_writer,
                                        pending_logs: vec![],
                                        state: LoopState::Send {
                                            start: start + count,
                                            count: context.pending_logs.len() - start - count,
                                        },
                                        ..context
                                    })
                                }
                            }

                            WriteLogResponse::MaxSizeReached => {
                                Result::<_, anyhow::Error>::Ok(LoopContext {
                                    state: LoopState::InitLog,
                                    ..context
                                })
                            }
                            WriteLogResponse::MessageTooLarge => {
                                // slit the logs here
                                Result::<_, anyhow::Error>::Ok(LoopContext {
                                    state: LoopState::Send {
                                        start,
                                        count: count / 2,
                                    },
                                    ..context
                                })
                            }
                        }
                    }
                    LoopState::InitLog => {
                        let new_writer = context.log_writer.get_next_writer().await?;
                        Result::<_, anyhow::Error>::Ok(LoopContext {
                            log_writer: new_writer,
                            state: if context.pending_logs.is_empty() {
                                LoopState::Receive
                            } else {
                                LoopState::Send {
                                    start: 0,
                                    count: context.pending_logs.len(),
                                }
                            },
                            ..context
                        })
                    }
                    LoopState::Receive => {
                        let mut event = context.event;
                        loop {
                            let mut data = Vec::with_capacity(self.log_buffer_size);
                            let now = tokio::time::Instant::now();
                            loop {
                                if data.len() >= self.log_buffer_size {
                                    break;
                                }

                                if tokio::time::Instant::now() - now > self.logging_period {
                                    break;
                                }

                                if let Ok(v) = event.try_recv() {
                                    data.push(v);
                                } else {
                                    tokio::time::sleep(self.polling_period).await;
                                }
                            }

                            if !data.is_empty() {
                                return Result::<_, anyhow::Error>::Ok(LoopContext {
                                    state: LoopState::Send {
                                        start: 0,
                                        count: data.len(),
                                    },
                                    pending_logs: data,
                                    event,
                                    ..context
                                });
                            }
                            // else {
                            //     Result::<_, anyhow::Error>::Ok(LoopContext {
                            //         log_writer: context.log_writer,
                            //         state: LoopState2::Send{start: 0, count: data.len()},
                            //         pending_logs: context.pending_logs,
                            //     })

                            // }
                        }
                    }
                }
            })
            .await;

        Ok(())
    }

    pub async fn start(&self, event: Receiver<LogEvent>, log_container: Url) -> Result<()> {
        let blob_writer = BlobLogWriter::create(
            self.job_id,
            self.task_id,
            self.machine_id,
            log_container,
            MAX_LOG_SIZE,
        )
        .await?;

        self._start(event, Box::new(blob_writer)).await
    }

    async fn _start<T: 'static + Send>(
        &self,
        event: Receiver<LogEvent>,
        log_writer: Box<dyn LogWriter<T>>,
    ) -> Result<()> {
        self.clone().event_loop(log_writer, event).await?;
        Ok(())
    }
}

#[cfg(test)]
mod tests {
    use std::{collections::HashMap, sync::RwLock};

    use super::*;
    use reqwest::Url;

    #[tokio::test]
    #[ignore]
    async fn test_stream() -> Result<()> {
        let url = std::env::var("test_blob_logger_container")?;
        let log_container = Url::parse(&url)?;
        let client = TaskLogger::create_container_client(&log_container)?;

        let response = client
            .list_blobs()
            .prefix(format!("job1/tak1/1"))
            .execute()
            .await
            .map_err(|e| anyhow!(e.to_string()))?;

        println!("********************");
        println!("blob prefix {:?}", response.blobs.blob_prefix);

        for blob in response.blobs.blobs {
            println!("{}", blob.name);
        }
        println!("********************");

        Ok(())
    }

    #[tokio::test]
    #[ignore]
    async fn test_write_log() -> Result<()> {
        let url = std::env::var("test_blob_logger_container")?;
        let log_container = Url::parse(&url)?;
        let blob_logger = TaskLogger::new(Uuid::new_v4(), Uuid::new_v4(), Uuid::new_v4());

        let (tx, rx) = tokio::sync::broadcast::channel(16);

        tx.send(LogEvent::Trace((log::Level::Info, "test".into())))?;

        blob_logger.start(rx, log_container).await?;
        Ok(())
    }

    pub struct TestLogWriter {
        events: Arc<RwLock<HashMap<usize, Vec<LogEvent>>>>,
        id: usize,
        max_size: usize,
    }

    #[async_trait]
    impl LogWriter<TestLogWriter> for TestLogWriter {
        async fn write_logs(&self, logs: &[LogEvent]) -> Result<WriteLogResponse> {
            println!("***** write_logs");

            let mut events = self.events.write().unwrap();
            let entry = &mut *events.entry(self.id).or_insert(Vec::new());
            if entry.len() >= self.max_size {
                Ok(WriteLogResponse::MaxSizeReached)
            } else {
                for v in logs {
                    println!("***** current id {:?}", self.id);
                    println!("***** pushing value {:?}", v);
                    entry.push(v.clone());
                }
                Ok(WriteLogResponse::Success)
            }
        }
        async fn get_next_writer(&self) -> Result<Box<dyn LogWriter<TestLogWriter>>> {
            //let events = self.events.get_mut().unwrap();
            println!("***** get_next_writer");
            // let events = self.events.read().unwrap();
            Ok(Box::new(Self {
                events: self.events.clone(),
                id: self.id + 1,
                ..*self
            }))
        }
    }

    #[tokio::test]
    #[ignore]
    async fn test() -> Result<()> {
        let events = Arc::new(RwLock::new(HashMap::new()));
        let log_writer = Box::new(TestLogWriter {
            id: 0,
            events: events.clone(),
            max_size: 1,
        });

        // let events = log_writer.events.clone();

        let blob_logger = TaskLogger {
            job_id: Uuid::new_v4(),
            task_id: Uuid::new_v4(),
            machine_id: Uuid::new_v4(),
            logging_period: Duration::from_secs(1),
            log_buffer_size: 1,
            // max_log_size: 1,
            polling_period: Duration::from_secs(1),
        };

        let (tx, rx) = tokio::sync::broadcast::channel(16);
        tx.send(LogEvent::Trace((log::Level::Info, "test1".into())))?;
        tx.send(LogEvent::Trace((log::Level::Info, "test2".into())))?;
        tx.send(LogEvent::Trace((log::Level::Info, "test3".into())))?;
        tx.send(LogEvent::Trace((log::Level::Info, "test4".into())))?;
        tx.send(LogEvent::Trace((log::Level::Info, "test5".into())))?;

        let _res =
            tokio::time::timeout(Duration::from_secs(5), blob_logger._start(rx, log_writer)).await;

        let x = events.read().unwrap();

        for (k, values) in x.iter() {
            println!("{}", k);
            for v in values {
                println!(" {:?}", v);
            }
        }

        assert_eq!(x.keys().len(), 5, "failed ******");
        Ok(())
    }
}
