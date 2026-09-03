# Notes on Implementing Kafka

Notes on Kafka.

## Overview

Kafka is more of an append-only event log rather than a traditional message queue.

I go into more detail on each of these points below, but the cliff notes for implementing Kafka are:

* This template's version of Kafka assumes no authentication, which is currently left as an excercise for the reader.
* It is strongly advised to use Batch mode polling when using Kafka as a job source.
* It is strongly advised to enable idempotency handling for Kafka. Basic enabling of idempotency handling hinges off of
  the `JOBS__IDEMPOTENCY__ENABLED` environment variable, with other options described in the configuration section of
  this document and demonstrated in `test/local/docker-compose.yaml`.

## Kafka Authentication

This general template was tested against a local Kafka container with no authentication set up. In addition to that,
there are currently 5 different SASL mechanisms to choose from when implementing authentication. Implementing
authentication for Kafka when adapting this template is currently left as an exercise for the reader.

Kafka clients are constructed in the `RedShirt.Example.JobWorker.JobManagement.Kafka` project, in
`Factories/KafkaConsumerFactory.cs`.

## Kinesis Comparisons and Batch Mode Recommendation

A Kafka topic is very similar to a Kinesis stream. I am going to be comparing Kafka to Kinesis very heavily in this
section because Kinesis is a much more established job source implementation that I have more experience with. For notes
specifically on Kinesis, please refer to [`job-source-notes-kinesis.md`](job-source-notes-kinesis.md).

The Kinesis comparison carries down to a basic implementation level of the technology. A Kafka topic is divided into
partitions, just as a Kinesis stream is divided into shards. Processing jobs from either of these sources involves some
layer of the process managing shard/partition ownership

However, a major difference between the available interfaces for Kafka and Kinesis and their implementations in this
template is how ownership of a partition/shard works:

* In Kinesis, the job source's application code lists and iterates through shards in an attempt to find one that does
  not have a distributed lock. The Kinesis job source then performs a `GetRecords` operation on that shard.
* In Kafka, our options are more limited.
    * Kafka does have an option to list individual partitions, but this is considered more of an admin action.
    * Instead, the client declares a Kafka consumer which simply calls `consumerObject.Consume(TimeSpan)`, with a
      TimeSpan for timeouts.
    * Along the same lines: with no ability to iterate through partitions, ownership of a partition is out of the
      client's hands.
        * The Kakfa server/cluster calculates ownership of a partition within a consumer group when the number of
          partitions changes or the number of connected clients changes. This means that a Kafka client can lose access
          to a partition while still working exactly as intended.

Commiting a message in Kafka (done by its offset) implies that every message before it in the partition has also been
processed. This template sorts the jobs retrieved by a job source and messages could be run in parallel worker threads
with different finish times. Under these conditions, it cannot be guaranteed that the batch of messages being commited
during acknowledgement is the next one on the partition's to-do list.

Because of this, it is strongly recommended to run message polling in Batch mode as opposed to Loader mode. With Batch
mode, the job source is polled as soon as the previous batch has finished. In Loader mode, the job loader handler could
have to wait for several seconds (based on the value of the `JOBS__MAX_IDLE_WAIT_SECONDS` environment variable) before
polling again.

## Kafka Idempotency Handling

As described in the above section on Kinesis comparisons, Kafka clients in a consumer group do not control over what
partitions they have authority to commit to. Ownership of a partition is reconsidered when the number of clients in a
consumer group or the number of partitions in a topic changes. A client can lose commit rights to a topic partition
through no fault of its own.

Because of the above point, it is *strongly* encouraged to enable idempotency support for your application if you are
using Kafka with multiple consumers. Basic enabling of idempotency handling hinges off of the
`JOBS__IDEMPOTENCY__ENABLED` environment variable, with other options described in the configuration section of this
document and demonstrated in `test/local/docker-compose.yaml`. For more information on the idempotency system, please
refer to [`idempotency.md`](idempotency.md).