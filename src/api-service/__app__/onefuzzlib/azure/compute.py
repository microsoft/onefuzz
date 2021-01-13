from azure.mgmt.compute import ComputeManagementClient
from memoization import cached

from .creds import get_identity, get_subscription


@cached
def get_client() -> ComputeManagementClient:
    return ComputeManagementClient(get_identity(), get_subscription())
