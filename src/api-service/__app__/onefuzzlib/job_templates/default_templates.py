#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

from uuid import UUID

from onefuzztypes.enums import (
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

POOL_HELP = "Execute the task on the specified pool"
DURATION_HELP = "Number of hours to execute the task"
TARGET_EXE_HELP = "Path to the target executable"
TARGET_OPTIONS_HELP = "Command line options for the target"
VM_COUNT_HELP = "Number of VMs to use for fuzzing"
RETRY_COUNT_HELP = "Number of times to retry a crash to verify reproducability"
REBOOT_HELP = "After executing the setup script, reboot the VM"
TAGS_HELP = "User provided metadata for the tasks"

TEMPLATES = {
    "afl": JobTemplate(
        job=JobConfig(project="", name=Container(""), build="", duration=1),
        tasks=[
            TaskConfig(
                job_id=(UUID(int=0)),
                task=TaskDetails(
                    type=TaskType.generic_supervisor,
                    duration=1,
                    target_exe="fuzz.exe",
                    target_env={},
                    target_options=[],
                    supervisor_exe="",
                    supervisor_options=[],
                    supervisor_input_marker="@@",
                ),
                pool=TaskPool(count=1, pool_name=PoolName("")),
                containers=[
                    TaskContainers(
                        name=Container("afl-container-name"), type=ContainerType.tools
                    ),
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
                    type=TaskType.generic_crash_report,
                    duration=1,
                    target_exe="fuzz.exe",
                    target_env={},
                    target_options=[],
                    check_debugger=True,
                ),
                pool=TaskPool(count=1, pool_name=PoolName("")),
                containers=[
                    TaskContainers(name=Container(""), type=ContainerType.setup),
                    TaskContainers(name=Container(""), type=ContainerType.crashes),
                    TaskContainers(name=Container(""), type=ContainerType.no_repro),
                    TaskContainers(name=Container(""), type=ContainerType.reports),
                    TaskContainers(
                        name=Container(""), type=ContainerType.unique_reports
                    ),
                ],
                tags={},
            ),
        ],
        notifications=[],
        user_fields=[
            UserField(
                name="pool_name",
                help="Execute the task on the specified pool",
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
                        op=UserFieldOperation.replace, path="/job/duration"
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
                ],
            ),
            UserField(
                name="supervisor_exe",
                help="Path to the AFL executable",
                type=UserFieldType.Str,
                default="{tools_dir}/afl-fuzz",
                locations=[
                    UserFieldLocation(
                        op=UserFieldOperation.replace,
                        path="/tasks/0/task/supervisor_exe",
                    ),
                ],
            ),
            UserField(
                name="supervisor_options",
                help="AFL command line options",
                type=UserFieldType.ListStr,
                default=[
                    "-d",
                    "-i",
                    "{input_corpus}",
                    "-o",
                    "{runtime_dir}",
                    "--",
                    "{target_exe}",
                    "{target_options}",
                ],
                locations=[
                    UserFieldLocation(
                        op=UserFieldOperation.replace,
                        path="/tasks/0/task/supervisor_options",
                    ),
                ],
            ),
            UserField(
                name="supervisor_env",
                help="Enviornment variables for AFL",
                type=UserFieldType.DictStr,
                locations=[
                    UserFieldLocation(
                        op=UserFieldOperation.replace,
                        path="/tasks/0/task/supervisor_env",
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
                name="afl_container",
                help=(
                    "Name of the AFL storage container (use "
                    "this to specify alternate builds of AFL)"
                ),
                type=UserFieldType.Str,
                default="afl-linux",
                locations=[
                    UserFieldLocation(
                        op=UserFieldOperation.replace,
                        path="/tasks/0/containers/0/name",
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
                ],
            ),
        ],
    ),
    "libfuzzer": JobTemplate(
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
                    TaskContainers(
                        name=Container(""), type=ContainerType.unique_reports
                    ),
                ],
                tags={},
            ),
            TaskConfig(
                job_id=UUID(int=0),
                prereq_tasks=[UUID(int=0)],
                task=TaskDetails(
                    type=TaskType.libfuzzer_coverage,
                    duration=1,
                    target_exe="fuzz.exe",
                    target_env={},
                    target_options=[],
                ),
                pool=TaskPool(count=1, pool_name=PoolName("")),
                containers=[
                    TaskContainers(name=Container(""), type=ContainerType.setup),
                    TaskContainers(
                        name=Container(""), type=ContainerType.readonly_inputs
                    ),
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
                    UserFieldLocation(
                        op=UserFieldOperation.replace, path="/job/duration"
                    ),
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
    ),
}
