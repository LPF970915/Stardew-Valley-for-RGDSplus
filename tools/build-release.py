"""Build and verify the game-free RGDSplus release using explicit runtime inputs."""
import argparse
import hashlib
import json
from pathlib import Path, PurePosixPath
import re
import subprocess
import zipfile

ROOT = Path(__file__).resolve().parents[1]
BUILD = "20261012-04"
APP = "Ports/Stardew Valley for RGDSplus/"
ENTRY = "Ports/Stardew Valley for RGDSplus.sh"
MANIFEST = APP + "RELEASE.json"
CHECKSUMS = APP + "SHA256SUMS.txt"
RUNTIME_DIRS = ("dlls", "dotnet", "gl4es.aarch64", "libs.aarch64",
                "licenses", "overrides", "tools")
EMPTY_DIRS = ("Ports/", APP, APP + "gamedata/",
              APP + "savedata/")
SENSITIVE = (rb"github_pat_[A-Za-z0-9_]{20,}", rb"gh[pousr]_[A-Za-z0-9]{20,}",
             rb"-----BEGIN (?:OPENSSH|RSA|EC|DSA) PRIVATE KEY-----",
             rb"steamLoginSecure", rb"RGDSPLUS_SSH_PASSWORD\s*=")


def digest(data):
    return hashlib.sha256(data).hexdigest()


def validate_name(name):
    path = PurePosixPath(name)
    if not (name == ENTRY or name.startswith(APP)):
        raise ValueError("Unexpected release root: " + name)
    if (path.is_absolute() or "\\" in name or ":" in name
            or any(p in ("", ".", "..") for p in name.split("/"))):
        raise ValueError("Unsafe archive path: " + name)
    if name in {APP + "overrides/gamedata/MonoGame.Framework.dll",
                APP + "overrides/gamedata/MonoGame.Framework.xml"}:
        return
    if any(p.lower() in {"gamedata", "savedata", "logs", "__pycache__", "bin",
                         "obj", "cache", "private"} for p in path.parts):
        raise ValueError("Game/player/generated data in release: " + name)
    if (path.suffix.lower() in {".pdb", ".log", ".pyc", ".bak", ".next", ".key",
                              ".pem", ".xnb", ".xwb", ".exe"}
            or path.name.lower() in {"stardew valley.dll", "stardewvalley.gamedata.dll",
                                    "profiling.enabled"}
            or (path.name == "config.json"
                and name != APP + "tools/SMAPIBundle/game-root/smapi-internal/config.json")
            or path.name.startswith("frame-metrics")):
        raise ValueError("Forbidden payload: " + name)


def executable(name, data):
    return name.endswith(".sh") or PurePosixPath(name).name in {
        "dotnet", "smapi-common"
    } or data.startswith(b"\x7fELF")


