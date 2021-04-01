// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use onefuzz::blob::BlobContainerUrl;
use uuid::Uuid;

use crate::coordinator::double::*;
use crate::reboot::double::*;
use crate::scheduler::*;
use crate::setup::double::*;
use crate::work::double::*;
use crate::work::*;
use crate::worker::double::*;
use crate::worker::WorkerEvent;
use onefuzz::process::ExitStatus;

use super::*;

struct Fixture;

impl Fixture {
    pub fn agent(&self) -> Agent {
        let coordinator = Box::new(CoordinatorDouble::default());
        let reboot = Box::new(RebootDouble::default());
        let scheduler = Scheduler::new();
        let setup_runner = Box::new(SetupRunnerDouble::default());
        let work_queue = Box::new(WorkQueueDouble::default());
        let worker_runner = Box::new(WorkerRunnerDouble::default());

        Agent::new(
            coordinator,
            reboot,
            scheduler,
            setup_runner,
            work_queue,
            worker_runner,
            None,
        )
    }

    pub fn job_id(&self) -> Uuid {
        "83267e88-efdd-4b1d-92c0-6b80d01887f8".parse().unwrap()
    }

    pub fn task_id(&self) -> Uuid {
        "eb8ee6b8-6f2d-43b1-aec2-022e9813e86b".parse().unwrap()
    }

    pub fn message(&self) -> Message {
        let work_set = self.work_set();

        Message {
            work_set,
            queue_message: None,
        }
    }

    pub fn work_set(&self) -> WorkSet {
        WorkSet {
            reboot: false,
            setup_url: self.setup_url(),
            script: false,
            work_units: vec![self.work_unit()],
        }
    }

    pub fn setup_url(&self) -> BlobContainerUrl {
        let url = "https://contoso.com/my-setup-container";
        BlobContainerUrl::parse(&url).unwrap()
    }

    pub fn work_unit(&self) -> WorkUnit {
        let config = r#"{ "hello": "world" }"#.to_owned().into();

        WorkUnit {
            job_id: self.job_id(),
            task_id: self.task_id(),
            config,
        }
    }
}

#[tokio::test]
async fn test_update_free_no_work() {
    let mut agent = Fixture.agent();

    let done = agent.update().await.unwrap();
    assert!(!done);

    assert!(matches!(agent.scheduler().unwrap(), Scheduler::Free(..)));

    let double: &WorkQueueDouble = agent.work_queue.downcast_ref().unwrap();
    let claimed_worksets = double
        .claimed
        .iter()
        .map(|cl| cl.work_set.clone())
        .collect::<Vec<WorkSet>>();
    assert_eq!(claimed_worksets, &[]);
}

#[tokio::test]
async fn test_update_free_has_work() {
    let mut agent = Fixture.agent();
    agent
        .work_queue
        .downcast_mut::<WorkQueueDouble>()
        .unwrap()
        .available
        .push(Fixture.message());

    let done = agent.update().await.unwrap();
    assert!(!done);

    assert!(matches!(
        agent.scheduler().unwrap(),
        Scheduler::SettingUp(..)
    ));

    let double: &WorkQueueDouble = agent.work_queue.downcast_ref().unwrap();
    let claimed_worksets = double
        .claimed
        .iter()
        .map(|cl| cl.work_set.clone())
        .collect::<Vec<WorkSet>>();
    assert_eq!(claimed_worksets, &[Fixture.work_set()]);
}

#[tokio::test]
async fn test_emitted_state() {
    let mut agent = Agent {
        worker_runner: Box::new(WorkerRunnerDouble {
            child: ChildDouble {
                exit_status: Some(ExitStatus {
                    code: Some(0),
                    signal: None,
                    success: true,
                }),
                ..ChildDouble::default()
            },
        }),
        ..Fixture.agent()
    };

    agent
        .work_queue
        .downcast_mut::<WorkQueueDouble>()
        .unwrap()
        .available
        .push(Fixture.message());

    for _i in 0..10 {
        if agent.update().await.unwrap() {
            break;
        }
    }

    let expected_events: Vec<NodeEvent> = vec![
        NodeEvent::StateUpdate(StateUpdateEvent::Free),
        NodeEvent::StateUpdate(StateUpdateEvent::SettingUp {
            tasks: vec![Fixture.task_id()],
        }),
        NodeEvent::StateUpdate(StateUpdateEvent::Ready),
        NodeEvent::StateUpdate(StateUpdateEvent::Busy),
        NodeEvent::WorkerEvent(WorkerEvent::Running {
            task_id: Fixture.task_id(),
        }),
        NodeEvent::WorkerEvent(WorkerEvent::Done {
            task_id: Fixture.task_id(),
            exit_status: ExitStatus {
                code: Some(0),
                signal: None,
                success: true,
            },
            stderr: String::default(),
            stdout: String::default(),
        }),
        NodeEvent::StateUpdate(StateUpdateEvent::Done {
            error: None,
            script_output: None,
        }),
    ];
    let coordinator: &CoordinatorDouble = agent.coordinator.downcast_ref().unwrap();
    let events = &coordinator.events;
    assert_eq!(events, &expected_events);
}

#[tokio::test]
async fn test_emitted_state_failed_setup() {
    let error_message = "Failed setup";
    let mut agent = Agent {
        setup_runner: Box::new(SetupRunnerDouble {
            error_message: Some(String::from(error_message)),
            ..SetupRunnerDouble::default()
        }),
        ..Fixture.agent()
    };

    agent
        .work_queue
        .downcast_mut::<WorkQueueDouble>()
        .unwrap()
        .available
        .push(Fixture.message());

    for _i in 0..10 {
        if agent.update().await.unwrap() {
            break;
        }
    }

    let expected_events: Vec<NodeEvent> = vec![
        NodeEvent::StateUpdate(StateUpdateEvent::Free),
        NodeEvent::StateUpdate(StateUpdateEvent::SettingUp {
            tasks: vec![Fixture.task_id()],
        }),
        NodeEvent::StateUpdate(StateUpdateEvent::Done {
            error: Some(String::from(error_message)),
            script_output: None,
        }),
    ];
    let coordinator: &CoordinatorDouble = agent.coordinator.downcast_ref().unwrap();
    let events = &coordinator.events;
    assert_eq!(events, &expected_events);

    // TODO: at some point, the underlying tests should be updated to not write
    // this file in the first place.
    tokio::fs::remove_file(crate::done::done_path().unwrap())
        .await
        .unwrap();
}
