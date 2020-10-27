#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import argparse
import os
import requests

BASE_URL = "https://api.github.com/repos/microsoft/onefuzz"


class Onefuzz:
    def get_latest_version(self):
        latest_releasee = requests.get(f"{BASE_URL}/releases/latest").json()
        return (latest_releasee["id"], latest_releasee["name"])

    def list_assets(self, release_id):
        assets = requests.get(f"{BASE_URL}/releases/{release_id}/assets").json()
        artifacts = []
        for asset in assets:
            artifacts.append({"id": asset["id"], "name": asset["name"]})
        return artifacts

    def download_artifact(self, path, asset_id, asset_name):
        headers = {"Accept": "application/octet-stream"}
        asset = requests.get(
            f"{BASE_URL}/releases/assets/{asset_id}", headers=headers
        )
        with open(os.path.join(path, asset_name), "wb") as artifact:
            artifact.write(asset.content)

    def download_artifacts(self, path, artifacts):
        for artifact in artifacts:
            self.download_artifact(path, artifact["id"], artifact["name"])

    def onefuzz_release_artifacts(self, path):
        release_id, _ = self.get_latest_version()
        artifacts = self.list_assets(release_id)
        self.download_artifacts(path, artifacts)


def main():
    def dir_path(path):
        path = os.path.abspath(path)
        if not os.path.isdir(path):
            os.makedirs(path)
        return path

    parser = argparse.ArgumentParser(description="Download artifacts")
    parser.add_argument(
        "-path",
        type=dir_path,
        help="Path to download binaries",
    )
    parser.add_argument(
        "-version",
        action="store_true",
        help="Get Onefuzz latest version",
    )

    args = parser.parse_args()
    if args.path:
        Onefuzz().onefuzz_release_artifacts(args.path)

    if args.version:
        print(Onefuzz().get_latest_version()[1])


if __name__ == "__main__":
    main()
