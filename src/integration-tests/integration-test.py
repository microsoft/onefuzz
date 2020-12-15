#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.
#

""" Launch multiple templates using samples to verify Onefuzz works end-to-end """

# NOTE:
# 1. This script uses pre-built fuzzing samples from the onefuzz-samples project.
#    https://github.com/microsoft/onefuzz-samples/releases/latest
#
# 2. This script will create new pools & managed scalesets during the testing by
#    default.  To use pre-existing pools, specify `--user_pools os=pool_name`
#
# 3. For each stage, this script launches everything for the stage in batch, then
#    checks on each of the created items for the stage.  This batch processing
#    allows testing multiple components concurrently.

import logging
import os
import re
import sys
from enum import Enum
from typing import Dict, List, Optional, Set, Tuple
from uuid import UUID, uuid4

from onefuzz.api import Command, Onefuzz
from onefuzz.backend import ContainerWrapper, wait
from onefuzz.cli import execute_api
from onefuzztypes.enums import OS, ContainerType, TaskState, VmState
from onefuzztypes.models import Job, Pool, Repro, Scaleset
from onefuzztypes.primitives import Directory, File
from pydantic import BaseModel, Field

LINUX_POOL = "linux-test"
WINDOWS_POOL = "linux-test"
BUILD = "0"


class TemplateType(Enum):
    libfuzzer = "libfuzzer"
    afl = "afl"
    radamsa = "radamsa"


class Integration(BaseModel):
    template: TemplateType
    os: OS
    target_exe: str
    inputs: Optional[str]
    use_setup: bool = Field(default=False)
    wait_for_files: List[ContainerType]
    check_asan_log: Optional[bool] = Field(default=False)
    disable_check_debugger: Optional[bool] = Field(default=False)


TARGETS: Dict[str, Integration] = {
    "linux-trivial-crash-afl": Integration(
        template=TemplateType.afl,
        os=OS.linux,
        target_exe="fuzz.exe",
        inputs="seeds",
        wait_for_files=[ContainerType.unique_reports],
    ),
    "linux-libfuzzer": Integration(
        template=TemplateType.libfuzzer,
        os=OS.linux,
        target_exe="fuzz.exe",
        inputs="seeds",
        wait_for_files=[ContainerType.unique_reports, ContainerType.coverage],
    ),
    "linux-libfuzzer-rust": Integration(
        template=TemplateType.libfuzzer,
        os=OS.linux,
        target_exe="fuzz_target_1",
        wait_for_files=[ContainerType.unique_reports, ContainerType.coverage],
    ),
    "linux-trivial-crash": Integration(
        template=TemplateType.radamsa,
        os=OS.linux,
        target_exe="fuzz.exe",
        inputs="seeds",
        wait_for_files=[ContainerType.unique_reports],
    ),
    "linux-trivial-crash-asan": Integration(
        template=TemplateType.radamsa,
        os=OS.linux,
        target_exe="fuzz.exe",
        inputs="seeds",
        wait_for_files=[ContainerType.unique_reports],
        check_asan_log=True,
        disable_check_debugger=True,
    ),
    "windows-libfuzzer": Integration(
        template=TemplateType.libfuzzer,
        os=OS.windows,
        target_exe="fuzz.exe",
        inputs="seeds",
        wait_for_files=[
            ContainerType.unique_reports,
            ContainerType.coverage,
        ],
    ),
    "windows-trivial-crash": Integration(
        template=TemplateType.radamsa,
        os=OS.windows,
        target_exe="fuzz.exe",
        inputs="seeds",
        wait_for_files=[ContainerType.unique_reports],
    ),
}


