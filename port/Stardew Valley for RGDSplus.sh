#!/bin/sh
set -u
PORTS_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd -P) || exit 1
LAUNCHER="$PORTS_DIR/StardewValleyMainline/StardewValleyMainline.sh"
if [ ! -r "$LAUNCHER" ]; then
    printf '[Stardew Valley for RGDSplus] Missing launcher: %s\n' "$LAUNCHER" >&2
    exit 127
fi
exec /bin/bash "$LAUNCHER" "$@"
