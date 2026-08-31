#!/bin/sh
# sleepy-GUI on an X11 session.
set -e
# Resolve through symlinks: distribution packages put a link in /usr/bin while
# the app itself lives elsewhere.
self=$0
while [ -L "$self" ]; do
  link=$(readlink "$self")
  case "$link" in
    /*) self=$link ;;
    *) self=$(dirname "$self")/$link ;;
  esac
done
here=$(CDPATH= cd -- "$(dirname -- "$self")" && pwd)

if [ -z "$DISPLAY" ]; then
  echo "sleepy-GUI: no DISPLAY is set — is this an X11 session?" >&2
  exit 1
fi

exec "$here/sleepy-gui" --display x11 "$@"
