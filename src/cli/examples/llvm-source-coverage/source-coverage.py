#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import argparse
import logging

from onefuzztypes.enums import ContainerType, TaskType

from onefuzz.api import Onefuzz
from onefuzz.templates import JobHelper


def main() -> None:
    parser = argparse.ArgumentParser(
        formatter_class=argparse.ArgumentDefaultsHelpFormatter
    )
    parser.add_argument("setup_dir", type=str, help="Target setup directory")
    parser.add_argument(
        "target_coverage_exe",
        type=str,
        help="Target executable within setup directory with coverage instrumentation",
    )
    parser.add_argument("project", type=str, help="Name of project")
    parser.add_argument("name", type=str, help="Name of target")
    parser.add_argument("build", type=str, help="Target build version.")
    parser.add_argument("pool_name", type=str, help="VM pool to use")
    parser.add_argument("tools", type=str, help="tools directory")
    parser.add_argument(
        "--duration", type=int, default=24, help="Hours to run the fuzzing task"
    )
    parser.add_argument("--inputs", help="seeds to use")
    args = parser.parse_args()

    of = Onefuzz()
    logging.basicConfig(level=logging.WARNING)
    of.logger.setLevel(logging.INFO)

    helper = JobHelper(
        of,
        of.logger,
        args.project,
        args.name,
        args.build,
        args.duration,
        pool_name=args.pool_name,
        target_exe=args.target_exe,
    )

    helper.define_containers(
        ContainerType.setup,
        ContainerType.analysis,
        ContainerType.inputs,
        ContainerType.tools,
    )
    helper.create_containers()
    helper.upload_setup(args.setup_dir, args.target_coverage_exe)

    if args.inputs:
        helper.upload_inputs(args.inputs)

    of.containers.files.upload_file(
        helper.containers[ContainerType.tools], f"{args.tools}/source-coverage.sh"
    )

    containers = [
        (ContainerType.setup, helper.containers[ContainerType.setup]),
        (ContainerType.analysis, helper.containers[ContainerType.analysis]),
        (ContainerType.tools, helper.containers[ContainerType.tools]),
        # note, analysis is typically for crashes, but this is analyzing inputs
        (ContainerType.crashes, helper.containers[ContainerType.inputs]),
    ]

    of.logger.info("Creating generic_analysis task")
    of.tasks.create(
        helper.job.job_id,
        TaskType.generic_analysis,
        helper.setup_relative_blob_name(args.target_coverage_exe, args.setup_dir),
        containers,
        pool_name=args.pool_name,
        duration=args.duration,
        analyzer_exe="{tools_dir}/source-coverage.sh",
        analyzer_options=["{target_exe}", "{output_dir}", "{input}"],
    )

    print(f"job:{helper.job.json(indent=4)}")


if __name__ == "__main__":
    main()
