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
import json
import logging
import os
import re
import shutil
import subprocess
import sys
from textwrap import TextWrapper
import time
import zipfile
from enum import Enum
from shutil import which
from typing import Any, Callable, Dict, List, Optional, Set, Tuple, TypeVar
from uuid import UUID, uuid4

import requests
import yaml
from onefuzztypes.enums import OS, ContainerType, ScalesetState, TaskState, VmState
from onefuzztypes.models import Job, Pool, Repro, Scaleset, Task
from onefuzztypes.primitives import Container, Directory, File, PoolName, Region
from pydantic import BaseModel, Field

from onefuzz.api import Command, Onefuzz
from onefuzz.backend import ContainerWrapper, wait
from onefuzz.cli import execute_api

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


class LaunchInfo(BaseModel):
    test_id: UUID
    jobs: List[UUID]


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
    fuzzing_target_options: Optional[List[str]]
    inject_fake_regression: bool = Field(default=False)
    target_class: Optional[str]
    target_method: Optional[str]
    setup_dir: Optional[str]
    target_env: Optional[Dict[str, str]]
    pool: PoolName


TARGETS: Dict[str, Integration] = {
    "linux-trivial-crash-afl": Integration(
        template=TemplateType.afl,
        os=OS.linux,
        target_exe="fuzz.exe",
        inputs="seeds",
        wait_for_files={ContainerType.unique_reports: 1},
        pool="linux",
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
            # TODO: crashdumps are intermittently not captured
            # during integration tests on Linux. This requires more
            # investigation before we can fully enable this test.
            # ContainerType.crashdumps: 1,
            ContainerType.extra_output: 1,
        },
        reboot_after_setup=True,
        inject_fake_regression=True,
        target_env={
            # same TODO
            # "ASAN_OPTIONS": "disable_coredump=0:abort_on_error=1:unmap_shadow_on_exit=1"
        },
        fuzzing_target_options=[
            "--test:{extra_setup_dir}",
            "--only_asan_failures",
            "--write_test_file={extra_output_dir}/test.txt",
        ],
        pool="linux",
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
        fuzzing_target_options=["-runs=10000000"],
        pool="linux",
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
        pool="linux",
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
        pool="linux",
    ),
    "linux-libfuzzer-dotnet": Integration(
        template=TemplateType.libfuzzer_dotnet,
        os=OS.linux,
        setup_dir="GoodBadDotnet",
        target_exe="GoodBadDotnet/GoodBad.dll",
        target_options=["-max_len=4", "-only_ascii=1", "-seed=1"],
        target_class="GoodBad.Fuzzer",
        target_method="TestInput",
        use_setup=True,
        wait_for_files={
            ContainerType.inputs: 2,
            ContainerType.coverage: 1,
            ContainerType.crashes: 1,
            ContainerType.unique_reports: 1,
        },
        test_repro=False,
        pool="linux",
    ),
    "linux-libfuzzer-aarch64-crosscompile": Integration(
        template=TemplateType.libfuzzer_qemu_user,
        os=OS.linux,
        target_exe="fuzz.exe",
        inputs="inputs",
        use_setup=True,
        wait_for_files={ContainerType.inputs: 2, ContainerType.crashes: 1},
        test_repro=False,
        pool="linux",
    ),
    "linux-libfuzzer-rust": Integration(
        template=TemplateType.libfuzzer,
        os=OS.linux,
        target_exe="fuzz_target_1",
        wait_for_files={ContainerType.unique_reports: 1, ContainerType.coverage: 1},
        fuzzing_target_options=["--test:{extra_setup_dir}"],
        pool="linux",
    ),
    "linux-trivial-crash": Integration(
        template=TemplateType.radamsa,
        os=OS.linux,
        target_exe="fuzz.exe",
        inputs="seeds",
        wait_for_files={ContainerType.unique_reports: 1},
        inject_fake_regression=True,
        pool="linux",
    ),
    "linux-trivial-crash-asan": Integration(
        template=TemplateType.radamsa,
        os=OS.linux,
        target_exe="fuzz.exe",
        inputs="seeds",
        wait_for_files={ContainerType.unique_reports: 1},
        check_asan_log=True,
        disable_check_debugger=True,
        pool="linux",
    ),
    # TODO: Don't install OMS extension on linux anymore
    # TODO: Figure out why non mariner work is being scheduled to the mariner pool
    "mariner-libfuzzer": Integration(
        template=TemplateType.libfuzzer,
        os=OS.linux,
        target_exe="fuzz.exe",
        inputs="seeds",
        wait_for_files={
            ContainerType.unique_reports: 1,
            ContainerType.coverage: 1,
            ContainerType.inputs: 2,
            ContainerType.extra_output: 1,
        },
        reboot_after_setup=True,
        inject_fake_regression=True,
        fuzzing_target_options=[
            "--test:{extra_setup_dir}",
            "--write_test_file={extra_output_dir}/test.txt",
        ],
        pool=PoolName("mariner"),
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
            ContainerType.crashdumps: 1,
            ContainerType.extra_output: 1,
        },
        inject_fake_regression=True,
        target_env={"ASAN_SAVE_DUMPS": "my_dump.dmp"},
        # we should set unmap_shadow_on_exit=1 but it fails on Windows at the moment
        fuzzing_target_options=[
            "--test:{extra_setup_dir}",
            "--only_asan_failures",
            "--write_test_file={extra_output_dir}/test.txt",
        ],
        pool="windows",
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
        pool="windows",
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
        pool="windows",
    ),
    "windows-libfuzzer-dotnet": Integration(
        template=TemplateType.libfuzzer_dotnet,
        os=OS.windows,
        setup_dir="GoodBadDotnet",
        target_exe="GoodBadDotnet/GoodBad.dll",
        target_options=["-max_len=4", "-only_ascii=1", "-seed=1"],
        target_class="GoodBad.Fuzzer",
        target_method="TestInput",
        use_setup=True,
        wait_for_files={
            ContainerType.inputs: 2,
            ContainerType.coverage: 1,
            ContainerType.crashes: 1,
            ContainerType.unique_reports: 1,
        },
        test_repro=False,
        pool="windows",
    ),
    "windows-trivial-crash": Integration(
        template=TemplateType.radamsa,
        os=OS.windows,
        target_exe="fuzz.exe",
        inputs="seeds",
        wait_for_files={ContainerType.unique_reports: 1},
        inject_fake_regression=True,
        pool="windows",
    ),
}

