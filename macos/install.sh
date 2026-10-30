#!/bin/bash
# Installs or upgrades Sentry Six Manager on a Mac with Apple Silicon.
#
#   curl -fsSL https://raw.githubusercontent.com/skittles-fivem/Sentry-Manager/main/macos/install.sh | sudo bash
#   sudo bash install.sh --from SentrySixManager-<ver>-<sha>-macos-arm64.tar.gz [--sums SHA256SUMS]
#   sudo bash install.sh --release macos-v<ver>-<sha>
#
# The Mac app runs it once through the administrator prompt, on a copy of
# its own engine it has already staged and checked (docs/plans/macos-app.md 3.4):
#
#   bash install.sh --from-dir <staged dir> --service-mode daemon
#                   [--local-user <name>] [--import-data <data dir>] [--update]
#
# It downloads the newest macos-v* release over HTTPS (or takes --from),
# checks its SHA-256 before anything in it is used, installs it to
# /Library/SentrySixManager, keeps settings and data in
# /Library/Application Support/SentrySixManager, runs the service as the
# hidden account _sentrysix and prints the address, the setup code and the
# certificate fingerprint. Running it again upgrades in place; if the new
# version does not answer within a minute, the old one is put back.
#
# update.sh is this script with --update. bash 3.2 (macOS's own) is enough.
#
# SENTRY_ROOT puts every path under another folder (the tests use it; sudo
# drops it, so a real install always uses /).

set -euo pipefail

LABEL=io.github.skittles-fivem.sentry-six-manager
SVC=_sentrysix
REPO=${SENTRY_RELEASE_REPO:-skittles-fivem/Sentry-Manager}
WAIT=${SENTRY_WAIT_SECONDS:-60}
# how long to wait for the service's own VideoToolbox answer
VT_WAIT=${SENTRY_VT_WAIT_SECONDS:-60}

ROOT=${SENTRY_ROOT:-}
ROOT=${ROOT%/}
PROG="$ROOT/Library/SentrySixManager"
STATE="$ROOT/Library/Application Support/SentrySixManager"
LOGDIR="$ROOT/Library/Logs/SentrySixManager"
PLIST="$ROOT/Library/LaunchDaemons/$LABEL.plist"
NEWSYSLOG="$ROOT/etc/newsyslog.d/sentry-six-manager.conf"
PATHS_D="$ROOT/etc/paths.d/sentry-six-manager"
# engine.json for the Mac app (public: certificates, port, version)
PUBLIC="$ROOT/Library/Application Support/SentrySixManager Public"
# the same-Mac sign-in socket's folder: only the allowed Mac users get in
LOCAL="$ROOT/Library/Application Support/SentrySixManager Local"
LOCAL_USERS="$STATE/local-signin-users"
FIREWALL=/usr/libexec/ApplicationFirewall/socketfilterfw

FROM=""
SUMS=""
TAG=""
FROM_DIR=""
SERVICE_MODE=""
LOCAL_USER=""
LOCAL_USER_SET=0
IMPORT_DATA=""
UPDATE=0
WORK=""
# set once the new version is in place and not yet known to answer
SWAPPED=0

say() { printf '%s\n' "$*"; }
fail() { printf 'install.sh: %s\n' "$*" >&2; exit 1; }

usage() {
  say "usage: sudo bash install.sh [--from <tarball> [--sums <SHA256SUMS>]] [--release <tag>] [--update]"
  say "       (the Mac app) bash install.sh --from-dir <dir> --service-mode daemon [--local-user <name>] [--import-data <dir>] [--update]"
}

# ---- checks ------------------------------------------------------------------

check_root() {
  [ "$(id -u)" = 0 ] || fail "run it as root: sudo bash install.sh"
}

# Node 24 needs macOS 13.5 or newer (its BUILDING.md).
check_macos() {
  local v maj rest min
  v=$(sw_vers -productVersion 2>/dev/null || true)
  maj=${v%%.*}
  rest=${v#*.}
  [ "$rest" != "$v" ] || rest=0
  min=${rest%%.*}
  case "$maj" in ''|*[!0-9]*) fail "this is not macOS (sw_vers says '$v')" ;; esac
  case "$min" in ''|*[!0-9]*) min=0 ;; esac
  if [ "$maj" -lt 13 ] || { [ "$maj" -eq 13 ] && [ "$min" -lt 5 ]; }; then
    fail "Sentry Six Manager needs macOS 13.5 or newer; this Mac has $v"
  fi
}

