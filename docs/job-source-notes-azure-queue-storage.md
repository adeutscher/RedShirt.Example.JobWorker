# Notes on Implementing Azure Queue Storage

Notes on Azure Queue Storage.

## Failed Messages and Receive Limits

Azure Queue Storage has no negative acknowledgement and no dead-letter queue. When a job finishes, the worker:

* Deletes the message for successful and unrecoverable results (`Empty`, `Parsing`, `InvalidData`).
* Leaves the message for recoverable results (`Failure`, `Cancelled`), so that it becomes visible again after its
  visibility timeout and is retried.

Every receive increases the message's `DequeueCount`. To stop a poison message from looping forever, the worker uses
`JOB_SOURCE__AZURE_QUEUE_STORAGE__MAXIMUM_RECEIVES` (default 5, matching the `maxDequeueCount` default of Azure
Functions queue triggers):

* A recoverable failure on the last allowed receive deletes the message instead of leaving it.
* A message received more times than the limit is deleted as soon as it is received, without running it. This covers
  messages that were never acknowledged, for example because they crash the worker.

Each deletion is logged as a warning with the message ID. The message body is not kept anywhere. If failed messages need
to be kept, copy them to a separate poison queue before deleting them, as Azure Functions does with `<queue>-poison`.
