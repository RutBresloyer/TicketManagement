# Usage (Windows PowerShell 5.1+ or PowerShell 7+):  .\scripts\dev-env.ps1
#
# Windows counterpart of scripts/dev-env.sh. Builds the local connection strings from .env and sets them as environment
# variables of the current PowerShell window for the API, the CLI commands and the tests. A script can change the
# window's environment, so it does not need to be dot-sourced; run it again in every new window.
# Only the connection strings are set (not the raw passwords) and nothing secret is printed.
#   ConnectionStrings__RequestManagement       API runtime    -> rm_app (least privilege), database RequestManagementDemo
#   ConnectionStrings__RequestManagementAdmin  migrate / seed -> sa,                       database RequestManagementDemo
#   ConnectionStrings__RequestManagementTests  test suite     -> sa,                       database RequestManagement_Tests
#
# TrustServerCertificate=True is ONLY acceptable for the local container's self-signed certificate.
# Against any shared server use a CA-signed certificate and TrustServerCertificate=False.

# Runs in its own scope so no password is left behind in a session variable, even when the script is dot-sourced.
& {
    param([string]$Root)
    $envFile = Join-Path $Root '.env'
    if (-not (Test-Path -LiteralPath $envFile)) {
        throw "Missing $envFile - copy .env.example to .env and set the passwords."
    }

    $sa = ''; $app = ''; $port = '14333'
    foreach ($line in Get-Content -LiteralPath $envFile -Encoding UTF8) {
        $key, $value = $line -split '=', 2
        switch -CaseSensitive ($key) {
            'MSSQL_SA_PASSWORD' { $sa = $value }
            'RM_APP_PASSWORD' { $app = $value }
            'SQLSERVER_PORT' { $port = $value }
        }
    }

    $server = "Server=127.0.0.1,$port"
    $tls = 'Encrypt=True;TrustServerCertificate=True'
    $env:ConnectionStrings__RequestManagement = "$server;Database=RequestManagementDemo;User ID=rm_app;Password=$app;$tls"
    $env:ConnectionStrings__RequestManagementAdmin = "$server;Database=RequestManagementDemo;User ID=sa;Password=$sa;$tls"
    $env:ConnectionStrings__RequestManagementTests = "$server;Database=RequestManagement_Tests;User ID=sa;Password=$sa;$tls"
} (Split-Path -Parent $PSScriptRoot)

Write-Host 'Set ConnectionStrings__RequestManagement, __RequestManagementAdmin, __RequestManagementTests (values hidden).'
