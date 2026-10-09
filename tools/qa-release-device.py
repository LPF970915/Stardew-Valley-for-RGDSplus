"""Test an extracted release with legal local Steam assets, isolated HOME and saves."""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import shlex
import sys
import tarfile
import time
import uuid
import zipfile

import paramiko
from PIL import Image, ImageStat

ROOT = Path(__file__).resolve().parents[1]


def load(name, file):
    spec = importlib.util.spec_from_file_location(name, ROOT / "tools" / file)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("archive", type=Path)
    parser.add_argument("--assets", type=Path, required=True)
    parser.add_argument("--host", default="[device-host]")
    parser.add_argument("--reuse-test-directory",
                        help="Reuse only a previous isolated QA directory; reapply its raw input archive.")
    parser.add_argument("--device-content", default="/mnt/sdcard/Ports/StardewVerticalWorldD2/StardewVerticalWorldD2/StardewValleyMainline/gamedata/Content",
                        help="Existing legally imported Content, verified against local assets before copying.")
    args = parser.parse_args()
    release = load("release", "build-release.py")
    audit = release.verify_archive(args.archive)
    out = ROOT / "build" / ("release-device-" + time.strftime("%Y%m%d-%H%M%S"))
    out.mkdir()
    suffix = uuid.uuid4().hex[:12]
    remote = args.reuse_test_directory or "/mnt/sdcard/Ports/.sdv-release-qa-" + suffix
    if not re.fullmatch(r"/mnt/sdcard/Ports/\.sdv-release-qa-[0-9a-f]{12}", remote):
        raise ValueError("Not an isolated QA directory")
    game = remote + "/" + release.APP.rstrip("/")
    qa = load("device_ui", "qa-device-ui.py")
    qa.GAME = game
    qa.QA = remote + "/commands"
    ref = load("reference", "qa-reference-v2.py")
    ref.qa = qa
    ref.OUT = out / "reference"
    result = {"archive": audit, "remote": remote, "tests": []}
    payload = out / "private-test-input.tar.gz"
    with tarfile.open(payload, "w:gz") as tar, zipfile.ZipFile(args.archive) as archive:
        import io
        for info in archive.infolist():
            entry = tarfile.TarInfo(info.filename.rstrip("/"))
            entry.mode = (info.external_attr >> 16) & 0o777
            if info.is_dir():
                entry.type = tarfile.DIRTYPE
                tar.addfile(entry)
            else:
                data = archive.read(info.filename)
                entry.size = len(data)
                tar.addfile(entry, io.BytesIO(data))
        def exclude_content(entry):
            if entry.name.rstrip("/") == release.APP + "gamedata/Content":
                return None
            return entry
        tar.add(args.assets.resolve(), arcname=release.APP + "gamedata", filter=exclude_content)
    ssh = paramiko.SSHClient()
    ssh.load_system_host_keys()
    ssh.set_missing_host_key_policy(paramiko.RejectPolicy())
    ssh.connect(args.host, username="root", password=os.environ["RGDSPLUS_SSH_PASSWORD"], timeout=15)
    game_pid = None
    protected_before = None
    muted = False
    with ssh, ssh.open_sftp() as ftp:
        def run(command):
            _, stdout, stderr = ssh.exec_command(command, timeout=300)
            text = stdout.read().decode(errors="replace") + stderr.read().decode(errors="replace")
            if stdout.channel.recv_exit_status():
                raise RuntimeError(text)
            return text

        def check(name, condition):
            result["tests"].append({"name": name, "passed": bool(condition)})
            (out / "results.json").write_text(json.dumps(result, indent=2), encoding="utf-8")
            print(("PASS " if condition else "FAIL ") + name, flush=True)
            if not condition:
                raise AssertionError(name)

        def protected():
            # Ignore only this game's newly generated diagnostics; hash player state
            # and launchers in every pre-existing Stardew deployment.
            command = (
                "find /mnt/sdcard/Ports/Stardew* /root/.config/StardewValley "
                "-type f \\( -path '*/savedata/*' -o -path '*/saves/*' "
                "-o -path '/root/.config/StardewValley/*' -o -name config.json "
                "-o -name companion-history.json -o -name VerticalWorld.dll "
                "-o -name '*.sh' \\) -print0 2>/dev/null | sort -z | xargs -0 -r sha256sum"
            )
            return run(command)

        def request(action, **kwargs):
            return qa.request(ftp, action, **kwargs)

        def capture(label, settle=0):
            deadline = time.monotonic() + settle
            while True:
                frames = {}
                for crtc, panel in ((130, "upper"), (90, "lower")):
                    _, stdout, stderr = ssh.exec_command("kmsgrab --crtc " + str(crtc), timeout=30)
                    data = stdout.read()
                    error = stderr.read().decode(errors="replace")
                    if stdout.channel.recv_exit_status() or len(data) != 1024 * 768 * 4:
                        raise RuntimeError(error or "Incomplete physical capture")
                    frames[panel] = Image.frombytes("RGB", (1024, 768), data, "raw", "BGRX")
                if all(max(ImageStat.Stat(image).stddev) > 10 for image in frames.values()):
                    break
                if time.monotonic() >= deadline:
                    for panel, image in frames.items():
                        image.save(out / (label + "-" + panel + ".png"))
                    raise AssertionError(label + " did not leave a blank transition frame")
                time.sleep(2)
            for panel, image in frames.items():
                image.save(out / (label + "-" + panel + ".png"))
                check(label + " " + panel + " complete nonblank capture", True)

        check("no pre-existing game interrupted",
              not run("pgrep -af '[d]otnet .*StardewModdingAPI.dll|[d]otnet .*Stardew Valley.dll' || true").strip())
        protected_before = protected()
        result["protectedBeforeSha256"] = hashlib.sha256(protected_before.encode()).hexdigest()
        result["audioBefore"] = run("amixer sget Master; pactl get-sink-mute @DEFAULT_SINK@")
        try:
            run("amixer -q sset Master mute; pactl set-sink-mute @DEFAULT_SINK@ 1")
            muted = True
            audio = run("amixer sget Master; pactl get-sink-mute @DEFAULT_SINK@")
            check("test output muted", "[on]" not in audio and "Mute: yes" in audio)
            run("mkdir -p " + shlex.quote(remote))
            content = args.assets.resolve() / "Content"
            expected_content = {}
            for path in sorted(content.rglob("*")):
                if path.is_file():
                    with path.open("rb") as stream:
                        expected_content[path.relative_to(content).as_posix()] = hashlib.file_digest(stream, "sha256").hexdigest()
            hash_script = (
                "import hashlib,json,pathlib,sys\np=pathlib.Path(sys.argv[1]); result={}\n"
                "for f in sorted(p.rglob('*')):\n"
                " if f.is_file():\n"
                "  h=hashlib.sha256()\n"
                "  with f.open('rb') as s:\n"
                "   for block in iter(lambda:s.read(1048576),b''): h.update(block)\n"
                "  result[f.relative_to(p).as_posix()]=h.hexdigest()\n"
                "print(json.dumps(result))\n"
            )
            remote_content = json.loads(run("python3 - " + shlex.quote(args.device_content) + " <<'PY'\n" + hash_script + "PY"))
            # The device may retain optional language/season assets from an
            # older legal import. Every current local Steam asset must match;
            # extra files are harmless and are left outside the release package.
            check("existing device Content contains matching legal Steam import",
                  all(remote_content.get(name) == digest for name, digest in expected_content.items()))
            with ftp.open(remote + "/content-manifest.json", "w") as stream:
                stream.write(json.dumps(expected_content))
            # Avoid unbounded SFTP upload pipelining, which can deadlock older servers.
            if not args.reuse_test_directory:
                with payload.open("rb") as source, ftp.open(remote + "/input.tar.gz", "wb") as target:
                    target.set_pipelined(False)
                    transferred = 0
                    next_report = 5 * 1024 * 1024
                    while block := source.read(128 * 1024):
                        target.write(block)
                        transferred += len(block)
                        if transferred >= next_report:
                            print(f"UPLOAD {transferred // (1024 * 1024)} / {payload.stat().st_size // (1024 * 1024)} MiB", flush=True)
                            next_report += 5 * 1024 * 1024
                check("uploaded private input hash",
                      run("sha256sum " + remote + "/input.tar.gz").split()[0] == hashlib.sha256(payload.read_bytes()).hexdigest())
            run("tar -xzf " + remote + "/input.tar.gz -C " + remote)
            copy_script = (
                "import json,pathlib,shutil,sys\n"
                "src,dst=map(pathlib.Path,sys.argv[1:3]); names=json.load(open(sys.argv[3]))\n"
                "for name in names:\n"
                " p=dst/name; p.parent.mkdir(parents=True,exist_ok=True); shutil.copy2(src/name,p)\n"
            )
            if not args.reuse_test_directory:
                run("python3 - " + shlex.quote(args.device_content) + " " + shlex.quote(game + "/gamedata/Content")
                    + " " + remote + "/content-manifest.json <<'PY'\n" + copy_script + "PY")
            copied_content = json.loads(run("python3 - " + shlex.quote(game + "/gamedata/Content") + " <<'PY'\n" + hash_script + "PY"))
            check("isolated Content copy hashes verified", copied_content == expected_content)
            ftp.put(str(ROOT / "tools/native-lz4/test-lz4.py"), remote + "/test-lz4.py")
            native_result = run("python3 " + remote + "/test-lz4.py " + game + "/libs.aarch64/liblwjgl_lz4.so")
            check("bundled ARM64 LZ4 ABI and block roundtrips", "PASS:" in native_result)
            run("mkdir -p " + remote + "/home/.config " + qa.QA)
            run("rm -f " + qa.QA + "/command.json " + qa.QA + "/result.json")
            # Reject collisions in the isolated HOME; no user save binding is used.
            entry = remote + "/" + release.ENTRY
            command = ("nohup env HOME=" + remote + "/home XDG_CONFIG_HOME=" + remote +
                       "/home/.config XDG_DATA_HOME=/root/.local/share SDV_DUAL_UI_TEST_DIR=" + qa.QA +
                       " /bin/bash " + shlex.quote(entry) + " >" + remote +
                       "/launch.log 2>&1 </dev/null &")
            run(command)
            state = request("status")
            game_pid = run("pgrep -f " + shlex.quote("[d]otnet StardewModdingAPI.dll")).strip()
            check("one isolated game process", game_pid.isdigit())
            environment = run("tr '\\000' '\\n' </proc/" + game_pid + "/environ")
            check("release process uses isolated HOME and QA",
                  "HOME=" + remote + "/home" in environment and "SDV_DUAL_UI_TEST_DIR=" + qa.QA in environment)
            request("present")
            time.sleep(2)
            capture("title", settle=120)
            # Fetch the exact newly patched game assembly for geometry/IL checks.
            ftp.get(game + "/gamedata/Stardew Valley.dll", str(out / "device-game.dll"))
            run("python3 - " + shlex.quote(game) + " <<'PY'\n"
                "import pathlib,sys\n"
                "p=pathlib.Path(sys.argv[1]); assert (p/'gamedata/StardewModdingAPI.dll').is_file()\n"
                "assert (p/'dotnet/dotnet').is_file()\n"
                "print('fresh PortMaster patcher and SMAPI synchronization passed')\nPY")
            check("release remains muted after entry", "Mute: yes" in run("pactl get-sink-mute @DEFAULT_SINK@"))
            ref.OUT.mkdir()
            previous = sys.argv
            try:
                sys.argv = ["qa-reference-v2.py", "suite"]
                ref.main()
            finally:
                sys.argv = previous
            reference = json.loads((ref.OUT / "results.json").read_text(encoding="utf-8"))
            check("full reference UI suite", reference and all(item["passed"] for item in reference))
            result["referenceChecks"] = len(reference)
            dialogue = []
            for language in ("zh", "en"):
                request("language", code=language)
                for scale in (.75, 1, 1.25, 1.5):
                    request("scale", ui=scale, zoom=1)
                    for mode in ("SceneHud", "ExpandedHud"):
                        if request("status")["mode"] != mode:
                            request("toggle")
                        for kind in ("portrait", "plain", "list", "question"):
                            state = request("dialogue_probe", kind=kind)
                            box = state["dialogue"]
                            label = f"{language}-{scale}-{mode}-{kind}"
                            valid = (box and box["x"] - 44 >= 0
                                     and box["x"] + box["width"] + 40 <= state["uiWidth"]
                                     and box["y"] - (box["heightForQuestions"] - box["height"]
                                         if box["isQuestion"] else 0) - 28 >= 0
                                     and box["y"] + box["height"] + 40 <= state["uiHeight"])
                            if kind == "portrait":
                                valid = valid and box["portrait"] and box["pages"] > 1
                            check("dialogue " + label, valid)
                            dialogue.append({"label": label, "state": state})
                            if language == "zh" and scale == 1.5 and kind in ("portrait", "question"):
                                capture("dialogue-" + mode + "-" + kind)
                            request("dialogue_dismiss")
            (out / "dialogue-results.json").write_text(json.dumps(dialogue, ensure_ascii=False, indent=2), encoding="utf-8")
            request("reset_ui")
            request("language", code="zh")
            if request("status")["mode"] != "ExpandedHud":
                request("toggle")
            request("dashboard")
            capture("dashboard")
            result["dialogueChecks"] = len(dialogue)
            request("fixture_quit")
            for _ in range(60):
                if run("test ! -d /proc/" + game_pid + " && echo stopped || true").strip():
                    break
                time.sleep(.5)
            else:
                raise RuntimeError("QA game did not exit normally")
            check("isolated game exited", True)
            time.sleep(3)
            log = run("cat " + shlex.quote(game + "/log.txt"))
            check("normal game exit status zero", "Stardew Valley exited with status 0." in log)
            check("no farm saves created", not run("find " + game + "/savedata -type f -name SaveGameInfo -print").strip())
            result["completed"] = True
        finally:
            try:
                if not game_pid:
                    for candidate in run("pgrep -f '[d]otnet StardewModdingAPI.dll' || true").split():
                        environment = run("tr '\\000' '\\n' </proc/" + candidate + "/environ")
                        if "SDV_DUAL_UI_TEST_DIR=" + qa.QA in environment:
                            game_pid = candidate
                if game_pid and run("test -d /proc/" + game_pid + " && echo running || true").strip():
                    environment = run("tr '\\000' '\\n' </proc/" + game_pid + "/environ")
                    if "SDV_DUAL_UI_TEST_DIR=" + qa.QA in environment:
                        if not request("status")["worldReady"]:
                            request("fixture")
                        request("fixture_quit")
                        for _ in range(60):
                            if run("test ! -d /proc/" + game_pid + " && echo stopped || true").strip():
                                break
                            time.sleep(.5)
                        else:
                            raise RuntimeError("Cannot clean up isolated QA game normally")
                for remote_file, local in ((game + "/log.txt", "device.log"),
                                           (game + "/Mods/RGDSplus.VerticalWorld/vertical-world.log", "plugin.log"),
                                           (remote + "/launch.log", "launch.log")):
                    try:
                        ftp.get(remote_file, str(out / local))
                    except OSError:
                        pass
                check("existing production files unchanged", protected() == protected_before)
                check("no game left running",
                      not run("pgrep -af '[d]otnet .*StardewModdingAPI.dll|[d]otnet .*Stardew Valley.dll' || true").strip())
            finally:
                if muted:
                    run("amixer -q sset Master unmute; pactl set-sink-mute @DEFAULT_SINK@ 0")
                    audio = run("amixer sget Master; pactl get-sink-mute @DEFAULT_SINK@")
                    result["audioAfter"] = audio
                    check("device output unmuted after tests", "[off]" not in audio and "Mute: no" in audio)
                (out / "results.json").write_text(json.dumps(result, indent=2), encoding="utf-8")
    print("EVIDENCE " + str(out), flush=True)


if __name__ == "__main__":
    main()
