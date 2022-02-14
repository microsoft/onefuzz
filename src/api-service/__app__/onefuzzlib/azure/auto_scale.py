#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

from datetime import datetime, timedelta
import logging
from typing import Any, Dict, List, Optional, Set, Union, cast
from uuid import UUID
import uuid

from onefuzztypes.models import Error
from .monitor import get_monitor_client

from azure.mgmt.monitor.models import (
    AutoscaleProfile,
    AutoscaleSettingResource,
    ScaleCapacity,
    ScaleRule,
    MetricTrigger,
    ScaleAction,
    TimeAggregationType,
    ComparisonOperationType,
    MetricStatisticType,
    ScaleDirection,
    ScaleType
)

from .creds import (
    get_base_resource_group,
    get_scaleset_identity_resource_path,
    retry_on_auth_failure,
)


@retry_on_auth_failure()
def add_auto_scale_to_vmss(vmss: UUID) -> Optional[Error]:
    # TODO: Check if auto scale resource already exists in vmss
    # TODO: If it doesn't exist, create it for the scaleset
    return None

def create_auto_scale_resource_for(resource_id: UUID, location: str, profile: AutoscaleProfile) -> Union[AutoscaleSettingResource, Error]:
    client = get_monitor_client()
    resource_group = get_base_resource_group()

    params: Dict[str, Any] = {
        "location": location,
        "profiles": [profile]
    }

    client.autoscale_settings.create_or_update(
        resource_group,
        str(uuid.uuid4()),
        params
    )
    return None

def create_vmss_auto_scale_profile(min: int, max: int, pool_queue_uri: str) -> AutoscaleProfile:
    return AutoscaleProfile(
        name=str(uuid.uuid4()),
        capacity=ScaleCapacity(
            minimum=min,
            maximum=max,
            default=max
        ),
        rules=[
            ScaleRule(
                matric_trigger=MetricTrigger(
                    metric_name="PoolQueueItemCount",
                    metric_resource_uri=pool_queue_uri,

                    # Check every minute
                    time_grain=timedelta(minutes=1),

                    # The average amount of messages there are in the pool queue
                    time_aggregation= TimeAggregationType.AVERAGE,
                    statistic=MetricStatisticType.COUNT,

                    # Over the past 10 minutes
                    time_window = timedelta(minutes=10),

                    # When there's more than 1 message in the pool queue
                    operator= ComparisonOperationType.GREATER_THAN,
                    threshold=1,
                ),
                scale_action=ScaleAction(
                    direction=ScaleDirection.INCREASE,
                    type=ScaleType.CHANGE_COUNT,
                    value = 1,
                    cooldown = timedelta(minutes=5)
                )
            )
        ])
