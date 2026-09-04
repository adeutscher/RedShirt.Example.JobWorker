# Idempotency

## Overview

In order to properly implement the idempotent consumer pattern, the outcome of processing the same message repeatedly
must be the same as processing the message once.

This template has support for idempotent operations by way of Redis caches. Idempotency is based off of the message ID
set in the messaging source, which is internally referred to as the idempotency ID. The idempotency ID is also exposed
to the `IJobLogicRunner` implementation so that implementation logic can potentially make decisions based on this
identifier (this is encouraged if possible, see the [Idempotency in Job Logic](#idempotency-in-job-logic) section of
this document).

## Feature History

Prior to its implementation in this template, the idea for idempotency came from working on an unrelated RabbitMQ
message consumer application. The connection to RabbitMQ would become unstable when the server was under load. Because
of how RabbitMQ functions, this meant that ownership of the messages would be lost by the consumer. Even in an
environment with only one message consumer instance, the retrieval of a message is tied to the specific connection that
it came from. Even if the connection to RabbitMQ is reestablished, it cannot be used to acknowledge the message from the
previous connection.

Thus, the idempotency system in this template and the other worker had two main objectives:

* The same message from a source should not be processed twice.
* The same message from a source should *especially* not be processed twice simultaneously.
* If a message is received twice, then that suggests that the previous retrieval of the message lost custody and is
  unable to acknowledge. The retrieval with custody should use the response status of the retrieval that lost custody
  once it becomes available rather than re-process the message.

## Feature Architecture

* Idempotency operations are centralized in the Idempotency Execution Service (`IIdempotencyExecutionService`).
* The Idempotency Execution Service is used extensively by the executor worker threads. However, the idempotency service
  is also written to return permissive stand-ins in the event that idempotency is not enabled in configuration.
* If idempotency support is enabled, then a monitor worker thread shall occasionally check the status of messages that
  were detected as being re-received in parallel to an original receive.
    * This parallel running could happen on this instance of the job worker process or another instance, as judgment is
      made by a distributed lock based in a Redis instance.
    * The monitor thread shall periodically try to re-acquire an exclusive lock on the message ID and to use the cached
      result to immediately acknowledge the message based on past processing.
    * If the monitor thread is able to acquire a lock but fails to retrieve a cached result, then the message is
      re-flagged in the job repository as being a candidate for execution.

### Redis Connection Instability Tolerance

Though being a proper idempotent consumer is the overall goal, this general template prioritizes overall stability over
strict idempotency. Non-critical exceptions encountered while interacting with Redis at the low-level are captured by a
safety layer.

If the application fails to interact with Redis, then the safety layers will enter a "disgrace" state, in which the
lower-level Redis services will not be attempted until the "disgrace" period has passed.

## Idempotency IDs

The Idempotency ID of a message is its unique identifier that allows the idempotency system to function. The application
will not crash if receives a job with a null idempotency ID, but it won't be able to act as an idempotent consumer.

* For message brokers like SQS or Azure Service Bus, the Idempotency ID value is set off of the messages ID from the
  system.
* For more stream-like job sources such as Kinesis or Kafka, the Idempotency ID value is based on an indication of a
  record's position in the stream.

For many job sources and configurations, this identifier is automatically generated. However, there are some sources and
configurations where it is not set.

### Concerning Idempotency ID Uniqueness

Many message sources can automatically provide Idempotency IDs that are reliably unique. However, some services allow
them to be specified by the publisher submitting the message.

The Idempotency IDs are considered to be reliably unique based on of the configuration variable
`JOBS__IDEMPOTENCY__IDEMPOTENCY_IDS_CAN_REPEAT=false`. If the IDs are said to not repeat, then a successful
acknowledgement of a message shall mean that the cached result for that message will be cleared or not entered into the
cache at all. This is done in hope of saving cache resources.

### RabbitMQ Message IDs

Of the current roster of job sources, RabbitMQ has no option to automatically generate a message ID for the application
to take as an idempotency key. If you are using RabbitMQ and wish to make use of idempotency, then you will need to make
sure that your message publishers are providing a message ID.

In the RabbitMQ browser view, this can be done by manually specifying the `message_id` property.

In C#, this would look like this:

```csharp
var properties = channel.CreateBasicProperties();
properties.MessageId = Guid.NewGuid().ToString();
properties.Persistent = true; // Optional: make message persistent

var body = Encoding.UTF8.GetBytes("Hello RabbitMQ");

// Assume that channel has already been declared.
channel.BasicPublish(
    exchange: "my-exchange",
    routingKey: "my-routing-key",
    mandatory: false,
    basicProperties: properties,
    body: body);
```

In Python (using the `pika` module):

```python
#!/usr/bin/env python

import uuid
import pika

properties = pika.BasicProperties(
    message_id=str(uuid.uuid4()),
    content_type="application/json",
    delivery_mode=2,  # Optional: make message persistent
)

body = b"Hello RabbitMQ"

# Assume that channel has already been declared.
channel.basic_publish(
    exchange="my-exchange",
    routing_key="my-routing-key",
    body=body,
    properties=properties,
)
```

### Redis Streams Message IDs

In practice, Redis Streams seems to also require manual setting of a message ID.

Important distinction for this template: the Redis stream entry ID is exposed as the job's `MessageId`, but the
idempotency key is taken from a `message_id` **field** on the stream entry. If you are using Redis Streams and wish to
make use of this template's idempotency features, then your publishers should set that field. Auto-generating or
manually specifying the Redis stream entry ID alone is not enough for the idempotency system.

Example in C# (StackExchange.Redis):

```csharp
var db = multiplexer.GetDatabase();
var fields = new NameValueEntry[]
{
    new("body", """{"SleepDurationSeconds":12}"""),
    // Supply a specific Redis stream entry ID
    new("message_id", Guid.NewGuid().ToString()) // Idempotency ID for this template
};

var specificEntryId = await db.StreamAddAsync("jobs", fields);
```

In Python (`redis-py`):

```python
#!/usr/bin/env python

import json
import uuid

import redis

client = redis.Redis(host="localhost", port=6379, decode_responses=True)
values = {
    "body": json.dumps({"SleepDurationSeconds": 12}),
    # Supply a specific Redis stream entry ID
    "message_id": str(uuid.uuid4()),  # Idempotency ID for this template
}

specific_entry_id = client.xadd("jobs", values)
```

Documentation purports that one can provide an asterisk to request that Redis auto-generate an ID for a message, but
this has not been my experience in practice.

## Idempotency in Job Logic

Being a general template, this application does not make use of the exposed idempotency ID in job logic, nor does it use
any other job properties in a similar manner. That being said, I would like to make a case for considering doing so.

Consider a message that requires a complex operation with multiple steps. For the sake of this document, we'll imagine
that there are two steps:

1. Submit information in remote system A.
2. Submit information in remote system B.

If a previous handling of the job previously failed on step 2, then there is a risk of repeating step 1. In our
perfect-world (or next to perfect, we are dealing with a re-run after all), we would not repeat step 1 by giving our
system the awareness that the step had been executed.

Possible options for being state-aware:

* Information could be placed in a cache such as Redis.
* To re-use our imaginary complex operation from earlier in this section, system A could potentially be polled to
  confirm if the information had already been submitted. 