# hw.optional.arm64 is 1 on Apple Silicon, even from a Rosetta shell. Intel
# Macs usually do not have the key at all.
check_apple_silicon() {
  local arm
  arm=$(sysctl -n hw.optional.arm64 2>/dev/null || true)
  [ "$arm" = 1 ] || fail "this Mac has an Intel processor. Sentry Six Manager for macOS runs on Apple Silicon (M1 and later) only; on an Intel Mac, use the Docker version on another computer."
}

# Under Rosetta (Terminal set to Open using Rosetta) sysctl.proc_translated is 1.
check_not_rosetta() {
  [ "$(sysctl -n sysctl.proc_translated 2>/dev/null || true)" != 1 ] || fail "this installer is running under Rosetta. Open Terminal without Rosetta (Get Info, then untick Open using Rosetta), or run it with arch -arm64."
}

# ---- download and verify -------------------------------------------------------

cleanup() {
  [ -z "$WORK" ] || rm -rf "$WORK"
}

on_exit() {
  local rc=$?
  if [ "$SWAPPED" = 1 ]; then
    SWAPPED=0
    say "The installation stopped half way: going back to the version before."
    roll_back || true
    rc=1
  fi
  # a package that was refused leaves nothing behind
  [ "$rc" = 0 ] || rm -rf "$PROG.new"
  cleanup
  exit "$rc"
}

https_get() { # https_get <url> <file>
  curl -fsSL --proto '=https' --proto-redir '=https' --tlsv1.2 --retry 2 -o "$2" "$1"
}

# The newest final release whose tag starts with macos-v (or --release), as
# the tarball's URL. Releases come newest first, 100 to a page; Windows
# publishes its own in between, so a few pages are read before giving up.
find_release() {
  local api repo_rx tag_rx url page n
  printf '%s' "$REPO" | grep -Eq '^[A-Za-z0-9-]{1,39}/[A-Za-z0-9._-]{1,100}$' || fail "SENTRY_RELEASE_REPO is not owner/name: $REPO"
  repo_rx=$(printf '%s' "$REPO" | sed 's/[.]/\\./g')
  tag_rx='macos-v[^/]+'
  if [ -n "$TAG" ]; then
    printf '%s' "$TAG" | grep -Eq '^macos-v[A-Za-z0-9._+-]+$' || fail "--release takes a macos-v... tag"
    tag_rx=$(printf '%s' "$TAG" | sed 's/[.+]/\\&/g')
  fi
  url=""
  for page in 1 2 3 4 5; do
    api="https://api.github.com/repos/$REPO/releases?per_page=100&page=$page"
    https_get "$api" "$WORK/releases.json" || fail "could not ask GitHub for the releases ($api)"
    url=$(release_in_page "$WORK/releases.json" "$repo_rx" "$tag_rx")
    [ -z "$url" ] || break
    # a page that is not full is the last one
    n=$(grep -oE '"tag_name": *"' "$WORK/releases.json" | wc -l | tr -d ' ' || true)
    [ "$n" -ge 100 ] || break
  done
  [ -n "$url" ] || fail "found no macOS release in $REPO${TAG:+ called $TAG}"
  printf '%s\n' "$url"
}

# release_in_page <releases.json> <repo regex> <tag regex>: the tarball URL
# of the first matching release on that page, or nothing.
release_in_page() {
  local u t finals
  # the tags of final releases (each release has its tag_name before its
  # prerelease flag): a pre-release is installed only when named with --release
  finals=" $(grep -oE '"(tag_name|prerelease)": *("[^"]*"|true|false)' "$1" \
    | awk -F'"' '$2 == "tag_name" { t = $4; next } $2 == "prerelease" { if ($3 ~ /false/ && t != "") print t; t = "" }' \
    | tr '\n' ' ') "
  for u in $(grep -oE '"browser_download_url": *"[^"]+"' "$1" \
    | sed -E 's/^.*"(https:[^"]+)"$/\1/' \
    | grep -E "^https://github\\.com/$2/releases/download/$3/SentrySixManager-[A-Za-z0-9._+-]+-macos-arm64\\.tar\\.gz\$" || true); do
    t=$(basename "$(dirname "$u")")
    if [ -n "$TAG" ]; then printf '%s\n' "$u"; return; fi
    case "$finals" in *" $t "*) printf '%s\n' "$u"; return ;; esac
  done
}

