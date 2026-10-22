# API Examples

All responses below are **real output** captured on 2026-10-22 from the running API (Development, `http://localhost:5080`)
against the seeded 100K dataset, by [`scripts/demo-api-flow.sh`](../scripts/demo-api-flow.sh). Re-run that script to
reproduce the whole flow on your machine.

> **`rowVersion` values will differ on your machine.** SQL Server's `rowversion` is a database-wide counter that only
> grows, even across `reset-and-seed`. Never copy a token from this page into a request: always read it from your own
> `GET` response first, exactly as shown in step 1.

Everything works the same in **Swagger UI** (`http://localhost:5080/swagger`, Try it out is enabled by default).
For PATCH calls, fill in the `X-Actor-Id` header field (e.g. `demo-user-1`) and paste the `rowVersion` from a GET into `expectedVersion`.
[`screenshots/06-swagger-try-it-out-409.png`](screenshots/06-swagger-try-it-out-409.png) shows the stale-token `409` produced through Swagger UI.

---

## 0. List with filters, search, sort and paging

```bash
curl -s 'http://localhost:5080/api/requests?status=New&priority=High&q=payment&page=1&pageSize=2&sortBy=createdAt&sortDirection=desc'
```

```json
{
  "items": [
    {
      "id": 78471,
      "title": "Configure payment provider timeout (REF-078471)",
      "organizationName": "Coastal Valley Partners",
      "status": "New",
      "priority": "High",
      "assignedTo": "agent-02",
      "createdAt": "2026-09-28T23:04:56+00:00",
      "updatedAt": "2026-09-28T23:04:56+00:00",
      "rowVersion": "AAAAAAABO4w=",
      "allowedNextStatuses": ["InProgress", "Waiting"]
    },
    { "id": 27166, "title": "Approve payment provider timeout (REF-027166)", "...": "..." }
  ],
  "page": 1,
  "pageSize": 2,
  "totalCount": 584,
  "totalPages": 292
}
```

(The second item is abbreviated here. The run was made before the final reseed, so tokens differ from the other steps; filters and counts are stable across reseeds.)

## 1. Read a request (and its token)

```bash
curl -s http://localhost:5080/api/requests/45833
```

```json
{
  "id": 45833,
  "title": "Investigate refund request (REF-045833)",
  "organizationName": "Atlas Valley Ltd",
  "status": "New",
  "priority": "High",
  "assignedTo": "agent-10",
  "createdAt": "2025-04-09T00:21:04+00:00",
  "updatedAt": "2025-04-09T00:21:04+00:00",
  "rowVersion": "AAAAAAADzBY=",
  "allowedNextStatuses": ["InProgress", "Waiting"]
}
```

## 2. Change the status with the token you just read → `200`

```bash
curl -s -X PATCH http://localhost:5080/api/requests/45833/status \
  -H 'Content-Type: application/json' -H 'X-Actor-Id: demo-user-1' \
  -d '{"status":"InProgress","expectedVersion":"AAAAAAADzBY="}'
```

```json
{
  "id": 45833,
  "status": "InProgress",
  "updatedAt": "2026-10-22T12:00:24.417413+00:00",
  "rowVersion": "AAAAAAAEoII=",
  "allowedNextStatuses": ["Waiting", "Completed"],
  "...": "other fields unchanged"
}
```

The new `rowVersion` is the value SQL Server generated for the committed row.

## 3. Re-use the now-stale token → `409 Conflict`

A second user who read the request at step 1 still holds `AAAAAAADzBY=`:

```bash
curl -s -X PATCH http://localhost:5080/api/requests/45833/status \
  -H 'Content-Type: application/json' -H 'X-Actor-Id: demo-user-2' \
  -d '{"status":"Waiting","expectedVersion":"AAAAAAADzBY="}'
```

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.10",
  "title": "The request was changed by someone else.",
  "status": 409,
  "detail": "The request was modified by someone else after you read it. Refresh it and decide whether to retry.",
  "instance": "/api/requests/45833/status",
  "traceId": "00-5f1e0d606cbb1e42d9c741f4f2a5a536-a2dde330033649d2-00",
  "code": "ConcurrencyConflict"
}
```

Nothing was written: the status stays `InProgress` and no history row is added (see step 5).

## 4. Forbidden transition → `422 Unprocessable Entity`

```bash
curl -s -X PATCH http://localhost:5080/api/requests/45833/status \
  -H 'Content-Type: application/json' -H 'X-Actor-Id: demo-user-1' \
  -d '{"status":"New","expectedVersion":"AAAAAAAEoII="}'
