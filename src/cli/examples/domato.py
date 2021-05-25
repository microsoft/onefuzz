#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import argparse
import json
import logging
import os
import shutil
import subprocess  # nosec
import sys
import tempfile
from typing import Optional

from onefuzztypes.enums import OS, ContainerType, TaskType
from onefuzztypes.models import NotificationConfig
from onefuzztypes.primitives import Container

from onefuzz.api import Onefuzz
from onefuzz.templates import JobHelper

AZCOPY_PATH = os.environ.get("AZCOPY") or shutil.which("azcopy")
if not AZCOPY_PATH:
    raise Exception("unable to find 'azcopy' in path or AZCOPY environment variable")

FUZZER_NAME = "domato"
FUZZER_URL = "https://github.com/googleprojectzero/domato.git"

FUZZER_NAME = "domato"
FUZZER_URL = "https://github.com/googleprojectzero/domato.git"

PY_URL = "https://www.python.org/ftp/python/3.7.8/python-3.7.8-amd64.exe"
PY_INSTALLER_ARGS = "/passive InstallAllUsers=1 AssociateFiles=1 PrependPath=1"
PY_INSTALLER = """
Invoke-WebRequest -Uri "%s" -OutFile "c:/python-install.exe"
Start-Process "C:\\python-install.exe"  -ArgumentList "%s" -Wait
""" % (
    PY_URL,
    PY_INSTALLER_ARGS,
)


def add_setup_script(of: Onefuzz, container: Container) -> None:
    with tempfile.TemporaryDirectory() as tmpdir:
        setup = os.path.join(tmpdir, "setup.ps1")
        with open(setup, "w") as handle:
            handle.write(PY_INSTALLER)
        of.containers.files.upload_file(container, setup)


def upload_to_fuzzer_container(of: Onefuzz, fuzzer_name: str, fuzzer_url: str) -> None:
    fuzzer_sas = of.containers.create(Container(fuzzer_name)).sas_url
    with tempfile.TemporaryDirectory() as tmpdir:
        command = ["git", "clone", "--depth", "1", fuzzer_url, tmpdir]
        of.logger.info("Dowloading fuzzer '%s' ...", fuzzer_name)
        subprocess.check_call(command)
        if AZCOPY_PATH is None:
            raise Exception("missing azcopy")
        command = [AZCOPY_PATH, "sync", tmpdir, fuzzer_sas]
        of.logger.info(
            "Uploading fuzzer '%s' to OneFuzz: %s",
            fuzzer_name,
            " ".join(command),
        )
        subprocess.check_call(command)


def upload_to_setup_container(of: Onefuzz, helper: JobHelper, setup_dir: str) -> None:
    setup_sas = of.containers.get(helper.containers[ContainerType.setup]).sas_url
    if AZCOPY_PATH is None:
        raise Exception("missing azcopy")
    command = [AZCOPY_PATH, "sync", setup_dir, setup_sas]
    of.logger.info("Uploading '%s' to OneFuzz: %s", setup_dir, " ".join(command))
    subprocess.check_call(command)


def main() -> None:
    parser = argparse.ArgumentParser(
        formatter_class=argparse.ArgumentDefaultsHelpFormatter
    )
    parser.add_argument("setup_dir", type=str, help="Target setup directory")
    parser.add_argument(
        "target_exe", type=str, help="Target executable within setup directory"
    )
    parser.add_argument("build", type=str, help="Target build version.")
    parser.add_argument("pool_name", type=str, help="Worker pool")
    parser.add_argument("--project", type=str, default="msedge", help="Name of project")
    parser.add_argument("--name", type=str, default="browser", help="Name of target")
    parser.add_argument(
        "--duration", type=int, default=1, help="Hours to run the fuzzing task"
    )
    parser.add_argument("--vm_sku", default="Standard_DS1_v2", help="VM image to use")
    parser.add_argument("--notification_config", help="Notification configuration")
    parser.add_argument(
        "--platform",
        type=OS,
        help="Specify Platform. Possible values: %s" % ", ".join([x.name for x in OS]),
    )
    args = parser.parse_args()

    notification_config: Optional[NotificationConfig] = None
    if args.notification_config:
        with open(args.notification_config) as handle:
            notification_config = NotificationConfig.parse_obj(json.load(handle))

    of = Onefuzz()
    logging.basicConfig(level=logging.WARNING)
    of.logger.setLevel(logging.INFO)

    if not os.path.exists(args.target_exe):
        logging.warning(
            "target (%s) does not exist.  Unless this is "
            "downloaded via in-VM setup script, fuzzing will fail",
            args.target_exe,
        )

        if args.platform is None:
            logging.error("Without target exe, platform must be set")
            sys.exit(1)

    helper = JobHelper(
        of,
        of.logger,
        args.project,
        args.name,
        args.build,
        args.duration,
        pool_name=args.pool_name,
        target_exe=args.target_exe,
        platform=args.platform,
    )

    upload_to_fuzzer_container(of, FUZZER_NAME, FUZZER_URL)

    helper.define_containers(
        ContainerType.setup,
        ContainerType.readonly_inputs,
        ContainerType.crashes,
        ContainerType.unique_reports,
        ContainerType.reports,
    )
    helper.create_containers()
    helper.setup_notifications(notification_config)
    upload_to_setup_container(of, helper, args.setup_dir)
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
        (ContainerType.tools, Container(FUZZER_NAME)),
        (ContainerType.setup, helper.containers[ContainerType.setup]),
        (ContainerType.crashes, helper.containers[ContainerType.crashes]),
        (
            ContainerType.readonly_inputs,
            helper.containers[ContainerType.readonly_inputs],
        ),
    ]

    of.logger.info("Creating generic_generator")

    target_command = [
        "--no-sandbox",
        "--no-first-run",
        "--no-default-browser-check",
        "--allow-file-access-from-files",
        "--disable-popup-blocking",
        "--enable-logging=stderr",
        "--js-flags='--expose_gc'",
        "--window-size=1024,1024",
        "--enable-webgl-draft-extensions",
        "--enable-experimental-web-platform-features",
        "--enable-experimental-canvas-features",
        "--user-data-dir=c:\\msedge-data",
        "{input}",
    ]
    fuzzer_generator = "python.exe"
    fuzzer_generator_options = [
        "{tools_dir}/generator.py",
        "--output_dir",
        "{generated_inputs}",
        "--no_of_files",
        "1000",
    ]

    of.tasks.create(
        helper.job.job_id,
        TaskType.generic_generator,
        helper.setup_relative_blob_name(args.target_exe, args.setup_dir),
        containers,
        pool_name=args.pool_name,
        target_options=target_command,
        duration=args.duration,
        generator_exe=fuzzer_generator,
        generator_options=fuzzer_generator_options,
        reboot_after_setup=True,
    )


if __name__ == "__main__":
    main()