# Puts the tarball and SHA256SUMS into $WORK and prints the tarball's name.
fetch() {
  local url name
  if [ -n "$FROM" ]; then
    [ -f "$FROM" ] || fail "$FROM does not exist"
    [ -n "$SUMS" ] || SUMS="$(dirname "$FROM")/SHA256SUMS"
    [ -f "$SUMS" ] || fail "no SHA256SUMS next to $FROM (pass --sums <file>): nothing is installed unchecked"
    name=$(basename "$FROM")
    cp "$FROM" "$WORK/$name"
    cp "$SUMS" "$WORK/SHA256SUMS"
  else
    url=$(find_release)
    name=$(basename "$url")
    say "Downloading $name" >&2
    https_get "$url" "$WORK/$name" || fail "could not download $url"
    https_get "$(dirname "$url")/SHA256SUMS" "$WORK/SHA256SUMS" || fail "could not download the release's SHA256SUMS"
  fi
  printf '%s\n' "$name"
}

verify() { # verify <tarball name>
  local name=$1
  awk -v n="$name" '$2 == n || $2 == "*" n' "$WORK/SHA256SUMS" > "$WORK/one.sha256"
  [ "$(wc -l < "$WORK/one.sha256" | tr -d ' ')" = 1 ] || fail "SHA256SUMS has no single line for $name: nothing was changed"
  grep -Eq '^[0-9a-f]{64} ' "$WORK/one.sha256" || fail "SHA256SUMS line for $name is not a SHA-256: nothing was changed"
  ( cd "$WORK" && shasum -a 256 -c one.sha256 >/dev/null ) || fail "$name does not match its SHA-256: it was damaged or changed on the way. Nothing was changed."
  say "Checked $name against its SHA-256"
  # Only now that it is known to be ours: a browser download is quarantined
  # (curl adds no quarantine, so usually there is nothing to remove).
  xattr -dr com.apple.quarantine "$WORK/$name" 2>/dev/null || true
}

# Unpacks into $PROG.new and checks that it is a whole package.
extract() { # extract <tarball name>
  local tarball="$WORK/$1" f
  tar -tzf "$tarball" > "$WORK/list" || fail "$1 is not a readable tarball"
  if grep -Eq '(^/|(^|/)\.\.(/|$))' "$WORK/list"; then fail "$1 has paths outside its folder: refusing it"; fi
  tar -tvzf "$tarball" > "$WORK/listing" || fail "$1 is not a readable tarball"
  if grep -Eq '^[lh]' "$WORK/listing"; then fail "$1 has links in it: refusing it"; fi
  rm -rf "$PROG.new"
  mkdir -p "$PROG.new"
  chmod 0755 "$PROG.new"
  tar -xzf "$tarball" -C "$PROG.new" || fail "could not unpack $1"
  check_new
}

# The Mac app's staged copy of its engine (root-owned, its MANIFEST already
# checked by the app's admin step) into $PROG.new.
copy_from_dir() {
  local d=$FROM_DIR
  [ -d "$d" ] && [ ! -L "$d" ] || fail "--from-dir $d is not a folder"
  if [ -n "$(find "$d" -type l -print 2>/dev/null | head -n 1)" ]; then fail "$d has links in it: refusing it"; fi
  rm -rf "$PROG.new"
  mkdir -p "$PROG.new"
  chmod 0755 "$PROG.new"
  cp -R "$d/." "$PROG.new/" || fail "could not copy $d"
  # the MANIFEST checked the bytes, not the metadata the user's copy carried:
  # no access list may let anyone write here once root owns it
  chmod -R -N "$PROG.new" 2>/dev/null || true
  if ls -leR "$PROG.new" 2>/dev/null | grep -Eq '^ *[0-9]+: '; then fail "$d has access lists that cannot be removed: refusing it"; fi
  xattr -dr com.apple.quarantine "$PROG.new" 2>/dev/null || true
  say "Copied the app's engine from $d"
  check_new
}

