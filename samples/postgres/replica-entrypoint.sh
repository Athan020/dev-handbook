#!/bin/sh
# Starts a streaming-replication read replica of the `postgres` service (B1 stage 6).
# On first start it clones the primary with pg_basebackup; after that it follows the
# primary's write-ahead log. REPLICA_DELAY (milliseconds) holds back applying changes,
# so you can watch replication lag on purpose.
set -e
PGDATA=/var/lib/postgresql/18/docker

if [ ! -s "$PGDATA/PG_VERSION" ]; then
    echo "Cloning the primary with pg_basebackup. This copies the whole database once."
    mkdir -p "$PGDATA"
    chown -R postgres:postgres /var/lib/postgresql
    chmod 700 "$PGDATA"
    gosu postgres pg_basebackup -h postgres -U bank -D "$PGDATA" -X stream -R --progress
fi

exec gosu postgres postgres -D "$PGDATA" \
    -c hot_standby=on \
    -c recovery_min_apply_delay="${REPLICA_DELAY:-0}ms" \
    -c shared_preload_libraries=pg_stat_statements
