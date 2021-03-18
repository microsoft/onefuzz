#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.
#

import json
import logging
import os
import sys
import time
from typing import Optional
from uuid import UUID, uuid4

from onefuzz.api import Command, Onefuzz
from onefuzz.cli import execute_api
from onefuzztypes.enums import OS, ContainerType, TaskState, TaskType
from onefuzztypes.models import Job, RegressionReport
from onefuzztypes.primitives import Container, Directory, File, PoolName


class Run(Command):
    def cleanup(self, test_id: UUID):
        for pool in self.onefuzz.pools.list():
            if str(test_id) in pool.name:
                self.onefuzz.pools.shutdown(pool.name, now=True)

        self.onefuzz.template.stop(
            str(test_id), "linux-libfuzzer", build=None, delete_containers=True
        )

    def _wait_for_regression_task(self, job: Job) -> None:
        while True:
            self.logger.info("waiting for regression task to finish")
            for task in self.onefuzz.jobs.tasks.list(job.job_id):
                if task.config.task.type not in [
                    TaskType.libfuzzer_regression,
                    TaskType.generic_regression,
                ]:
                    continue
                if task.state != TaskState.stopped:
                    continue
                return
            time.sleep(10)

    def _check_regression(self, job: Job) -> bool:
        # get the regression reports containers for the job
        results = self.onefuzz.jobs.containers.list(
            job.job_id, ContainerType.regression_reports
        )

        # expect one and only one regression report container
        if len(results) != 1:
            raise Exception(f"unexpected regression containers: {results}")
        container = list(results.keys())[0]

        # expect one and only one file in the container
        if len(results[container]) != 1:
            raise Exception(f"unexpected regression container output: {results}")
        file = results[container][0]

        # get the regression report
        content = self.onefuzz.containers.files.get(Container(container), file)
        as_str = content.decode()
        as_obj = json.loads(as_str)
        report = RegressionReport.parse_obj(as_obj)

        if report.crash_test_result.crash_report is not None:
            self.logger.info("regression report has crash report")
            return True

        if report.crash_test_result.no_repro is not None:
            self.logger.info("regression report has no-repro")
            return False

        raise Exception(f"unexpected report: {report}")

    def _run_job(
        self, test_id: UUID, pool: PoolName, target: str, exe: File, build: int
    ) -> Job:
        if build == 1:
            wait_for_files = [ContainerType.unique_reports]
        else:
            wait_for_files = [ContainerType.regression_reports]
        job = self.onefuzz.template.libfuzzer.basic(
            str(test_id),
            target,
            str(build),
            pool,
            target_exe=exe,
            duration=1,
            vm_count=1,
            wait_for_files=wait_for_files,
        )
        if job is None:
            raise Exception(f"invalid job: {target} {build}")

        if build > 1:
            self._wait_for_regression_task(job)
        self.onefuzz.template.stop(str(test_id), target, str(build))
        return job

    def _run(self, target_os: OS, test_id: UUID, base: Directory, target: str) -> None:
        pool = PoolName(f"{target}-{target_os.name}-{test_id}")
        self.onefuzz.pools.create(pool, target_os)
        self.onefuzz.scalesets.create(pool, 5)
        broken = File(os.path.join(base, target, "broken.exe"))
        fixed = File(os.path.join(base, target, "fixed.exe"))

        self.logger.info("starting first build")
        self._run_job(test_id, pool, target, broken, 1)

        self.logger.info("starting second build")
        job = self._run_job(test_id, pool, target, fixed, 2)
        if self._check_regression(job):
            raise Exception("fixed binary should be a no repro")

        self.logger.info("starting third build")
        job = self._run_job(test_id, pool, target, broken, 3)
        if not self._check_regression(job):
            raise Exception("broken binary should be a crash report")

        self.onefuzz.pools.shutdown(pool, now=True)

    def test(
        self,
        samples: Directory,
        *,
        endpoint: Optional[str] = None,
    ):
        test_id = uuid4()
        self.logger.info(f"launch test {test_id}")
        self.onefuzz.__setup__(endpoint=endpoint)
        error: Optional[Exception] = None
        try:
            self._run(OS.linux, test_id, samples, "linux-libfuzzer-regression")
        except Exception as err:
            error = err
        except KeyboardInterrupt:
            self.logger.warning("interruptted")
        finally:
            self.logger.info("cleaning up tests")
            self.cleanup(test_id)

        if error:
            raise error


def main() -> int:
    return execute_api(
        Run(Onefuzz(), logging.getLogger("regression")), [Command], "0.0.1"
    )


if __name__ == "__main__":
    sys.exit(main())
