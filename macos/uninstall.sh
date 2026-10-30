#!/bin/bash
# Removes Sentry Six Manager from this Mac:
#
#   sudo sentry-six-manager uninstall [--remove-data]
#   sudo bash /Library/SentrySixManager/uninstall.sh [--remove-data]
#
# It stops the service and removes the program, the launchd job, the log
# rotation entry and the PATH entry. Settings, data, footage folders, the log
# and the _sentrysix account stay, so installing again picks up where it left
# off. --remove-data also removes those.
#
# SENTRY_ROOT puts every path under another folder (the tests use it; sudo
# drops it, so a real uninstall always uses /).

set -euo pipefail

LABEL=io.github.skittles-fivem.sentry-six-manager
SVC=_sentrysix
WAIT=${SENTRY_WAIT_SECONDS:-60}

ROOT=${SENTRY_ROOT:-}
ROOT=${ROOT%/}
PROG="$ROOT/Library/SentrySixManager"
STATE="$ROOT/Library/Application Support/SentrySixManager"
LOGDIR="$ROOT/Library/Logs/SentrySixManager"
PLIST="$ROOT/Library/LaunchDaemons/$LABEL.plist"
NEWSYSLOG="$ROOT/etc/newsyslog.d/sentry-six-manager.conf"
PATHS_D="$ROOT/etc/paths.d/sentry-six-manager"
PUBLIC="$ROOT/Library/Application Support/SentrySixManager Public"
LOCAL="$ROOT/Library/Application Support/SentrySixManager Local"

say() { printf '%s\n' "$*"; }
fail() { printf 'uninstall.sh: %s\n' "$*" >&2; exit 1; }

running() { launchctl print "system/$LABEL" 2>/dev/null | grep -Eq '^[[:space:]]*state = running'; }

main() {
  local remove_data=0 i=0
  while [ $# -gt 0 ]; do
    case "$1" in
      --remove-data) remove_data=1; shift ;;
      -h|--help) say "usage: sudo bash uninstall.sh [--remove-data]"; exit 0 ;;
      *) fail "unknown option $1" ;;
    esac
  done
  [ "$(id -u)" = 0 ] || fail "run it as root: sudo sentry-six-manager uninstall"

  if launchctl print "system/$LABEL" >/dev/null 2>&1; then
    say "Stopping the service"
    if running; then
      launchctl kill TERM "system/$LABEL" || true
      while running && [ "$i" -lt $((WAIT + 10)) ]; do sleep 1; i=$((i + 1)); done
    fi
    launchctl bootout "system/$LABEL" 2>/dev/null || true
  fi
  rm -f "$PLIST" "$NEWSYSLOG" "$PATHS_D"
  # the Mac app's two folders: engine.json and the sign-in socket
  rm -rf "$PUBLIC" "$LOCAL"
  rm -rf "$PROG" "$PROG.new" "$PROG.old" "$PROG.failed"
  say "Removed the program from /Library/SentrySixManager"

  if [ "$remove_data" = 1 ]; then
    # nothing may still be mounted under footage/: rm would go into the share
    if [ -d "$STATE/footage" ] && mount | grep -F " on $STATE/footage/" >/dev/null; then
      fail "a share is still mounted under $STATE/footage: unmount it first (or restart the Mac), then run this again"
    fi
    rm -rf "$STATE" "$LOGDIR"
    if dscl . -read "/Users/$SVC" >/dev/null 2>&1; then dscl . -delete "/Users/$SVC"; fi
    if dscl . -read "/Groups/$SVC" >/dev/null 2>&1; then dscl . -delete "/Groups/$SVC"; fi
    say "Removed the settings, the data, the log and the $SVC account"
  else
    say "Kept the settings and data in $STATE,"
    say "the log in $LOGDIR and the $SVC account."
    say "To remove them too: sudo bash uninstall.sh --remove-data"
  fi
}

main "$@"
