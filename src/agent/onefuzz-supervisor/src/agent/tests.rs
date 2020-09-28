// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use url::Url;
use uuid::Uuid;

use crate::coordinator::double::*;
use crate::reboot::double::*;
use crate::scheduler::*;
use crate::setup::double::*;
use crate::work::double::*;
use crate::work::*;
use crate::worker::double::*;

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
        )
    }

    pub fn job_id(&self) -> Uuid {
        "83267e88-efdd-4b1d-92c0-6b80d01887f8".parse().unwrap()
    }

    pub fn task_id(&self) -> Uuid {
        "eb8ee6b8-6f2d-43b1-aec2-022e9813e86b".parse().unwrap()
    }

    pub fn message(&self) -> Message {
        let receipt = self.receipt();
        let work_set = self.work_set();

        Message { receipt, work_set }
    }

    pub fn receipt(&self) -> Receipt {
        let message_id = "6a0bc779-a1a8-4112-93cd-eb0d77529aa3".parse().unwrap();

        Receipt(storage_queue::Receipt {
            message_id,
            pop_receipt: "abc".into(),
        })
    }

    pub fn work_set(&self) -> WorkSet {
        WorkSet {
            reboot: false,
            setup_url: self.setup_url(),
            script: false,
            work_units: vec![self.work_unit()],
        }
    }

    pub fn setup_url(&self) -> Url {
        "https://contoso.com/my-setup-container".parse().unwrap()
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
    assert_eq!(double.claimed, &[]);
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
    assert_eq!(double.claimed, &[Fixture.receipt()]);
}
