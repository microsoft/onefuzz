from azure.mgmt.network import NetworkManagementClient
from memoization import cached

from .creds import get_identity, get_subscription


@cached
def get_network_client() -> NetworkManagementClient:
    return NetworkManagementClient(get_identity(), get_subscription())
