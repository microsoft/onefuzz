// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

use crate::work::WorkUnit;
use crate::worker::double::ChildDouble;

use super::*;

struct Fixture;

impl Fixture {
    fn work(&self) -> WorkUnit {
        let job_id = "d4e6cb4a-917e-4826-8a44-7646938c80a8".parse().unwrap();
        let task_id = "1cfcdfe6-df10-42a5-aab7-1a45db0d0e48".parse().unwrap();
        let config = r#"{ "some": "config" }"#.to_owned().into();

        WorkUnit {
            job_id,
            task_id,
            config,
        }
    }

    fn child_running(&self) -> ChildDouble {
        ChildDouble {
            id: 123,
            stderr: "stderr".into(),
            stdout: "stdout".into(),
            ..Default::default()
        }
    }

    fn child_exited(&self, exit_status: ExitStatus) -> ChildDouble {
        let mut child = self.child_running();
        child.exit_status = Some(exit_status);
        child
    }

    fn runner(&self, child: ChildDouble) -> RunnerDouble {
        RunnerDouble { child }
    }

    fn exit_status_ok(&self) -> ExitStatus {
        ExitStatus {
            code: Some(0),
            signal: None,
            success: true,
        }
    }
}

struct RunnerDouble {
    child: ChildDouble,
}

#[async_trait]
impl IWorkerRunner for RunnerDouble {
    async fn run(&mut self, _setup_dir: &Path, _work: &WorkUnit) -> Result<Box<dyn IWorkerChild>> {
        Ok(Box::new(self.child.clone()))
    }
}

#[tokio::test]
async fn test_ready_run() {
    let mut runner = Fixture.runner(Fixture.child_running());
    let state = State {
        ctx: Ready {
            setup_dir: PathBuf::default(),
        },
        work: Fixture.work(),
    };

    let state: State<Running> = state.run(&mut runner).await.unwrap();

    let child = state
        .ctx
        .child
        .downcast_ref::<ChildDouble>()
        .cloned()
        .unwrap();
    assert_eq!(child, Fixture.child_running());
}

#[tokio::test]
async fn test_running_kill() {
    let child = Box::new(Fixture.child_running());
    let mut state = State {
        ctx: Running { child },
        work: Fixture.work(),
    };

    state.kill().unwrap();

    let child = state
        .ctx
        .child
        .downcast_ref::<ChildDouble>()
        .cloned()
        .unwrap();
    assert!(child.killed);
}

#[tokio::test]
async fn test_running_wait_running() {
    let child = Box::new(Fixture.child_running());
    let state = State {
        ctx: Running { child },
        work: Fixture.work(),
    };

    let waited = state.wait().unwrap();

    assert!(matches!(waited, Waited::Running(..)));

    if let Waited::Running(state) = waited {
        let child = state
            .ctx
            .child
            .downcast_ref::<ChildDouble>()
            .cloned()
            .unwrap();
        assert_eq!(child, Fixture.child_running());
    }
}

#[tokio::test]
async fn test_running_wait_done() {
    let exit_status = Fixture.exit_status_ok();
    let child = Box::new(Fixture.child_exited(exit_status));
    let state = State {
        ctx: Running { child },
        work: Fixture.work(),
    };

    let waited = state.wait().unwrap();

    assert!(matches!(waited, Waited::Done(..)));

    if let Waited::Done(done) = waited {
        assert_eq!(done.output().exit_status, exit_status);
    }
}

#[tokio::test]
async fn test_worker_ready_update() {
    let task_id = Fixture.work().task_id;

    let state = State {
        ctx: Ready {
            setup_dir: PathBuf::default(),
        },
        work: Fixture.work(),
    };
    let worker = Worker::Ready(state);
    let mut runner = Fixture.runner(Fixture.child_running());
    let mut events = vec![];
    let worker = worker.update(&mut events, &mut runner).await.unwrap();

    assert!(matches!(worker, Worker::Running(..)));
    assert_eq!(events, vec![WorkerEvent::Running { task_id }]);
}

