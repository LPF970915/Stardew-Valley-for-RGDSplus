#!/bin/bash

# Refuse a second instance before PortMaster or input helpers change state.
if [ "${SDV_DUAL_SCREEN_VERTICAL_WORLD:-0}" = "1" ] && [ "${SDV_SESSION_LOCKED:-0}" != "1" ]; then
  # --close prevents the long-lived firmware/tee helpers inheriting the lock.
  exec flock -n -o /tmp/stardew-rgds-session.lock env SDV_SESSION_LOCKED=1 /bin/bash "$0" "$@"
fi
unset SDV_SESSION_LOCKED

XDG_DATA_HOME=${XDG_DATA_HOME:-$HOME/.local/share}

if [ -d "/opt/system/Tools/PortMaster/" ]; then
  controlfolder="/opt/system/Tools/PortMaster"
elif [ -d "/opt/tools/PortMaster/" ]; then
  controlfolder="/opt/tools/PortMaster"
elif [ -d "$XDG_DATA_HOME/PortMaster/" ]; then
  controlfolder="$XDG_DATA_HOME/PortMaster"
else
  controlfolder="/roms/ports/PortMaster"
fi

source $controlfolder/control.txt
[ -f "${controlfolder}/mod_${CFW_NAME}.txt" ] && source "${controlfolder}/mod_${CFW_NAME}.txt"
get_controls

# RGDSplus menu launches normally provide these values; keep direct SSH smoke
# tests and older menu integrations on the same Wayland/PulseAudio defaults.
export XDG_RUNTIME_DIR="${XDG_RUNTIME_DIR:-/var/run}"
export WAYLAND_DISPLAY="${WAYLAND_DISPLAY:-wayland-0}"
export SDL_VIDEODRIVER="${SDL_VIDEODRIVER:-wayland}"
export PULSE_SERVER="${PULSE_SERVER:-unix:/tmp/pulse-socket}"

# Older RGDSplus PortMaster builds do not provide bind_directories. Keep the
# same per-port save location with a symlink fallback.
if ! command -v bind_directories >/dev/null 2>&1; then
  bind_directories() {
    local source_path="$1"
    local target_path="$2"
    mkdir -p "$(dirname "$source_path")" "$target_path"
    if [ ! -e "$source_path" ]; then
      ln -s "$target_path" "$source_path"
    fi
  }
fi

[ -n "${DISPLAY_WIDTH:-}" ] && export DISPLAY_WIDTH
[ -n "${DISPLAY_HEIGHT:-}" ] && export DISPLAY_HEIGHT

# Resolve the payload relative to this launcher. This keeps the port usable
# from RGDSplus card-two /mnt/sdcard/Ports, where $directory still points
# at the card-one /roms tree in the legacy PortMaster control script.
PORTS_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd -P) || exit 1
GAMEDIR="$PORTS_DIR"
gamedir="$GAMEDIR"

cd "$GAMEDIR"
> "$GAMEDIR/log.txt" && exec > >(tee "$GAMEDIR/log.txt") 2>&1
echo "Display environment: DISPLAY_WIDTH=${DISPLAY_WIDTH:-<unset>} DISPLAY_HEIGHT=${DISPLAY_HEIGHT:-<unset>}"

# OpenAL aborts before showing a window when the firmware audio server is down.
# Recover it before acquiring input/display state, and retain startup diagnostics.
if [ "${SDV_DUAL_SCREEN_VERTICAL_WORLD:-0}" = "1" ]; then
  if ! python3 "$gamedir/tools/rgds-audio.py" --log-dir "$gamedir/logs"; then
    cat "$gamedir/log.txt" > /dev/tty0
    exit 1
  fi
fi

