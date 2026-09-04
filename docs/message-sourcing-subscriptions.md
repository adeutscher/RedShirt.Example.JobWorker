# Subscriptions

This document describes this template's subscription pattern. For the polling pattern, see [
`message-sourcing-polling.md`](message-sourcing-polling.md).

## Overview

Subscribing to a source is an option for allowing a job worker to be more responsive to messages without bombarding the
source with poll requests.

Subscribing is configured on job sources that support it with a `SUBSCRIBE` environment variable. Setting this value to
`true` will enable it.

| Job source | Environment variable              |
|------------|-----------------------------------|
| ActiveMQ   | `JOB_SOURCE__ACTIVEMQ__SUBSCRIBE` |
| NATS       | `JOB_SOURCE__NATS__SUBSCRIBE`     |
| RabbitMQ   | `JOB_SOURCE__RABBITMQ__SUBSCRIBE` |

## Terminology: Pull vs Poll

This section describes a bit of terminology confusion that I ran into when I was researching subscribe features on
different message broker technologies.

It may be tempting to think of subscribing to messages and being assigned a message by the message broker as being a
Pull operation. After all, RabbitMQ's underlying protocol has messages being delivered to a client. The subscription
mode in ActiveMQ and consuming in NATS involves reading from a stream that the server writes to, which could be interpreted as a Pull
or a Push from a certain point of view. However, these systems are not Push systems.

The terms Push and Pull in message brokers specifically refer to the directionality of the connection that the messages
are sent over. The mechanics of how messages are relayed at the protocol level is irrelevant. Thus, every message source
and acquisition method that this template supports is a Pull. A true Push system would involve a message broker actively
delegating messages to consumers.

Because of this, the template uses the terms polling and subscribe to describe messaging strategies.

## Notes on Implementing Other Subscribe Patterns

A subscription job source is made to leverage the consume feature of a message provider. Like with long polling, the
goal of a subscription pattern is increased responsiveness to new messages in an empty queue.

To be a good fit for subscribe mode, the fundamental implementation of the technology should involve the broker
technology delivering messages down the connection. For example:

* RabbitMQ broker delivers messages to clients through their existing connection.
* ActiveMQ broker writes to a stream for the client to pull.

If the fundamental behaviour of a subscription's at the client library level is still a poll behaviour, then I would
advise against implementing the subscription pattern and instead use this template's established poll pattern to pull
messages.

To give this some historical context: Azure Service Bus was considered for an implementation option using the subscribe
pattern, but the underlying behaviour of its client's processor is still a pull. Implementing this as a subscription
would have been a bespoke phrasing of the existing poll pattern. It would have offered no fundamental benefit to the
template that couldn't have been gained by adjusting the maximum rest time between polls and long polling time in the
existing logic.