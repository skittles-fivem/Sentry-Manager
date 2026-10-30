#!/bin/bash
# No WebKit anywhere in the Mac app (docs/plans/macos-app.md 1 and 10.5):
#
#   bash check-no-webkit.sh [--sources <dir>] [--app <Sentry Six Manager.app>]
#
#   --sources  (default macos/app/Sources) no import of WebKit or
#              JavaScriptCore, no web view, no HTML turned into text
#   --app      no .html, .css or .js file in the bundle outside the engine
#              (the server, which the app never loads), and no binary that
#              links WebKit.framework or JavaScriptCore.framework, also
#              through the libraries it links (otool -L, recursively)
#
# Linux runs the sources check too (test/macosApp.test.js).

set -euo pipefail

HERE=$(cd "$(dirname "$0")" && pwd)
SOURCES="$HERE/../Sources"
APP=""
fail() { printf 'check-no-webkit.sh: %s\n' "$*" >&2; exit 1; }

while [ $# -gt 0 ]; do
  case "$1" in
    --sources) SOURCES=$2; shift 2 ;;
    --app) APP=$2; shift 2 ;;
    *) fail "unknown option $1" ;;
  esac
done

PATTERN='import +(WebKit|JavaScriptCore)|WKWebView|WebView\(|JSContext|NSAttributedString\.DocumentType\.html|\.html\b.*documentType|NSAttributedString\(html|examples/ui-packs'
if hits=$(grep -R -n -E "$PATTERN" "$SOURCES" 2>/dev/null); then
  printf '%s\n' "$hits" >&2
  fail "the app's sources use WebKit, JavaScriptCore or HTML (above)"
fi
echo "No WebKit in $(cd "$SOURCES" && pwd)"

[ -n "$APP" ] || exit 0
[ -d "$APP/Contents" ] || fail "$APP is not an app bundle"

web=$(find "$APP" -type f \( -iname '*.html' -o -iname '*.htm' -o -iname '*.css' -o -iname '*.js' \) ! -path "$APP/Contents/Resources/engine/*" -print)
[ -z "$web" ] || { printf '%s\n' "$web" >&2; fail "web files in the bundle outside the engine (above)"; }

# every Mach-O outside the engine, and what it links, recursively
seen=" "
check_links() { # check_links <binary>
  local lib
  case "$seen" in *" $1 "*) return 0 ;; esac
  seen="$seen$1 "
  while IFS= read -r lib; do
    case "$lib" in
      */WebKit.framework/*|*/JavaScriptCore.framework/*) fail "$1 links $lib" ;;
      /System/*|/usr/lib/*) ;;
      @rpath/*|@executable_path/*|@loader_path/*)
        local base=${lib#*/}
        for cand in "$APP/Contents/Frameworks/$base" "$APP/Contents/MacOS/$base"; do [ ! -f "$cand" ] || check_links "$cand"; done ;;
      /*) [ ! -f "$lib" ] || check_links "$lib" ;;
    esac
  done < <(otool -L "$1" | tail -n +2 | awk '{ print $1 }')
}
while IFS= read -r -d '' f; do
  if file -b "$f" | grep -q 'Mach-O'; then check_links "$f"; fi
done < <(find "$APP/Contents" -type f ! -path "$APP/Contents/Resources/engine/*" -print0)
echo "No WebKit linked in $(basename "$APP")"
