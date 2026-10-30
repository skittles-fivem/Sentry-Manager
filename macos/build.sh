#!/bin/bash
# Makes the macOS release tarball (Apple Silicon only), on a Mac with the
# Xcode command line tools:
#
#   bash macos/build.sh [--out DIR] [--work DIR] [--sha SHA] [--branch NAME]
#                       [--repo owner/name] [--built-at ISO-TIME]
#                       [--channel macos|macos-app]
#
# --channel macos-app is the engine inside the Mac app (macos/app/scripts/
# build-app.sh): the same files, its BUILD_INFO.json says so.
#
# Out come dist/SentrySixManager-<version>-<shortsha>-macos-arm64.tar.gz, its
# SHA256SUMS and release.json (what server/releaseUpdates.js reads). The
# tarball's root is the program folder as installed: node/ ffmpeg/ bin/ app/
# plus the install, update and uninstall scripts and the launchd plist.
#
# The only network use is the two pinned downloads in macos/versions.json,
# each checked against its SHA-256 before anything in it is used. The app
# files are the ones the Dockerfile copies into /app (macos/appFiles.js), so
# the two builds ship the same app. Nothing downloaded is modified or
# re-signed: that would break the arm64 signatures.
#
# bash 3.2 (macOS's own) is enough.

set -euo pipefail

HERE=$(cd "$(dirname "$0")" && pwd)
ROOT=$(cd "$HERE/.." && pwd)
OUT=""
WORK=""
SHA=""
BRANCH=""
REPO=""
BUILT_AT=""
CHANNEL=macos

fail() { echo "build.sh: $*" >&2; exit 1; }

while [ $# -gt 0 ]; do
  case "$1" in
    --out) OUT=$2; shift 2 ;;
    --work) WORK=$2; shift 2 ;;
    --sha) SHA=$2; shift 2 ;;
    --branch) BRANCH=$2; shift 2 ;;
    --repo) REPO=$2; shift 2 ;;
    --built-at) BUILT_AT=$2; shift 2 ;;
    --channel) CHANNEL=$2; shift 2 ;;
    *) fail "unknown option $1" ;;
  esac
done

case "$CHANNEL" in macos|macos-app) ;; *) fail "--channel is macos or macos-app" ;; esac
[ "$(uname -s)" = Darwin ] || fail "build on a Mac: the package is checked by running what it holds"
[ "$(sysctl -n hw.optional.arm64 2>/dev/null || true)" = 1 ] || fail "build on an Apple Silicon Mac (Intel Macs are not supported)"
for t in plutil shasum curl tar clang lipo codesign; do
  command -v "$t" >/dev/null 2>&1 || fail "$t is missing (xcode-select --install)"
done

[ -n "$OUT" ] || OUT="$ROOT/dist"
[ -n "$WORK" ] || WORK="$ROOT/build/macos"
mkdir -p "$OUT" "$WORK"
OUT=$(cd "$OUT" && pwd)
WORK=$(cd "$WORK" && pwd)

# ---- what is being built ---------------------------------------------------

[ -n "$SHA" ] || SHA=${GITHUB_SHA:-}
[ -n "$SHA" ] || SHA=$(git -C "$ROOT" rev-parse HEAD 2>/dev/null || true)
SHA=$(printf '%s' "$SHA" | tr 'A-F' 'a-f')
printf '%s' "$SHA" | grep -Eq '^[0-9a-f]{40}$' || fail "no commit id: pass --sha with the full 40-character id"
[ -n "$BRANCH" ] || BRANCH=${GITHUB_REF_NAME:-}
[ -n "$BRANCH" ] || BRANCH=$(git -C "$ROOT" rev-parse --abbrev-ref HEAD 2>/dev/null || true)
[ "$BRANCH" != HEAD ] || BRANCH=""
[ -n "$REPO" ] || REPO=${GITHUB_REPOSITORY:-}
if [ -z "$REPO" ]; then
  # a local build: the origin remote, as https://github.com/o/r(.git) or git@github.com:o/r(.git)
  url=$(git -C "$ROOT" remote get-url origin 2>/dev/null || true)
  REPO=$(printf '%s' "$url" | sed -nE 's#^.*github\.com[/:]([^/]+)/([^/]+)$#\1/\2#p' | sed -E 's#\.git/?$##; s#/$##')
fi
[ -n "$REPO" ] || fail "no repository: pass --repo owner/name (update notices and update.sh ask its GitHub Releases)"
[ -n "$BUILT_AT" ] || BUILT_AT=${BUILD_DATE:-}
[ -n "$BUILT_AT" ] || BUILT_AT=$(date -u +%Y-%m-%dT%H:%M:%SZ)

