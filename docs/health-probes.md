# Health probes

Notes on health probes.

## Overview

The JobWorker application has a configurable set of health pages. When health endpoints are enabled, health is currently
determined by the amount of time since the most recent major exception caught in the `RedShirt.Example.JobWorker.Core`
project when it interacts with a service from the `RedShirt.Example.JobWorker.Common.Distributed` project or the
`IJobSource` implementation in one of the `JobManagement` projects.

| Endpoint          | Purpose    | Healthy response       | Unhealthy response           |
|-------------------|------------|------------------------|------------------------------|
| `GET /live`       | Liveness   | `200` plain text `OK`  | N/A                          |
| `GET /health`     | Health     | `200` plain text `OK`  | `503` plain text `unhealthy` |
| `GET /statistics` | Statistics | `200` JSON (see below) | N/A                          |

Environment variables related to health:

* `HEALTH__ENABLED`: HTTP listener with health pages (default: `true`). When `false`, the worker runs without binding a
  health port.
* `HEALTH__PORT`: TCP port for health endpoints, bound on `0.0.0.0` (default: `8080`).
* `HEALTH__RECENT_INCIDENT_THRESHOLD_SECONDS`: Amount of seconds after a major exception in `Core` project for which the
  system will be considered unhealthy.
* `JOBS__HALT_ON_FAILURE`: Related. If set to `true`, then the application shall immediately throw major exceptions to
  crash the application, making the health system moot. Only recommended for local development.

## Statistics Example

This is an example of the returned statistics model (C# definitions can be found in
`RedShirt.Example.JobWorker.Common.Health` in `Models/StatisticsModel.cs`:

```json
{
  "lifetime": {
    "successfulTimings": {
      "average": "00:00:00",
      "max": "00:00:00",
      "min": "00:00:00"
    },
    "totals": {
      "received": 0,
      "successful": 0,
      "cancelled": 0,
      "failed": 0,
      "invalidData": 0
    }
  },
  "uptime": "00:12:34.5678900"
}
```