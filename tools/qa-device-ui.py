"""Opt-in RGDS UI QA, using an in-memory farm and the real game renderer."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shlex
import time
import uuid

import paramiko

ROOT = Path(__file__).resolve().parents[1]
GAME = "/mnt/sdcard/Ports/StardewVerticalWorldD2/StardewVerticalWorldD2/StardewValleyMainline"
QA = "/tmp/sdv-ui-qa"
OUT = ROOT / "build/acceptance-reference-v2-device"


def run(ssh, command):
    _, stdout, stderr = ssh.exec_command(command, timeout=30)
    result = stdout.read().decode(errors="replace") + stderr.read().decode(errors="replace")
    if stdout.channel.recv_exit_status():
        raise RuntimeError(result)
    return result


def request(ftp, action, **kwargs):
    identifier = uuid.uuid4().hex
    with ftp.open(QA + "/command.json.next", "w") as stream:
        stream.write(json.dumps(dict(id=identifier, action=action, **kwargs)))
    ftp.posix_rename(QA + "/command.json.next", QA + "/command.json")
    end = time.monotonic() + 240
    while time.monotonic() < end:
        try:
            with ftp.open(QA + "/result.json") as stream:
                result = json.load(stream)
            if result["id"] == identifier:
                if result["error"]:
                    raise RuntimeError(result["error"])
                return result
        except (FileNotFoundError, OSError):
            pass
        time.sleep(.2)
    raise TimeoutError(action)


def deploy(ssh, ftp):
    mod = GAME + "/Mods/RGDSplus.VerticalWorld"
    backup = "/mnt/sdcard/Ports/.backup-stardew-reference-v2-" + time.strftime("%Y%m%d-%H%M%S")
    run(ssh, f"mkdir -p {shlex.quote(backup)}; cp -p {mod}/VerticalWorld.dll {mod}/manifest.json {shlex.quote(backup)}/")
    for name, local in [("VerticalWorld.dll", ROOT / "dualscreen/VerticalWorld/bin/Release/net6.0/VerticalWorld.dll"),
                        ("manifest.json", ROOT / "dualscreen/VerticalWorld/manifest.json")]:
        ftp.put(str(local), mod + "/" + name + ".next")
        digest = hashlib.sha256(local.read_bytes()).hexdigest()
        assert run(ssh, f"sha256sum {mod}/{name}.next").split()[0] == digest
        run(ssh, f"mv {mod}/{name}.next {mod}/{name}")
    print("Deployed; backup=" + backup, flush=True)


def launch(ssh, qa=True):
    # Stop only this game's old process. Never kill the system menu or unrelated ports.
    run(ssh, "pkill -TERM -f '[S]tardewModdingAPI.dll' || true")
    time.sleep(3)
    prefix = f"mkdir -p {QA}; rm -f {QA}/command.json {QA}/result.json; " if qa else ""
    environment = "SDV_DUAL_UI_TEST_DIR=" + QA if qa else "-u SDV_DUAL_UI_TEST_DIR"
    log = "/tmp/sdv-ui-qa-launch.log" if qa else "/tmp/sdv-ui-normal-launch.log"
    run(ssh, prefix + "nohup env " + environment +
        " /bin/bash '/mnt/sdcard/Ports/Stardew Valley Vertical World D2 Card2.sh' "
        ">" + log + " 2>&1 </dev/null &")
    print("QA launcher started" if qa else "Normal launcher started without QA", flush=True)


def extras(ftp):
    state = request(ftp, "status")
    assert state["worldReady"] and state["mode"] == "ExpandedHud"
    request(ftp, "dashboard")
    before = request(ftp, "status")
    request(ftp, "touch", x=250, y=215, pressed=True, released=False)
    time.sleep(.9)
    state = request(ftp, "touch", x=250, y=215, pressed=False, released=True)
    checks = [dict(name="hold food consumes exactly one without popup", passed=state["items"][0]["stack"] == before["items"][0]["stack"] - 1 and state["menu"] is None, state=state)]
    time.sleep(3)
    for attempt in range(3):
        state = request(ftp, "pause")
        checks.append(dict(name="repeated Start blocked " + str(attempt + 1), passed=state["menu"] is None, state=state))
    results = json.loads((OUT / "results.json").read_text(encoding="utf-8"))
    results.extend(checks)
    (OUT / "results.json").write_text(json.dumps(results, indent=2), encoding="utf-8")
    for check in checks:
        print(("PASS " if check["passed"] else "FAIL ") + check["name"], flush=True)
    assert all(check["passed"] for check in checks)


def suite(ssh, ftp):
    OUT.mkdir(parents=True, exist_ok=True)
    checks = []

    def check(name, condition, state):
        checks.append(dict(name=name, passed=bool(condition), state=state))
        print(("PASS " if condition else "FAIL ") + name, flush=True)
        if not condition:
            raise AssertionError(json.dumps(state))

    def shot(name):
        time.sleep(.6)
        state = request(ftp, "capture")
        path = OUT / (name + ".png")
        ftp.get(QA + "/" + state["screenshot"], str(path))
        from PIL import Image, ImageStat
        with Image.open(path) as image:
            check(name + " rendered 1024x768", image.size == (1024, 768) and max(ImageStat.Stat(image.convert("RGB")).stddev) > 10, state)

    def tap(x, y):
        state = request(ftp, "touch", x=x, y=y, pressed=True, released=False)
        return request(ftp, "touch", x=x, y=y, pressed=False, released=True)

    try:
        state = request(ftp, "status")
        if not state["worldReady"]:
            state = request(ftp, "fixture")
            time.sleep(8)
        end = time.monotonic() + 60
        while time.monotonic() < end:
            state = request(ftp, "status")
            if state["worldReady"] and state["menu"] is None: break
            time.sleep(1)
        check("in-memory world ready", state["worldReady"] and state["menu"] is None, state)
        check("both-mode UI baseline defaults to 150%", abs(state["uiScale"] - 1.5) < .001 and abs(state["preferredUiScale"] - 1.5) < .001, state)
        if state["mode"] != "ExpandedHud":
            request(ftp, "toggle")
        request(ftp, "language", code="zh")
        request(ftp, "dashboard")
        state = request(ftp, "pause")
        check("Start pause entry blocked in companion mode", state["menu"] is None, state)
        shot("today-zh")
        state = tap(224 + 5 * 53 + 25, 215)
        check("today inventory touch selects item", state["currentItem"] == 5, state)
        state = tap(224 + 2 * 64 + 30, 724)
        check("fixed bottom toolbar touch selects item", state["currentItem"] == 2, state)
        before = request(ftp, "status")
        request(ftp, "touch", x=250, y=215, pressed=True, released=False)
        request(ftp, "touch", x=833, y=272, pressed=False, released=False)
        state = request(ftp, "touch", x=833, y=272, pressed=False, released=True)
        check("inventory drag moves item without popup", state["items"][23] == before["items"][0] and state["items"][0] is None and state["menu"] is None, state)
        request(ftp, "touch", x=833, y=272, pressed=True, released=False)
        state = request(ftp, "touch", x=250, y=215, pressed=False, released=True)
        check("inventory drag restores exact stack", state["items"][0] == before["items"][0] and state["items"][23] is None, state)
        for page in range(12):
            request(ftp, "dashboard")
            state = tap(90, 101 + page * 47 + 21)
            check("page " + str(page) + " stays nonmodal", state["menu"] is None and state["page"] == page, state)
            shot("page-%02d-zh" % page)
            if page == 10:
                request(ftp, "touch", x=600, y=500, pressed=True, released=False)
                state = request(ftp, "touch", x=600, y=370, pressed=False, released=False)
                request(ftp, "touch", x=600, y=370, pressed=False, released=True)
                check("evdev drag scrolls recipes", state["scroll"] > 0, state)
            if page == 1:
                for section in range(5):
                    state = tap(222 + section * 153 + 74, 175)
                    check("farm subpage " + str(section), state["section"] == str(section) and state["menu"] is None, state)
                    shot("farm-%s-zh" % section)
                    if section == 0:
                        request(ftp, "touch", x=600, y=500, pressed=True, released=False)
                        state = request(ftp, "touch", x=600, y=370, pressed=False, released=False)
                        request(ftp, "touch", x=600, y=370, pressed=False, released=True)
                        check("machine list scrolls independently", state["scroll"] == 3, state)
                    elif section == 1:
                        check("animal list starts at its own offset", state["scroll"] == 0, state)
                state = tap(296, 175)
                check("machine list retains its offset", state["scroll"] == 3, state)
            if page == 7:
                state = tap(706, 247)
                check("calendar selects day inline", state["calendarDay"] == 5 and state["menu"] is None, state)
                check("calendar cart day appears in detail", "旅行货车" in state["calendarEvents"], state)
                state = tap(922, 247)
                check("calendar birthday and cart coexist", "生日" in state["calendarEvents"] and "旅行货车" in state["calendarEvents"], state)
                shot("calendar-multiple-events-zh")
            if page in (4, 8, 9, 10):
                tap(540, 238)
                shot("detail-%02d-zh" % page)
                if page == 4:
                    before = request(ftp, "status")
                    state = tap(850, 589)
                    check("quest reward claimed inline once", state["money"] == before["money"] + 500 and state["menu"] is None, state)
                tap(275, 175)
        tap(90, 101 + 4 * 47 + 21)
        tap(540, 238)
        before = request(ftp, "status")
        state = tap(850, 589)
        check("quest cancellation first tap confirms inline", state["quests"] == before["quests"], state)
        state = tap(850, 589)
        check("quest cancellation second tap applies", state["quests"] == before["quests"] - 1 and state["menu"] is None, state)
        before = request(ftp, "status")
        time.sleep(2)
        state = request(ftp, "status")
        check("update loop continues while lower page is open", state["ticks"] > before["ticks"] and state["menu"] is None, state)
        for language in ("en", "zh"):
            request(ftp, "language", code=language)
            for scale in (.75, 1, 1.25, 1.5):
                request(ftp, "dashboard")
                request(ftp, "scale", ui=scale, zoom=.75)
                shot("today-%s-ui-%s" % (language, scale))
                state = tap(90, 101 + 10 * 47 + 21)
                check("scaled nav " + language + str(scale), state["menu"] is None and state["page"] == 10, state)
                shot("recipes-%s-ui-%s" % (language, scale))
                tap(90, 101 + 7 * 47 + 21)
                shot("calendar-%s-ui-%s" % (language, scale))
        tap(90, 101 + 11 * 47 + 21)
        request(ftp, "scale", ui=1, zoom=1)
        state = tap(928, 240)
        check("world zoom plus works inline", abs(state["zoom"] - 1.05) < .001 and state["menu"] is None, state)
        state = tap(928, 310)
        check("UI scale plus works inline", abs(state["uiScale"] - 1.05) < .001 and state["menu"] is None, state)
        tap(600, 175)
        state = tap(850, 240)
        check("appearance changes inline", not state["nativeFrames"] and state["menu"] is None, state)
        shot("settings-simple-zh")
        tap(625, 240)
        tap(865, 175)
        state = tap(730, 240)
        check("page visibility hides farm inline", not state["visiblePages"][1] and state["menu"] is None, state)
        shot("settings-pages-zh")
        tap(730, 240)
        for cooking in (False, True):
            request(ftp, "prepare_recipe", cooking=cooking)
            tap(90, 101 + (10 if cooking else 9) * 47 + 21)
            tap(540, 238)
            before = request(ftp, "status")
            state = tap(850, 589)
            def count(snapshot, item_id, quality=None):
                return sum(item["stack"] for item in snapshot["items"] if item and item["id"] == item_id and (quality is None or item["quality"] == quality))
            if cooking:
                check("cooking consumes egg and seasoning once", count(state, "(O)176") == count(before, "(O)176") - 1 and count(state, "(O)917") == count(before, "(O)917") - 1 and count(state, "(O)194", 2) == count(before, "(O)194", 2) + 1 and state["cookedCounts"].get("194", 0) == before["cookedCounts"].get("194", 0) + 1 and state["menu"] is None, state)
            else:
                check("crafting consumes materials and records output once", count(state, "(O)388") == count(before, "(O)388") - 1 and count(state, "(O)92") == count(before, "(O)92") - 2 and count(state, "(O)93") == count(before, "(O)93") + 1 and state["craftCounts"].get("Torch") == 1 and state["menu"] is None, state)
            shot("crafted-cooking-" + str(cooking))
        request(ftp, "toggle")
        state = request(ftp, "pause")
        check("continuous dual-screen native Start menu preserved", state["mode"] == "SceneHud" and state["menu"] == "GameMenu", state)
        request(ftp, "close")
        time.sleep(.35)
        request(ftp, "toggle")
        request(ftp, "scale", ui=1, zoom=1)
        request(ftp, "language", code="zh")
        request(ftp, "dashboard")
        request(ftp, "reset_ui")
        shot("final-today-zh")
    finally:
        (OUT / "results.json").write_text(json.dumps(checks, indent=2), encoding="utf-8")
        for remote, local in [(GAME + "/Mods/RGDSplus.VerticalWorld/vertical-world.log", "device-plugin.log"),
                              ("/tmp/sdv-ui-qa-launch.log", "device-launch.log")]:
            ftp.get(remote, str(OUT / local))


def readability(ssh, ftp):
    from PIL import Image, ImageStat
    OUT.mkdir(parents=True, exist_ok=True)
    checks = []

    def check(name, condition, state):
        checks.append(dict(name=name, passed=bool(condition), state=state))
        print(("PASS " if condition else "FAIL ") + name, flush=True)
        assert condition, json.dumps(state)

    def tap(x, y):
        request(ftp, "touch", x=x, y=y, pressed=True, released=False)
        return request(ftp, "touch", x=x, y=y, pressed=False, released=True)

    def shot(name):
        time.sleep(.6)
        state = request(ftp, "capture")
        path = OUT / (name + ".png")
        ftp.get(QA + "/" + state["screenshot"], str(path))
        with Image.open(path) as image:
            check(name + " rendered", image.size == (1024, 768) and max(ImageStat.Stat(image.convert("RGB")).stddev) > 10, state)
        return path

    def green_bounds(path, toolbar=False):
        # Compare the parsnip's actual green sprite pixels, not merely hit-box math.
        with Image.open(path) as image:
            pixels = image.convert("RGB")
            left, top, width, height = (224, 695, 60, 60) if toolbar else (224, 189, 50, 52)
            found = [(x, y) for y in range(top, top + height) for x in range(left, left + width)
                     if (lambda c: c[1] > 65 and c[1] > c[0] * 1.25 and c[1] > c[2] * 1.2)(pixels.getpixel((x, y)))]
            assert found, str(path)
            return [min(x for x, y in found), min(y for x, y in found), max(x for x, y in found), max(y for x, y in found)]

    try:
        state = request(ftp, "status")
        check("readability starts at 150% in companion mode", state["mode"] == "ExpandedHud" and abs(state["uiScale"] - 1.5) < .001, state)
        request(ftp, "prepare_icons")
        request(ftp, "dashboard")
        reference = None
        for language in ("zh", "en"):
            request(ftp, "language", code=language)
            for scale in (1.5, 1.25, 1, .75):
                request(ftp, "scale", ui=scale, zoom=1)
                request(ftp, "dashboard")
                path = shot("icons-header-%s-ui-%s" % (language, scale))
                bounds = [green_bounds(path), green_bounds(path, toolbar=True)]
                if reference is None:
                    reference = bounds
                check("backpack and toolbar sprites stay aligned " + language + str(scale),
                      max(abs(a - b) for actual, expected in zip(bounds, reference) for a, b in zip(actual, expected)) <= 2,
                      dict(actual=bounds, reference=reference))
                state = tap(224 + 6 * 53 + 25, 215)
                check("tool slot hit follows sprite " + language + str(scale), state["currentItem"] == 6 and state["menu"] is None, state)
                tap(250, 215)
        # Exercise the user's real settings control, not just the QA scale setter.
        request(ftp, "reset_ui")
        tap(90, 101 + 11 * 47 + 21)
        state = tap(697, 310)
        check("settings minus saves 145% preference", abs(state["preferredUiScale"] - 1.45) < .001 and abs(state["uiScale"] - 1.45) < .001, state)
        tap(90, 122)
        shot("settings-changed-icons-en-145")
        request(ftp, "toggle")
        state = request(ftp, "status")
        check("continuous mode shares explicit UI preference", state["mode"] == "SceneHud" and abs(state["uiScale"] - 1.45) < .001, state)
        request(ftp, "reset_ui")
        state = request(ftp, "pause")
        check("native Start works at 150%", state["menu"] == "GameMenu" and abs(state["uiScale"] - 1.5) < .001, state)
        shot("scene-native-menu-en-150")
        request(ftp, "close")
        request(ftp, "settle_scene")
        time.sleep(1)
        state = request(ftp, "status")
        check("continuous native HUD is enabled at 150%", state["hudDrawn"] and "Toolbar" in state["hudMenus"], state)
        shot("scene-hud-en-150")
        request(ftp, "toggle")
        request(ftp, "language", code="zh")
        request(ftp, "dashboard")
        state = request(ftp, "pause")
        check("companion Start remains blocked at 150%", state["menu"] is None and abs(state["uiScale"] - 1.5) < .001, state)
        shot("final-reference-v2-zh-150")
    finally:
        (OUT / "readability-results.json").write_text(json.dumps(checks, indent=2), encoding="utf-8")
        ftp.get(GAME + "/Mods/RGDSplus.VerticalWorld/vertical-world.log", str(OUT / "device-plugin.log"))


def main():
    global OUT
    parser = argparse.ArgumentParser()
    parser.add_argument("action", choices=("deploy", "launch", "normal", "suite", "extras", "readability", "status"))
    parser.add_argument("--host", default=os.environ.get("RGDSPLUS_SSH_HOST", "rgdsplus.local"))
    parser.add_argument("--out", type=Path)
    args = parser.parse_args()
    if args.out:
        OUT = args.out.resolve()
    ssh = paramiko.SSHClient()
    ssh.set_missing_host_key_policy(paramiko.AutoAddPolicy())
    ssh.connect(args.host, username="root", password=os.environ["RGDSPLUS_SSH_PASSWORD"], timeout=10)
    try:
        with ssh.open_sftp() as ftp:
            if args.action == "deploy": deploy(ssh, ftp)
            elif args.action == "launch": launch(ssh)
            elif args.action == "normal": launch(ssh, qa=False)
            elif args.action == "suite": suite(ssh, ftp)
            elif args.action == "extras": extras(ftp)
            elif args.action == "readability": readability(ssh, ftp)
            else: print(json.dumps(request(ftp, "status"), ensure_ascii=False, indent=2))
    finally:
        ssh.close()


if __name__ == "__main__":
    main()