```

```json
{
  "type": "https://tools.ietf.org/html/rfc4918#section-11.2",
  "title": "This status transition is not allowed.",
  "status": 422,
  "detail": "Cannot change status from 'InProgress' to 'New'. Allowed from 'InProgress': Waiting, Completed.",
  "instance": "/api/requests/45833/status",
  "traceId": "00-cbc9991c5cd0f67fff0ae96b8307c94d-e9f9828a4a7c52f6-00",
  "code": "InvalidStatusTransition",
  "currentStatus": "InProgress",
  "requestedStatus": "New",
  "allowedNextStatuses": ["Waiting", "Completed"]
}
```

## 5. History → exactly one entry, same timestamp as `updatedAt`

```bash
curl -s http://localhost:5080/api/requests/45833/history
```

```json
[
  {
    "id": 133769,
    "previousStatus": "New",
    "newStatus": "InProgress",
    "changedAt": "2026-10-22T12:00:24.417413+00:00",
    "changedBy": "demo-user-1"
  }
]
```

## 6. Bulk (partial success) → `200` with one result per item, in input order

```bash
curl -s -X PATCH http://localhost:5080/api/requests/bulk-status \
  -H 'Content-Type: application/json' -H 'X-Actor-Id: demo-user-1' \
  -d '{"items":[
        {"id":58882,"status":"Waiting","expectedVersion":"AAAAAAAD/0I="},
        {"id":45833,"status":"Completed","expectedVersion":"AAAAAAADzBY="},
        {"id":999999999,"status":"InProgress","expectedVersion":"AAAAAAADzBY="}]}'
```

```json
{
  "succeeded": 1,
  "failed": 2,
  "results": [
    { "id": 58882, "outcome": "Updated", "rowVersion": "AAAAAAAEoIQ=", "status": "Waiting", "error": null },
    { "id": 45833, "outcome": "ConcurrencyConflict", "rowVersion": null, "status": null,
      "error": { "code": "ConcurrencyConflict", "message": "The request was modified by someone else after you read it. Refresh it and decide whether to retry." } },
    { "id": 999999999, "outcome": "NotFound", "rowVersion": null, "status": null,
      "error": { "code": "NotFound", "message": "No request with this ID exists." } }
  ]
}
```

Item 1 committed (with its own audit row) even though items 2 and 3 failed.

## 7. Summary → reflects the committed changes immediately

```bash
curl -s http://localhost:5080/api/requests/summary
```

```json
{
  "totalCount": 100000,
  "byStatus": [
    { "status": "New", "count": 24834 },
    { "status": "InProgress", "count": 30073 },
    { "status": "Waiting", "count": 15130 },
    { "status": "Completed", "count": 29963 }
  ],
  "byPriority": [
    { "priority": "Low", "count": 34979 },
    { "priority": "Medium", "count": 44962 },
    { "priority": "High", "count": 20059 }
  ],
  "generatedAt": "2026-10-22T12:00:24.867188+00:00"
}
```

Compared with the fresh seed (`New` 24,836 / `InProgress` 30,072 / `Waiting` 15,129): two requests left `New`, one
entered `InProgress` and one entered `Waiting`.

## 8. Validation errors → `400` ValidationProblemDetails

```bash
curl -s 'http://localhost:5080/api/requests?status=New,InProgress&pageSize=101'
```

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "detail": "See 'errors' for the invalid fields.",
  "instance": "/api/requests",
  "errors": {
    "status": ["'New,InProgress' is not a valid value for status. Allowed values: New, InProgress, Waiting, Completed."],
    "pageSize": ["pageSize must be between 1 and 100."]
  },
  "traceId": "00-fb127725117c0e0d85f91bdc556205f1-f66ae0d36022ee28-00",
  "code": "ValidationFailed"
}
```

(MVC's default enum binding would have silently turned `New,InProgress` into `Waiting`, the numeric OR of 1 and 2;
the strict binder in `Validation/StrictQueryModelBinders.cs` rejects it.)

Other `400` cases covered by tests: `status=2` (numeric enums are not part of the contract), unknown `sortBy`, malformed or
reversed dates, `q` over 100 characters, non-positive IDs, a missing or malformed `X-Actor-Id`, an `expectedVersion` that is not
base64 of exactly 8 bytes, numeric enum values in JSON bodies, and a bulk call that is empty, has more than 100 items or repeats an ID.