#[tokio::test]
async fn test_worker_running_update_running() {
    let mut runner = Fixture.runner(Fixture.child_running());
    let child = Box::new(Fixture.child_running());
    let state = State {
        ctx: Running { child },
        work: Fixture.work(),
    };
    let worker = Worker::Running(state);

    let mut events = vec![];
    let worker = worker.update(&mut events, &mut runner).await.unwrap();

    assert!(matches!(worker, Worker::Running(..)));
    assert_eq!(events, vec![]);
}

#[tokio::test]
async fn test_worker_running_update_done() {
    let exit_status = Fixture.exit_status_ok();
    let child = Box::new(Fixture.child_exited(exit_status));
    let state = State {
        ctx: Running { child },
        work: Fixture.work(),
    };
    let worker = Worker::Running(state);
    let mut runner = Fixture.runner(Fixture.child_running());

    let mut events = vec![];
    let worker = worker.update(&mut events, &mut runner).await.unwrap();

    assert!(matches!(worker, Worker::Done(..)));
    assert_eq!(
        events,
        vec![WorkerEvent::Done {
            task_id: Fixture.work().task_id,
            exit_status,
            stderr: "stderr".into(),
            stdout: "stdout".into(),
        }]
    );
}

#[tokio::test]
async fn test_worker_done() {
    // TODO: Child doesn't matter here, fix API.
    let mut runner = Fixture.runner(Fixture.child_running());

    let exit_status = Fixture.exit_status_ok();
    let output = Output {
        exit_status,
        stderr: "stderr".into(),
        stdout: "stdout".into(),
    };
    let state = State {
        ctx: Done { output },
        work: Fixture.work(),
    };
    let worker = Worker::Done(state);

    let mut events = vec![];
    let worker = worker.update(&mut events, &mut runner).await.unwrap();

    assert!(matches!(worker, Worker::Done(..)));
    assert_eq!(events, vec![]);
}

#[cfg(target_family = "unix")]
#[test]
fn test_redirected_child() {
    use std::iter::repeat;
    use std::process::Command;

    // Assume OS pipe capacity of 16 pages, each 4096 bytes.
    //
    // For each stream,
    //
    //   1. Write enough of one char to fill up the OS pipe.
    //   2. Write a smaller count (< tail size) of another char to overflow it.
    //
    // Our tailing buffer has size 4096, so we will expect to see all of the
    // bytes from the second char, and the remainder from the first char.
    let script = "import sys;\
sys.stdout.write('A' * 65536 + 'B' * 4000);\
sys.stderr.write('C' * 65536 + 'D' * 4000)";

    let mut cmd = Command::new("python3");
    cmd.args(&["-c", script]);

    let mut redirected = RedirectedChild::spawn(cmd).unwrap();
    redirected.child.wait().unwrap();
    let captured = redirected.streams.unwrap().join().unwrap();

    let stdout: String = repeat('A').take(96).chain(repeat('B').take(4000)).collect();
    assert_eq!(captured.stdout, stdout);

    let stderr: String = repeat('C').take(96).chain(repeat('D').take(4000)).collect();
    assert_eq!(captured.stderr, stderr);
}

#[cfg(target_family = "windows")]
#[test]
fn test_redirected_child() {
    use std::iter::repeat;
    use std::process::Command;

    // Only write to stdout.
    let script = "Write-Output ('A' * 65536  + 'B' * 4000)";

    let mut cmd = Command::new("powershell.exe");
    cmd.args(&[
        "-NonInteractive",
        "-ExecutionPolicy",
        "Unrestricted",
        "-Command",
        script,
    ]);

    let mut redirected = RedirectedChild::spawn(cmd).unwrap();
    redirected.child.wait().unwrap();
    let captured = redirected.streams.unwrap().join().unwrap();

    let mut stdout: String = repeat('A').take(94).chain(repeat('B').take(4000)).collect();
    stdout.push_str("\r\n");
    assert_eq!(captured.stdout, stdout);

    assert_eq!(captured.stderr, "");
}
