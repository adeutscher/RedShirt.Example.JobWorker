# Polling

This document describes this template's polling pattern. For the subscription pattern, see [
`message-sourcing-subscriptions.md`](message-sourcing-subscriptions.md).

## Overview and Alternative Terms

This template's original and default messaging strategy is polling with an exponential back-off.

Some message sources phrase polling in different ways:

* Explicit receive
* Unary get / Unary pull (Google Pub/Sub likes this one)

## Exponential Backoff

Exponential backoff is a strategy to minimize unnecessary calls during low traffic periods. If polling receives no
messages from an attempt then it will rest for an exponentially increasing amount of time that is capped by a
configurable value.

Exponential backoff serves two main purposes:

* Reduces load on the system, network, or message broker server.
* Reduces potential costs if the message broker is a remote solution bills per request.

In this template, the maximum amount of time that polling will wait for is capped by the configuration driven by the
`JOBS__MAX_IDLE_WAIT_SECONDS` environment variable.

## Batch Mode vs. Loader Mode

This template offers two different approaches to how messages are polled from a message source (internally referred to
as a job source):

* "Batch" mode will poll the source for a batch of messages and wait until all pulled messages have been processed
  before polling the job source again.
* "Loader" mode will maintain a buffer of messages in memory with the goal of reducing worker thread downtime.

Batch mode is the default mode for this template. To enable loader mode:

* Set the `JOBS__LOADER_MODE__ENABLED` environment variable to `true`.
* Optionally set `JOBS__LOADER_MODE__MINIMUM_BATCH_SIZE` (default effective value: `1`) so that the loader waits when
  free backlog capacity is below that size instead of polling for tiny refill batches.
* If you wish to change the default or to have your application use only one polling strategy, then you can adjust the
  logic in the `RedShirt.Example.JobWorker.Core` project's `Extensions/ServiceCollectionExtensions.cs` (as part of
  initializing this template).

Some job sources work better with Loader Mode than others, as the below subsections will explain.

### Important Note: Loader Mode + Kinesis

Important note: Before combining Loader mode with the Kinesis job source, please consider the below message about some
behaviours of the job source implementation that one should be aware of.

A Kinesis stream is fundamentally composed of multiple shards, and the shards contain the job record messages. A default
Kinesis stream will have 4 shards. The Kinesis job source implementation in this worker template operates by placing a
distributed lock on a shard until the worker has fully processed all the job record messages that it pulled from that
shard. The distributed lock prevents a parallel instance of the job worker from pulling the same records and duplicating
work (subject to any idempotency system the template implementation's work processor has in place).

Loader mode is designed to asynchronously poll for messages. This means that a single job worker instance using Loader
mode could potentially place a claim on all available shards on a Kinesis stream. This would limit potential for scaling
the stream consumer. The Kinesis job source might fundamentally be a better fit for the "Batch" mode message sourcing
for which it was originally designed. This noteworthiness could be a case for "Batch" mode to be refactored to have more
distinction between class roles rather than entirely replaced in the future.

If you choose to apply this template by combining Loader mode and Kinesis, please be aware of this warning.

## Long Polling

Several job sources can wait on the broker for the first messages of a poll instead of returning immediately when the
queue is idle. This increases the responsiveness of the JobWorker to new messages.

If you choose to configure your job worker for long polling, please consider the following points:

* This template's long polling strategy is fundamentally based around increased responsiveness by maintaining an open
  line for messages to be received. If one is using long polling, I would strongly advise configuring a low value to the
  Core job loader loop's incremental back-off limit (set by the `JOBS__MAX_IDLE_WAIT_SECONDS` variable). Leaving this at
  a high value as is encouraged for short-polling would leave your application flickering between periods of low
  responsiveness during back-off and periods of high responsiveness during long polling.
* Long polling implementations lean towards grabbing the first available messages and running with them, even if the
  batch is not fully fulfilled. If you wish to use multiple worker threads (as configured with the
  `JOBS__WORKER_THREAD_COUNT` environment variable), then this could leave some threads under-utilized in batch mode. I
  would suggest pairing long polling with Loader mode (setting `JOBS__LOADER_MODE__ENABLED` to `true`).

Long polling is configured on job sources that support it with a `WAIT_TIME_SECONDS` environment variable. A value of
`0` (the local compose default) is short-polling. A positive value is the number of seconds to wait on the **first**
request of a message-source fetch. If a job source implementation relies on multiple fetch calls within one call of
`IJobSource.GetJobsAsync`, then follow-up requests that attempt to fulfill the overall batch size count specified in the
request omit the long polling wait. This avoids a stacking delay to the delivery of already-received messages.

| Job source        | Environment variable                               | Effective range              |
|-------------------|----------------------------------------------------|------------------------------|
| Amazon SQS        | `JOB_SOURCE__SQS__WAIT_TIME_SECONDS`               | 0–20 (SQS long-poll maximum) |
| Apache Pulsar     | `JOB_SOURCE__PULSAR__WAIT_TIME_SECONDS`            | 0 or greater                 |
| Azure Service Bus | `JOB_SOURCE__AZURE_SERVICE_BUS__WAIT_TIME_SECONDS` | 0 or greater                 |
| Google Pub/Sub    | `JOB_SOURCE__GOOGLE_PUB_SUB__WAIT_TIME_SECONDS`    | 0–60                         |
| NATS              | `JOB_SOURCE__NATS__WAIT_TIME_SECONDS`              | 0 or greater                 |

The other job sources in this template (ActiveMQ, Kafka, Kinesis, Pulsar, RabbitMQ, Redis Streams, and Azure Queue
Storage) do not support long polling at this time. This is due to the constraints of the underlying technology or
interface library.

For configuration examples, see the `worker` section of the `test/local/docker-compose.yaml` file. Because some job
sources do not support long polling and long polling is a recent addition to the template, the defaults in the local
testing stack are still tuned for short polling.