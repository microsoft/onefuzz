#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import json
from subprocess import check_call

from onefuzz.api import Onefuzz


def main() -> None:
    of = Onefuzz()
    containers = [x.name for x in of.containers.list()]
    for entry in of.notifications.list():
        container = entry.container
        if container not in containers:
            continue
        files = of.containers.files.list(container).files
        assert len(files), "missing files in report container: %s" % container
        assert files[0].endswith(".json"), "not .json extension: %s" % files[0]
        data = json.loads(of.containers.files.get(container, files[0]))
        print("checking", data["task_id"])
        check_call(["python", "check-ado.py", "--title", data["task_id"]])


if __name__ == "__main__":
    main()