# A whole package, nothing in it writable by the service account, signatures that verify.
check_new() {
  local f
  for f in node/bin/node ffmpeg/ffmpeg ffmpeg/ffprobe bin/sentry-fsops bin/sentry-six-manager \
    app/server/index.js app/BUILD_INFO.json install.sh update.sh uninstall.sh "$LABEL.plist" newsyslog.conf manager.env.example; do
    [ -f "$PROG.new/$f" ] || fail "the package has no $f: refusing it"
  done
  if grep -q 'DYLD_' "$PROG.new/$LABEL.plist"; then fail "the package's launchd job sets DYLD_ variables: refusing it"; fi
  # nothing in the program folder is writable by the service account
  chown -R root:wheel "$PROG.new"
  # and nothing runs as root by its set-id bit
  chmod -R go-w,ug-s "$PROG.new"
  # node's Developer ID signature and our helper's own; ffmpeg's is left as
  # Jellyfin made it (an ad hoc signature cannot be checked against anyone)
  codesign --verify "$PROG.new/node/bin/node" || fail "node's code signature does not verify: refusing the package"
  codesign --verify "$PROG.new/bin/sentry-fsops" || fail "sentry-fsops's code signature does not verify: refusing the package"
}

# ---- the account -----------------------------------------------------------------

read_id() { # read_id <Users|Groups> <attribute>
  dscl . -read "/$1/$SVC" "$2" 2>/dev/null | awk -v k="$2:" '$1 == k { print $2 }' || true
}

# the ids in use, read once: " 0 1 70 ... "
USER_IDS=""
GROUP_IDS=""
taken() { # taken <Users|Groups> <id>
  case "$1" in
    Users) case "$USER_IDS" in *" $2 "*) return 0 ;; esac ;;
    Groups) case "$GROUP_IDS" in *" $2 "*) return 0 ;; esac ;;
  esac
  return 1
}

# The hidden account _sentrysix with one id N in 200-399 for both the user
# and its group. N is kept in <state>/.uid, so a re-created account gets the
# same id and keeps owning its data.
ensure_account() {
  local uid gid want n
  uid=$(read_id Users UniqueID)
  gid=$(read_id Groups PrimaryGroupID)
  USER_IDS=" $(dscl . -list /Users UniqueID | awk '{ print $NF }' | tr '\n' ' ') "
  GROUP_IDS=" $(dscl . -list /Groups PrimaryGroupID | awk '{ print $NF }' | tr '\n' ' ') "
  want=""
  if [ -f "$STATE/.uid" ]; then
    want=$(tr -cd '0-9' < "$STATE/.uid")
    if [ -z "$want" ] || [ "$want" -lt 200 ] || [ "$want" -gt 399 ]; then want=""; fi
  fi
  if [ -n "$uid" ]; then
    n=$uid
  elif [ -n "$gid" ]; then
    n=$gid
  elif [ -n "$want" ] && ! taken Users "$want" && ! taken Groups "$want"; then
    n=$want
  else
    n=""
    for i in $(seq 200 399); do
      if ! taken Users "$i" && ! taken Groups "$i"; then n=$i; break; fi
    done
    [ -n "$n" ] || fail "no free id in 200-399 for the $SVC account"
  fi
  if [ -z "$gid" ]; then
    taken Groups "$n" && fail "group id $n is taken: cannot make the $SVC group"
    dscl . -create "/Groups/$SVC"
    dscl . -create "/Groups/$SVC" PrimaryGroupID "$n"
    dscl . -create "/Groups/$SVC" RealName "Sentry Six Manager"
    dscl . -create "/Groups/$SVC" Password '*'
    gid=$n
  fi
  if [ -z "$uid" ]; then
    taken Users "$n" && fail "user id $n is taken: cannot make the $SVC account"
    dscl . -create "/Users/$SVC"
    dscl . -create "/Users/$SVC" UniqueID "$n"
    dscl . -create "/Users/$SVC" PrimaryGroupID "$gid"
    dscl . -create "/Users/$SVC" UserShell /usr/bin/false
    dscl . -create "/Users/$SVC" NFSHomeDirectory /var/empty
    dscl . -create "/Users/$SVC" RealName "Sentry Six Manager"
    dscl . -create "/Users/$SVC" Password '*'
    dscl . -create "/Users/$SVC" IsHidden 1
    uid=$n
    say "Made the hidden account $SVC ($uid)"
  fi
  SVC_UID=$uid
  SVC_GID=$gid
  # data from an account with another id: give it to this one
  if [ -n "$want" ] && [ "$want" != "$uid" ] && [ -d "$STATE/data" ]; then
    say "The $SVC account's id changed ($want to $uid): handing the data to it"
    # not footage/: shares may be mounted there
    chown -R "$uid:$gid" "$STATE/data" "$STATE/run"
  fi
}

