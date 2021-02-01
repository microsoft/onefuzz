#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import logging
import os
import subprocess  # nosec
import tempfile
from contextlib import contextmanager
from typing import Generator


@contextmanager
def rdp_connect(ip: str, password: str, *, port: int) -> Generator:
    FILENAME = "onefuzz.rdp"
    with tempfile.TemporaryDirectory() as tmpdir:
        rdp_file = os.path.join(tmpdir, FILENAME)

        logging.info("encrypting password")

        script = (
            'ConvertTo-SecureString -AsPlainText -Force "%s" | ConvertFrom-SecureString'
            % password
        )

        encrypted = (
            # security note: script includes content coming from the server,
            # which is trusted in this context.
            subprocess.check_output(  # nosec
                ["powershell.exe", "-ExecutionPolicy", "Unrestricted", script]
            )
            .decode()
            .strip()
        )

        content = [
            "full address:s:%s:%d" % (ip, port),
            "username:s:onefuzz",
            "password 51:b:%s" % encrypted,
            "administrative session:i:1",
        ]

        with open(rdp_file, "w") as handle:
            handle.write("\r\n".join(content))

        previous = os.getcwd()
        os.chdir(tmpdir)
        cmd = ["mstsc.exe", FILENAME]
        logging.info("launching rdp: %s", " ".join(cmd))
        # security note: mstsc.exe is called using a config with data coming
        # from the server, which is trusted in this context.
        yield subprocess.call(cmd)  # nosec
        os.chdir(previous)
