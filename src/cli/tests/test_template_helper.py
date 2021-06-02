#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import logging
import sys
import unittest
from uuid import UUID

from onefuzztypes.enums import JobState
from onefuzztypes.models import Job, JobConfig
from onefuzztypes.primitives import Directory, File

from onefuzz.api import Onefuzz
from onefuzz.templates import JobHelper


class TestHelper(unittest.TestCase):
    def test_path_resolution(self) -> None:
        helper = JobHelper(
            Onefuzz(),
            logging.getLogger(),
            "a",
            "b",
            "c",
            False,
            target_exe=File("README.md"),
            job=Job(
                job_id=UUID("0" * 32),
                state=JobState.init,
                config=JobConfig(project="a", name="a", build="a", duration=1),
            ),
        )
        values = {
            (File("filename.txt"), None): "filename.txt",
            (File("dir/filename.txt"), None): "filename.txt",
            (File("./filename.txt"), None): "filename.txt",
            (File("./filename.txt"), Directory(".")): "filename.txt",
            (File("dir/filename.txt"), Directory("dir")): "filename.txt",
            (File("dir/filename.txt"), Directory("dir/")): "filename.txt",
            (File("dir/filename.txt"), Directory("./dir")): "filename.txt",
            (File("./dir/filename.txt"), Directory("./dir/")): "filename.txt",
        }

        expected = "filename.txt"
        if sys.platform == "linux":
            filename = File("/unused/filename.txt")
            values[(filename, None)] = expected
            values[(filename, Directory("/unused"))] = expected
            values[(filename, Directory("/unused/"))] = expected

        if sys.platform == "windows":
            for filename in [
                File("c:/unused/filename.txt"),
                File("c:\\unused/filename.txt"),
                File("c:\\unused\\filename.txt"),
            ]:
                values[(filename, None)] = expected
                values[(filename, Directory("c:/unused"))] = expected
                values[(filename, Directory("c:/unused/"))] = expected
                values[(filename, Directory("c:\\unused\\"))] = expected
                values[(filename, Directory("c:\\unused\\"))] = expected

        for (args, expected) in values.items():
            self.assertEqual(helper.setup_relative_blob_name(*args), expected)

        with self.assertRaises(ValueError):
            helper.setup_relative_blob_name(
                File("dir/filename.txt"), Directory("other_dir")
            )


if __name__ == "__main__":
    unittest.main()
