#!/bin/bash
set -u
PORTS_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd -P) || exit 1
GAME_DIR="$PORTS_DIR/StardewVerticalWorldD2/StardewValleyMainline"
LEGACY_DIR="$PORTS_DIR/StardewVerticalWorldD2/StardewVerticalWorldD2/StardewValleyMainline"
# Prefer an installed legacy game over an empty new adapter; preserve its saves.
if [ ! -r "$GAME_DIR/gamedata/Stardew Valley.dll" ] && [ -r "$LEGACY_DIR/gamedata/Stardew Valley.dll" ]; then
  GAME_DIR="$LEGACY_DIR"
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
