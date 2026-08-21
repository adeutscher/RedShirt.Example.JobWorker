#!/usr/bin/env python3

"""Create the local ActiveMQ Artemis address/queue used by the job worker.

Uses the Artemis Jolokia HTTP management API (stdlib only; no extra packages).
Safe to re-run if the queue already exists.
"""

import base64
import json
import sys
import time
import urllib.error
import urllib.request

JOLOKIA_URL = "http://localhost:8161/console/jolokia/"
BROKER_MBEAN = 'org.apache.activemq.artemis:broker="0.0.0.0"'
USERNAME = "admin"
PASSWORD = "admin"
QUEUE_NAME = "/queue/ActiveQueue"


def _auth_header() -> str:
    token = base64.b64encode(f"{USERNAME}:{PASSWORD}".encode()).decode()
    return f"Basic {token}"


def jolokia_exec(operation: str, arguments: list) -> dict:
    payload = json.dumps(
        {
            "type": "exec",
            "mbean": BROKER_MBEAN,
            "operation": operation,
            "arguments": arguments,
        }
    ).encode()
    request = urllib.request.Request(
        JOLOKIA_URL,
        data=payload,
        method="POST",
        headers={
            "Content-Type": "application/json",
            "Origin": "http://localhost",
            "Authorization": _auth_header(),
        },
    )
    with urllib.request.urlopen(request, timeout=10) as response:
        return json.loads(response.read().decode())


def wait_for_jolokia(timeout_seconds: int = 120) -> None:
    deadline = time.time() + timeout_seconds
    last_error = None
    while time.time() < deadline:
        try:
            request = urllib.request.Request(
                f"{JOLOKIA_URL}version",
                headers={
                    "Origin": "http://localhost",
                    "Authorization": _auth_header(),
                },
            )
            with urllib.request.urlopen(request, timeout=2) as response:
                if response.status == 200:
                    return
        except Exception as exc:  # noqa: BLE001 - local bootstrap helper
            last_error = exc
            time.sleep(2)
    raise RuntimeError(f"Artemis Jolokia was not ready at {JOLOKIA_URL}: {last_error}")


def ensure_queue() -> None:
    queue_config = json.dumps(
        {
            "name": QUEUE_NAME,
            "address": QUEUE_NAME,
            "routing-type": "ANYCAST",
            "durable": True,
        }
    )
    try:
        result = jolokia_exec(
            "createQueue(java.lang.String,boolean)",
            [queue_config, True],
        )
    except urllib.error.HTTPError as exc:
        body = exc.read().decode("utf-8", errors="replace")
        if "already exists" in body.lower():
            print(f"Queue '{QUEUE_NAME}' already exists.")
            return
        raise RuntimeError(f"Failed to create queue '{QUEUE_NAME}': {body}") from exc

    if result.get("status") == 200:
        value = result.get("value")
        if isinstance(value, str) and "id" not in value and QUEUE_NAME in value:
            # ignoreIfExists path often returns a slim JSON without a new id.
            print(f"Ensured queue '{QUEUE_NAME}' exists.")
        else:
            print(f"Created queue '{QUEUE_NAME}'.")
        return

    error = str(result.get("error", ""))
    if "already exists" in error.lower():
        print(f"Queue '{QUEUE_NAME}' already exists.")
        return

    raise RuntimeError(f"Failed to create queue '{QUEUE_NAME}': {result}")


def main() -> int:
    wait_for_jolokia()
    ensure_queue()
    print(json.dumps({"queue": QUEUE_NAME, "jolokia_url": JOLOKIA_URL}))
    return 0


if __name__ == "__main__":
    sys.exit(main())
