#!/usr/bin/env python

"""Create the local Pulsar topic used by the job worker.

Uses the Pulsar admin HTTP API (no extra Python packages required).
Registers a STRING schema so it matches Schema.STRING on the .NET consumer.
"""

import json
import sys
import time
import urllib.error
import urllib.request

ADMIN_URL = "http://localhost:8080"
TENANT = "public"
NAMESPACE = "default"
TOPIC = "jobs"
FULL_TOPIC = f"persistent://{TENANT}/{NAMESPACE}/{TOPIC}"


def wait_for_admin(timeout_seconds: int = 120) -> None:
    deadline = time.time() + timeout_seconds
    last_error = None
    while time.time() < deadline:
        try:
            with urllib.request.urlopen(
                f"{ADMIN_URL}/admin/v2/clusters", timeout=2
            ) as response:
                if response.status == 200:
                    return
        except Exception as exc:  # noqa: BLE001 - local bootstrap helper
            last_error = exc
            time.sleep(2)
    raise RuntimeError(f"Pulsar admin API was not ready at {ADMIN_URL}: {last_error}")


def create_topic() -> None:
    url = f"{ADMIN_URL}/admin/v2/persistent/{TENANT}/{NAMESPACE}/{TOPIC}"
    request = urllib.request.Request(url, method="PUT", data=b"")
    request.add_header("Content-Type", "application/json")
    try:
        with urllib.request.urlopen(request, timeout=10) as response:
            print(f"Created topic {FULL_TOPIC} (HTTP {response.status})")
    except urllib.error.HTTPError as exc:
        if exc.code in (409, 500):
            # Topic already exists (some Pulsar versions return 500 for create-if-exists races).
            body = exc.read().decode("utf-8", errors="replace")
            if "already" in body.lower() or exc.code == 409:
                print(f"Topic {FULL_TOPIC} already exists")
                return
        raise


def upload_string_schema() -> None:
    # Matches PulsarConsumerFactory: client.NewConsumer(Schema.STRING(Encoding.UTF8))
    url = f"{ADMIN_URL}/admin/v2/schemas/{TENANT}/{NAMESPACE}/{TOPIC}/schema"
    payload = json.dumps({"type": "STRING", "schema": "", "properties": {}}).encode(
        "utf-8"
    )
    request = urllib.request.Request(url, method="POST", data=payload)
    request.add_header("Content-Type", "application/json")
    try:
        with urllib.request.urlopen(request, timeout=10) as response:
            print(f"Uploaded STRING schema for {FULL_TOPIC} (HTTP {response.status})")
    except urllib.error.HTTPError as exc:
        body = exc.read().decode("utf-8", errors="replace")
        if exc.code in (409, 412) and "incompatible" not in body.lower():
            print(f"STRING schema for {FULL_TOPIC} already present")
            return
        if "incompatible" in body.lower() or "BYTES" in body:
            raise RuntimeError(
                f"Topic {FULL_TOPIC} already has a BYTES/empty schema. "
                "Delete the topic (or recreate the Pulsar container) and rerun this "
                "script before send-pulsar-job.py. The worker uses STRING schema."
            ) from exc
        raise


def main() -> int:
    wait_for_admin()
    create_topic()
    upload_string_schema()
    print(json.dumps({"topic": FULL_TOPIC, "admin_url": ADMIN_URL}))
    return 0


if __name__ == "__main__":
    sys.exit(main())
