# Webhook Events

This document describes the basic webhook event subscriptions available in OneFuzz

## Payload

Each event will be submitted via HTTP POST to the user provided URL.

### Example

```json
{
    "event": {
        "ping_id": "00000000-0000-0000-0000-000000000000"
    },
    "event_id": "00000000-0000-0000-0000-000000000000",
    "event_type": "ping",
    "instance_id": "00000000-0000-0000-0000-000000000000",
    "instance_name": "example",
    "webhook_id": "00000000-0000-0000-0000-000000000000"
}
```

## Event Types (EventType)

* [crash_reported](#crash_reported)
* [file_added](#file_added)
* [instance_config_updated](#instance_config_updated)
* [job_created](#job_created)
* [job_stopped](#job_stopped)
* [node_created](#node_created)
* [node_deleted](#node_deleted)
* [node_heartbeat](#node_heartbeat)
* [node_state_updated](#node_state_updated)
* [ping](#ping)
* [pool_created](#pool_created)
* [pool_deleted](#pool_deleted)
* [proxy_created](#proxy_created)
* [proxy_deleted](#proxy_deleted)
* [proxy_failed](#proxy_failed)
* [proxy_state_updated](#proxy_state_updated)
* [regression_reported](#regression_reported)
* [scaleset_created](#scaleset_created)
* [scaleset_deleted](#scaleset_deleted)
* [scaleset_failed](#scaleset_failed)
* [scaleset_state_updated](#scaleset_state_updated)
* [task_created](#task_created)
* [task_failed](#task_failed)
* [task_heartbeat](#task_heartbeat)
* [task_state_updated](#task_state_updated)
* [task_stopped](#task_stopped)

### crash_reported

#### Example

```json
{
    "container": "container-name",
    "filename": "example.json",
    "report": {
        "asan_log": "example asan log",
        "call_stack": [
            "#0 line",
            "#1 line",
            "#2 line"
        ],
        "call_stack_sha256": "0000000000000000000000000000000000000000000000000000000000000000",
        "crash_site": "example crash site",
        "crash_type": "example crash report type",
        "executable": "fuzz.exe",
        "input_blob": {
            "account": "contoso-storage-account",
            "container": "crashes",
            "name": "input.txt"
        },
        "input_sha256": "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
        "job_id": "00000000-0000-0000-0000-000000000000",
        "scariness_description": "example-scariness",
        "scariness_score": 10,
        "task_id": "00000000-0000-0000-0000-000000000000"
    }
}
```

#### Schema

```json
{
    "definitions": {
        "BlobRef": {
            "properties": {
                "account": {
                    "title": "Account",
                    "type": "string"
                },
                "container": {
                    "title": "Container",
                    "type": "string"
                },
                "name": {
                    "title": "Name",
                    "type": "string"
                }
            },
            "required": [
                "account",
                "container",
                "name"
            ],
            "title": "BlobRef",
            "type": "object"
        },
        "ContainerType": {
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
                "unique_reports",
                "regression_reports"
            ],
            "title": "ContainerType"
        },
        "Report": {
            "properties": {
                "asan_log": {
                    "title": "Asan Log",
                    "type": "string"
                },
                "call_stack": {
                    "items": {
                        "type": "string"
                    },
                    "title": "Call Stack",
                    "type": "array"
                },
                "call_stack_sha256": {
                    "title": "Call Stack Sha256",
                    "type": "string"
                },
                "crash_site": {
                    "title": "Crash Site",
                    "type": "string"
                },
                "crash_type": {
                    "title": "Crash Type",
                    "type": "string"
                },
                "executable": {
                    "title": "Executable",
                    "type": "string"
                },
                "input_blob": {
                    "$ref": "#/definitions/BlobRef"
                },
                "input_sha256": {
                    "title": "Input Sha256",
                    "type": "string"
                },
                "input_url": {
                    "title": "Input Url",
                    "type": "string"
                },
                "job_id": {
                    "format": "uuid",
                    "title": "Job Id",
                    "type": "string"
                },
                "minimized_stack": {
                    "items": {
                        "type": "string"
                    },
                    "title": "Minimized Stack",
                    "type": "array"
                },
                "minimized_stack_function_lines": {
                    "items": {
                        "type": "string"
                    },
                    "title": "Minimized Stack Function Lines",
                    "type": "array"
                },
                "minimized_stack_function_lines_sha256": {
                    "title": "Minimized Stack Function Lines Sha256",
                    "type": "string"
                },
                "minimized_stack_function_names": {
                    "items": {
                        "type": "string"
                    },
                    "title": "Minimized Stack Function Names",
                    "type": "array"
                },
                "minimized_stack_function_names_sha256": {
                    "title": "Minimized Stack Function Names Sha256",
                    "type": "string"
                },
                "minimized_stack_sha256": {
                    "title": "Minimized Stack Sha256",
                    "type": "string"
                },
                "scariness_description": {
                    "title": "Scariness Description",
                    "type": "string"
                },
                "scariness_score": {
                    "title": "Scariness Score",
                    "type": "integer"
                },
                "task_id": {
                    "format": "uuid",
                    "title": "Task Id",
                    "type": "string"
                }
            },
            "required": [
                "executable",
                "crash_type",
                "crash_site",
                "call_stack",
                "call_stack_sha256",
                "input_sha256",
                "task_id",
                "job_id"
            ],
            "title": "Report",
            "type": "object"
        },
        "StatsFormat": {
            "description": "An enumeration.",
            "enum": [
                "AFL"
            ],
            "title": "StatsFormat"
        },
        "TaskConfig": {
            "properties": {
                "colocate": {
                    "title": "Colocate",
                    "type": "boolean"
                },
                "containers": {
                    "items": {
                        "$ref": "#/definitions/TaskContainers"
                    },
                    "title": "Containers",
                    "type": "array"
                },
                "debug": {
                    "items": {
                        "$ref": "#/definitions/TaskDebugFlag"
                    },
                    "type": "array"
                },
                "job_id": {
                    "format": "uuid",
                    "title": "Job Id",
                    "type": "string"
                },
                "pool": {
                    "$ref": "#/definitions/TaskPool"
                },
                "prereq_tasks": {
                    "items": {
                        "format": "uuid",
                        "type": "string"
                    },
                    "title": "Prereq Tasks",
                    "type": "array"
                },
                "tags": {
                    "additionalProperties": {
                        "type": "string"
                    },
                    "title": "Tags",
                    "type": "object"
                },
                "task": {
                    "$ref": "#/definitions/TaskDetails"
                },
                "vm": {
                    "$ref": "#/definitions/TaskVm"
                }
            },
            "required": [
                "job_id",
                "task",
                "containers",
                "tags"
            ],
            "title": "TaskConfig",
            "type": "object"
        },
        "TaskContainers": {
            "properties": {
                "name": {
                    "title": "Name",
                    "type": "string"
                },
                "type": {
                    "$ref": "#/definitions/ContainerType"
                }
            },
            "required": [
                "type",
                "name"
            ],
            "title": "TaskContainers",
            "type": "object"
        },
        "TaskDebugFlag": {
            "description": "An enumeration.",
            "enum": [
                "keep_node_on_failure",
                "keep_node_on_completion"
            ],
            "title": "TaskDebugFlag"
        },
        "TaskDetails": {
            "properties": {
                "analyzer_env": {
                    "additionalProperties": {
                        "type": "string"
                    },
                    "title": "Analyzer Env",
                    "type": "object"
                },
                "analyzer_exe": {
                    "title": "Analyzer Exe",
                    "type": "string"
                },
                "analyzer_options": {
                    "items": {
                        "type": "string"
                    },
                    "title": "Analyzer Options",
                    "type": "array"
                },
                "check_asan_log": {
                    "title": "Check Asan Log",
                    "type": "boolean"
                },
                "check_debugger": {
                    "default": true,
                    "title": "Check Debugger",
                    "type": "boolean"
                },
                "check_fuzzer_help": {
                    "title": "Check Fuzzer Help",
                    "type": "boolean"
                },
                "check_retry_count": {
                    "title": "Check Retry Count",
                    "type": "integer"
                },
                "coverage_filter": {
                    "title": "Coverage Filter",
                    "type": "string"
                },
                "duration": {
                    "title": "Duration",
                    "type": "integer"
                },
                "ensemble_sync_delay": {
                    "title": "Ensemble Sync Delay",
                    "type": "integer"
                },
                "expect_crash_on_failure": {
                    "title": "Expect Crash On Failure",
                    "type": "boolean"
                },
                "generator_env": {
                    "additionalProperties": {
                        "type": "string"
                    },
                    "title": "Generator Env",
                    "type": "object"
                },
                "generator_exe": {
                    "title": "Generator Exe",
                    "type": "string"
                },
                "generator_options": {
                    "items": {
                        "type": "string"
                    },
                    "title": "Generator Options",
                    "type": "array"
                },
                "input_file": {
                    "title": "Input File",
                    "type": "string"
                },
                "minimized_stack_depth": {
                    "title": "Minimized Stack Depth",
                    "type": "integer"
                },
                "preserve_existing_outputs": {
                    "title": "Preserve Existing Outputs",
                    "type": "boolean"
                },
                "reboot_after_setup": {
                    "title": "Reboot After Setup",
                    "type": "boolean"
                },
                "rename_output": {
                    "title": "Rename Output",
                    "type": "boolean"
                },
                "report_list": {
                    "items": {
                        "type": "string"
                    },
                    "title": "Report List",
                    "type": "array"
                },
                "stats_file": {
                    "title": "Stats File",
                    "type": "string"
                },
                "stats_format": {
                    "$ref": "#/definitions/StatsFormat"
                },
                "supervisor_env": {
                    "additionalProperties": {
                        "type": "string"
                    },
                    "title": "Supervisor Env",
                    "type": "object"
                },
                "supervisor_exe": {
                    "title": "Supervisor Exe",
                    "type": "string"
                },
                "supervisor_input_marker": {
                    "title": "Supervisor Input Marker",
                    "type": "string"
                },
                "supervisor_options": {
                    "items": {
                        "type": "string"
                    },
                    "title": "Supervisor Options",
                    "type": "array"
                },
                "target_env": {
                    "additionalProperties": {
                        "type": "string"
                    },
                    "title": "Target Env",
                    "type": "object"
                },
                "target_exe": {
                    "title": "Target Exe",
                    "type": "string"
                },
                "target_options": {
                    "items": {
                        "type": "string"
                    },
                    "title": "Target Options",
                    "type": "array"
                },
                "target_options_merge": {
                    "title": "Target Options Merge",
                    "type": "boolean"
                },
                "target_timeout": {
                    "title": "Target Timeout",
                    "type": "integer"
                },
                "target_workers": {
                    "title": "Target Workers",
                    "type": "integer"
                },
                "type": {
                    "$ref": "#/definitions/TaskType"
                },
                "wait_for_files": {
                    "$ref": "#/definitions/ContainerType"
                }
            },
            "required": [
                "type",
                "duration"
            ],
            "title": "TaskDetails",
            "type": "object"
        },
        "TaskPool": {
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
            ],
            "title": "TaskPool",
            "type": "object"
        },
        "TaskType": {
            "description": "An enumeration.",
            "enum": [
                "coverage",
                "libfuzzer_fuzz",
                "libfuzzer_coverage",
                "libfuzzer_crash_report",
                "libfuzzer_merge",
                "libfuzzer_regression",
                "generic_analysis",
                "generic_supervisor",
                "generic_merge",
                "generic_generator",
                "generic_crash_report",
                "generic_regression",
                "analysis_single"
            ],
            "title": "TaskType"
        },
        "TaskVm": {
            "properties": {
                "count": {
                    "default": 1,
                    "title": "Count",
                    "type": "integer"
                },
                "image": {
                    "title": "Image",
                    "type": "string"
                },
                "reboot_after_setup": {
                    "title": "Reboot After Setup",
                    "type": "boolean"
                },
                "region": {
                    "title": "Region",
                    "type": "string"
                },
                "sku": {
                    "title": "Sku",
                    "type": "string"
                },
                "spot_instances": {
                    "default": false,
                    "title": "Spot Instances",
                    "type": "boolean"
                }
            },
            "required": [
                "region",
                "sku",
                "image"
            ],
            "title": "TaskVm",
            "type": "object"
        }
    },
    "properties": {
        "container": {
            "title": "Container",
            "type": "string"
        },
        "filename": {
            "title": "Filename",
            "type": "string"
        },
        "report": {
            "$ref": "#/definitions/Report"
        },
        "task_config": {
            "$ref": "#/definitions/TaskConfig"
        }
    },
    "required": [
        "report",
        "container",
        "filename"
    ],
    "title": "EventCrashReported",
    "type": "object"
}
```

### file_added

#### Example

```json
{
    "container": "container-name",
    "filename": "example.txt"
}
```

#### Schema

```json
{
    "properties": {
        "container": {
            "title": "Container",
            "type": "string"
        },
        "filename": {
            "title": "Filename",
            "type": "string"
        }
    },
    "required": [
        "container",
        "filename"
    ],
    "title": "EventFileAdded",
    "type": "object"
}
```

### instance_config_updated

#### Example

```json
{
    "config": {
        "admins": [
            "00000000-0000-0000-0000-000000000000"
        ],
        "allow_pool_management": true
    }
}
```

#### Schema

```json
{
    "definitions": {
        "InstanceConfig": {
            "properties": {
                "admins": {
                    "items": {
                        "format": "uuid",
                        "type": "string"
                    },
                    "title": "Admins",
                    "type": "array"
                },
                "allow_pool_management": {
                    "default": true,
                    "title": "Allow Pool Management",
                    "type": "boolean"
                }
            },
            "title": "InstanceConfig",
            "type": "object"
        }
    },
    "properties": {
        "config": {
            "$ref": "#/definitions/InstanceConfig"
        }
    },
    "required": [
        "config"
    ],
    "title": "EventInstanceConfigUpdated",
    "type": "object"
}
```

### job_created

#### Example

```json
{
    "config": {
        "build": "build 1",
        "duration": 24,
        "name": "example name",
        "project": "example project"
    },
    "job_id": "00000000-0000-0000-0000-000000000000"
}
```

#### Schema

```json
{
    "definitions": {
        "JobConfig": {
            "properties": {
                "build": {
                    "title": "Build",
                    "type": "string"
                },
                "duration": {
                    "title": "Duration",
                    "type": "integer"
                },
                "name": {
                    "title": "Name",
                    "type": "string"
                },
                "project": {
                    "title": "Project",
                    "type": "string"
                }
            },
            "required": [
                "project",
                "name",
                "build",
                "duration"
            ],
            "title": "JobConfig",
            "type": "object"
        },
        "UserInfo": {
            "properties": {
                "application_id": {
                    "format": "uuid",
                    "title": "Application Id",
                    "type": "string"
                },
                "object_id": {
                    "format": "uuid",
                    "title": "Object Id",
                    "type": "string"
                },
                "upn": {
                    "title": "Upn",
                    "type": "string"
                }
            },
            "title": "UserInfo",
            "type": "object"
        }
    },
    "properties": {
        "config": {
            "$ref": "#/definitions/JobConfig"
        },
        "job_id": {
            "format": "uuid",
            "title": "Job Id",
            "type": "string"
        },
        "user_info": {
            "$ref": "#/definitions/UserInfo"
        }
    },
    "required": [
        "job_id",
        "config"
    ],
    "title": "EventJobCreated",
    "type": "object"
}
```

### job_stopped

#### Example

```json
{
    "config": {
        "build": "build 1",
        "duration": 24,
        "name": "example name",
        "project": "example project"
    },
    "job_id": "00000000-0000-0000-0000-000000000000",
    "task_info": [
        {
            "error": {
                "code": 468,
                "errors": [
                    "example error message"
                ]
            },
            "task_id": "00000000-0000-0000-0000-000000000000",
            "task_type": "libfuzzer_fuzz"
        },
        {
            "task_id": "00000000-0000-0000-0000-000000000001",
            "task_type": "libfuzzer_coverage"
        }
    ]
}
```

#### Schema

```json
{
    "definitions": {
        "Error": {
            "properties": {
                "code": {
                    "$ref": "#/definitions/ErrorCode"
                },
                "errors": {
                    "items": {
                        "type": "string"
                    },
                    "title": "Errors",
                    "type": "array"
                }
            },
            "required": [
                "code",
                "errors"
            ],
            "title": "Error",
            "type": "object"
        },
        "ErrorCode": {
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
            ],
            "title": "ErrorCode"
        },
        "JobConfig": {
            "properties": {
                "build": {
                    "title": "Build",
                    "type": "string"
                },
                "duration": {
                    "title": "Duration",
                    "type": "integer"
                },
                "name": {
                    "title": "Name",
                    "type": "string"
                },
                "project": {
                    "title": "Project",
                    "type": "string"
                }
            },
            "required": [
                "project",
                "name",
                "build",
                "duration"
            ],
            "title": "JobConfig",
            "type": "object"
        },
        "JobTaskStopped": {
            "properties": {
                "error": {
                    "$ref": "#/definitions/Error"
                },
                "task_id": {
                    "format": "uuid",
                    "title": "Task Id",
                    "type": "string"
                },
                "task_type": {
                    "$ref": "#/definitions/TaskType"
                }
            },
            "required": [
                "task_id",
                "task_type"
            ],
            "title": "JobTaskStopped",
            "type": "object"
        },
        "TaskType": {
            "description": "An enumeration.",
            "enum": [
                "coverage",
                "libfuzzer_fuzz",
                "libfuzzer_coverage",
                "libfuzzer_crash_report",
                "libfuzzer_merge",
                "libfuzzer_regression",
                "generic_analysis",
                "generic_supervisor",
                "generic_merge",
                "generic_generator",
                "generic_crash_report",
                "generic_regression",
                "analysis_single"
            ],
            "title": "TaskType"
        },
        "UserInfo": {
            "properties": {
                "application_id": {
                    "format": "uuid",
                    "title": "Application Id",
                    "type": "string"
                },
                "object_id": {
                    "format": "uuid",
                    "title": "Object Id",
                    "type": "string"
                },
                "upn": {
                    "title": "Upn",
                    "type": "string"
                }
            },
            "title": "UserInfo",
            "type": "object"
        }
    },
    "properties": {
        "config": {
            "$ref": "#/definitions/JobConfig"
        },
        "job_id": {
            "format": "uuid",
            "title": "Job Id",
            "type": "string"
        },
        "task_info": {
            "items": {
                "$ref": "#/definitions/JobTaskStopped"
            },
            "title": "Task Info",
            "type": "array"
        },
        "user_info": {
            "$ref": "#/definitions/UserInfo"
        }
    },
    "required": [
        "job_id",
        "config"
    ],
    "title": "EventJobStopped",
    "type": "object"
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
    "properties": {
        "machine_id": {
            "format": "uuid",
            "title": "Machine Id",
            "type": "string"
        },
        "pool_name": {
            "title": "Pool Name",
            "type": "string"
        },
        "scaleset_id": {
            "format": "uuid",
            "title": "Scaleset Id",
            "type": "string"
        }
    },
    "required": [
        "machine_id",
        "pool_name"
    ],
    "title": "EventNodeCreated",
    "type": "object"
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
    "properties": {
        "machine_id": {
            "format": "uuid",
            "title": "Machine Id",
            "type": "string"
        },
        "pool_name": {
            "title": "Pool Name",
            "type": "string"
        },
        "scaleset_id": {
            "format": "uuid",
            "title": "Scaleset Id",
            "type": "string"
        }
    },
    "required": [
        "machine_id",
        "pool_name"
    ],
    "title": "EventNodeDeleted",
    "type": "object"
}
```

### node_heartbeat

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
    "properties": {
        "machine_id": {
            "format": "uuid",
            "title": "Machine Id",
            "type": "string"
        },
        "pool_name": {
            "title": "Pool Name",
            "type": "string"
        },
        "scaleset_id": {
            "format": "uuid",
            "title": "Scaleset Id",
            "type": "string"
        }
    },
    "required": [
        "machine_id",
        "pool_name"
    ],
    "title": "EventNodeHeartbeat",
    "type": "object"
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
    "definitions": {
        "NodeState": {
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
            ],
            "title": "NodeState"
        }
    },
    "properties": {
        "machine_id": {
            "format": "uuid",
            "title": "Machine Id",
            "type": "string"
        },
        "pool_name": {
            "title": "Pool Name",
            "type": "string"
        },
        "scaleset_id": {
            "format": "uuid",
            "title": "Scaleset Id",
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
    "title": "EventNodeStateUpdated",
    "type": "object"
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
    "properties": {
        "ping_id": {
            "format": "uuid",
            "title": "Ping Id",
            "type": "string"
        }
    },
    "required": [
        "ping_id"
    ],
    "title": "EventPing",
    "type": "object"
}
```

### pool_created

#### Example

```json
{
    "arch": "x86_64",
    "managed": true,
    "os": "linux",
    "pool_name": "example"
}
```

#### Schema

```json
{
    "definitions": {
        "Architecture": {
            "description": "An enumeration.",
            "enum": [
                "x86_64"
            ],
            "title": "Architecture"
        },
        "AutoScaleConfig": {
            "properties": {
                "ephemeral_os_disks": {
                    "default": false,
                    "title": "Ephemeral Os Disks",
                    "type": "boolean"
                },
                "image": {
                    "title": "Image",
                    "type": "string"
                },
                "max_size": {
                    "title": "Max Size",
                    "type": "integer"
                },
                "min_size": {
                    "default": 0,
                    "title": "Min Size",
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
                    "default": false,
                    "title": "Spot Instances",
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
            ],
            "title": "AutoScaleConfig",
            "type": "object"
        },
        "OS": {
            "description": "An enumeration.",
            "enum": [
                "windows",
                "linux"
            ],
            "title": "OS"
        }
    },
    "properties": {
        "arch": {
            "$ref": "#/definitions/Architecture"
        },
        "autoscale": {
            "$ref": "#/definitions/AutoScaleConfig"
        },
        "managed": {
            "title": "Managed",
            "type": "boolean"
        },
        "os": {
            "$ref": "#/definitions/OS"
        },
        "pool_name": {
            "title": "Pool Name",
            "type": "string"
        }
    },
    "required": [
        "pool_name",
        "os",
        "arch",
        "managed"
    ],
    "title": "EventPoolCreated",
    "type": "object"
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
    "properties": {
        "pool_name": {
            "title": "Pool Name",
            "type": "string"
        }
    },
    "required": [
        "pool_name"
    ],
    "title": "EventPoolDeleted",
    "type": "object"
}
```

### proxy_created

#### Example

```json
{
    "proxy_id": "00000000-0000-0000-0000-000000000000",
    "region": "eastus"
}
```

#### Schema

```json
{
    "properties": {
        "proxy_id": {
            "format": "uuid",
            "title": "Proxy Id",
            "type": "string"
        },
        "region": {
            "title": "Region",
            "type": "string"
        }
    },
    "required": [
        "region"
    ],
    "title": "EventProxyCreated",
    "type": "object"
}
```

### proxy_deleted

#### Example

```json
{
    "proxy_id": "00000000-0000-0000-0000-000000000000",
    "region": "eastus"
}
```

#### Schema

```json
{
    "properties": {
        "proxy_id": {
            "format": "uuid",
            "title": "Proxy Id",
            "type": "string"
        },
        "region": {
            "title": "Region",
            "type": "string"
        }
    },
    "required": [
        "region"
    ],
    "title": "EventProxyDeleted",
    "type": "object"
}
```

### proxy_failed

#### Example

```json
{
    "error": {
        "code": 472,
        "errors": [
            "example error message"
        ]
    },
    "proxy_id": "00000000-0000-0000-0000-000000000000",
    "region": "eastus"
}
```

#### Schema

```json
{
    "definitions": {
        "Error": {
            "properties": {
                "code": {
                    "$ref": "#/definitions/ErrorCode"
                },
                "errors": {
                    "items": {
                        "type": "string"
                    },
                    "title": "Errors",
                    "type": "array"
                }
            },
            "required": [
                "code",
                "errors"
            ],
            "title": "Error",
            "type": "object"
        },
        "ErrorCode": {
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
            ],
            "title": "ErrorCode"
        }
    },
    "properties": {
        "error": {
            "$ref": "#/definitions/Error"
        },
        "proxy_id": {
            "format": "uuid",
            "title": "Proxy Id",
            "type": "string"
        },
        "region": {
            "title": "Region",
            "type": "string"
        }
    },
    "required": [
        "region",
        "error"
    ],
    "title": "EventProxyFailed",
    "type": "object"
}
```

### proxy_state_updated

#### Example

```json
{
    "proxy_id": "00000000-0000-0000-0000-000000000000",
    "region": "eastus",
    "state": "init"
}
```

#### Schema

```json
{
    "definitions": {
        "VmState": {
            "description": "An enumeration.",
            "enum": [
                "init",
                "extensions_launch",
                "extensions_failed",
                "vm_allocation_failed",
                "running",
                "stopping",
                "stopped"
            ],
            "title": "VmState"
        }
    },
    "properties": {
        "proxy_id": {
            "format": "uuid",
            "title": "Proxy Id",
            "type": "string"
        },
        "region": {
            "title": "Region",
            "type": "string"
        },
        "state": {
            "$ref": "#/definitions/VmState"
        }
    },
    "required": [
        "region",
        "proxy_id",
        "state"
    ],
    "title": "EventProxyStateUpdated",
    "type": "object"
}
```

### regression_reported

#### Example

```json
{
    "container": "container-name",
    "filename": "example.json",
    "regression_report": {
        "crash_test_result": {
            "crash_report": {
                "asan_log": "example asan log",
                "call_stack": [
                    "#0 line",
                    "#1 line",
                    "#2 line"
                ],
                "call_stack_sha256": "0000000000000000000000000000000000000000000000000000000000000000",
                "crash_site": "example crash site",
                "crash_type": "example crash report type",
                "executable": "fuzz.exe",
                "input_blob": {
                    "account": "contoso-storage-account",
                    "container": "crashes",
                    "name": "input.txt"
                },
                "input_sha256": "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
                "job_id": "00000000-0000-0000-0000-000000000000",
                "scariness_description": "example-scariness",
                "scariness_score": 10,
                "task_id": "00000000-0000-0000-0000-000000000000"
            }
        },
        "original_crash_test_result": {
            "crash_report": {
                "asan_log": "example asan log",
                "call_stack": [
                    "#0 line",
                    "#1 line",
                    "#2 line"
                ],
                "call_stack_sha256": "0000000000000000000000000000000000000000000000000000000000000000",
                "crash_site": "example crash site",
                "crash_type": "example crash report type",
                "executable": "fuzz.exe",
                "input_blob": {
                    "account": "contoso-storage-account",
                    "container": "crashes",
                    "name": "input.txt"
                },
                "input_sha256": "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
                "job_id": "00000000-0000-0000-0000-000000000000",
                "scariness_description": "example-scariness",
                "scariness_score": 10,
                "task_id": "00000000-0000-0000-0000-000000000000"
            }
        }
    }
}
```

#### Schema

```json
{
    "definitions": {
        "BlobRef": {
            "properties": {
                "account": {
                    "title": "Account",
                    "type": "string"
                },
                "container": {
                    "title": "Container",
                    "type": "string"
                },
                "name": {
                    "title": "Name",
                    "type": "string"
                }
            },
            "required": [
                "account",
                "container",
                "name"
            ],
            "title": "BlobRef",
            "type": "object"
        },
        "ContainerType": {
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
                "unique_reports",
                "regression_reports"
            ],
            "title": "ContainerType"
        },
        "CrashTestResult": {
            "properties": {
                "crash_report": {
                    "$ref": "#/definitions/Report"
                },
                "no_repro": {
                    "$ref": "#/definitions/NoReproReport"
                }
            },
            "title": "CrashTestResult",
            "type": "object"
        },
        "NoReproReport": {
            "properties": {
                "error": {
                    "title": "Error",
                    "type": "string"
                },
                "executable": {
                    "title": "Executable",
                    "type": "string"
                },
                "input_blob": {
                    "$ref": "#/definitions/BlobRef"
                },
                "input_sha256": {
                    "title": "Input Sha256",
                    "type": "string"
                },
                "job_id": {
                    "format": "uuid",
                    "title": "Job Id",
                    "type": "string"
                },
                "task_id": {
                    "format": "uuid",
                    "title": "Task Id",
                    "type": "string"
                },
                "tries": {
                    "title": "Tries",
                    "type": "integer"
                }
            },
            "required": [
                "input_sha256",
                "executable",
                "task_id",
                "job_id",
                "tries"
            ],
            "title": "NoReproReport",
            "type": "object"
        },
        "RegressionReport": {
            "properties": {
                "crash_test_result": {
                    "$ref": "#/definitions/CrashTestResult"
                },
                "original_crash_test_result": {
                    "$ref": "#/definitions/CrashTestResult"
                }
            },
            "required": [
                "crash_test_result"
            ],
            "title": "RegressionReport",
            "type": "object"
        },
        "Report": {
            "properties": {
                "asan_log": {
                    "title": "Asan Log",
                    "type": "string"
                },
                "call_stack": {
                    "items": {
                        "type": "string"
                    },
                    "title": "Call Stack",
                    "type": "array"
                },
                "call_stack_sha256": {
                    "title": "Call Stack Sha256",
                    "type": "string"
                },
                "crash_site": {
                    "title": "Crash Site",
                    "type": "string"
                },
                "crash_type": {
                    "title": "Crash Type",
                    "type": "string"
                },
                "executable": {
                    "title": "Executable",
                    "type": "string"
                },
                "input_blob": {
                    "$ref": "#/definitions/BlobRef"
                },
                "input_sha256": {
                    "title": "Input Sha256",
                    "type": "string"
                },
                "input_url": {
                    "title": "Input Url",
                    "type": "string"
                },
                "job_id": {
                    "format": "uuid",
                    "title": "Job Id",
                    "type": "string"
                },
                "minimized_stack": {
                    "items": {
                        "type": "string"
                    },
                    "title": "Minimized Stack",
                    "type": "array"
                },
                "minimized_stack_function_lines": {
                    "items": {
                        "type": "string"
                    },
                    "title": "Minimized Stack Function Lines",
                    "type": "array"
                },
                "minimized_stack_function_lines_sha256": {
                    "title": "Minimized Stack Function Lines Sha256",
                    "type": "string"
                },
                "minimized_stack_function_names": {
                    "items": {
                        "type": "string"
                    },
                    "title": "Minimized Stack Function Names",
                    "type": "array"
                },
                "minimized_stack_function_names_sha256": {
                    "title": "Minimized Stack Function Names Sha256",
                    "type": "string"
                },
                "minimized_stack_sha256": {
                    "title": "Minimized Stack Sha256",
                    "type": "string"
                },
                "scariness_description": {
                    "title": "Scariness Description",
                    "type": "string"
                },
                "scariness_score": {
                    "title": "Scariness Score",
                    "type": "integer"
                },
                "task_id": {
                    "format": "uuid",
                    "title": "Task Id",
                    "type": "string"
                }
            },
            "required": [
                "executable",
                "crash_type",
                "crash_site",
                "call_stack",
                "call_stack_sha256",
                "input_sha256",
                "task_id",
                "job_id"
            ],
            "title": "Report",
            "type": "object"
        },
        "StatsFormat": {
            "description": "An enumeration.",
            "enum": [
                "AFL"
            ],
            "title": "StatsFormat"
        },
        "TaskConfig": {
            "properties": {
                "colocate": {
                    "title": "Colocate",
                    "type": "boolean"
                },
                "containers": {
                    "items": {
                        "$ref": "#/definitions/TaskContainers"
                    },
                    "title": "Containers",
                    "type": "array"
                },
                "debug": {
                    "items": {
                        "$ref": "#/definitions/TaskDebugFlag"
                    },
                    "type": "array"
                },
                "job_id": {
                    "format": "uuid",
                    "title": "Job Id",
                    "type": "string"
                },
                "pool": {
                    "$ref": "#/definitions/TaskPool"
                },
                "prereq_tasks": {
                    "items": {
                        "format": "uuid",
                        "type": "string"
                    },
                    "title": "Prereq Tasks",
                    "type": "array"
                },
                "tags": {
                    "additionalProperties": {
                        "type": "string"
                    },
                    "title": "Tags",
                    "type": "object"
                },
                "task": {
                    "$ref": "#/definitions/TaskDetails"
                },
                "vm": {
                    "$ref": "#/definitions/TaskVm"
                }
            },
            "required": [
                "job_id",
                "task",
                "containers",
                "tags"
            ],
            "title": "TaskConfig",
            "type": "object"
        },
        "TaskContainers": {
            "properties": {
                "name": {
                    "title": "Name",
                    "type": "string"
                },
                "type": {
                    "$ref": "#/definitions/ContainerType"
                }
            },
            "required": [
                "type",
                "name"
            ],
            "title": "TaskContainers",
            "type": "object"
        },
        "TaskDebugFlag": {
            "description": "An enumeration.",
            "enum": [
                "keep_node_on_failure",
                "keep_node_on_completion"
            ],
            "title": "TaskDebugFlag"
        },
        "TaskDetails": {
            "properties": {
                "analyzer_env": {
                    "additionalProperties": {
                        "type": "string"
                    },
                    "title": "Analyzer Env",
                    "type": "object"
                },
                "analyzer_exe": {
                    "title": "Analyzer Exe",
                    "type": "string"
                },
                "analyzer_options": {
                    "items": {
                        "type": "string"
                    },
                    "title": "Analyzer Options",
                    "type": "array"
                },
                "check_asan_log": {
                    "title": "Check Asan Log",
                    "type": "boolean"
                },
                "check_debugger": {
                    "default": true,
                    "title": "Check Debugger",
                    "type": "boolean"
                },
                "check_fuzzer_help": {
                    "title": "Check Fuzzer Help",
                    "type": "boolean"
                },
                "check_retry_count": {
                    "title": "Check Retry Count",
                    "type": "integer"
                },
                "coverage_filter": {
                    "title": "Coverage Filter",
                    "type": "string"
                },
                "duration": {
                    "title": "Duration",
                    "type": "integer"
                },
                "ensemble_sync_delay": {
                    "title": "Ensemble Sync Delay",
                    "type": "integer"
                },
                "expect_crash_on_failure": {
                    "title": "Expect Crash On Failure",
                    "type": "boolean"
                },
                "generator_env": {
                    "additionalProperties": {
                        "type": "string"
                    },
                    "title": "Generator Env",
                    "type": "object"
                },
                "generator_exe": {
                    "title": "Generator Exe",
                    "type": "string"
                },
                "generator_options": {
                    "items": {
                        "type": "string"
                    },
                    "title": "Generator Options",
                    "type": "array"
                },
                "input_file": {
                    "title": "Input File",
                    "type": "string"
                },
                "minimized_stack_depth": {
                    "title": "Minimized Stack Depth",
                    "type": "integer"
                },
                "preserve_existing_outputs": {
                    "title": "Preserve Existing Outputs",
                    "type": "boolean"
                },
                "reboot_after_setup": {
                    "title": "Reboot After Setup",
                    "type": "boolean"
                },
                "rename_output": {
                    "title": "Rename Output",
                    "type": "boolean"
                },
                "report_list": {
                    "items": {
                        "type": "string"
                    },
                    "title": "Report List",
                    "type": "array"
                },
                "stats_file": {
                    "title": "Stats File",
                    "type": "string"
                },
                "stats_format": {
                    "$ref": "#/definitions/StatsFormat"
                },
                "supervisor_env": {
                    "additionalProperties": {
                        "type": "string"
                    },
                    "title": "Supervisor Env",
                    "type": "object"
                },
                "supervisor_exe": {
                    "title": "Supervisor Exe",
                    "type": "string"
                },
                "supervisor_input_marker": {
                    "title": "Supervisor Input Marker",
                    "type": "string"
                },
                "supervisor_options": {
                    "items": {
                        "type": "string"
                    },
                    "title": "Supervisor Options",
                    "type": "array"
                },
                "target_env": {
                    "additionalProperties": {
                        "type": "string"
                    },
                    "title": "Target Env",
                    "type": "object"
                },
                "target_exe": {
                    "title": "Target Exe",
                    "type": "string"
                },
                "target_options": {
                    "items": {
                        "type": "string"
                    },
                    "title": "Target Options",
                    "type": "array"
                },
                "target_options_merge": {
                    "title": "Target Options Merge",
                    "type": "boolean"
                },
                "target_timeout": {
                    "title": "Target Timeout",
                    "type": "integer"
                },
                "target_workers": {
                    "title": "Target Workers",
                    "type": "integer"
                },
                "type": {
                    "$ref": "#/definitions/TaskType"
                },
                "wait_for_files": {
                    "$ref": "#/definitions/ContainerType"
                }
            },
            "required": [
                "type",
                "duration"
            ],
            "title": "TaskDetails",
            "type": "object"
        },
        "TaskPool": {
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
            ],
            "title": "TaskPool",
            "type": "object"
        },
        "TaskType": {
            "description": "An enumeration.",
            "enum": [
                "coverage",
                "libfuzzer_fuzz",
                "libfuzzer_coverage",
                "libfuzzer_crash_report",
                "libfuzzer_merge",
                "libfuzzer_regression",
                "generic_analysis",
                "generic_supervisor",
                "generic_merge",
                "generic_generator",
                "generic_crash_report",
                "generic_regression",
                "analysis_single"
            ],
            "title": "TaskType"
        },
        "TaskVm": {
            "properties": {
                "count": {
                    "default": 1,
                    "title": "Count",
                    "type": "integer"
                },
                "image": {
                    "title": "Image",
                    "type": "string"
                },
                "reboot_after_setup": {
                    "title": "Reboot After Setup",
                    "type": "boolean"
                },
                "region": {
                    "title": "Region",
                    "type": "string"
                },
                "sku": {
                    "title": "Sku",
                    "type": "string"
                },
                "spot_instances": {
                    "default": false,
                    "title": "Spot Instances",
                    "type": "boolean"
                }
            },
            "required": [
                "region",
                "sku",
                "image"
            ],
            "title": "TaskVm",
            "type": "object"
        }
    },
    "properties": {
        "container": {
            "title": "Container",
            "type": "string"
        },
        "filename": {
            "title": "Filename",
            "type": "string"
        },
        "regression_report": {
            "$ref": "#/definitions/RegressionReport"
        },
        "task_config": {
            "$ref": "#/definitions/TaskConfig"
        }
    },
    "required": [
        "regression_report",
        "container",
        "filename"
    ],
    "title": "EventRegressionReported",
    "type": "object"
}
```

### scaleset_created

#### Example

```json
{
    "image": "Canonical:UbuntuServer:18.04-LTS:latest",
    "pool_name": "example",
    "region": "eastus",
    "scaleset_id": "00000000-0000-0000-0000-000000000000",
    "size": 10,
    "vm_sku": "Standard_D2s_v3"
}
```

#### Schema

```json
{
    "properties": {
        "image": {
            "title": "Image",
            "type": "string"
        },
        "pool_name": {
            "title": "Pool Name",
            "type": "string"
        },
        "region": {
            "title": "Region",
            "type": "string"
        },
        "scaleset_id": {
            "format": "uuid",
            "title": "Scaleset Id",
            "type": "string"
        },
        "size": {
            "title": "Size",
            "type": "integer"
        },
        "vm_sku": {
            "title": "Vm Sku",
            "type": "string"
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
    "title": "EventScalesetCreated",
    "type": "object"
}
```

### scaleset_deleted

#### Example

```json
{
    "pool_name": "example",
    "scaleset_id": "00000000-0000-0000-0000-000000000000"
}
```

#### Schema

```json
{
    "properties": {
        "pool_name": {
            "title": "Pool Name",
            "type": "string"
        },
        "scaleset_id": {
            "format": "uuid",
            "title": "Scaleset Id",
            "type": "string"
        }
    },
    "required": [
        "scaleset_id",
        "pool_name"
    ],
    "title": "EventScalesetDeleted",
    "type": "object"
}
```

### scaleset_failed

#### Example

```json
{
    "error": {
        "code": 456,
        "errors": [
            "example error message"
        ]
    },
    "pool_name": "example",
    "scaleset_id": "00000000-0000-0000-0000-000000000000"
}
```

#### Schema

```json
{
    "definitions": {
        "Error": {
            "properties": {
                "code": {
                    "$ref": "#/definitions/ErrorCode"
                },
                "errors": {
                    "items": {
                        "type": "string"
                    },
                    "title": "Errors",
                    "type": "array"
                }
            },
            "required": [
                "code",
                "errors"
            ],
            "title": "Error",
            "type": "object"
        },
        "ErrorCode": {
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
            ],
            "title": "ErrorCode"
        }
    },
    "properties": {
        "error": {
            "$ref": "#/definitions/Error"
        },
        "pool_name": {
            "title": "Pool Name",
            "type": "string"
        },
        "scaleset_id": {
            "format": "uuid",
            "title": "Scaleset Id",
            "type": "string"
        }
    },
    "required": [
        "scaleset_id",
        "pool_name",
        "error"
    ],
    "title": "EventScalesetFailed",
    "type": "object"
}
```

### scaleset_state_updated

#### Example

```json
{
    "pool_name": "example",
    "scaleset_id": "00000000-0000-0000-0000-000000000000",
    "state": "init"
}
```

#### Schema

```json
{
    "definitions": {
        "ScalesetState": {
            "description": "An enumeration.",
            "enum": [
                "init",
                "setup",
                "resize",
                "running",
                "shutdown",
                "halt",
                "creation_failed"
            ],
            "title": "ScalesetState"
        }
    },
    "properties": {
        "pool_name": {
            "title": "Pool Name",
            "type": "string"
        },
        "scaleset_id": {
            "format": "uuid",
            "title": "Scaleset Id",
            "type": "string"
        },
        "state": {
            "$ref": "#/definitions/ScalesetState"
        }
    },
    "required": [
        "scaleset_id",
        "pool_name",
        "state"
    ],
    "title": "EventScalesetStateUpdated",
    "type": "object"
}
```

### task_created

#### Example

```json
{
    "config": {
        "containers": [
            {
                "name": "my-setup",
                "type": "setup"
            },
            {
                "name": "my-inputs",
                "type": "inputs"
            },
            {
                "name": "my-crashes",
                "type": "crashes"
            }
        ],
        "job_id": "00000000-0000-0000-0000-000000000000",
        "tags": {},
        "task": {
            "check_debugger": true,
            "duration": 1,
            "target_env": {},
            "target_exe": "fuzz.exe",
            "target_options": [],
            "type": "libfuzzer_fuzz"
        }
    },
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
    "definitions": {
        "ContainerType": {
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
                "unique_reports",
                "regression_reports"
            ],
            "title": "ContainerType"
        },
        "StatsFormat": {
            "description": "An enumeration.",
            "enum": [
                "AFL"
            ],
            "title": "StatsFormat"
        },
        "TaskConfig": {
            "properties": {
                "colocate": {
                    "title": "Colocate",
                    "type": "boolean"
                },
                "containers": {
                    "items": {
                        "$ref": "#/definitions/TaskContainers"
                    },
                    "title": "Containers",
                    "type": "array"
                },
                "debug": {
                    "items": {
                        "$ref": "#/definitions/TaskDebugFlag"
                    },
                    "type": "array"
                },
                "job_id": {
                    "format": "uuid",
                    "title": "Job Id",
                    "type": "string"
                },
                "pool": {
                    "$ref": "#/definitions/TaskPool"
                },
                "prereq_tasks": {
                    "items": {
                        "format": "uuid",
                        "type": "string"
                    },
                    "title": "Prereq Tasks",
                    "type": "array"
                },
                "tags": {
                    "additionalProperties": {
                        "type": "string"
                    },
                    "title": "Tags",
                    "type": "object"
                },
                "task": {
                    "$ref": "#/definitions/TaskDetails"
                },
                "vm": {
                    "$ref": "#/definitions/TaskVm"
                }
            },
            "required": [
                "job_id",
                "task",
                "containers",
                "tags"
            ],
            "title": "TaskConfig",
            "type": "object"
        },
        "TaskContainers": {
            "properties": {
                "name": {
                    "title": "Name",
                    "type": "string"
                },
                "type": {
                    "$ref": "#/definitions/ContainerType"
                }
            },
            "required": [
                "type",
                "name"
            ],
            "title": "TaskContainers",
            "type": "object"
        },
        "TaskDebugFlag": {
            "description": "An enumeration.",
            "enum": [
                "keep_node_on_failure",
                "keep_node_on_completion"
            ],
            "title": "TaskDebugFlag"
        },
        "TaskDetails": {
            "properties": {
                "analyzer_env": {
                    "additionalProperties": {
                        "type": "string"
                    },
                    "title": "Analyzer Env",
                    "type": "object"
                },
                "analyzer_exe": {
                    "title": "Analyzer Exe",
                    "type": "string"
                },
                "analyzer_options": {
                    "items": {
                        "type": "string"
                    },
                    "title": "Analyzer Options",
                    "type": "array"
                },
                "check_asan_log": {
                    "title": "Check Asan Log",
                    "type": "boolean"
                },
                "check_debugger": {
                    "default": true,
                    "title": "Check Debugger",
                    "type": "boolean"
                },
                "check_fuzzer_help": {
                    "title": "Check Fuzzer Help",
                    "type": "boolean"
                },
                "check_retry_count": {
                    "title": "Check Retry Count",
                    "type": "integer"
                },
                "coverage_filter": {
                    "title": "Coverage Filter",
                    "type": "string"
                },
                "duration": {
                    "title": "Duration",
                    "type": "integer"
                },
                "ensemble_sync_delay": {
                    "title": "Ensemble Sync Delay",
                    "type": "integer"
                },
                "expect_crash_on_failure": {
                    "title": "Expect Crash On Failure",
                    "type": "boolean"
                },
                "generator_env": {
                    "additionalProperties": {
                        "type": "string"
                    },
                    "title": "Generator Env",
                    "type": "object"
                },
                "generator_exe": {
                    "title": "Generator Exe",
                    "type": "string"
                },
                "generator_options": {
                    "items": {
                        "type": "string"
                    },
                    "title": "Generator Options",
                    "type": "array"
                },
                "input_file": {
                    "title": "Input File",
                    "type": "string"
                },
                "minimized_stack_depth": {
                    "title": "Minimized Stack Depth",
                    "type": "integer"
                },
                "preserve_existing_outputs": {
                    "title": "Preserve Existing Outputs",
                    "type": "boolean"
                },
                "reboot_after_setup": {
                    "title": "Reboot After Setup",
                    "type": "boolean"
                },
                "rename_output": {
                    "title": "Rename Output",
                    "type": "boolean"
                },
                "report_list": {
                    "items": {
                        "type": "string"
                    },
                    "title": "Report List",
                    "type": "array"
                },
                "stats_file": {
                    "title": "Stats File",
                    "type": "string"
                },
                "stats_format": {
                    "$ref": "#/definitions/StatsFormat"
                },
                "supervisor_env": {
                    "additionalProperties": {
                        "type": "string"
                    },
                    "title": "Supervisor Env",
                    "type": "object"
                },
                "supervisor_exe": {
                    "title": "Supervisor Exe",
                    "type": "string"
                },
                "supervisor_input_marker": {
                    "title": "Supervisor Input Marker",
                    "type": "string"
                },
                "supervisor_options": {
                    "items": {
                        "type": "string"
                    },
                    "title": "Supervisor Options",
                    "type": "array"
                },
                "target_env": {
                    "additionalProperties": {
                        "type": "string"
                    },
                    "title": "Target Env",
                    "type": "object"
                },
                "target_exe": {
                    "title": "Target Exe",
                    "type": "string"
                },
                "target_options": {
                    "items": {
                        "type": "string"
                    },
                    "title": "Target Options",
                    "type": "array"
                },
                "target_options_merge": {
                    "title": "Target Options Merge",
                    "type": "boolean"
                },
                "target_timeout": {
                    "title": "Target Timeout",
                    "type": "integer"
                },
                "target_workers": {
                    "title": "Target Workers",
                    "type": "integer"
                },
                "type": {
                    "$ref": "#/definitions/TaskType"
                },
                "wait_for_files": {
                    "$ref": "#/definitions/ContainerType"
                }
            },
            "required": [
                "type",
                "duration"
            ],
            "title": "TaskDetails",
            "type": "object"
        },
        "TaskPool": {
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
            ],
            "title": "TaskPool",
            "type": "object"
        },
        "TaskType": {
            "description": "An enumeration.",
            "enum": [
                "coverage",
                "libfuzzer_fuzz",
                "libfuzzer_coverage",
                "libfuzzer_crash_report",
                "libfuzzer_merge",
                "libfuzzer_regression",
                "generic_analysis",
                "generic_supervisor",
                "generic_merge",
                "generic_generator",
                "generic_crash_report",
                "generic_regression",
                "analysis_single"
            ],
            "title": "TaskType"
        },
        "TaskVm": {
            "properties": {
                "count": {
                    "default": 1,
                    "title": "Count",
                    "type": "integer"
                },
                "image": {
                    "title": "Image",
                    "type": "string"
                },
                "reboot_after_setup": {
                    "title": "Reboot After Setup",
                    "type": "boolean"
                },
                "region": {
                    "title": "Region",
                    "type": "string"
                },
                "sku": {
                    "title": "Sku",
                    "type": "string"
                },
                "spot_instances": {
                    "default": false,
                    "title": "Spot Instances",
                    "type": "boolean"
                }
            },
            "required": [
                "region",
                "sku",
                "image"
            ],
            "title": "TaskVm",
            "type": "object"
        },
        "UserInfo": {
            "properties": {
                "application_id": {
                    "format": "uuid",
                    "title": "Application Id",
                    "type": "string"
                },
                "object_id": {
                    "format": "uuid",
                    "title": "Object Id",
                    "type": "string"
                },
                "upn": {
                    "title": "Upn",
                    "type": "string"
                }
            },
            "title": "UserInfo",
            "type": "object"
        }
    },
    "properties": {
        "config": {
            "$ref": "#/definitions/TaskConfig"
        },
        "job_id": {
            "format": "uuid",
            "title": "Job Id",
            "type": "string"
        },
        "task_id": {
            "format": "uuid",
            "title": "Task Id",
            "type": "string"
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
    "title": "EventTaskCreated",
    "type": "object"
}
```

### task_failed

#### Example

```json
{
    "config": {
        "containers": [
            {
                "name": "my-setup",
                "type": "setup"
            },
            {
                "name": "my-inputs",
                "type": "inputs"
            },
            {
                "name": "my-crashes",
                "type": "crashes"
            }
        ],
        "job_id": "00000000-0000-0000-0000-000000000000",
        "tags": {},
        "task": {
            "check_debugger": true,
            "duration": 1,
            "target_env": {},
            "target_exe": "fuzz.exe",
            "target_options": [],
            "type": "libfuzzer_fuzz"
        }
    },
    "error": {
        "code": 468,
        "errors": [
            "example error message"
        ]
    },
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
    "definitions": {
        "ContainerType": {
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
                "unique_reports",
                "regression_reports"
            ],
            "title": "ContainerType"
        },
        "Error": {
            "properties": {
                "code": {
                    "$ref": "#/definitions/ErrorCode"
                },
                "errors": {
                    "items": {
                        "type": "string"
                    },
                    "title": "Errors",
                    "type": "array"
                }
            },
            "required": [
                "code",
                "errors"
            ],
            "title": "Error",
            "type": "object"
        },
        "ErrorCode": {
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
            ],
            "title": "ErrorCode"
        },
        "StatsFormat": {
            "description": "An enumeration.",
            "enum": [
                "AFL"
            ],
            "title": "StatsFormat"
        },
        "TaskConfig": {
            "properties": {
                "colocate": {
                    "title": "Colocate",
                    "type": "boolean"
                },
                "containers": {
                    "items": {
                        "$ref": "#/definitions/TaskContainers"
                    },
                    "title": "Containers",
                    "type": "array"
                },
                "debug": {
                    "items": {
                        "$ref": "#/definitions/TaskDebugFlag"
                    },
                    "type": "array"
                },
                "job_id": {
                    "format": "uuid",
                    "title": "Job Id",
                    "type": "string"
                },
                "pool": {
                    "$ref": "#/definitions/TaskPool"
                },
                "prereq_tasks": {
                    "items": {
                        "format": "uuid",
                        "type": "string"
                    },
                    "title": "Prereq Tasks",
                    "type": "array"
                },
                "tags": {
                    "additionalProperties": {
                        "type": "string"
                    },
                    "title": "Tags",
                    "type": "object"
                },
                "task": {
                    "$ref": "#/definitions/TaskDetails"
                },
                "vm": {
                    "$ref": "#/definitions/TaskVm"
                }
            },
            "required": [
                "job_id",
                "task",
                "containers",
                "tags"
            ],
            "title": "TaskConfig",
            "type": "object"
        },
        "TaskContainers": {
            "properties": {
                "name": {
                    "title": "Name",
                    "type": "string"
                },
                "type": {
                    "$ref": "#/definitions/ContainerType"
                }
            },
            "required": [
                "type",
                "name"
            ],
            "title": "TaskContainers",
            "type": "object"
        },
        "TaskDebugFlag": {
            "description": "An enumeration.",
            "enum": [
                "keep_node_on_failure",
                "keep_node_on_completion"
            ],
            "title": "TaskDebugFlag"
        },
        "TaskDetails": {
            "properties": {
                "analyzer_env": {
                    "additionalProperties": {
                        "type": "string"
                    },
                    "title": "Analyzer Env",
                    "type": "object"
                },
                "analyzer_exe": {
                    "title": "Analyzer Exe",
                    "type": "string"
                },
                "analyzer_options": {
                    "items": {
                        "type": "string"
                    },
                    "title": "Analyzer Options",
                    "type": "array"
                },
                "check_asan_log": {
                    "title": "Check Asan Log",
                    "type": "boolean"
                },
                "check_debugger": {
                    "default": true,
                    "title": "Check Debugger",
                    "type": "boolean"
                },
                "check_fuzzer_help": {
                    "title": "Check Fuzzer Help",
                    "type": "boolean"
                },
                "check_retry_count": {
                    "title": "Check Retry Count",
                    "type": "integer"
                },
                "coverage_filter": {
                    "title": "Coverage Filter",
                    "type": "string"
                },
                "duration": {
                    "title": "Duration",
                    "type": "integer"
                },
                "ensemble_sync_delay": {
                    "title": "Ensemble Sync Delay",
                    "type": "integer"
                },
                "expect_crash_on_failure": {
                    "title": "Expect Crash On Failure",
                    "type": "boolean"
                },
                "generator_env": {
                    "additionalProperties": {
                        "type": "string"
                    },
                    "title": "Generator Env",
                    "type": "object"
                },
                "generator_exe": {
                    "title": "Generator Exe",
                    "type": "string"
                },
                "generator_options": {
                    "items": {
                        "type": "string"
                    },
                    "title": "Generator Options",
                    "type": "array"
                },
                "input_file": {
                    "title": "Input File",
                    "type": "string"
                },
                "minimized_stack_depth": {
                    "title": "Minimized Stack Depth",
                    "type": "integer"
                },
                "preserve_existing_outputs": {
                    "title": "Preserve Existing Outputs",
                    "type": "boolean"
                },
                "reboot_after_setup": {
                    "title": "Reboot After Setup",
                    "type": "boolean"
                },
                "rename_output": {
                    "title": "Rename Output",
                    "type": "boolean"
                },
                "report_list": {
                    "items": {
                        "type": "string"
                    },
                    "title": "Report List",
                    "type": "array"
                },
                "stats_file": {
                    "title": "Stats File",
                    "type": "string"
                },
                "stats_format": {
                    "$ref": "#/definitions/StatsFormat"
                },
                "supervisor_env": {
                    "additionalProperties": {
                        "type": "string"
                    },
                    "title": "Supervisor Env",
                    "type": "object"
                },
                "supervisor_exe": {
                    "title": "Supervisor Exe",
                    "type": "string"
                },
                "supervisor_input_marker": {
                    "title": "Supervisor Input Marker",
                    "type": "string"
                },
                "supervisor_options": {
                    "items": {
                        "type": "string"
                    },
                    "title": "Supervisor Options",
                    "type": "array"
                },
                "target_env": {
                    "additionalProperties": {
                        "type": "string"
                    },
                    "title": "Target Env",
                    "type": "object"
                },
                "target_exe": {
                    "title": "Target Exe",
                    "type": "string"
                },
                "target_options": {
                    "items": {
                        "type": "string"
                    },
                    "title": "Target Options",
                    "type": "array"
                },
                "target_options_merge": {
                    "title": "Target Options Merge",
                    "type": "boolean"
                },
                "target_timeout": {
                    "title": "Target Timeout",
                    "type": "integer"
                },
                "target_workers": {
                    "title": "Target Workers",
                    "type": "integer"
                },
                "type": {
                    "$ref": "#/definitions/TaskType"
                },
                "wait_for_files": {
                    "$ref": "#/definitions/ContainerType"
                }
            },
            "required": [
                "type",
                "duration"
            ],
            "title": "TaskDetails",
            "type": "object"
        },
        "TaskPool": {
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
            ],
            "title": "TaskPool",
            "type": "object"
        },
        "TaskType": {
            "description": "An enumeration.",
            "enum": [
                "coverage",
                "libfuzzer_fuzz",
                "libfuzzer_coverage",
                "libfuzzer_crash_report",
                "libfuzzer_merge",
                "libfuzzer_regression",
                "generic_analysis",
                "generic_supervisor",
                "generic_merge",
                "generic_generator",
                "generic_crash_report",
                "generic_regression",
                "analysis_single"
            ],
            "title": "TaskType"
        },
        "TaskVm": {
            "properties": {
                "count": {
                    "default": 1,
                    "title": "Count",
                    "type": "integer"
                },
                "image": {
                    "title": "Image",
                    "type": "string"
                },
                "reboot_after_setup": {
                    "title": "Reboot After Setup",
                    "type": "boolean"
                },
                "region": {
                    "title": "Region",
                    "type": "string"
                },
                "sku": {
                    "title": "Sku",
                    "type": "string"
                },
                "spot_instances": {
                    "default": false,
                    "title": "Spot Instances",
                    "type": "boolean"
                }
            },
            "required": [
                "region",
                "sku",
                "image"
            ],
            "title": "TaskVm",
            "type": "object"
        },
        "UserInfo": {
            "properties": {
                "application_id": {
                    "format": "uuid",
                    "title": "Application Id",
                    "type": "string"
                },
                "object_id": {
                    "format": "uuid",
                    "title": "Object Id",
                    "type": "string"
                },
                "upn": {
                    "title": "Upn",
                    "type": "string"
                }
            },
            "title": "UserInfo",
            "type": "object"
        }
    },
    "properties": {
        "config": {
            "$ref": "#/definitions/TaskConfig"
        },
        "error": {
            "$ref": "#/definitions/Error"
        },
        "job_id": {
            "format": "uuid",
            "title": "Job Id",
            "type": "string"
        },
        "task_id": {
            "format": "uuid",
            "title": "Task Id",
            "type": "string"
        },
        "user_info": {
            "$ref": "#/definitions/UserInfo"
        }
    },
    "required": [
        "job_id",
        "task_id",
        "error",
        "config"
    ],
    "title": "EventTaskFailed",
    "type": "object"
}
```

### task_heartbeat

#### Example

```json
{
    "config": {
        "containers": [
            {
                "name": "my-setup",
                "type": "setup"
            },
            {
                "name": "my-inputs",
                "type": "inputs"
            },
            {
                "name": "my-crashes",
                "type": "crashes"
            }
        ],
        "job_id": "00000000-0000-0000-0000-000000000000",
        "tags": {},
        "task": {
            "check_debugger": true,
            "duration": 1,
            "target_env": {},
            "target_exe": "fuzz.exe",
            "target_options": [],
            "type": "libfuzzer_fuzz"
        }
    },
    "job_id": "00000000-0000-0000-0000-000000000000",
    "task_id": "00000000-0000-0000-0000-000000000000"
}
```

#### Schema

```json
{
    "definitions": {
        "ContainerType": {
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
                "unique_reports",
                "regression_reports"
            ],
            "title": "ContainerType"
        },
        "StatsFormat": {
            "description": "An enumeration.",
            "enum": [
                "AFL"
            ],
            "title": "StatsFormat"
        },
        "TaskConfig": {
            "properties": {
                "colocate": {
                    "title": "Colocate",
                    "type": "boolean"
                },
                "containers": {
                    "items": {
                        "$ref": "#/definitions/TaskContainers"
                    },
                    "title": "Containers",
                    "type": "array"
                },
                "debug": {
                    "items": {
                        "$ref": "#/definitions/TaskDebugFlag"
                    },
                    "type": "array"
                },
                "job_id": {
                    "format": "uuid",
                    "title": "Job Id",
                    "type": "string"
                },
                "pool": {
                    "$ref": "#/definitions/TaskPool"
                },
                "prereq_tasks": {
                    "items": {
                        "format": "uuid",
                        "type": "string"
                    },
                    "title": "Prereq Tasks",
                    "type": "array"
                },
                "tags": {
                    "additionalProperties": {
                        "type": "string"
                    },
                    "title": "Tags",
                    "type": "object"
                },
                "task": {
                    "$ref": "#/definitions/TaskDetails"
                },
                "vm": {
                    "$ref": "#/definitions/TaskVm"
                }
            },
            "required": [
                "job_id",
                "task",
                "containers",
                "tags"
            ],
            "title": "TaskConfig",
            "type": "object"
        },
        "TaskContainers": {
            "properties": {
                "name": {
                    "title": "Name",
                    "type": "string"
                },
                "type": {
                    "$ref": "#/definitions/ContainerType"
                }
            },
            "required": [
                "type",
                "name"
            ],
            "title": "TaskContainers",
            "type": "object"
        },
        "TaskDebugFlag": {
            "description": "An enumeration.",
            "enum": [
                "keep_node_on_failure",
                "keep_node_on_completion"
            ],
            "title": "TaskDebugFlag"
        },
        "TaskDetails": {
            "properties": {
                "analyzer_env": {
                    "additionalProperties": {
                        "type": "string"
                    },
                    "title": "Analyzer Env",
                    "type": "object"
                },
                "analyzer_exe": {
                    "title": "Analyzer Exe",
                    "type": "string"
                },
                "analyzer_options": {
                    "items": {
                        "type": "string"
                    },
                    "title": "Analyzer Options",
                    "type": "array"
                },
                "check_asan_log": {
                    "title": "Check Asan Log",
                    "type": "boolean"
                },
                "check_debugger": {
                    "default": true,
                    "title": "Check Debugger",
                    "type": "boolean"
                },
                "check_fuzzer_help": {
                    "title": "Check Fuzzer Help",
                    "type": "boolean"
                },
                "check_retry_count": {
                    "title": "Check Retry Count",
                    "type": "integer"
                },
                "coverage_filter": {
                    "title": "Coverage Filter",
                    "type": "string"
                },
                "duration": {
                    "title": "Duration",
                    "type": "integer"
                },
                "ensemble_sync_delay": {
                    "title": "Ensemble Sync Delay",
                    "type": "integer"
                },
                "expect_crash_on_failure": {
                    "title": "Expect Crash On Failure",
                    "type": "boolean"
                },
                "generator_env": {
                    "additionalProperties": {
                        "type": "string"
                    },
                    "title": "Generator Env",
                    "type": "object"
                },
                "generator_exe": {
                    "title": "Generator Exe",
                    "type": "string"
                },
                "generator_options": {
                    "items": {
                        "type": "string"
                    },
                    "title": "Generator Options",
                    "type": "array"
                },
                "input_file": {
                    "title": "Input File",
                    "type": "string"
                },
                "minimized_stack_depth": {
                    "title": "Minimized Stack Depth",
                    "type": "integer"
                },
                "preserve_existing_outputs": {
                    "title": "Preserve Existing Outputs",
                    "type": "boolean"
                },
                "reboot_after_setup": {
                    "title": "Reboot After Setup",
                    "type": "boolean"
                },
                "rename_output": {
                    "title": "Rename Output",
                    "type": "boolean"
                },
                "report_list": {
                    "items": {
                        "type": "string"
                    },
                    "title": "Report List",
                    "type": "array"
                },
                "stats_file": {
                    "title": "Stats File",
                    "type": "string"
                },
                "stats_format": {
                    "$ref": "#/definitions/StatsFormat"
                },
                "supervisor_env": {
                    "additionalProperties": {
                        "type": "string"
                    },
                    "title": "Supervisor Env",
                    "type": "object"
                },
                "supervisor_exe": {
                    "title": "Supervisor Exe",
                    "type": "string"
                },
                "supervisor_input_marker": {
                    "title": "Supervisor Input Marker",
                    "type": "string"
                },
                "supervisor_options": {
                    "items": {
                        "type": "string"
                    },
                    "title": "Supervisor Options",
                    "type": "array"
                },
                "target_env": {
                    "additionalProperties": {
                        "type": "string"
                    },
                    "title": "Target Env",
                    "type": "object"
                },
                "target_exe": {
                    "title": "Target Exe",
                    "type": "string"
                },
                "target_options": {
                    "items": {
                        "type": "string"
                    },
                    "title": "Target Options",
                    "type": "array"
                },
                "target_options_merge": {
                    "title": "Target Options Merge",
                    "type": "boolean"
                },
                "target_timeout": {
                    "title": "Target Timeout",
                    "type": "integer"
                },
                "target_workers": {
                    "title": "Target Workers",
                    "type": "integer"
                },
                "type": {
                    "$ref": "#/definitions/TaskType"
                },
                "wait_for_files": {
                    "$ref": "#/definitions/ContainerType"
                }
            },
            "required": [
                "type",
                "duration"
            ],
            "title": "TaskDetails",
            "type": "object"
        },
        "TaskPool": {
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
            ],
            "title": "TaskPool",
            "type": "object"
        },
        "TaskType": {
            "description": "An enumeration.",
            "enum": [
                "coverage",
                "libfuzzer_fuzz",
                "libfuzzer_coverage",
                "libfuzzer_crash_report",
                "libfuzzer_merge",
                "libfuzzer_regression",
                "generic_analysis",
                "generic_supervisor",
                "generic_merge",
                "generic_generator",
                "generic_crash_report",
                "generic_regression",
                "analysis_single"
            ],
            "title": "TaskType"
        },
        "TaskVm": {
            "properties": {
                "count": {
                    "default": 1,
                    "title": "Count",
                    "type": "integer"
                },
                "image": {
                    "title": "Image",
                    "type": "string"
                },
                "reboot_after_setup": {
                    "title": "Reboot After Setup",
                    "type": "boolean"
                },
                "region": {
                    "title": "Region",
                    "type": "string"
                },
                "sku": {
                    "title": "Sku",
                    "type": "string"
                },
                "spot_instances": {
                    "default": false,
                    "title": "Spot Instances",
                    "type": "boolean"
                }
            },
            "required": [
                "region",
                "sku",
                "image"
            ],
            "title": "TaskVm",
            "type": "object"
        }
    },
    "properties": {
        "config": {
            "$ref": "#/definitions/TaskConfig"
        },
        "job_id": {
            "format": "uuid",
            "title": "Job Id",
            "type": "string"
        },
        "task_id": {
            "format": "uuid",
            "title": "Task Id",
            "type": "string"
        }
    },
    "required": [
        "job_id",
        "task_id",
        "config"
    ],
    "title": "EventTaskHeartbeat",
    "type": "object"
}
```

### task_state_updated

#### Example

```json
{
    "config": {
        "containers": [
            {
                "name": "my-setup",
                "type": "setup"
            },
            {
                "name": "my-inputs",
                "type": "inputs"
            },
            {
                "name": "my-crashes",
                "type": "crashes"
            }
        ],
        "job_id": "00000000-0000-0000-0000-000000000000",
        "tags": {},
        "task": {
            "check_debugger": true,
            "duration": 1,
            "target_env": {},
            "target_exe": "fuzz.exe",
            "target_options": [],
            "type": "libfuzzer_fuzz"
        }
    },
    "job_id": "00000000-0000-0000-0000-000000000000",
    "state": "init",
    "task_id": "00000000-0000-0000-0000-000000000000"
}
```

#### Schema

```json
{
    "definitions": {
        "ContainerType": {
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
                "unique_reports",
                "regression_reports"
            ],
            "title": "ContainerType"
        },
        "StatsFormat": {
            "description": "An enumeration.",
            "enum": [
                "AFL"
            ],
            "title": "StatsFormat"
        },
        "TaskConfig": {
            "properties": {
                "colocate": {
                    "title": "Colocate",
                    "type": "boolean"
                },
                "containers": {
                    "items": {
                        "$ref": "#/definitions/TaskContainers"
                    },
                    "title": "Containers",
                    "type": "array"
                },
                "debug": {
                    "items": {
                        "$ref": "#/definitions/TaskDebugFlag"
                    },
                    "type": "array"
                },
                "job_id": {
                    "format": "uuid",
                    "title": "Job Id",
                    "type": "string"
                },
                "pool": {
                    "$ref": "#/definitions/TaskPool"
                },
                "prereq_tasks": {
                    "items": {
                        "format": "uuid",
                        "type": "string"
                    },
                    "title": "Prereq Tasks",
                    "type": "array"
                },
                "tags": {
                    "additionalProperties": {
                        "type": "string"
                    },
                    "title": "Tags",
                    "type": "object"
                },
                "task": {
                    "$ref": "#/definitions/TaskDetails"
                },
                "vm": {
                    "$ref": "#/definitions/TaskVm"
                }
            },
            "required": [
                "job_id",
                "task",
                "containers",
                "tags"
            ],
            "title": "TaskConfig",
            "type": "object"
        },
        "TaskContainers": {
            "properties": {
                "name": {
                    "title": "Name",
                    "type": "string"
                },
                "type": {
                    "$ref": "#/definitions/ContainerType"
                }
            },
            "required": [
                "type",
                "name"
            ],
            "title": "TaskContainers",
            "type": "object"
        },
        "TaskDebugFlag": {
            "description": "An enumeration.",
            "enum": [
                "keep_node_on_failure",
                "keep_node_on_completion"
            ],
            "title": "TaskDebugFlag"
        },
        "TaskDetails": {
            "properties": {
                "analyzer_env": {
                    "additionalProperties": {
                        "type": "string"
                    },
                    "title": "Analyzer Env",
                    "type": "object"
                },
                "analyzer_exe": {
                    "title": "Analyzer Exe",
                    "type": "string"
                },
                "analyzer_options": {
                    "items": {
                        "type": "string"
                    },
                    "title": "Analyzer Options",
                    "type": "array"
                },
                "check_asan_log": {
                    "title": "Check Asan Log",
                    "type": "boolean"
                },
                "check_debugger": {
                    "default": true,
                    "title": "Check Debugger",
                    "type": "boolean"
                },
                "check_fuzzer_help": {
                    "title": "Check Fuzzer Help",
                    "type": "boolean"
                },
                "check_retry_count": {
                    "title": "Check Retry Count",
                    "type": "integer"
                },
                "coverage_filter": {
                    "title": "Coverage Filter",
                    "type": "string"
                },
                "duration": {
                    "title": "Duration",
                    "type": "integer"
                },
                "ensemble_sync_delay": {
                    "title": "Ensemble Sync Delay",
                    "type": "integer"
                },
                "expect_crash_on_failure": {
                    "title": "Expect Crash On Failure",
                    "type": "boolean"
                },
                "generator_env": {
                    "additionalProperties": {
                        "type": "string"
                    },
                    "title": "Generator Env",
                    "type": "object"
                },
                "generator_exe": {
                    "title": "Generator Exe",
                    "type": "string"
                },
                "generator_options": {
                    "items": {
                        "type": "string"
                    },
                    "title": "Generator Options",
                    "type": "array"
                },
                "input_file": {
                    "title": "Input File",
                    "type": "string"
                },
                "minimized_stack_depth": {
                    "title": "Minimized Stack Depth",
                    "type": "integer"
                },
                "preserve_existing_outputs": {
                    "title": "Preserve Existing Outputs",
                    "type": "boolean"
                },
                "reboot_after_setup": {
                    "title": "Reboot After Setup",
                    "type": "boolean"
                },
                "rename_output": {
                    "title": "Rename Output",
                    "type": "boolean"
                },
                "report_list": {
                    "items": {
                        "type": "string"
                    },
                    "title": "Report List",
                    "type": "array"
                },
                "stats_file": {
                    "title": "Stats File",
                    "type": "string"
                },
                "stats_format": {
                    "$ref": "#/definitions/StatsFormat"
                },
                "supervisor_env": {
                    "additionalProperties": {
                        "type": "string"
                    },
                    "title": "Supervisor Env",
                    "type": "object"
                },
                "supervisor_exe": {
                    "title": "Supervisor Exe",
                    "type": "string"
                },
                "supervisor_input_marker": {
                    "title": "Supervisor Input Marker",
                    "type": "string"
                },
                "supervisor_options": {
                    "items": {
                        "type": "string"
                    },
                    "title": "Supervisor Options",
                    "type": "array"
                },
                "target_env": {
                    "additionalProperties": {
                        "type": "string"
                    },
                    "title": "Target Env",
                    "type": "object"
                },
                "target_exe": {
                    "title": "Target Exe",
                    "type": "string"
                },
                "target_options": {
                    "items": {
                        "type": "string"
                    },
                    "title": "Target Options",
                    "type": "array"
                },
                "target_options_merge": {
                    "title": "Target Options Merge",
                    "type": "boolean"
                },
                "target_timeout": {
                    "title": "Target Timeout",
                    "type": "integer"
                },
                "target_workers": {
                    "title": "Target Workers",
                    "type": "integer"
                },
                "type": {
                    "$ref": "#/definitions/TaskType"
                },
                "wait_for_files": {
                    "$ref": "#/definitions/ContainerType"
                }
            },
            "required": [
                "type",
                "duration"
            ],
            "title": "TaskDetails",
            "type": "object"
        },
        "TaskPool": {
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
            ],
            "title": "TaskPool",
            "type": "object"
        },
        "TaskState": {
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
            ],
            "title": "TaskState"
        },
        "TaskType": {
            "description": "An enumeration.",
            "enum": [
                "coverage",
                "libfuzzer_fuzz",
                "libfuzzer_coverage",
                "libfuzzer_crash_report",
                "libfuzzer_merge",
                "libfuzzer_regression",
                "generic_analysis",
                "generic_supervisor",
                "generic_merge",
                "generic_generator",
                "generic_crash_report",
                "generic_regression",
                "analysis_single"
            ],
            "title": "TaskType"
        },
        "TaskVm": {
            "properties": {
                "count": {
                    "default": 1,
                    "title": "Count",
                    "type": "integer"
                },
                "image": {
                    "title": "Image",
                    "type": "string"
                },
                "reboot_after_setup": {
                    "title": "Reboot After Setup",
                    "type": "boolean"
                },
                "region": {
                    "title": "Region",
                    "type": "string"
                },
                "sku": {
                    "title": "Sku",
                    "type": "string"
                },
                "spot_instances": {
                    "default": false,
                    "title": "Spot Instances",
                    "type": "boolean"
                }
            },
            "required": [
                "region",
                "sku",
                "image"
            ],
            "title": "TaskVm",
            "type": "object"
        }
    },
    "properties": {
        "config": {
            "$ref": "#/definitions/TaskConfig"
        },
        "end_time": {
            "format": "date-time",
            "title": "End Time",
            "type": "string"
        },
        "job_id": {
            "format": "uuid",
            "title": "Job Id",
            "type": "string"
        },
        "state": {
            "$ref": "#/definitions/TaskState"
        },
        "task_id": {
            "format": "uuid",
            "title": "Task Id",
            "type": "string"
        }
    },
    "required": [
        "job_id",
        "task_id",
        "state",
        "config"
    ],
    "title": "EventTaskStateUpdated",
    "type": "object"
}
```

### task_stopped

#### Example

```json
{
    "config": {
        "containers": [
            {
                "name": "my-setup",
                "type": "setup"
            },
            {
                "name": "my-inputs",
                "type": "inputs"
            },
            {
                "name": "my-crashes",
                "type": "crashes"
            }
        ],
        "job_id": "00000000-0000-0000-0000-000000000000",
        "tags": {},
        "task": {
            "check_debugger": true,
            "duration": 1,
            "target_env": {},
            "target_exe": "fuzz.exe",
            "target_options": [],
            "type": "libfuzzer_fuzz"
        }
    },
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
    "definitions": {
        "ContainerType": {
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
                "unique_reports",
                "regression_reports"
            ],
            "title": "ContainerType"
        },
        "StatsFormat": {
            "description": "An enumeration.",
            "enum": [
                "AFL"
            ],
            "title": "StatsFormat"
        },
        "TaskConfig": {
            "properties": {
                "colocate": {
                    "title": "Colocate",
                    "type": "boolean"
                },
                "containers": {
                    "items": {
                        "$ref": "#/definitions/TaskContainers"
                    },
                    "title": "Containers",
                    "type": "array"
                },
                "debug": {
                    "items": {
                        "$ref": "#/definitions/TaskDebugFlag"
                    },
                    "type": "array"
                },
                "job_id": {
                    "format": "uuid",
                    "title": "Job Id",
                    "type": "string"
                },
                "pool": {
                    "$ref": "#/definitions/TaskPool"
                },
                "prereq_tasks": {
                    "items": {
                        "format": "uuid",
                        "type": "string"
                    },
                    "title": "Prereq Tasks",
                    "type": "array"
                },
                "tags": {
                    "additionalProperties": {
                        "type": "string"
                    },
                    "title": "Tags",
                    "type": "object"
                },
                "task": {
                    "$ref": "#/definitions/TaskDetails"
                },
                "vm": {
                    "$ref": "#/definitions/TaskVm"
                }
            },
            "required": [
                "job_id",
                "task",
                "containers",
                "tags"
            ],
            "title": "TaskConfig",
            "type": "object"
        },
        "TaskContainers": {
            "properties": {
                "name": {
                    "title": "Name",
                    "type": "string"
                },
                "type": {
                    "$ref": "#/definitions/ContainerType"
                }
            },
            "required": [
                "type",
                "name"
            ],
            "title": "TaskContainers",
            "type": "object"
        },
        "TaskDebugFlag": {
            "description": "An enumeration.",
            "enum": [
                "keep_node_on_failure",
                "keep_node_on_completion"
            ],
            "title": "TaskDebugFlag"
        },
        "TaskDetails": {
            "properties": {
                "analyzer_env": {
                    "additionalProperties": {
                        "type": "string"
                    },
                    "title": "Analyzer Env",
                    "type": "object"
                },
                "analyzer_exe": {
                    "title": "Analyzer Exe",
                    "type": "string"
                },
                "analyzer_options": {
                    "items": {
                        "type": "string"
                    },
                    "title": "Analyzer Options",
                    "type": "array"
                },
                "check_asan_log": {
                    "title": "Check Asan Log",
                    "type": "boolean"
                },
                "check_debugger": {
                    "default": true,
                    "title": "Check Debugger",
                    "type": "boolean"
                },
                "check_fuzzer_help": {
                    "title": "Check Fuzzer Help",
                    "type": "boolean"
                },
                "check_retry_count": {
                    "title": "Check Retry Count",
                    "type": "integer"
                },
                "coverage_filter": {
                    "title": "Coverage Filter",
                    "type": "string"
                },
                "duration": {
                    "title": "Duration",
                    "type": "integer"
                },
                "ensemble_sync_delay": {
                    "title": "Ensemble Sync Delay",
                    "type": "integer"
                },
                "expect_crash_on_failure": {
                    "title": "Expect Crash On Failure",
                    "type": "boolean"
                },
                "generator_env": {
                    "additionalProperties": {
                        "type": "string"
                    },
                    "title": "Generator Env",
                    "type": "object"
                },
                "generator_exe": {
                    "title": "Generator Exe",
                    "type": "string"
                },
                "generator_options": {
                    "items": {
                        "type": "string"
                    },
                    "title": "Generator Options",
                    "type": "array"
                },
                "input_file": {
                    "title": "Input File",
                    "type": "string"
                },
                "minimized_stack_depth": {
                    "title": "Minimized Stack Depth",
                    "type": "integer"
                },
                "preserve_existing_outputs": {
                    "title": "Preserve Existing Outputs",
                    "type": "boolean"
                },
                "reboot_after_setup": {
                    "title": "Reboot After Setup",
                    "type": "boolean"
                },
                "rename_output": {
                    "title": "Rename Output",
                    "type": "boolean"
                },
                "report_list": {
                    "items": {
                        "type": "string"
                    },
                    "title": "Report List",
                    "type": "array"
                },
                "stats_file": {
                    "title": "Stats File",
                    "type": "string"
                },
                "stats_format": {
                    "$ref": "#/definitions/StatsFormat"
                },
                "supervisor_env": {
                    "additionalProperties": {
                        "type": "string"
                    },
                    "title": "Supervisor Env",
                    "type": "object"
                },
                "supervisor_exe": {
                    "title": "Supervisor Exe",
                    "type": "string"
                },
                "supervisor_input_marker": {
                    "title": "Supervisor Input Marker",
                    "type": "string"
                },
                "supervisor_options": {
                    "items": {
                        "type": "string"
                    },
                    "title": "Supervisor Options",
                    "type": "array"
                },
                "target_env": {
                    "additionalProperties": {
                        "type": "string"
                    },
                    "title": "Target Env",
                    "type": "object"
                },
                "target_exe": {
                    "title": "Target Exe",
                    "type": "string"
                },
                "target_options": {
                    "items": {
                        "type": "string"
                    },
                    "title": "Target Options",
                    "type": "array"
                },
                "target_options_merge": {
                    "title": "Target Options Merge",
                    "type": "boolean"
                },
                "target_timeout": {
                    "title": "Target Timeout",
                    "type": "integer"
                },
                "target_workers": {
                    "title": "Target Workers",
                    "type": "integer"
                },
                "type": {
                    "$ref": "#/definitions/TaskType"
                },
                "wait_for_files": {
                    "$ref": "#/definitions/ContainerType"
                }
            },
            "required": [
                "type",
                "duration"
            ],
            "title": "TaskDetails",
            "type": "object"
        },
        "TaskPool": {
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
            ],
            "title": "TaskPool",
            "type": "object"
        },
        "TaskType": {
            "description": "An enumeration.",
            "enum": [
                "coverage",
                "libfuzzer_fuzz",
                "libfuzzer_coverage",
                "libfuzzer_crash_report",
                "libfuzzer_merge",
                "libfuzzer_regression",
                "generic_analysis",
                "generic_supervisor",
                "generic_merge",
                "generic_generator",
                "generic_crash_report",
                "generic_regression",
                "analysis_single"
            ],
            "title": "TaskType"
        },
        "TaskVm": {
            "properties": {
                "count": {
                    "default": 1,
                    "title": "Count",
                    "type": "integer"
                },
                "image": {
                    "title": "Image",
                    "type": "string"
                },
                "reboot_after_setup": {
                    "title": "Reboot After Setup",
                    "type": "boolean"
                },
                "region": {
                    "title": "Region",
                    "type": "string"
                },
                "sku": {
                    "title": "Sku",
                    "type": "string"
                },
                "spot_instances": {
                    "default": false,
                    "title": "Spot Instances",
                    "type": "boolean"
                }
            },
            "required": [
                "region",
                "sku",
                "image"
            ],
            "title": "TaskVm",
            "type": "object"
        },
        "UserInfo": {
            "properties": {
                "application_id": {
                    "format": "uuid",
                    "title": "Application Id",
                    "type": "string"
                },
                "object_id": {
                    "format": "uuid",
                    "title": "Object Id",
                    "type": "string"
                },
                "upn": {
                    "title": "Upn",
                    "type": "string"
                }
            },
            "title": "UserInfo",
            "type": "object"
        }
    },
    "properties": {
        "config": {
            "$ref": "#/definitions/TaskConfig"
        },
        "job_id": {
            "format": "uuid",
            "title": "Job Id",
            "type": "string"
        },
        "task_id": {
            "format": "uuid",
            "title": "Task Id",
            "type": "string"
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
    "title": "EventTaskStopped",
    "type": "object"
}
```

## Full Event Schema

```json
{
    "definitions": {
        "Architecture": {
            "description": "An enumeration.",
            "enum": [
                "x86_64"
            ],
            "title": "Architecture"
        },
        "AutoScaleConfig": {
            "properties": {
                "ephemeral_os_disks": {
                    "default": false,
                    "title": "Ephemeral Os Disks",
                    "type": "boolean"
                },
                "image": {
                    "title": "Image",
                    "type": "string"
                },
                "max_size": {
                    "title": "Max Size",
                    "type": "integer"
                },
                "min_size": {
                    "default": 0,
                    "title": "Min Size",
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
                    "default": false,
                    "title": "Spot Instances",
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
            ],
            "title": "AutoScaleConfig",
            "type": "object"
        },
        "BlobRef": {
            "properties": {
                "account": {
                    "title": "Account",
                    "type": "string"
                },
                "container": {
                    "title": "Container",
                    "type": "string"
                },
                "name": {
                    "title": "Name",
                    "type": "string"
                }
            },
            "required": [
                "account",
                "container",
                "name"
            ],
            "title": "BlobRef",
            "type": "object"
        },
        "ContainerType": {
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
                "unique_reports",
                "regression_reports"
            ],
            "title": "ContainerType"
        },
        "CrashTestResult": {
            "properties": {
                "crash_report": {
                    "$ref": "#/definitions/Report"
                },
                "no_repro": {
                    "$ref": "#/definitions/NoReproReport"
                }
            },
            "title": "CrashTestResult",
            "type": "object"
        },
        "Error": {
            "properties": {
                "code": {
                    "$ref": "#/definitions/ErrorCode"
                },
                "errors": {
                    "items": {
                        "type": "string"
                    },
                    "title": "Errors",
                    "type": "array"
                }
            },
            "required": [
                "code",
                "errors"
            ],
            "title": "Error",
            "type": "object"
        },
        "ErrorCode": {
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
            ],
            "title": "ErrorCode"
        },
        "EventCrashReported": {
            "properties": {
                "container": {
                    "title": "Container",
                    "type": "string"
                },
                "filename": {
                    "title": "Filename",
                    "type": "string"
                },
                "report": {
                    "$ref": "#/definitions/Report"
                },
                "task_config": {
                    "$ref": "#/definitions/TaskConfig"
                }
            },
            "required": [
                "report",
                "container",
                "filename"
            ],
            "title": "EventCrashReported",
            "type": "object"
        },
        "EventFileAdded": {
            "properties": {
                "container": {
                    "title": "Container",
                    "type": "string"
                },
                "filename": {
                    "title": "Filename",
                    "type": "string"
                }
            },
            "required": [
                "container",
                "filename"
            ],
            "title": "EventFileAdded",
            "type": "object"
        },
        "EventInstanceConfigUpdated": {
            "properties": {
                "config": {
                    "$ref": "#/definitions/InstanceConfig"
                }
            },
            "required": [
                "config"
            ],
            "title": "EventInstanceConfigUpdated",
            "type": "object"
        },
        "EventJobCreated": {
            "properties": {
                "config": {
                    "$ref": "#/definitions/JobConfig"
                },
                "job_id": {
                    "format": "uuid",
                    "title": "Job Id",
                    "type": "string"
                },
                "user_info": {
                    "$ref": "#/definitions/UserInfo"
                }
            },
            "required": [
                "job_id",
                "config"
            ],
            "title": "EventJobCreated",
            "type": "object"
        },
        "EventJobStopped": {
            "properties": {
                "config": {
                    "$ref": "#/definitions/JobConfig"
                },
                "job_id": {
                    "format": "uuid",
                    "title": "Job Id",
                    "type": "string"
                },
                "task_info": {
                    "items": {
                        "$ref": "#/definitions/JobTaskStopped"
                    },
                    "title": "Task Info",
                    "type": "array"
                },
                "user_info": {
                    "$ref": "#/definitions/UserInfo"
                }
            },
            "required": [
                "job_id",
                "config"
            ],
            "title": "EventJobStopped",
            "type": "object"
        },
        "EventNodeCreated": {
            "properties": {
                "machine_id": {
                    "format": "uuid",
                    "title": "Machine Id",
                    "type": "string"
                },
                "pool_name": {
                    "title": "Pool Name",
                    "type": "string"
                },
                "scaleset_id": {
                    "format": "uuid",
                    "title": "Scaleset Id",
                    "type": "string"
                }
            },
            "required": [
                "machine_id",
                "pool_name"
            ],
            "title": "EventNodeCreated",
            "type": "object"
        },
        "EventNodeDeleted": {
            "properties": {
                "machine_id": {
                    "format": "uuid",
                    "title": "Machine Id",
                    "type": "string"
                },
                "pool_name": {
                    "title": "Pool Name",
                    "type": "string"
                },
                "scaleset_id": {
                    "format": "uuid",
                    "title": "Scaleset Id",
                    "type": "string"
                }
            },
            "required": [
                "machine_id",
                "pool_name"
            ],
            "title": "EventNodeDeleted",
            "type": "object"
        },
        "EventNodeHeartbeat": {
            "properties": {
                "machine_id": {
                    "format": "uuid",
                    "title": "Machine Id",
                    "type": "string"
                },
                "pool_name": {
                    "title": "Pool Name",
                    "type": "string"
                },
                "scaleset_id": {
                    "format": "uuid",
                    "title": "Scaleset Id",
                    "type": "string"
                }
            },
            "required": [
                "machine_id",
                "pool_name"
            ],
            "title": "EventNodeHeartbeat",
            "type": "object"
        },
        "EventNodeStateUpdated": {
            "properties": {
                "machine_id": {
                    "format": "uuid",
                    "title": "Machine Id",
                    "type": "string"
                },
                "pool_name": {
                    "title": "Pool Name",
                    "type": "string"
                },
                "scaleset_id": {
                    "format": "uuid",
                    "title": "Scaleset Id",
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
            "title": "EventNodeStateUpdated",
            "type": "object"
        },
        "EventPing": {
            "properties": {
                "ping_id": {
                    "format": "uuid",
                    "title": "Ping Id",
                    "type": "string"
                }
            },
            "required": [
                "ping_id"
            ],
            "title": "EventPing",
            "type": "object"
        },
        "EventPoolCreated": {
            "properties": {
                "arch": {
                    "$ref": "#/definitions/Architecture"
                },
                "autoscale": {
                    "$ref": "#/definitions/AutoScaleConfig"
                },
                "managed": {
                    "title": "Managed",
                    "type": "boolean"
                },
                "os": {
                    "$ref": "#/definitions/OS"
                },
                "pool_name": {
                    "title": "Pool Name",
                    "type": "string"
                }
            },
            "required": [
                "pool_name",
                "os",
                "arch",
                "managed"
            ],
            "title": "EventPoolCreated",
            "type": "object"
        },
        "EventPoolDeleted": {
            "properties": {
                "pool_name": {
                    "title": "Pool Name",
                    "type": "string"
                }
            },
            "required": [
                "pool_name"
            ],
            "title": "EventPoolDeleted",
            "type": "object"
        },
        "EventProxyCreated": {
            "properties": {
                "proxy_id": {
                    "format": "uuid",
                    "title": "Proxy Id",
                    "type": "string"
                },
                "region": {
                    "title": "Region",
                    "type": "string"
                }
            },
            "required": [
                "region"
            ],
            "title": "EventProxyCreated",
            "type": "object"
        },
        "EventProxyDeleted": {
            "properties": {
                "proxy_id": {
                    "format": "uuid",
                    "title": "Proxy Id",
                    "type": "string"
                },
                "region": {
                    "title": "Region",
                    "type": "string"
                }
            },
            "required": [
                "region"
            ],
            "title": "EventProxyDeleted",
            "type": "object"
        },
        "EventProxyFailed": {
            "properties": {
                "error": {
                    "$ref": "#/definitions/Error"
                },
                "proxy_id": {
                    "format": "uuid",
                    "title": "Proxy Id",
                    "type": "string"
                },
                "region": {
                    "title": "Region",
                    "type": "string"
                }
            },
            "required": [
                "region",
                "error"
            ],
            "title": "EventProxyFailed",
            "type": "object"
        },
        "EventProxyStateUpdated": {
            "properties": {
                "proxy_id": {
                    "format": "uuid",
                    "title": "Proxy Id",
                    "type": "string"
                },
                "region": {
                    "title": "Region",
                    "type": "string"
                },
                "state": {
                    "$ref": "#/definitions/VmState"
                }
            },
            "required": [
                "region",
                "proxy_id",
                "state"
            ],
            "title": "EventProxyStateUpdated",
            "type": "object"
        },
        "EventRegressionReported": {
            "properties": {
                "container": {
                    "title": "Container",
                    "type": "string"
                },
                "filename": {
                    "title": "Filename",
                    "type": "string"
                },
                "regression_report": {
                    "$ref": "#/definitions/RegressionReport"
                },
                "task_config": {
                    "$ref": "#/definitions/TaskConfig"
                }
            },
            "required": [
                "regression_report",
                "container",
                "filename"
            ],
            "title": "EventRegressionReported",
            "type": "object"
        },
        "EventScalesetCreated": {
            "properties": {
                "image": {
                    "title": "Image",
                    "type": "string"
                },
                "pool_name": {
                    "title": "Pool Name",
                    "type": "string"
                },
                "region": {
                    "title": "Region",
                    "type": "string"
                },
                "scaleset_id": {
                    "format": "uuid",
                    "title": "Scaleset Id",
                    "type": "string"
                },
                "size": {
                    "title": "Size",
                    "type": "integer"
                },
                "vm_sku": {
                    "title": "Vm Sku",
                    "type": "string"
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
            "title": "EventScalesetCreated",
            "type": "object"
        },
        "EventScalesetDeleted": {
            "properties": {
                "pool_name": {
                    "title": "Pool Name",
                    "type": "string"
                },
                "scaleset_id": {
                    "format": "uuid",
                    "title": "Scaleset Id",
                    "type": "string"
                }
            },
            "required": [
                "scaleset_id",
                "pool_name"
            ],
            "title": "EventScalesetDeleted",
            "type": "object"
        },
        "EventScalesetFailed": {
            "properties": {
                "error": {
                    "$ref": "#/definitions/Error"
                },
                "pool_name": {
                    "title": "Pool Name",
                    "type": "string"
                },
                "scaleset_id": {
                    "format": "uuid",
                    "title": "Scaleset Id",
                    "type": "string"
                }
            },
            "required": [
                "scaleset_id",
                "pool_name",
                "error"
            ],
            "title": "EventScalesetFailed",
            "type": "object"
        },
        "EventScalesetStateUpdated": {
            "properties": {
                "pool_name": {
                    "title": "Pool Name",
                    "type": "string"
                },
                "scaleset_id": {
                    "format": "uuid",
                    "title": "Scaleset Id",
                    "type": "string"
                },
                "state": {
                    "$ref": "#/definitions/ScalesetState"
                }
            },
            "required": [
                "scaleset_id",
                "pool_name",
                "state"
            ],
            "title": "EventScalesetStateUpdated",
            "type": "object"
        },
        "EventTaskCreated": {
            "properties": {
                "config": {
                    "$ref": "#/definitions/TaskConfig"
                },
                "job_id": {
                    "format": "uuid",
                    "title": "Job Id",
                    "type": "string"
                },
                "task_id": {
                    "format": "uuid",
                    "title": "Task Id",
                    "type": "string"
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
            "title": "EventTaskCreated",
            "type": "object"
        },
        "EventTaskFailed": {
            "properties": {
                "config": {
                    "$ref": "#/definitions/TaskConfig"
                },
                "error": {
                    "$ref": "#/definitions/Error"
                },
                "job_id": {
                    "format": "uuid",
                    "title": "Job Id",
                    "type": "string"
                },
                "task_id": {
                    "format": "uuid",
                    "title": "Task Id",
                    "type": "string"
                },
                "user_info": {
                    "$ref": "#/definitions/UserInfo"
                }
            },
            "required": [
                "job_id",
                "task_id",
                "error",
                "config"
            ],
            "title": "EventTaskFailed",
            "type": "object"
        },
        "EventTaskHeartbeat": {
            "properties": {
                "config": {
                    "$ref": "#/definitions/TaskConfig"
                },
                "job_id": {
                    "format": "uuid",
                    "title": "Job Id",
                    "type": "string"
                },
                "task_id": {
                    "format": "uuid",
                    "title": "Task Id",
                    "type": "string"
                }
            },
            "required": [
                "job_id",
                "task_id",
                "config"
            ],
            "title": "EventTaskHeartbeat",
            "type": "object"
        },
        "EventTaskStateUpdated": {
            "properties": {
                "config": {
                    "$ref": "#/definitions/TaskConfig"
                },
                "end_time": {
                    "format": "date-time",
                    "title": "End Time",
                    "type": "string"
                },
                "job_id": {
                    "format": "uuid",
                    "title": "Job Id",
                    "type": "string"
                },
                "state": {
                    "$ref": "#/definitions/TaskState"
                },
                "task_id": {
                    "format": "uuid",
                    "title": "Task Id",
                    "type": "string"
                }
            },
            "required": [
                "job_id",
                "task_id",
                "state",
                "config"
            ],
            "title": "EventTaskStateUpdated",
            "type": "object"
        },
        "EventTaskStopped": {
            "properties": {
                "config": {
                    "$ref": "#/definitions/TaskConfig"
                },
                "job_id": {
                    "format": "uuid",
                    "title": "Job Id",
                    "type": "string"
                },
                "task_id": {
                    "format": "uuid",
                    "title": "Task Id",
                    "type": "string"
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
            "title": "EventTaskStopped",
            "type": "object"
        },
        "EventType": {
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
                "proxy_state_updated",
                "scaleset_created",
                "scaleset_deleted",
                "scaleset_failed",
                "scaleset_state_updated",
                "task_created",
                "task_failed",
                "task_state_updated",
                "task_stopped",
                "crash_reported",
                "regression_reported",
                "file_added",
                "task_heartbeat",
                "node_heartbeat",
                "instance_config_updated"
            ],
            "title": "EventType"
        },
        "InstanceConfig": {
            "properties": {
                "admins": {
                    "items": {
                        "format": "uuid",
                        "type": "string"
                    },
                    "title": "Admins",
                    "type": "array"
                },
                "allow_pool_management": {
                    "default": true,
                    "title": "Allow Pool Management",
                    "type": "boolean"
                }
            },
            "title": "InstanceConfig",
            "type": "object"
        },
        "JobConfig": {
            "properties": {
                "build": {
                    "title": "Build",
                    "type": "string"
                },
                "duration": {
                    "title": "Duration",
                    "type": "integer"
                },
                "name": {
                    "title": "Name",
                    "type": "string"
                },
                "project": {
                    "title": "Project",
                    "type": "string"
                }
            },
            "required": [
                "project",
                "name",
                "build",
                "duration"
            ],
            "title": "JobConfig",
            "type": "object"
        },
        "JobTaskStopped": {
            "properties": {
                "error": {
                    "$ref": "#/definitions/Error"
                },
                "task_id": {
                    "format": "uuid",
                    "title": "Task Id",
                    "type": "string"
                },
                "task_type": {
                    "$ref": "#/definitions/TaskType"
                }
            },
            "required": [
                "task_id",
                "task_type"
            ],
            "title": "JobTaskStopped",
            "type": "object"
        },
        "NoReproReport": {
            "properties": {
                "error": {
                    "title": "Error",
                    "type": "string"
                },
                "executable": {
                    "title": "Executable",
                    "type": "string"
                },
                "input_blob": {
                    "$ref": "#/definitions/BlobRef"
                },
                "input_sha256": {
                    "title": "Input Sha256",
                    "type": "string"
                },
                "job_id": {
                    "format": "uuid",
                    "title": "Job Id",
                    "type": "string"
                },
                "task_id": {
                    "format": "uuid",
                    "title": "Task Id",
                    "type": "string"
                },
                "tries": {
                    "title": "Tries",
                    "type": "integer"
                }
            },
            "required": [
                "input_sha256",
                "executable",
                "task_id",
                "job_id",
                "tries"
            ],
            "title": "NoReproReport",
            "type": "object"
        },
        "NodeState": {
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
            ],
            "title": "NodeState"
        },
        "OS": {
            "description": "An enumeration.",
            "enum": [
                "windows",
                "linux"
            ],
            "title": "OS"
        },
        "RegressionReport": {
            "properties": {
                "crash_test_result": {
                    "$ref": "#/definitions/CrashTestResult"
                },
                "original_crash_test_result": {
                    "$ref": "#/definitions/CrashTestResult"
                }
            },
            "required": [
                "crash_test_result"
            ],
            "title": "RegressionReport",
            "type": "object"
        },
        "Report": {
            "properties": {
                "asan_log": {
                    "title": "Asan Log",
                    "type": "string"
                },
                "call_stack": {
                    "items": {
                        "type": "string"
                    },
                    "title": "Call Stack",
                    "type": "array"
                },
                "call_stack_sha256": {
                    "title": "Call Stack Sha256",
                    "type": "string"
                },
                "crash_site": {
                    "title": "Crash Site",
                    "type": "string"
                },
                "crash_type": {
                    "title": "Crash Type",
                    "type": "string"
                },
                "executable": {
                    "title": "Executable",
                    "type": "string"
                },
                "input_blob": {
                    "$ref": "#/definitions/BlobRef"
                },
                "input_sha256": {
                    "title": "Input Sha256",
                    "type": "string"
                },
                "input_url": {
                    "title": "Input Url",
                    "type": "string"
                },
                "job_id": {
                    "format": "uuid",
                    "title": "Job Id",
                    "type": "string"
                },
                "minimized_stack": {
                    "items": {
                        "type": "string"
                    },
                    "title": "Minimized Stack",
                    "type": "array"
                },
                "minimized_stack_function_lines": {
                    "items": {
                        "type": "string"
                    },
                    "title": "Minimized Stack Function Lines",
                    "type": "array"
                },
                "minimized_stack_function_lines_sha256": {
                    "title": "Minimized Stack Function Lines Sha256",
                    "type": "string"
                },
                "minimized_stack_function_names": {
                    "items": {
                        "type": "string"
                    },
                    "title": "Minimized Stack Function Names",
                    "type": "array"
                },
                "minimized_stack_function_names_sha256": {
                    "title": "Minimized Stack Function Names Sha256",
                    "type": "string"
                },
                "minimized_stack_sha256": {
                    "title": "Minimized Stack Sha256",
                    "type": "string"
                },
                "scariness_description": {
                    "title": "Scariness Description",
                    "type": "string"
                },
                "scariness_score": {
                    "title": "Scariness Score",
                    "type": "integer"
                },
                "task_id": {
                    "format": "uuid",
                    "title": "Task Id",
                    "type": "string"
                }
            },
            "required": [
                "executable",
                "crash_type",
                "crash_site",
                "call_stack",
                "call_stack_sha256",
                "input_sha256",
                "task_id",
                "job_id"
            ],
            "title": "Report",
            "type": "object"
        },
        "ScalesetState": {
            "description": "An enumeration.",
            "enum": [
                "init",
                "setup",
                "resize",
                "running",
                "shutdown",
                "halt",
                "creation_failed"
            ],
            "title": "ScalesetState"
        },
        "StatsFormat": {
            "description": "An enumeration.",
            "enum": [
                "AFL"
            ],
            "title": "StatsFormat"
        },
        "TaskConfig": {
            "properties": {
                "colocate": {
                    "title": "Colocate",
                    "type": "boolean"
                },
                "containers": {
                    "items": {
                        "$ref": "#/definitions/TaskContainers"
                    },
                    "title": "Containers",
                    "type": "array"
                },
                "debug": {
                    "items": {
                        "$ref": "#/definitions/TaskDebugFlag"
                    },
                    "type": "array"
                },
                "job_id": {
                    "format": "uuid",
                    "title": "Job Id",
                    "type": "string"
                },
                "pool": {
                    "$ref": "#/definitions/TaskPool"
                },
                "prereq_tasks": {
                    "items": {
                        "format": "uuid",
                        "type": "string"
                    },
                    "title": "Prereq Tasks",
                    "type": "array"
                },
                "tags": {
                    "additionalProperties": {
                        "type": "string"
                    },
                    "title": "Tags",
                    "type": "object"
                },
                "task": {
                    "$ref": "#/definitions/TaskDetails"
                },
                "vm": {
                    "$ref": "#/definitions/TaskVm"
                }
            },
            "required": [
                "job_id",
                "task",
                "containers",
                "tags"
            ],
            "title": "TaskConfig",
            "type": "object"
        },
        "TaskContainers": {
            "properties": {
                "name": {
                    "title": "Name",
                    "type": "string"
                },
                "type": {
                    "$ref": "#/definitions/ContainerType"
                }
            },
            "required": [
                "type",
                "name"
            ],
            "title": "TaskContainers",
            "type": "object"
        },
        "TaskDebugFlag": {
            "description": "An enumeration.",
            "enum": [
                "keep_node_on_failure",
                "keep_node_on_completion"
            ],
            "title": "TaskDebugFlag"
        },
        "TaskDetails": {
            "properties": {
                "analyzer_env": {
                    "additionalProperties": {
                        "type": "string"
                    },
                    "title": "Analyzer Env",
                    "type": "object"
                },
                "analyzer_exe": {
                    "title": "Analyzer Exe",
                    "type": "string"
                },
                "analyzer_options": {
                    "items": {
                        "type": "string"
                    },
                    "title": "Analyzer Options",
                    "type": "array"
                },
                "check_asan_log": {
                    "title": "Check Asan Log",
                    "type": "boolean"
                },
                "check_debugger": {
                    "default": true,
                    "title": "Check Debugger",
                    "type": "boolean"
                },
                "check_fuzzer_help": {
                    "title": "Check Fuzzer Help",
                    "type": "boolean"
                },
                "check_retry_count": {
                    "title": "Check Retry Count",
                    "type": "integer"
                },
                "coverage_filter": {
                    "title": "Coverage Filter",
                    "type": "string"
                },
                "duration": {
                    "title": "Duration",
                    "type": "integer"
                },
                "ensemble_sync_delay": {
                    "title": "Ensemble Sync Delay",
                    "type": "integer"
                },
                "expect_crash_on_failure": {
                    "title": "Expect Crash On Failure",
                    "type": "boolean"
                },
                "generator_env": {
                    "additionalProperties": {
                        "type": "string"
                    },
                    "title": "Generator Env",
                    "type": "object"
                },
                "generator_exe": {
                    "title": "Generator Exe",
                    "type": "string"
                },
                "generator_options": {
                    "items": {
                        "type": "string"
                    },
                    "title": "Generator Options",
                    "type": "array"
                },
                "input_file": {
                    "title": "Input File",
                    "type": "string"
                },
                "minimized_stack_depth": {
                    "title": "Minimized Stack Depth",
                    "type": "integer"
                },
                "preserve_existing_outputs": {
                    "title": "Preserve Existing Outputs",
                    "type": "boolean"
                },
                "reboot_after_setup": {
                    "title": "Reboot After Setup",
                    "type": "boolean"
                },
                "rename_output": {
                    "title": "Rename Output",
                    "type": "boolean"
                },
                "report_list": {
                    "items": {
                        "type": "string"
                    },
                    "title": "Report List",
                    "type": "array"
                },
                "stats_file": {
                    "title": "Stats File",
                    "type": "string"
                },
                "stats_format": {
                    "$ref": "#/definitions/StatsFormat"
                },
                "supervisor_env": {
                    "additionalProperties": {
                        "type": "string"
                    },
                    "title": "Supervisor Env",
                    "type": "object"
                },
                "supervisor_exe": {
                    "title": "Supervisor Exe",
                    "type": "string"
                },
                "supervisor_input_marker": {
                    "title": "Supervisor Input Marker",
                    "type": "string"
                },
                "supervisor_options": {
                    "items": {
                        "type": "string"
                    },
                    "title": "Supervisor Options",
                    "type": "array"
                },
                "target_env": {
                    "additionalProperties": {
                        "type": "string"
                    },
                    "title": "Target Env",
                    "type": "object"
                },
                "target_exe": {
                    "title": "Target Exe",
                    "type": "string"
                },
                "target_options": {
                    "items": {
                        "type": "string"
                    },
                    "title": "Target Options",
                    "type": "array"
                },
                "target_options_merge": {
                    "title": "Target Options Merge",
                    "type": "boolean"
                },
                "target_timeout": {
                    "title": "Target Timeout",
                    "type": "integer"
                },
                "target_workers": {
                    "title": "Target Workers",
                    "type": "integer"
                },
                "type": {
                    "$ref": "#/definitions/TaskType"
                },
                "wait_for_files": {
                    "$ref": "#/definitions/ContainerType"
                }
            },
            "required": [
                "type",
                "duration"
            ],
            "title": "TaskDetails",
            "type": "object"
        },
        "TaskPool": {
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
            ],
            "title": "TaskPool",
            "type": "object"
        },
        "TaskState": {
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
            ],
            "title": "TaskState"
        },
        "TaskType": {
            "description": "An enumeration.",
            "enum": [
                "coverage",
                "libfuzzer_fuzz",
                "libfuzzer_coverage",
                "libfuzzer_crash_report",
                "libfuzzer_merge",
                "libfuzzer_regression",
                "generic_analysis",
                "generic_supervisor",
                "generic_merge",
                "generic_generator",
                "generic_crash_report",
                "generic_regression",
                "analysis_single"
            ],
            "title": "TaskType"
        },
        "TaskVm": {
            "properties": {
                "count": {
                    "default": 1,
                    "title": "Count",
                    "type": "integer"
                },
                "image": {
                    "title": "Image",
                    "type": "string"
                },
                "reboot_after_setup": {
                    "title": "Reboot After Setup",
                    "type": "boolean"
                },
                "region": {
                    "title": "Region",
                    "type": "string"
                },
                "sku": {
                    "title": "Sku",
                    "type": "string"
                },
                "spot_instances": {
                    "default": false,
                    "title": "Spot Instances",
                    "type": "boolean"
                }
            },
            "required": [
                "region",
                "sku",
                "image"
            ],
            "title": "TaskVm",
            "type": "object"
        },
        "UserInfo": {
            "properties": {
                "application_id": {
                    "format": "uuid",
                    "title": "Application Id",
                    "type": "string"
                },
                "object_id": {
                    "format": "uuid",
                    "title": "Object Id",
                    "type": "string"
                },
                "upn": {
                    "title": "Upn",
                    "type": "string"
                }
            },
            "title": "UserInfo",
            "type": "object"
        },
        "VmState": {
            "description": "An enumeration.",
            "enum": [
                "init",
                "extensions_launch",
                "extensions_failed",
                "vm_allocation_failed",
                "running",
                "stopping",
                "stopped"
            ],
            "title": "VmState"
        }
    },
    "properties": {
        "event": {
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
                    "$ref": "#/definitions/EventNodeHeartbeat"
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
                    "$ref": "#/definitions/EventProxyStateUpdated"
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
                    "$ref": "#/definitions/EventScalesetStateUpdated"
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
                },
                {
                    "$ref": "#/definitions/EventTaskHeartbeat"
                },
                {
                    "$ref": "#/definitions/EventCrashReported"
                },
                {
                    "$ref": "#/definitions/EventRegressionReported"
                },
                {
                    "$ref": "#/definitions/EventFileAdded"
                },
                {
                    "$ref": "#/definitions/EventInstanceConfigUpdated"
                }
            ],
            "title": "Event"
        },
        "event_id": {
            "format": "uuid",
            "title": "Event Id",
            "type": "string"
        },
        "event_type": {
            "$ref": "#/definitions/EventType"
        },
        "instance_id": {
            "format": "uuid",
            "title": "Instance Id",
            "type": "string"
        },
        "instance_name": {
            "title": "Instance Name",
            "type": "string"
        },
        "webhook_id": {
            "format": "uuid",
            "title": "Webhook Id",
            "type": "string"
        }
    },
    "required": [
        "event_type",
        "event",
        "instance_id",
        "instance_name",
        "webhook_id"
    ],
    "title": "WebhookMessage",
    "type": "object"
}
```

