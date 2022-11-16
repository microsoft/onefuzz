# This script is a helper to deploy a scale set of unmanaged machines

# deploy the unamanged template
# optionally upload the deployment files
# the script should reference the deployment files

import argparse
import datetime
import json
import logging
import os
import subprocess
import sys
import tempfile
import uuid
import zipfile
from typing import Any, List, Optional, cast

from azure.common.credentials import get_cli_profile
from azure.core.exceptions import (
    HttpResponseError,  # ResourceNotFoundError,
    ResourceExistsError,
)
from azure.identity import AzureCliCredential
from azure.mgmt.resource import ResourceManagementClient
from azure.mgmt.resource.resources.models import (
    Deployment,
    DeploymentMode,
    DeploymentProperties,
)
from azure.mgmt.storage import StorageManagementClient
from azure.mgmt.storage.models import (
    AccessTier,
    Kind,
    Sku,
    SkuName,
    StorageAccountCreateParameters,
)
from azure.storage.blob import (
    BlobServiceClient,
    ContainerClient,
    generate_container_sas,
    ContainerSasPermissions,
)
from onefuzz.api import Onefuzz
from onefuzz.azcopy import azcopy_copy
from onefuzztypes.primitives import PoolName

logger = logging.getLogger("deploy")


def get_template(path: str) -> Any:
    json_template = subprocess.check_output(  # nosec
        ["az", "bicep", "build", "--file", path, "--stdout"], shell=True
    )
    return json.loads(json_template)


class Deployer:
    arm_template = "scaleset_template_windows.bicep"

    def __init__(
        self,
        resource_group: str,
        location: str,
        pool_name: str,
        subscription_id: Optional[str],
        arm_template: str = "scaleset_template_windows.bicep",
        scaleset_size: int = 1,
        os: str = "win64",
    ):
        self.arm_template = arm_template
        self.resource_group = resource_group
        self.location = location
        self.scaleset_size = scaleset_size
        self.storage_account = f"{self.resource_group}sa".replace("-", "").replace(
            "_", ""
        )
        self.os = os
        self.onefuzz = Onefuzz()
        self.pool_name = pool_name

        if subscription_id:
            self.subscription_id = subscription_id
        else:
            profile = get_cli_profile()
            self.subscription_id = cast(str, profile.get_subscription_id())

        pass

    def deploy(self) -> None:
        logger.info("deploying")
        template = get_template(self.arm_template)
        logger.info("deploying 2")
        credential = AzureCliCredential()
        client = ResourceManagementClient(
            credential, subscription_id=self.subscription_id
        )
        logger.info("deploying 3")

        # client.resource_groups.get(self.resource_group)

        storageClient = StorageManagementClient(
            credential, subscription_id=self.subscription_id
        )
        try:
            logger.info("checking for storage account")
            prop = storageClient.storage_accounts.get_properties(
                self.resource_group, self.storage_account
            )
            logger.info("storage account exists")
        except HttpResponseError as e:
            logger.info("storage account does not exist creating a new one")
            params = StorageAccountCreateParameters(
                sku=Sku(name=SkuName.PREMIUM_LRS),
                kind=Kind.block_blob_storage,
                location=self.location,
                access_tier=AccessTier.hot,
                allow_blob_public_access=False,
                minimum_tls_version="TLS1_2",
            )
            r = storageClient.storage_accounts.begin_create(
                self.resource_group, self.storage_account, params
            ).result()
            logger.info("storage account created")

        file_uris = self.upload_tools(storageClient)

        logger.info("deploying scaleset")
        client.resource_groups.create_or_update(
            self.resource_group, {"location": self.location}
        )

        deploy_params = {
            "scaleset_name": {"value": self.resource_group},
            "location": {"value": self.location},
            "networkSecurityGroups_name": {"value": "nsg"},
            "adminUsername": {"value": "onefuzz"},
            "capacity": {"value": self.scaleset_size},
            "adminPassword": {"value": str(uuid.uuid4())},
            "storageAccountName": {"value": "self.storage_account"},
        }

        deployment = Deployment(
            properties=DeploymentProperties(
                mode=DeploymentMode.incremental,
                template=template,
                parameters=deploy_params,
            )
        )

        result = client.deployments.begin_create_or_update(
            self.resource_group, str(uuid.uuid4()), deployment
        ).result()

        if result.properties.provisioning_state != "Succeeded":
            logger.error(
                "error deploying: %s",
                json.dumps(result.as_dict(), indent=4, sort_keys=True),
            )
            sys.exit(1)

    def upload_tools(self, storageClient: StorageManagementClient):
        logger.info("downloading tools")

        with tempfile.TemporaryDirectory() as tmpDir:
            zip_path = os.path.join(tmpDir, "tools.zip")
            extracted_path = os.path.join(tmpDir, "tools")
            os.makedirs(extracted_path, exist_ok=True)
            self.onefuzz.tools.get(tmpDir)
            config = self.onefuzz.pools.get_config(PoolName(self.pool_name))

            with open(os.path.join(extracted_path, "config.json"), "w") as text_file:
                text_file.write(config.json())

            # extract zip
            with zipfile.ZipFile(zip_path, "r") as zip_ref:
                zip_ref.extractall(extracted_path)
            account_key = (
                storageClient.storage_accounts.list_keys(
                    self.resource_group, self.storage_account
                )
                .keys[0]
                .value
            )
            account_url = f"https://{self.storage_account}.blob.core.windows.net"

            blob_client: BlobServiceClient = BlobServiceClient(account_url, account_key)
            try:
                container_client: ContainerClient = blob_client.create_container(
                    "tools"
                )
            except ResourceExistsError:
                container_client = blob_client.get_container_client("tools")
                pass

            sas = generate_container_sas(
                account_name=container_client.account_name,
                container_name=container_client.container_name,
                account_key=account_key,
                permission=ContainerSasPermissions(read=True, write=True, add=True),
                expiry=datetime.datetime.utcnow() + datetime.timedelta(hours=1),
            )

            container_sas_url = container_client.url + "/?" + sas
            logger.info("uploading files")
            azcopy_copy(os.path.join(extracted_path, "*"), container_sas_url)


def arg_file(arg: str) -> str:
    if not os.path.isfile(arg):
        raise argparse.ArgumentTypeError("not a file: %s" % arg)
    return arg


def main() -> None:

    formatter = argparse.ArgumentDefaultsHelpFormatter
    parser = argparse.ArgumentParser(formatter_class=formatter)
    parser.add_argument("location")
    parser.add_argument("resource_group")
    parser.add_argument("pool_name")
    # parser.add_argument("owner")
    # parser.add_argument("nsg_config")
    parser.add_argument(
        "--bicep-template",
        type=arg_file,
        default="scaleset_template_windows.bicep",
        help="(default: %(default)s)",
    )
    parser.add_argument("-v", "--verbose", action="store_true")
    parser.add_argument(
        "--subscription_id",
        type=str,
    )
    args = parser.parse_args()
    if args.verbose:
        level = logging.DEBUG
    else:
        level = logging.WARN

    logging.basicConfig(level=level)
    logging.getLogger("deploy").setLevel(logging.INFO)

    deployer = Deployer(
        args.resource_group,
        args.location,
        args.pool_name,
        args.subscription_id,
        args.bicep_template,
    )
    deployer.deploy()


if __name__ == "__main__":
    main()
