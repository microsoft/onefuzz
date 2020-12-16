# Webhook Events

This document describes the basic webhook event subscriptions available in OneFuzz

## Payload

Each event will be submitted via HTTP POST to the user provided URL.

### Example

```json
{
    "event_id": "00000000-0000-0000-0000-000000000000",
    "event_type": "ping",
    "event": {
        "ping_id": "00000000-0000-0000-0000-000000000000"
    },
    "webhook_id": "00000000-0000-0000-0000-000000000000"
}
```

## Event Types (EventType)

* [job_created](#job_created)
* [job_stopped](#job_stopped)
* [node_created](#node_created)
* [node_deleted](#node_deleted)
* [node_state_updated](#node_state_updated)
* [ping](#ping)
* [pool_created](#pool_created)
* [pool_deleted](#pool_deleted)
* [proxy_created](#proxy_created)
* [proxy_deleted](#proxy_deleted)
* [proxy_failed](#proxy_failed)
* [scaleset_created](#scaleset_created)
* [scaleset_deleted](#scaleset_deleted)
* [scaleset_failed](#scaleset_failed)
* [task_created](#task_created)
* [task_failed](#task_failed)
* [task_state_updated](#task_state_updated)
* [task_stopped](#task_stopped)

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
    "title": "EventJobCreated",
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
    "additionalProperties": false,
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

### job_stopped

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
    "title": "EventJobStopped",
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
    "additionalProperties": false,
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

### node_created

#### Example

```json
{
    "machine_id": "00000000-0000-0000-0000-000000000000",
    "pool_name": "example"
}
```

#### Schema

```json
{
    "title": "EventNodeCreated",
    "type": "object",
    "properties": {
        "machine_id": {
            "title": "Machine Id",
            "type": "string",
            "format": "uuid"
        },
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
        "machine_id",
        "pool_name"
    ],
    "additionalProperties": false
}
```

### node_deleted

#### Example

```json
{
    "machine_id": "00000000-0000-0000-0000-000000000000",
    "pool_name": "example"
}
```

#### Schema

```json
{
    "title": "EventNodeDeleted",
    "type": "object",
    "properties": {
        "machine_id": {
            "title": "Machine Id",
            "type": "string",
            "format": "uuid"
        },
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
        "machine_id",
        "pool_name"
    ],
    "additionalProperties": false
}
```

### node_state_updated

#### Example

```json
{
    "machine_id": "00000000-0000-0000-0000-000000000000",
    "pool_name": "example",
    "state": "setting_up"
}
```

#### Schema

```json
{
    "title": "EventNodeStateUpdated",
    "type": "object",
    "properties": {
        "machine_id": {
            "title": "Machine Id",
            "type": "string",
            "format": "uuid"
        },
        "scaleset_id": {
            "title": "Scaleset Id",
            "type": "string",
            "format": "uuid"
        },
        "pool_name": {
            "title": "Pool Name",
            "type": "string"
        },
        "state": {
            "$ref": "#/definitions/NodeState"
        }
    },
    "required": [
        "machine_id",
        "pool_name",
        "state"
    ],
    "additionalProperties": false,
    "definitions": {
        "NodeState": {
            "title": "NodeState",
            "description": "An enumeration.",
            "enum": [
                "init",
                "free",
                "setting_up",
                "rebooting",
                "ready",
                "busy",
                "done",
                "shutdown",
                "halt"
            ]
        }
    }
}
```

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
    "title": "EventPing",
    "type": "object",
    "properties": {
        "ping_id": {
            "title": "Ping Id",
            "type": "string",
            "format": "uuid"
        }
    },
    "required": [
        "ping_id"
    ]
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
    "title": "EventPoolCreated",
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
    "additionalProperties": false,
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
    "title": "EventPoolDeleted",
    "type": "object",
    "properties": {
        "pool_name": {
            "title": "Pool Name",
            "type": "string"
        }
    },
    "required": [
        "pool_name"
    ],
    "additionalProperties": false
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
    "title": "EventProxyCreated",
    "type": "object",
    "properties": {
        "region": {
            "title": "Region",
            "type": "string"
        }
    },
    "required": [
        "region"
    ],
    "additionalProperties": false
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
    "title": "EventProxyDeleted",
    "type": "object",
    "properties": {
        "region": {
            "title": "Region",
            "type": "string"
        }
    },
    "required": [
        "region"
    ],
    "additionalProperties": false
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
    "title": "EventProxyFailed",
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
    "additionalProperties": false,
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
    "title": "EventScalesetCreated",
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
    ],
    "additionalProperties": false
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
    "title": "EventScalesetDeleted",
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
    ],
    "additionalProperties": false
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
    "title": "EventScalesetFailed",
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
    "additionalProperties": false,
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
    "title": "EventTaskCreated",
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
    "additionalProperties": false,
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
    "title": "EventTaskFailed",
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
    "additionalProperties": false,
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

### task_state_updated

#### Example

```json
{
    "job_id": "00000000-0000-0000-0000-000000000000",
    "task_id": "00000000-0000-0000-0000-000000000000",
    "state": "init"
}
```

#### Schema

```json
{
    "title": "EventTaskStateUpdated",
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
        "state": {
            "$ref": "#/definitions/TaskState"
        }
    },
    "required": [
        "job_id",
        "task_id",
        "state"
    ],
    "additionalProperties": false,
    "definitions": {
        "TaskState": {
            "title": "TaskState",
            "description": "An enumeration.",
            "enum": [
                "init",
                "waiting",
                "scheduled",
                "setting_up",
                "running",
                "stopping",
                "stopped",
                "wait_job"
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
    "title": "EventTaskStopped",
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
    "additionalProperties": false,
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

## Full Event Schema

```json
{
    "title": "WebhookMessage",
    "type": "object",
    "properties": {
        "event_id": {
            "title": "Event Id",
            "type": "string",
            "format": "uuid"
        },
        "event_type": {
            "$ref": "#/definitions/EventType"
        },
        "event": {
            "title": "Event",
            "anyOf": [
                {
                    "$ref": "#/definitions/EventJobCreated"
                },
                {
                    "$ref": "#/definitions/EventJobStopped"
                },
                {
                    "$ref": "#/definitions/EventNodeStateUpdated"
                },
                {
                    "$ref": "#/definitions/EventNodeCreated"
                },
                {
                    "$ref": "#/definitions/EventNodeDeleted"
                },
                {
                    "$ref": "#/definitions/EventPing"
                },
                {
                    "$ref": "#/definitions/EventPoolCreated"
                },
                {
                    "$ref": "#/definitions/EventPoolDeleted"
                },
                {
                    "$ref": "#/definitions/EventProxyFailed"
                },
                {
                    "$ref": "#/definitions/EventProxyCreated"
                },
                {
                    "$ref": "#/definitions/EventProxyDeleted"
                },
                {
                    "$ref": "#/definitions/EventScalesetFailed"
                },
                {
                    "$ref": "#/definitions/EventScalesetCreated"
                },
                {
                    "$ref": "#/definitions/EventScalesetDeleted"
                },
                {
                    "$ref": "#/definitions/EventTaskFailed"
                },
                {
                    "$ref": "#/definitions/EventTaskStateUpdated"
                },
                {
                    "$ref": "#/definitions/EventTaskCreated"
                },
                {
                    "$ref": "#/definitions/EventTaskStopped"
                }
            ]
        },
        "webhook_id": {
            "title": "Webhook Id",
            "type": "string",
            "format": "uuid"
        }
    },
    "required": [
        "event_type",
        "event",
        "webhook_id"
    ],
    "additionalProperties": false,
    "definitions": {
        "EventType": {
            "title": "EventType",
            "description": "An enumeration.",
            "enum": [
                "job_created",
                "job_stopped",
                "node_created",
                "node_deleted",
                "node_state_updated",
                "ping",
                "pool_created",
                "pool_deleted",
                "proxy_created",
                "proxy_deleted",
                "proxy_failed",
                "scaleset_created",
                "scaleset_deleted",
                "scaleset_failed",
                "task_created",
                "task_failed",
                "task_state_updated",
                "task_stopped"
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
        "EventJobCreated": {
            "title": "EventJobCreated",
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
            "additionalProperties": false
        },
        "EventJobStopped": {
            "title": "EventJobStopped",
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
            "additionalProperties": false
        },
        "NodeState": {
            "title": "NodeState",
            "description": "An enumeration.",
            "enum": [
                "init",
                "free",
                "setting_up",
                "rebooting",
                "ready",
                "busy",
                "done",
                "shutdown",
                "halt"
            ]
        },
        "EventNodeStateUpdated": {
            "title": "EventNodeStateUpdated",
            "type": "object",
            "properties": {
                "machine_id": {
                    "title": "Machine Id",
                    "type": "string",
                    "format": "uuid"
                },
                "scaleset_id": {
                    "title": "Scaleset Id",
                    "type": "string",
                    "format": "uuid"
                },
                "pool_name": {
                    "title": "Pool Name",
                    "type": "string"
                },
                "state": {
                    "$ref": "#/definitions/NodeState"
                }
            },
            "required": [
                "machine_id",
                "pool_name",
                "state"
            ],
            "additionalProperties": false
        },
        "EventNodeCreated": {
            "title": "EventNodeCreated",
            "type": "object",
            "properties": {
                "machine_id": {
                    "title": "Machine Id",
                    "type": "string",
                    "format": "uuid"
                },
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
                "machine_id",
                "pool_name"
            ],
            "additionalProperties": false
        },
        "EventNodeDeleted": {
            "title": "EventNodeDeleted",
            "type": "object",
            "properties": {
                "machine_id": {
                    "title": "Machine Id",
                    "type": "string",
                    "format": "uuid"
                },
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
                "machine_id",
                "pool_name"
            ],
            "additionalProperties": false
        },
        "EventPing": {
            "title": "EventPing",
            "type": "object",
            "properties": {
                "ping_id": {
                    "title": "Ping Id",
                    "type": "string",
                    "format": "uuid"
                }
            },
            "required": [
                "ping_id"
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
        "EventPoolCreated": {
            "title": "EventPoolCreated",
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
            "additionalProperties": false
        },
        "EventPoolDeleted": {
            "title": "EventPoolDeleted",
            "type": "object",
            "properties": {
                "pool_name": {
                    "title": "Pool Name",
                    "type": "string"
                }
            },
            "required": [
                "pool_name"
            ],
            "additionalProperties": false
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
        "EventProxyFailed": {
            "title": "EventProxyFailed",
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
            "additionalProperties": false
        },
        "EventProxyCreated": {
            "title": "EventProxyCreated",
            "type": "object",
            "properties": {
                "region": {
                    "title": "Region",
                    "type": "string"
                }
            },
            "required": [
                "region"
            ],
            "additionalProperties": false
        },
        "EventProxyDeleted": {
            "title": "EventProxyDeleted",
            "type": "object",
            "properties": {
                "region": {
                    "title": "Region",
                    "type": "string"
                }
            },
            "required": [
                "region"
            ],
            "additionalProperties": false
        },
        "EventScalesetFailed": {
            "title": "EventScalesetFailed",
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
            "additionalProperties": false
        },
        "EventScalesetCreated": {
            "title": "EventScalesetCreated",
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
            ],
            "additionalProperties": false
        },
        "EventScalesetDeleted": {
            "title": "EventScalesetDeleted",
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
            ],
            "additionalProperties": false
        },
        "EventTaskFailed": {
            "title": "EventTaskFailed",
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
            "additionalProperties": false
        },
        "TaskState": {
            "title": "TaskState",
            "description": "An enumeration.",
            "enum": [
                "init",
                "waiting",
                "scheduled",
                "setting_up",
                "running",
                "stopping",
                "stopped",
                "wait_job"
            ]
        },
        "EventTaskStateUpdated": {
            "title": "EventTaskStateUpdated",
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
                "state": {
                    "$ref": "#/definitions/TaskState"
                }
            },
            "required": [
                "job_id",
                "task_id",
                "state"
            ],
            "additionalProperties": false
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
        "EventTaskCreated": {
            "title": "EventTaskCreated",
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
            "additionalProperties": false
        },
        "EventTaskStopped": {
            "title": "EventTaskStopped",
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
            "additionalProperties": false
        }
    }
}
```

