#!/usr/bin/env bash
#
# Puts back what deploy/backup.sh took.
#
# It exists because a backup nobody has restored is a guess. Run it against a stack that is up and
# empty — a fresh set of volumes — and check that the dashboard shows what it showed before. Doing
# that once, deliberately, is the only way to find out whether the copies are worth anything, and
# the only moment to find out is not the moment you need them.
#
#   ./deploy/restore.sh backups/20260822T101500Z
#
# It refuses to run against stores that already have data in them. Restoring on top of a live
# installation is not a thing this can do safely: the control plane would be replaced wholesale
# while the telemetry gained a second copy of every row.

set -euo pipefail

readonly ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
readonly FROM="${1:-}"

if [[ -z "${FROM}" || ! -d "${FROM}" ]]; then
  echo "Usage: $0 <backup directory>" >&2
  exit 1
fi

if [[ ! -f "${FROM}/manifest.txt" ]]; then
  echo "${FROM} has no manifest.txt, so the backup that made it did not finish." >&2
  exit 1
fi

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

# Which stack this talks to. Left unset it is the one the compose file names. It is settable so that
# a restore can be proved against a fresh set of volumes without touching the installation the
# copies came from — which is the only test of a backup that counts, and the reason this script
# exists at all.
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

# ---------------------------------------------------------------------------
# Refuse to do this to something that is already in use
# ---------------------------------------------------------------------------
# Counted rather than assumed. The schema itself is created on start-up, so "the tables exist" says
# nothing; what says this is a fresh installation is that nobody has an account on it.
readonly ACCOUNTS="$(compose exec -T postgres psql \
  --username "${POSTGRES_USER}" \
  --dbname "${POSTGRES_DB}" \
  --tuples-only --no-align \
  --command "SELECT count(*) FROM users" 2>/dev/null || echo 0)"

if [[ "${ACCOUNTS}" != "0" ]]; then
  echo "This installation already has ${ACCOUNTS} account(s) on it." >&2
  echo "Restore into empty volumes, not on top of something in use." >&2
  exit 1
fi

echo "Restoring from ${FROM}"

# ---------------------------------------------------------------------------
# The control plane
# ---------------------------------------------------------------------------
# --clean drops what the dump is about to recreate, which is what makes this repeatable against a
# database the engine has already created a schema in on start-up.
echo "  postgres: restoring ${POSTGRES_DB}"
compose exec -T postgres pg_restore \
  --username "${POSTGRES_USER}" \
  --dbname "${POSTGRES_DB}" \
  --clean --if-exists --no-owner \
  < "${FROM}/control-plane.dump"

# ---------------------------------------------------------------------------
# The telemetry
# ---------------------------------------------------------------------------
# The tables the engine created on start-up are dropped and recreated from the copy's own schema, so
# what comes back is the shape the rows were written in rather than the shape today's migrations
# would produce. Anything newer is applied by the engine the next time it starts.
echo "  clickhouse: restoring the schema"
clickhouse --multiquery < "${FROM}/telemetry-schema.sql"

for file in "${FROM}"/telemetry-*.native.gz; do
  [[ -e "${file}" ]] || continue

  table="$(basename "${file}")"
  table="${table#telemetry-}"
  table="${table%.native.gz}"

  echo "  clickhouse: filling ${table}"
  gzip -dc "${file}" | clickhouse --query "INSERT INTO \`${table}\` FORMAT Native"
done

echo "Done. Open the dashboard and check that it shows what it showed before."