# RGDSplus exposes the lower touchscreen through the session-scoped firmware
# switch. Keep the previous value and restore it only if this session still
# owns the active value; this avoids changing the system menu's later choice.
touch_mode_path="/sys/class/anbernic_misc/tpctrl"
touch_mode_previous=""
touch_mode_active=0
if [ -r "$touch_mode_path" ]; then
  touch_mode_previous="$(cat "$touch_mode_path" 2>/dev/null || true)"
  case "$touch_mode_previous" in
    0|1)
      if printf '0\n' >"$touch_mode_path" 2>/dev/null && [ "$(cat "$touch_mode_path" 2>/dev/null || true)" = "0" ]; then
        touch_mode_active=1
        echo "[touch-mode] before=$touch_mode_previous requested=0 active=0"
      else
        echo "[touch-mode] enable failed"
      fi
      ;;
    *) echo "[touch-mode] unknown value=$touch_mode_previous; unchanged" ;;
  esac
else
  echo "[touch-mode] unavailable path=$touch_mode_path"
fi
restore_touch_mode() {
  if [ "$touch_mode_active" -ne 1 ]; then return 0; fi
  if [ "$(cat "$touch_mode_path" 2>/dev/null || true)" = "0" ]; then
    printf '%s\n' "$touch_mode_previous" >"$touch_mode_path" 2>/dev/null || true
    echo "[touch-mode] restored=$(cat "$touch_mode_path" 2>/dev/null || true)"
  else
    echo "[touch-mode] restore skipped current=$(cat "$touch_mode_path" 2>/dev/null || true)"
  fi
}
session_guard_pid=""
gptokeyb_pid=""
session_path=""
cleanup_session() {
  if [ -n "$session_guard_pid" ]; then
    kill -TERM "$session_guard_pid" 2>/dev/null || true
    wait "$session_guard_pid" 2>/dev/null || true
  fi
  if [ -n "$gptokeyb_pid" ]; then
    kill -TERM "$gptokeyb_pid" 2>/dev/null || true
    wait "$gptokeyb_pid" 2>/dev/null || true
  fi
  restore_touch_mode
}
trap cleanup_session EXIT
trap 'exit 143' TERM HUP
trap 'exit 130' INT

if [ "${SDV_DUAL_SCREEN_VERTICAL_WORLD:-0}" = "1" ]; then
  session_path="$(mktemp -d /tmp/sdv-session-XXXXXXXX)/run"
  export SDV_SESSION_TOKEN="$session_path"
  python3 "$gamedir/tools/rgds-session.py" --session "$session_path" --owner "$$" --game-dir "$gamedir" &
  session_guard_pid=$!
  for ((attempt=0; attempt<60; attempt++)); do
    [ ! -f "$session_path.ready" ] || break
    kill -0 "$session_guard_pid" 2>/dev/null || break
    sleep .05
  done
  if [ ! -f "$session_path.ready" ]; then
    echo "RGDS input guard failed to start; refusing an unprotected session."
    exit 1
  fi
fi

$ESUDO chmod 666 /dev/tty0
printf "\033c" > /dev/tty0
echo "Loading... Please Wait." > /dev/tty0
show_load_status() {
  printf '%s\n' "$1" > /dev/tty0
  echo "[loading] $1"
}
show_load_status "正在准备运行环境..."

bind_directories ~/.config/StardewValley "$gamedir/savedata"

modsdir="$gamedir/Mods"
runtime_root="$gamedir/.rgds-runtime"
runtime_modsdir="$runtime_root/Mods"
mods_state_path="$runtime_root/mod-state.json"
smapi_bundle_dir="$gamedir/tools/SMAPIBundle"
smapi_game_root="$smapi_bundle_dir/game-root"
smapi_default_mods_dir="$smapi_bundle_dir/default-mods"
smapi_helper="$gamedir/tools/smapi-common"

if [ ! -f "$smapi_helper" ]; then
  pm_message "Missing SMAPI helper script at $smapi_helper."
  sleep 5
  exit 1
fi

source "$smapi_helper"
mkdir -p "$modsdir"

launch_mode="$(sdv_smapi_determine_mode "$modsdir")"
entry_assembly="Stardew Valley.dll"
smapi_patch_path="$gamedir/dlls/StardewPatches.dll"
patcher_args=(
  --game-dir "$gamedir/gamedata"
  --overlay-dir "$gamedir/overrides/gamedata"
)

