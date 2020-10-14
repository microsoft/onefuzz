# Notifications via Github Issues

OneFuzz can create or update [Github Issues](https://guides.github.com/features/issues/) 
upon creation of crash reports in OneFuzz managed [containers](../containers.md).

Nearly every field can be customized using [jinja2](https://jinja.palletsprojects.com/)
templates.   There are multiple python objects provided via the template engine that
can be used such that any arbitrary component can be used to flesh out the configuration:

* task (See [TaskConfig](../../src/pytypes/onefuzztypes/models.py))
* report (See [Report](../../src/pytypes/onefuzztypes/models.py))
* job (See [JobConfig](../../src/pytypes/onefuzztypes/models.py))

Using these objects allows dynamic configuration. As an example, the `repository`
could be specified directly, or dynamically pulled from the task configuration:

```json
{
  "repository": "{{ task.tags['repository'] }}"
}
```

There are additional values that can be used in any template:

* report_url: This will link to an authenticated download link for the report
* input_url: This will link to an authenticated download link for crashing input
* target_url: This will link to an authenticated download link for the target
  executable
* repro_cmd: This will give an example command to initiate a live reproduction
  of a crash
* report_container: This will give the name of the report storage container
* report_filename: This will give the container relative path to the report

## Example Config

```json
{
   "config": {
      "auth": {
         "user": "INSERT_YOUR_USERNAME_HERE",
         "personal_access_token": "INSERT_YOUR_PERSONAL_ACCESS_TOKEN_HERE"
      },
      "organization": "contoso",
      "repository": "sample-project",
      "title": "{{ report.executable }} - {{report.crash_site}}",
      "body": "## Files\n\n* input: [{{ report.input_blob.name }}]({{ input_url }})\n* exe: [{{ report.executable }}]( {{ target_url }})\n* report: [{{ report_filename }}]({{ report_url }})\n\n## Repro\n\n `{{ repro_cmd }}`\n\n## Call Stack\n\n```{% for item in report.call_stack %}{{ item }}\n{% endfor %}```\n\n## ASAN Log\n\n```{{ report.asan_log }}```",
      "unique_search": {
         "field_match": ["title"],
         "string": "{{ report.executable }}"
      },
      "assignees": [],
      "labels": ["bug", "{{ report.crash_type }}"],
      "on_duplicate": {
         "comment": "Duplicate found.\n\n* input: [{{ report.input_blob.name }}]({{ input_url }})\n* exe: [{{ report.executable }}]( {{ target_url }})\n* report: [{{ report_filename }}]({{ report_url }})",
         "labels": ["{{ report.crash_type }}"],
         "reopen": true
      }
   }
}
```

For full documentation on the syntax, see [GithubIssueTemplate](../../src/pytypes/onefuzztypes/models.py))

## Integration

1. Create a [Personal access token](https://github.com/settings/tokens).
2. Update your config to specify your user and personal access token.
1. Add a notification to your OneFuzz instance.

    ```
    onefuzz notifications create <CONTAINER> @./config.json
    ```

Until the integration is deleted, when a crash report is written to the indicated container, 
issues will be created and updated based on the reports.

The OneFuzz SDK provides an example tool [fake-report.py](../../src/cli/examples/fake-report.py),
which can be used to generate a synthetic crash report to verify the integration
is functional.