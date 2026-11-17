#!/bin/sh
set -eu
case "$ORDERFLOW_PUBLIC_HOST" in ''|*[!A-Za-z0-9.-]*) echo 'Configure a DNS public host.' >&2; exit 1;; esac
envsubst '${ORDERFLOW_PUBLIC_HOST}' < /etc/orderflow/nginx.conf.template > /tmp/nginx.conf
exec nginx -c /tmp/nginx.conf -g 'daemon off;'
