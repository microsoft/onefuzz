from azure.mgmt.monitor import MonitorManagementClient
from memoization import cached

from .creds import get_identity, get_subscription


@cached
def get_monitor_client() -> MonitorManagementClient:
    return MonitorManagementClient(get_identity(), get_subscription())
