#!/bin/bash

[ -z "${1}" ] && exit 1

NATS_URL=nats://admin:admin@localhost:4222 nats pub -J foo "{\"SleepDurationSeconds\": ${1}}"