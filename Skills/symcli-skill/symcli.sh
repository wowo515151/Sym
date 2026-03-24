#!/usr/bin/env sh
set -eu

SCRIPT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
REPO_ROOT=$(CDPATH= cd -- "$SCRIPT_DIR/../.." && pwd)
CLI_PROJECT="$REPO_ROOT/src/SymCLI/SymCLI.csproj"
CLI_RELEASE="$REPO_ROOT/src/SymCLI/bin/Release/net10.0/SymCLI"
CLI_DEBUG="$REPO_ROOT/src/SymCLI/bin/Debug/net10.0/SymCLI"

if [ -x "$CLI_RELEASE" ]; then
    exec "$CLI_RELEASE" "$@"
fi

if [ -x "$CLI_DEBUG" ]; then
    exec "$CLI_DEBUG" "$@"
fi

if [ ! -f "$CLI_PROJECT" ]; then
    echo "Error: could not find SymCLI project at $CLI_PROJECT" >&2
    exit 1
fi

exec dotnet run --project "$CLI_PROJECT" -- "$@"
