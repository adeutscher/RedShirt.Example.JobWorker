#!/usr/bin/env python3

"""Publish a sleep-job message to the local ActiveMQ Artemis queue.

Requires the `stomp.py` Python module (`pip install stomp.py`).
"""

import base64
import json
import sys
import time
import urllib.error
import urllib.request

import stomp

QUEUE = "/queue/ActiveQueue"
HOST = "localhost"
PORT = 61616
USERNAME = "admin"
PASSWORD = "admin"
JOLOKIA_URL = "http://localhost:8161/console/jolokia/"
BROKER_MBEAN = 'org.apache.activemq.artemis:broker="0.0.0.0"'


class _ErrorListener(stomp.ConnectionListener):
    def __init__(self) -> None:
        self.error: str | None = None

    def on_error(self, frame) -> None:  # noqa: ANN001 - stomp.py frame type
        body = frame.body or ""
        headers = getattr(frame, "headers", {}) or {}
        message = headers.get("message") or body or "unknown STOMP error"
        self.error = message


def _auth_header() -> str:
    token = base64.b64encode(f"{USERNAME}:{PASSWORD}".encode()).decode()
    return f"Basic {token}"


def _disk_usage_hint() -> str | None:
    """Return a hint when Artemis is blocking producers due to max-disk-usage."""
    try:
        payload = json.dumps(
            {
                "type": "read",
                "mbean": BROKER_MBEAN,
                "attribute": ["DiskStoreUsage", "MaxDiskUsage"],
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
        with urllib.request.urlopen(request, timeout=3) as response:
            result = json.loads(response.read().decode())
        values = result.get("value") or {}
        usage = float(values.get("DiskStoreUsage", 0))
        max_usage = float(values.get("MaxDiskUsage", 90))
        usage_pct = usage * 100 if usage <= 1 else usage
        if usage_pct >= max_usage:
            return (
                f"Artemis is blocking producers: disk usage {usage_pct:.1f}% "
                f">= max-disk-usage {max_usage:.0f}%. "
                "Recreate the local activemq compose service "
                "(it raises max-disk-usage to 99 for local use), or free disk space."
            )
    except (urllib.error.URLError, TimeoutError, ValueError, TypeError, KeyError):
        return None
    return None


def main() -> int:
    if len(sys.argv) < 2:
        print(
            f"Usage: {sys.argv[0]} <sleep-seconds> [message-id]",
            file=sys.stderr,
        )
        return 1

    body = {"SleepDurationSeconds": int(sys.argv[1])}
    headers = {
        "content-type": "application/json",
        "persistent": "true",
        "receipt": "send-1",
    }
    if len(sys.argv) > 2:
        headers["correlation-id"] = sys.argv[2]

    listener = _ErrorListener()
    conn = stomp.Connection([(HOST, PORT)])
    conn.set_listener("errors", listener)
    conn.connect(USERNAME, PASSWORD, wait=True)
    try:
        # Destination must be prefixed with /queue/ for STOMP anycast.
        conn.send(body=json.dumps(body), destination=QUEUE, headers=headers)
        # Allow ERROR frames (e.g. broker disk full) to arrive before disconnect.
        time.sleep(0.25)
        if listener.error:
            hint = _disk_usage_hint()
            print(f"Failed to publish to {QUEUE}: {listener.error}", file=sys.stderr)
            if hint:
                print(hint, file=sys.stderr)
            return 1
    finally:
        if conn.is_connected():
            conn.disconnect()

    print(f"Published to {QUEUE}: {json.dumps(body)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
