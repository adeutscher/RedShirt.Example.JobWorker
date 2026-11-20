# Notes on Implementing RabbitMQ

Notes on RabbitMQ.

## Failed Messages and Delivery Limits

When a job fails, the worker rejects (NAcks) its message:

* Unrecoverable results (`Empty`, `Parsing`, `InvalidData`) are rejected without requeue.
* Recoverable results (`Failure`, `Cancelled`) are requeued so that the message can be tried again, until the message
  reaches its delivery limit. On its last delivery, the message is rejected without requeue.

A message rejected without requeue is dead-lettered if the queue has a dead letter exchange, and dropped otherwise.
Configure a dead letter exchange on every queue that the worker consumes from, so that poison messages can be inspected
instead of lost.

How many times a message may be delivered depends on the queue type:

| Queue type | Delivery count                         | Deliveries before rejecting without requeue                   |
| ---------- | -------------------------------------- | ------------------------------------------------------------- |
| Quorum     | `x-delivery-count` header              | `JOB_SOURCE__RABBITMQ__MAXIMUM_DELIVERIES` (default 5)        |
| Classic    | Not tracked, only a `redelivered` flag | 2 (the first delivery and one redelivery), whatever the limit |

Classic queues cannot tell a second delivery from a hundredth, so the worker only requeues a classic queue message on
its first delivery. Use a quorum queue to allow more attempts. This also applies to the temporary queues declared when
`JOB_SOURCE__RABBITMQ__USE_EXCHANGE` is `true`, which are classic queues.

Since RabbitMQ 4.0, quorum queues also have a broker-side `delivery-limit` of 20 by default. Whichever limit is lower
applies. A policy that sets `delivery-limit` to `-1` disables the broker's limit.

The worker's limit is checked when it acknowledges a message. A message that crashes the worker every time is never
acknowledged, so the worker's limit cannot stop it. Whether the broker's `delivery-limit` counts those returns depends on
the RabbitMQ version, so check it for the version you run.
