#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import argparse
import json
import logging
import os
import tempfile
from typing import Optional

from onefuzztypes.enums import ContainerType, TaskType
from onefuzztypes.models import NotificationConfig
from onefuzztypes.primitives import Container

from onefuzz.api import Onefuzz
from onefuzz.templates import JobHelper

SETUP = """
cd /onefuzz
apt-get update
apt-get -y install build-essential libbfd-dev libunwind-dev
git clone https://github.com/google/honggfuzz/
cd /onefuzz/honggfuzz
make
"""


def add_setup_script(of: Onefuzz, container: Container) -> None:
    with tempfile.TemporaryDirectory() as tmpdir:
        setup = os.path.join(tmpdir, "setup.sh")
        with open(setup, "w") as handle:
            handle.write(SETUP)
        of.containers.files.upload_file(container, setup)


def main() -> None:
    parser = argparse.ArgumentParser(
        formatter_class=argparse.ArgumentDefaultsHelpFormatter
    )
    parser.add_argument("setup_dir", type=str, help="Target setup directory")
    parser.add_argument(
        "target_exe", type=str, help="Target executable within setup directory"
    )
    parser.add_argument("project", type=str, help="Name of project")
    parser.add_argument("name", type=str, help="Name of target")
    parser.add_argument("build", type=str, help="Target build version.")
    parser.add_argument("pool_name", type=str, help="VM pool to use")
    parser.add_argument(
        "--duration", type=int, default=1, help="Hours to run the fuzzing task"
    )
    parser.add_argument("--inputs", help="seeds to use")
    parser.add_argument("--notification_config", help="Notification configuration")
    args = parser.parse_args()

    notification_config: Optional[NotificationConfig] = None
    if args.notification_config:
        with open(args.notification_config) as handle:
            notification_config = NotificationConfig.parse_obj(json.load(handle))

    of = Onefuzz()
    logging.basicConfig(level=logging.WARNING)
    of.logger.setLevel(logging.DEBUG)

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
        ContainerType.readonly_inputs,
        ContainerType.crashes,
        ContainerType.inputs,
        ContainerType.reports,
        ContainerType.unique_reports,
    )
    helper.create_containers()
    helper.setup_notifications(notification_config)
    helper.upload_setup(args.setup_dir, args.target_exe)
    if args.inputs:
        helper.upload_inputs(args.inputs)

    add_setup_script(of, helper.containers[ContainerType.setup])

    containers = [
        (ContainerType.setup, helper.containers[ContainerType.setup]),
        (ContainerType.crashes, helper.containers[ContainerType.crashes]),
        (ContainerType.reports, helper.containers[ContainerType.reports]),
        (ContainerType.unique_reports, helper.containers[ContainerType.unique_reports]),
    ]

    of.logger.info("Creating generic_crash_report task")
    of.tasks.create(
        helper.job.job_id,
        TaskType.generic_crash_report,
        helper.setup_relative_blob_name(args.target_exe, args.setup_dir),
        containers,
        pool_name=args.pool_name,
        duration=args.duration,
    )

    containers = [
        (ContainerType.tools, Container("honggfuzz")),
        (ContainerType.setup, helper.containers[ContainerType.setup]),
        (ContainerType.crashes, helper.containers[ContainerType.crashes]),
        (
            ContainerType.inputs,
            helper.containers[ContainerType.inputs],
        ),
    ]

    supervisor_options = [
        "-n1",
        "--crashdir",
        "{crashes}",
        "-u",
        "-i",
        "{input_corpus}",
        "--",
        "{target_exe}",
        "{input}",
    ]

    of.tasks.create(
        helper.job.job_id,
        TaskType.generic_supervisor,
        helper.setup_relative_blob_name(args.target_exe, args.setup_dir),
        containers,
        pool_name=args.pool_name,
        supervisor_exe="/onefuzz/honggfuzz/honggfuzz",
        supervisor_options=supervisor_options,
        supervisor_input_marker="___FILE___",
        duration=args.duration,
        vm_count=1,
        tags=helper.tags,
    )


if __name__ == "__main__":
    main()
