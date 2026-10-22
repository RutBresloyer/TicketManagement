#!/usr/bin/env bash
# Walks through read -> update -> stale update (409) -> forbidden transition (422) -> history -> bulk -> summary
# against a running API, printing each request and the real response. Requires curl and python3.
# Usage: scripts/demo-api-flow.sh [base-url]    (default http://localhost:5080)
# NOTE: this performs real status changes on the demo database (run reset-and-seed to restore it).
set -euo pipefail
BASE="${1:-http://localhost:5080}/api/requests"
ACTOR="demo-user-1"

show() { python3 -c 'import json,sys; print(json.dumps(json.load(sys.stdin), indent=2))'; }
field() { python3 -c "import json,sys; print(json.load(sys.stdin)$1)"; }

echo "### 1. Pick the oldest 'New' request"
ID=$(curl -sf "$BASE?status=New&sortBy=createdAt&sortDirection=asc&pageSize=1" | field "['items'][0]['id']")
echo "GET $BASE/$ID"
REQ=$(curl -sf "$BASE/$ID"); echo "$REQ" | show
V1=$(echo "$REQ" | field "['rowVersion']")

echo; echo "### 2. PATCH with the token just read (expectedVersion=$V1) -> 200"
R2=$(curl -s -w '\n%{http_code}' -X PATCH "$BASE/$ID/status" -H 'Content-Type: application/json' -H "X-Actor-Id: $ACTOR" \
  -d "{\"status\":\"InProgress\",\"expectedVersion\":\"$V1\"}")
echo "HTTP $(echo "$R2" | tail -1)"; echo "$R2" | sed '$d' | show
V2=$(echo "$R2" | sed '$d' | field "['rowVersion']")

echo; echo "### 3. PATCH again with the now-stale token $V1 -> 409 (no overwrite, no audit row)"
R3=$(curl -s -w '\n%{http_code}' -X PATCH "$BASE/$ID/status" -H 'Content-Type: application/json' -H "X-Actor-Id: demo-user-2" \
  -d "{\"status\":\"Waiting\",\"expectedVersion\":\"$V1\"}")
echo "HTTP $(echo "$R3" | tail -1)"; echo "$R3" | sed '$d' | show

echo; echo "### 4. Forbidden transition InProgress -> New with the current token $V2 -> 422"
R4=$(curl -s -w '\n%{http_code}' -X PATCH "$BASE/$ID/status" -H 'Content-Type: application/json' -H "X-Actor-Id: $ACTOR" \
  -d "{\"status\":\"New\",\"expectedVersion\":\"$V2\"}")
echo "HTTP $(echo "$R4" | tail -1)"; echo "$R4" | sed '$d' | show

echo; echo "### 5. History of $ID (exactly one entry from step 2)"
curl -sf "$BASE/$ID/history" | show

echo; echo "### 6. Bulk: one valid change, one stale token, one missing ID"
NEXT=$(curl -sf "$BASE?status=New&sortBy=createdAt&sortDirection=asc&pageSize=1" | python3 -c 'import json,sys; i=json.load(sys.stdin)["items"][0]; print(i["id"], i["rowVersion"])')
NEXT_ID=${NEXT% *}; NEXT_V=${NEXT#* }
BODY="{\"items\":[{\"id\":$NEXT_ID,\"status\":\"Waiting\",\"expectedVersion\":\"$NEXT_V\"},{\"id\":$ID,\"status\":\"Completed\",\"expectedVersion\":\"$V1\"},{\"id\":999999999,\"status\":\"InProgress\",\"expectedVersion\":\"$V1\"}]}"
echo "PATCH $BASE/bulk-status  $BODY"
R6=$(curl -s -w '\n%{http_code}' -X PATCH "$BASE/bulk-status" -H 'Content-Type: application/json' -H "X-Actor-Id: $ACTOR" -d "$BODY")
echo "HTTP $(echo "$R6" | tail -1)"; echo "$R6" | sed '$d' | show

echo; echo "### 7. Summary (reflects the changes: cache was invalidated after each commit)"
curl -sf "$BASE/summary" | show
