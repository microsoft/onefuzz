#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import os
import subprocess  # nosec - used for ssh key generation
import tempfile
from typing import Tuple
from uuid import uuid4

from onefuzztypes.models import Authentication


def generate_keypair() -> Tuple[str, str]:

    with tempfile.TemporaryDirectory() as tmpdir:
        filename = os.path.join(tmpdir, "key")

        cmd = ["ssh-keygen", "-t", "rsa", "-f", filename, "-P", "", "-b", "2048"]
        subprocess.check_output(cmd)  # nosec - all arguments are under our control

        with open(filename, "r") as handle:
            private = handle.read()

        with open(filename + ".pub", "r") as handle:
            public = handle.read().strip()

    return (public, private)


def build_auth() -> Authentication:
    public_key, private_key = generate_keypair()
    auth = Authentication(
        password=str(uuid4()), public_key=public_key, private_key=private_key
    )
    return auth
