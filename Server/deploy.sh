#!/usr/bin/env bash
# Relay sunucusunu VDS'e yükler, orada derler ve servisi yeniden başlatır.
# Kullanım: Server/deploy.sh [root@188.132.198.43]
# İlk kurulum (bir kez): Server/README.md içindeki "İlk kurulum" adımları.
set -euo pipefail

TARGET=${1:-root@188.132.198.43}
ROOT=$(cd "$(dirname "$0")/.." && pwd)

# Proje, protokolü ../../Assets altından okuyor; depo içindeki göreli yolları koruyarak gönder.
tar -C "$ROOT" --exclude=bin --exclude=obj -czf - \
    Server/HarvesterRelay Server/deploy Assets/scripts/Net/RelayProtocol.cs \
  | ssh "$TARGET" 'set -euo pipefail
      export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
      rm -rf /opt/harvester-relay/src
      mkdir -p /opt/harvester-relay/src
      tar -C /opt/harvester-relay/src -xzf -
      cd /opt/harvester-relay/src/Server/HarvesterRelay
      dotnet publish -c Release -o /opt/harvester-relay/app.new -v q
      rm -rf /opt/harvester-relay/app
      mv /opt/harvester-relay/app.new /opt/harvester-relay/app
      install -m 644 /opt/harvester-relay/src/Server/deploy/harvester-relay.service /etc/systemd/system/
      systemctl daemon-reload
      systemctl enable harvester-relay >/dev/null 2>&1
      systemctl restart harvester-relay
      sleep 1
      systemctl --no-pager --lines=5 status harvester-relay'
