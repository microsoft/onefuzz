#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.
#

""" Launch multiple templates using samples to verify Onefuzz works end-to-end """

# NOTE:
# 1. This script uses an unpacked version of the `integration-test-results`
#    from the CI pipeline.
#
#    Check out https://github.com/microsoft/onefuzz/actions/workflows/
#       ci.yml?query=branch%3Amain+is%3Asuccess
#
# 2. For each stage, this script launches everything for the stage in batch, then
#    checks on each of the created items for the stage.  This batch processing
#    allows testing multiple components concurrently.

import datetime
import logging
import os
import re
import sys
from enum import Enum
from shutil import which
from typing import Dict, List, Optional, Set, Tuple
from uuid import UUID, uuid4

import requests
from onefuzz.api import Command, Onefuzz
from onefuzz.backend import ContainerWrapper, wait
from onefuzz.cli import execute_api
from onefuzztypes.enums import OS, ContainerType, TaskState, VmState
from onefuzztypes.models import Job, Pool, Repro, Scaleset, Task
from onefuzztypes.primitives import Container, Directory, File, PoolName, Region
from pydantic import BaseModel, Field

LINUX_POOL = "linux-test"
WINDOWS_POOL = "linux-test"
BUILD = "0"


class TaskTestState(Enum):
    not_running = "not_running"
    running = "running"
    stopped = "stopped"
    failed = "failed"


class TemplateType(Enum):
    libfuzzer = "libfuzzer"
    libfuzzer_dotnet = "libfuzzer_dotnet"
    libfuzzer_qemu_user = "libfuzzer_qemu_user"
    afl = "afl"
    radamsa = "radamsa"


class Integration(BaseModel):
    template: TemplateType
    os: OS
    target_exe: str
    inputs: Optional[str]
    use_setup: bool = Field(default=False)
    nested_setup_dir: Optional[str]
    wait_for_files: Dict[ContainerType, int]
    check_asan_log: Optional[bool] = Field(default=False)
    disable_check_debugger: Optional[bool] = Field(default=False)
    reboot_after_setup: Optional[bool] = Field(default=False)
    test_repro: Optional[bool] = Field(default=True)
    target_options: Optional[List[str]]


