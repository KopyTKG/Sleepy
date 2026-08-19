#!/bin/sh
# sleepy-GUI on an X11 session.
set -e
here=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)

if [ -z "$DISPLAY" ]; then
  echo "sleepy-GUI: no DISPLAY is set — is this an X11 session?" >&2
  exit 1
fi

exec "$here/sleepy-gui" --display x11 "$@"
