#!/bin/sh
# sleepy-GUI on a Wayland session.
#
# The app renders with Skia through Uno's X11 host, which on a Wayland session
# runs via XWayland — Uno has no native Wayland backend yet. Every Wayland
# compositor in common use ships XWayland, so this works out of the box; it is
# only called out so the window type is no surprise.
set -e
here=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)

if [ -z "$WAYLAND_DISPLAY" ]; then
  echo "sleepy-GUI: no WAYLAND_DISPLAY — use sleepy-gui-x11 on an X11 session." >&2
  exit 1
fi

if [ -z "$DISPLAY" ]; then
  echo "sleepy-GUI: Wayland session without XWayland (no DISPLAY). Enable XWayland," >&2
  echo "            or run sleepy-gui-console from a TTY." >&2
  exit 1
fi

exec "$here/sleepy-gui" --display x11 "$@"