# ---- state, settings, logs ---------------------------------------------------------

dir_as() { # dir_as <owner> <mode> <dir>
  mkdir -p "$3"
  chown "$1" "$3"
  chmod "$2" "$3"
}

ensure_state() {
  local u="$SVC_UID:$SVC_GID"
  dir_as "root:$SVC_GID" 0750 "$STATE"
  printf '%s\n' "$SVC_UID" > "$STATE/.uid"
  chown root:wheel "$STATE/.uid"
  chmod 0644 "$STATE/.uid"
  dir_as "$u" 0700 "$STATE/data"
  # Spotlight leaves it alone
  [ -e "$STATE/data/.metadata_never_index" ] || { : > "$STATE/data/.metadata_never_index"; chown "$u" "$STATE/data/.metadata_never_index"; }
  dir_as "$u" 0700 "$STATE/run"
  dir_as "$u" 0700 "$STATE/run/home"
  dir_as "$u" 0700 "$STATE/run/tmp"
  # SMB mount points must belong to the account that mounts
  dir_as "$u" 0755 "$STATE/footage"
  # the settings: written once, never overwritten (they may hold passwords)
  if [ ! -e "$STATE/manager.env" ]; then
    cp "$PROG.new/manager.env.example" "$STATE/manager.env"
    say "Wrote the settings file $STATE/manager.env"
  fi
  chown "root:$SVC_GID" "$STATE/manager.env"
  chmod 0640 "$STATE/manager.env"
  dir_as root:admin 0755 "$LOGDIR"
  [ -e "$LOGDIR/service.log" ] || : > "$LOGDIR/service.log"
  chown "$SVC_UID:admin" "$LOGDIR/service.log"
  chmod 0640 "$LOGDIR/service.log"
  mkdir -p "$(dirname "$NEWSYSLOG")" "$(dirname "$PATHS_D")"
  cp "$PROG.new/newsyslog.conf" "$NEWSYSLOG"
  chown root:wheel "$NEWSYSLOG"
  chmod 0644 "$NEWSYSLOG"
  # sentry-six-manager on every login shell's PATH (path_helper reads it)
  printf '%s\n' /Library/SentrySixManager/bin > "$PATHS_D"
  chown root:wheel "$PATHS_D"
  chmod 0644 "$PATHS_D"
  # beside the state root, not in it: nothing private becomes readable
  dir_as "$SVC_UID:wheel" 0755 "$PUBLIC"
  # a new folder has no access entries yet: the list starts empty with it
  if [ ! -d "$LOCAL" ]; then rm -f "$LOCAL_USERS"; fi
  dir_as "$SVC_UID:wheel" 0700 "$LOCAL"
  [ -e "$LOCAL_USERS" ] || : > "$LOCAL_USERS"
  chown root:wheel "$LOCAL_USERS"
  chmod 0644 "$LOCAL_USERS"
  trusted_hosts_loopback
}

# The Mac app connects to 127.0.0.1: a TRUSTED_HOSTS list in manager.env
# without it would refuse the app, so it is added (once, at the end).
trusted_hosts_loopback() {
  local th
  th=$(setting TRUSTED_HOSTS)
  [ -n "$th" ] || return 0
  case ",$(printf '%s' "$th" | tr ' ' ','),"  in *,127.0.0.1,*) return 0 ;; esac
  printf '\n# added by install.sh: the Sentry Six Manager app on this Mac connects to 127.0.0.1\nTRUSTED_HOSTS=%s,127.0.0.1\n' "$th" >> "$STATE/manager.env"
  say "Added 127.0.0.1 to TRUSTED_HOSTS in $STATE/manager.env (the app on this Mac connects there)"
}