TARGETS: Dict[str, Integration] = {
    "linux-trivial-crash-afl": Integration(
        template=TemplateType.afl,
        os=OS.linux,
        target_exe="fuzz.exe",
        inputs="seeds",
        wait_for_files={ContainerType.unique_reports: 1},
    ),
    "linux-libfuzzer": Integration(
        template=TemplateType.libfuzzer,
        os=OS.linux,
        target_exe="fuzz.exe",
        inputs="seeds",
        wait_for_files={
            ContainerType.unique_reports: 1,
            ContainerType.coverage: 1,
            ContainerType.inputs: 2,
        },
        reboot_after_setup=True,
    ),
    "linux-libfuzzer-with-options": Integration(
        template=TemplateType.libfuzzer,
        os=OS.linux,
        target_exe="fuzz.exe",
        inputs="seeds",
        wait_for_files={
            ContainerType.unique_reports: 1,
            ContainerType.coverage: 1,
            ContainerType.inputs: 2,
        },
        reboot_after_setup=True,
        target_options=["-runs=10000000"],
    ),
    "linux-libfuzzer-dlopen": Integration(
        template=TemplateType.libfuzzer,
        os=OS.linux,
        target_exe="fuzz.exe",
        inputs="seeds",
        wait_for_files={
            ContainerType.unique_reports: 1,
            ContainerType.coverage: 1,
            ContainerType.inputs: 2,
        },
        reboot_after_setup=True,
        use_setup=True,
    ),
    "linux-libfuzzer-linked-library": Integration(
        template=TemplateType.libfuzzer,
        os=OS.linux,
        target_exe="fuzz.exe",
        inputs="seeds",
        wait_for_files={
            ContainerType.unique_reports: 1,
            ContainerType.coverage: 1,
            ContainerType.inputs: 2,
        },
        reboot_after_setup=True,
        use_setup=True,
    ),
    "linux-libfuzzer-dotnet": Integration(
        template=TemplateType.libfuzzer_dotnet,
        os=OS.linux,
        target_exe="wrapper",
        nested_setup_dir="my-fuzzer",
        inputs="inputs",
        use_setup=True,
        wait_for_files={ContainerType.inputs: 2, ContainerType.crashes: 1},
        test_repro=False,
    ),
    "linux-libfuzzer-aarch64-crosscompile": Integration(
        template=TemplateType.libfuzzer_qemu_user,
        os=OS.linux,
        target_exe="fuzz.exe",
        inputs="inputs",
        use_setup=True,
        wait_for_files={ContainerType.inputs: 2, ContainerType.crashes: 1},
        test_repro=False,
    ),
    "linux-libfuzzer-rust": Integration(
        template=TemplateType.libfuzzer,
        os=OS.linux,
        target_exe="fuzz_target_1",
        wait_for_files={ContainerType.unique_reports: 1, ContainerType.coverage: 1},
    ),
    "linux-trivial-crash": Integration(
        template=TemplateType.radamsa,
        os=OS.linux,
        target_exe="fuzz.exe",
        inputs="seeds",
        wait_for_files={ContainerType.unique_reports: 1},
    ),
    "linux-trivial-crash-asan": Integration(
        template=TemplateType.radamsa,
        os=OS.linux,
        target_exe="fuzz.exe",
        inputs="seeds",
        wait_for_files={ContainerType.unique_reports: 1},
        check_asan_log=True,
        disable_check_debugger=True,
    ),
    "windows-libfuzzer": Integration(
        template=TemplateType.libfuzzer,
        os=OS.windows,
        target_exe="fuzz.exe",
        inputs="seeds",
        wait_for_files={
            ContainerType.inputs: 2,
            ContainerType.unique_reports: 1,
            ContainerType.coverage: 1,
        },
    ),
    "windows-libfuzzer-linked-library": Integration(
        template=TemplateType.libfuzzer,
        os=OS.windows,
        target_exe="fuzz.exe",
        inputs="seeds",
        wait_for_files={
            ContainerType.inputs: 2,
            ContainerType.unique_reports: 1,
            ContainerType.coverage: 1,
        },
        use_setup=True,
    ),
    "windows-libfuzzer-load-library": Integration(
        template=TemplateType.libfuzzer,
        os=OS.windows,
        target_exe="fuzz.exe",
        inputs="seeds",
        wait_for_files={
            ContainerType.inputs: 2,
            ContainerType.unique_reports: 1,
            ContainerType.coverage: 1,
        },
        use_setup=True,
    ),
    "windows-trivial-crash": Integration(
        template=TemplateType.radamsa,
        os=OS.windows,
        target_exe="fuzz.exe",
        inputs="seeds",
        wait_for_files={ContainerType.unique_reports: 1},
    ),
}


