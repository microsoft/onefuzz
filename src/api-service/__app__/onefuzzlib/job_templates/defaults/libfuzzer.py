#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

from uuid import UUID

from onefuzztypes.enums import (
    OS,
    ContainerType,
    TaskType,
    UserFieldOperation,
    UserFieldType,
)
from onefuzztypes.job_templates import JobTemplate, UserField, UserFieldLocation
from onefuzztypes.models import (
    JobConfig,
    TaskConfig,
    TaskContainers,
    TaskDetails,
    TaskPool,
)
from onefuzztypes.primitives import Container, PoolName

from .common import (
    DURATION_HELP,
    POOL_HELP,
    REBOOT_HELP,
    RETRY_COUNT_HELP,
    TAGS_HELP,
    TARGET_EXE_HELP,
    TARGET_OPTIONS_HELP,
    VM_COUNT_HELP,
)

libfuzzer_linux = JobTemplate(
    os=OS.linux,
    job=JobConfig(project="", name=Container(""), build="", duration=1),
    tasks=[
        TaskConfig(
            job_id=UUID(int=0),
            task=TaskDetails(
                type=TaskType.libfuzzer_fuzz,
                duration=1,
                target_exe="fuzz.exe",
                target_env={},
                target_options=[],
            ),
            pool=TaskPool(count=1, pool_name=PoolName("")),
            containers=[
                TaskContainers(name=Container(""), type=ContainerType.setup),
                TaskContainers(name=Container(""), type=ContainerType.crashes),
                TaskContainers(name=Container(""), type=ContainerType.inputs),
            ],
            tags={},
        ),
        TaskConfig(
            job_id=UUID(int=0),
            prereq_tasks=[UUID(int=0)],
            task=TaskDetails(
                type=TaskType.libfuzzer_crash_report,
                duration=1,
                target_exe="fuzz.exe",
                target_env={},
                target_options=[],
            ),
            pool=TaskPool(count=1, pool_name=PoolName("")),
            containers=[
                TaskContainers(name=Container(""), type=ContainerType.setup),
                TaskContainers(name=Container(""), type=ContainerType.crashes),
                TaskContainers(name=Container(""), type=ContainerType.no_repro),
                TaskContainers(name=Container(""), type=ContainerType.reports),
                TaskContainers(name=Container(""), type=ContainerType.unique_reports),
            ],
            tags={},
        ),
        TaskConfig(
            job_id=UUID(int=0),
            prereq_tasks=[UUID(int=0)],
            task=TaskDetails(
                type=TaskType.coverage,
                duration=1,
                target_exe="fuzz.exe",
                target_env={},
                target_options=[],
            ),
            pool=TaskPool(count=1, pool_name=PoolName("")),
            containers=[
                TaskContainers(name=Container(""), type=ContainerType.setup),
                TaskContainers(name=Container(""), type=ContainerType.readonly_inputs),
                TaskContainers(name=Container(""), type=ContainerType.coverage),
            ],
            tags={},
        ),
    ],
    notifications=[],
    user_fields=[
        UserField(
            name="pool_name",
            help=POOL_HELP,
            type=UserFieldType.Str,
            required=True,
            locations=[
                UserFieldLocation(
                    op=UserFieldOperation.replace,
                    path="/tasks/0/pool/pool_name",
                ),
                UserFieldLocation(
                    op=UserFieldOperation.replace,
                    path="/tasks/1/pool/pool_name",
                ),
                UserFieldLocation(
                    op=UserFieldOperation.replace,
                    path="/tasks/2/pool/pool_name",
                ),
            ],
        ),
        UserField(
            name="target_exe",
            help=TARGET_EXE_HELP,
            type=UserFieldType.Str,
            default="fuzz.exe",
            locations=[
                UserFieldLocation(
                    op=UserFieldOperation.replace,
                    path="/tasks/0/task/target_exe",
                ),
                UserFieldLocation(
                    op=UserFieldOperation.replace,
                    path="/tasks/1/task/target_exe",
                ),
                UserFieldLocation(
                    op=UserFieldOperation.replace,
                    path="/tasks/2/task/target_exe",
                ),
            ],
        ),
        UserField(
            name="duration",
            help=DURATION_HELP,
            type=UserFieldType.Int,
            default=24,
            locations=[
                UserFieldLocation(
                    op=UserFieldOperation.replace,
                    path="/tasks/0/task/duration",
                ),
                UserFieldLocation(
                    op=UserFieldOperation.replace,
                    path="/tasks/1/task/duration",
                ),
                UserFieldLocation(
                    op=UserFieldOperation.replace,
                    path="/tasks/2/task/duration",
                ),
                UserFieldLocation(op=UserFieldOperation.replace, path="/job/duration"),
            ],
        ),
        UserField(
            name="target_workers",
            help="Number of instances of the libfuzzer target on each VM",
            type=UserFieldType.Int,
            locations=[
                UserFieldLocation(
                    op=UserFieldOperation.replace,
                    path="/tasks/0/task/target_workers",
                ),
            ],
        ),
        UserField(
            name="vm_count",
            help=VM_COUNT_HELP,
            type=UserFieldType.Int,
            default=2,
            locations=[
                UserFieldLocation(
                    op=UserFieldOperation.replace,
                    path="/tasks/0/pool/count",
                ),
            ],
        ),
        UserField(
            name="target_options",
            help=TARGET_OPTIONS_HELP,
            type=UserFieldType.ListStr,
            locations=[
                UserFieldLocation(
                    op=UserFieldOperation.replace,
                    path="/tasks/0/task/target_options",
                ),
                UserFieldLocation(
                    op=UserFieldOperation.replace,
                    path="/tasks/1/task/target_options",
                ),
                UserFieldLocation(
                    op=UserFieldOperation.replace,
                    path="/tasks/2/task/target_options",
                ),
            ],
        ),
        UserField(
            name="target_env",
            help="Environment variables for the target",
            type=UserFieldType.DictStr,
            locations=[
                UserFieldLocation(
                    op=UserFieldOperation.replace,
                    path="/tasks/0/task/target_env",
                ),
                UserFieldLocation(
                    op=UserFieldOperation.replace,
                    path="/tasks/1/task/target_env",
                ),
                UserFieldLocation(
                    op=UserFieldOperation.replace,
                    path="/tasks/2/task/target_env",
                ),
            ],
        ),
        UserField(
            name="check_fuzzer_help",
            help="Verify fuzzer by checking if it supports -help=1",
            type=UserFieldType.Bool,
            default=True,
            locations=[
                UserFieldLocation(
                    op=UserFieldOperation.add,
                    path="/tasks/0/task/check_fuzzer_help",
                ),
                UserFieldLocation(
                    op=UserFieldOperation.add,
                    path="/tasks/1/task/check_fuzzer_help",
                ),
                UserFieldLocation(
                    op=UserFieldOperation.add,
                    path="/tasks/2/task/check_fuzzer_help",
                ),
            ],
        ),
        UserField(
            name="colocate",
            help="Run all of the tasks on the same node",
            type=UserFieldType.Bool,
            default=True,
            locations=[
                UserFieldLocation(
                    op=UserFieldOperation.add,
                    path="/tasks/0/colocate",
                ),
                UserFieldLocation(
                    op=UserFieldOperation.add,
                    path="/tasks/1/colocate",
                ),
                UserFieldLocation(
                    op=UserFieldOperation.add,
                    path="/tasks/2/colocate",
                ),
            ],
        ),
        UserField(
            name="expect_crash_on_failure",
            help="Require crashes upon non-zero exits from libfuzzer",
            type=UserFieldType.Bool,
            default=False,
            locations=[
                UserFieldLocation(
                    op=UserFieldOperation.add,
                    path="/tasks/0/task/expect_crash_on_failure",
                ),
            ],
        ),
        UserField(
            name="reboot_after_setup",
            help=REBOOT_HELP,
            type=UserFieldType.Bool,
            default=False,
            locations=[
                UserFieldLocation(
                    op=UserFieldOperation.replace,
                    path="/tasks/0/task/reboot_after_setup",
                ),
                UserFieldLocation(
                    op=UserFieldOperation.replace,
                    path="/tasks/1/task/reboot_after_setup",
                ),
                UserFieldLocation(
                    op=UserFieldOperation.replace,
                    path="/tasks/2/task/reboot_after_setup",
                ),
            ],
        ),
        UserField(
            name="check_retry_count",
            help=RETRY_COUNT_HELP,
            type=UserFieldType.Int,
            locations=[
                UserFieldLocation(
                    op=UserFieldOperation.replace,
                    path="/tasks/1/task/check_retry_count",
                ),
            ],
        ),
        UserField(
            name="target_timeout",
            help="Number of seconds to timeout during reproduction",
            type=UserFieldType.Int,
            locations=[
                UserFieldLocation(
                    op=UserFieldOperation.replace,
                    path="/tasks/1/task/target_timeout",
                ),
            ],
        ),
        UserField(
            name="minimized_stack_depth",
            help="Number of frames to include in the minimized stack",
            type=UserFieldType.Int,
            locations=[
                UserFieldLocation(
                    op=UserFieldOperation.replace,
                    path="/tasks/1/task/minimized_stack_depth",
                ),
            ],
        ),
        UserField(
            name="tags",
            help=TAGS_HELP,
            type=UserFieldType.DictStr,
            locations=[
                UserFieldLocation(
                    op=UserFieldOperation.add,
                    path="/tasks/0/tags",
                ),
                UserFieldLocation(
                    op=UserFieldOperation.add,
                    path="/tasks/1/tags",
                ),
                UserFieldLocation(
                    op=UserFieldOperation.add,
                    path="/tasks/2/tags",
                ),
            ],
        ),
    ],
)

libfuzzer_windows = libfuzzer_linux.copy(deep=True)
libfuzzer_windows.os = OS.windows
