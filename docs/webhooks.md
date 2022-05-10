# Webhooks

Webhooks allow you to build integrations to OneFuzz that subscribe to events from 
your fuzzing workflow.  When an event is triggered, an HTTP POST will be sent to 
the URL for the webhook.

## Types of Events

See the [Webhook Event Types](webhook_events.md) for a full list of available webhook events, the schemas for each events, as well as an example.

## Configuring your webhooks

When configuring a webhook, you can specify which events to subscribe.

Example creating a webhook subscription only the `task_created` events:

```
$ onefuzz webhooks create MYWEBHOOK https://contoso.com/my-custom-webhook task_created
{
    "webhook_id": "cc6926de-7c6f-487e-96ec-7b632d3ed52b",
    "name": "MYWEBHOOK",
    "event_types": [
        "task_created"
    ]
}
$
```

Example creating a webhook subscription only the `task_created` events that produces webhook data in [Azure Event Grid](https://docs.microsoft.com/en-us/azure/event-grid/event-schema) compatible format:

```
$ onefuzz webhooks create MYWEBHOOK https://contoso.com/my-custom-webhook task_created --message_format event_grid
{
    "webhook_id": "cc6926de-7c6f-487e-96ec-7b632d3ed52b",
    "name": "MYWEBHOOK",
    "event_types": [
        "task_created"
    ]
}
$
```


### Listing existing webhooks

```
$ onefuzz webhooks list
[
    {
        "webhook_id": "cc6926de-7c6f-487e-96ec-7b632d3ed52b",
        "name": "MYWEBHOOK",
        "event_types": [
            "task_created"
        ]
    }
]
$
```

### Updating an existing webhook

This example updates the previously created webhook and sets the subscribed event types to `task_created` and `task_failed`.
```
$ onefuzz webhooks update cc6926de-7c6f-487e-96ec-7b632d3ed52b --event_types task_created task_failed
{
    "webhook_id": "cc6926de-7c6f-487e-96ec-7b632d3ed52b",
    "name": "MYWEBHOOK",
    "event_types": [
        "task_created",
        "task_failed"
    ]
}
$
```

## Testing your webhook

At any time, you can test your webhooks by sending a [ping](webhook_events#ping) event to your webhook
using the CLI.

```
$ onefuzz webhooks ping 1809010d-57fd-4085-a7ce-9d248895e651
{
    "ping_id": "f8c5694e-3307-4646-8489-45e6f897b7f6"
}
$
```

This example pings the webhook `1809010d-57fd-4085-a7ce-9d248895e651` and provides
the event payload that will be sent to the webhook.

## Securing your Webhook

When creating or updating a webhook, you can specify a `secret_token` which will be used to generate
an HMAC-SHA512 of the payloads, and which will be added to the HTTP headers as `X-Onefuzz-Digest`.