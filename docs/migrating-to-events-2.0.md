In an effort to increase the reliability of our event and webhook delivery, we are reducing the amount of data being sent. To account for the missing data, we added a new endpoint for downloading an event's complete payload.

## Example

### Current Event format

Here is an example webhook in Onefuzz version 8.1

```jsonc
{
    "event": {
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
          "onefuzz_version": "1.2.3",
          "scariness_description": "example-scariness",
          "scariness_score": 10,
          "task_id": "00000000-0000-0000-0000-000000000000",
          "tool_name": "libfuzzer",
          "tool_version": "1.2.3"
      }
    },
    "event_id": "00000000-0000-0000-0000-000000000000",
    "event_type": "crash_reported",
    "instance_id": "00000000-0000-0000-0000-000000000000",
    "instance_name": "example",
    "webhook_id": "00000000-0000-0000-0000-000000000000",
    "sas_url": "https://example.com", // <------ THIS IS NEW
    "version": "1.0"                  // <------ THIS IS NEW
}
```

Notice the `sas_url` and `version` properties are new.

### Events 2.0 sent event format

When events 2.0 is released, the above event will be _sent_ like this:

```json
{
    "event": {
      "container": "container-name",
      "report": {
          "input_blob": {
              "account": "contoso-storage-account",
              "container": "crashes",
              "name": "input.txt"
          },
          "job_id": "00000000-0000-0000-0000-000000000000",
          "task_id": "00000000-0000-0000-0000-000000000000",
      }
    },
    "event_id": "00000000-0000-0000-0000-000000000000",
    "event_type": "crash_reported",
    "instance_id": "00000000-0000-0000-0000-000000000000",
    "instance_name": "example",
    "webhook_id": "00000000-0000-0000-0000-000000000000",
    "sas_url": "https://example.com",
    "version": "2.0",
    "expiration_date": "01/01/2025"
}
```

You'll notice many fields are omitted. 

**`event_id`, `event_type`, `instance_id`, `instance_name`, `webhook_id`, `sas_url`, `version`, `expiration_date` WILL ALWAYS BE INCLUDED**

### How to retrieve the full event payload

There are 3 options for retrieving the full event payload as it would have been sent in events 1.0

1. Using the `sas_url` that is included in the event
2. Using the new events API at `GET https://{onefuzz instance}/api/events` with a request body `{ "event_id": "00000000-0000-0000-0000-000000000000" }`
3. Using the onefuzz cli `onefuzz events get "00000000-0000-0000-0000-000000000000"`

The event payload data will only be retained until the expiration date. We are currently planning to retain event data for 90 days but this may change in the future.

_**You can migrate your event handling code today to use any of those 3 options so that you will be unaffected by the future breaking change.**_