class TestOnefuzz:
    def __init__(
        self,
        onefuzz: Onefuzz,
        logger: logging.Logger,
        *,
        pool_size: int,
        os_list: List[OS],
        targets: List[str],
        skip_cleanup: bool,
    ) -> None:
        self.of = onefuzz
        self.logger = logger
        self.pools: Dict[OS, Pool] = {}
        self.project = "test-" + str(uuid4()).split("-")[0]
        self.pool_size = pool_size
        self.os = os_list
        self.targets = targets
        self.skip_cleanup = skip_cleanup

        # job_id -> Job
        self.jobs: Dict[UUID, Job] = {}

        # job_id -> List[container_url]
        self.containers: Dict[UUID, List[ContainerWrapper]] = {}

        # task_id -> job_id
        self.tasks: Dict[UUID, UUID] = {}

        self.successful_jobs: Set[UUID] = set()
        self.failed_jobs: Set[UUID] = set()
        self.failed_repro: Set[UUID] = set()

        # job_id -> Repro
        self.repros: Dict[UUID, Repro] = {}

        # job_id -> target
        self.target_jobs: Dict[UUID, str] = {}

    def setup(
        self,
        *,
        region: Optional[str] = None,
        user_pools: Optional[Dict[str, str]] = None,
    ) -> None:
        for entry in self.os:
            if user_pools and entry.name in user_pools:
                self.logger.info(
                    "using existing pool: %s:%s", entry.name, user_pools[entry.name]
                )
                self.pools[entry] = self.of.pools.get(user_pools[entry.name])
            else:
                name = "pool-%s-%s" % (self.project, entry.name)
                self.logger.info("creating pool: %s:%s", entry.name, name)
                self.pools[entry] = self.of.pools.create(name, entry)
                self.logger.info("creating scaleset for pool: %s", name)
                self.of.scalesets.create(name, self.pool_size, region=region)

    def launch(self, path: str) -> None:
        """ Launch all of the fuzzing templates """

        for target, config in TARGETS.items():
            if target not in self.targets:
                continue

            if config.os not in self.os:
                continue

            self.logger.info("launching: %s", target)

            setup = Directory(os.path.join(path, target)) if config.use_setup else None
            target_exe = File(os.path.join(path, target, config.target_exe))
            inputs = (
                Directory(os.path.join(path, target, config.inputs))
                if config.inputs
                else None
            )

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
                    duration=1,
                    vm_count=1,
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
                    duration=1,
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
                    duration=1,
                    vm_count=1,
                )
            else:
                raise NotImplementedError

            if not job:
                raise Exception("missing job")

            self.containers[job.job_id] = []
            for task in self.of.tasks.list(job_id=job.job_id):
                self.tasks[task.task_id] = job.job_id
                self.containers[job.job_id] += [
                    ContainerWrapper(self.of.containers.get(x.name).sas_url)
                    for x in task.config.containers
                    if x.type in TARGETS[job.config.name].wait_for_files
                ]
            self.jobs[job.job_id] = job
            self.target_jobs[job.job_id] = target

    def check_task(self, task_id: UUID, scalesets: List[Scaleset]) -> Optional[str]:
        task = self.of.tasks.get(task_id)

        # Check if the scaleset the task is assigned is OK
        for scaleset in scalesets:
            if (
                task.config.pool is not None
                and scaleset.pool_name == task.config.pool.pool_name
                and scaleset.state not in scaleset.state.available()
            ):
                return "task scaleset failed: %s - %s - %s (%s)" % (
                    self.jobs[self.tasks[task_id]].config.name,
                    task.config.task.type.name,
                    scaleset.state.name,
                    scaleset.error,
                )

        # check if the task itself has an error
        if task.error is not None:
            return "task failed: %s - %s (%s)" % (
                self.jobs[self.tasks[task_id]].config.name,
                task.config.task.type.name,
                task.error,
            )

        # just in case someone else stopped the task
        if task.state in TaskState.shutting_down():
            return "task shutdown early: %s - %s" % (
                self.jobs[self.tasks[task_id]].config.name,
                task.config.task.type.name,
            )
        return None

    def check_jobs_impl(
        self,
    ) -> Tuple[bool, str, bool]:
        self.cleared = False

        def clear() -> None:
            if not self.cleared:
                self.cleared = True
                print("")

        if self.jobs:
            finished_job: Set[UUID] = set()

            # check all the containers we care about for the job
            for job_id in self.containers:
                done: Set[ContainerWrapper] = set()
                for container in self.containers[job_id]:
                    if len(container.list_blobs()) > 0:
                        clear()
                        self.logger.info(
                            "new files in: %s", container.client.container_name
                        )
                        done.add(container)
                for container in done:
                    self.containers[job_id].remove(container)
                if not self.containers[job_id]:
                    clear()
                    self.logger.info("finished: %s", self.jobs[job_id].config.name)
                    finished_job.add(job_id)

            # check all the tasks associated with the job
            if self.tasks:
                scalesets = self.of.scalesets.list()
                for task_id in self.tasks:
                    error = self.check_task(task_id, scalesets)
                    if error is not None:
                        clear()
                        self.logger.error(error)
                        finished_job.add(self.tasks[task_id])
                        self.failed_jobs.add(self.tasks[task_id])

            # cleanup jobs that are done testing
            for job_id in finished_job:
                self.stop_template(
                    self.jobs[job_id].config.name, delete_containers=False
                )

                for task_id, task_job_id in list(self.tasks.items()):
                    if job_id == task_job_id:
                        del self.tasks[task_id]

                if job_id in self.jobs:
                    self.successful_jobs.add(job_id)
                    del self.jobs[job_id]

                if job_id in self.containers:
                    del self.containers[job_id]

        msg = "waiting on: %s" % ",".join(
            sorted(x.config.name for x in self.jobs.values())
        )
        if len(msg) > 80:
            msg = "waiting on %d jobs" % len(self.jobs)

        return (
            not bool(self.jobs),
            msg,
            not bool(self.failed_jobs),
        )

    def check_jobs(self) -> bool:
        """ Check all of the integration jobs """
        self.logger.info("checking jobs")
        return wait(self.check_jobs_impl)

    def get_job_crash(self, job_id: UUID) -> Optional[Tuple[str, str]]:
        # get the crash container for a given job

        for task in self.of.tasks.list(job_id=job_id, state=None):
            for container in task.config.containers:
                if container.type != ContainerType.unique_reports:
                    continue
                files = self.of.containers.files.list(container.name)
                if len(files.files) > 0:
                    return (container.name, files.files[0])
        return None

    def launch_repro(self) -> None:
        # launch repro for one report from all succeessful jobs
        for job_id in self.successful_jobs:
            self.logger.info("launching repro: %s", self.target_jobs[job_id])
            report = self.get_job_crash(job_id)
            if report is None:
                return
            (container, path) = report
            self.repros[job_id] = self.of.repro.create(container, path, duration=1)

    def check_repro_impl(
        self,
    ) -> Tuple[bool, str, bool]:
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

        info: Dict[str, List[str]] = {}

        done: Set[UUID] = set()
        for job_id, repro in self.repros.items():
            repro = self.of.repro.get(repro.vm_id)
            if repro.error:
                clear()
                self.logger.error(
                    "repro failed: %s: %s", self.target_jobs[job_id], repro.error
                )
                self.failed_jobs.add(job_id)
                done.add(job_id)
            elif repro.state not in [VmState.init, VmState.extensions_launch]:
                done.add(job_id)
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
                        self.logger.info(
                            "repro succeeded: %s", self.target_jobs[job_id]
                        )
                        self.failed_jobs.add(job_id)
                        done.add(job_id)
                    else:
                        clear()
                        self.logger.error(
                            "repro failed: %s: %s", self.target_jobs[job_id], result
                        )
                        self.failed_jobs.add(job_id)
                        done.add(job_id)
                except Exception as e:
                    clear()
                    self.logger.error(
                        "repro failed: %s: %s", self.target_jobs[job_id], repr(e)
                    )
                    self.failed_jobs.add(job_id)
                    done.add(job_id)
            else:
                if repro.state.name not in info:
                    info[repro.state.name] = []
                info[repro.state.name].append(self.target_jobs[job_id])

        for job_id in done:
            self.of.repro.delete(self.repros[job_id].vm_id)
            del self.repros[job_id]

        logline = []
        for name in info:
            logline.append("%s:%s" % (name, ",".join(info[name])))

        msg = "waiting repro: %s" % " ".join(logline)
        if len(logline) > 80:
            msg = "waiting on %d repros" % len(self.repros)

        return (
            not bool(self.repros),
            msg,
            bool(self.failed_jobs),
        )

    def check_repro(self) -> bool:
        self.logger.info("checking repros")
        return wait(self.check_repro_impl)

    def stop_template(self, target: str, delete_containers: bool = True) -> None:
        """ stop a specific template """

        if self.skip_cleanup:
            self.logger.warning("not cleaning up target: %s", target)
        else:
            self.of.template.stop(
                self.project,
                target,
                BUILD,
                delete_containers=delete_containers,
                stop_notifications=True,
            )

    def cleanup(self, *, user_pools: Optional[Dict[str, str]] = None) -> bool:
        """ cleanup all of the integration pools & jobs """

        if self.skip_cleanup:
            self.logger.warning("not cleaning up")
            return True

        self.logger.info("cleaning up")
        errors: List[Exception] = []

        for target, config in TARGETS.items():
            if config.os not in self.os:
                continue
            if target not in self.targets:
                continue

            try:
                self.logger.info("stopping %s", target)
                self.stop_template(target, delete_containers=False)
            except Exception as e:
                self.logger.error("cleanup of %s failed", target)
                errors.append(e)

        for pool in self.pools.values():
            if user_pools and pool.name in user_pools.values():
                continue

            self.logger.info(
                "halting: %s:%s:%s", pool.name, pool.os.name, pool.arch.name
            )
            try:
                self.of.pools.shutdown(pool.name, now=True)
            except Exception as e:
                self.logger.error("cleanup of pool failed: %s - %s", pool.name, e)
                errors.append(e)

        for repro in self.repros.values():
            try:
                self.of.repro.delete(repro.vm_id)
            except Exception as e:
                self.logger.error("cleanup of repro failed: %s - %s", repro.vm_id, e)
                errors.append(e)

        return not bool(errors)


