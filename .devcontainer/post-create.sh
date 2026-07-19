#!/usr/bin/env bash
# Devcontainer post-create: restore packages and tools so the container is ready
# to build and run on first attach. Guarded so the script no-ops before the
# solution exists.
set -euo pipefail

if compgen -G "*.sln" > /dev/null || compgen -G "*.slnx" > /dev/null; then
  dotnet restore
  dotnet tool restore 2>/dev/null || true
fi
