#!/usr/bin/env bash
# Usage (bash or zsh):  source scripts/dev-env.sh
#
# Builds the local connection strings from .env and exports them for the API, the CLI commands and the tests.
# Only the connection strings are exported (not the raw passwords) and nothing secret is printed.
#   ConnectionStrings__RequestManagement       API runtime    -> rm_app (least privilege), database RequestManagementDemo
#   ConnectionStrings__RequestManagementAdmin  migrate / seed -> sa,                       database RequestManagementDemo
#   ConnectionStrings__RequestManagementTests  test suite     -> sa,                       database RequestManagement_Tests
#
# TrustServerCertificate=True is ONLY acceptable for the local container's self-signed certificate.
# Against any shared server use a CA-signed certificate and TrustServerCertificate=False.

_rm_root="$(cd "$(dirname "${BASH_SOURCE[0]:-$0}")/.." && pwd)"
if [ ! -f "$_rm_root/.env" ]; then
  echo "Missing $_rm_root/.env - copy .env.example to .env and set the passwords." >&2
  return 1 2>/dev/null || exit 1
fi

_rm_sa=""; _rm_app=""; _rm_port="14333"
while IFS='=' read -r _rm_key _rm_value; do
  case "$_rm_key" in
    MSSQL_SA_PASSWORD) _rm_sa="$_rm_value" ;;
    RM_APP_PASSWORD) _rm_app="$_rm_value" ;;
    SQLSERVER_PORT) _rm_port="$_rm_value" ;;
  esac
done < "$_rm_root/.env"

_rm_server="Server=127.0.0.1,${_rm_port}"
_rm_tls="Encrypt=True;TrustServerCertificate=True"
export ConnectionStrings__RequestManagement="${_rm_server};Database=RequestManagementDemo;User ID=rm_app;Password=${_rm_app};${_rm_tls}"
export ConnectionStrings__RequestManagementAdmin="${_rm_server};Database=RequestManagementDemo;User ID=sa;Password=${_rm_sa};${_rm_tls}"
export ConnectionStrings__RequestManagementTests="${_rm_server};Database=RequestManagement_Tests;User ID=sa;Password=${_rm_sa};${_rm_tls}"

unset _rm_root _rm_sa _rm_app _rm_port _rm_key _rm_value _rm_server _rm_tls
echo "Exported ConnectionStrings__RequestManagement, __RequestManagementAdmin, __RequestManagementTests (values hidden)."
