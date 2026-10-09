"""Opt-in performance matrix on Card2; only uses the existing in-memory fixture."""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import shlex
import time
import uuid

import paramiko

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("device_ui", ROOT / "tools/qa-device-ui.py")
qa = importlib.util.module_from_spec(spec)
spec.loader.exec_module(qa)
validation_spec = importlib.util.spec_from_file_location("first_use_validation", ROOT / "tools/validate-first-use.py")
validation = importlib.util.module_from_spec(validation_spec)
validation_spec.loader.exec_module(validation)
GAME = qa.GAME
ENTRY = "/mnt/sdcard/Ports/Stardew Valley Vertical World D2 Card2.sh"
MOD = GAME + "/Mods/RGDSplus.PerformanceProbe"


def read_reply(ftp, path):
    try:
        with ftp.open(path) as stream:
            return json.load(stream)
    except (OSError, json.JSONDecodeError):
        # The production UI QA driver writes replies in place. A partial read
        # must be retried without resending a possibly executed command.
        return None


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--smoke", action="store_true")
    parser.add_argument("--seconds", type=int, default=60)
    parser.add_argument("--repeats", type=int, default=3)
    parser.add_argument("--page-seconds", type=int, default=30)
    parser.add_argument("--pages-only", action="store_true", help="Run a fresh-process page matrix without repeating world fixtures.")
    parser.add_argument("--world-only", action="store_true", help="Run only the SceneHud world matrix in a fresh process.")
    parser.add_argument("--zoom", type=float, default=1.0, help="World zoom used for this isolated matrix.")
    parser.add_argument("--lower-cache", action="store_true", help="Enable the temporary lower-world cache prototype.")
    parser.add_argument("--lower-cache-interval", type=int, default=4, help="Prototype lower cache refresh interval in frames.")
    parser.add_argument("--experiment-dll", type=Path, help="Temporarily deploy this DLL for the QA run and restore the production DLL afterward.")
    parser.add_argument("--first-use", action="store_true", help="Measure first and repeated tool use without prior action warmup or idle-scene matrix.")
    parser.add_argument("--tick-only", action="store_true", help="Patch only Game.Tick to check whether detailed instrumentation exaggerates first use.")
    parser.add_argument("--mode", choices=("SceneHud", "ExpandedHud"), default="SceneHud", help="World mode for --first-use.")
    parser.add_argument("--first-use-scenes", choices=("tools", "move", "transition"), default="tools", help="Controlled first-use path; transition invokes engine warp, not physical doorway input.")
    parser.add_argument("--prepare-methods", action="store_true", help="QA-only targeted method precompilation; does not execute gameplay actions.")
    parser.add_argument("--prepare-tool-chain", action="store_true", help="Extend QA precompilation to tool callbacks, sprite animation, and debris helpers.")
    args = parser.parse_args()
    if args.smoke:
        args.seconds, args.repeats, args.page_seconds = 5, 1, 5
    if args.first_use:
        args.world_only = True
    if args.prepare_tool_chain:
        args.prepare_methods = True
    if not 0.5 <= args.zoom <= 1.5:
        parser.error("--zoom must be between 0.5 and 1.5")
    if not 1 <= args.lower_cache_interval <= 30:
        parser.error("--lower-cache-interval must be between 1 and 30")
    if args.experiment_dll and not args.experiment_dll.is_file():
        parser.error("--experiment-dll does not exist")
    stamp = time.strftime("%Y%m%d-%H%M%S")
    out = ROOT / "build" / ("performance-" + stamp)
    out.mkdir(parents=True)
    remote = "/tmp/sdv-perf-" + stamp
    qa.QA = remote + "/ui"
    ssh = paramiko.SSHClient()
    ssh.load_system_host_keys()
    ssh.set_missing_host_key_policy(paramiko.RejectPolicy())
    ssh.connect("[device-host]", username="root", password=os.environ["RGDSPLUS_SSH_PASSWORD"], timeout=15)
    ftp = ssh.open_sftp()
    pid = None
    deployed = False
    saved_configs = {}
    before = None
    results = []
    production_dll_backup = None

    def run(cmd):
        return qa.run(ssh, cmd)

    def request(action, **kw):
        identifier = uuid.uuid4().hex
        with ftp.open(qa.QA + "/command.json.next", "w") as f:
            f.write(json.dumps(dict(id=identifier, action=action, **kw)))
        ftp.posix_rename(qa.QA + "/command.json.next", qa.QA + "/command.json")
        end = time.monotonic() + 240
        while time.monotonic() < end:
            value = read_reply(ftp, qa.QA + "/result.json")
            if value and value["id"] == identifier:
                if value.get("error"):
                    raise RuntimeError(value["error"])
                return value
            time.sleep(.2)
        raise TimeoutError("UI command " + action)

    def probe(action, **kw):
        identifier = uuid.uuid4().hex
        with ftp.open(remote + "/command.json.next", "w") as f:
            f.write(json.dumps(dict(id=identifier, action=action, **kw)))
        ftp.posix_rename(remote + "/command.json.next", remote + "/command.json")
        end = time.monotonic() + 40
        while time.monotonic() < end:
            value = read_reply(ftp, remote + "/result.json")
            if value and value["id"] == identifier:
                if value.get("error"):
                    raise RuntimeError(value["error"])
                return value["result"]
            time.sleep(.2)
        raise TimeoutError("Performance command " + action)

    def inventory():
        cmd = "find -L " + shlex.quote(GAME) + " /root -type f \\( -name config.json -o -name startup_preferences -o -name SaveGameInfo -o -path '*/Saves/*' \\) -not -path '*/gamedata/*' -exec sha256sum {} + 2>/dev/null || true"
        return dict(line.split(None, 1)[::-1] for line in run(cmd).splitlines() if len(line.split(None, 1)) == 2)

    def physical(name):
        from PIL import Image, ImageStat
        for crtc, label in ((130, "upper"), (90, "lower")):
            _, stdout, stderr = ssh.exec_command("kmsgrab --crtc " + str(crtc), timeout=20)
            raw = stdout.read()
            error = stderr.read().decode(errors="replace")
            if stdout.channel.recv_exit_status() or len(raw) != 1024 * 768 * 4:
                raise RuntimeError("Physical capture failed: " + error)
            image = Image.frombytes("RGB", (1024, 768), raw, "raw", "BGRX")
            if max(ImageStat.Stat(image).stddev) < 5:
                raise RuntimeError("Blank physical " + label)
            image.save(out / (name + "-" + label + ".png"))

    def mode(target):
        state = request("status")
        if state["mode"] != target:
            request("toggle")
            time.sleep(2)
        assert request("status")["mode"] == target
        request("close")
        if target == "ExpandedHud":
            request("dashboard")

    def page(target):
        state = request("status")
        for _ in range(13):
            if state["page"] == target:
                assert state["menu"] is None
                return
            request("touch", x=976, y=46, pressed=True, released=False)
            state = request("touch", x=976, y=46, pressed=False, released=True)
        raise RuntimeError("Page navigation failed " + str(target))

    def measure(label, seconds, action=False):
        state = request("status")
        assert state["worldReady"] and state["menu"] is None
        with ftp.open(remote + "/label", "w") as f:
            f.write(label)
        probe("start", label=label, actions=action)
        time.sleep(seconds)
        result = probe("stop")
        result["state"] = {k: state.get(k) for k in ("mode", "page", "zoom", "uiScale", "menu", "worldReady")}
        results.append(result)
        (out / "results.json").write_text(json.dumps(results, indent=2, ensure_ascii=False), encoding="utf-8")
        print(f"DONE {label}: {result['fps']:.2f} FPS, P95 {result['frameMs']['p95']:.2f} ms, alloc {result['allocatedMiBPerSecond']:.2f} MiB/s, actions {result['actions']}, removed {result['removed']}", flush=True)
        validation.validate(result)
        assert result["frames"] > seconds * 5 and result["activeFrames"] >= result["frames"] * .95
        return result

    try:
        if run("pgrep -af '[S]tardewModdingAPI|[d]otnet' || true").strip():
            raise RuntimeError("A game is already running; it will not be stopped.")
        if run("test ! -e " + shlex.quote(MOD) + " && echo clear || true").strip() != "clear":
            raise RuntimeError("Probe directory already exists; do not overwrite it.")
        before = inventory()
        (out / "inventory-before.json").write_text(json.dumps(before, indent=2), encoding="utf-8")
        for path in before:
            if path.endswith("config.json") or path.endswith("startup_preferences"):
                with ftp.open(path, "rb") as f:
                    saved_configs[path] = f.read()
        dll_path = GAME + "/Mods/RGDSplus.VerticalWorld/VerticalWorld.dll"
        dll_before = run("sha256sum '" + dll_path + "'").split()[0]
        if args.experiment_dll:
            production_dll_backup = out / "VerticalWorld.production.before.dll"
            ftp.get(dll_path, str(production_dll_backup))
            ftp.put(str(args.experiment_dll), dll_path + ".perf-next")
            if run("sha256sum '" + dll_path + ".perf-next'").split()[0] != hashlib.sha256(args.experiment_dll.read_bytes()).hexdigest():
                raise RuntimeError("Experimental DLL upload hash mismatch")
            ftp.posix_rename(dll_path + ".perf-next", dll_path)
        metadata = {"entry": ENTRY, "game": GAME, "host": "[device-host]", "localStamp": stamp,
                    "worldSeconds": args.seconds, "pageSeconds": args.page_seconds, "repeats": args.repeats,
                    "smoke": args.smoke, "productionDllSha256": dll_before,
                    "probeSha256": hashlib.sha256((ROOT / "tools/performance-probe/bin/Release/net6.0/PerformanceProbe.dll").read_bytes()).hexdigest(),
                    "language": "zh", "zoom": args.zoom, "uiPreference": 1.5, "fixtureOnly": True,
                    "deviceOutputMuted": True, "audioLogicDisabled": False, "pagesOnly": args.pages_only,
                    "worldOnly": args.world_only, "lowerCache": args.lower_cache,
                    "lowerCacheInterval": args.lower_cache_interval,
                    "experimentDll": str(args.experiment_dll) if args.experiment_dll else None}
        metadata.update(firstUse=args.first_use, tickOnly=args.tick_only, firstUseMode=args.mode, firstUseScenes=args.first_use_scenes, prepareMethods=args.prepare_methods, prepareToolChain=args.prepare_tool_chain)
        (out / "measurement.json").write_text(json.dumps(metadata, indent=2), encoding="utf-8")
        context = run("date -u; uname -a; free -m; cat /proc/cpuinfo; ls /sys/class/devfreq; ps -eo pid,comm,args; cat '" + ENTRY + "'")
        (out / "device-before.txt").write_text(context, encoding="utf-8")
        audio_before = run("amixer sget Master")
        audio_after = run("amixer sset Master mute")
        if "[off]" not in audio_after:
            raise RuntimeError("Device output mute was not confirmed.")
        (out / "audio.json").write_text(json.dumps({"before": audio_before, "muted": audio_after,
                                                    "gameAudioDisabled": False, "keepMuted": True}, indent=2), encoding="utf-8")
        run("mkdir -p " + shlex.quote(remote + "/ui") + " " + shlex.quote(MOD))
        deployed = True
        for name, local in (("PerformanceProbe.dll", ROOT / "tools/performance-probe/bin/Release/net6.0/PerformanceProbe.dll"), ("manifest.json", ROOT / "tools/performance-probe/manifest.json")):
            ftp.put(str(local), MOD + "/" + name + ".next")
            assert run("sha256sum " + shlex.quote(MOD + "/" + name + ".next")).split()[0] == hashlib.sha256(local.read_bytes()).hexdigest()
            ftp.posix_rename(MOD + "/" + name + ".next", MOD + "/" + name)
        experiment_env = ""
        if args.lower_cache:
            experiment_env += " SDV_LOWER_WORLD_CACHE=1 SDV_LOWER_CACHE_INTERVAL=" + str(args.lower_cache_interval)
        if args.tick_only:
            experiment_env += " SDV_PERF_TICK_ONLY=1"
        command = f"nohup env SDV_DUAL_UI_TEST_DIR={qa.QA} SDV_DUAL_PERF_TEST_DIR={remote}{experiment_env} /bin/bash {shlex.quote(ENTRY)} >{remote}/launch.log 2>&1 </dev/null &"
        run(command)
        print("START " + remote + " output=" + str(out), flush=True)
        for _ in range(120):
            processes = run("pgrep -f '[S]tardewModdingAPI.dll' || true").split()
            for candidate in processes:
                env = run("tr '\\0' '\\n' </proc/" + candidate + "/environ 2>/dev/null || true")
                if "SDV_DUAL_PERF_TEST_DIR=" + remote in env.splitlines():
                    pid = int(candidate)
                    break
            try:
                ftp.stat(remote + "/ready.json")
                if pid:
                    break
            except OSError:
                pass
            time.sleep(1)
        if not pid:
            raise RuntimeError("QA game did not start.")
        print("OWNED PID " + str(pid), flush=True)
        request("fixture")
        request("present")
        request("language", code="zh")
        request("scale", ui=1.5, zoom=args.zoom)
        request("settle_scene")
        time.sleep(45)
        # The remote sampler reads counters once a second and never forces GC.
        sampler = f'''#!/bin/sh
while test -r /proc/{pid}/status; do
 echo "@sample $(date +%s) $(cat {remote}/label 2>/dev/null)"
 for f in /proc/{pid}/status /proc/{pid}/stat /proc/{pid}/io /proc/{pid}/smaps_rollup /proc/meminfo /proc/vmstat /proc/stat /sys/devices/system/cpu/cpu0/cpufreq/scaling_cur_freq /sys/class/devfreq/fde60000.gpu/cur_freq /sys/class/devfreq/fde60000.gpu/load /sys/class/devfreq/dmc/cur_freq /sys/class/thermal/thermal_zone*/temp; do
  if test -r "$f"; then echo "@file $f"; cat "$f"; fi
 done
 sleep 1
done
'''
        with ftp.open(remote + "/sampler.sh", "w") as f:
            f.write(sampler)
        run(f"nohup /bin/sh {remote}/sampler.sh >{remote}/system-samples.txt 2>{remote}/sampler-errors.txt </dev/null &")
        mode(args.mode if args.first_use else "SceneHud")
        if args.prepare_methods:
            precompile = probe("precompile", toolChain=args.prepare_tool_chain)
            (out / "precompile.json").write_text(json.dumps(precompile, indent=2), encoding="utf-8")
            print("PRECOMPILE " + json.dumps(precompile), flush=True)
        probe("prepare", scene="sparse")
        time.sleep(4)
        physical("initial-scene")
        measure("smoke-sparse", 5)
        scenes = [("sparse", False), ("grass", False), ("mixed", False), ("grass", True), ("axe", True), ("pickaxe", True)]
        if args.first_use:
            scenes = [("grass", True), ("axe", True), ("pickaxe", True)] if args.first_use_scenes == "tools" else [(args.first_use_scenes, True)]
        if args.smoke:
            scenes = [("mixed", False), ("grass", True), ("axe", True), ("pickaxe", True)]
        for repeat in range(0 if args.pages_only else args.repeats):
            # world-only isolates the continuous dual-screen world path. The full
            # matrix still alternates modes to reduce the effect of temperature drift.
            if args.world_only:
                modes = [args.mode if args.first_use else "SceneHud"]
            else:
                modes = ["SceneHud", "ExpandedHud"] if repeat % 2 == 0 else ["ExpandedHud", "SceneHud"]
            for target in modes:
                mode(target)
                for scene, action in scenes:
                    probe("prepare", scene=scene)
                    time.sleep(4)
                    label = f"world-r{repeat + 1}-{target}-{scene}-{'action' if action else 'idle'}"
                    print("MEASURE " + label, flush=True)
                    measure(label, args.seconds, action)
        if not args.world_only:
            mode("ExpandedHud")
            for scene in (["data"] if args.smoke else ["sparse", "data"]):
                for repeat in range(args.repeats):
                    probe("prepare", scene=scene)
                    request("dashboard")
                    targets = [0, 7, 8] if args.smoke else list(range(12)) if repeat == 0 else [0, 1, 7, 8]
                    if repeat % 2:
                        targets.reverse()
                    for target in targets:
                        page(target)
                        time.sleep(4)
                        label = f"page-r{repeat + 1}-{scene}-{target:02d}"
                        print("MEASURE " + label, flush=True)
                        measure(label, args.page_seconds)
                        if repeat == 0 and scene == "data":
                            physical("page-%02d" % target)
        print("MATRIX COMPLETE " + str(len(results)), flush=True)
    finally:
        cleanup = {"remote": remote, "pid": pid, "out": str(out), "completed": len(results)}
        try:
            if pid:
                env = run("tr '\\0' '\\n' </proc/" + str(pid) + "/environ 2>/dev/null || true")
                if "SDV_DUAL_PERF_TEST_DIR=" + remote in env.splitlines():
                    run("kill -TERM " + str(pid))
                    for _ in range(15):
                        if not run("test -r /proc/" + str(pid) + "/status && echo alive || true").strip():
                            break
                        time.sleep(1)
                    else:
                        raise RuntimeError("Owned QA process did not exit; no force kill applied.")
                cleanup["qaStopped"] = not bool(run("test -r /proc/" + str(pid) + "/status && echo alive || true").strip())
            if deployed:
                for item in ftp.listdir_attr(remote):
                    if item.st_mode & 0o170000 == 0o100000:
                        ftp.get(remote + "/" + item.filename, str(out / item.filename))
            # Keep the built-in production metrics as a second, independent channel.
            if pid:
                ftp.get(GAME + "/Mods/RGDSplus.VerticalWorld/frame-metrics.csv", str(out / "production-frame-metrics.csv"))
            if before is not None:
                after = inventory()
                config_changes = []
                for path, content in saved_configs.items():
                    if after.get(path) != before[path]:
                        config_changes.append(path)
                        with ftp.open(path + ".perf-restore", "wb") as f:
                            f.write(content)
                        ftp.posix_rename(path + ".perf-restore", path)
                cleanup["restoredConfigs"] = config_changes
                if production_dll_backup is not None and production_dll_backup.is_file():
                    ftp.put(str(production_dll_backup), dll_path + ".perf-restore")
                    ftp.posix_rename(dll_path + ".perf-restore", dll_path)
                    cleanup["restoredProductionDll"] = True
                final = inventory()
                (out / "inventory-after.json").write_text(json.dumps(final, indent=2), encoding="utf-8")
                cleanup["protectedFilesUnchanged"] = final == before
                cleanup["productionDllUnchanged"] = run("sha256sum '" + GAME + "/Mods/RGDSplus.VerticalWorld/VerticalWorld.dll'").split()[0] == dll_before
            if deployed and cleanup.get("qaStopped", pid is None):
                for name in ftp.listdir(MOD):
                    ftp.remove(MOD + "/" + name)
                ftp.rmdir(MOD)
                cleanup["probeRemoved"] = True
            cleanup["processesAfter"] = run("pgrep -af '[S]tardewModdingAPI|[d]otnet' || true")
        except Exception as ex:
            cleanup["error"] = str(ex)
            print("CLEANUP ERROR " + str(ex), flush=True)
        (out / "cleanup.json").write_text(json.dumps(cleanup, indent=2), encoding="utf-8")
        ftp.close()
        ssh.close()
        print("CLEANUP " + json.dumps(cleanup), flush=True)


if __name__ == "__main__":
    main()
