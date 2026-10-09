import importlib.util
import json
from pathlib import Path
import subprocess
import shlex
import tempfile
import unittest
import zipfile

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("release", ROOT / "tools/build-release.py")
release = importlib.util.module_from_spec(spec)
spec.loader.exec_module(release)


class ReleaseTests(unittest.TestCase):
    def test_reject_unsafe_and_private_names(self):
        for name in ("/etc/passwd", release.APP + "../secret", release.APP + "gamedata/Content/a.xnb",
                     release.APP + "savedata/SaveGameInfo", release.APP + "tools/__pycache__/a.pyc",
                     release.APP + "Mods/RGDSplus.VerticalWorld/config.json",
                     release.APP + "logs/test.log", release.APP + "Stardew Valley.dll"):
            with self.subTest(name=name), self.assertRaises(ValueError):
                release.validate_name(name)

    def test_collect_exact_release_policy(self):
        files = release.collect()
        manifest = json.loads(files[release.MANIFEST])
        self.assertFalse(manifest["gameAssetsIncluded"])
        self.assertFalse(manifest["testsEnabled"])
        self.assertTrue(manifest["audio"])
        self.assertEqual(manifest["defaultMode"], "SceneHud")
        self.assertIn(release.APP + "steam-import/Import Steam Assets.ps1", files)
        self.assertFalse(any("PerformanceProbe" in n or "test-" in n for n in files))
        self.assertEqual(json.loads(files[release.APP + "Mods/SMAPI-config.json"]),
                         {"ListenForConsoleInput": False})

    def test_build_and_tamper_detection(self):
        with tempfile.TemporaryDirectory(prefix="sdv-release-test-") as tmp:
            path = release.build(Path(tmp) / "release.zip")
            report = release.verify_archive(path)
            self.assertTrue(report["verified"])
            damaged = Path(tmp) / "damaged.zip"
            with zipfile.ZipFile(path) as source, zipfile.ZipFile(damaged, "w") as target:
                for info in source.infolist():
                    data = source.read(info.filename)
                    if info.filename == release.ENTRY:
                        data += b"# changed\n"
                    target.writestr(info, data)
            with self.assertRaisesRegex(ValueError, "Checksum mismatch"):
                release.verify_archive(damaged)

    def test_import_rejects_wrong_game_and_keeps_user_state(self):
        with tempfile.TemporaryDirectory(prefix="sdv-import-test-") as tmp:
            base = Path(tmp)
            steam = base / "steam"
            steam.mkdir()
            destination = base / "adapter/gamedata"
            destination.mkdir(parents=True)
            savedata = base / "adapter/savedata"
            savedata.mkdir()
            save = savedata / "do-not-touch"
            save.write_bytes(b"save fixture")
            old = destination / "existing"
            old.write_bytes(b"existing game fixture")
            command = ["powershell.exe", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File",
                       str(ROOT / "tools/prepare-steam-assets.ps1"), "-SteamDir", str(steam),
                       "-PortDir", str(destination), "-Clean"]
            result = subprocess.run(command, capture_output=True)
            self.assertNotEqual(result.returncode, 0)
            self.assertEqual(old.read_bytes(), b"existing game fixture")
            self.assertEqual(save.read_bytes(), b"save fixture")

    def test_entry_audio_policy_and_legacy_resolution(self):
        with tempfile.TemporaryDirectory(prefix="sdv-entry-test-") as tmp:
            base = Path(tmp)
            game = base / "StardewVerticalWorldD2/StardewValleyMainline"
            game.mkdir(parents=True)
            stub = b"#!/bin/bash\nprintf 'GAME=%s WIDTH=%s HEIGHT=%s\\n' \"$PWD\" \"$DISPLAY_WIDTH\" \"$DISPLAY_HEIGHT\"\n"
            (game / "StardewValleyMainline.sh").write_bytes(stub)
            legacy = base / "StardewVerticalWorldD2/StardewVerticalWorldD2/StardewValleyMainline"
            (legacy / "gamedata").mkdir(parents=True)
            (legacy / "gamedata/Stardew Valley.dll").write_bytes(b"legal test placeholder")
            (legacy / "StardewValleyMainline.sh").write_bytes(b"#!/bin/bash\nprintf 'LEGACY WIDTH=%s HEIGHT=%s\\n' \"$DISPLAY_WIDTH\" \"$DISPLAY_HEIGHT\"\n")
            entry = base / "entry.sh"
            entry.write_bytes((ROOT / "packaging/Stardew Valley for RGDSplus.sh").read_bytes())
            mock = base / "mock"
            mock.mkdir()
            for name in ("amixer", "pactl"):
                (mock / name).write_bytes(
                    f'#!/bin/bash\nprintf "{name} %s\\n" "$*" >> "$AUDIO_LOG"\n'.encode())
            linux = "/mnt/" + base.drive[0].lower() + base.as_posix()[2:]
            log = base / "audio.log"
            for qa_enabled in (False, True):
                if log.exists():
                    log.unlink()
                prefix = shlex.quote(linux)
                command = ("chmod +x " + prefix + '/mock/*; env PATH="' + linux +
                           '/mock:$PATH" AUDIO_LOG=' + prefix + "/audio.log SDV_DUAL_UI_TEST_DIR=" +
                           ("/tmp/qa" if qa_enabled else "''") +
                           " SDV_DUAL_PERF_TEST_DIR='' /bin/bash " + prefix + "/entry.sh")
                result = subprocess.run(["wsl", "-d", "Ubuntu", "--exec", "bash", "-c", command],
                                        capture_output=True, text=True, encoding="utf-8", errors="replace")
                self.assertEqual(result.returncode, 0, result.stderr)
                self.assertIn("LEGACY WIDTH=2048 HEIGHT=768", result.stdout)
                if qa_enabled:
                    self.assertFalse(log.exists(), "QA must not invoke mixer unmute")
                else:
                    audio = log.read_text()
                    self.assertIn("amixer -q sset Master unmute", audio)
                    self.assertIn("pactl set-sink-mute @DEFAULT_SINK@ 0", audio)


if __name__ == "__main__":
    unittest.main()
