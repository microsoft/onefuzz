#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import logging
import os
import platform
import subprocess  # nosec
import tempfile
from asyncio.subprocess import PIPE
from contextlib import contextmanager
from typing import Generator, Optional


def get_local_tmp() -> Optional[str]:
    # Use $env:LocalAppData on Windows if it's available, as $env:TEMP
    # can frequently have permissions unacceptable for SSH keys

    if platform.system() != "Windows":
        return None

    local_app_data = os.getenv("LOCALAPPDATA")
    if not local_app_data:
        return None

    local_tmp = os.path.join(local_app_data, "temp")
    if os.path.exists(local_tmp):
        return local_tmp

    return None


@contextmanager
def temp_file(
    filename: str, content: str, *, set_owner_only: bool = False
) -> Generator:
    with tempfile.TemporaryDirectory(dir=get_local_tmp()) as tmpdir:
        full_path = os.path.join(tmpdir, filename)

        logging.debug("creating file %s", full_path)
        with open(full_path, "w") as handle:
            handle.write(content)

        if set_owner_only and platform.system() != "Windows":
            # security note: full_path is created via callers using known static
            # filenames within the newly created temporary file name
            subprocess.check_call(["chmod", "600", full_path])  # nosec

        yield full_path

        logging.debug("cleaning up file %s", full_path)


@contextmanager
def build_ssh_command(
    ip: str,
    private_key: str,
    *,
    proxy: Optional[str] = None,
    port: Optional[int] = None,
    command: Optional[str] = None,
) -> Generator:
    with temp_file("id_rsa", private_key, set_owner_only=True) as ssh_key:
        cmd = [
            "ssh",
            "onefuzz@%s" % ip,
            "-i",
            ssh_key,
            "-o",
            "UserKnownHostsFile=/dev/null",
            "-o",
            "StrictHostKeyChecking=no",
        ]

        if proxy:
            cmd += ["-L", proxy]
        if port:
            cmd += ["-p", str(port)]

        log_level = logging.getLogger("backend").getEffectiveLevel()
        if log_level <= logging.DEBUG:
            cmd += ["-v"]

        if command:
            cmd += [command]

        yield cmd


@contextmanager
def ssh_connect(
    ip: str,
    private_key: str,
    *,
    proxy: Optional[str] = None,
    call: bool = False,
    port: Optional[int] = None,
    command: Optional[str] = None,
) -> Generator:
    with build_ssh_command(
        ip, private_key, proxy=proxy, port=port, command=command
    ) as cmd:
        logging.info("launching ssh: %s", " ".join(cmd))

        if call:
            # security note: command includes user provided arguments
            # intentionally
            yield subprocess.call(cmd)  # nosec
            return

        # security note: command includes user provided arguments
        # intentionally
        with subprocess.Popen(  # nosec
            cmd, stdin=PIPE, stdout=PIPE, stderr=PIPE, bufsize=0
        ) as ssh:
            yield ssh
            ssh.kill()
