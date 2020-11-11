#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

from onefuzztypes.enums import (
    Compare,
    ContainerPermission,
    ContainerType,
    TaskFeature,
    TaskType,
)
from onefuzztypes.models import ContainerDefinition, TaskDefinition, VmDefinition

# all tasks are required to have a 'setup' container
TASK_DEFINITIONS = {
    TaskType.generic_analysis: TaskDefinition(
        features=[
            TaskFeature.target_exe,
            TaskFeature.target_options,
            TaskFeature.analyzer_exe,
            TaskFeature.analyzer_env,
            TaskFeature.analyzer_options,
        ],
        vm=VmDefinition(compare=Compare.AtLeast, value=1),
        containers=[
            ContainerDefinition(
                type=ContainerType.setup,
                compare=Compare.Equal,
                value=1,
                permissions=[ContainerPermission.Read, ContainerPermission.List],
            ),
            ContainerDefinition(
                type=ContainerType.analysis,
                compare=Compare.Equal,
                value=1,
                permissions=[
                    ContainerPermission.Write,
                    ContainerPermission.Read,
                    ContainerPermission.List,
                    ContainerPermission.Create,
                ],
            ),
            ContainerDefinition(
                type=ContainerType.crashes,
                compare=Compare.Equal,
                value=1,
                permissions=[ContainerPermission.Read, ContainerPermission.List],
            ),
            ContainerDefinition(
                type=ContainerType.tools,
                compare=Compare.Equal,
                value=1,
                permissions=[ContainerPermission.Read, ContainerPermission.List],
            ),
        ],
        monitor_queue=ContainerType.crashes,
    ),
    TaskType.libfuzzer_fuzz: TaskDefinition(
        features=[
            TaskFeature.target_exe,
            TaskFeature.target_env,
            TaskFeature.target_options,
            TaskFeature.target_workers,
            TaskFeature.ensemble_sync_delay,
        ],
        vm=VmDefinition(compare=Compare.AtLeast, value=1),
        containers=[
            ContainerDefinition(
                type=ContainerType.setup,
                compare=Compare.Equal,
                value=1,
                permissions=[ContainerPermission.Read, ContainerPermission.List],
            ),
            ContainerDefinition(
                type=ContainerType.crashes,
                compare=Compare.Equal,
                value=1,
                permissions=[ContainerPermission.Write, ContainerPermission.Create],
            ),
            ContainerDefinition(
                type=ContainerType.inputs,
                compare=Compare.Equal,
                value=1,
                permissions=[
                    ContainerPermission.Write,
                    ContainerPermission.Read,
                    ContainerPermission.List,
                    ContainerPermission.Create,
                ],
            ),
            ContainerDefinition(
                type=ContainerType.readonly_inputs,
                compare=Compare.AtLeast,
                value=0,
                permissions=[ContainerPermission.Read, ContainerPermission.List],
            ),
        ],
        monitor_queue=None,
    ),
    TaskType.libfuzzer_crash_report: TaskDefinition(
        features=[
            TaskFeature.target_exe,
            TaskFeature.target_env,
            TaskFeature.target_options,
            TaskFeature.target_timeout,
            TaskFeature.check_retry_count,
        ],
        vm=VmDefinition(compare=Compare.AtLeast, value=1),
        containers=[
            ContainerDefinition(
                type=ContainerType.setup,
                compare=Compare.Equal,
                value=1,
                permissions=[ContainerPermission.Read, ContainerPermission.List],
            ),
            ContainerDefinition(
                type=ContainerType.crashes,
                compare=Compare.Equal,
                value=1,
                permissions=[ContainerPermission.Read, ContainerPermission.List],
            ),
            ContainerDefinition(
                type=ContainerType.reports,
                compare=Compare.AtMost,
                value=1,
                permissions=[ContainerPermission.Create],
            ),
            ContainerDefinition(
                type=ContainerType.unique_reports,
                compare=Compare.AtMost,
                value=1,
                permissions=[ContainerPermission.Create],
            ),
            ContainerDefinition(
                type=ContainerType.no_repro,
                compare=Compare.AtMost,
                value=1,
                permissions=[ContainerPermission.Create],
            ),
        ],
        monitor_queue=ContainerType.crashes,
    ),
    TaskType.libfuzzer_coverage: TaskDefinition(
        features=[
            TaskFeature.target_exe,
            TaskFeature.target_env,
            TaskFeature.target_options,
        ],
        vm=VmDefinition(compare=Compare.Equal, value=1),
        containers=[
            ContainerDefinition(
                type=ContainerType.setup,
                compare=Compare.Equal,
                value=1,
                permissions=[ContainerPermission.Read, ContainerPermission.List],
            ),
            ContainerDefinition(
                type=ContainerType.readonly_inputs,
                compare=Compare.AtLeast,
                value=1,
                permissions=[ContainerPermission.Read, ContainerPermission.List],
            ),
            ContainerDefinition(
                type=ContainerType.coverage,
                compare=Compare.Equal,
                value=1,
                permissions=[
                    ContainerPermission.Create,
                    ContainerPermission.List,
                    ContainerPermission.Read,
                    ContainerPermission.Write,
                ],
            ),
        ],
        monitor_queue=ContainerType.readonly_inputs,
    ),
    TaskType.libfuzzer_merge: TaskDefinition(
        features=[
            TaskFeature.target_exe,
            TaskFeature.target_env,
            TaskFeature.target_options,
        ],
        vm=VmDefinition(compare=Compare.Equal, value=1),
        containers=[
            ContainerDefinition(
                type=ContainerType.setup,
                compare=Compare.Equal,
                value=1,
                permissions=[ContainerPermission.Read, ContainerPermission.List],
            ),
            ContainerDefinition(
                type=ContainerType.unique_inputs,
                compare=Compare.Equal,
                value=1,
                permissions=[
                    ContainerPermission.Create,
                    ContainerPermission.List,
                    ContainerPermission.Read,
                    ContainerPermission.Write,
                ],
            ),
            ContainerDefinition(
                type=ContainerType.inputs,
                compare=Compare.Equal,
                value=1,
                permissions=[ContainerPermission.Create, ContainerPermission.List],
            ),
        ],
        monitor_queue=ContainerType.inputs,
    ),
    TaskType.generic_supervisor: TaskDefinition(
        features=[
            TaskFeature.target_exe,
            TaskFeature.target_options,
            TaskFeature.supervisor_exe,
            TaskFeature.supervisor_env,
            TaskFeature.supervisor_options,
            TaskFeature.supervisor_input_marker,
            TaskFeature.wait_for_files,
            TaskFeature.stats_file,
            TaskFeature.ensemble_sync_delay,
        ],
        vm=VmDefinition(compare=Compare.AtLeast, value=1),
        containers=[
            ContainerDefinition(
                type=ContainerType.setup,
                compare=Compare.Equal,
                value=1,
                permissions=[ContainerPermission.Read, ContainerPermission.List],
            ),
            ContainerDefinition(
                type=ContainerType.tools,
                compare=Compare.Equal,
                value=1,
                permissions=[ContainerPermission.Read, ContainerPermission.List],
            ),
            ContainerDefinition(
                type=ContainerType.crashes,
                compare=Compare.Equal,
                value=1,
                permissions=[ContainerPermission.Create],
            ),
            ContainerDefinition(
                type=ContainerType.inputs,
                compare=Compare.Equal,
                value=1,
                permissions=[
                    ContainerPermission.Create,
                    ContainerPermission.Read,
                    ContainerPermission.List,
                ],
            ),
        ],
        monitor_queue=None,
    ),
    TaskType.generic_merge: TaskDefinition(
        features=[
            TaskFeature.target_exe,
            TaskFeature.target_options,
            TaskFeature.supervisor_exe,
            TaskFeature.supervisor_env,
            TaskFeature.supervisor_options,
            TaskFeature.supervisor_input_marker,
            TaskFeature.stats_file,
        ],
        vm=VmDefinition(compare=Compare.AtLeast, value=1),
        containers=[
            ContainerDefinition(
                type=ContainerType.setup,
                compare=Compare.Equal,
                value=1,
                permissions=[ContainerPermission.Read, ContainerPermission.List],
            ),
            ContainerDefinition(
                type=ContainerType.tools,
                compare=Compare.Equal,
                value=1,
                permissions=[ContainerPermission.Read, ContainerPermission.List],
            ),
            ContainerDefinition(
                type=ContainerType.readonly_inputs,
                compare=Compare.AtLeast,
                value=1,
                permissions=[ContainerPermission.Read, ContainerPermission.List],
            ),
            ContainerDefinition(
                type=ContainerType.inputs,
                compare=Compare.Equal,
                value=1,
                permissions=[ContainerPermission.Create, ContainerPermission.List],
            ),
        ],
        monitor_queue=None,
    ),
    TaskType.generic_generator: TaskDefinition(
        features=[
            TaskFeature.generator_exe,
            TaskFeature.generator_env,
            TaskFeature.generator_options,
            TaskFeature.target_exe,
            TaskFeature.target_env,
            TaskFeature.target_options,
            TaskFeature.rename_output,
            TaskFeature.target_timeout,
            TaskFeature.check_asan_log,
            TaskFeature.check_debugger,
            TaskFeature.check_retry_count,
            TaskFeature.ensemble_sync_delay,
        ],
        vm=VmDefinition(compare=Compare.AtLeast, value=1),
        containers=[
            ContainerDefinition(
                type=ContainerType.setup,
                compare=Compare.Equal,
                value=1,
                permissions=[ContainerPermission.Read, ContainerPermission.List],
            ),
            ContainerDefinition(
                type=ContainerType.tools,
                compare=Compare.Equal,
                value=1,
                permissions=[ContainerPermission.Read, ContainerPermission.List],
            ),
            ContainerDefinition(
                type=ContainerType.crashes,
                compare=Compare.Equal,
                value=1,
                permissions=[ContainerPermission.Create],
            ),
            ContainerDefinition(
                type=ContainerType.readonly_inputs,
                compare=Compare.AtLeast,
                value=1,
                permissions=[ContainerPermission.Read, ContainerPermission.List],
            ),
        ],
        monitor_queue=None,
    ),
    TaskType.generic_crash_report: TaskDefinition(
        features=[
            TaskFeature.target_exe,
            TaskFeature.target_env,
            TaskFeature.target_options,
            TaskFeature.target_timeout,
            TaskFeature.check_asan_log,
            TaskFeature.check_debugger,
            TaskFeature.check_retry_count,
        ],
        vm=VmDefinition(compare=Compare.AtLeast, value=1),
        containers=[
            ContainerDefinition(
                type=ContainerType.setup,
                compare=Compare.Equal,
                value=1,
                permissions=[ContainerPermission.Read, ContainerPermission.List],
            ),
            ContainerDefinition(
                type=ContainerType.crashes,
                compare=Compare.Equal,
                value=1,
                permissions=[ContainerPermission.Read, ContainerPermission.List],
            ),
            ContainerDefinition(
                type=ContainerType.reports,
                compare=Compare.AtMost,
                value=1,
                permissions=[ContainerPermission.Create],
            ),
            ContainerDefinition(
                type=ContainerType.unique_reports,
                compare=Compare.AtMost,
                value=1,
                permissions=[ContainerPermission.Create],
            ),
            ContainerDefinition(
                type=ContainerType.no_repro,
                compare=Compare.AtMost,
                value=1,
                permissions=[ContainerPermission.Create],
            ),
        ],
        monitor_queue=ContainerType.crashes,
    ),
}
