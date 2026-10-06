#!/usr/bin/env bash
# Packs the Linux single-file build (dotnet publish src/Reyfen.Timbratune.Desktop -p:PublishProfile=linux-x64)
# into the two files users get, then removes the raw binary (v0.1.0 = <Version> in Directory.Build.props):
#   Timbratune-Desktop-v0.1.0-linux-x64.deb              Mint / Ubuntu / Debian: double-click → Install; then
#                                                      it's in the app menu (and `timbratune` in a terminal).
#   Timbratune-Desktop-v0.1.0-linux-x64.AppImage.tar.gz  any distribution: extract (double-click → Extract),
#                                                      then double-click the AppImage. The archive keeps its
#                                                      "run as program" bit, which a bare download loses.
# Run on Linux or in WSL. Needs dpkg-deb, ImageMagick (convert) and appimagetool
# (github.com/AppImage/appimagetool/releases; default: on PATH, or ~/tools/appimagetool).
#
#   scripts/package-linux.sh [path/to/appimagetool]
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
out_dir="$root/publish/Timbratune-linux-x64"
tool="${1:-$(command -v appimagetool || echo "$HOME/tools/appimagetool")}"
version="$(sed -n 's|.*<Version>\(.*\)</Version>.*|\1|p' "$root/Directory.Build.props" | head -1)"
name="Timbratune-Desktop-v$version-linux-x64"
binary="$out_dir/$name"
[ -f "$binary" ] || { echo "no $binary — publish the linux-x64 profile first"; exit 1; }
[ -x "$tool" ] || { echo "appimagetool not found ($tool)"; exit 1; }

# Build on the Linux filesystem: a Windows drive (/mnt/c) can't hold Unix permissions.
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
convert "$root/src/Reyfen.Timbratune/Assets/icon.ico[3]" "$work/timbratune.png"   # the 256 px frame

desktop_entry() { # $1 = Exec
  cat <<EOF
[Desktop Entry]
Type=Application
Name=Timbratune
Comment=Find the sound that feels like you: live voice feedback
Exec=$1
Icon=timbratune
Categories=AudioVideo;Audio;Education;
Terminal=false
EOF
}

# --- .deb ---------------------------------------------------------------------------------------
deb="$work/deb"
install -Dm755 "$binary" "$deb/opt/timbratune/Timbratune-Desktop"
mkdir -p "$deb/usr/bin"
ln -s /opt/timbratune/Timbratune-Desktop "$deb/usr/bin/timbratune"
install -Dm644 "$work/timbratune.png" "$deb/usr/share/icons/hicolor/256x256/apps/timbratune.png"
mkdir -p "$deb/usr/share/applications"
desktop_entry timbratune > "$deb/usr/share/applications/timbratune.desktop"
mkdir -p "$deb/DEBIAN"
cat > "$deb/DEBIAN/control" <<EOF
Package: timbratune
Version: $version
Architecture: amd64
Maintainer: Reyfen
Installed-Size: $(du -sk "$deb/opt" | cut -f1)
Depends: libc6, libx11-6, libice6, libsm6, libfontconfig1
Recommends: fonts-noto-color-emoji, pipewire-pulse | pulseaudio
Section: sound
Priority: optional
Description: Voice training feedback: pitch, resonance, weight and phrasing
 Record a take and see your pitch, resonance (formants), vocal weight, loudness,
 clarity and register/phrasing on one dashboard, live while you speak.
EOF
dpkg-deb --root-owner-group --build "$deb" "$work/$name.deb" >/dev/null

# --- AppImage -----------------------------------------------------------------------------------
app="$work/Timbratune.AppDir"
install -Dm755 "$binary" "$app/usr/bin/Reyfen.Timbratune.Desktop"
cp "$work/timbratune.png" "$app/timbratune.png"
desktop_entry Reyfen.Timbratune.Desktop > "$app/timbratune.desktop"
cat > "$app/AppRun" <<'EOF'
#!/bin/sh
here="$(dirname "$(readlink -f "$0")")"
exec "$here/usr/bin/Reyfen.Timbratune.Desktop" "$@"
EOF
chmod +x "$app/AppRun"
(cd "$work" && ARCH=x86_64 APPIMAGE_EXTRACT_AND_RUN=1 "$tool" --no-appstream "$app" "$work/$name.AppImage" >/dev/null)
chmod 755 "$work/$name.AppImage"
tar --owner=0 --group=0 -czf "$work/$name.AppImage.tar.gz" -C "$work" "$name.AppImage"

# --- into publish/ ------------------------------------------------------------------------------
rm -f "$binary"
cp "$work/$name.deb" "$work/$name.AppImage.tar.gz" "$out_dir/"
ls -la "$out_dir"