OperationResult = TypeVar("OperationResult")


def retry(
    logger: logging.Logger,
    operation: Callable[[Any], OperationResult],
    description: str,
    tries: int = 10,
    wait_duration: int = 10,
    data: Any = None,
) -> OperationResult:
    count = 0
    while True:
        try:
            return operation(data)
        except Exception as exc:
            exception = exc
            logger.error(f"failed '{description}'. logging stack trace.")
            logger.error(exc)
        count += 1
        if count >= tries:
            if exception:
                raise exception
            else:
                raise Exception(f"failed '{description}'")
        else:
            logger.info(
                f"waiting {wait_duration} seconds before retrying '{description}'",
            )
            time.sleep(wait_duration)


class TestOnefuzz:
    def __init__(
        self,
        onefuzz: Onefuzz,
        logger: logging.Logger,
        test_id: UUID,
        polling_period=30,
        unmanaged_client_id: Optional[UUID] = None,
        unmanaged_client_secret: Optional[str] = None,
        unmanaged_principal_id: Optional[UUID] = None,
    ) -> None:
        self.of = onefuzz
        self.logger = logger
        self.test_id = test_id
        self.project = f"test-{self.test_id}"
        self.start_log_marker = f"integration-test-injection-error-start-{self.test_id}"
        self.stop_log_marker = f"integration-test-injection-error-stop-{self.test_id}"
        self.polling_period = polling_period
        self.tools_dir = f"{self.test_id}/tools"
        self.unmanaged_client_id = unmanaged_client_id
        self.unmanaged_client_secret = unmanaged_client_secret
        self.unmanaged_principal_id = unmanaged_principal_id

    def setup(
        self, *, region: Optional[Region] = None, pool_size: int, os_list: List[OS]
    ) -> None:
        def try_info_get(data: Any) -> None:
            self.of.info.get()

        retry(self.logger, try_info_get, "testing endpoint")

        self.inject_log(self.start_log_marker)
        for entry in os_list:
            name = self.build_pool_name(entry.name)
            self.logger.info("creating pool: %s:%s", entry.name, name)
            self.of.pools.create(name, entry)
            self.logger.info("creating scaleset for pool: %s", name)
            self.of.scalesets.create(
                name, pool_size, region=region, initial_size=pool_size
            )

        name = self.build_pool_name("mariner")
        self.logger.info("creating pool: %s:%s", "mariner", name)
        self.of.pools.create(name, OS.linux)
        self.logger.info("creating scaleset for pool: %s", name)
        self.of.scalesets.create(
            name,
            pool_size,
            region=region,
            initial_size=pool_size,
            image="MicrosoftCBLMariner:cbl-mariner:cbl-mariner-2-gen2:latest",
        )

    class UnmanagedPool:
        def __init__(
            self,
            onefuzz: Onefuzz,
            logger: logging.Logger,
            test_id: UUID,
            pool_name: PoolName,
            the_os: OS,
            pool_size: int,
            unmanaged_client_id: UUID,
            unmanaged_client_secret: str,
            unmanaged_principal_id: UUID,
            save_logs: bool = False,
        ) -> None:
            self.of = onefuzz
            self.logger = logger
            self.test_id = test_id
            self.project = f"test-{self.test_id}"
            self.tools_dir = f"{self.test_id}/tools"
            self.unmanaged_client_id = unmanaged_client_id
            self.unmanaged_client_secret = unmanaged_client_secret
            self.pool_name = pool_name
            if pool_size < 1:
                raise Exception("pool_size must be >= 1")
            self.pool_size = pool_size
            self.the_os = the_os
            self.unmanaged_principal_id = unmanaged_principal_id
            self.image_tag = f"unmanaged_agent:{self.test_id}"
            self.log_file_path: Optional[str] = None
            self.process: Optional[subprocess.Popen[bytes]] = None
            self.save_logs = save_logs

        def __enter__(self):
            self.start_unmanaged_pool()

        def __exit__(self, *args):
            self.stop_unmanaged_pool()

        def get_tools_path(self, the_os: OS):
            if the_os == OS.linux:
                return os.path.join(self.tools_dir, "linux")
            elif the_os == OS.windows:
                return os.path.join(self.tools_dir, "win64")
            else:
                raise Exception(f"unsupported os: {the_os}")

        def start_unmanaged_pool(self):
            self.logger.info("creating pool: %s:%s", self.the_os.name, self.pool_name)
            self.of.pools.create(
                self.pool_name,
                self.the_os,
                unmanaged=True,
                object_id=self.unmanaged_principal_id,
            )

            os.makedirs(self.tools_dir, exist_ok=True)
            self.logger.info("starting unmanaged pools docker containers")
            if self.unmanaged_client_id is None or self.unmanaged_client_secret is None:
                raise Exception(
                    "unmanaged_client_id and unmanaged_client_secret must be set to test the unmanaged scenario"
                )

            self.logger.info("downloading tools")
            self.of.tools.get(self.tools_dir)
            self.logger.info("extracting tools")
            with zipfile.ZipFile(
                os.path.join(self.tools_dir, "tools.zip"), "r"
            ) as zip_ref:
                zip_ref.extractall(self.tools_dir)

            tools_path = self.get_tools_path(self.the_os)

            self.logger.info("creating docker compose file")
            services = list(
                map(
                    lambda x: {
                        f"agent{x+1}": {
                            "depends_on": ["agent1"],
                            "image": self.image_tag,
                            "command": f"--machine_id {uuid4()}",
                            "restart": "unless-stopped",
                        }
                    },
                    range(1, self.pool_size - 1),
                )
            )
            build = {"context": "."}
            if self.the_os == OS.windows:
                windows_type = subprocess.check_output(
                    "powershell -c (Get-ComputerInfo).OsProductType", shell=True
                )
                if windows_type.strip() == b"Workstation":
                    self.logger.info("using windows workstation image")
                    build = {
                        "context": ".",
                        "args": {"BASE_IMAGE": "mcr.microsoft.com/windows:ltsc2019"},
                    }
                else:
                    self.logger.info("using windows server image")
                    build = {
                        "context": ".",
                        "args": {
                            "BASE_IMAGE": "mcr.microsoft.com/windows/server:ltsc2022"
                        },
                    }

            # create docker compose file
            compose = {
                "version": "3",
                "services": {
                    "agent1": {
                        "image": self.image_tag,
                        "build": build,
                        "command": f"--machine_id {uuid4()}",
                        "restart": "unless-stopped",
                    }
                },
            }
            for service in services:
                key = next(iter(service.keys()))
                compose["services"][key] = service[key]

            docker_compose_path = os.path.join(tools_path, "docker-compose.yml")
            self.logger.info(
                f"writing docker-compose.yml to {docker_compose_path}:\n{yaml.dump(compose)}"
            )
            with open(docker_compose_path, "w") as f:
                yaml.dump(compose, f)

            self.logger.info(f"retrieving base config.json from {self.pool_name}")
            config = self.of.pools.get_config(self.pool_name)

            self.logger.info(f"updating config.json with unmanaged credentials")
            config.client_credentials.client_id = self.unmanaged_client_id
            config.client_credentials.client_secret = self.unmanaged_client_secret

            config_path = os.path.join(tools_path, "config.json")
            self.logger.info(f"writing config.json to {config_path}")
            with open(config_path, "w") as f:
                f.write(config.json())

            self.logger.info(f"starting docker compose")
            log_file_name = "docker-logs.txt"
            self.log_file_path = os.path.join(tools_path, log_file_name)
            subprocess.check_call(
                "docker compose up -d --force-recreate --build",
                shell=True,
                cwd=tools_path,
            )
            if self.save_logs:
                self.process = subprocess.Popen(
                    f"docker compose logs -f > {log_file_name} 2>&1",
                    shell=True,
                    cwd=tools_path,
                )

        def stop_unmanaged_pool(self):
            tools_path = self.get_tools_path(self.the_os)
            subprocess.check_call(
                "docker compose rm --stop --force", shell=True, cwd=tools_path
            )
            subprocess.check_call(
                f"docker image rm {self.image_tag}", shell=True, cwd=tools_path
            )

    def create_unmanaged_pool(
        self, pool_size: int, the_os: OS, save_logs: bool = False
    ) -> "UnmanagedPool":
        if (
            self.unmanaged_client_id is None
            or self.unmanaged_client_secret is None
            or self.unmanaged_principal_id is None
        ):
            raise Exception(
                "unmanaged_client_id, unmanaged_client_secret and unmanaged_principal_id must be set to test the unmanaged scenario"
            )

        return self.UnmanagedPool(
            self.of,
            self.logger,
            self.test_id,
            PoolName(f"unmanaged-testpool-{self.test_id}"),
            the_os,
            pool_size,
            unmanaged_client_id=self.unmanaged_client_id,
            unmanaged_client_secret=self.unmanaged_client_secret,
            unmanaged_principal_id=self.unmanaged_principal_id,
            save_logs=save_logs,
        )

    def launch(
        self,
        path: Directory,
        *,
        os_list: List[OS],
        targets: List[str],
        duration=int,
        unmanaged_pool: Optional[UnmanagedPool] = None,
    ) -> List[UUID]:
        """Launch all of the fuzzing templates"""

        pool = None
        if unmanaged_pool is not None:
            pool = unmanaged_pool.pool_name

        job_ids = []

        for target, config in TARGETS.items():
            if target not in targets:
                continue

            if config.os not in os_list:
                continue

            if pool is None:
                pool = self.build_pool_name(config.pool)

            self.logger.info("launching: %s", target)

            setup: Directory | str | None
            if config.setup_dir is None:
                if config.use_setup:
                    setup = Directory(os.path.join(path, target))
                else:
                    setup = None
            else:
                setup = Directory(config.setup_dir)

            target_exe = File(os.path.join(path, target, config.target_exe))
            inputs = (
                Directory(os.path.join(path, target, config.inputs))
                if config.inputs
                else None
            )

            if setup and config.nested_setup_dir:
                setup = Directory(os.path.join(setup, config.nested_setup_dir))

            job: Optional[Job] = None

            job = self.build_job(
                duration, pool, target, config, setup, target_exe, inputs
            )

            if config.inject_fake_regression and job is not None:
                self.of.debug.notification.job(job.job_id)

            if not job:
                raise Exception("missing job")

            job_ids.append(job.job_id)

        return job_ids

    def build_job(
        self,
        duration: int,
        pool: PoolName,
        target: str,
        config: Integration,
        setup: Optional[Directory],
        target_exe: File,
        inputs: Optional[Directory],
    ) -> Optional[Job]:
        if config.template == TemplateType.libfuzzer:
            # building the extra_setup & extra_output containers to test variable substitution
            # and upload of files (in the case of extra_output)
            extra_setup_container = self.of.containers.create("extra-setup")
            extra_output_container = self.of.containers.create("extra-output")
            return self.of.template.libfuzzer.basic(
                self.project,
                target,
                BUILD,
                pool,
                target_exe=target_exe,
                inputs=inputs,
                setup_dir=setup,
                duration=duration,
                vm_count=1,
                reboot_after_setup=config.reboot_after_setup or False,
                target_options=config.target_options,
                fuzzing_target_options=config.fuzzing_target_options,
                extra_setup_container=Container(extra_setup_container.name),
                extra_output_container=Container(extra_output_container.name),
                target_env=config.target_env,
            )
        elif config.template == TemplateType.libfuzzer_dotnet:
            if setup is None:
                raise Exception("setup required for libfuzzer_dotnet")
            if config.target_class is None:
                raise Exception("target_class required for libfuzzer_dotnet")
            if config.target_method is None:
                raise Exception("target_method required for libfuzzer_dotnet")

            return self.of.template.libfuzzer.dotnet(
                self.project,
                target,
                BUILD,
                pool,
                target_dll=File(config.target_exe),
                inputs=inputs,
                setup_dir=setup,
                duration=duration,
                vm_count=1,
                fuzzing_target_options=config.target_options,
                target_class=config.target_class,
                target_method=config.target_method,
                target_env=config.target_env,
            )
        elif config.template == TemplateType.libfuzzer_qemu_user:
            return self.of.template.libfuzzer.qemu_user(
                self.project,
                target,
                BUILD,
                pool,
                inputs=inputs,
                target_exe=target_exe,
                duration=duration,
                vm_count=1,
                target_options=config.target_options,
                target_env=config.target_env,
            )
        elif config.template == TemplateType.radamsa:
            return self.of.template.radamsa.basic(
                self.project,
                target,
                BUILD,
                pool_name=pool,
                target_exe=target_exe,
                inputs=inputs,
                setup_dir=setup,
                check_asan_log=config.check_asan_log or False,
                disable_check_debugger=config.disable_check_debugger or False,
                duration=duration,
                vm_count=1,
                target_env=config.target_env,
            )
        elif config.template == TemplateType.afl:
            return self.of.template.afl.basic(
                self.project,
                target,
                BUILD,
                pool_name=pool,
                target_exe=target_exe,
                inputs=inputs,
                setup_dir=setup,
                duration=duration,
                vm_count=1,
                target_options=config.target_options,
                target_env=config.target_env,
            )
        else:
            raise NotImplementedError

    def check_task(
        self, job: Job, task: Task, scalesets: List[Scaleset]
    ) -> TaskTestState:
        # Check if the scaleset the task is assigned is OK
        for scaleset in scalesets:
            if (
                task.config.pool is not None
                and scaleset.pool_name == task.config.pool.pool_name
                and scaleset.state not in scaleset.state.available()
                # not available() does not mean failed
                and scaleset.state not in [ScalesetState.init, ScalesetState.setup]
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
        self,
        poll: bool = False,
        stop_on_complete_check: bool = False,
        job_ids: List[UUID] = [],
        timeout: datetime.timedelta = datetime.timedelta(hours=1),
    ) -> bool:
        """Check all of the integration jobs"""
        jobs: Dict[UUID, Job] = {
            x.job_id: x
            for x in self.get_jobs()
            if (not job_ids) or (x.job_id in job_ids)
        }
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
                if task.config.containers:
                    for container in task.config.containers:
                        if container.type in TARGETS[job.config.name].wait_for_files:
                            count = TARGETS[job.config.name].wait_for_files[
                                container.type
                            ]
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

        start = datetime.datetime.utcnow()

        def check_jobs_impl() -> Tuple[bool, str, bool]:
            self.cleared = False
            failed_jobs: Set[UUID] = set()
            job_task_states: Dict[UUID, Set[TaskTestState]] = {}

            if datetime.datetime.utcnow() - start > timeout:
                return (True, "timed out while checking jobs", False)

            for job_id in check_containers:
                job_name = jobs[job_id].config.name
                finished_containers: Set[Container] = set()
                for container_name, container_impl in check_containers[job_id].items():
                    container_client, required_count = container_impl
                    found_count = len(container_client.list_blobs())
                    if found_count >= required_count:
                        clear()
                        self.logger.info(
                            "found %d files (needed %d) for %s - %s",
                            found_count,
                            required_count,
                            job_name,
                            container_name,
                        )
                        finished_containers.add(container_name)

                for container_name in finished_containers:
                    del check_containers[job_id][container_name]

                to_check = check_containers[job_id].keys()
                if len(to_check) > 0:
                    self.logger.info(
                        "%s - still waiting for %s", job_name, ", ".join(to_check)
                    )

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
            return wait(check_jobs_impl, frequency=self.polling_period)
        else:
            _, msg, result = check_jobs_impl()
            self.logger.info(msg)
            return result

    def get_job_crash_report(self, job_id: UUID) -> Optional[Tuple[Container, str]]:
        for task in self.of.tasks.list(job_id=job_id, state=None):
            if task.config.containers:
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

    def launch_repro(
        self, job_ids: List[UUID] = []
    ) -> Tuple[bool, Dict[UUID, Tuple[Job, Repro]]]:
        # launch repro for one report from all succeessful jobs
        has_cdb = bool(which("cdb.exe"))
        has_gdb = bool(which("gdb"))

        jobs = [
            job for job in self.get_jobs() if (not job_ids) or (job.job_id in job_ids)
        ]

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

            for job, repro in list(repros.values()):
                repros[job.job_id] = (job, self.of.repro.get(repro.vm_id))

            for job, repro in list(repros.values()):
                if repro.error:
                    clear()
                    self.logger.error(
                        "repro failed: %s: %s",
                        job.config.name,
                        repro.error,
                    )
                    self.success = False
                    # self.of.repro.delete(repro.vm_id)
                    # del repros[job.job_id]
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
                            self.success = False
                    except Exception as err:
                        clear()
                        self.logger.error("repro failed: %s - %s", job.config.name, err)
                        self.success = False
                    del repros[job.job_id]
                elif repro.state not in [VmState.init, VmState.extensions_launch]:
                    self.logger.error(
                        "repro failed: %s - bad state: %s", job.config.name, repro.state
                    )
                    self.success = False
                    del repros[job.job_id]

            repro_states: Dict[str, List[str]] = {}
            for job, repro in repros.values():
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

        return wait(check_repro_impl, frequency=self.polling_period)

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

        shutil.rmtree(self.tools_dir)
        container_names = set()
        for job in jobs:
            for task in self.of.tasks.list(job_id=job.job_id, state=None):
                if task.config.containers:
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
        wait(self.check_log_end_marker, frequency=self.polling_period)
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
            if (
                entry.get("customDimensions", {}).get("LogLevel") == "Information"
                or entry.get("severityLevel") <= 2
            ):
                continue

            # ignore warnings coming from the rust code, only be concerned
            # about errors
            if entry.get("severityLevel") == 2 and "rust" in entry.get("sdkVersion"):
                continue

            # ignore resource not found warnings from azure-functions layer,
            # which relate to azure-retry issues
            if (
                message.startswith("Client-Request-ID=")
                and ("ResourceNotFound" in message or "TableAlreadyExists" in message)
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
            ) and ("rust" in entry.get("sdkVersion")):
                continue

            if message is None:
                self.logger.error("error log: %s", entry)
            else:
                self.logger.error("error log: %s", message)
            seen_errors = True

        if seen_errors:
            raise Exception("logs included errors")

    def build_pool_name(self, os_type: str) -> PoolName:
        return PoolName(f"testpool-{os_type}-{self.test_id}")


