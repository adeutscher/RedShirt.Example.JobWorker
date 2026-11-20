# Notes on Implementing Redis Streams

Notes on Redis Streams.

## Acknowledgement

The worker reads new entries with `XREADGROUP` and acknowledges every entry with `XACK` once its job finishes, whatever
the result. Redis Streams has no dead-letter queue, so dead-lettering failed jobs is left to the `IJobFailureHandler`
implementation. The default handler does nothing.

## Reclaiming Pending Entries

An entry stays in the consumer group's pending entries list (PEL) from delivery until it is acknowledged. If a worker
crashes or is stopped mid-batch, its entries stay pending. Reading with `>` never returns them again, not even to a new
worker that uses the same consumer name, so without reclaiming those jobs never run.

Before reading new entries, the worker looks for pending entries that have been idle for at least
`JOB_SOURCE__REDIS_STREAMS__PENDING_RECLAIM_IDLE_SECONDS` (default 300), whichever consumer holds them:

* An entry delivered fewer than `JOB_SOURCE__REDIS_STREAMS__MAXIMUM_DELIVERIES` times (default 5) is claimed with
  `XCLAIM` and run again. Reclaimed entries count toward the batch size, and new entries fill the rest of the batch.
* An entry that has reached that number of deliveries is acknowledged without running and logged as a warning. This
  stops an entry that crashes the worker every time from looping forever. The entry is not dead-lettered.

Each delivery, including each reclaim, adds one to the entry's delivery count. When several workers look at the same
pending entries at once, the claim only succeeds for the first one, so each entry is reclaimed by one worker.

| Setting                        | Default      | Notes                                               |
| ------------------------------ | ------------ | --------------------------------------------------- |
| `PENDING_RECLAIM_IDLE_SECONDS` | 300          | Zero or negative disables reclaiming and heartbeats |
| `MAXIMUM_DELIVERIES`           | 5            | Zero or negative uses the default                   |
| `CONSUMER_NAME`                | Machine name | Must be unique for each running worker              |

### Heartbeats

While reclaiming is enabled, the worker sends a heartbeat for each running job every half of the reclaim idle time. A
heartbeat is an `XCLAIM ... JUSTID` of the entry to the worker's own consumer with a minimum idle time of 0. It resets
the entry's idle time without adding a delivery, so a long job is not reclaimed by another worker while it is still
running.

If a worker stalls for longer than the idle time, for example during a long garbage collection pause or a network
partition, another worker can reclaim its entry and run the job a second time. When the stalled worker recovers, its
next heartbeat takes the entry back, and either worker's acknowledgement clears it. Idempotency handling softens the
duplicate run.

Choose an idle time long enough that a healthy worker's heartbeats always arrive in time, and short enough that a job
lost with a crashed worker does not wait too long to run again.

### Requirements and Limits

* Reclaiming needs Redis 6.2 or later, for the `IDLE` option of `XPENDING`. On older versions, set
  `PENDING_RECLAIM_IDLE_SECONDS` to `0`.
* An entry trimmed from the stream while it is pending has nothing left to run. Redis 7 removes it from the PEL when it
  is claimed. Older versions keep it until it reaches the delivery limit and is dropped.
* Consumers are never removed from the group. Workers that use the machine name as their consumer name leave one
  consumer per container behind, which shows up in `XINFO CONSUMERS`. Remove idle consumers that hold no pending
  entries with `XGROUP DELCONSUMER` if this matters.
