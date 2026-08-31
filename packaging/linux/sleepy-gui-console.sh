#!/bin/sh
# sleepy-GUI straight on the Linux framebuffer/DRM, with no X11 or Wayland
# session at all. Run it from a TTY.
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
exec "$here/sleepy-gui" --display framebuffer "$@"
