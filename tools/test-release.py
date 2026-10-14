import importlib.util
import json
import shutil
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
        self.assertEqual(release.APP, "Ports/Stardew Valley for RGDSplus/")
        self.assertIn(release.APP + "StardewValleyMainline.sh", files)
        self.assertNotIn("Ports/StardewVerticalWorldD2/StardewValleyMainline.sh", files)
        self.assertFalse(manifest["gameAssetsIncluded"])
        self.assertFalse(manifest["testsEnabled"])
        self.assertTrue(manifest["audio"])
        self.assertEqual(manifest["defaultMode"], "SceneHud")
        self.assertEqual(manifest["worldRenderScale"], 1)
        self.assertEqual(manifest["worldUpscaler"], "none")
        self.assertEqual(manifest["targetDrawRate"], 30)
        self.assertEqual(manifest["fixedUpdateRate"], 60)
        self.assertTrue(manifest["verticalSync"])
        self.assertTrue(manifest["firstUseWarmup"])
        self.assertTrue(manifest["modCompatibility"]["sourceModsUnchanged"])
        self.assertTrue(manifest["modCompatibility"]["steamAssetsUnchanged"])
        self.assertEqual(manifest["modCompatibility"]["runtimeDirectory"], ".rgds-runtime/Mods")
        self.assertEqual(manifest["modCompatibility"]["targetUniqueIds"],
                         ["OO.Portraits", "OO.Portraits2", "Mud.SkimpyPortraits", "Mud.SkimpyPortraits2"])
        self.assertEqual(manifest["modCompatibility"]["pngThreshold"], 512)
        self.assertEqual(manifest["modCompatibility"]["scale"], 0.125)
        self.assertEqual(manifest["modCompatibility"]["rulesVersion"], 9)
        self.assertTrue(manifest["modCompatibility"]["fromAreaCoordinatesScaled"])
        self.assertTrue(manifest["modCompatibility"]["ambiguousFromAreaSourceImagesPreserved"])
        self.assertEqual(manifest["modCompatibility"]["portraitAntialias"], "box-prefilter-point-clamp")
        self.assertEqual(manifest["modCompatibility"]["loadingOverlay"], "launcher-game-runner-and-smapi-load-stage")
        self.assertEqual(manifest["performanceValidation"],
                         "first-use-passed-long-run-unresolved-manual-test-requested")
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

    def test_import_cmd_has_sibling_check_and_propagates_failure(self):
        command = (ROOT / "packaging/Import Steam Assets.cmd").read_text(encoding="utf-8")
        self.assertIn('if not exist "%~dp0Import Steam Assets.ps1"', command)
        self.assertIn('-PromptSteamDir', command)
        self.assertIn('set "RC=%ERRORLEVEL%"', command)
        self.assertIn('if not "%RC%"=="0"', command)

    def test_double_click_prompt_imports_quoted_path_and_reports_failure(self):
        with tempfile.TemporaryDirectory(prefix="sdv interactive import ") as tmp:
            base = Path(tmp)
            game = base / "adapter"
            importer = game / "steam-import"
            importer.mkdir(parents=True)
            (game / "StardewValleyMainline.sh").write_bytes(b"#!/bin/bash\n")
            shutil.copy2(ROOT / "packaging/Import Steam Assets.cmd", importer)
            shutil.copy2(ROOT / "tools/prepare-steam-assets.ps1", importer / "Import Steam Assets.ps1")
            steam = base / "Steam game with spaces"
            content = steam / "Content"
            content.mkdir(parents=True)
            for index in range(3000):
                (content / f"asset-{index}.xnb").write_bytes(b"test asset")
            for name in ("Stardew Valley.dll", "StardewValley.GameData.dll", "MonoGame.Framework.dll", "xTile.dll"):
                (steam / name).write_bytes(b"test assembly")
            (steam / "Stardew Valley.deps.json").write_text("{}", encoding="utf-8")
            (steam / "Stardew Valley.runtimeconfig.json").write_text(json.dumps({
                "runtimeOptions": {"includedFrameworks": [{"name": "Microsoft.NETCore.App", "version": "6.0.32"}]}
            }), encoding="utf-8")
            savedata = game / "savedata"
            savedata.mkdir()
            (savedata / "keep").write_bytes(b"test save")
            command = ['cmd.exe', '/d', '/c', str(importer / "Import Steam Assets.cmd")]
            result = subprocess.run(command, input=f'"{steam}"\n\n', capture_output=True,
                                    text=True, errors="replace", timeout=120)
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            self.assertIn("Paste your Steam", result.stdout)
            self.assertIn("[2/4] Copying", result.stdout)
            self.assertIn("Verified 3006/3006", result.stdout)
            self.assertIn("Import complete.", result.stdout)
            self.assertEqual(len(list((game / "gamedata/Content").glob("*"))), 3000)
            self.assertEqual((savedata / "keep").read_bytes(), b"test save")
            result = subprocess.run(command, input=f'{base / "missing"}\n\n', capture_output=True,
                                    text=True, errors="replace", timeout=30)
            self.assertNotEqual(result.returncode, 0)
            self.assertIn("Import failed", result.stdout)
            self.assertNotIn("Import complete.", result.stdout)
            self.assertEqual((savedata / "keep").read_bytes(), b"test save")

    def test_entry_audio_policy_and_legacy_resolution(self):
        with tempfile.TemporaryDirectory(prefix="sdv-entry-test-") as tmp:
            base = Path(tmp)
            game = base / "Stardew Valley for RGDSplus"
            game.mkdir(parents=True)
            (game / "gamedata").mkdir()
            (game / "gamedata/Stardew Valley.dll").write_bytes(b"new legal test placeholder")
            stub = b"#!/bin/bash\nprintf 'GAME=%s WIDTH=%s HEIGHT=%s SCALE=%s FPS=%s WARMUP=%s\\n' \"$PWD\" \"$DISPLAY_WIDTH\" \"$DISPLAY_HEIGHT\" \"$SDV_WORLD_RENDER_SCALE\" \"$SDV_TARGET_FPS\" \"$SDV_FIRST_USE_WARMUP\"\n"
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
            for qa_enabled, warmup in ((False, None), (True, None), (True, '0')):
                if log.exists():
                    log.unlink()
                prefix = shlex.quote(linux)
                command = ("chmod +x " + prefix + '/mock/*; env PATH="' + linux +
                           '/mock:$PATH" AUDIO_LOG=' + prefix + "/audio.log SDV_DUAL_UI_TEST_DIR=" +
                           ("/tmp/qa" if qa_enabled else "''") +
                           " SDV_DUAL_PERF_TEST_DIR='' SDV_FIRST_USE_WARMUP=" +
                           ("''" if warmup is None else warmup) + " /bin/bash " + prefix + "/entry.sh")
                result = subprocess.run(["wsl", "-d", "Ubuntu", "--exec", "bash", "-c", command],
                                        capture_output=True, text=True, encoding="utf-8", errors="replace")
                self.assertEqual(result.returncode, 0, result.stderr)
                self.assertIn("GAME=/mnt/", result.stdout)
                self.assertIn("WIDTH=2048 HEIGHT=768", result.stdout)
                self.assertIn("SCALE=1 FPS=30 WARMUP=" + (warmup or '1'), result.stdout)
                if qa_enabled:
                    self.assertFalse(log.exists(), "QA must not invoke mixer unmute")
                else:
                    audio = log.read_text()
                    self.assertIn("amixer -q sset Master unmute", audio)
                    self.assertIn("pactl set-sink-mute @DEFAULT_SINK@ 0", audio)
            # Remove the new same-named folder and verify the old nested install
            # remains a supported fallback.
            shutil.rmtree(game)
            log.unlink(missing_ok=True)
            command = ("chmod +x " + prefix + '/mock/*; env PATH="' + linux +
                       '/mock:$PATH" AUDIO_LOG=' + prefix + "/audio.log SDV_DUAL_UI_TEST_DIR='' "
                       "/bin/bash " + prefix + "/entry.sh")
            result = subprocess.run(["wsl", "-d", "Ubuntu", "--exec", "bash", "-c", command],
                                    capture_output=True, text=True, encoding="utf-8", errors="replace")
            self.assertEqual(result.returncode, 0, result.stderr)
            self.assertIn("LEGACY WIDTH=2048 HEIGHT=768", result.stdout)

    def test_private_runtime_preserves_source_and_passes_smapi_path(self):
        with tempfile.TemporaryDirectory(prefix="sdv-runtime-launch-") as tmp:
            base = Path(tmp)
            game = base / "Stardew Valley for RGDSplus"
            game.mkdir()
            mods = game / "Mods"
            mods.mkdir()
            user_mod = mods / "UserMod"
            user_mod.mkdir()
            (user_mod / "manifest.json").write_text(json.dumps({"UniqueID": "User.Mod", "Name": "User Mod", "Version": "1.0.0"}), encoding="utf-8")
            (user_mod / "asset.txt").write_text("source remains", encoding="utf-8")
            runtime = game / ".rgds-runtime/Mods"
            state_file = game / ".rgds-runtime/mod-state.json"
            state_file.parent.mkdir()
            state_file.write_text(json.dumps({"User.Mod": False}), encoding="utf-8")
            spec = importlib.util.spec_from_file_location("compat", ROOT / "port/stardewvalleymainline/tools/rgds-mod-compat.py")
            compat = importlib.util.module_from_spec(spec)
            spec.loader.exec_module(compat)
            compat.build_runtime(mods, runtime, state_file=state_file)
            self.assertTrue((user_mod / "manifest.json").exists())
            self.assertFalse((mods / ".rgds-disabled--UserMod").exists())
            self.assertFalse((runtime / "UserMod").exists())
            self.assertEqual(list(mods.iterdir()), [user_mod])
            script = (ROOT / "port/StardewValleyMainline.sh").read_text(encoding="utf-8")
            self.assertIn('export SMAPI_MODS_PATH="$runtime_modsdir"', script)
            self.assertIn('export RGDSPLUS_USER_MODS_PATH="$modsdir"', script)
            self.assertIn('RGDSPLUS_MOD_STATE_PATH="$mods_state_path"', script)


if __name__ == "__main__":
    unittest.main()
