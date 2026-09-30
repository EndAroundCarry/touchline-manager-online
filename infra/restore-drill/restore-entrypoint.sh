#!/bin/sh
#
# The restore service's entrypoint.
#
# A cluster restored from a base backup does not start recovery on its own: PostgreSQL 12 and later
# require a `recovery.signal` file beside `PGDATA` and the `restore_command`/`recovery_target_*`
# settings the compose service passes on the command line. The official entrypoint only starts an
# already-initialised cluster, so create the signal here and hand over.
set -e

touch "${PGDATA}/recovery.signal"

exec docker-entrypoint.sh "$@"