class Run(Command):
    def test(
        self,
        samples: Directory,
        *,
        endpoint: Optional[str] = None,
        user_pools: Optional[Dict[str, str]] = None,
        pool_size: int = 10,
        region: Optional[str] = None,
        os_list: List[OS] = [OS.linux, OS.windows],
        targets: List[str] = list(TARGETS.keys()),
        skip_repro: bool = False,
        skip_cleanup: bool = False,
    ) -> None:
        self.onefuzz.__setup__(endpoint=endpoint)
        tester = TestOnefuzz(
            self.onefuzz,
            self.logger,
            pool_size=pool_size,
            os_list=os_list,
            targets=targets,
            skip_cleanup=skip_cleanup,
        )
        success = True

        error: Optional[Exception] = None
        try:
            tester.setup(region=region, user_pools=user_pools)
            tester.launch(samples)
            tester.check_jobs()
            if skip_repro:
                self.logger.warning("not testing crash repro")
            else:
                tester.launch_repro()
                tester.check_repro()
        except Exception as e:
            self.logger.error("testing failed: %s", repr(e))
            error = e
            success = False
        except KeyboardInterrupt:
            self.logger.error("interrupted testing")
            success = False

        if not tester.cleanup(user_pools=user_pools):
            success = False

        if tester.failed_jobs or tester.failed_repro:
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
