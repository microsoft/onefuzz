#[macro_use]
extern crate serde;

use anyhow::Result;
use chrono::{DateTime, Duration, Utc};
use std::collections::{HashMap, HashSet};
use std::fs::File;
use std::io::{self, BufRead, BufReader};

#[derive(Debug, Deserialize, Serialize)]
#[serde(rename_all = "camelCase")]
struct Event {
    name: String,
    count: u64,
}

#[derive(Debug, Deserialize, Serialize)]
#[serde(rename_all = "camelCase")]
struct Device {
    os_version: Option<String>,
    role_instance: Option<String>,
}

#[derive(Debug, Deserialize, Serialize)]
#[serde(rename_all = "camelCase")]
struct Data {
    event_time: DateTime<Utc>,
}

#[derive(Debug, Deserialize, Serialize)]
#[serde(rename_all = "camelCase")]
struct Custom {
    dimensions: Vec<HashMap<String, String>>,
}

#[derive(Debug, Deserialize, Serialize)]
struct Context {
    device: Device,
    custom: Custom,
    data: Data,
}

#[derive(Debug, Deserialize, Serialize)]
struct Entry {
    event: Vec<Event>,
    context: Context,
}

fn read_file(filename: &str) -> Result<Vec<Entry>> {
    let mut results = vec![];
    let file = File::open(filename)?;
    let reader = BufReader::new(file);
    for line in reader.lines() {
        let line = line?;
        let entry: Entry = serde_json::from_str(&line)?;
        results.push(entry);
    }

    Ok(results)
}

fn get_files() -> Result<Vec<String>> {
    let mut files = vec![];

    let stdin = io::stdin();
    for line in stdin.lock().lines() {
        let line = line?;
        files.push(line);
    }
    Ok(files)
}

#[derive(Debug, Deserialize, Serialize)]
struct Seen {
    first: DateTime<Utc>,
    last: DateTime<Utc>,
}

#[derive(Debug, Deserialize, Serialize, Default)]
struct Stats {
    tasks: HashSet<String>,
    machines: HashMap<String, Seen>,
    instances: HashSet<String>,
    jobs: HashSet<String>,
    events: HashMap<String, u64>,
    tools: HashMap<String, u64>,
}

fn main() -> Result<()> {
    let files = get_files()?;
    let mut stats = Stats::default();

    for entry in files.iter().filter_map(|s| read_file(s).ok()).flatten() {
        for dimension in entry.context.custom.dimensions {
            if let Some(x) = dimension.get("task_id") {
                stats.tasks.insert(x.to_owned());
            } else if let Some(x) = dimension.get("job_id") {
                stats.jobs.insert(x.to_owned());
            } else if let Some(x) = dimension.get("instance_id") {
                stats.instances.insert(x.to_owned());
            } else if let Some(x) = dimension.get("machine_id") {
                let event_time = entry.context.data.event_time;
                if let Some(x) = stats.machines.get_mut(x) {
                    if x.first > event_time {
                        x.first = event_time.to_owned();
                    }
                    if x.last < event_time {
                        x.last = event_time.to_owned();
                    }
                } else {
                    stats.machines.insert(
                        x.to_owned(),
                        Seen {
                            first: event_time.to_owned(),
                            last: event_time.to_owned(),
                        },
                    );
                }
            } else if let Some(x) = dimension.get("tool_name") {
                if let Some(x) = stats.tools.get_mut(x) {
                    *x += 1;
                } else {
                    stats.tools.insert(x.to_owned(), 1);
                }
            }
        }
        for event in entry.event {
            if let Some(x) = stats.events.get_mut(&event.name) {
                *x += event.count;
            } else {
                stats.events.insert(event.name, event.count);
            }
        }
    }

    let mut compute_used: Duration = Duration::zero();

    for entry in stats.machines.values() {
        let seen = entry.last - entry.first;
        compute_used = compute_used + seen;
    }

    println!("jobs: {}", stats.jobs.len());
    println!("tasks: {}", stats.tasks.len());
    println!("instances: {}", stats.instances.len());
    println!("machines: {}", stats.machines.len());
    println!("events: {:#?}", stats.events);
    println!("tools: {:#?}", stats.tools);
    println!("compute used: {:?}", compute_used);

    Ok(())
}
