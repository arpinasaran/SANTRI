#!/bin/zsh
# Jalankan preview GUI SANTRI di macOS (Redis + 3 aplikasi).
# Stop semua: ./run-preview.sh stop
set -e
DIR="$(cd "$(dirname "$0")" && pwd)"
export DOTNET_ROOT="/opt/homebrew/opt/dotnet/libexec"

if [[ "$1" == "stop" ]]; then
  pkill -f "SANTRI\..*\.Mac" 2>/dev/null || true
  /opt/homebrew/bin/redis-cli shutdown nosave 2>/dev/null || true
  echo "Preview dihentikan."
  exit 0
fi

/opt/homebrew/bin/redis-cli ping >/dev/null 2>&1 || /opt/homebrew/bin/redis-server --daemonize yes --port 6379 --save ''

for app in Server Token Client; do
  proj="$DIR/SANTRI.$app.Mac"
  bin="$proj/bin/Debug/net10.0/SANTRI.$app.Mac"
  [[ -x "$bin" ]] || /opt/homebrew/bin/dotnet build "$proj"
  (cd "$(dirname "$bin")" && nohup "./$(basename "$bin")" >/dev/null 2>&1 &)
done
echo "Preview jalan: Server (TV), Token (kiosk), Client (loket)."
