#!/bin/sh
set -eu
REDISCLI_AUTH="$(cat /run/secrets/redis_password)"
export REDISCLI_AUTH
exec redis-cli -e ping # Server-side AUTH/command errors must also fail the probe.
