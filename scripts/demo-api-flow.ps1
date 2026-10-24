# Walks through read -> update -> stale update (409) -> forbidden transition (422) -> history -> bulk -> summary
# against a running API, printing each request and the real response. Windows counterpart of scripts/demo-api-flow.sh;
# needs only Windows PowerShell 5.1+ or PowerShell 7+ (no curl or python).
# Usage: .\scripts\demo-api-flow.ps1 [base-url]    (default http://localhost:5080)
# NOTE: this performs real status changes on the demo database (run reset-and-seed to restore it).
#Requires -Version 5.1
param([string]$BaseUrl = 'http://localhost:5080')
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$baseUri = $null
if (-not [Uri]::TryCreate($BaseUrl, [UriKind]::Absolute, [ref]$baseUri) -or $baseUri.Scheme -notin 'http', 'https') {
    throw "Base URL must be an absolute http(s) URL such as http://localhost:5080 (got '$BaseUrl')."
}
# The actor header and the status changes travel in clear text over http, so plain http is only allowed to this machine.
if ($baseUri.Scheme -eq 'http' -and -not $baseUri.IsLoopback) {
    throw "Refusing plain http to $($baseUri.Host); use https:// for anything other than localhost."
}
$base = $BaseUrl.TrimEnd('/') + '/api/requests'
$actor = 'demo-user-1'

Add-Type -AssemblyName System.Net.Http
$client = [System.Net.Http.HttpClient]::new()

# Returns the status code and raw body of every response, including 409/422 (Invoke-WebRequest throws on those in
# Windows PowerShell 5.1).
function Send-Request([string]$Method, [string]$Uri, [string]$ActorId, [string]$Body) {
    $request = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::new($Method), $Uri)
    if ($ActorId) { $request.Headers.Add('X-Actor-Id', $ActorId) }
    if ($Body) { $request.Content = [System.Net.Http.StringContent]::new($Body, [System.Text.Encoding]::UTF8, 'application/json') }
    try { $response = $client.SendAsync($request).GetAwaiter().GetResult() }
    catch { throw "$Method $Uri failed: $($_.Exception.GetBaseException().Message) Is the API running?" }
    [pscustomobject]@{
        Status = [int]$response.StatusCode
        Body   = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    }
}

# GET that must succeed (curl -f in the bash version).
function Get-Body([string]$Uri) {
    $response = Send-Request 'GET' $Uri
    if ($response.Status -lt 200 -or $response.Status -ge 300) { throw "GET $Uri returned HTTP $($response.Status)." }
    $response.Body
}

# Indents JSON without parsing it, so tokens and timestamps print exactly as the API sent them
# (ConvertFrom-Json | ConvertTo-Json would rewrite the timestamps in local time on PowerShell 7).
function Format-Json([string]$Json) {
    $out = [System.Text.StringBuilder]::new()
    $depth = 0
    $inString = $false
    for ($i = 0; $i -lt $Json.Length; $i++) {
        $c = $Json[$i]
        if ($inString) {
            [void]$out.Append($c)
            if ($c -eq '\') { $i++; [void]$out.Append($Json[$i]) }
            elseif ($c -eq '"') { $inString = $false }
        }
        elseif ($c -eq '"') { $inString = $true; [void]$out.Append($c) }
        elseif ($c -eq '{' -or $c -eq '[') {
            $j = $i + 1
            while ($j -lt $Json.Length -and [char]::IsWhiteSpace($Json[$j])) { $j++ }
            if ($j -lt $Json.Length -and ($Json[$j] -eq '}' -or $Json[$j] -eq ']')) {
                [void]$out.Append($c).Append($Json[$j])   # keep {} and [] on one line
                $i = $j
            }
            else { $depth++; [void]$out.Append($c).Append("`n").Append('  ' * $depth) }
        }
        elseif ($c -eq '}' -or $c -eq ']') { $depth--; [void]$out.Append("`n").Append('  ' * $depth).Append($c) }
        elseif ($c -eq ',') { [void]$out.Append(",`n").Append('  ' * $depth) }
        elseif ($c -eq ':') { [void]$out.Append(': ') }
        elseif (-not [char]::IsWhiteSpace($c)) { [void]$out.Append($c) }
    }
    $out.ToString()
}

function Get-OldestNewRequest {
    $items = @((Get-Body "${base}?status=New&sortBy=createdAt&sortDirection=asc&pageSize=1" | ConvertFrom-Json).items)
    if ($items.Count -eq 0) { throw 'No request in status New is left; run reset-and-seed to restore the demo data.' }
    $items[0]
}

"### 1. Pick the oldest 'New' request"
$id = (Get-OldestNewRequest).id
"GET $base/$id"
$details = Get-Body "$base/$id"
Format-Json $details
$v1 = ($details | ConvertFrom-Json).rowVersion

''
"### 2. PATCH with the token just read (expectedVersion=$v1) -> 200"
$r2 = Send-Request 'PATCH' "$base/$id/status" $actor ([ordered]@{ status = 'InProgress'; expectedVersion = $v1 } | ConvertTo-Json -Compress)
"HTTP $($r2.Status)"
Format-Json $r2.Body
$v2 = ($r2.Body | ConvertFrom-Json).rowVersion

''
"### 3. PATCH again with the now-stale token $v1 -> 409 (no overwrite, no audit row)"
$r3 = Send-Request 'PATCH' "$base/$id/status" 'demo-user-2' ([ordered]@{ status = 'Waiting'; expectedVersion = $v1 } | ConvertTo-Json -Compress)
"HTTP $($r3.Status)"
Format-Json $r3.Body

''
"### 4. Forbidden transition InProgress -> New with the current token $v2 -> 422"
$r4 = Send-Request 'PATCH' "$base/$id/status" $actor ([ordered]@{ status = 'New'; expectedVersion = $v2 } | ConvertTo-Json -Compress)
"HTTP $($r4.Status)"
Format-Json $r4.Body

''
"### 5. History of $id (exactly one entry from step 2)"
Format-Json (Get-Body "$base/$id/history")

''
'### 6. Bulk: one valid change, one stale token, one missing ID'
$next = Get-OldestNewRequest
$bulk = [ordered]@{ items = @(
        [ordered]@{ id = $next.id; status = 'Waiting'; expectedVersion = $next.rowVersion }
        [ordered]@{ id = $id; status = 'Completed'; expectedVersion = $v1 }
        [ordered]@{ id = 999999999; status = 'InProgress'; expectedVersion = $v1 }
    ) } | ConvertTo-Json -Depth 5 -Compress
"PATCH $base/bulk-status  $bulk"
$r6 = Send-Request 'PATCH' "$base/bulk-status" $actor $bulk
"HTTP $($r6.Status)"
Format-Json $r6.Body

''
'### 7. Summary (reflects the changes: cache was invalidated after each commit)'
Format-Json (Get-Body "$base/summary")
