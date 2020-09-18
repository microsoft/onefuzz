#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

from onefuzz.api import Onefuzz


def main() -> None:
    onefuzz = Onefuzz()
    jobs = onefuzz.jobs.list()
    for job in jobs:
        print(
            "job:",
            str(job.job_id)[:8],
            ":".join([job.config.project, job.config.name, job.config.build]),
        )
        for task in onefuzz.tasks.list(job_id=job.job_id):
            if task.state in ["stopped", "stopping"]:
                continue
            print(
                "    ",
                str(task.task_id)[:8],
                task.config.task.type,
                task.config.task.target_exe,
            )


if __name__ == "__main__":
    main()