if [ "$launch_mode" = "smapi" ]; then
  show_load_status "正在扫描 MOD..."
  entry_assembly="StardewModdingAPI.dll"
  smapi_patch_path="$gamedir/gamedata/smapi-internal/StardewPatches.dll"
  patcher_args+=(
    --smapi-bundle-dir "$smapi_game_root"
    --smapi-patch-assembly "$gamedir/dlls/StardewPatches.dll"
  )
fi

export DOTNET_ROOT="$gamedir/dotnet"
chmod +x "$DOTNET_ROOT/dotnet" 2>/dev/null || true
if [ -f "$gamedir/profiling.enabled" ]; then
  mkdir -p "$gamedir/logs"
  rm -f \
    "$gamedir/logs/perf-latest.csv" \
    "$gamedir/logs/content-load-latest.csv" \
    "$gamedir/logs/slow-frame-latest.csv"
  export SDV_PERF_LOG="$gamedir/logs/perf-latest.csv"
  export SDV_CONTENT_PROFILE_LOG="$gamedir/logs/content-load-latest.csv"
  export SDV_SLOW_FRAME_LOG="$gamedir/logs/slow-frame-latest.csv"
else
  unset SDV_PERF_LOG
  unset SDV_CONTENT_PROFILE_LOG
  unset SDV_SLOW_FRAME_LOG
fi
port_arch="${DEVICE_ARCH:-aarch64}"
export LD_LIBRARY_PATH="$gamedir/libs.${port_arch}${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
export SDL_GAMECONTROLLERCONFIG="$sdl_controllerconfig"

# Request libGL from PortMaster.
if [ -f "${controlfolder}/libgl_${CFW_NAME}.txt" ]; then
  source "${controlfolder}/libgl_${CFW_NAME}.txt"
else
  source "${controlfolder}/libgl_default.txt"
fi

if [[ "$LIBGL_ES" != "" ]]; then
  export SDL_VIDEO_GL_DRIVER="${gamedir}/gl4es.${port_arch}/libGL.so.1"
  export SDL_VIDEO_EGL_DRIVER="${gamedir}/gl4es.${port_arch}/libEGL.so.1"
fi

if [ ! -f "$gamedir/gamedata/Stardew Valley.dll" ]; then
  if [ -f "$gamedir/gamedata/Stardew Valley.exe" ] || [ -f "$gamedir/gamedata/StardewValley.exe" ]; then
    pm_message "Wrong Stardew Valley files. This port needs the regular Windows Steam version, not the compatibility branch. Switch Steam out of the compatibility beta and copy the game files again."
  else
    pm_message "Missing Stardew Valley game files. Copy the regular Windows Steam version into ports/stardewvalleymainline/gamedata."
  fi
  sleep 5
  exit 1
fi

if [ ! -f "$gamedir/gamedata/Stardew Valley.deps.json" ] || [ ! -f "$gamedir/gamedata/Stardew Valley.runtimeconfig.json" ]; then
  pm_message "Incomplete Stardew Valley files. This port needs the regular Windows Steam mainline build, not the compatibility branch."
  sleep 5
  exit 1
fi

export MONOGAME_PATCH="$smapi_patch_path"
if [ "$launch_mode" = "smapi" ]; then
  if ! python3 "$gamedir/tools/rgds-mod-state.py" --mods "$modsdir" --state-file "$mods_state_path"; then
    echo "[mods] Could not validate queued choices; retaining the current MOD selection."
  fi
  compat_mode="${SDV_MOD_COMPAT_MODE:-auto}"
  compat_resizer="$gamedir/tools/ModCompatResizer/RGDSplus.ModCompatResizer.dll"
  compat_args=(--mods "$modsdir" --runtime "$runtime_modsdir" --defaults "$smapi_default_mods_dir" --state-file "$mods_state_path")
  [ "$compat_mode" = "off" ] && compat_args+=(--mode off)
  show_load_status "正在检查肖像 MOD 并准备兼容资源..."
  if [ ! -f "$compat_resizer" ]; then
    echo "[mod-compat] Packaged low-memory image resizer is missing; refusing an unsafe SMAPI launch."
    exit 1
  fi
  if python3 "$gamedir/tools/rgds-mod-compat.py" "${compat_args[@]}" --resizer "$compat_resizer"; then
    show_load_status "正在构建 MOD 兼容运行目录..."
    patcher_args+=(--mods-dir "$runtime_modsdir")
    export SMAPI_MODS_PATH="$runtime_modsdir"
  else
    echo "[mod-compat] Private runtime build failed; refusing to launch an unpatched MOD set."
    exit 1
  fi
  export RGDSPLUS_USER_MODS_PATH="$modsdir"
  export RGDSPLUS_RUNTIME_MODS_PATH="${SMAPI_MODS_PATH:-$modsdir}"
  export RGDSPLUS_MOD_STATE_PATH="$mods_state_path"
  export SMAPI_USE_CURRENT_SHELL=true
