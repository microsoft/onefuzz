use anyhow::Result;
use async_trait::async_trait;
use backoff::{self, future::FutureOperation as _, Error, ExponentialBackoff};
use reqwest::Response;
use std::{io, time::Duration};

const DEFAULT_RETRY_PERIOD: Duration = Duration::from_millis(500);
const MAX_ELAPSED_TIME: Duration = Duration::from_secs(60 * 5);

#[async_trait]
pub trait SendRetry {
    async fn send_retry(
        self,
        retry_period: Duration,
        max_elapsed_time: Duration,
    ) -> Result<Response>;
    async fn send_retry_default(self) -> Result<Response>;
}

#[async_trait]
impl SendRetry for reqwest::RequestBuilder {
    async fn send_retry_default(self) -> Result<Response> {
        self.send_retry(DEFAULT_RETRY_PERIOD, MAX_ELAPSED_TIME)
            .await
    }

    async fn send_retry(
        self,
        retry_period: Duration,
        max_elapsed_time: Duration,
    ) -> Result<Response> {
        let op = || async {
            match self.try_clone() {
                Some(cloned) => {
                    let result = cloned.send().await;
                    Ok(result)
                }
                None => Err(Error::Permanent(io::Error::new(
                    io::ErrorKind::Other,
                    "This request cannot be retried. Make sure the body is not a stream.",
                ))),
            }
        };

        let result = op
            .retry(ExponentialBackoff {
                current_interval: retry_period,
                initial_interval: retry_period,
                max_elapsed_time: Some(max_elapsed_time),
                ..ExponentialBackoff::default()
            })
            .await??;

        Ok(result)
    }
}

#[cfg(test)]
mod test {
    use super::*;
    use std::net::TcpListener;

    // fun server() {
    //     // todo: randomize the port number
    //     // note: might need to restric to linux since windows could require address registration at the os level

    //     let listener = TcpListener::bind("127.0.0.1:7878").unwrap();

    //     for stream in listener.incoming() {
    //         let stream = stream.unwrap();

    //         let mut buffer = [0; 1024];
    //         stream.read(&mut buffer).unwrap();

    //         let get = b"GET / HTTP/1.1\r\n";

    //         if buffer.starts_with(get) {
    //             let contents = fs::read_to_string("hello.html").unwrap();

    //             let response = format!(
    //                 "HTTP/1.1 200 OK\r\nContent-Length: {}\r\n\r\n{}",
    //                 contents.len(),
    //                 contents
    //             );

    //             stream.write(response.as_bytes()).unwrap();
    //             stream.flush().unwrap();
    //         } else {
    //             // some other request
    //         }
    //     }

    // }

    // fn handle_connection(mut stream: TcpStream) {

    // }

    // #[test]
    // #[tokio::test]
    // async fn empty_stack() {

    // }
}
