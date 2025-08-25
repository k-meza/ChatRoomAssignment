#!/usr/bin/env bash
set -euo pipefail

# Change working directory to solution root (parent of API)
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
cd "$ROOT_DIR"

pids=()

echo "Starting Bot.Worker..."
dotnet run --project "Bot.Worker/Bot.Worker.csproj" &
pids+=($!)
sleep 2

echo "Starting API..."
dotnet run --project "API/API.csproj" &
pids+=($!)
sleep 2

echo "Starting WebApp..."
dotnet run --project "WebApp/WebApp.csproj" &
pids+=($!)

cleanup() {
  echo ""
  echo "Stopping processes..."
  for pid in "${pids[@]}"; do
    if kill -0 "$pid" 2>/dev/null; then
      kill "$pid" 2>/dev/null || true
    fi
  done
  wait || true
  echo "Done."
}

trap cleanup INT TERM

echo "All processes started. Press Ctrl+C to stop."
wait