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
# Loket 2 & 3: salinan binary Client dgn Config.txt LOKET_ID berbeda
CB="$DIR/SANTRI.Client.Mac/bin"
for id in 2 3; do
  if [[ ! -d "$CB/loket$id" ]]; then
    cp -R "$CB/Debug/net10.0" "$CB/loket$id"
    printf '=== PENGATURAN SISTEM ANTREAN ===\r\nREDIS_CONNECTION=127.0.0.1:6379\r\nLOKET_ID=%s\r\n' $id > "$CB/loket$id/Config.txt"
  fi
  (cd "$CB/loket$id" && nohup ./SANTRI.Client.Mac >/dev/null 2>&1 &)
done

echo "Preview jalan: Server (TV), Token (kiosk), Client loket 1-3."
