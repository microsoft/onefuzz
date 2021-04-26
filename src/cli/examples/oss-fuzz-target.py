#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.


import logging
import os
import sys
import tempfile
from subprocess import PIPE, CalledProcessError, check_call  # nosec
from typing import List, Optional

from onefuzztypes.models import NotificationConfig
from onefuzztypes.primitives import PoolName

from onefuzz.api import Command, Onefuzz
from onefuzz.cli import execute_api

SANITIZERS = ["address", "dataflow", "memory", "undefined"]


class Ossfuzz(Command):
    def build(self, project: str, sanitizer: str) -> None:
        """Build the latest oss-fuzz target"""
        self.logger.info("building %s:%s", project, sanitizer)
        cmd = [
            "docker",
            "run",
            "--rm",
            "-ti",
            "-e",
            "SANITIZER=%s" % sanitizer,
            "--mount",
            "src=%s,target=/out,type=bind" % os.getcwd(),
            "gcr.io/oss-fuzz/%s" % project,
            "compile",
        ]

        check_call(cmd, stderr=PIPE, stdout=PIPE)

    def fuzz(
        self,
        project: str,
        build: str,
        pool: PoolName,
        sanitizers: Optional[List[str]] = None,
        notification_config: Optional[NotificationConfig] = None,
    ) -> None:
        """Build & Launch all of the libFuzzer targets for a given project"""

        if sanitizers is None:
            sanitizers = SANITIZERS

        for sanitizer in sanitizers:
            with tempfile.TemporaryDirectory() as tmpdir:
                os.chdir(tmpdir)
                try:
                    self.build(project, sanitizer)
                except CalledProcessError:
                    self.logger.warning("building %s:%s failed", project, sanitizer)
                    continue

                self.logger.info("launching %s:%s build:%s", project, sanitizer, build)
                self.onefuzz.template.ossfuzz.libfuzzer(
                    project,
                    "%s:%s" % (sanitizer, build),
                    pool,
                    max_target_count=0,
                    sync_inputs=True,
                    notification_config=notification_config,
                )

    def stop(self, project: str) -> None:
        for job in self.onefuzz.jobs.list():
            if job.config.project != project:
                continue
            if job.config.build != "base":
                continue
            self.logger.info("stopping %s: %s", job.job_id, job.state)
            self.onefuzz.jobs.delete(job.job_id)


def main() -> int:
    return execute_api(
        Ossfuzz(Onefuzz(), logging.getLogger("ossfuzz")), [Command], "0.0.1"
    )


if __name__ == "__main__":
    sys.exit(main())
