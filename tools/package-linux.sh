#!/usr/bin/env bash
#
# Builds the Boxwright Linux AppImage (ADR-0011): publishes the self-contained app,
# assembles a standard AppDir, and packs it with a pinned appimagetool. Companion to
# tools/package-windows.ps1.
#
# Unlike the Windows ZIP, the AppImage does NOT bundle QEMU: on Linux, Boxwright resolves
# qemu-system-* / qemu-img from PATH and opens displays via the system remote-viewer
# (virt-viewer) — both are system packages (see packaging/README-FIRST-linux.txt). So there
# is no GPL source-offer obligation here (architecture §10).
#
# Usage:
#   tools/package-linux.sh <version> [output-dir]
#   tools/package-linux.sh 0.3.0
#
# Runs on Linux (needs dotnet, curl, sha256sum). The `dotnet publish -r linux-x64` step can
# be smoke-checked on any OS; the appimagetool packing is Linux-only.
set -euo pipefail

VERSION="${1:-}"
if [ -z "$VERSION" ]; then
  echo "usage: $0 <version> [output-dir]" >&2
  exit 2
fi

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUTPUT_DIR="${2:-$REPO_ROOT/artifacts/linux-x64}"
PUBLISH_DIR="$OUTPUT_DIR/publish"
APPDIR="$OUTPUT_DIR/AppDir"
CACHE_DIR="$REPO_ROOT/artifacts/_cache"
APP_PROJ="$REPO_ROOT/src/Boxwright.App/Boxwright.App.csproj"

# --- Pinned appimagetool (immutable release + SHA-256). To bump: update both + ADR-0011. ---
APPIMAGETOOL_URL="https://github.com/AppImage/appimagetool/releases/download/1.9.1/appimagetool-x86_64.AppImage"
APPIMAGETOOL_SHA256="ed4ce84f0d9caff66f50bcca6ff6f35aae54ce8135408b3fa33abfc3cb384eb0"

echo "Boxwright Linux packaging -> $OUTPUT_DIR (version $VERSION)"

# 1. Publish self-contained (NOT trimmed, NOT single-file -> Avalonia-safe; see ADR-0009/0011).
echo "Publishing self-contained linux-x64 ..."
rm -rf "$PUBLISH_DIR"
dotnet publish "$APP_PROJ" -c Release -r linux-x64 \
  --self-contained true \
  -p:PublishSingleFile=false -p:PublishTrimmed=false -p:DebugType=none \
  -o "$PUBLISH_DIR"

if [ ! -f "$PUBLISH_DIR/Boxwright.App" ]; then
  echo "Publish is missing the Boxwright.App executable" >&2
  exit 1
fi

# 2. Assemble the AppDir (no QEMU bundled — system qemu + virt-viewer).
echo "Assembling AppDir ..."
rm -rf "$APPDIR"
mkdir -p "$APPDIR/usr/bin" \
         "$APPDIR/usr/share/icons/hicolor/256x256/apps" \
         "$APPDIR/usr/share/applications" \
         "$APPDIR/usr/share/doc/boxwright"
cp -r "$PUBLISH_DIR"/. "$APPDIR/usr/bin/"
chmod +x "$APPDIR/usr/bin/Boxwright.App"

cp "$REPO_ROOT/LICENSE" "$APPDIR/usr/share/doc/boxwright/"
cp "$REPO_ROOT/packaging/README-FIRST-linux.txt" "$APPDIR/usr/share/doc/boxwright/"

# Icon: top-level (for appimagetool), .DirIcon, and the hicolor theme path.
cp "$REPO_ROOT/packaging/boxwright.png" "$APPDIR/usr/share/icons/hicolor/256x256/apps/boxwright.png"
cp "$REPO_ROOT/packaging/boxwright.png" "$APPDIR/boxwright.png"
cp "$REPO_ROOT/packaging/boxwright.png" "$APPDIR/.DirIcon"

# .desktop: top-level (read by appimagetool) + the canonical applications path.
cat > "$APPDIR/boxwright.desktop" <<'DESKTOP'
[Desktop Entry]
Type=Application
Name=Boxwright
Comment=A cross-platform GUI for QEMU virtual machines
Exec=Boxwright.App
Icon=boxwright
Categories=System;Utility;
Keywords=qemu;vm;virtual machine;virtualization;
Terminal=false
StartupWMClass=Boxwright.App
DESKTOP
cp "$APPDIR/boxwright.desktop" "$APPDIR/usr/share/applications/boxwright.desktop"

# AppRun entrypoint. IMPORTANT: do NOT override $HOME — Boxwright stores VMs/ISOs/logs under
# ~/.local/share/Boxwright, so relocating HOME would hide (and orphan) the user's data.
cat > "$APPDIR/AppRun" <<'APPRUN'
#!/bin/sh
HERE="$(dirname "$(readlink -f "$0")")"
export LD_LIBRARY_PATH="${HERE}/usr/bin:${LD_LIBRARY_PATH}"
exec "${HERE}/usr/bin/Boxwright.App" "$@"
APPRUN
chmod +x "$APPDIR/AppRun"

# 3. Fetch + verify the pinned appimagetool.
mkdir -p "$CACHE_DIR"
TOOL="$CACHE_DIR/appimagetool-1.9.1-x86_64.AppImage"
if [ ! -f "$TOOL" ]; then
  echo "Downloading appimagetool ..."
  curl -fSL "$APPIMAGETOOL_URL" -o "$TOOL"
fi
ACTUAL="$(sha256sum "$TOOL" | cut -d' ' -f1)"
if [ "$ACTUAL" != "$APPIMAGETOOL_SHA256" ]; then
  echo "appimagetool SHA-256 mismatch:" >&2
  echo "  expected $APPIMAGETOOL_SHA256" >&2
  echo "  actual   $ACTUAL" >&2
  echo "Re-pin in this script (and ADR-0011) or investigate." >&2
  exit 1
fi
chmod +x "$TOOL"

# 4. Pack. APPIMAGE_EXTRACT_AND_RUN=1 avoids FUSE (GitHub-hosted runners lack it).
APPIMAGE="$OUTPUT_DIR/Boxwright-$VERSION-x86_64.AppImage"
rm -f "$APPIMAGE"
echo "Packing AppImage ..."
ARCH=x86_64 APPIMAGE_EXTRACT_AND_RUN=1 "$TOOL" "$APPDIR" "$APPIMAGE"

SIZE_MB="$(du -m "$APPIMAGE" | cut -f1)"
SHA="$(sha256sum "$APPIMAGE" | cut -d' ' -f1)"
echo ""
echo "Created $APPIMAGE (${SIZE_MB} MB)"
echo "SHA-256: $SHA"