# ---- same-Mac sign-in: who may enter the socket's folder ------------------------

# A real Mac user (uid 501 or above) by short name, or nothing.
mac_user_ok() { # mac_user_ok <name>
  local uid
  printf '%s' "$1" | grep -Eq '^[A-Za-z0-9_][A-Za-z0-9_.-]{0,254}$' || return 1
  uid=$(dscl . -read "/Users/$1" UniqueID 2>/dev/null | awk '$1 == "UniqueID:" { print $2 }' || true)
  case "$uid" in ''|*[!0-9]*) return 1 ;; esac
  [ "$uid" -ge 501 ]
}

# One ACL entry on the folder (macOS stores it by the user's GUID) and a
# line in the list the CLI shows. Added once; the others stay.
allow_local_user() { # allow_local_user <name>
  mac_user_ok "$1" || fail "$1 is not a user on this Mac"
  grep -qxF "$1" "$LOCAL_USERS" 2>/dev/null && return 0
  chmod +a "user:$1 allow search" "$LOCAL"
  printf '%s\n' "$1" >> "$LOCAL_USERS"
  say "$1 can sign in to the app on this Mac without a password"
}

# --local-user, or for a Terminal install the one who ran sudo; nobody else
# (the password still works)
local_signin_users() {
  local who=""
  if [ "$LOCAL_USER_SET" = 1 ]; then
    who=$LOCAL_USER
  elif [ -n "${SUDO_USER:-}" ] && mac_user_ok "$SUDO_USER"; then
    who=$SUDO_USER
  fi
  [ -z "$who" ] || allow_local_user "$who"
}

# ---- data from the app's "in your login" mode ---------------------------------

# Copied in only when the service has no data of its own yet; never overwritten.
import_data() {
  local src=$IMPORT_DATA n
  [ -n "$src" ] || return 0
  [ -d "$src" ] && [ ! -L "$src" ] || fail "--import-data $src is not a folder"
  n=$({ ls -A "$STATE/data" | grep -vx '.metadata_never_index' || true; } | wc -l | tr -d ' ')
  if [ "$n" != 0 ]; then
    say "The service already has its own data in $STATE/data: kept it; $src was not copied."
    return 0
  fi
  ditto "$src" "$STATE/data" || fail "could not copy $src"
  # nothing from a user's folder may point elsewhere once the service owns it
  find "$STATE/data" -type l -exec rm -f {} + 2>/dev/null || true
  chown -R "$SVC_UID:$SVC_GID" "$STATE/data"
  chmod -R go-rwx "$STATE/data"
  say "Copied the data from $src (accounts, vehicles, policies, the Recycle Bin index and the certificates)"
}

# ---- the service -------------------------------------------------------------------

loaded() { launchctl print "system/$LABEL" >/dev/null 2>&1; }
running() { launchctl print "system/$LABEL" 2>/dev/null | grep -Eq '^[[:space:]]*state = running'; }

# A clean stop (exit 0, so launchd does not start it again), then unload.
stop_service() {
  local i=0
  loaded || return 0
  if running; then
    say "Stopping the service"
    launchctl kill TERM "system/$LABEL" || true
    # launchd sends SIGKILL itself after ExitTimeOut (60 s)
    while running && [ "$i" -lt $((WAIT + 10)) ]; do sleep 1; i=$((i + 1)); done
  fi
  launchctl bootout "system/$LABEL" 2>/dev/null || true
}

# enable first: a job left disabled (launchctl disable, or switched off in
# Login Items) cannot be bootstrapped
start_service() {
  # the service writes its own VideoToolbox answer here once it is up
  rm -f "$STATE/run/videotoolbox"
  mkdir -p "$(dirname "$PLIST")"
  cp "$PROG/$LABEL.plist" "$PLIST"
  chown root:wheel "$PLIST"
  chmod 0644 "$PLIST"
  launchctl enable "system/$LABEL"
  launchctl bootstrap system "$PLIST"
}

# A setting from manager.env (the last line for it wins), without quotes.
setting() { # setting <KEY>
  awk -F= -v k="$1" '$0 ~ "^[[:space:]]*" k "[[:space:]]*=" { v = $2 } END { print v }' "$STATE/manager.env" 2>/dev/null | tr -d ' "'\''\r' || true
}

