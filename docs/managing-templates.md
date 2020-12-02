# Managing Declarative Job Templates

[Declarative Job Templates](declarative-templates.md), currently a preview
feature, allow a user to define a reusable fuzzing pipeline as a template.
Once saved, any user of the OneFuzz instance can create fuzzing jobs based on
the templates.

This is a walk-through guide for updating an existing job template, though
the process is similar for creating templates from scratch. 

This process demonstrates adding [Microsoft Teams
notifications](notifications/teams.md) for any unique reports found to any
`libfuzzer_linux` job templates and saving it as `libfuzzer_with_teams`

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
4. With your preferred text editor, add the following to the notifications list (look for `"notifications": []`):
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
automatically refresh the templates every 24 hours.

If an existing template is changed without requiring new user input via [form
fields](declarative-templates.md#example-form-fields), using the template can
happen transparently.

If you create a new template or update an existing template that changes the
user interaction, users will need to refresh their templates to make use of
the change.

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
3. Verify a notification was setup for the unique reports container
    ```
    $ onefuzz notifications  list --query "[?container == 'oft-unique-reports-88dfb15b9ab758b88b122508d4648687']"
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