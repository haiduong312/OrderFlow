#!/bin/sh
set -eu
# Read the private Compose secret without logging it or embedding it in image/configuration.
RABBITMQ_DEFAULT_PASS="$(cat /run/secrets/rabbitmq_password)"
export RABBITMQ_DEFAULT_PASS
exec /usr/local/bin/docker-entrypoint.sh "$@"