else
  unset SMAPI_MODS_PATH
  unset SMAPI_USE_CURRENT_SHELL
  unset RGDSPLUS_USER_MODS_PATH RGDSPLUS_RUNTIME_MODS_PATH RGDSPLUS_MOD_STATE_PATH
fi

show_load_status "正在检查游戏文件并准备启动..."
if ! "$DOTNET_ROOT/dotnet" "$gamedir/tools/MainlineGameDataPatcher/MainlineGameDataPatcher.dll" "${patcher_args[@]}"; then
  cat "${gamedir}/log.txt" > /dev/tty0
  sleep 5
  exit 1
fi
show_load_status "正在准备游戏数据..."

cd "$gamedir/gamedata"

# Leave RAM for the firmware and graphics driver on the 1 GiB handheld.
# .NET environment limits use hexadecimal bytes; these apply only to this game.
if [ "$(awk '/MemTotal:/ {print $2}' /proc/meminfo)" -le 1200000 ]; then
  export DOTNET_GCHeapHardLimit="${DOTNET_GCHeapHardLimit:-10000000}"
  export DOTNET_GCConserveMemory="${DOTNET_GCConserveMemory:-9}"
fi

$GPTOKEYB "$DOTNET_ROOT/dotnet" &
gptokeyb_pid=$!
command -v pm_platform_helper >/dev/null 2>&1 && pm_platform_helper "$DOTNET_ROOT/dotnet"
while :; do
  show_load_status "正在启动游戏，请稍候..."
  "$DOTNET_ROOT/dotnet" "$entry_assembly" &
  game_pid=$!
  if [ -n "$session_path" ]; then printf '%s\n' "$game_pid" >"$session_path.game.pid"; fi
  wait "$game_pid"
  game_status=$?
  echo "Stardew Valley exited with status ${game_status}."
  if [ "$game_status" -ne 0 ]; then
    echo "[crash-diagnostics] Recent memory state:"
    awk '/MemTotal:|MemAvailable:|SwapTotal:|SwapFree:/ {print}' /proc/meminfo
    echo "[crash-diagnostics] Recent kernel OOM/segfault records:"
    dmesg 2>/dev/null | grep -iE 'out of memory|oom-kill|killed process|segfault' | tail -12 || true
    echo "[crash-diagnostics] SMAPI log candidates:"
    for candidate in "$gamedir/gamedata/SMAPI-latest.txt" "$gamedir/gamedata/SMAPI-crash.txt" "$HOME/.config/StardewValley/ErrorLogs"; do
      [ ! -e "$candidate" ] || ls -ld "$candidate"
    done
  fi
  # Only a normal exit plus an explicit request from this exact child restarts.
  # MOD choices apply once above, so returning to title retains this session's set.
  if [ "$game_status" -ne 0 ] || [ -z "$session_path" ] ||
     [ -f "$session_path.exit-requested" ] ||
     [ "$(cat "$session_path.return-title" 2>/dev/null)" != "$game_pid" ]; then break; fi
  rm -f -- "$session_path.return-title"
  echo "Restarting Stardew Valley at the title screen."
done

if command -v pm_finish >/dev/null 2>&1; then
  pm_finish
else
  [ -n "$gptokeyb_pid" ] && kill -TERM "$gptokeyb_pid" 2>/dev/null || true
  command -v systemctl >/dev/null 2>&1 && $ESUDO systemctl restart oga_events &
fi
