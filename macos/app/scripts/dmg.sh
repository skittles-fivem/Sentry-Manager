#!/bin/bash
# The drag-to-Applications disk image (docs/plans/macos-app.md 10.1 step 7):
# the app and a link to /Applications, nothing else, so CI needs no Finder
# scripting. Signed when an identity other than "-" is given.
#
#   bash dmg.sh "<app>" <out.dmg> <identity or ->

set -euo pipefail

fail() { printf 'dmg.sh: %s\n' "$*" >&2; exit 1; }

app=${1:-}
out=${2:-}
id=${3:--}
[ -d "$app" ] && [ -n "$out" ] || fail "usage: dmg.sh <app> <out.dmg> [identity]"

stage=$(mktemp -d "${RUNNER_TEMP:-${TMPDIR:-/tmp}}/dmg.XXXXXX")
trap 'rm -rf "$stage"' EXIT
ditto "$app" "$stage/$(basename "$app")"
ln -s /Applications "$stage/Applications"
rm -f "$out"
hdiutil create -volname "Sentry Six Manager" -srcfolder "$stage" -format UDZO -fs APFS -ov "$out" >/dev/null
[ "$id" = - ] || codesign --force --timestamp --sign "$id" "$out"
hdiutil verify "$out" >/dev/null
echo "Made $(basename "$out")"
