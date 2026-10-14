"""Visible reference-v2 walkthrough, recording both physical DRM outputs."""
import argparse
import importlib.util
import json
import os
from pathlib import Path
import time

import paramiko
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("qa", ROOT / "tools/qa-device-ui.py")
qa = importlib.util.module_from_spec(spec)
spec.loader.exec_module(qa)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--prepare", action="store_true")
    parser.add_argument("--hold", type=float, default=5)
    args = parser.parse_args()
    out = ROOT / "build/acceptance-visible-ui"
    out.mkdir(parents=True, exist_ok=True)
    ssh = paramiko.SSHClient()
    ssh.set_missing_host_key_policy(paramiko.AutoAddPolicy())
    ssh.connect(os.environ.get("RGDSPLUS_SSH_HOST", "rgdsplus.local"), username="root",
                password=os.environ["RGDSPLUS_SSH_PASSWORD"], timeout=10)
    results = []
    with ssh, ssh.open_sftp() as ftp:
        def request(action, **kw):
            return qa.request(ftp, action, **kw)

        def tap(x, y):
            request("touch", x=x, y=y, pressed=True, released=False)
            return request("touch", x=x, y=y, pressed=False, released=True)

        def page(target):
            state = request("status")
            for _ in range(13):
                if state["page"] == target:
                    assert state["menu"] is None
                    return state
                state = tap(976, 46)
            raise AssertionError("Navigation failed: " + str(target))

        def physical(name):
            frames = []
            for crtc, label in ((130, "upper"), (90, "lower")):
                stdin, stdout, stderr = ssh.exec_command("kmsgrab --crtc " + str(crtc), timeout=15)
                raw = stdout.read()
                diagnostic = stderr.read().decode(errors="replace")
                if stdout.channel.recv_exit_status() or len(raw) != 1024 * 768 * 4:
                    raise RuntimeError("Physical capture failed: " + diagnostic)
                frame = Image.frombytes("RGB", (1024, 768), raw, "raw", "BGRX")
                frame.save(out / (name + "-" + label + ".png"))
                frames.append(frame)
            combined = Image.new("RGB", (1024, 1536))
            combined.paste(frames[0], (0, 0))
            combined.paste(frames[1], (0, 768))
            combined.save(out / (name + "-dual.png"))

        state = request("status")
        if not state["worldReady"]:
            request("fixture")
            time.sleep(8)
        request("present")
        if request("status")["mode"] != "ExpandedHud":
            request("toggle")
        request("language", code="zh")
        request("reset_ui")
        request("settle_scene")
        request("dashboard")
        if args.prepare:
            time.sleep(2)
            physical("ready")
            print("READY: physical dual-screen capture saved", flush=True)
            return

        try:
            for language in ("zh", "en"):
                request("language", code=language)
                for scale in (1.5, 1.25, 1.0, .75):
                    request("scale", ui=scale, zoom=1)
                    request("dashboard")
                    for target in range(12):
                        state = page(target)
                        label = "%s-%03d-page-%02d" % (language, round(scale * 100), target)
                        print("SHOW " + label, flush=True)
                        time.sleep(args.hold)
                        physical(label)
                        state = request("status")
                        passed = state["page"] == target and state["menu"] is None and state["language"] == language and abs(state["uiScale"] - scale) < .001
                        results.append(dict(label=label, passed=passed, page=state["page"], ticks=state["ticks"]))
                        (out / "results.json").write_text(json.dumps(results, indent=2), encoding="utf-8")
                        if not passed:
                            raise AssertionError(label)
            print("PASS 96 physical dual-screen page/language/scale combinations", flush=True)
        finally:
            request("close")
            if request("status")["mode"] != "ExpandedHud":
                request("toggle")
            request("language", code="zh")
            request("reset_ui")
            request("dashboard")
            physical("final-zh-150")


if __name__ == "__main__":
    main()
