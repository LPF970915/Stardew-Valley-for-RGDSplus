"""Opt-in dialogue layout checks and physical screenshots using an in-memory farm."""
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
    output = ROOT / "build" / ("dialogue-layout-" + time.strftime("%Y%m%d-%H%M%S"))
    output.mkdir(parents=True, exist_ok=False)
    ssh = paramiko.SSHClient()
    ssh.load_system_host_keys()
    ssh.set_missing_host_key_policy(paramiko.RejectPolicy())
    ssh.connect("[device-host]", username="root", password=os.environ["RGDSPLUS_SSH_PASSWORD"], timeout=10)
    checks = []
    pid = None
    with ssh, ssh.open_sftp() as ftp:
        def request(action, **kwargs):
            return qa.request(ftp, action, **kwargs)

        def capture(label):
            frames = []
            for crtc, name in ((130, "upper"), (90, "lower")):
                _, stdout, stderr = ssh.exec_command("kmsgrab --crtc " + str(crtc), timeout=20)
                raw = stdout.read()
                error = stderr.read().decode(errors="replace")
                if stdout.channel.recv_exit_status() or len(raw) != 1024 * 768 * 4:
                    raise RuntimeError(error or "Incomplete physical capture")
                image = Image.frombytes("RGB", (1024, 768), raw, "raw", "BGRX")
                image.save(output / (label + "-" + name + ".png"))
                frames.append(image)
            combined = Image.new("RGB", (1024, 1536))
            combined.paste(frames[0])
            combined.paste(frames[1], (0, 768))
            combined.save(output / (label + "-dual.png"))

        active = qa.run(ssh, "pgrep -af '[S]tardewModdingAPI.dll' || true").strip()
        if active:
            raise RuntimeError("Refusing to interrupt an existing game: " + active)
        ftp.get(qa.GAME + "/log.txt", str(output / "previous-production.log"))
        qa.run(ssh, "mkdir -p " + qa.QA + "; rm -f " + qa.QA + "/command.json " + qa.QA + "/result.json")
        qa.run(ssh, "nohup env SDV_DUAL_UI_TEST_DIR=" + qa.QA +
               " /bin/bash '/mnt/sdcard/Ports/Stardew Valley Vertical World D2 Card2.sh'"
               " >/tmp/sdv-dialogue-qa-launch.log 2>&1 </dev/null &")
        try:
            request("status")
            pid = qa.run(ssh, "pgrep -f '[d]otnet StardewModdingAPI.dll'").strip()
            assert pid.isdigit(), "Expected exactly one QA game"
            environment = qa.run(ssh, "tr '\\000' '\\n' </proc/" + pid + "/environ")
            assert "SDV_DUAL_UI_TEST_DIR=" + qa.QA in environment
            request("fixture")
            time.sleep(8)
            request("present")
            for language in ("zh", "en"):
                request("language", code=language)
                for scale in (1.5, 1.25, 1.0, .75):
                    request("dialogue_dismiss")
                    request("scale", ui=scale, zoom=1)
                    for mode in ("ExpandedHud", "SceneHud"):
                        if request("status")["mode"] != mode:
                            request("toggle")
                        for kind in ("portrait", "plain", "list", "question"):
                            state = request("dialogue_probe", kind=kind)
                            box = state["dialogue"]
                            assert box and state["mode"] == mode
                            assert box["x"] - 44 >= 0
                            assert box["x"] + box["width"] + 40 <= state["uiWidth"]
                            assert box["y"] - (box["heightForQuestions"] - box["height"] if box["isQuestion"] else 0) - 28 >= 0
                            assert box["y"] + box["height"] + 40 <= state["uiHeight"]
                            assert abs(state["uiScale"] - scale) < .001
                            if kind == "portrait":
                                assert box["portrait"] and box["pages"] > 1, "Long NPC dialogue did not paginate"
                            label = f"{language}-{int(scale * 100):03}-{mode}-{kind}"
                            checks.append(dict(label=label, passed=True, state=state))
                            (output / "results.json").write_text(json.dumps(checks, ensure_ascii=False, indent=2), encoding="utf-8")
                            print("PASS " + label + " width=" + str(box["width"]), flush=True)
                            if scale == 1.5:
                                time.sleep(.4)
                                capture(label)
                            request("dialogue_dismiss")
            request("reset_ui")
            request("language", code="zh")
            if request("status")["mode"] != "ExpandedHud":
                request("toggle")
            request("dashboard")
            print("PASS: all dialogue cases; dashboard restored", flush=True)
        finally:
            if pid:
                # Let SDL/Wayland clean up inside the game loop, not a signal handler.
                if qa.run(ssh, "test ! -e /proc/" + pid + "/environ || tr '\\000' '\\n' </proc/" + pid + "/environ").find(
                        "SDV_DUAL_UI_TEST_DIR=" + qa.QA) >= 0:
                    request("fixture_quit")
                    for _ in range(30):
                        if qa.run(ssh, "test ! -d /proc/" + pid + " && echo stopped || true").strip():
                            break
                        time.sleep(.5)
                    else:
                        raise RuntimeError("QA game did not exit normally.")
            ftp.get(qa.GAME + "/log.txt", str(output / "device.log"))
            ftp.get(qa.GAME + "/Mods/RGDSplus.VerticalWorld/vertical-world.log", str(output / "plugin.log"))
    print("Evidence: " + str(output))


if __name__ == "__main__":
    main()
