# Various internal comms channels used in OneFuzz

Storage Queues:

* Per pool queue. These are used to queue Worksets to nodes in a given pool.
  (location: `<pool_id>` on `fuzz` storage account)

* Per task queue. These are used for tasks that use an input queue such as "new
  crashing inputs" (location: `<task_id>` on `fuzz` storage account)

* Per instance 'Heartbeat' queue. Agents send status 'heartbeat' messages to the
  API service (location: `heartbeats` on `func` storage account)

* Oauth2 enabled `Backchannel` HTTP endpoint. Agents send & receive messages via
  endpoint

  * location: `agent/commands` on `azure functions` instance
  * location: `agent/events` on `azure functions` instance
  * location: `agent/register` on `azure functions` instance

* Per instance file change notification queue. Event-Grid updates file updates
  in the `fuzz` storage account (location: `file-changes` on `func` storage
  account)

* Per instance proxy queue. Scaleset proxy agents send status updates to the API
  service (location: `proxy` on `func` storage account)

* Per instance update queue. API service uses this to queue updates in the
  future (location `update-queue` on `func` storage account)
