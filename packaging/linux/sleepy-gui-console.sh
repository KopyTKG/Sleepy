#!/bin/sh
# sleepy-GUI straight on the Linux framebuffer/DRM, with no X11 or Wayland
# session at all. Run it from a TTY.
set -e
here=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
exec "$here/sleepy-gui" --display framebuffer "$@"
