"""Recover the RGDSplus local audio server before MonoGame initializes OpenAL."""
import argparse
import os
from pathlib import Path
import subprocess
import time


SERVER = "unix:/tmp/pulse-socket"


def run(arguments, environment, timeout=5):
    try:
        result = subprocess.run(arguments, env=environment, capture_output=True,
                                text=True, errors="replace", timeout=timeout)
        return result.returncode == 0, result.stdout + result.stderr
    except (OSError, subprocess.TimeoutExpired) as error:
        return False, str(error)


def prepare(log_dir, environment=None):
    environment = dict(os.environ if environment is None else environment)
    environment.setdefault("PULSE_SERVER", SERVER)

    def ready():
        return run(["pactl", "info"], environment)[0]

    def recovered():
        # A newly started daemon has no previous mute state. Keep explicit QA
        # runs muted; normal launches unmute without changing any volume.
        qa = environment.get("SDV_DUAL_UI_TEST_DIR") or environment.get("SDV_DUAL_PERF_TEST_DIR")
        ok, error = run(["pactl", "set-sink-mute", "@DEFAULT_SINK@", "1" if qa else "0"], environment)
        if not ok:
            raise RuntimeError("Audio recovered, but mute policy failed: " + error)

    if ready():
        print("[audio] PulseAudio ready", flush=True)
        return
    if environment["PULSE_SERVER"] != SERVER:
        raise RuntimeError("Configured PULSE_SERVER is unavailable: " + environment["PULSE_SERVER"])
    # Never kill/restart another application's audio server. It may still be
    # starting; allow a short bounded wait before reporting an error.
    if run(["pgrep", "-x", "pulseaudio"], environment)[0]:
        for _ in range(5):
            time.sleep(.2)
            if ready():
                return
        raise RuntimeError("PulseAudio is running but its local socket is unavailable")

    log_dir = Path(log_dir)
    log_dir.mkdir(parents=True, exist_ok=True)
    daemon_env = dict(environment, PULSE_HOME="/tmp/.pulse")
    # Use the firmware's native libraries and state, including during isolated QA.
    for name in ("LD_LIBRARY_PATH", "LD_PRELOAD", "XDG_CONFIG_HOME", "PULSE_RUNTIME_PATH"):
        daemon_env.pop(name, None)
    print("[audio] Server unavailable; starting firmware audio service", flush=True)
    _, output = run(["/etc/init.d/S50pulseaudio", "start"], daemon_env, timeout=10)
    (log_dir / "audio-service-start.txt").write_text(output, encoding="utf-8")
    # The firmware init script can print OK even when the daemon crashes.
    if ready():
        recovered()
        print("[audio] Firmware audio service recovered", flush=True)
        return
    if run(["pgrep", "-x", "pulseaudio"], environment)[0]:
        raise RuntimeError("Firmware audio service has no reachable socket")

    cards = Path("/proc/asound/cards").read_text()
    if "rockchipaw883xx" not in cards:
        raise RuntimeError("Audio recovery requires the RGDSplus speaker sound card")
    # This firmware can SIGSEGV while probing the ALSA a52/FFmpeg plugin.
    # Open the actual stereo speaker PCM directly instead of probing unrelated
    # surround encoders. This changes no firmware files or hardware volumes.
    print("[audio] Firmware startup failed; using direct RGDSplus speaker PCM", flush=True)
    command = [
        "pulseaudio", "--daemonize", "--exit-idle-time=-1", "--disallow-exit", "-n",
        "--log-target=file:" + str(log_dir / "audio-recovery.log"),
        "--load=module-device-restore", "--load=module-stream-restore",
        "--load=module-alsa-sink device=hw:rockchipaw883xx sink_name=rgds_speakers tsched=0 channels=2 rate=44100",
        "--load=module-native-protocol-unix auth-anonymous=1 socket=/tmp/pulse-socket",
        "--load=module-default-device-restore", "--load=module-suspend-on-idle",
    ]
    _, output = run(command, daemon_env, timeout=10)
    if not ready():
        raise RuntimeError("Direct speaker recovery failed: " + output)
    recovered()
    print("[audio] Recovered PulseAudio using rgds_speakers", flush=True)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--log-dir", type=Path, required=True)
    args = parser.parse_args()
    try:
        prepare(args.log_dir)
    except (OSError, RuntimeError) as error:
        print("[audio] ERROR: " + str(error), flush=True)
        raise SystemExit(1)
