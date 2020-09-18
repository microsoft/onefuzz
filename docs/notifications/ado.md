# Azure Devops Work Item creation

Automatic creation of ADO Work Items from OneFuzz allows for the user to
customize any field using [jinja2](https://jinja.palletsprojects.com/)
templates.

There are multiple Python objects provided via the template engine that
can be used such that any arbitrary component can be used to flesh out
the configuration:

* task (See [TaskConfig](../../src/pytypes/onefuzztypes/models.py))
* report (See [Report](../../src/pytypes/onefuzztypes/models.py))
* job (See [JobConfig](../../src/pytypes/onefuzztypes/models.py))

Using these objects allows dynamic configuration. As an example, the `project`
could be specified directly, or dynamically pulled from a template:

```json
{
  "project": "{{ task.tags['project'] }}"
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

Note, _most_, but not all fields in ADO take HTML. If you want the URL to be
clickable, make it a link.

# Example Config

```json
{
  "config": {
    "base_url": "https://dev.azure.com/org_name",
    "auth_token": "ADO_AUTH_TOKEN",
    "type": "Bug",
    "project": "Project Name",
    "ado_fields": {
      "System.AreaPath": "Area Path Here",
      "Microsoft.VSTS.Scheduling.StoryPoints": "1",
      "System.IterationPath": "Iteration\\Path\\Here",
      "System.Title": "{{ report.crash_site }} - {{ report.executable }}",
      "Microsoft.VSTS.TCM.ReproSteps": "This is my call stack: <ul> {% for item in report.call_stack %} <li> {{ item }} </li> {% endfor %} </ul>"
    },
    "comment": "This is my comment. {{ report.input_sha256 }} {{ input_url }} <br> <pre>{{ repro_cmd }}</pre>",
    "unique_fields": ["System.Title", "System.AreaPath"],
    "on_duplicate": {
      "comment": "Another <a href='{{ input_url }}'>POC</a> was found in <a href='{{ target_url }}'>target</a>. <br> <pre>{{ repro_cmd }}</pre>",
      "set_state": { "Resolved": "Active" },
      "ado_fields": {
        "System.IterationPath": "Iteration\\Path\\Here2"
      },
      "increment": ["Microsoft.VSTS.Scheduling.StoryPoints"]
    }
  }
}
```

# How to uniquely identify work items

The `unique_fields` is used as a tuple to uniquely identify an ADO work item.
For the above configuration, this evaluates to the following
[Wiql](https://docs.microsoft.com/en-us/azure/devops/boards/queries/wiql-syntax?view=azure-devops)
query.

Given the report crash site of "example" and executable of "fuzz.exe"

```
    select [System.Id] from WorkItems where
        [System.Title] = "example - fuzz.exe" AND
        [System.AreaPath] = "Area Path Here"
```

This allows for customized ADO work item de-duplication.

_NOTE_: In some instances, while work items are created serially, ADO work item
creation has latency such that created work items do not always immediately show
up in the queries. In some cases, this may cause spurious duplicate work items
in the case that duplicate crash reports occur in rapid succession.

# On creating a new work item

If no existing work items match the aforementioned tuple, a new work item is
created.

1. Define arbitrary rendered fields to be created.
2. Optionally provide a rendered comment. To not comment on the new work item,
   remove the `comment` field.

# On identifying duplicate work items

There are multiple configurable actions that can performed upon finding a
duplicate work item.

1. Add a rendered comment to the original work item. To add a comment, remove
   the `comment` field within `on_duplicate`.
2. Replace any field with a rendered value. In the above example,
   `System.IterationPath` replaced with `Iteration\\Path\\Here2` whenever a
   duplicate bug is found.
3. Increment any number of arbitrary fields. In the above example,
   `Microsoft.VSTS.Scheduling.StoryPoints` is initially set to 1 and incremented
   each time a duplicate crash report is found. To not increment any field, set
   it to an empty array.

# To provide no change on duplicate work items

To do nothing on duplicate reports, use the following `on_duplicate` entries:

```json
"on_duplicate": {
    "comment": null,
    "set_state": {},
    "fields": {},
    "increment": []
}
```

In the CLI, don't provide any of the --on*dup*\* arguments

# Example CLI usage:

To create a similar configuration monitoring the container
oft-my-demo-job-reports, use the following command:

```bash
onefuzz notifications create_ado oft-my-demo-job-reports \
    "Project Name" https://dev.azure.com/org_name \
    ADO_AUTH_TOKEN Bug System.Title System.AreaPath \
    --fields \
        System.AreaPath=OneFuzz-Ado-Integration \
        Microsoft.VSTS.Scheduling.StoryPoints=1 \
        "System.IterationPath=Iteration\\Path\\Here" \
        "System.Title={{ report.crash_site }} - {{ report.executable }}" \
        "Microsoft.VSTS.TCM.ReproSteps=This is my call stack: <ul> {% for item in report.call_stack %} <li> {{ item }} </li> {% endfor %} </ul>" \
    --comment "This is my comment. {{ report.input_sha256 }} {{ input_url }}" \
    --on_dup_comment "Another <a href='{{ input_url }}'>POC</a> was found in <a href='{{ target_url }}'>target</a>" \
    --on_dup_set_state Resolved=Active \
    --on_dup_fields "System.IterationPath=Iteration\\Path\\Here2" \
    --on_dup_increment Microsoft.VSTS.Scheduling.StoryPoints
```
