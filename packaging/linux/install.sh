#!/bin/sh
# Installs sleepy-GUI for the current user: the app under
# ~/.local/share/sleepy-gui, launchers on PATH, and both desktop entries.
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
prefix="${PREFIX:-$HOME/.local}"

app_dir="$prefix/share/sleepy-gui"
bin_dir="$prefix/bin"
desktop_dir="$prefix/share/applications"
icon_dir="$prefix/share/icons/hicolor/512x512/apps"

mkdir -p "$app_dir" "$bin_dir" "$desktop_dir" "$icon_dir"
cp -r "$here/." "$app_dir/"
chmod +x "$app_dir/sleepy-gui" "$app_dir"/*.sh

for launcher in x11 wayland console; do
  ln -sf "$app_dir/sleepy-gui-$launcher.sh" "$bin_dir/sleepy-gui-$launcher"
done
ln -sf "$app_dir/sleepy-gui" "$bin_dir/sleepy-gui"

cp "$here/sleepy-gui-x11.desktop" "$here/sleepy-gui-wayland.desktop" "$desktop_dir/"

# The desktop entries reference Icon=sleepy-gui, so put the artwork where the
# icon theme will find it.
if [ -f "$here/sleepy-gui.png" ]; then
  cp "$here/sleepy-gui.png" "$icon_dir/sleepy-gui.png"
fi

command -v gtk-update-icon-cache >/dev/null 2>&1 &&
  gtk-update-icon-cache -q -t -f "$prefix/share/icons/hicolor" 2>/dev/null || true
command -v update-desktop-database >/dev/null 2>&1 &&
  update-desktop-database -q "$desktop_dir" 2>/dev/null || true

echo "Installed to $app_dir"
echo "Launchers: sleepy-gui, sleepy-gui-x11, sleepy-gui-wayland, sleepy-gui-console"
