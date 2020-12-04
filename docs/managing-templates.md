# Managing Declarative Job Templates

[Declarative Job Templates](declarative-templates.md), currently a preview
feature, allow a user to define a reusable fuzzing pipeline as a template.
Once saved, any user of the OneFuzz instance can create fuzzing jobs based on
the templates.

This is a walk-through guide for updating an existing job template, though
the process is similar for creating templates from scratch. 

This process demonstrates adding [Microsoft Teams
notifications](notifications/teams.md) for new unique crash reports to an existing
`libfuzzer_linux` job template and saving it as `libfuzzer_with_teams`.

## Using the CLI & modifying JSON

1. Enable support for declarative templates.
    ```
    onefuzz config --enable_feature job_templates
    ```
2. List available templates.
    ```
    onefuzz job_templates list
    ````
3. Save a copy of the template locally.
    ```
    onefuzz job_templates manage get libfuzzer_linux > libfuzzer_linux.json
    ```
3. With your preferred text editor, add the following to the `notifications` list:
    ```json 
    {
        "container_type": "unique_reports", 
        "notification": {
            "config": {
                "url": "https://contoso.com/webhook-url-here"
            }
        }
    }
    ```
5. Upload the template.
    ```
    onefuzz job_templates manage upload libfuzzer_with_teams @./libfuzzer_linux.json
    ```

## Using the SDK

1. Enable support for declarative templates.
    ```
    onefuzz config --enable_feature job_templates
    ```
2. Run the following python
    ```python
    from onefuzztypes.job_templates import JobTemplateNotification
    from onefuzztypes.models import NotificationConfig, TeamsTemplate
    from onefuzztypes.enums import ContainerType
    from onefuzz.api import Onefuzz

    o = Onefuzz()
    template = o.job_templates.manage.get("libfuzzer_linux")
    template.notifications.append(
        JobTemplateNotification(
            container_type=ContainerType.unique_reports,
            notification=NotificationConfig(
                config=TeamsTemplate(url="https://contoso.com/webhook-url-here")
            ),
        )
    )
    o.job_templates.manage.upload("libfuzzer_with_teams", template)
    ```

## Using the updated template

The OneFuzz SDK caches the list of Declarative Job Templates and will
automatically refresh the templates every 24 hours.  As shown below, users can
refresh the declarative job template cache on demand.

If an existing template is changed without requiring new user input via [form
fields](declarative-templates.md#example-form-fields), using the template can
happen transparently.

If you create a new template or update an existing template that changes the
user interaction, users will need to refresh their template cache to make use
of the change.

Now let's make use of our new template.

1. Update our template cache to make sure we have the latest `libfuzzer_with_teams` template
    ```
    $ onefuzz job_templates refresh
    WARNING:onefuzz:job_templates are a preview-feature and may change in an upcoming release
    INFO:onefuzz:refreshing job template cache
    INFO:onefuzz:updated template definition: libfuzzer_linux
    INFO:onefuzz:updated template definition: libfuzzer_with_teams
    INFO:onefuzz:updated template definition: afl_windows
    INFO:onefuzz:updated template definition: afl_linux
    INFO:onefuzz:updated template definition: libfuzzer_windows
    $
    ```
2. Launch our job
    ```
    $ onefuzz job_templates submit libfuzzer_with_teams example-project example-target build-1 linux --target_exe ./fuzz.exe
    WARNING:onefuzz:job_templates are a preview-feature and may change in an upcoming release
    INFO:onefuzz:creating container: oft-inputs-88dfb15b9ab758b88b122508d4648687
    INFO:onefuzz:creating container: oft-readonly-inputs-88dfb15b9ab758b88b122508d4648687
    INFO:onefuzz:creating container: oft-no-repro-88dfb15b9ab758b88b122508d4648687
    INFO:onefuzz:creating container: oft-crashes-88dfb15b9ab758b88b122508d4648687
    INFO:onefuzz:creating container: oft-reports-88dfb15b9ab758b88b122508d4648687
    INFO:onefuzz:creating container: oft-unique-reports-88dfb15b9ab758b88b122508d4648687
    INFO:onefuzz:creating container: oft-setup-fde90db8a8e65b4e8b7518f9d1350036
    INFO:onefuzz:uploading ./fuzz.exe to oft-setup-fde90db8a8e65b4e8b7518f9d1350036
    INFO:onefuzz:creating container: oft-coverage-88dfb15b9ab758b88b122508d4648687
    {
        "config": {
            "build": "build-1",
            "duration": 24,
            "name": "example-target",
            "project": "example-project"
        },
        "job_id": "d3259dfe-fdad-45a0-bf90-a381b8dc1ee8",
        "state": "init"
    }
    $ 
    ```
3. Verify a notification was set up for the unique reports container
    ```
    $ onefuzz notifications list --query "[?container == 'oft-unique-reports-88dfb15b9ab758b88b122508d4648687']"
    [
        {
            "config": {
                "url": "***"
            },
            "container": "oft-unique-reports-88dfb15b9ab758b88b122508d4648687",
            "notification_id": "0e0c10a1-78ef-4f65-be56-d3ba0788fcb5"
        }
    ]
    ```

## Adding a required field

In may cases, we want users of the template to provide more detail, such as
data that can be used to tailor the notifications based on the target at
hand. This is accomplished via a [form fields](declarative-templates.md#example-form-fields).

Let's make a new template that enables notifications via [Azure Devops Work
Items](notifications/ado.md), where we require the user to specify the Area
Path that the work items OneFuzz will create.

This example will demonstrate setting the following:
* `Project` via the project name specified during job creation.
* `Area Path` via the new required field.
* `Iteration Path` via a new optional field, with a default value in the template.

1. Enable support for declarative templates.
    ```
    onefuzz config --enable_feature job_templates
    ```
2. Save a copy of the template locally.
    ```
    onefuzz job_templates manage get libfuzzer_linux > libfuzzer_linux_ado_areapath.json
    ```
3. With your preferred text editor, add the following to the `notifications` list:
    ```json 
    {
      "config": {
        "base_url": "https://dev.azure.com/org_name",
        "auth_token": "ADO_AUTH_TOKEN",
        "type": "Bug",
        "project": "{{ job.project }}",
        "ado_fields": {
          "System.AreaPath": "{{ task.tags['area_path'] }}",
          "Microsoft.VSTS.Scheduling.StoryPoints": "1",
          "System.IterationPath": "{{ task.tags['iteration_path'] }}",
          "System.Title": "{{ report.crash_site }} - {{ report.executable }}",
          "Microsoft.VSTS.TCM.ReproSteps": "This is my call stack: <ul> {% for item in report.call_stack %} <li> {{ item }} </li> {% endfor %} </ul>"
        },
        "comment": "This is my comment. {{ report.input_sha256 }} {{ input_url }} <br> <pre>{{ repro_cmd }}</pre>",
        "unique_fields": ["System.Title", "System.AreaPath"],
        "on_duplicate": {
          "comment": "Another <a href='{{ input_url }}'>POC</a> was found in <a href='{{ target_url }}'>target</a>. <br> <pre>{{ repro_cmd }}</pre>",
          "set_state": { "Resolved": "Active" },
          "ado_fields": {
            "System.IterationPath": "{{ task.tags['iteration_path'] }}"
          },
          "increment": ["Microsoft.VSTS.Scheduling.StoryPoints"]
        }
      }
    }
    ```
4. With your preferred text editor, add the following to user fields to the end of the `user_fields` list.
    1. A required field that specifies the tag name `area_path`.
        ```json
        {
            "help": "Area path for reported crashes",
            "locations": [
                {
                    "op": "add",
                    "path": "/tasks/1/tags/area_path"
                }
            ],
            "name": "area_path",
            "required": true,
            "type": "Str"
        }
        ```
    2. An optional field that specifies the `iteration_path`
        ```json
        {
            "default": "Iteration\\Path\\Default\\Here",
            "help": "Iteration path for reported crashes",
            "locations": [
                {
                    "op": "add",
                    "path": "/tasks/1/tags/iteration_path"
                }
            ],
            "name": "iteration_path",
            "required": false,
            "type": "Str"
        }
        ```
    > NOTE: Each of these fields use the `jsonpatch` path to add a tag to the second task (by array index), which is the `crash_report` task.  Check out [form fields](declarative-templates.md#example-form-fields) for more information.
5. Upload the template.
    ```
    onefuzz job_templates manage upload libfuzzer_linux_ado_areapath @./libfuzzer_linux_ado_areapath.json
    ```
6. Refresh the template cache
    ```
    onefuzz job_templates refresh
    ```

Using `--help`, we can see the new optional and required arguments.  
```
$ onefuzz job_templates submit libfuzzer_linux_ado_areapath --help
usage: onefuzz job_templates submit libfuzzer_linux_ado_areapath [-h] [-v] [--format {json,raw}] [--query QUERY]
    [--target_exe TARGET_EXE] [--duration DURATION]
    [--target_workers TARGET_WORKERS] [--vm_count VM_COUNT]
    [--target_options [TARGET_OPTIONS [TARGET_OPTIONS ...]]]
    [--target_env str=str [str=str ...]] [--reboot_after_setup]
    [--check_retry_count CHECK_RETRY_COUNT]
    [--target_timeout TARGET_TIMEOUT] [--mytags str=str [str=str ...]]
    [--iteration_path ITERATION_PATH] [--setup_dir SETUP_DIR]
    [--inputs_dir INPUTS_DIR] [--readonly_inputs_dir READONLY_INPUTS_DIR]
    [--container_names ContainerType=str [ContainerType=str ...]]
    [--parameters JobTemplateRequestParameters]
    project name build pool_name area_path
