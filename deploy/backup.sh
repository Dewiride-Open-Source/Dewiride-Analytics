#!/usr/bin/env bash
#
# Takes a copy of everything this product holds.
#
# Two stores, and they are backed up in different ways because they are different kinds of thing.
# PostgreSQL holds the control plane — who has an account, which websites they measure, what their
# settings are — and is small, so it is dumped whole. ClickHouse holds every visit ever recorded and
# is not small, so each table is written out in its own native format, which is the compact one its
# own INSERT can read straight back.
#
# Run it against a stack that is up:
#
#   ./deploy/backup.sh
#   ./deploy/backup.sh /mnt/backups          # somewhere other than ./backups
#
# It does not stop anything. PostgreSQL's dump is consistent on its own; ClickHouse's tables are
# append-mostly, so a copy taken while events are arriving is a copy that ends at some moment during
# the run rather than a broken one.
#
# What it does not do is take the copy anywhere. A backup on the same disk as the thing it is a
# backup of is a copy, not a backup — send the directory somewhere else afterwards, and check that
# it can be read back. deploy/restore.sh is how it is read back.

set -euo pipefail

readonly ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
readonly DESTINATION="${1:-${ROOT}/backups}"
readonly STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
readonly OUT="${DESTINATION}/${STAMP}"

# Loaded because the credentials the two stores were created with are the credentials they are read
# with, and they live in the same file the stack was started from.
if [[ -f "${ROOT}/.env" ]]; then
  set -a
  # shellcheck disable=SC1091
  source "${ROOT}/.env"
  set +a
fi

readonly POSTGRES_USER="${POSTGRES_USER:-dewiride}"
readonly POSTGRES_DB="${POSTGRES_DB:-dewiride_analytics}"
readonly CLICKHOUSE_USER="${CLICKHOUSE_USER:-dewiride}"
readonly CLICKHOUSE_DB="${CLICKHOUSE_DB:-dewiride_telemetry}"

if [[ -z "${CLICKHOUSE_PASSWORD:-}" ]]; then
  echo "CLICKHOUSE_PASSWORD is not set. Run this from the directory the stack was started in." >&2
  exit 1
fi

# Which stack this talks to. Left unset it is the one the compose file names, which is what anybody
# running this actually wants. It is settable so that a restore can be proved against a fresh set of
# volumes without touching the installation the copies came from — which is the only test of a
# backup that counts.
readonly PROJECT="${DEWIRIDE_COMPOSE_PROJECT:-}"

compose() {
  if [[ -n "${PROJECT}" ]]; then
    docker compose --project-directory "${ROOT}" --project-name "${PROJECT}" "$@"
  else
    docker compose --project-directory "${ROOT}" "$@"
  fi
}

clickhouse() {
  compose exec -T clickhouse clickhouse-client \
    --user "${CLICKHOUSE_USER}" \
    --password "${CLICKHOUSE_PASSWORD}" \
    --database "${CLICKHOUSE_DB}" \
    "$@"
}

mkdir -p "${OUT}"

echo "Backing up to ${OUT}"

# ---------------------------------------------------------------------------
# The control plane
# ---------------------------------------------------------------------------
# Custom format rather than plain SQL: it is compressed, it can be restored table by table, and it
# carries the ownership and the extensions the schema needs.
echo "  postgres: dumping ${POSTGRES_DB}"
compose exec -T postgres pg_dump \
  --username "${POSTGRES_USER}" \
  --dbname "${POSTGRES_DB}" \
  --format custom \
  --no-owner \
  > "${OUT}/control-plane.dump"

# ---------------------------------------------------------------------------
# The telemetry
# ---------------------------------------------------------------------------
# The schema first, so a restore into an empty server has somewhere to put the rows. Written as the
# statements that create the tables rather than as a migration, because what is being restored is
# this copy's shape and not whatever the current migrations would produce.
# The whole list is read before anything else runs, and this is not a style preference. Every call
# below is a client that reads its own standard input, and one running inside a loop fed by a pipe
# eats the rest of the list — so the first table is copied, the others are silently skipped, and the
# backup looks like it worked.
mapfile -t TABLES < <(clickhouse --query "SHOW TABLES" | tr -d '\r')

echo "  clickhouse: writing the schema"

# TSVRaw, not the default. The default escapes every newline in a value as two characters, and a
# CREATE TABLE statement is mostly newlines — the file would be syntactically valid nonsense.
#
# Each table is dropped before it is recreated, so a restore runs against a server the engine has
# already applied its migrations to on start-up, which is every server there is.
: > "${OUT}/telemetry-schema.sql"

for table in "${TABLES[@]}"; do
  [[ -z "${table}" ]] && continue

  printf 'DROP TABLE IF EXISTS `%s`;\n' "${table}" >> "${OUT}/telemetry-schema.sql"
  clickhouse --query "SHOW CREATE TABLE \`${table}\` FORMAT TSVRaw" >> "${OUT}/telemetry-schema.sql"
  printf ';\n\n' >> "${OUT}/telemetry-schema.sql"
done

# Native is ClickHouse's own on-disk representation of a result. It is the compact one and the one
# INSERT reads back without parsing anything, which for a table of this shape is the difference
# between minutes and hours.
for table in "${TABLES[@]}"; do
  [[ -z "${table}" ]] && continue
  echo "  clickhouse: copying ${table}"
  clickhouse --query "SELECT * FROM \`${table}\` FORMAT Native" \
    | gzip -6 > "${OUT}/telemetry-${table}.native.gz"
done

# ---------------------------------------------------------------------------
# What this copy is
# ---------------------------------------------------------------------------
# Written last, so its presence is what says the copy finished. A directory without it is a run that
# was interrupted, and restoring from one would restore half of something.
cat > "${OUT}/manifest.txt" <<MANIFEST
Dewiride Analytics backup
taken:      ${STAMP}
postgres:   ${POSTGRES_DB} (custom format dump)
clickhouse: ${CLICKHOUSE_DB} (schema plus one native file per table)
restore:    ./deploy/restore.sh ${OUT}
MANIFEST

echo "Done. ${OUT}"
du -sh "${OUT}" 2>/dev/null || true
