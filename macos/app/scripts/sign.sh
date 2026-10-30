#!/bin/bash
# Code signing for build-app.sh (docs/plans/macos-app.md 10.2). Inside out,
# never --deep. Identity "-" signs ad hoc (no secrets: 10.4).
#
#   bash sign.sh keychain                 a build keychain from DEVELOPER_ID_P12; prints the identity
#   bash sign.sh engine <dir> <id> <node.entitlements>
#   bash sign.sh app <app> <id> <app.entitlements>
#   bash sign.sh cleanup                  deletes the build keychain
#
# Secrets come from the environment only and are never printed (no command tracing here).

set -euo pipefail

KEYCHAIN=sentry-six-build.keychain
fail() { printf 'sign.sh: %s\n' "$*" >&2; exit 1; }

# codesign with the hardened runtime and a timestamp; ad hoc has neither
sign() { # sign <id> <path> [entitlements]
  local id=$1 path=$2 ent=${3:-}
  if [ "$id" = - ]; then
    codesign --force --sign - ${ent:+--entitlements "$ent"} "$path"
  else
    codesign --force --timestamp --options runtime --sign "$id" ${ent:+--entitlements "$ent"} "$path"
  fi
}

cmd_keychain() {
  local tmp pass p12 id
  [ -n "${DEVELOPER_ID_P12:-}" ] && [ -n "${DEVELOPER_ID_P12_PASSWORD:-}" ] && [ -n "${APPLE_TEAM_ID:-}" ] || fail "the signing secrets are not set"
  tmp=${RUNNER_TEMP:-${TMPDIR:-/tmp}}
  p12=$(mktemp "$tmp/devid.XXXXXX")
  pass=$(openssl rand -hex 24)
  printf '%s' "$DEVELOPER_ID_P12" | base64 --decode > "$p12"
  security delete-keychain "$KEYCHAIN" >/dev/null 2>&1 || true
  security create-keychain -p "$pass" "$KEYCHAIN" >/dev/null
  security set-keychain-settings -lut 21600 "$KEYCHAIN"
  security unlock-keychain -p "$pass" "$KEYCHAIN"
  security import "$p12" -k "$KEYCHAIN" -P "$DEVELOPER_ID_P12_PASSWORD" -T /usr/bin/codesign >/dev/null
  rm -f "$p12"
  security set-key-partition-list -S apple-tool:,apple:,codesign: -s -k "$pass" "$KEYCHAIN" >/dev/null
  security list-keychains -d user -s "$KEYCHAIN" login.keychain
  # the "Developer ID Application: ... (TEAM)" line for this team
  id=$(security find-identity -v -p codesigning "$KEYCHAIN" | sed -nE 's/^ *[0-9]+\) [0-9A-F]{40} "(Developer ID Application: .*\('"$APPLE_TEAM_ID"'\))"$/\1/p' | head -n 1)
  [ -n "$id" ] || fail "the certificate has no Developer ID Application identity for team $APPLE_TEAM_ID"
  printf '%s\n' "$id"
}

# ffmpeg, ffprobe and sentry-fsops without entitlements; node with JIT only
cmd_engine() {
  local dir=$1 id=$2 ent=$3 b
  for b in ffmpeg/ffmpeg ffmpeg/ffprobe bin/sentry-fsops; do
    [ -f "$dir/$b" ] || fail "no $b in $dir"
    sign "$id" "$dir/$b"
  done
  sign "$id" "$dir/node/bin/node" "$ent"
}

# the launcher, then the app; then the checks
cmd_app() {
  local app=$1 id=$2 ent=$3 main
  main="$app/Contents/MacOS/Sentry Six Manager"
  sign "$id" "$app/Contents/MacOS/sentry-six-engine"
  sign "$id" "$app" "$ent"
  codesign --verify --deep --strict --verbose=2 "$app"
  # the entitlements each binary carries, against the expected ones
  check_entitlements "$app/Contents/Resources/engine/node/bin/node" 'com.apple.security.cs.allow-jit com.apple.security.cs.allow-unsigned-executable-memory'
  check_entitlements "$main" ''
  check_entitlements "$app/Contents/MacOS/sentry-six-engine" ''
  check_entitlements "$app/Contents/Resources/engine/ffmpeg/ffmpeg" ''
}

check_entitlements() { # check_entitlements <binary> <keys, space separated, sorted>
  local got
  got=$(codesign -d --entitlements - --xml "$1" 2>/dev/null | grep -oE '<key>[^<]+</key>' | sed -E 's#</?key>##g' | LC_ALL=C sort | tr '\n' ' ' | sed 's/ $//' || true)
  [ "$got" = "$2" ] || fail "$1 has entitlements [$got], expected [$2]"
}

cmd_cleanup() {
  security delete-keychain "$KEYCHAIN" >/dev/null 2>&1 || true
}

case "${1:-}" in
  keychain) cmd_keychain ;;
  engine) [ $# = 4 ] || fail "usage: sign.sh engine <dir> <id> <entitlements>"; cmd_engine "$2" "$3" "$4" ;;
  app) [ $# = 4 ] || fail "usage: sign.sh app <app> <id> <entitlements>"; cmd_app "$2" "$3" "$4" ;;
  cleanup) cmd_cleanup ;;
  *) fail "usage: sign.sh keychain | engine <dir> <id> <ent> | app <app> <id> <ent> | cleanup" ;;
esac