class Run(Command):
    def check_jobs(
        self,
        test_id: UUID,
        *,
        endpoint: Optional[str],
        authority: Optional[str] = None,
        client_id: Optional[str],
        client_secret: Optional[str],
        poll: bool = False,
        stop_on_complete_check: bool = False,
        job_ids: List[UUID] = [],
    ) -> None:
        self.onefuzz.__setup__(
            endpoint=endpoint,
            client_id=client_id,
            client_secret=client_secret,
            authority=authority,
        )
        tester = TestOnefuzz(self.onefuzz, self.logger, test_id)
        result = tester.check_jobs(
            poll=poll,
            stop_on_complete_check=stop_on_complete_check,
            job_ids=job_ids,
        )
        if not result:
            raise Exception("jobs failed")

    def check_repros(
        self,
        test_id: UUID,
        *,
        endpoint: Optional[str],
        client_id: Optional[str],
        client_secret: Optional[str],
        authority: Optional[str] = None,
        job_ids: List[UUID] = [],
    ) -> None:
        self.onefuzz.__setup__(
            endpoint=endpoint,
            client_id=client_id,
            client_secret=client_secret,
            authority=authority,
        )
        tester = TestOnefuzz(self.onefuzz, self.logger, test_id)
        launch_result, repros = tester.launch_repro(job_ids=job_ids)
        result = tester.check_repro(repros)
        if not (result and launch_result):
            raise Exception("repros failed")

    def setup(
        self,
        *,
        endpoint: Optional[str] = None,
        authority: Optional[str] = None,
        client_id: Optional[str] = None,
        client_secret: Optional[str] = None,
        pool_size: int = 20,
        region: Optional[Region] = None,
        os_list: List[OS] = [OS.linux, OS.windows],
        test_id: Optional[UUID] = None,
    ) -> None:
        if test_id is None:
            test_id = uuid4()
        self.logger.info("launching test_id: %s", test_id)

        def try_setup(data: Any) -> None:
            self.onefuzz.__setup__(
                endpoint=endpoint,
                client_id=client_id,
                client_secret=client_secret,
                authority=authority,
            )

        retry(self.logger, try_setup, "trying to configure")

        tester = TestOnefuzz(
            self.onefuzz,
            self.logger,
            test_id,
        )
        tester.setup(
            region=region,
            pool_size=pool_size,
            os_list=os_list,
        )

    def launch(
        self,
        samples: Directory,
        *,
        endpoint: Optional[str] = None,
        authority: Optional[str] = None,
        client_id: Optional[str] = None,
        client_secret: Optional[str] = None,
        os_list: List[OS] = [OS.linux, OS.windows],
        targets: List[str] = list(TARGETS.keys()),
        test_id: Optional[UUID] = None,
        duration: int = 1,
    ) -> None:
        if test_id is None:
            test_id = uuid4()
        self.logger.info("launching test_id: %s", test_id)

        def try_setup(data: Any) -> None:
            self.onefuzz.__setup__(
                endpoint=endpoint,
                client_id=client_id,
                client_secret=client_secret,
                authority=authority,
            )

        retry(self.logger, try_setup, "trying to configure")

        tester = TestOnefuzz(self.onefuzz, self.logger, test_id)
        job_ids = tester.launch(
            samples, os_list=os_list, targets=targets, duration=duration
        )
        launch_data = LaunchInfo(test_id=test_id, jobs=job_ids)

        print(f"launch info: {launch_data.json()}")

    def cleanup(
        self,
        test_id: UUID,
        *,
        endpoint: Optional[str],
        authority: Optional[str],
        client_id: Optional[str],
        client_secret: Optional[str],
    ) -> None:
        self.onefuzz.__setup__(
            endpoint=endpoint,
            client_id=client_id,
            client_secret=client_secret,
            authority=authority,
        )
        tester = TestOnefuzz(self.onefuzz, self.logger, test_id=test_id)
        tester.cleanup()

    def check_logs(
        self,
        test_id: UUID,
        *,
        endpoint: Optional[str],
        authority: Optional[str] = None,
        client_id: Optional[str],
        client_secret: Optional[str],
    ) -> None:
        self.onefuzz.__setup__(
            endpoint=endpoint,
            client_id=client_id,
            client_secret=client_secret,
            authority=authority,
        )
        tester = TestOnefuzz(self.onefuzz, self.logger, test_id=test_id)
        tester.check_logs_for_errors()

    def check_results(
        self,
        *,
        endpoint: Optional[str] = None,
        authority: Optional[str] = None,
        client_id: Optional[str] = None,
        client_secret: Optional[str] = None,
        skip_repro: bool = False,
        test_id: UUID,
        job_ids: List[UUID] = [],
    ) -> None:
        self.check_jobs(
            test_id,
            endpoint=endpoint,
            authority=authority,
            client_id=client_id,
            client_secret=client_secret,
            poll=True,
            stop_on_complete_check=True,
            job_ids=job_ids,
        )

    def test_unmanaged(
        self,
        samples: Directory,
        os: OS,
        *,
        test_id: Optional[UUID] = None,
        endpoint: Optional[str] = None,
        authority: Optional[str] = None,
        client_id: Optional[str] = None,
        client_secret: Optional[str] = None,
        pool_size: int = 4,
        targets: List[str] = list(TARGETS.keys()),
        duration: int = 1,
        unmanaged_client_id: Optional[UUID] = None,
        unmanaged_client_secret: Optional[str] = None,
        unmanaged_principal_id: Optional[UUID] = None,
        save_logs: bool = False,
        timeout_in_minutes: int = 60,
    ) -> None:
        if test_id is None:
            test_id = uuid4()
        self.logger.info("test_unmanaged test_id: %s", test_id)
        try:

            def try_setup(data: Any) -> None:
                self.onefuzz.__setup__(
                    endpoint=endpoint,
                    client_id=client_id,
                    client_secret=client_secret,
                    authority=authority,
                )

            retry(self.logger, try_setup, "trying to configure")
            tester = TestOnefuzz(
                self.onefuzz,
                self.logger,
                test_id,
                unmanaged_client_id=unmanaged_client_id,
                unmanaged_client_secret=unmanaged_client_secret,
                unmanaged_principal_id=unmanaged_principal_id,
            )

            unmanaged_pool = tester.create_unmanaged_pool(
                pool_size, os, save_logs=save_logs
            )
            with unmanaged_pool:
                tester.launch(
                    samples,
                    os_list=[os],
                    targets=targets,
                    duration=duration,
                    unmanaged_pool=unmanaged_pool,
                )
                result = tester.check_jobs(
                    poll=True,
                    stop_on_complete_check=True,
                    timeout=datetime.timedelta(minutes=timeout_in_minutes),
                )
                if not result:
                    raise Exception("jobs failed")
                else:
                    self.logger.info("****** testing succeeded")

        except Exception as e:
            self.logger.error("testing failed: %s", repr(e))
            sys.exit(1)
        except KeyboardInterrupt:
            self.logger.error("interrupted testing")
            sys.exit(1)

    def test(
        self,
        samples: Directory,
        *,
        endpoint: Optional[str] = None,
        authority: Optional[str] = None,
        client_id: Optional[str] = None,
        client_secret: Optional[str] = None,
        pool_size: int = 15,
        region: Optional[Region] = None,
        os_list: List[OS] = [OS.linux, OS.windows],
        targets: List[str] = list(TARGETS.keys()),
        skip_repro: bool = False,
        duration: int = 1,
        unmanaged_client_id: Optional[UUID] = None,
        unmanaged_client_secret: Optional[str] = None,
    ) -> None:
        success = True

        test_id = uuid4()
        error: Optional[Exception] = None
        try:

            def try_setup(data: Any) -> None:
                self.onefuzz.__setup__(
                    endpoint=endpoint,
                    client_id=client_id,
                    client_secret=client_secret,
                    authority=authority,
                )

            retry(self.logger, try_setup, "trying to configure")
            tester = TestOnefuzz(
                self.onefuzz,
                self.logger,
                test_id,
                unmanaged_client_id=unmanaged_client_id,
                unmanaged_client_secret=unmanaged_client_secret,
            )
            tester.setup(
                region=region,
                pool_size=pool_size,
                os_list=os_list,
            )
            tester.launch(samples, os_list=os_list, targets=targets, duration=duration)
            result = tester.check_jobs(poll=True, stop_on_complete_check=True)
            if not result:
                raise Exception("jobs failed")
            if skip_repro:
                self.logger.warning("not testing crash repro")
            else:
                launch_result, repros = tester.launch_repro()
                result = tester.check_repro(repros)
                if not (result and launch_result):
                    raise Exception("repros failed")

            tester.check_logs_for_errors()

        except Exception as e:
            self.logger.error("testing failed: %s", repr(e))
            error = e
            success = False
        except KeyboardInterrupt:
            self.logger.error("interrupted testing")
            success = False

        try:
            self.cleanup(
                test_id,
                endpoint=endpoint,
                client_id=client_id,
                client_secret=client_secret,
                authority=authority,
            )
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
