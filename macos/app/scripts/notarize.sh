#!/bin/bash
# Notarizes and staples an app or a dmg (docs/plans/macos-app.md 10.3):
#
#   bash notarize.sh "<app or dmg>"
#
# APPLE_ID, APPLE_TEAM_ID and APPLE_APP_PASSWORD (an app-specific password)
# come from the environment and are never printed. On anything but
# Accepted, Apple's log goes to the job log and the build fails.

set -euo pipefail

fail() { printf 'notarize.sh: %s\n' "$*" >&2; exit 1; }

target=${1:-}
[ -e "$target" ] || fail "usage: notarize.sh <app or dmg>"
[ -n "${APPLE_ID:-}" ] && [ -n "${APPLE_TEAM_ID:-}" ] && [ -n "${APPLE_APP_PASSWORD:-}" ] || fail "the notarization secrets are not set"

work=$(mktemp -d "${RUNNER_TEMP:-${TMPDIR:-/tmp}}/notarize.XXXXXX")
trap 'rm -rf "$work"' EXIT
upload=$target
case "$target" in
  *.app) upload="$work/$(basename "$target" .app).zip"; ditto -c -k --keepParent "$target" "$upload" ;;
  *.dmg) ;;
  *) fail "$target is neither an .app nor a .dmg" ;;
esac

creds=(--apple-id "$APPLE_ID" --team-id "$APPLE_TEAM_ID" --password "$APPLE_APP_PASSWORD")
echo "Submitting $(basename "$target") for notarization"
xcrun notarytool submit "$upload" "${creds[@]}" --wait --timeout 60m --output-format json > "$work/result.json" || true
status=$(plutil -extract status raw -o - "$work/result.json" 2>/dev/null || true)
id=$(plutil -extract id raw -o - "$work/result.json" 2>/dev/null || true)
if [ "$status" != Accepted ]; then
  [ -z "$id" ] || xcrun notarytool log "$id" "${creds[@]}" || true
  fail "notarization of $(basename "$target") ended as '${status:-no answer}'"
fi
echo "Notarized ($id)"
xcrun stapler staple "$target"
xcrun stapler validate "$target"
