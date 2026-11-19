# Notes on Implementing Kinesis

Notes on Kinesis.

## Overview

Kinesis is a bit different from most of the other job sources in this template.

A Kinesis stream is fundamentally composed of multiple shards, and the shards contain the job record messages. A default
Kinesis stream will have 4 shards.

Fundamentally, the Kinesis as a source of records is different from the other messaging technologies covered within this
template in a number of ways:

* Kinesis is a stream rather than a true message broker.
* As a stream, individual messages have no built-in mechanism to be reclaimed by the queue in the event of the container
  being stopped by a sudden and catastrophic problem outside its control (e.g. hardware failure). This is why the job
  worker will only proceed to move the tracker for a shard past the current batch of messages after all messages have
  been attempted. It is an intentional safety mechanism.
* The stream stores messages individually, but the iterator string used to progress in a short-term context only
  operates in batches.
* The stream stores messages in sequential order, but this template supports prioritizing messages received in an
  arbitrary order as defined by the chosen implementation of `ISourceMessageSorter`.

## Checkpoint and Lock Keys

Shard checkpoints and shard locks are stored under keys in the form `checkpoint:{application}:{stream}:{shard}` and
`lock:{application}:{stream}:{shard}`. The stream name comes from `JOB_SOURCE__KINESIS__STREAM_ARN`. Shard IDs such as
`shardId-000000000000` repeat across streams, so including the stream name keeps deployments that share Redis or the
DynamoDB checkpoint table from overwriting each other's checkpoints or holding each other's locks.

Earlier versions used `checkpoint:{application}-{shard}` and `lock:{application}-{shard}`. When upgrading:

* Long-term checkpoints carry over. If no DynamoDB record exists under the new key, the worker reads the old key, and
  its next checkpoint is saved under the new key. The stream is not replayed from `TRIM_HORIZON`.
* Short-term iterators in Redis do not carry over, but they only live for a few minutes, and a missing iterator falls
  back to the long-term checkpoint.
* Locks do not carry over. During a rolling deploy, an old instance and a new instance could both read the same shard
  and run its records twice. Stop the old instances before starting the new ones.

The fallback to the old checkpoint key can be removed once every shard has saved a checkpoint under the new key, or once
the old records have expired.

## Kinesis AI Audit Notes

The solutions to the above considerations for Kinesis are part of why an AI audit of the key Kinesis using the Composer
model took (and sometimes continues to take) issue with many points of the Kinesis job source's design. These objections
include but are not limited to:

* Worrying about "leaking" locks by keeping them for later storage rather than encapsulating all remaining operations in
  the method in a try-finally statement.
* Assuming that messages will not be acknowledged if the job failed to process, or that some message Ids will never be
  acknowledged for some other reason.
    * This one in particular might be solvable with a different method name that better implies that it is always
      called, but this is not a priority.
* Worrying about the "all-or-nothing" nature of processing a batch of messages from a shard. Composer is under the
  impression that progress can be incremented per message. It is wrong.

While Composer really doesn't like the Kinesis job source in particular, these issues are in fact fundamental to the
operation of Kinesis within this framework.

## Kinesis Failure Queue Messages

When `JOB_SOURCE__KINESIS__FAILURES__QUEUE_URL` is set, failed records are forwarded to that SQS queue. The stream only
advances once every record in a batch has been acknowledged, and a failed record is only acknowledged after it has been
forwarded. A record that SQS can never accept would hold up the stream, so every record is sent in a form that SQS
accepts:

* **Plain**: The record body, unchanged, with no message attributes. This is the usual case.
* **Base64**: The body contains characters that SQS does not allow (such as control characters). The message body is the
  base64 of the record's UTF-8 bytes, with the `BodyEncoding` attribute set to `base64`.
* **Reference**: The body is empty, or SQS still rejects it for a reason that will not go away (for example it is larger
  than the queue's maximum message size). The message is a small JSON object with `BodyOmitted`, `SourceMessageId`,
  `SourceAttributes` and `Reason` fields, and the `BodyOmitted` attribute is set to `true`. Use the source message ID
  (the sequence number) and shard ID to read the record again from the stream while it is still retained.

Base64 and reference messages also carry a `SourceMessageId` attribute and a `SourceShardId` attribute.

If SQS fails for a reason that could be transient (such as throttling) and retries run out, the record is not
acknowledged. The stream does not advance, and the record is processed again after the worker restarts.
