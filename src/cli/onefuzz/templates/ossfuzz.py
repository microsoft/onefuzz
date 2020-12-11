#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import configparser
import glob
import os
import subprocess  # nosec
from multiprocessing.pool import ThreadPool
from typing import Dict, List, Optional, Tuple

from onefuzztypes.enums import OS, ContainerType, TaskDebugFlag
from onefuzztypes.models import NotificationConfig
from onefuzztypes.primitives import File, Directory

from onefuzz.api import Command
from onefuzz.backend import container_file_path

from . import JobHelper

VM_SKU = "Standard_D2s_v3"
VM_COUNT = 1


class OssFuzz(Command):
    """ OssFuzz style jobs """

    @classmethod
    def _read_options(
        _cls, filename: Optional[File]
    ) -> Tuple[Dict[str, str], List[str]]:
        target_env: Dict[str, str] = {}
        target_options: List[str] = []
        asan_options: List[str] = []

        if filename is not None and os.path.exists(filename):
            config = configparser.ConfigParser()
            with open(filename, "r") as handle:
                config.read_file(handle)

                if config.has_section("asan"):
                    for arg, value in config.items("asan"):
                        asan_options.append("%s=%s" % (arg, value))

                if config.has_section("env"):
                    target_env.update({x[0].upper(): x[1] for x in config.items("env")})

                if config.has_section("libfuzzer"):
                    for arg, value in config.items("libfuzzer"):
                        if arg == "dict":
                            value = "setup/%s" % value
                        target_options.append("-%s=%s" % (arg, value))

        if asan_options:
            asan = ":".join(asan_options)
            if "ASAN_OPTIONS" in target_env:
                target_env["ASAN_OPTIONS"] += ":" + asan
            else:
                target_env["ASAN_OPTIONS"] = asan

        return target_env, target_options

    def _options(
        self, filename: File, base_options: Optional[File]
    ) -> Tuple[Dict[str, str], List[str]]:
        base_env, base_options = self._read_options(base_options)
        target_env, target_options = self._read_options(filename)

        if "ASAN_OPTIONS" in target_env and "ASAN_OPTIONS" in base_env:
            base_env["ASAN_OPTIONS"] = (
                target_env["ASAN_OPTIONS"] + ":" + base_env["ASAN_OPTIONS"]
            )
            del target_env["ASAN_OPTIONS"]

        base_env.update(target_env)
        base_options += target_options
        return base_env, base_options

    @classmethod
    def _owners(_cls, filename: File) -> Dict[str, str]:
        tags = {}

        if os.path.exists(filename):
            with open(filename, "r") as handle:
                owners = handle.read()
            owner, ado_path = owners.split(",")
            tags["EMAIL"] = owner
            tags["ADO_PATH"] = ado_path

        return tags

    def _copy_all(self, src_sas: str, dst_sas: str) -> None:
        cmd = [
            "azcopy",
            "sync",
            src_sas,
            dst_sas,
        ]
        self.logger.info("copying base setup")
        subprocess.check_output(cmd)

    def _copy_exe(self, src_sas: str, dst_sas: str, target_exe: File) -> None:
        files: List[File] = [target_exe]
        pdb_path = os.path.splitext(target_exe)[0] + ".pdb"
        if os.path.exists(str(pdb_path)):
            files.append(File(pdb_path))

        for path in files:
            filename = os.path.basename(path)
            src_url = container_file_path(src_sas, filename)
            dst_url = container_file_path(dst_sas, filename)

            cmd = [
                "azcopy",
                "copy",
                src_url,
                dst_url,
            ]
            self.logger.info("uploading %s", path)
            subprocess.check_output(cmd)

    def _launch_it(
        self,
        *,
        fuzzer: File,
        pool_name: str,
        project: str,
        build: str,
        platform: OS,
        tags: Optional[Dict[str, str]],
        duration: int,
        sync_inputs: bool,
        debug: Optional[List[TaskDebugFlag]],
        ensemble_sync_delay: Optional[int],
        notification_config: Optional[NotificationConfig],
        base_setup: Optional[Directory],
        base_options: Optional[File],
    ) -> None:
        fuzzer_name = fuzzer.replace(".exe", "").replace("_fuzzer", "")
        self.logger.info("creating tasks for %s", fuzzer)
        self.onefuzz.template.libfuzzer._check_is_libfuzzer(fuzzer)
        helper = JobHelper(
            self.onefuzz,
            self.logger,
            project,
            fuzzer_name,
            build,
            duration,
            pool_name=pool_name,
            target_exe=fuzzer,
        )
        helper.add_tags(tags)
        helper.platform = platform
        helper.define_containers(
            ContainerType.setup,
            ContainerType.inputs,
            ContainerType.crashes,
            ContainerType.reports,
            ContainerType.unique_reports,
            ContainerType.no_repro,
            ContainerType.coverage,
        )
        helper.create_containers()
        helper.setup_notifications(notification_config)

        dst_sas = self.onefuzz.containers.get(
            helper.containers[ContainerType.setup]
        ).sas_url

        if base_setup:
            self._copy_all(base_setup, dst_sas)

        zip_name = fuzzer.replace(".exe", "").replace("_fuzzer", "_seed_corpus.zip")

        if os.path.exists(zip_name) and sync_inputs:
            self.logger.info("uploading seeds")
            helper.upload_inputs_zip(File(zip_name))

        owners_path = File("%s.msowners" % fuzzer.replace(".exe", ""))
        options_path = File("%s.options" % fuzzer.replace(".exe", ""))

        target_env, target_options = self._options(base_options, options_path)
        helper.add_tags(self._owners(owners_path))

        # All fuzzers are copied to the setup container root.
        #
        # Cast because `glob()` returns `str`.
        fuzzer_blob_name = helper.target_exe_blob_name(fuzzer, None)

        self.onefuzz.template.libfuzzer._create_tasks(
            job=helper.job,
            containers=helper.containers,
            pool_name=pool_name,
            target_exe=fuzzer_blob_name,
            vm_count=VM_COUNT,
            duration=duration,
            target_options=target_options,
            target_env=target_env,
            tags=helper.tags,
            debug=debug,
            ensemble_sync_delay=ensemble_sync_delay,
            enable_coverage=False,
        )
        self.onefuzz.logger.info("started: %s", helper.job.job_id)

    def libfuzzer(
        self,
        project: str,
        build: str,
        pool_name: str,
        duration: int = 24,
        tags: Optional[Dict[str, str]] = None,
        max_target_count: int = 20,
        sync_inputs: bool = False,
        notification_config: Optional[NotificationConfig] = None,
        debug: Optional[List[TaskDebugFlag]] = None,
        ensemble_sync_delay: Optional[int] = None,
        include_fuzzers: Optional[List[str]] = None,
        base_options: Optional[File] = None,
        base_setup: Optional[Directory] = None,
    ) -> None:
        """
        OssFuzz style libfuzzer jobs

        :param bool ensemble_sync_delay: Specify duration between
            syncing inputs during ensemble fuzzing (0 to disable).
        """

        fuzzers = sorted(glob.glob("*fuzzer"))
        if fuzzers:
            platform = OS.linux
        else:
            platform = OS.windows
            fuzzers = sorted(glob.glob("*fuzzer.exe"))

        if include_fuzzers:
            assert len(include_fuzzers)
            fuzzers = [x for x in fuzzers if x in include_fuzzers]

        if max_target_count:
            fuzzers = fuzzers[:max_target_count]

        todo = []

        for fuzzer in [File(x) for x in fuzzers]:
            kwargs = {
                "fuzzer": fuzzer,
                "project": project,
                "build": build,
                "tags": tags,
                "platform": platform,
                "ensemble_sync_delay": ensemble_sync_delay,
                "sync_inputs": sync_inputs,
                "debug": debug,
                "pool_name": pool_name,
                "duration": duration,
                "notification_config": notification_config,
                "base_setup": base_setup,
                "base_options": base_options,
            }
            todo.append(kwargs)

        with ThreadPool(processes=30) as pool:
            pool.map(lambda x: self._launch_it(**x), todo)