class TestOnefuzz:
    def __init__(self, onefuzz: Onefuzz, logger: logging.Logger, test_id: UUID) -> None:
        self.of = onefuzz
        self.logger = logger
        self.pools: Dict[OS, Pool] = {}
        self.test_id = test_id
        self.project = f"test-{self.test_id}"
        self.start_log_marker = f"integration-test-injection-error-start-{self.test_id}"
        self.stop_log_marker = f"integration-test-injection-error-stop-{self.test_id}"

    def setup(
        self,
        *,
        region: Optional[Region] = None,
        pool_size: int,
        os_list: List[OS],
    ) -> None:
        self.inject_log(self.start_log_marker)
        for entry in os_list:
            name = PoolName(f"testpool-{entry.name}-{self.test_id}")
            self.logger.info("creating pool: %s:%s", entry.name, name)
            self.pools[entry] = self.of.pools.create(name, entry)
            self.logger.info("creating scaleset for pool: %s", name)
            self.of.scalesets.create(name, pool_size, region=region)

    def launch(
        self, path: Directory, *, os_list: List[OS], targets: List[str], duration=int
    ) -> None:
        """Launch all of the fuzzing templates"""
        for target, config in TARGETS.items():
            if target not in targets:
                continue

            if config.os not in os_list:
                continue

            self.logger.info("launching: %s", target)

            setup = Directory(os.path.join(path, target)) if config.use_setup else None
            target_exe = File(os.path.join(path, target, config.target_exe))
            inputs = (
                Directory(os.path.join(path, target, config.inputs))
                if config.inputs
                else None
            )

            if setup and config.nested_setup_dir:
                setup = Directory(os.path.join(setup, config.nested_setup_dir))

            job: Optional[Job] = None
            if config.template == TemplateType.libfuzzer:
                job = self.of.template.libfuzzer.basic(
                    self.project,
                    target,
                    BUILD,
                    self.pools[config.os].name,
                    target_exe=target_exe,
                    inputs=inputs,
                    setup_dir=setup,
                    duration=duration,
                    vm_count=1,
                    reboot_after_setup=config.reboot_after_setup or False,
                    target_options=config.target_options,
                )
            elif config.template == TemplateType.libfuzzer_dotnet:
                if setup is None:
                    raise Exception("setup required for libfuzzer_dotnet")
                job = self.of.template.libfuzzer.dotnet(
                    self.project,
                    target,
                    BUILD,
                    self.pools[config.os].name,
                    target_harness=config.target_exe,
                    inputs=inputs,
                    setup_dir=setup,
                    duration=duration,
                    vm_count=1,
                    target_options=config.target_options,
                )
            elif config.template == TemplateType.libfuzzer_qemu_user:
                job = self.of.template.libfuzzer.qemu_user(
                    self.project,
                    target,
                    BUILD,
                    self.pools[config.os].name,
                    inputs=inputs,
                    target_exe=target_exe,
                    duration=duration,
                    vm_count=1,
                    target_options=config.target_options,
                )
            elif config.template == TemplateType.radamsa:
                job = self.of.template.radamsa.basic(
                    self.project,
                    target,
                    BUILD,
                    pool_name=self.pools[config.os].name,
                    target_exe=target_exe,
                    inputs=inputs,
                    setup_dir=setup,
                    check_asan_log=config.check_asan_log or False,
                    disable_check_debugger=config.disable_check_debugger or False,
                    duration=duration,
                    vm_count=1,
                )
            elif config.template == TemplateType.afl:
                job = self.of.template.afl.basic(
                    self.project,
                    target,
                    BUILD,
                    pool_name=self.pools[config.os].name,
                    target_exe=target_exe,
                    inputs=inputs,
                    setup_dir=setup,
                    duration=duration,
                    vm_count=1,
                    target_options=config.target_options,
                )
            else:
                raise NotImplementedError

            if not job:
                raise Exception("missing job")

    def check_task(
        self, job: Job, task: Task, scalesets: List[Scaleset]
    ) -> TaskTestState:
        # Check if the scaleset the task is assigned is OK
        for scaleset in scalesets:
            if (
                task.config.pool is not None
                and scaleset.pool_name == task.config.pool.pool_name
                and scaleset.state not in scaleset.state.available()
            ):
                self.logger.error(
                    "task scaleset failed: %s - %s - %s (%s)",
                    job.config.name,
                    task.config.task.type.name,
                    scaleset.state.name,
                    scaleset.error,
                )
                return TaskTestState.failed

        task = self.of.tasks.get(task.task_id)

        # check if the task itself has an error
        if task.error is not None:
            self.logger.error(
                "task failed: %s - %s (%s) - %s",
                job.config.name,
                task.config.task.type.name,
                task.error,
                task.task_id,
            )
            return TaskTestState.failed

        if task.state in [TaskState.stopped, TaskState.stopping]:
            return TaskTestState.stopped

        if task.state == TaskState.running:
            return TaskTestState.running

        return TaskTestState.not_running

    def check_jobs(
        self, poll: bool = False, stop_on_complete_check: bool = False
    ) -> bool:
        """Check all of the integration jobs"""
        jobs: Dict[UUID, Job] = {x.job_id: x for x in self.get_jobs()}
        job_tasks: Dict[UUID, List[Task]] = {}
        check_containers: Dict[UUID, Dict[Container, Tuple[ContainerWrapper, int]]] = {}

        for job in jobs.values():
            if job.config.name not in TARGETS:
                self.logger.error("unknown job target: %s", job.config.name)
                continue

            tasks = self.of.jobs.tasks.list(job.job_id)
            job_tasks[job.job_id] = tasks
            check_containers[job.job_id] = {}
            for task in tasks:
                for container in task.config.containers:
                    if container.type in TARGETS[job.config.name].wait_for_files:
                        count = TARGETS[job.config.name].wait_for_files[container.type]
                        check_containers[job.job_id][container.name] = (
                            ContainerWrapper(
                                self.of.containers.get(container.name).sas_url
                            ),
                            count,
                        )

        self.success = True
        self.logger.info("checking %d jobs", len(jobs))

        self.cleared = False

        def clear() -> None:
            if not self.cleared:
                self.cleared = True
                if poll:
                    print("")

        def check_jobs_impl() -> Tuple[bool, str, bool]:
            self.cleared = False
            failed_jobs: Set[UUID] = set()
            job_task_states: Dict[UUID, Set[TaskTestState]] = {}

            for job_id in check_containers:
                finished_containers: Set[Container] = set()
                for (container_name, container_impl) in check_containers[
                    job_id
                ].items():
                    container_client, count = container_impl
                    if len(container_client.list_blobs()) >= count:
                        clear()
                        self.logger.info(
                            "found files for %s - %s",
                            jobs[job_id].config.name,
                            container_name,
                        )
                        finished_containers.add(container_name)

                for container_name in finished_containers:
                    del check_containers[job_id][container_name]

            scalesets = self.of.scalesets.list()
            for job_id in job_tasks:
                finished_tasks: Set[UUID] = set()
                job_task_states[job_id] = set()

                for task in job_tasks[job_id]:
                    if job_id not in jobs:
                        continue

                    task_result = self.check_task(jobs[job_id], task, scalesets)
                    if task_result == TaskTestState.failed:
                        self.success = False
                        failed_jobs.add(job_id)
                    elif task_result == TaskTestState.stopped:
                        finished_tasks.add(task.task_id)
                    else:
                        job_task_states[job_id].add(task_result)
                job_tasks[job_id] = [
                    x for x in job_tasks[job_id] if x.task_id not in finished_tasks
                ]

            to_remove: Set[UUID] = set()
            for job in jobs.values():
                # stop tracking failed jobs
                if job.job_id in failed_jobs:
                    if job.job_id in check_containers:
                        del check_containers[job.job_id]
                    if job.job_id in job_tasks:
                        del job_tasks[job.job_id]
                    continue

                # stop checking containers once all the containers for the job
                # have checked out.
                if job.job_id in check_containers:
                    if not check_containers[job.job_id]:
                        clear()
                        self.logger.info(
                            "found files in all containers for %s", job.config.name
                        )
                        del check_containers[job.job_id]

                if job.job_id not in check_containers:
                    if job.job_id in job_task_states:
                        if set([TaskTestState.running]).issuperset(
                            job_task_states[job.job_id]
                        ):
                            del job_tasks[job.job_id]

                if job.job_id not in job_tasks and job.job_id not in check_containers:
                    clear()
                    self.logger.info("%s completed", job.config.name)
                    to_remove.add(job.job_id)

            for job_id in to_remove:
                if stop_on_complete_check:
                    self.stop_job(jobs[job_id])
                del jobs[job_id]

            msg = "waiting on: %s" % ",".join(
                sorted(x.config.name for x in jobs.values())
            )
            if poll and len(msg) > 80:
                msg = "waiting on %d jobs" % len(jobs)

            if not jobs:
                msg = "done all tasks"

            return (not bool(jobs), msg, self.success)

        if poll:
            return wait(check_jobs_impl)
        else:
            _, msg, result = check_jobs_impl()
            self.logger.info(msg)
            return result

    def get_job_crash_report(self, job_id: UUID) -> Optional[Tuple[Container, str]]:
        for task in self.of.tasks.list(job_id=job_id, state=None):
            for container in task.config.containers:
                if container.type not in [
                    ContainerType.unique_reports,
                    ContainerType.reports,
                ]:
                    continue

                files = self.of.containers.files.list(container.name)
                if len(files.files) > 0:
                    return (container.name, files.files[0])
        return None

    def launch_repro(self) -> Tuple[bool, Dict[UUID, Tuple[Job, Repro]]]:
        # launch repro for one report from all succeessful jobs
        has_cdb = bool(which("cdb.exe"))
        has_gdb = bool(which("gdb"))

        jobs = self.get_jobs()

        result = True
        repros = {}
        for job in jobs:
            if not TARGETS[job.config.name].test_repro:
                self.logger.info("not testing repro for %s", job.config.name)
                continue

            if TARGETS[job.config.name].os == OS.linux and not has_gdb:
                self.logger.warning(
                    "skipping repro for %s, missing gdb", job.config.name
                )
                continue

            if TARGETS[job.config.name].os == OS.windows and not has_cdb:
                self.logger.warning(
                    "skipping repro for %s, missing cdb", job.config.name
                )
                continue

            report = self.get_job_crash_report(job.job_id)
            if report is None:
                self.logger.error(
                    "target does not include crash reports: %s", job.config.name
                )
                result = False
            else:
                self.logger.info("launching repro: %s", job.config.name)
                (container, path) = report
                repro = self.of.repro.create(container, path, duration=1)
                repros[job.job_id] = (job, repro)

        return (result, repros)

    def check_repro(self, repros: Dict[UUID, Tuple[Job, Repro]]) -> bool:
        self.logger.info("checking repros")
        self.success = True

        def check_repro_impl() -> Tuple[bool, str, bool]:
            # check all of the launched repros

            self.cleared = False

            def clear() -> None:
                if not self.cleared:
                    self.cleared = True
                    print("")

            commands: Dict[OS, Tuple[str, str]] = {
                OS.windows: ("r rip", r"^rip=[a-f0-9]{16}"),
                OS.linux: ("info reg rip", r"^rip\s+0x[a-f0-9]+\s+0x[a-f0-9]+"),
            }

            for (job, repro) in list(repros.values()):
                repros[job.job_id] = (job, self.of.repro.get(repro.vm_id))

            for (job, repro) in list(repros.values()):
                if repro.error:
                    clear()
                    self.logger.error(
                        "repro failed: %s: %s",
                        job.config.name,
                        repro.error,
                    )
                    self.of.repro.delete(repro.vm_id)
                    del repros[job.job_id]
                elif repro.state == VmState.running:
                    try:
                        result = self.of.repro.connect(
                            repro.vm_id,
                            delete_after_use=True,
                            debug_command=commands[repro.os][0],
                        )
                        if result is not None and re.search(
                            commands[repro.os][1], result, re.MULTILINE
                        ):
                            clear()
                            self.logger.info("repro succeeded: %s", job.config.name)
                        else:
                            clear()
                            self.logger.error(
                                "repro failed: %s - %s", job.config.name, result
                            )
                    except Exception as err:
                        clear()
                        self.logger.error("repro failed: %s - %s", job.config.name, err)
                    del repros[job.job_id]
                elif repro.state not in [VmState.init, VmState.extensions_launch]:
                    self.logger.error(
                        "repro failed: %s - bad state: %s", job.config.name, repro.state
                    )
                    del repros[job.job_id]

            repro_states: Dict[str, List[str]] = {}
            for (job, repro) in repros.values():
                if repro.state.name not in repro_states:
                    repro_states[repro.state.name] = []
                repro_states[repro.state.name].append(job.config.name)

            logline = []
            for state in repro_states:
                logline.append("%s:%s" % (state, ",".join(repro_states[state])))

            msg = "waiting repro: %s" % " ".join(logline)
            if len(msg) > 80:
                msg = "waiting on %d repros" % len(repros)
            return (not bool(repros), msg, self.success)

        return wait(check_repro_impl)

    def get_jobs(self) -> List[Job]:
        jobs = self.of.jobs.list(job_state=None)
        jobs = [x for x in jobs if x.config.project == self.project]
        return jobs

    def stop_job(self, job: Job, delete_containers: bool = False) -> None:
        self.of.template.stop(
            job.config.project,
            job.config.name,
            BUILD,
            delete_containers=delete_containers,
        )

    def get_pools(self) -> List[Pool]:
        pools = self.of.pools.list()
        pools = [x for x in pools if x.name == f"testpool-{x.os.name}-{self.test_id}"]
        return pools

    def cleanup(self) -> None:
        """cleanup all of the integration pools & jobs"""

        self.logger.info("cleaning up")
        errors: List[Exception] = []

        jobs = self.get_jobs()
        for job in jobs:
            try:
                self.stop_job(job, delete_containers=True)
            except Exception as e:
                self.logger.error("cleanup of job failed: %s - %s", job, e)
                errors.append(e)

        for pool in self.get_pools():
            self.logger.info(
                "halting: %s:%s:%s", pool.name, pool.os.name, pool.arch.name
            )
            try:
                self.of.pools.shutdown(pool.name, now=True)
            except Exception as e:
                self.logger.error("cleanup of pool failed: %s - %s", pool.name, e)
                errors.append(e)

        container_names = set()
        for job in jobs:
            for task in self.of.tasks.list(job_id=job.job_id, state=None):
                for container in task.config.containers:
                    if container.type in [
                        ContainerType.reports,
                        ContainerType.unique_reports,
                    ]:
                        container_names.add(container.name)

        for repro in self.of.repro.list():
            if repro.config.container in container_names:
                try:
                    self.of.repro.delete(repro.vm_id)
                except Exception as e:
                    self.logger.error("cleanup of repro failed: %s %s", repro.vm_id, e)
                    errors.append(e)

        if errors:
            raise Exception("cleanup failed")

    def inject_log(self, message: str) -> None:
        # This is an *extremely* minimal implementation of the Application Insights rest
        # API, as discussed here:
        #
        # https://apmtips.com/posts/2017-10-27-send-metric-to-application-insights/

        key = self.of.info.get().insights_instrumentation_key
        assert key is not None, "instrumentation key required for integration testing"

        data = {
            "data": {
                "baseData": {
                    "message": message,
                    "severityLevel": "Information",
                    "ver": 2,
                },
                "baseType": "MessageData",
            },
            "iKey": key,
            "name": "Microsoft.ApplicationInsights.Message",
            "time": datetime.datetime.now(datetime.timezone.utc)
            .astimezone()
            .isoformat(),
        }

        requests.post(
            "https://dc.services.visualstudio.com/v2/track", json=data
        ).raise_for_status()

    def check_log_end_marker(
        self,
    ) -> Tuple[bool, str, bool]:
        logs = self.of.debug.logs.keyword(
            self.stop_log_marker, limit=1, timespan="PT1H"
        )
        return (
            len(logs) > 0,
            "waiting for application insight logs to flush",
            True,
        )

    def check_logs_for_errors(self) -> None:
        # only check for errors that exist between the start and stop markers
        # also, only check for the most recent 100 errors within the last 3
        # hours. The records are scanned through in reverse chronological
        # order.

        self.inject_log(self.stop_log_marker)
        wait(self.check_log_end_marker, frequency=5.0)
        self.logger.info("application insights log flushed")

        logs = self.of.debug.logs.keyword("error", limit=100000, timespan="PT3H")

        seen_errors = False
        seen_stop = False
        for entry in logs:
            message = entry.get("message", "")
            if not seen_stop:
                if self.stop_log_marker in message:
                    seen_stop = True
                continue

            if self.start_log_marker in message:
                break

            # ignore logging.info coming from Azure Functions
            if entry.get("customDimensions", {}).get("LogLevel") == "Information":
                continue

            # ignore warnings coming from the rust code, only be concerned
            # about errors
            if (
                entry.get("severityLevel") == 2
                and entry.get("sdkVersion") == "rust:0.1.5"
            ):
                continue

            # ignore resource not found warnings from azure-functions layer,
            # which relate to azure-retry issues
            if (
                message.startswith("Client-Request-ID=")
                and "ResourceNotFound" in message
                and entry.get("sdkVersion", "").startswith("azurefunctions")
            ):
                continue

            # ignore analyzer output, as we can't control what applications
            # being fuzzed send to stdout or stderr. (most importantly, cdb
            # prints "Symbol Loading Error Summary")
            if message.startswith("process (stdout) analyzer:") or message.startswith(
                "process (stderr) analyzer:"
            ):
                continue

            # TODO: ignore queue errors until tasks are shut down before
            # deleting queues https://github.com/microsoft/onefuzz/issues/141
            if (
                "storage queue pop failed" in message
                or "storage queue delete failed" in message
            ) and entry.get("sdkVersion") == "rust:0.1.5":
                continue

            if message is None:
                self.logger.error("error log: %s", entry)
            else:
                self.logger.error("error log: %s", message)
            seen_errors = True

        if seen_errors:
            raise Exception("logs included errors")


