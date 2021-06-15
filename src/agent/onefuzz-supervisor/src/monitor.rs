use anyhow::Result;
use onefuzz::system::processes;
use std::time::Duration;
use tokio::time::sleep;

const MONITOR_PROCESS_WAIT: Duration = Duration::from_secs(10);

pub async fn monitor_processes() -> Result<()> {
    loop {
        let mut processes = processes()?;
        processes.sort_by(|x, y| y.memory_kb.cmp(&x.memory_kb));
        // processes.truncate(10);
        for process in processes {
            info!("process info {:?}", process)
        }
        sleep(MONITOR_PROCESS_WAIT).await;
    }
}