PINS="$HERE/versions.json"
pin() { plutil -extract "$1" raw -o - "$PINS" 2>/dev/null || fail "versions.json: '$1' is missing"; }
[ "$(pin arch)" = arm64 ] || fail "versions.json: arch must be arm64"

# ---- pinned downloads ------------------------------------------------------

unset_pins=""
for n in node ffmpeg; do
  pin "$n.sha256" | grep -Eq '^[0-9a-f]{64}$' || unset_pins="$unset_pins $n"
done
[ -z "$unset_pins" ] || fail "macos/versions.json has no SHA-256 for:$unset_pins. Download each url yourself, check it against the publisher's own checksum (nodejs.org/dist/v<version>/SHASUMS256.txt for node, the release page's digest for jellyfin-ffmpeg), then put the output of shasum -a 256 <file> in its sha256 field."

# prints the path of the verified download; reuses a cached one that still matches
fetch_pinned() {
  local name=$1 url want file got
  url=$(pin "$name.url")
  want=$(pin "$name.sha256")
  case "$url" in https://*) ;; *) fail "versions.json: $name url must be https" ;; esac
  file="$WORK/$name-$(basename "$url")"
  if [ -f "$file" ] && [ "$(shasum -a 256 "$file" | awk '{print $1}')" = "$want" ]; then
    printf '%s\n' "$file"; return
  fi
  rm -f "$file" "$file.part"
  echo "Downloading $name $(pin "$name.version")" >&2
  curl -fsSL --proto '=https' --tlsv1.2 -o "$file.part" "$url" || fail "could not download $url"
  got=$(shasum -a 256 "$file.part" | awk '{print $1}')
  if [ "$got" != "$want" ]; then
    rm -f "$file.part"
    fail "$name download does not match its pinned SHA-256 (expected $want, got $got): refusing to build with it"
  fi
  mv "$file.part" "$file"
  printf '%s\n' "$file"
}

STAGE="$WORK/stage"
rm -rf "$STAGE" "$WORK/unpack"
mkdir -p "$STAGE/node/bin" "$STAGE/ffmpeg" "$STAGE/bin" "$WORK/unpack"