class Run(Command):
    def check_jobs(
        self,
        test_id: UUID,
        *,
        endpoint: Optional[str],
        poll: bool = False,
        stop_on_complete_check: bool = False,
    ) -> None:
        self.onefuzz.__setup__(endpoint=endpoint)
        tester = TestOnefuzz(self.onefuzz, self.logger, test_id)
        result = tester.check_jobs(
            poll=poll, stop_on_complete_check=stop_on_complete_check
        )
        if not result:
            raise Exception("jobs failed")

    def check_repros(self, test_id: UUID, *, endpoint: Optional[str]) -> None:
        self.onefuzz.__setup__(endpoint=endpoint)
        tester = TestOnefuzz(self.onefuzz, self.logger, test_id)
        launch_result, repros = tester.launch_repro()
        result = tester.check_repro(repros)
        if not (result and launch_result):
            raise Exception("repros failed")

    def launch(
        self,
        samples: Directory,
        *,
        endpoint: Optional[str] = None,
        pool_size: int = 10,
        region: Optional[Region] = None,
        os_list: List[OS] = [OS.linux, OS.windows],
        targets: List[str] = list(TARGETS.keys()),
        test_id: Optional[UUID] = None,
        duration: int = 1,
    ) -> UUID:
        if test_id is None:
            test_id = uuid4()
        self.logger.info("launching test_id: %s", test_id)

        self.onefuzz.__setup__(endpoint=endpoint)
        tester = TestOnefuzz(self.onefuzz, self.logger, test_id)
        tester.setup(region=region, pool_size=pool_size, os_list=os_list)
        tester.launch(samples, os_list=os_list, targets=targets, duration=duration)
        return test_id

    def cleanup(self, test_id: UUID, *, endpoint: Optional[str]) -> None:
        self.onefuzz.__setup__(endpoint=endpoint)
        tester = TestOnefuzz(self.onefuzz, self.logger, test_id=test_id)
        tester.cleanup()

    def check_logs(self, test_id: UUID, *, endpoint: Optional[str]) -> None:
        self.onefuzz.__setup__(endpoint=endpoint)
        tester = TestOnefuzz(self.onefuzz, self.logger, test_id=test_id)
        tester.check_logs_for_errors()

    def test(
        self,
        samples: Directory,
        *,
        endpoint: Optional[str] = None,
        pool_size: int = 15,
        region: Optional[Region] = None,
        os_list: List[OS] = [OS.linux, OS.windows],
        targets: List[str] = list(TARGETS.keys()),
        skip_repro: bool = False,
        duration: int = 1,
    ) -> None:
        success = True

        test_id = uuid4()
        error: Optional[Exception] = None
        try:
            self.launch(
                samples,
                endpoint=endpoint,
                pool_size=pool_size,
                region=region,
                os_list=os_list,
                targets=targets,
                test_id=test_id,
                duration=duration,
            )
            self.check_jobs(
                test_id, endpoint=endpoint, poll=True, stop_on_complete_check=True
            )

            if skip_repro:
                self.logger.warning("not testing crash repro")
            else:
                self.check_repros(test_id, endpoint=endpoint)

            self.check_logs(test_id, endpoint=endpoint)

        except Exception as e:
            self.logger.error("testing failed: %s", repr(e))
            error = e
            success = False
        except KeyboardInterrupt:
            self.logger.error("interrupted testing")
            success = False

        try:
            self.cleanup(test_id, endpoint=endpoint)
        except Exception as e:
            self.logger.error("testing failed: %s", repr(e))
            error = e
            success = False

        if error:
            try:
                raise error
            except Exception:
                import traceback

                entry = traceback.format_exc()
                for x in entry.split("\n"):
                    self.logger.error("traceback: %s", x)
        if not success:
            sys.exit(1)


def main() -> int:
    return execute_api(
        Run(Onefuzz(), logging.getLogger("integration")), [Command], "0.0.1"
    )


if __name__ == "__main__":
    sys.exit(main())
