#!/bin/bash
set -u
PORTS_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd -P) || exit 1
GAME_DIR="$PORTS_DIR/Stardew Valley for RGDSplus"
# New installs keep the launcher and payload in the same-named folder. If an
# older card already has game data, use it rather than splitting the saves.
if [ ! -r "$GAME_DIR/StardewValleyMainline.sh" ]; then
  for candidate in \
    "$PORTS_DIR/StardewVerticalWorldD2/StardewValleyMainline" \
    "$PORTS_DIR/StardewVerticalWorldD2/StardewVerticalWorldD2/StardewValleyMainline"
  do
    if [ -r "$candidate/StardewValleyMainline.sh" ]; then
      GAME_DIR="$candidate"
      break
    fi
  done
elif [ ! -r "$GAME_DIR/gamedata/Stardew Valley.dll" ]; then
  for candidate in \
    "$PORTS_DIR/StardewVerticalWorldD2/StardewValleyMainline" \
    "$PORTS_DIR/StardewVerticalWorldD2/StardewVerticalWorldD2/StardewValleyMainline"
  do
    if [ -r "$candidate/StardewValleyMainline.sh" ] && [ -r "$candidate/gamedata/Stardew Valley.dll" ]; then
      GAME_DIR="$candidate"
      break
    fi
  done
fi
if [ ! -r "$GAME_DIR/StardewValleyMainline.sh" ]; then
  printf 'Missing Stardew Valley adapter: %s\n' "$GAME_DIR" >&2
  exit 1
fi
export DISPLAY_WIDTH=2048
export DISPLAY_HEIGHT=768
export SDL_VIDEO_DOUBLE_BUFFER=1
export SDL_RENDER_VSYNC=1
export SDV_FORCE_SMAPI=1
export SDV_DUAL_SCREEN_LOGICAL_4X3=1
export SDV_DUAL_SCREEN_VERTICAL_WORLD=1
export SDV_WORLD_RENDER_SCALE="${SDV_WORLD_RENDER_SCALE:-1}"
export SDV_TARGET_FPS="${SDV_TARGET_FPS:-30}"
export SDV_FIRST_USE_WARMUP="${SDV_FIRST_USE_WARMUP:-1}"
export SDV_WORLD_ALPHA_DISCARD="${SDV_WORLD_ALPHA_DISCARD:-1}"
export SDV_TREE_TRIM="${SDV_TREE_TRIM:-1}"
export SDV_RGDS_PHYSICAL_UPPER="${SDV_RGDS_PHYSICAL_UPPER:-left}"
export SDV_DUAL_WORLD_PASSES="${SDV_DUAL_WORLD_PASSES:-1}"
export PULSE_SERVER="${PULSE_SERVER:-unix:/tmp/pulse-socket}"
# Device QA mutes the mixer before launch. Normal releases start unmuted,
# without changing the user's volume or any game audio setting.
if [ -z "${SDV_DUAL_UI_TEST_DIR:-}" ] && [ -z "${SDV_DUAL_PERF_TEST_DIR:-}" ]; then
  command -v amixer >/dev/null 2>&1 && amixer -q sset Master unmute || true
  command -v pactl >/dev/null 2>&1 && pactl set-sink-mute @DEFAULT_SINK@ 0 || true
fi
exec /bin/bash "$GAME_DIR/StardewValleyMainline.sh" "$@"