# Only the named members leave an archive, each to a fixed name: nothing
# else in a download can land anywhere.
take() { # take <name> <archive> <key in .files> <destination>
  local member
  member=$(pin "$1.files.$3")
  case "$member" in /*|*..*) fail "versions.json: $1 member $member is not a plain path" ;; esac
  rm -rf "$WORK/unpack/$1"
  mkdir -p "$WORK/unpack/$1"
  tar -xJf "$2" -C "$WORK/unpack/$1" "$member" || fail "$1 archive has no $member: fix its path in versions.json"
  cp -p "$WORK/unpack/$1/$member" "$4"
}

NODE_TAR=$(fetch_pinned node)
FF_TAR=$(fetch_pinned ffmpeg)
take node "$NODE_TAR" bin/node "$STAGE/node/bin/node"
lic=$(pin node.notices.LICENSE)
tar -xJf "$NODE_TAR" -C "$WORK/unpack/node" "$lic" && cp "$WORK/unpack/node/$lic" "$STAGE/node/LICENSE"
take ffmpeg "$FF_TAR" ffmpeg "$STAGE/ffmpeg/ffmpeg"
take ffmpeg "$FF_TAR" ffprobe "$STAGE/ffmpeg/ffprobe"
rm -rf "$WORK/unpack"

NODE="$STAGE/node/bin/node"
FFMPEG="$STAGE/ffmpeg/ffmpeg"

# what was pinned is what runs, and only for Apple Silicon
[ "$("$NODE" --version)" = "v$(pin node.version)" ] || fail "node says $("$NODE" --version), versions.json pins $(pin node.version)"
for b in "$NODE" "$FFMPEG" "$STAGE/ffmpeg/ffprobe"; do
  [ "$(lipo -archs "$b")" = arm64 ] || fail "$b is $(lipo -archs "$b"), not arm64 only"
done
codesign --verify "$NODE" || fail "node's signature does not verify"
encoders=$("$FFMPEG" -hide_banner -encoders 2>/dev/null)
decoders=$("$FFMPEG" -hide_banner -decoders 2>/dev/null)
for e in $(plutil -extract ffmpeg.requireEncoders json -o - "$PINS" | tr -d '[]",'); do
  printf '%s\n' "$encoders" | grep -Eq "^ *[A-Z.]{6} +$e " || fail "the pinned ffmpeg has no encoder $e: pick a build that has it"
done
for d in $(plutil -extract ffmpeg.requireDecoders json -o - "$PINS" | tr -d '[]",'); do
  printf '%s\n' "$decoders" | grep -Eq "^ *[A-Z.]{6} +$d " || fail "the pinned ffmpeg has no decoder $d: pick a build that has it"
done

# ---- the native helper -----------------------------------------------------

FSOPS_SRC="$ROOT/native/darwin/sentry-fsops.c"
[ -f "$FSOPS_SRC" ] || fail "native/darwin/sentry-fsops.c is missing: it has to be written before a package can be built"
clang -O2 -Wall -Wextra -Werror -arch arm64 -mmacosx-version-min=13.5 -o "$STAGE/bin/sentry-fsops" "$FSOPS_SRC" \
  || fail "clang could not compile native/darwin/sentry-fsops.c"
# the linker signs ad hoc on arm64 already; this makes sure
codesign --force --sign - "$STAGE/bin/sentry-fsops"

# ---- the app files: the Dockerfile's own list ------------------------------

mkdir -p "$STAGE/app"
"$NODE" "$HERE/appFiles.js" copy "$ROOT" "$STAGE/app"
"$NODE" "$HERE/buildInfo.js" "$ROOT" "$STAGE/app" "$OUT" "$SHA" "$BRANCH" "$BUILT_AT" "$REPO" "$CHANNEL"
VERSION=$("$NODE" -p "require('$STAGE/app/BUILD_INFO.json').version")

# ---- scripts, plist and readme ---------------------------------------------

cp "$HERE/sentry-six-manager" "$STAGE/bin/sentry-six-manager"
for f in install.sh update.sh uninstall.sh io.github.skittles-fivem.sentry-six-manager.plist newsyslog.conf manager.env.example; do
  cp "$HERE/$f" "$STAGE/$f"
done
chmod 0755 "$STAGE/bin/sentry-six-manager" "$STAGE/install.sh" "$STAGE/update.sh" "$STAGE/uninstall.sh"

SHORT=$(printf '%s' "$SHA" | cut -c1-7)
cat > "$STAGE/README-macOS.txt" <<EOF
Sentry Six Manager $VERSION for macOS (Apple Silicon)
Built $BUILT_AT from $REPO at $SHORT.

Install or upgrade (macOS 13.5 or newer, Apple Silicon only)
  sudo bash install.sh --from <this tarball>
  It installs to /Library/SentrySixManager, keeps settings and data in
  /Library/Application Support/SentrySixManager, starts the service as the
  hidden account _sentrysix and prints the address, the setup code and the
  certificate fingerprint. Running it again upgrades in place and keeps your
  data.

Settings
  /Library/Application Support/SentrySixManager/manager.env takes the same
  keys as the Docker .env file (PORT, TLS_MODE, FOLDER_ROOTS, ...). Then:
  sudo sentry-six-manager restart

Every day
  sentry-six-manager status        is it running, and where
  sudo sentry-six-manager logs     the app's log
  sudo sentry-six-manager allow-folder /Volumes/TESLADRIVE
                                   let the service read a drive or folder
  sudo sentry-six-manager update   the latest release, checked by SHA-256;
                                   back to the old one if it does not start
  sudo sentry-six-manager uninstall [--remove-data]

Included: Node.js $(pin node.version) (node/, with its licence) and
jellyfin-ffmpeg $(pin ffmpeg.version) (ffmpeg/, GPL; source at
https://github.com/jellyfin/jellyfin-ffmpeg).
EOF

# ---- tarball, SHA256SUMS -----------------------------------------------------

TARBALL="SentrySixManager-$VERSION-$SHORT-macos-arm64.tar.gz"
rm -f "$OUT/$TARBALL"
# no ._ AppleDouble files; owners are set by install.sh, not by the archive
( cd "$STAGE" && COPYFILE_DISABLE=1 tar --uid 0 --gid 0 --uname root --gname wheel -czf "$OUT/$TARBALL" \
    README-macOS.txt app bin ffmpeg node install.sh update.sh uninstall.sh \
    io.github.skittles-fivem.sentry-six-manager.plist newsyslog.conf manager.env.example )
# shasum's format: "<hash>  <name>", so shasum -a 256 -c works on it
( cd "$OUT" && shasum -a 256 "$TARBALL" release.json > SHA256SUMS )
rm -rf "$STAGE"
echo "Wrote $OUT/$TARBALL, SHA256SUMS and release.json"
