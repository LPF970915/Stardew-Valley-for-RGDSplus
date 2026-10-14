"""Recover evidence and remove only a verified, owned in-memory QA run."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shlex
import stat
import time

import paramiko


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("directory", type=Path)
    args = parser.parse_args()
    out = args.directory.resolve()
    previous = json.loads((out / "cleanup.json").read_text(encoding="utf-8"))
    remote, pid = previous["remote"], previous["pid"]
    assert remote == "/tmp/sdv-perf-" + out.name.removeprefix("performance-")
    game = "/mnt/sdcard/Ports/StardewVerticalWorldD2/StardewVerticalWorldD2/StardewValleyMainline"
    mod = game + "/Mods/RGDSplus.PerformanceProbe"
    ssh = paramiko.SSHClient()
    ssh.load_system_host_keys()
    ssh.set_missing_host_key_policy(paramiko.RejectPolicy())
    ssh.connect(os.environ.get("RGDSPLUS_SSH_HOST", "rgdsplus.local"), username="root",
                password=os.environ["RGDSPLUS_SSH_PASSWORD"], timeout=20)
    ftp = ssh.open_sftp()
    ftp.get_channel().settimeout(120)

    def run(command):
        _, stdout, stderr = ssh.exec_command(command, timeout=120)
        value = stdout.read().decode(errors="replace") + stderr.read().decode(errors="replace")
        if stdout.channel.recv_exit_status():
            raise RuntimeError(value)
        return value

    def owned():
        try:
            with ftp.open(f"/proc/{pid}/environ", "rb") as stream:
                return ("SDV_DUAL_PERF_TEST_DIR=" + remote).encode() in stream.read().split(b"\0")
        except OSError:
            return False

    def alive():
        return bool(run(f"test -r /proc/{pid}/status && echo alive || true").strip())

    cleanup = {**previous, "initialCleanupError": repr(previous.get("error")), "recovery": True}
    try:
        if alive():
            if not owned():
                raise RuntimeError("PID ownership changed; refusing to stop it.")
            snapshot = run(f"date -u; free -m; cat /proc/{pid}/status; cat /proc/vmstat; ps -eo pid,comm,rss,args --sort=-rss | head -15")
            (out / "recovery-before.txt").write_text(snapshot, encoding="utf-8")
            run(f"kill -TERM {pid}")
            for _ in range(30):
                if not alive():
                    break
                time.sleep(1)
            if alive():
                if not owned():
                    raise RuntimeError("PID ownership changed after TERM.")
                print("TERM did not stop owned fixture; using bounded KILL fallback", flush=True)
                run(f"kill -KILL {pid}")
                cleanup["ownedFixtureForceStopped"] = True
                time.sleep(3)
        cleanup["qaStopped"] = not alive()
        if not cleanup["qaStopped"]:
            raise RuntimeError("Owned QA is still alive.")
        for entry in ftp.listdir_attr(remote):
            if stat.S_ISREG(entry.st_mode):
                ftp.get(remote + "/" + entry.filename, str(out / entry.filename))
        ftp.get(game + "/Mods/RGDSplus.VerticalWorld/frame-metrics.csv", str(out / "production-frame-metrics.csv"))
        before = json.loads((out / "inventory-before.json").read_text(encoding="utf-8"))
        after = {}
        for path in before:
            with ftp.open(path, "rb") as stream:
                after[path] = hashlib.sha256(stream.read()).hexdigest()
        (out / "inventory-after.json").write_text(json.dumps(after, indent=2), encoding="utf-8")
        cleanup["protectedFilesUnchanged"] = before == after
        production = game + "/Mods/RGDSplus.VerticalWorld/VerticalWorld.dll"
        with ftp.open(production, "rb") as stream:
            cleanup["productionDllUnchanged"] = hashlib.sha256(stream.read()).hexdigest() == json.loads((out / "measurement.json").read_text(encoding="utf-8"))["productionDllSha256"]
        try:
            for entry in ftp.listdir_attr(mod):
                if not stat.S_ISREG(entry.st_mode) or entry.filename not in ("PerformanceProbe.dll", "manifest.json"):
                    raise RuntimeError("Unexpected Probe content; refusing deletion.")
                ftp.remove(mod + "/" + entry.filename)
            ftp.rmdir(mod)
        except FileNotFoundError:
            pass
        cleanup["probeRemoved"] = True
        cleanup["processesAfter"] = run("pgrep -af '[S]tardewModdingAPI|[d]otnet' || true")
        cleanup["audioAfter"] = run("amixer sget Master")
        cleanup["error"] = None
    except Exception as exc:
        cleanup["error"] = repr(exc)
        raise
    finally:
        (out / "cleanup.json").write_text(json.dumps(cleanup, indent=2), encoding="utf-8")
        ftp.close()
        ssh.close()
        print(json.dumps(cleanup), flush=True)


if __name__ == "__main__":
    main()