def collect(root=ROOT):
    root = Path(root)
    files = {}

    def add(name, source):
        validate_name(name)
        if source.is_symlink():
            raise ValueError("Symlink source: " + str(source))
        data = source.read_bytes()
        if source.suffix.lower() in {".sh", ".ps1", ".py", ".json", ".md", ".txt", ".cmd"}:
            if any(re.search(pattern, data) for pattern in SENSITIVE):
                raise ValueError("Sensitive source: " + str(source))
            if source.suffix.lower() != ".cmd":
                data = data.replace(b"\r\n", b"\n")
        if name in files:
            raise ValueError("Duplicate path: " + name)
        files[name] = data

    port = root / "port/stardewvalleymainline"
    # Only tracked dependency inputs can be shipped, never local runtime state.
    tracked = subprocess.check_output(
        ["git", "ls-files", "-z", "port/stardewvalleymainline"], cwd=root
    ).decode().split("\0")
    for relative in tracked:
        if not relative:
            continue
        path = Path(relative)
        within = path.relative_to("port/stardewvalleymainline")
        if within.parts[0] in RUNTIME_DIRS:
            add(APP + within.as_posix(), root / path)
    for name in ("cover.png", "screenshot.png", "gameinfo.xml", "port.json", "stardewvalleymainline.md"):
        add(APP + name, port / name)
    add(APP + "StardewValleyMainline.sh", root / "port/StardewValleyMainline.sh")
    # Explicit required input also covers a new helper before its first commit.
    if APP + "tools/rgds-audio.py" not in files:
        add(APP + "tools/rgds-audio.py", port / "tools/rgds-audio.py")
    if APP + "tools/rgds-mod-state.py" not in files:
        add(APP + "tools/rgds-mod-state.py", port / "tools/rgds-mod-state.py")
    mod = root / "dualscreen/VerticalWorld"
    add(APP + "Mods/RGDSplus.VerticalWorld/VerticalWorld.dll", mod / "bin/Release/net6.0/VerticalWorld.dll")
    add(APP + "Mods/RGDSplus.VerticalWorld/manifest.json", mod / "manifest.json")
    add(APP + "Mods/SMAPI-config.json", mod / "SMAPI-config.json")
    add(ENTRY, root / "packaging/Stardew Valley for RGDSplus.sh")
    for name in ("README.zh-CN.md", "THIRD_PARTY_NOTICES.md", "upstream.json"):
        add(APP + name, root / "packaging" / name)
    add(APP + "steam-import/Import Steam Assets.cmd", root / "packaging/Import Steam Assets.cmd")
    add(APP + "steam-import/Import Steam Assets.ps1", root / "tools/prepare-steam-assets.ps1")
    upstream = json.loads(files[APP + "upstream.json"])
    if digest(files[APP + "libs.aarch64/liblwjgl_lz4.so"]) != upstream["lz4"]["sha256"]:
        raise ValueError("LZ4 library differs from verified baseline")
    release = {
        "build": BUILD,
        "version": json.loads(files[APP + "Mods/RGDSplus.VerticalWorld/manifest.json"])["Version"],
        "sourceCommit": subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=root).decode().strip(),
        "entrypoint": ENTRY,
        "payload": APP.rstrip("/"),
        "modSha256": digest(files[APP + "Mods/RGDSplus.VerticalWorld/VerticalWorld.dll"]),
        "gameAssetsIncluded": False,
        "savesIncluded": False,
        "testsEnabled": False,
        "audio": True,
        "runtimeNetworkRequired": False,
        "physicalBuffer": "2048x768",
        "worldRenderScale": 1,
        "worldUpscaler": "none",
        "targetDrawRate": 30,
        "fixedUpdateRate": 60,
        "verticalSync": True,
        "firstUseWarmup": True,
        "performanceValidation": "first-use-passed-long-run-unresolved-manual-test-requested",
        "worldAlphaDiscard": True,
        "treeTransparentMarginTrim": True,
        "upperHalf": "left",
        "lowerHalf": "right",
        "logicalViewport": "1024x768",
        "defaultMode": "SceneHud",
        "defaultUiScale": 1.5,
        "requiredGame": upstream["testedSteamBuild"],
        "firmwareRequirements": ["PortMaster controls and input helpers", "Python 3",
                                 "Wayland", "PulseAudio", "RGDSplus ARM64 drivers"],
        "upstream": upstream,
        "files": {name: digest(data) for name, data in sorted(files.items())},
    }
    files[MANIFEST] = (json.dumps(release, ensure_ascii=False, indent=2) + "\n").encode()
    files[CHECKSUMS] = "".join(f"{digest(data)}  {name}\n" for name, data in sorted(files.items())).encode("ascii")
    return files