port() {
  local p
  p=$(setting PORT)
  case "$p" in ''|*[!0-9]*) p=9000 ;; esac
  printf '%s\n' "$p"
}

# https, or http with TLS_MODE=off (behind a reverse proxy)
scheme() {
  case "$(setting TLS_MODE | tr 'A-Z' 'a-z')" in off) printf 'http\n' ;; *) printf 'https\n' ;; esac
}

# /healthz answers on plain HTTP too
healthy() {
  local i=0 p
  p=$(port)
  while [ "$i" -lt "$WAIT" ]; do
    if [ "$(curl -fsS --max-time 3 "http://127.0.0.1:$p/healthz" 2>/dev/null || true)" = ok ]; then return 0; fi
    sleep 1
    i=$((i + 1))
  done
  return 1
}

roll_back() {
  [ -d "$PROG.old" ] || return 1
  stop_service
  rm -rf "$PROG.failed"
  mv "$PROG" "$PROG.failed"
  mv "$PROG.old" "$PROG"
  start_service
  rm -rf "$PROG.failed"
  if healthy; then
    say "The version before runs again."
  else
    say "The version before does not answer either: sudo sentry-six-manager logs"
  fi
}

# ---- after the start ------------------------------------------------------------------

# Runs a command for at most <seconds>.
run_limited() { # run_limited <seconds> <command...>
  local secs=$1 pid watch rc=0
  shift
  "$@" </dev/null &
  pid=$!
  ( sleep "$secs"; kill -TERM "$pid" 2>/dev/null ) </dev/null >/dev/null 2>&1 &
  watch=$!
  wait "$pid" || rc=$?
  kill "$watch" 2>/dev/null || true
  wait "$watch" 2>/dev/null || true
  return "$rc"
}

# Whether hardware HEVC works for the service. The running service tries it
# itself (it runs as a daemon, which this Terminal session does not) and
# writes ok or failed to run/videotoolbox. When it has not answered in time,
# one second of HEVC as the service account from here, the way the converter
# encodes (-allow_sw 0: no quiet fallback to the CPU).
videotoolbox_selftest() {
  local answer="" i=0
  while [ "$i" -lt "$VT_WAIT" ]; do
    answer=$(tr -cd 'a-z' < "$STATE/run/videotoolbox" 2>/dev/null || true)
    [ -z "$answer" ] || break
    sleep 1
    i=$((i + 1))
  done
  if [ -z "$answer" ]; then
    if run_limited 30 sudo -u "$SVC" env HOME="$STATE/run/home" TMPDIR="$STATE/run/tmp" \
      "$PROG/ffmpeg/ffmpeg" -hide_banner -nostdin -v error -f lavfi -i testsrc2=size=1280x720:rate=30 -t 1 \
      -c:v hevc_videotoolbox -q:v 55 -allow_sw 0 -f null - >/dev/null 2>&1; then answer=ok; else answer=failed; fi
  fi
  if [ "$answer" = ok ]; then
    say "GPU: VideoToolbox HEVC works for the service."
  else
    say "GPU: VideoToolbox HEVC did not work for the service. Converting still works, on the CPU (libx265)."
  fi
}

# What `docker logs` shows on Docker: where to go, the setup code, the
# certificate to compare. The app writes the setup code and the certificate
# before it starts listening, so once /healthz answers they are there.
print_welcome() {
  local p host ip crt fp web
  p=$(port)
  web=$(scheme)
  host=$(scutil --get LocalHostName 2>/dev/null || true)
  ip=$(ipconfig getifaddr en0 2>/dev/null || ipconfig getifaddr en1 2>/dev/null || true)
  say ""
  say "Sentry Six Manager is running."
  [ -z "$host" ] || say "  Open $web://$host.local:$p"
  [ -z "$ip" ] || say "    or $web://$ip:$p"
  if [ -s "$STATE/data/setup-code.txt" ]; then
    say "  Setup code: $(tr -cd 'A-Za-z0-9-' < "$STATE/data/setup-code.txt")"
    # for the Mac app, which reads it from this output (and keeps it in memory only)
    [ -z "$SERVICE_MODE" ] || say "SETUP-CODE: $(tr -cd 'A-Za-z0-9-' < "$STATE/data/setup-code.txt")"
  else
    say "  No setup code: the account is already set up."
  fi
  crt="$STATE/data/tls/server.crt"
  if [ -s "$crt" ]; then
    fp=$("$PROG/node/bin/node" -e 'const c = new (require("crypto").X509Certificate)(require("fs").readFileSync(process.argv[1])); console.log(c.fingerprint256)' "$crt" 2>/dev/null || true)
    [ -z "$fp" ] || say "  Certificate fingerprint (SHA-256), as browsers show it: $fp"
  fi
  say ""
  say "  sentry-six-manager status, sudo sentry-six-manager logs (a new Terminal window has it on PATH)"
  say "  Footage on a drive or in /Users/Shared: sudo sentry-six-manager allow-folder <folder>"
}

# With the macOS firewall on, node may take connections.
open_firewall() {
  [ -z "$ROOT" ] || return 0
  [ -x "$FIREWALL" ] || return 0
  if "$FIREWALL" --getglobalstate 2>/dev/null | grep -qi enabled; then
    "$FIREWALL" --add "$PROG/node/bin/node" >/dev/null 2>&1 || true
    "$FIREWALL" --unblockapp "$PROG/node/bin/node" >/dev/null 2>&1 || true
  fi
}

# ---- main --------------------------------------------------------------------------

main() {
  local name
  while [ $# -gt 0 ]; do
    case "$1" in
      --from) [ $# -ge 2 ] || fail "--from needs a file"; FROM=$2; shift 2 ;;
      --sums) [ $# -ge 2 ] || fail "--sums needs a file"; SUMS=$2; shift 2 ;;
      --release) [ $# -ge 2 ] || fail "--release needs a tag"; TAG=$2; shift 2 ;;
      --update) UPDATE=1; shift ;;
      --from-dir) [ $# -ge 2 ] || fail "--from-dir needs a folder"; FROM_DIR=$2; shift 2 ;;
      --service-mode) [ $# -ge 2 ] || fail "--service-mode needs daemon"; SERVICE_MODE=$2; shift 2 ;;
      --local-user) [ $# -ge 2 ] || fail "--local-user needs a name"; LOCAL_USER=$2; LOCAL_USER_SET=1; shift 2 ;;
      --import-data) [ $# -ge 2 ] || fail "--import-data needs a folder"; IMPORT_DATA=$2; shift 2 ;;
      -h|--help) usage; exit 0 ;;
      *) usage >&2; fail "unknown option $1" ;;
    esac
  done
  case "$SERVICE_MODE" in ''|daemon) ;; *) fail "--service-mode takes daemon (the app runs \"in your login\" itself)" ;; esac
  if [ -n "$FROM_DIR" ] && { [ -n "$FROM" ] || [ -n "$TAG" ]; }; then fail "--from-dir cannot go with --from or --release"; fi
  check_root
  check_apple_silicon
  check_not_rosetta
  check_macos
  if [ "$UPDATE" = 1 ] && [ ! -d "$PROG" ]; then fail "Sentry Six Manager is not installed here: run install.sh"; fi

  WORK=$(mktemp -d "${TMPDIR:-/tmp}/sentry-six-install.XXXXXX")
  trap on_exit EXIT
  if [ -n "$FROM_DIR" ]; then
    copy_from_dir
  else
    name=$(fetch)
    verify "$name"
    extract "$name"
  fi

  ensure_account
  ensure_state
  local_signin_users

  # the swap: the running version stops only now, and stays as .old
  stop_service
  import_data
  rm -rf "$PROG.old"
  if [ -d "$PROG" ]; then mv "$PROG" "$PROG.old"; SWAPPED=1; fi
  mv "$PROG.new" "$PROG"
  start_service
  open_firewall
  say "Waiting for the service to answer"
  if ! healthy; then
    if [ "$SWAPPED" = 1 ]; then
      SWAPPED=0
      say "The new version did not answer within $WAIT s: going back to the version before."
      roll_back || true
      fail "the update was not installed; the version before is back"
    fi
    fail "the service did not answer within $WAIT s: sudo sentry-six-manager logs"
  fi
  SWAPPED=0
  videotoolbox_selftest
  print_welcome
}

main "$@"