```

The new field, `area_path` is now a required argument and `iteration_path` is
an optional argument.

Let's create a job using this template and verify the tags are as we expect.
Since the parameters we added only modify the `libfuzzer_crash_report` task,
we'll search just for the that task.
```
$ onefuzz job_templates submit libfuzzer_linux_ado_areapath myproject myname build1 linux My\\Iteration\\Path
WARNING:onefuzz:job_templates are a preview-feature and may change in an upcoming release
INFO:onefuzz:creating container: oft-setup-cf346c84f2df551381d9991a5efbd030
INFO:onefuzz:uploading fuzz.exe to oft-setup-cf346c84f2df551381d9991a5efbd030
INFO:onefuzz:creating container: oft-unique-reports-71bdf0b1ce175a2796954f8d62f5d3d0
INFO:onefuzz:creating container: oft-reports-71bdf0b1ce175a2796954f8d62f5d3d0
INFO:onefuzz:creating container: oft-coverage-71bdf0b1ce175a2796954f8d62f5d3d0
INFO:onefuzz:creating container: oft-no-repro-71bdf0b1ce175a2796954f8d62f5d3d0
INFO:onefuzz:creating container: oft-inputs-71bdf0b1ce175a2796954f8d62f5d3d0
INFO:onefuzz:creating container: oft-crashes-71bdf0b1ce175a2796954f8d62f5d3d0
INFO:onefuzz:creating container: oft-readonly-inputs-71bdf0b1ce175a2796954f8d62f5d3d0
{
    "config": {
        "build": "build1",
        "duration": 24,
        "name": "myname",
        "project": "myproject"
    },
    "job_id": "1eaa9a23-fcdb-400a-a26c-3ebe712fde53",
    "state": "init"
}
$ onefuzz jobs tasks list 1eaa9a23-fcdb-400a-a26c-3ebe712fde53 --query "[?config.task.type == 'libfuzzer_crash_report'].config.tags"
[
    {
        "area_path": "My\\Iteration\\Path",
        "iteration_path": "Iteration\\Path\\Default\\Here"
    }
]
$
```

As we can see, the tag `area_path` is the value we specified, and because we
didn't specify `iteration_path` it uses the default from the template.