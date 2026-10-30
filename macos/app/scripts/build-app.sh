#!/bin/bash
# Builds "Sentry Six Manager.app" and its .dmg on an Apple Silicon Mac with
# Xcode (docs/plans/macos-app.md, section 10):
#
#   bash macos/app/scripts/build-app.sh [--out DIR] [--work DIR] [--engine-dir DIR]
#                                       [--skip-sign] [--skip-notarize] [--skip-dmg]
#
#   --engine-dir   use an engine folder already laid out (macos/build.sh's
#                  package, unpacked) instead of building one
#   --skip-sign    no signature at all (the app then does not open on Apple
#                  Silicon; for looking at the bundle only)
#
# The order matters: the engine's programs are signed first, because signing
# changes their bytes; then MANIFEST.sha256 lists every engine file as it
# ships; its own SHA-256 is compiled into the app (the admin step checks the
# staged copy against it, 3.4); then the app is built, assembled, signed
# inside out, notarized and put in a dmg.
#
# Signing uses Developer ID and notarization when all five secrets are in the
# environment (APPLE_ID, APPLE_TEAM_ID, APPLE_APP_PASSWORD, DEVELOPER_ID_P12,
# DEVELOPER_ID_P12_PASSWORD); otherwise everything is signed ad hoc and the
# dmg is named ...-unsigned.dmg. No secret is ever printed.
#
# bash 3.2 (macOS's own) is enough.

set -euo pipefail

HERE=$(cd "$(dirname "$0")" && pwd)
APP_SRC=$(cd "$HERE/.." && pwd)
MACOS=$(cd "$APP_SRC/.." && pwd)
REPO=$(cd "$MACOS/.." && pwd)
BUNDLE_SRC="$APP_SRC/bundle"
APP_NAME="Sentry Six Manager"
AGENT_PLIST=io.github.skittles-fivem.sentry-six-manager.agent.plist

OUT=""
WORK=""
ENGINE_DIR=""
SIGN=1
NOTARIZE=1
DMG=1

say() { printf '%s\n' "$*"; }
fail() { printf 'build-app.sh: %s\n' "$*" >&2; exit 1; }

while [ $# -gt 0 ]; do
  case "$1" in
    --out) [ $# -ge 2 ] || fail "--out needs a folder"; OUT=$2; shift 2 ;;
    --work) [ $# -ge 2 ] || fail "--work needs a folder"; WORK=$2; shift 2 ;;
    --engine-dir) [ $# -ge 2 ] || fail "--engine-dir needs a folder"; ENGINE_DIR=$2; shift 2 ;;
    --skip-sign) SIGN=0; NOTARIZE=0; shift ;;
    --skip-notarize) NOTARIZE=0; shift ;;
    --skip-dmg) DMG=0; shift ;;
    -h|--help) sed -n '2,25p' "$0"; exit 0 ;;
    *) fail "unknown option $1" ;;
  esac
done

[ "$(uname -s)" = Darwin ] || fail "build on a Mac"
[ "$(sysctl -n hw.optional.arm64 2>/dev/null || true)" = 1 ] || fail "build on an Apple Silicon Mac (Intel Macs are not supported)"
for t in swift codesign lipo plutil shasum ditto hdiutil tar; do
  command -v "$t" >/dev/null 2>&1 || fail "$t is missing (install Xcode)"
done

[ -n "$OUT" ] || OUT="$APP_SRC/dist"
[ -n "$WORK" ] || WORK="$REPO/build/macos-app"
mkdir -p "$OUT" "$WORK"
OUT=$(cd "$OUT" && pwd)
WORK=$(cd "$WORK" && pwd)

# Developer ID only with all five secrets; any one missing means ad hoc
SIGNED=0
if [ "$SIGN" = 1 ] && [ -n "${APPLE_ID:-}" ] && [ -n "${APPLE_TEAM_ID:-}" ] && [ -n "${APPLE_APP_PASSWORD:-}" ] \
  && [ -n "${DEVELOPER_ID_P12:-}" ] && [ -n "${DEVELOPER_ID_P12_PASSWORD:-}" ]; then
  SIGNED=1
