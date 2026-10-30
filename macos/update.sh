#!/bin/bash
# Updates Sentry Six Manager to the newest macOS release:
#
#   sudo sentry-six-manager update            (or: sudo /Library/SentrySixManager/update.sh)
#   sudo /Library/SentrySixManager/update.sh --release macos-v<ver>-<sha>
#
# It is install.sh with --update: the download is checked against its
# SHA-256 and unpacked next to the running version before that one stops.
# Then the two are swapped (the old one stays as /Library/SentrySixManager.old),
# and if the new one does not answer within a minute the old one is put back.
# Settings, data, footage and the Recycle Bin stay as they are.

set -euo pipefail

HERE=$(cd "$(dirname "$0")" && pwd)
exec /bin/bash "$HERE/install.sh" --update "$@"
