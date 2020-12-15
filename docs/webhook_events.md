# Webhook Events

This document describes the basic webhook event subscriptions available in OneFuzz

## Payload

Each event will be submitted via HTTP POST to the user provided URL.

### Example

```json
{
    "webhook_id": "00000000-0000-0000-0000-000000000000",
    "event_id": "00000000-0000-0000-0000-000000000000",
    "event_type": "ping",
    "event": {
        "ping_id": "00000000-0000-0000-0000-000000000000"
    }
}
```

## Event Types (WebhookEventType)

* [ping](#ping)
* [task_created](#task_created)
* [task_stopped](#task_stopped)
* [task_failed](#task_failed)
* [proxy_created](#proxy_created)
* [proxy_deleted](#proxy_deleted)
* [proxy_failed](#proxy_failed)
* [pool_created](#pool_created)
* [pool_deleted](#pool_deleted)
* [scaleset_created](#scaleset_created)
* [scaleset_failed](#scaleset_failed)
* [scaleset_deleted](#scaleset_deleted)
* [job_created](#job_created)

### ping

#### Example

```json
{
    "ping_id": "00000000-0000-0000-0000-000000000000"
}
```

#### Schema

```json
{
    "title": "WebhookEventPing",
    "type": "object",
    "properties": {
        "ping_id": {
            "title": "Ping Id",
            "type": "string",
            "format": "uuid"
        }
    }
}
```

### task_created

#### Example

```json
{
    "job_id": "00000000-0000-0000-0000-000000000000",
    "task_id": "00000000-0000-0000-0000-000000000000",
    "config": {
        "job_id": "00000000-0000-0000-0000-000000000000",
        "task": {
            "type": "libfuzzer_fuzz",
            "duration": 1,
            "target_exe": "fuzz.exe",
            "target_env": {},
            "target_options": [],
            "check_debugger": true
        },
        "containers": [
            {
                "type": "setup",
                "name": "my-setup"
            },
            {
                "type": "inputs",
                "name": "my-inputs"
            },
            {
                "type": "crashes",
                "name": "my-crashes"
            }
        ],
        "tags": {}
    },
    "user_info": {
        "application_id": "00000000-0000-0000-0000-000000000000",
        "object_id": "00000000-0000-0000-0000-000000000000",
        "upn": "example@contoso.com"
    }
}
```

#### Schema

```json
{
    "title": "WebhookEventTaskCreated",
    "type": "object",
    "properties": {
        "job_id": {
            "title": "Job Id",
            "type": "string",
            "format": "uuid"
        },
        "task_id": {
            "title": "Task Id",
            "type": "string",
            "format": "uuid"
        },
        "config": {
            "$ref": "#/definitions/TaskConfig"
        },
        "user_info": {
            "$ref": "#/definitions/UserInfo"
        }
    },
    "required": [
        "job_id",
        "task_id",
        "config"
    ],
    "definitions": {
        "TaskType": {
            "title": "TaskType",
            "description": "An enumeration.",
            "enum": [
                "libfuzzer_fuzz",
                "libfuzzer_coverage",
                "libfuzzer_crash_report",
                "libfuzzer_merge",
                "generic_analysis",
                "generic_supervisor",
                "generic_merge",
                "generic_generator",
                "generic_crash_report"
            ]
        },
        "ContainerType": {
            "title": "ContainerType",
            "description": "An enumeration.",
            "enum": [
                "analysis",
                "coverage",
                "crashes",
                "inputs",
                "no_repro",
                "readonly_inputs",
                "reports",
                "setup",
                "tools",
                "unique_inputs",
                "unique_reports"
            ]
        },
        "StatsFormat": {
            "title": "StatsFormat",
            "description": "An enumeration.",
            "enum": [
                "AFL"
            ]
        },
        "TaskDetails": {
            "title": "TaskDetails",
            "type": "object",
            "properties": {
                "type": {
                    "$ref": "#/definitions/TaskType"
                },
                "duration": {
                    "title": "Duration",
                    "type": "integer"
                },
                "target_exe": {
                    "title": "Target Exe",
                    "type": "string"
                },
                "target_env": {
                    "title": "Target Env",
                    "type": "object",
                    "additionalProperties": {
                        "type": "string"
                    }
                },
                "target_options": {
                    "title": "Target Options",
                    "type": "array",
                    "items": {
                        "type": "string"
                    }
                },
                "target_workers": {
                    "title": "Target Workers",
                    "type": "integer"
                },
                "target_options_merge": {
                    "title": "Target Options Merge",
                    "type": "boolean"
                },
                "check_asan_log": {
                    "title": "Check Asan Log",
                    "type": "boolean"
                },
                "check_debugger": {
                    "title": "Check Debugger",
                    "default": true,
                    "type": "boolean"
                },
                "check_retry_count": {
                    "title": "Check Retry Count",
                    "type": "integer"
                },
                "rename_output": {
                    "title": "Rename Output",
                    "type": "boolean"
                },
                "supervisor_exe": {
                    "title": "Supervisor Exe",
                    "type": "string"
                },
                "supervisor_env": {
                    "title": "Supervisor Env",
                    "type": "object",
                    "additionalProperties": {
                        "type": "string"
                    }
                },
                "supervisor_options": {
                    "title": "Supervisor Options",
                    "type": "array",
                    "items": {
                        "type": "string"
                    }
                },
                "supervisor_input_marker": {
                    "title": "Supervisor Input Marker",
                    "type": "string"
                },
                "generator_exe": {
                    "title": "Generator Exe",
                    "type": "string"
                },
                "generator_env": {
                    "title": "Generator Env",
                    "type": "object",
                    "additionalProperties": {
                        "type": "string"
                    }
                },
                "generator_options": {
                    "title": "Generator Options",
                    "type": "array",
                    "items": {
                        "type": "string"
                    }
                },
                "analyzer_exe": {
                    "title": "Analyzer Exe",
                    "type": "string"
                },
                "analyzer_env": {
                    "title": "Analyzer Env",
                    "type": "object",
                    "additionalProperties": {
                        "type": "string"
                    }
                },
                "analyzer_options": {
                    "title": "Analyzer Options",
                    "type": "array",
                    "items": {
                        "type": "string"
                    }
                },
                "wait_for_files": {
                    "$ref": "#/definitions/ContainerType"
                },
                "stats_file": {
                    "title": "Stats File",
                    "type": "string"
                },
                "stats_format": {
                    "$ref": "#/definitions/StatsFormat"
                },
                "reboot_after_setup": {
                    "title": "Reboot After Setup",
                    "type": "boolean"
                },
                "target_timeout": {
                    "title": "Target Timeout",
                    "type": "integer"
                },
                "ensemble_sync_delay": {
                    "title": "Ensemble Sync Delay",
                    "type": "integer"
                },
                "preserve_existing_outputs": {
                    "title": "Preserve Existing Outputs",
                    "type": "boolean"
                }
            },
            "required": [
                "type",
                "duration",
                "target_exe",
                "target_env",
                "target_options"
            ]
        },
        "TaskVm": {
            "title": "TaskVm",
            "type": "object",
            "properties": {
                "region": {
                    "title": "Region",
                    "type": "string"
                },
                "sku": {
                    "title": "Sku",
                    "type": "string"
                },
                "image": {
                    "title": "Image",
                    "type": "string"
                },
                "count": {
                    "title": "Count",
                    "default": 1,
                    "type": "integer"
                },
                "spot_instances": {
                    "title": "Spot Instances",
                    "default": false,
                    "type": "boolean"
                },
                "reboot_after_setup": {
                    "title": "Reboot After Setup",
                    "type": "boolean"
                }
            },
            "required": [
                "region",
                "sku",
                "image"
            ]
        },
        "TaskPool": {
            "title": "TaskPool",
            "type": "object",
            "properties": {
                "count": {
                    "title": "Count",
                    "type": "integer"
                },
                "pool_name": {
                    "title": "Pool Name",
                    "type": "string"
                }
            },
            "required": [
                "count",
                "pool_name"
            ]
        },
        "TaskContainers": {
            "title": "TaskContainers",
            "type": "object",
            "properties": {
                "type": {
                    "$ref": "#/definitions/ContainerType"
                },
                "name": {
                    "title": "Name",
                    "type": "string"
                }
            },
            "required": [
                "type",
                "name"
            ]
        },
        "TaskDebugFlag": {
            "title": "TaskDebugFlag",
            "description": "An enumeration.",
            "enum": [
                "keep_node_on_failure",
                "keep_node_on_completion"
            ]
        },
        "TaskConfig": {
            "title": "TaskConfig",
            "type": "object",
            "properties": {
                "job_id": {
                    "title": "Job Id",
                    "type": "string",
                    "format": "uuid"
                },
                "prereq_tasks": {
                    "title": "Prereq Tasks",
                    "type": "array",
                    "items": {
                        "type": "string",
                        "format": "uuid"
                    }
                },
                "task": {
                    "$ref": "#/definitions/TaskDetails"
                },
                "vm": {
                    "$ref": "#/definitions/TaskVm"
                },
                "pool": {
                    "$ref": "#/definitions/TaskPool"
                },
                "containers": {
                    "title": "Containers",
                    "type": "array",
                    "items": {
                        "$ref": "#/definitions/TaskContainers"
                    }
                },
                "tags": {
                    "title": "Tags",
                    "type": "object",
                    "additionalProperties": {
                        "type": "string"
                    }
                },
                "debug": {
                    "title": "Debug",
                    "type": "array",
                    "items": {
                        "$ref": "#/definitions/TaskDebugFlag"
                    }
                }
            },
            "required": [
                "job_id",
                "task",
                "containers",
                "tags"
            ]
        },
        "UserInfo": {
            "title": "UserInfo",
            "type": "object",
            "properties": {
                "application_id": {
                    "title": "Application Id",
                    "type": "string",
                    "format": "uuid"
                },
                "object_id": {
                    "title": "Object Id",
                    "type": "string",
                    "format": "uuid"
                },
                "upn": {
                    "title": "Upn",
                    "type": "string"
                }
            },
            "required": [
                "application_id"
            ]
        }
    }
}
```

### task_stopped

#### Example

```json
{
    "job_id": "00000000-0000-0000-0000-000000000000",
    "task_id": "00000000-0000-0000-0000-000000000000",
    "user_info": {
        "application_id": "00000000-0000-0000-0000-000000000000",
        "object_id": "00000000-0000-0000-0000-000000000000",
        "upn": "example@contoso.com"
    }
}
```

#### Schema

```json
{
    "title": "WebhookEventTaskStopped",
    "type": "object",
    "properties": {
        "job_id": {
            "title": "Job Id",
            "type": "string",
            "format": "uuid"
        },
        "task_id": {
            "title": "Task Id",
            "type": "string",
            "format": "uuid"
        },
        "user_info": {
            "$ref": "#/definitions/UserInfo"
        }
    },
    "required": [
        "job_id",
        "task_id"
    ],
    "definitions": {
        "UserInfo": {
            "title": "UserInfo",
            "type": "object",
            "properties": {
                "application_id": {
                    "title": "Application Id",
                    "type": "string",
                    "format": "uuid"
                },
                "object_id": {
                    "title": "Object Id",
                    "type": "string",
                    "format": "uuid"
                },
                "upn": {
                    "title": "Upn",
                    "type": "string"
                }
            },
            "required": [
                "application_id"
            ]
        }
    }
}
```

### task_failed

#### Example

```json
{
    "job_id": "00000000-0000-0000-0000-000000000000",
    "task_id": "00000000-0000-0000-0000-000000000000",
    "error": {
        "code": 468,
        "errors": [
            "example error message"
        ]
    },
    "user_info": {
        "application_id": "00000000-0000-0000-0000-000000000000",
        "object_id": "00000000-0000-0000-0000-000000000000",
        "upn": "example@contoso.com"
    }
}
```

#### Schema

```json
{
    "title": "WebhookEventTaskFailed",
    "type": "object",
    "properties": {
        "job_id": {
            "title": "Job Id",
            "type": "string",
            "format": "uuid"
        },
        "task_id": {
            "title": "Task Id",
            "type": "string",
            "format": "uuid"
        },
        "error": {
            "$ref": "#/definitions/Error"
        },
        "user_info": {
            "$ref": "#/definitions/UserInfo"
        }
    },
    "required": [
        "job_id",
        "task_id",
        "error"
    ],
    "definitions": {
        "ErrorCode": {
            "title": "ErrorCode",
            "description": "An enumeration.",
            "enum": [
                450,
                451,
                452,
                453,
                454,
                455,
                456,
                457,
                458,
                459,
                460,
                461,
                462,
                463,
                464,
                465,
                467,
                468,
                469,
                470,
                471,
                472
            ]
        },
        "Error": {
            "title": "Error",
            "type": "object",
            "properties": {
                "code": {
                    "$ref": "#/definitions/ErrorCode"
                },
                "errors": {
                    "title": "Errors",
                    "type": "array",
                    "items": {
                        "type": "string"
                    }
                }
            },
            "required": [
                "code",
                "errors"
            ]
        },
        "UserInfo": {
            "title": "UserInfo",
            "type": "object",
            "properties": {
                "application_id": {
                    "title": "Application Id",
                    "type": "string",
                    "format": "uuid"
                },
                "object_id": {
                    "title": "Object Id",
                    "type": "string",
                    "format": "uuid"
                },
                "upn": {
                    "title": "Upn",
                    "type": "string"
                }
            },
            "required": [
                "application_id"
            ]
        }
    }
}
```

### proxy_created

#### Example

```json
{
    "region": "eastus"
}
```

#### Schema

```json
{
    "title": "WebhookEventProxyCreated",
    "type": "object",
    "properties": {
        "region": {
            "title": "Region",
            "type": "string"
        }
    },
    "required": [
        "region"
    ]
}
```

### proxy_deleted

#### Example

```json
{
    "region": "eastus"
}
```

#### Schema

```json
{
    "title": "WebhookEventProxyDeleted",
    "type": "object",
    "properties": {
        "region": {
            "title": "Region",
            "type": "string"
        }
    },
    "required": [
        "region"
    ]
}
```

### proxy_failed

#### Example

```json
{
    "region": "eastus",
    "error": {
        "code": 472,
        "errors": [
            "example error message"
        ]
    }
}
```

#### Schema

```json
{
    "title": "WebhookEventProxyFailed",
    "type": "object",
    "properties": {
        "region": {
            "title": "Region",
            "type": "string"
        },
        "error": {
            "$ref": "#/definitions/Error"
        }
    },
    "required": [
        "region",
        "error"
    ],
    "definitions": {
        "ErrorCode": {
            "title": "ErrorCode",
            "description": "An enumeration.",
            "enum": [
                450,
                451,
                452,
                453,
                454,
                455,
                456,
                457,
                458,
                459,
                460,
                461,
                462,
                463,
                464,
                465,
                467,
                468,
                469,
                470,
                471,
                472
            ]
        },
        "Error": {
            "title": "Error",
            "type": "object",
            "properties": {
                "code": {
                    "$ref": "#/definitions/ErrorCode"
                },
                "errors": {
                    "title": "Errors",
                    "type": "array",
                    "items": {
                        "type": "string"
                    }
                }
            },
            "required": [
                "code",
                "errors"
            ]
        }
    }
}
```

### pool_created

#### Example

```json
{
    "pool_name": "example",
    "os": "linux",
    "arch": "x86_64",
    "managed": true
}
```

#### Schema

```json
{
    "title": "WebhookEventPoolCreated",
    "type": "object",
    "properties": {
        "pool_name": {
            "title": "Pool Name",
            "type": "string"
        },
        "os": {
            "$ref": "#/definitions/OS"
        },
        "arch": {
            "$ref": "#/definitions/Architecture"
        },
        "managed": {
            "title": "Managed",
            "type": "boolean"
        },
        "autoscale": {
            "$ref": "#/definitions/AutoScaleConfig"
        }
    },
    "required": [
        "pool_name",
        "os",
        "arch",
        "managed"
    ],
    "definitions": {
        "OS": {
            "title": "OS",
            "description": "An enumeration.",
            "enum": [
                "windows",
                "linux"
            ]
        },
        "Architecture": {
            "title": "Architecture",
            "description": "An enumeration.",
            "enum": [
                "x86_64"
            ]
        },
        "AutoScaleConfig": {
            "title": "AutoScaleConfig",
            "type": "object",
            "properties": {
                "image": {
                    "title": "Image",
                    "type": "string"
                },
                "max_size": {
                    "title": "Max Size",
                    "type": "integer"
                },
                "min_size": {
                    "title": "Min Size",
                    "default": 0,
                    "type": "integer"
                },
                "region": {
                    "title": "Region",
                    "type": "string"
                },
                "scaleset_size": {
                    "title": "Scaleset Size",
                    "type": "integer"
                },
                "spot_instances": {
                    "title": "Spot Instances",
                    "default": false,
                    "type": "boolean"
                },
                "vm_sku": {
                    "title": "Vm Sku",
                    "type": "string"
                }
            },
            "required": [
                "image",
                "scaleset_size",
                "vm_sku"
            ]
        }
    }
}
```

### pool_deleted

#### Example

```json
{
    "pool_name": "example"
}
```

#### Schema

```json
{
    "title": "WebhookEventPoolDeleted",
    "type": "object",
    "properties": {
        "pool_name": {
            "title": "Pool Name",
            "type": "string"
        }
    },
    "required": [
        "pool_name"
    ]
}
```

### scaleset_created

#### Example

```json
{
    "scaleset_id": "00000000-0000-0000-0000-000000000000",
    "pool_name": "example",
    "vm_sku": "Standard_D2s_v3",
    "image": "Canonical:UbuntuServer:18.04-LTS:latest",
    "region": "eastus",
    "size": 10
}
```

#### Schema

```json
{
    "title": "WebhookEventScalesetCreated",
    "type": "object",
    "properties": {
        "scaleset_id": {
            "title": "Scaleset Id",
            "type": "string",
            "format": "uuid"
        },
        "pool_name": {
            "title": "Pool Name",
            "type": "string"
        },
        "vm_sku": {
            "title": "Vm Sku",
            "type": "string"
        },
        "image": {
            "title": "Image",
            "type": "string"
        },
        "region": {
            "title": "Region",
            "type": "string"
        },
        "size": {
            "title": "Size",
            "type": "integer"
        }
    },
    "required": [
        "scaleset_id",
        "pool_name",
        "vm_sku",
        "image",
        "region",
        "size"
    ]
}
```

### scaleset_failed

#### Example

```json
{
    "scaleset_id": "00000000-0000-0000-0000-000000000000",
    "pool_name": "example",
    "error": {
        "code": 456,
        "errors": [
            "example error message"
        ]
    }
}
```

#### Schema

```json
{
    "title": "WebhookEventScalesetFailed",
    "type": "object",
    "properties": {
        "scaleset_id": {
            "title": "Scaleset Id",
            "type": "string",
            "format": "uuid"
        },
        "pool_name": {
            "title": "Pool Name",
            "type": "string"
        },
        "error": {
            "$ref": "#/definitions/Error"
        }
    },
    "required": [
        "scaleset_id",
        "pool_name",
        "error"
    ],
    "definitions": {
        "ErrorCode": {
            "title": "ErrorCode",
            "description": "An enumeration.",
            "enum": [
                450,
                451,
                452,
                453,
                454,
                455,
                456,
                457,
                458,
                459,
                460,
                461,
                462,
                463,
                464,
                465,
                467,
                468,
                469,
                470,
                471,
                472
            ]
        },
        "Error": {
            "title": "Error",
            "type": "object",
            "properties": {
                "code": {
                    "$ref": "#/definitions/ErrorCode"
                },
                "errors": {
                    "title": "Errors",
                    "type": "array",
                    "items": {
                        "type": "string"
                    }
                }
            },
            "required": [
                "code",
                "errors"
            ]
        }
    }
}
```

### scaleset_deleted

#### Example

```json
{
    "scaleset_id": "00000000-0000-0000-0000-000000000000",
    "pool_name": "example"
}
```

#### Schema

```json
{
    "title": "WebhookEventScalesetDeleted",
    "type": "object",
    "properties": {
        "scaleset_id": {
            "title": "Scaleset Id",
            "type": "string",
            "format": "uuid"
        },
        "pool_name": {
            "title": "Pool Name",
            "type": "string"
        }
    },
    "required": [
        "scaleset_id",
        "pool_name"
    ]
}
```

### job_created

#### Example

```json
{
    "job_id": "00000000-0000-0000-0000-000000000000",
    "config": {
        "project": "example project",
        "name": "example name",
        "build": "build 1",
        "duration": 24
    }
}
```

#### Schema

```json
{
    "title": "WebhookEventJobCreated",
    "type": "object",
    "properties": {
        "job_id": {
            "title": "Job Id",
            "type": "string",
            "format": "uuid"
        },
        "config": {
            "$ref": "#/definitions/JobConfig"
        },
        "user_info": {
            "$ref": "#/definitions/UserInfo"
        }
    },
    "required": [
        "job_id",
        "config"
    ],
    "definitions": {
        "JobConfig": {
            "title": "JobConfig",
            "type": "object",
            "properties": {
                "project": {
                    "title": "Project",
                    "type": "string"
                },
                "name": {
                    "title": "Name",
                    "type": "string"
                },
                "build": {
                    "title": "Build",
                    "type": "string"
                },
                "duration": {
                    "title": "Duration",
                    "type": "integer"
                }
            },
            "required": [
                "project",
                "name",
                "build",
                "duration"
            ]
        },
        "UserInfo": {
            "title": "UserInfo",
            "type": "object",
            "properties": {
                "application_id": {
                    "title": "Application Id",
                    "type": "string",
                    "format": "uuid"
                },
                "object_id": {
                    "title": "Object Id",
                    "type": "string",
                    "format": "uuid"
                },
                "upn": {
                    "title": "Upn",
                    "type": "string"
                }
            },
            "required": [
                "application_id"
            ]
        }
    }
}
```

## Full Event Schema

```json
{
    "title": "WebhookMessage",
    "type": "object",
    "properties": {
        "webhook_id": {
            "title": "Webhook Id",
            "type": "string",
            "format": "uuid"
        },
        "event_id": {
            "title": "Event Id",
            "type": "string",
            "format": "uuid"
        },
        "event_type": {
            "$ref": "#/definitions/WebhookEventType"
        },
        "event": {
            "title": "Event",
            "anyOf": [
                {
                    "$ref": "#/definitions/WebhookEventProxyCreated"
                },
                {
                    "$ref": "#/definitions/WebhookEventProxyDeleted"
                },
                {
                    "$ref": "#/definitions/WebhookEventProxyFailed"
                },
                {
                    "$ref": "#/definitions/WebhookEventPoolCreated"
                },
                {
                    "$ref": "#/definitions/WebhookEventPoolDeleted"
                },
                {
                    "$ref": "#/definitions/WebhookEventScalesetCreated"
                },
                {
                    "$ref": "#/definitions/WebhookEventScalesetFailed"
                },
                {
                    "$ref": "#/definitions/WebhookEventScalesetDeleted"
                },
                {
                    "$ref": "#/definitions/WebhookEventTaskCreated"
                },
                {
                    "$ref": "#/definitions/WebhookEventTaskStopped"
                },
                {
                    "$ref": "#/definitions/WebhookEventTaskFailed"
                },
                {
                    "$ref": "#/definitions/WebhookEventJobCreated"
                },
                {
                    "$ref": "#/definitions/WebhookEventPing"
                }
            ]
        }
    },
    "required": [
        "webhook_id",
        "event_type",
        "event"
    ],
    "definitions": {
        "WebhookEventType": {
            "title": "WebhookEventType",
            "description": "An enumeration.",
            "enum": [
                "task_created",
                "task_stopped",
                "task_failed",
                "ping",
                "job_created",
                "pool_created",
                "pool_deleted",
                "proxy_created",
                "proxy_deleted",
                "proxy_failed",
                "scaleset_created",
                "scaleset_deleted",
                "scaleset_failed"
            ]
        },
        "WebhookEventProxyCreated": {
            "title": "WebhookEventProxyCreated",
            "type": "object",
            "properties": {
                "region": {
                    "title": "Region",
                    "type": "string"
                }
            },
            "required": [
                "region"
            ]
        },
        "WebhookEventProxyDeleted": {
            "title": "WebhookEventProxyDeleted",
            "type": "object",
            "properties": {
                "region": {
                    "title": "Region",
                    "type": "string"
                }
            },
            "required": [
                "region"
            ]
        },
        "ErrorCode": {
            "title": "ErrorCode",
            "description": "An enumeration.",
            "enum": [
                450,
                451,
                452,
                453,
                454,
                455,
                456,
                457,
                458,
                459,
                460,
                461,
                462,
                463,
                464,
                465,
                467,
                468,
                469,
                470,
                471,
                472
            ]
        },
        "Error": {
            "title": "Error",
            "type": "object",
            "properties": {
                "code": {
                    "$ref": "#/definitions/ErrorCode"
                },
                "errors": {
                    "title": "Errors",
                    "type": "array",
                    "items": {
                        "type": "string"
                    }
                }
            },
            "required": [
                "code",
                "errors"
            ]
        },
        "WebhookEventProxyFailed": {
            "title": "WebhookEventProxyFailed",
            "type": "object",
            "properties": {
                "region": {
                    "title": "Region",
                    "type": "string"
                },
                "error": {
                    "$ref": "#/definitions/Error"
                }
            },
            "required": [
                "region",
                "error"
            ]
        },
        "OS": {
            "title": "OS",
            "description": "An enumeration.",
            "enum": [
                "windows",
                "linux"
            ]
        },
        "Architecture": {
            "title": "Architecture",
            "description": "An enumeration.",
            "enum": [
                "x86_64"
            ]
        },
        "AutoScaleConfig": {
            "title": "AutoScaleConfig",
            "type": "object",
            "properties": {
                "image": {
                    "title": "Image",
                    "type": "string"
                },
                "max_size": {
                    "title": "Max Size",
                    "type": "integer"
                },
                "min_size": {
                    "title": "Min Size",
                    "default": 0,
                    "type": "integer"
                },
                "region": {
                    "title": "Region",
                    "type": "string"
                },
                "scaleset_size": {
                    "title": "Scaleset Size",
                    "type": "integer"
                },
                "spot_instances": {
                    "title": "Spot Instances",
                    "default": false,
                    "type": "boolean"
                },
                "vm_sku": {
                    "title": "Vm Sku",
                    "type": "string"
                }
            },
            "required": [
                "image",
                "scaleset_size",
                "vm_sku"
            ]
        },
        "WebhookEventPoolCreated": {
            "title": "WebhookEventPoolCreated",
            "type": "object",
            "properties": {
                "pool_name": {
                    "title": "Pool Name",
                    "type": "string"
                },
                "os": {
                    "$ref": "#/definitions/OS"
                },
                "arch": {
                    "$ref": "#/definitions/Architecture"
                },
                "managed": {
                    "title": "Managed",
                    "type": "boolean"
                },
                "autoscale": {
                    "$ref": "#/definitions/AutoScaleConfig"
                }
            },
            "required": [
                "pool_name",
                "os",
                "arch",
                "managed"
            ]
        },
        "WebhookEventPoolDeleted": {
            "title": "WebhookEventPoolDeleted",
            "type": "object",
            "properties": {
                "pool_name": {
                    "title": "Pool Name",
                    "type": "string"
                }
            },
            "required": [
                "pool_name"
            ]
        },
        "WebhookEventScalesetCreated": {
            "title": "WebhookEventScalesetCreated",
            "type": "object",
            "properties": {
                "scaleset_id": {
                    "title": "Scaleset Id",
                    "type": "string",
                    "format": "uuid"
                },
                "pool_name": {
                    "title": "Pool Name",
                    "type": "string"
                },
                "vm_sku": {
                    "title": "Vm Sku",
                    "type": "string"
                },
                "image": {
                    "title": "Image",
                    "type": "string"
                },
                "region": {
                    "title": "Region",
                    "type": "string"
                },
                "size": {
                    "title": "Size",
                    "type": "integer"
                }
            },
            "required": [
                "scaleset_id",
                "pool_name",
                "vm_sku",
                "image",
                "region",
                "size"
            ]
        },
        "WebhookEventScalesetFailed": {
            "title": "WebhookEventScalesetFailed",
            "type": "object",
            "properties": {
                "scaleset_id": {
                    "title": "Scaleset Id",
                    "type": "string",
                    "format": "uuid"
                },
                "pool_name": {
                    "title": "Pool Name",
                    "type": "string"
                },
                "error": {
                    "$ref": "#/definitions/Error"
                }
            },
            "required": [
                "scaleset_id",
                "pool_name",
                "error"
            ]
        },
        "WebhookEventScalesetDeleted": {
            "title": "WebhookEventScalesetDeleted",
            "type": "object",
            "properties": {
                "scaleset_id": {
                    "title": "Scaleset Id",
                    "type": "string",
                    "format": "uuid"
                },
                "pool_name": {
                    "title": "Pool Name",
                    "type": "string"
                }
            },
            "required": [
                "scaleset_id",
                "pool_name"
            ]
        },
        "TaskType": {
            "title": "TaskType",
            "description": "An enumeration.",
            "enum": [
                "libfuzzer_fuzz",
                "libfuzzer_coverage",
                "libfuzzer_crash_report",
                "libfuzzer_merge",
                "generic_analysis",
                "generic_supervisor",
                "generic_merge",
                "generic_generator",
                "generic_crash_report"
            ]
        },
        "ContainerType": {
            "title": "ContainerType",
            "description": "An enumeration.",
            "enum": [
                "analysis",
                "coverage",
                "crashes",
                "inputs",
                "no_repro",
                "readonly_inputs",
                "reports",
                "setup",
                "tools",
                "unique_inputs",
                "unique_reports"
            ]
        },
        "StatsFormat": {
            "title": "StatsFormat",
            "description": "An enumeration.",
            "enum": [
                "AFL"
            ]
        },
        "TaskDetails": {
            "title": "TaskDetails",
            "type": "object",
            "properties": {
                "type": {
                    "$ref": "#/definitions/TaskType"
                },
                "duration": {
                    "title": "Duration",
                    "type": "integer"
                },
                "target_exe": {
                    "title": "Target Exe",
                    "type": "string"
                },
                "target_env": {
                    "title": "Target Env",
                    "type": "object",
                    "additionalProperties": {
                        "type": "string"
                    }
                },
                "target_options": {
                    "title": "Target Options",
                    "type": "array",
                    "items": {
                        "type": "string"
                    }
                },
                "target_workers": {
                    "title": "Target Workers",
                    "type": "integer"
                },
                "target_options_merge": {
                    "title": "Target Options Merge",
                    "type": "boolean"
                },
                "check_asan_log": {
                    "title": "Check Asan Log",
                    "type": "boolean"
                },
                "check_debugger": {
                    "title": "Check Debugger",
                    "default": true,
                    "type": "boolean"
                },
                "check_retry_count": {
                    "title": "Check Retry Count",
                    "type": "integer"
                },
                "rename_output": {
                    "title": "Rename Output",
                    "type": "boolean"
                },
                "supervisor_exe": {
                    "title": "Supervisor Exe",
                    "type": "string"
                },
                "supervisor_env": {
                    "title": "Supervisor Env",
                    "type": "object",
                    "additionalProperties": {
                        "type": "string"
                    }
                },
                "supervisor_options": {
                    "title": "Supervisor Options",
                    "type": "array",
                    "items": {
                        "type": "string"
                    }
                },
                "supervisor_input_marker": {
                    "title": "Supervisor Input Marker",
                    "type": "string"
                },
                "generator_exe": {
                    "title": "Generator Exe",
                    "type": "string"
                },
                "generator_env": {
                    "title": "Generator Env",
                    "type": "object",
                    "additionalProperties": {
                        "type": "string"
                    }
                },
                "generator_options": {
                    "title": "Generator Options",
                    "type": "array",
                    "items": {
                        "type": "string"
                    }
                },
                "analyzer_exe": {
                    "title": "Analyzer Exe",
                    "type": "string"
                },
                "analyzer_env": {
                    "title": "Analyzer Env",
                    "type": "object",
                    "additionalProperties": {
                        "type": "string"
                    }
                },
                "analyzer_options": {
                    "title": "Analyzer Options",
                    "type": "array",
                    "items": {
                        "type": "string"
                    }
                },
                "wait_for_files": {
                    "$ref": "#/definitions/ContainerType"
                },
                "stats_file": {
                    "title": "Stats File",
                    "type": "string"
                },
                "stats_format": {
                    "$ref": "#/definitions/StatsFormat"
                },
                "reboot_after_setup": {
                    "title": "Reboot After Setup",
                    "type": "boolean"
                },
                "target_timeout": {
                    "title": "Target Timeout",
                    "type": "integer"
                },
                "ensemble_sync_delay": {
                    "title": "Ensemble Sync Delay",
                    "type": "integer"
                },
                "preserve_existing_outputs": {
                    "title": "Preserve Existing Outputs",
                    "type": "boolean"
                }
            },
            "required": [
                "type",
                "duration",
                "target_exe",
                "target_env",
                "target_options"
            ]
        },
        "TaskVm": {
            "title": "TaskVm",
            "type": "object",
            "properties": {
                "region": {
                    "title": "Region",
                    "type": "string"
                },
                "sku": {
                    "title": "Sku",
                    "type": "string"
                },
                "image": {
                    "title": "Image",
                    "type": "string"
                },
                "count": {
                    "title": "Count",
                    "default": 1,
                    "type": "integer"
                },
                "spot_instances": {
                    "title": "Spot Instances",
                    "default": false,
                    "type": "boolean"
                },
                "reboot_after_setup": {
                    "title": "Reboot After Setup",
                    "type": "boolean"
                }
            },
            "required": [
                "region",
                "sku",
                "image"
            ]
        },
        "TaskPool": {
            "title": "TaskPool",
            "type": "object",
            "properties": {
                "count": {
                    "title": "Count",
                    "type": "integer"
                },
                "pool_name": {
                    "title": "Pool Name",
                    "type": "string"
                }
            },
            "required": [
                "count",
                "pool_name"
            ]
        },
        "TaskContainers": {
            "title": "TaskContainers",
            "type": "object",
            "properties": {
                "type": {
                    "$ref": "#/definitions/ContainerType"
                },
                "name": {
                    "title": "Name",
                    "type": "string"
                }
            },
            "required": [
                "type",
                "name"
            ]
        },
        "TaskDebugFlag": {
            "title": "TaskDebugFlag",
            "description": "An enumeration.",
            "enum": [
                "keep_node_on_failure",
                "keep_node_on_completion"
            ]
        },
        "TaskConfig": {
            "title": "TaskConfig",
            "type": "object",
            "properties": {
                "job_id": {
                    "title": "Job Id",
                    "type": "string",
                    "format": "uuid"
                },
                "prereq_tasks": {
                    "title": "Prereq Tasks",
                    "type": "array",
                    "items": {
                        "type": "string",
                        "format": "uuid"
                    }
                },
                "task": {
                    "$ref": "#/definitions/TaskDetails"
                },
                "vm": {
                    "$ref": "#/definitions/TaskVm"
                },
                "pool": {
                    "$ref": "#/definitions/TaskPool"
                },
                "containers": {
                    "title": "Containers",
                    "type": "array",
                    "items": {
                        "$ref": "#/definitions/TaskContainers"
                    }
                },
                "tags": {
                    "title": "Tags",
                    "type": "object",
                    "additionalProperties": {
                        "type": "string"
                    }
                },
                "debug": {
                    "title": "Debug",
                    "type": "array",
                    "items": {
                        "$ref": "#/definitions/TaskDebugFlag"
                    }
                }
            },
            "required": [
                "job_id",
                "task",
                "containers",
                "tags"
            ]
        },
        "UserInfo": {
            "title": "UserInfo",
            "type": "object",
            "properties": {
                "application_id": {
                    "title": "Application Id",
                    "type": "string",
                    "format": "uuid"
                },
                "object_id": {
                    "title": "Object Id",
                    "type": "string",
                    "format": "uuid"
                },
                "upn": {
                    "title": "Upn",
                    "type": "string"
                }
            },
            "required": [
                "application_id"
            ]
        },
        "WebhookEventTaskCreated": {
            "title": "WebhookEventTaskCreated",
            "type": "object",
            "properties": {
                "job_id": {
                    "title": "Job Id",
                    "type": "string",
                    "format": "uuid"
                },
                "task_id": {
                    "title": "Task Id",
                    "type": "string",
                    "format": "uuid"
                },
                "config": {
                    "$ref": "#/definitions/TaskConfig"
                },
                "user_info": {
                    "$ref": "#/definitions/UserInfo"
                }
            },
            "required": [
                "job_id",
                "task_id",
                "config"
            ]
        },
        "WebhookEventTaskStopped": {
            "title": "WebhookEventTaskStopped",
            "type": "object",
            "properties": {
                "job_id": {
                    "title": "Job Id",
                    "type": "string",
                    "format": "uuid"
                },
                "task_id": {
                    "title": "Task Id",
                    "type": "string",
                    "format": "uuid"
                },
                "user_info": {
                    "$ref": "#/definitions/UserInfo"
                }
            },
            "required": [
                "job_id",
                "task_id"
            ]
        },
        "WebhookEventTaskFailed": {
            "title": "WebhookEventTaskFailed",
            "type": "object",
            "properties": {
                "job_id": {
                    "title": "Job Id",
                    "type": "string",
                    "format": "uuid"
                },
                "task_id": {
                    "title": "Task Id",
                    "type": "string",
                    "format": "uuid"
                },
                "error": {
                    "$ref": "#/definitions/Error"
                },
                "user_info": {
                    "$ref": "#/definitions/UserInfo"
                }
            },
            "required": [
                "job_id",
                "task_id",
                "error"
            ]
        },
        "JobConfig": {
            "title": "JobConfig",
            "type": "object",
            "properties": {
                "project": {
                    "title": "Project",
                    "type": "string"
                },
                "name": {
                    "title": "Name",
                    "type": "string"
                },
                "build": {
                    "title": "Build",
                    "type": "string"
                },
                "duration": {
                    "title": "Duration",
                    "type": "integer"
                }
            },
            "required": [
                "project",
                "name",
                "build",
                "duration"
            ]
        },
        "WebhookEventJobCreated": {
            "title": "WebhookEventJobCreated",
            "type": "object",
            "properties": {
                "job_id": {
                    "title": "Job Id",
                    "type": "string",
                    "format": "uuid"
                },
                "config": {
                    "$ref": "#/definitions/JobConfig"
                },
                "user_info": {
                    "$ref": "#/definitions/UserInfo"
                }
            },
            "required": [
                "job_id",
                "config"
            ]
        },
        "WebhookEventPing": {
            "title": "WebhookEventPing",
            "type": "object",
            "properties": {
                "ping_id": {
                    "title": "Ping Id",
                    "type": "string",
                    "format": "uuid"
                }
            }
        }
    }
}
```

