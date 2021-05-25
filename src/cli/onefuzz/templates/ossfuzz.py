#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import configparser
import glob
import os
import subprocess  # nosec
from typing import Dict, List, Optional, Tuple

from onefuzztypes.enums import OS, ContainerType, TaskDebugFlag
from onefuzztypes.models import NotificationConfig
from onefuzztypes.primitives import File, PoolName

from onefuzz.api import Command
from onefuzz.backend import container_file_path

from . import JobHelper

VM_SKU = "Standard_D2s_v3"
VM_COUNT = 1


class OssFuzz(Command):
    """OssFuzz style jobs"""

    def _containers(self, project: str, build: str, platform: OS) -> Dict[str, str]:
        guid = self.onefuzz.utils.namespaced_guid(
            project, build=build, platform=platform.name
        ).hex

        names = {
            "build": "oss-build-%s" % guid,
            "base": "oss-base-%s" % guid,
        }
        return names

    @classmethod
    def _options(_cls, filename: File) -> Tuple[Dict[str, str], List[str]]:
        target_env: Dict[str, str] = {}
        target_options: List[str] = []

        if not os.path.exists(filename):
            return target_env, target_options

        config = configparser.ConfigParser()
        with open(filename, "r") as handle:
            config.read_file(handle)

            if config.has_section("env"):
                target_env.update({x[0].upper(): x[1] for x in config.items("env")})

            if config.has_section("libfuzzer"):
                for arg, value in config.items("libfuzzer"):
                    if arg == "dict":
                        value = "setup/%s" % value
                    target_options.append("-%s=%s" % (arg, value))

        return target_env, target_options

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
        # security note: the source and destination container sas URLS are
        # considerd trusted from the service
        subprocess.check_output(cmd)  # nosec

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
            # security note: the source and destination container sas URLS
            # are considerd trusted from the service
            subprocess.check_output(cmd)  # nosec

    def libfuzzer(
        self,
        project: str,
        build: str,
        pool_name: PoolName,
        duration: int = 24,
        tags: Optional[Dict[str, str]] = None,
        dryrun: bool = False,
        max_target_count: int = 20,
        sync_inputs: bool = False,
        notification_config: Optional[NotificationConfig] = None,
        debug: Optional[List[TaskDebugFlag]] = None,
        ensemble_sync_delay: Optional[int] = None,
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

        if dryrun:
            return

        containers = self._containers(project, build, platform)
        container_sas = {}
        for name in containers:
            self.logger.info("creating container: %s", name)
            sas_url = self.onefuzz.containers.create(containers[name]).sas_url
            container_sas[name] = sas_url

        self.logger.info("uploading build artifacts")

        # security note: the container sas is considered trusted from the
        # service
        subprocess.check_output(  # nosec
            [
                "azcopy",
                "sync",
                ".",
                container_sas["build"],
                "--exclude-pattern",
                "*fuzzer_seed_corpus.zip",
            ]
        )
        subprocess.check_output(  # nosec
            [
                "azcopy",
                "sync",
                ".",
                container_sas["base"],
                '--include-pattern="*.so;*.dll;*.sh;*.ps1',
            ]
        )

        if max_target_count:
            fuzzers = fuzzers[:max_target_count]

        base_helper = JobHelper(
            self.onefuzz,
            self.logger,
            project,
            build,
            "base",
            duration,
            pool_name=pool_name,
            target_exe=File(fuzzers[0]),
        )
        base_helper.platform = platform

        helpers = []
        for fuzzer in [File(x) for x in fuzzers]:
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
                job=base_helper.job,
                pool_name=pool_name,
                target_exe=fuzzer,
            )
            helper.platform = platform
            helper.add_tags(tags)
            helper.platform = base_helper.platform
            helper.job = base_helper.job
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
            self._copy_exe(container_sas["build"], dst_sas, File(fuzzer))
            self._copy_all(container_sas["base"], dst_sas)

            zip_name = fuzzer.replace(".exe", "").replace("_fuzzer", "_seed_corpus.zip")

            if os.path.exists(zip_name) and sync_inputs:
                self.logger.info("uploading seeds")
                helper.upload_inputs_zip(File(zip_name))

            owners_path = File("%s.msowners" % fuzzer.replace(".exe", ""))
            options_path = File("%s.options" % fuzzer.replace(".exe", ""))

            target_env, target_options = self._options(options_path)
            helper.add_tags(self._owners(owners_path))

            # All fuzzers are copied to the setup container root.
            #
            # Cast because `glob()` returns `str`.
            fuzzer_blob_name = helper.setup_relative_blob_name(fuzzer, None)

            self.onefuzz.template.libfuzzer._create_tasks(
                job=base_helper.job,
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
            )
            helpers.append(helper)
        base_helper.wait()
