# One-shot local database setup: start SQL Server, apply migrations, create the least-privilege runtime user,
# seed the deterministic 100K demo dataset. Safe to re-run (every step is idempotent).
# Windows counterpart of scripts/dev-db-setup.sh.
# Usage (Windows PowerShell 5.1+ or PowerShell 7+):  .\scripts\dev-db-setup.ps1
#Requires -Version 5.1
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
# Windows PowerShell 5.1 pipes text to native commands as ASCII by default; send the SQL script as UTF-8 (no BOM).
$OutputEncoding = [System.Text.UTF8Encoding]::new($false)

# Runs a native command and stops on a non-zero exit code (like `set -e`, which PowerShell does not apply to native
# commands). ErrorActionPreference is relaxed for the call because Windows PowerShell turns redirected stderr output
# (docker's progress lines) into errors.
function Invoke-Native([string]$Step, [scriptblock]$Command) {
    $ErrorActionPreference = 'Continue'
    & $Command
    if ($LASTEXITCODE -ne 0) { throw "$Step failed (exit code $LASTEXITCODE)." }
}

foreach ($tool in 'docker', 'dotnet') {
    if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) { throw "$tool was not found on PATH." }
}

Push-Location (Split-Path -Parent $PSScriptRoot)
try {
    if (-not (Test-Path -LiteralPath .env)) {
        throw 'Missing .env - run: Copy-Item .env.example .env  (then set the passwords)'
    }

    Write-Host '==> Starting SQL Server (docker compose)'
    Invoke-Native 'docker compose up' { docker compose up -d --wait sqlserver }

    & (Join-Path $PSScriptRoot 'dev-env.ps1')

    Write-Host '==> Applying EF Core migrations'
    Invoke-Native 'migrate' { dotnet run --project backend/RequestManagement.Api --launch-profile http -- migrate }

    Write-Host '==> Creating least-privilege runtime user rm_app'
    # Visible only to this process and the docker exec below, never echoed; removed again in the finally block.
    $env:RM_APP_PASSWORD = (Get-Content -LiteralPath .env -Encoding UTF8 |
            Where-Object { $_ -like 'RM_APP_PASSWORD=*' } | Select-Object -Last 1) -replace '^RM_APP_PASSWORD=', ''
    if (-not $env:RM_APP_PASSWORD) { throw 'RM_APP_PASSWORD is not set in .env.' }
    # No double quotes inside the bash command: Windows PowerShell 5.1 does not escape them for native commands, and
    # bash does not word-split the value of an assignment, so they are not needed.
    Invoke-Native 'create-runtime-user.sql' {
        Get-Content -LiteralPath scripts/sql/create-runtime-user.sql -Raw -Encoding UTF8 -ErrorAction Stop |
            docker exec -i -e RM_APP_PASSWORD rm-sqlserver bash -c 'SQLCMDPASSWORD=$MSSQL_SA_PASSWORD /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -i /dev/stdin'
    }
    Remove-Item Env:RM_APP_PASSWORD

    Write-Host '==> Seeding demo data (idempotent)'
    Invoke-Native 'seed' { dotnet run --project backend/RequestManagement.Api --launch-profile http -- seed }

    Write-Host '==> Done. Start the API with (the connection strings are already set in this window):'
    Write-Host '    .\scripts\dev-env.ps1; dotnet run --project backend/RequestManagement.Api --launch-profile http'
}
finally {
    Remove-Item Env:RM_APP_PASSWORD -ErrorAction SilentlyContinue
    Pop-Location
}