fi
[ "$SIGNED" = 1 ] || NOTARIZE=0

# ---- 1. the engine -----------------------------------------------------------

ENGINE="$WORK/engine"
rm -rf "$ENGINE"
mkdir -p "$ENGINE"
if [ -n "$ENGINE_DIR" ]; then
  [ -d "$ENGINE_DIR" ] || fail "--engine-dir $ENGINE_DIR is not a folder"
  ditto "$ENGINE_DIR" "$ENGINE"
else
  say "Building the engine (macos/build.sh --channel macos-app)"
  rm -rf "$WORK/engine-dist"
  bash "$MACOS/build.sh" --out "$WORK/engine-dist" --work "$WORK/engine-work" --channel macos-app
  set -- "$WORK/engine-dist"/SentrySixManager-*-macos-arm64.tar.gz
  [ $# = 1 ] && [ -f "$1" ] || fail "macos/build.sh made no single tarball"
  tar -xzf "$1" -C "$ENGINE"
fi
for f in node/bin/node ffmpeg/ffmpeg ffmpeg/ffprobe bin/sentry-fsops app/server/index.js app/BUILD_INFO.json install.sh; do
  [ -f "$ENGINE/$f" ] || fail "the engine has no $f"
done
rm -f "$ENGINE/MANIFEST.sha256"
info() { "$ENGINE/node/bin/node" -p "String(require(process.argv[1]).$1 || '')" "$ENGINE/app/BUILD_INFO.json"; }
[ "$(info channel)" = macos-app ] || fail "the engine's BUILD_INFO.json says channel $(info channel), not macos-app"
VERSION=$(info version)
SHA=$(info sha)
printf '%s' "$VERSION" | grep -Eq '^[A-Za-z0-9][A-Za-z0-9._+-]{0,39}$' || fail "no usable version in BUILD_INFO.json"
printf '%s' "$SHA" | grep -Eq '^[0-9a-f]{40}$' || fail "no commit id in BUILD_INFO.json"
SHORT=$(printf '%s' "$SHA" | cut -c1-7)

# ---- 2. signing: the keychain, then the engine's programs ----------------------

IDENTITY=-
if [ "$SIGNED" = 1 ]; then
  # the build keychain goes when this script ends, however it ends
  trap 'bash "$HERE/sign.sh" cleanup' EXIT
  IDENTITY=$(bash "$HERE/sign.sh" keychain)
  [ -n "$IDENTITY" ] || fail "no Developer ID Application identity in the certificate"
  say "Signing with Developer ID"
elif [ "$SIGN" = 1 ]; then
  say "No signing secrets: signing ad hoc (the dmg is named -unsigned)"
fi
if [ "$SIGN" = 1 ]; then
  bash "$HERE/sign.sh" engine "$ENGINE" "$IDENTITY" "$BUNDLE_SRC/node.entitlements"
fi

# ---- 3. the manifest, and its SHA-256 compiled into the app ----------------------

( cd "$ENGINE" && find . -type f ! -path ./MANIFEST.sha256 | LC_ALL=C sort | while IFS= read -r f; do shasum -a 256 "$f"; done ) > "$WORK/MANIFEST.sha256"
mv "$WORK/MANIFEST.sha256" "$ENGINE/MANIFEST.sha256"
MANIFEST_SHA=$(shasum -a 256 "$ENGINE/MANIFEST.sha256" | awk '{ print $1 }')
printf '%s' "$MANIFEST_SHA" | grep -Eq '^[0-9a-f]{64}$' || fail "could not hash MANIFEST.sha256"
cat > "$APP_SRC/Sources/SentrySixManager/Platform/EngineManifest.generated.swift" <<EOF
// Written by scripts/build-app.sh: the SHA-256 of the bundled engine's MANIFEST.sha256.
#if ENGINE_MANIFEST
let generatedEngineManifestSHA256 = "$MANIFEST_SHA"
#endif
EOF

# ---- 4. compile --------------------------------------------------------------------

say "Compiling the app"
( cd "$APP_SRC" && swift build -c release --arch arm64 --product SentrySixManager -Xswiftc -DENGINE_MANIFEST \
  && swift build -c release --arch arm64 --product sentry-six-engine )
BIN=$(cd "$APP_SRC" && swift build -c release --arch arm64 --show-bin-path)
for b in SentrySixManager sentry-six-engine; do [ -f "$BIN/$b" ] || fail "swift build made no $b"; done

# ---- 5. the bundle -------------------------------------------------------------------

APP="$WORK/$APP_NAME.app"
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources" "$APP/Contents/Library/LaunchAgents"
sed -e "s/@VERSION@/$VERSION/g" -e "s/@BUILD@/$VERSION/g" "$BUNDLE_SRC/Info.plist" > "$APP/Contents/Info.plist"
printf 'APPL????' > "$APP/Contents/PkgInfo"
cp "$BIN/SentrySixManager" "$APP/Contents/MacOS/$APP_NAME"
cp "$BIN/sentry-six-engine" "$APP/Contents/MacOS/sentry-six-engine"
chmod 0755 "$APP/Contents/MacOS/$APP_NAME" "$APP/Contents/MacOS/sentry-six-engine"
cp "$BUNDLE_SRC/$AGENT_PLIST" "$APP/Contents/Library/LaunchAgents/$AGENT_PLIST"
if [ -d "$BUNDLE_SRC/AppIcon.iconset" ]; then
  iconutil -c icns -o "$APP/Contents/Resources/AppIcon.icns" "$BUNDLE_SRC/AppIcon.iconset"
fi
ditto "$ENGINE" "$APP/Contents/Resources/engine"
plutil -lint "$APP/Contents/Info.plist" >/dev/null || fail "Info.plist does not lint"
plutil -lint "$APP/Contents/Library/LaunchAgents/$AGENT_PLIST" >/dev/null || fail "the agent plist does not lint"
for b in "$APP/Contents/MacOS/$APP_NAME" "$APP/Contents/MacOS/sentry-six-engine"; do
  [ "$(lipo -archs "$b")" = arm64 ] || fail "$b is $(lipo -archs "$b"), not arm64 only"
done
bash "$HERE/check-no-webkit.sh" --app "$APP"

# ---- 6. sign the launcher and the app, then check -----------------------------------------

if [ "$SIGN" = 1 ]; then
  bash "$HERE/sign.sh" app "$APP" "$IDENTITY" "$BUNDLE_SRC/SentrySixManager.entitlements"
fi

# ---- 7. notarize and staple the app ---------------------------------------------------

[ "$NOTARIZE" = 0 ] || bash "$HERE/notarize.sh" "$APP"

# ---- 8. the dmg (signed, notarized and stapled too when signed) -----------------------------

if [ "$DMG" = 1 ]; then
  if [ "$SIGNED" = 1 ]; then NAME="SentrySixManager-$VERSION-$SHORT-macos-arm64.dmg"; else NAME="SentrySixManager-$VERSION-$SHORT-macos-arm64-unsigned.dmg"; fi
  rm -f "$OUT/$NAME"
  bash "$HERE/dmg.sh" "$APP" "$OUT/$NAME" "$IDENTITY"
  [ "$NOTARIZE" = 0 ] || bash "$HERE/notarize.sh" "$OUT/$NAME"
  # the release's SHA256SUMS gets this line next to the tarball's
  ( cd "$OUT" && shasum -a 256 "$NAME" > SHA256SUMS.dmg )
  say "Wrote $OUT/$NAME"
else
  rm -rf "$OUT/$APP_NAME.app"
  ditto "$APP" "$OUT/$APP_NAME.app"
  say "Wrote $OUT/$APP_NAME.app"
fi
