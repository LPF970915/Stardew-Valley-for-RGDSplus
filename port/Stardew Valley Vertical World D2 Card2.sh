#!/bin/bash
set -u
SCRIPT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd -P) || exit 1
GAME_DIR="$SCRIPT_DIR/StardewVerticalWorldD2/StardewValleyMainline"
if [ ! -r "$GAME_DIR/gamedata/Stardew Valley.dll" ]; then
  GAME_DIR="$SCRIPT_DIR/StardewVerticalWorldD2/StardewVerticalWorldD2/StardewValleyMainline"
fi
if [ ! -r "$GAME_DIR/StardewValleyMainline.sh" ]; then
  echo "Missing D2 launcher at $GAME_DIR/StardewValleyMainline.sh" >&2
  exit 1
fi
export DISPLAY_WIDTH=2048
export DISPLAY_HEIGHT=768
export SDL_VIDEO_DOUBLE_BUFFER=1
export SDL_RENDER_VSYNC=1
export SDV_FORCE_SMAPI=1
export SDV_DUAL_SCREEN_LOGICAL_4X3=1
export SDV_DUAL_SCREEN_VERTICAL_WORLD=1
export SDV_RGDS_PHYSICAL_UPPER="${SDV_RGDS_PHYSICAL_UPPER:-left}"
export SDV_DUAL_WORLD_PASSES="${SDV_DUAL_WORLD_PASSES:-1}"
exec /bin/bash "$GAME_DIR/StardewValleyMainline.sh" "$@"
