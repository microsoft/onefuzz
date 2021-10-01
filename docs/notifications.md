# Notifications in OneFuzz

OneFuzz supports built-in [container monitoring](containers.md) and reporting
via Notifications.  OneFuzz notifications monitor user specified containers
for changes and will perform the notifications upon new file creation.

## Features

* Arbitrary notification integrations per container
* Integration is tied to the containers, not tasks, enabling monitoring of
  container use outside of OneFuzz

## Implementation

Notifications can be created via the CLI via:

`onefuzz notifications create <CONTAINER <CONFIG>`: Create a notification using a JSON config (See [onefuzztypes.models.NotificationConfig](../src/pytypes/onefuzztypes/models.py)
   for syntax)

Existing notifications can be viewed via:

`onefuzz notifications list`

Existing notifications can be deleted via:

`onefuzz notifications delete <CONTAINER> <NOTIFICATION_ID>`

NOTE: While notifications are tied to containers, not tasks, the job templates support
creation notifications during execution.  Example:

```
onefuzz template libfuzzer basic my-project target-1 build-1 --notification_config @./notifications.json
```

You can specify a path to a file using `@/path/to/file` syntax, or specify the
JSON via a string, such as `'{"config":{...}}'`

## Supported integrations

* [Microsoft Teams](notifications/teams.md)
* [Azure Devops Work Items](notifications/ado.md)
* [Github Issues](notifications/github.md)