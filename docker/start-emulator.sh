#!/usr/bin/env bash
# Starts the local Service Bus emulator. The SQL Server container it needs gets a random password, kept in
# docker/.env (git-ignored) so no password is ever committed.
set -euo pipefail
cd "$(dirname "$0")"

if [[ ! -f .env ]] || ! grep -q '^SBX_SQL_PASSWORD=' .env; then
  # SQL Server requires upper and lower case letters, digits and a symbol.
  # head stops reading early on purpose, so pipefail is off for this pipeline.
  random=$(set +o pipefail; LC_ALL=C tr -dc 'A-Za-z0-9' < /dev/urandom | head -c 24)
  echo "SBX_SQL_PASSWORD=Sbx-${random}9a" >> .env
  chmod 600 .env
  echo "Created docker/.env with a random SQL Server password."
fi

docker compose -f emulator.yml --env-file .env up -d "$@"