def verify_archive(path):
    with zipfile.ZipFile(path) as archive:
        names = archive.namelist()
        if len(names) != len(set(names)) or archive.testzip():
            raise ValueError("Duplicate names or CRC failure")
        directories = {n for n in names if n.endswith("/")}
        if directories != set(EMPTY_DIRS):
            raise ValueError("Unexpected directory entries")
        data = {n: archive.read(n) for n in names if not n.endswith("/")}
        for name, content in data.items():
            validate_name(name)
            mode = archive.getinfo(name).external_attr >> 16
            if executable(name, content) and mode & 0o111 != 0o111:
                raise ValueError("Missing executable permissions: " + name)
            if name.endswith(".sh") and (b"\r" in content or not content.startswith(b"#!")):
                raise ValueError("Invalid shell encoding: " + name)
        checksums = dict(line.split("  ", 1)[::-1]
                         for line in data[CHECKSUMS].decode("ascii").splitlines())
        if set(checksums) != set(data) - {CHECKSUMS}:
            raise ValueError("Incomplete checksum coverage")
        for name, expected in checksums.items():
            if digest(data[name]) != expected:
                raise ValueError("Checksum mismatch: " + name)
        release = json.loads(data[MANIFEST])
        if (release["gameAssetsIncluded"] or release["savesIncluded"]
                or release["testsEnabled"] or not release["audio"]
                or release["runtimeNetworkRequired"] or release["entrypoint"] != ENTRY):
            raise ValueError("Invalid release defaults")
        if set(release["files"]) != set(data) - {MANIFEST, CHECKSUMS}:
            raise ValueError("Invalid manifest membership")
        for name, expected in release["files"].items():
            if digest(data[name]) != expected:
                raise ValueError("Manifest hash mismatch: " + name)
        required = ("dotnet/dotnet", "dotnet/shared/Microsoft.NETCore.App/6.0.32/libcoreclr.so",
                    "tools/MainlineGameDataPatcher/MainlineGameDataPatcher.dll",
                    "tools/SMAPIBundle/game-root/StardewModdingAPI.dll",
                    "tools/rgds-session.py", "tools/rgds-audio.py", "tools/rgds-mod-state.py", "tools/smapi-common",
                    "libs.aarch64/liblwjgl_lz4.so", "licenses/LICENSE.LZ4.txt")
        for name in required:
            if APP + name not in data:
                raise ValueError("Missing runtime input: " + name)
        for name in ("dotnet/dotnet", "libs.aarch64/liblwjgl_lz4.so"):
            header = data[APP + name]
            if header[:5] != b"\x7fELF\x02" or int.from_bytes(header[18:20], "little") != 183:
                raise ValueError("Not ARM64 ELF: " + name)
        dll = APP + "Mods/RGDSplus.VerticalWorld/VerticalWorld.dll"
        if digest(data[dll]) != release["modSha256"]:
            raise ValueError("Mod hash mismatch")
        entry = data[ENTRY]
        if b"amixer -q sset Master unmute" not in entry or b"SDV_DUAL_UI_TEST_DIR" not in entry:
            raise ValueError("Audio/QA policy missing")
        if re.search(rb"export\s+SDV_DUAL_(?:UI|PERF)_TEST_DIR\s*=", entry):
            raise ValueError("Release enables QA")
        if (release.get("worldRenderScale") != 1 or release.get("worldUpscaler") != "none"
                or release.get("targetDrawRate") != 30 or release.get("fixedUpdateRate") != 60
                or release.get("verticalSync") is not True
                or release.get("worldAlphaDiscard") is not True or release.get("treeTransparentMarginTrim") is not True
                or b'SDV_WORLD_RENDER_SCALE="${SDV_WORLD_RENDER_SCALE:-1}"' not in entry
                or b'SDV_TARGET_FPS="${SDV_TARGET_FPS:-30}"' not in entry
                or b'SDV_WORLD_ALPHA_DISCARD="${SDV_WORLD_ALPHA_DISCARD:-1}"' not in entry
                or b'SDV_TREE_TRIM="${SDV_TREE_TRIM:-1}"' not in entry):
            raise ValueError("Native resolution / synchronized 30 FPS defaults missing")
        if release.get("firstUseWarmup") is True:
            if b'SDV_FIRST_USE_WARMUP="${SDV_FIRST_USE_WARMUP:-1}"' not in entry:
                raise ValueError("First-use warmup default missing")
        elif release["build"] == BUILD:
            raise ValueError("Current release must declare first-use warmup")
        return {"build": release["build"], "files": len(data),
                "bytes": Path(path).stat().st_size, "sha256": digest(Path(path).read_bytes()),
                "gameAssetsIncluded": False, "savesIncluded": False,
                "testsEnabled": False, "audio": True, "verified": True}


def build(output):
    output = Path(output).resolve()
    output.parent.mkdir(parents=True, exist_ok=True)
    files = collect()
    with zipfile.ZipFile(output, "w", zipfile.ZIP_DEFLATED, compresslevel=6) as archive:
        for name in sorted(set(EMPTY_DIRS) | set(files)):
            info = zipfile.ZipInfo(name, (2026, 10, 9, 0, 0, 0))
            info.create_system = 3
            if name.endswith("/"):
                info.external_attr = (0o40755 << 16) | 0x10
                archive.writestr(info, b"")
            else:
                data = files[name]
                info.external_attr = (0o100755 if executable(name, data) else 0o100644) << 16
                info.compress_type = zipfile.ZIP_DEFLATED
                archive.writestr(info, data)
    report = verify_archive(output)
    output.with_suffix(".zip.sha256").write_text(
        report["sha256"] + "  " + output.name + "\n", encoding="ascii")
    output.with_suffix(".audit.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(dict(archive=str(output), **report), indent=2))
    return output


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, default=ROOT / "build/releases" / BUILD / "Stardew Valley for RGDSplus.zip")
    parser.add_argument("--verify", type=Path)
    args = parser.parse_args()
    if args.verify:
        print(json.dumps(verify_archive(args.verify), indent=2))
    else:
        build(args.output)
