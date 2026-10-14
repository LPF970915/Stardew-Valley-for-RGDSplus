import importlib.util
import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("mod_compat", ROOT / "port/stardewvalleymainline/tools/rgds-mod-compat.py")
compat = importlib.util.module_from_spec(spec)
spec.loader.exec_module(compat)


class ModCompatTests(unittest.TestCase):
    def test_detailed_expression_is_cropped_from_the_original_without_loading_other_tiles(self):
        resizer = ROOT / "tools/ModCompatResizer/bin/Release/net6.0/RGDSplus.ModCompatResizer.dll"
        if not resizer.exists():
            self.skipTest("build the packaged image resizer first")
        with tempfile.TemporaryDirectory() as temp:
            base = Path(temp)
            source, target = base / "atlas.png", base / "expression.png"
            rows = [bytearray(([255, 0, 0, 255] * 300 + [0, 255, 0, 255] * 300) if y < 600
                             else ([0, 0, 255, 255] * 300 + [200, 100, 50, 255] * 300)) for y in range(1200)]
            compat.png_write(source, 600, 1200, rows)
            before = compat.digest(source)
            result = subprocess.run([str(ROOT / "tools/dotnet6/dotnet.exe"), str(resizer), "--tile", str(source),
                str(target), "300", "600", "300", "600", "--factor=2"], capture_output=True, text=True, timeout=60)
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            width, height, pixels = compat.png_read(target)
            self.assertEqual((width, height), (150, 300))
            self.assertTrue(all(row == bytearray([200, 100, 50, 255] * 150) for row in pixels))
            self.assertEqual(compat.digest(source), before)

    def test_portrait_only_rule_keeps_walk_sprites_and_handles_single_expression_atlases(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            for name, size in (("Portraits/Gunther.png", 600), ("Characters/ScaleUp/Sam.png", 2048)):
                path = root / name
                path.parent.mkdir(parents=True)
                compat.png_write(path, size, 1, [bytearray([128, 64, 32, 255] * size)])
            self.assertTrue(compat.should_resize_image(root / "Portraits/Gunther.png", root))
            self.assertFalse(compat.should_resize_image(root / "Characters/ScaleUp/Sam.png", root))

    def test_runtime_settings_survive_toggle_and_log_writes_do_not_rebuild_images(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp) / "Mods"
            source = root / "A"
            source.mkdir(parents=True)
            (source / "manifest.json").write_text('{"UniqueID":"A"}', encoding="utf-8")
            runtime = Path(temp) / ".rgds-runtime/Mods"
            state = runtime.parent / "mod-state.json"
            compat.build_runtime(root, runtime, state_file=state)
            (runtime / "A/config.json").write_text('{"UiScale":1.25}', encoding="utf-8")
            (runtime / "A/run.log").write_text('new session', encoding="utf-8")
            self.assertTrue(compat.build_runtime(root, runtime, state_file=state)["reused"])
            state.write_text('{"A":false}', encoding="utf-8")
            compat.build_runtime(root, runtime, state_file=state)
            self.assertFalse((runtime / "A").exists())
            state.write_text('{"A":true}', encoding="utf-8")
            compat.build_runtime(root, runtime, state_file=state)
            self.assertEqual(compat.read_json(runtime / "A/config.json"), {"UiScale":1.25})
            self.assertFalse((source / "config.json").exists())

    def test_ddf_tiles_follow_runtime_image_scale_and_do_not_change_authored_layout(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            original = {"Changes": [{"Action": "EditData", "Target": "aedenthorn.DialogueDisplayFramework/dictionary",
                "Entries": {"Sam": {"portrait": {"w": 1000, "h": 1800, "x": -1, "y": -1, "scale": 1, "xOffset": -900}},
                            "Robin": {"portrait": {"w": 600, "h": 600, "scale": 1.5}},
                            "Vanilla": {"portrait": {"w": 64, "h": 64}}, "Alias": {"CopyFrom": "Sam"}}}]}
            source, target = root / "source.json", root / "runtime.json"
            source.write_text(json.dumps(original), encoding="utf-8")
            before = compat.digest(source)
            compat._rewrite_scaled_from_area_json(source, target, root, {"assets/sam.png"})
            entries = compat.read_json(target)["Changes"][0]["Entries"]
            self.assertEqual(entries["Sam"]["portrait"], {"w": 125, "h": 225, "x": -1, "y": -1, "scale": 8, "xOffset": -900})
            self.assertEqual(entries["Robin"]["portrait"], {"w": 75, "h": 75, "scale": 12})
            self.assertEqual(entries["Alias"]["packName"], "RGDSplus.CompatiblePortraits")
            self.assertEqual(entries["Vanilla"], original["Changes"][0]["Entries"]["Vanilla"])
            self.assertEqual(compat.digest(source), before)

    def test_legacy_hidden_original_is_read_in_place_and_old_generated_mod_is_excluded(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp) / "Mods"
            root.mkdir()
            old = root / ".rgds-disabled--Original"
            old.mkdir()
            (old / "manifest.json").write_text('{"UniqueID":"OO.Portraits"}', encoding="utf-8")
            obsolete = root / "RGDSplus.ModCompatibility--OO.Portraits"
            obsolete.mkdir()
            (obsolete / "manifest.json").write_text('{"UniqueID":"Generated"}', encoding="utf-8")
            runtime = Path(temp) / ".rgds-runtime/Mods"
            compat.build_runtime(root, runtime)
            entries, warnings = compat.scan_mods(runtime)
            self.assertEqual([e["id"] for e in entries], ["oo.portraits"])
            self.assertEqual(warnings, [])
            self.assertTrue(old.is_dir())
            self.assertTrue(obsolete.is_dir())
            self.assertTrue((runtime / "RGDSplus.LegacySource--Original/manifest.json").is_file())

    def test_private_runtime_scales_from_area_sources_and_keeps_the_source_mod_unchanged(self):
        with tempfile.TemporaryDirectory(prefix="sdv-compat-test-") as temp:
            root = Path(temp) / "Mods"
            root.mkdir()
            dependencies = {
                "ContentPatcher": {"UniqueID": "Pathoschild.ContentPatcher"},
                "DialogueDisplayFramework": {"UniqueID": "Mangupix.DialogueDisplayFrameworkContinued"},
                "ScaleUpUnofficial": {"UniqueID": "Arborsm.ScaleUpUnofficial", "Dependencies": [
                    {"UniqueID": "Pathoschild.ContentPatcher"}]},
            }
            for folder, manifest in dependencies.items():
                path = root / folder
                path.mkdir()
                (path / "manifest.json").write_text(json.dumps(manifest), encoding="utf-8")
            packs = (
                ("OO portraits", "OO.Portraits", ["Mangupix.DialogueDisplayFrameworkContinued"]),
                ("OO SVE portraits", "OO.Portraits2", ["Mangupix.DialogueDisplayFrameworkContinued"]),
                ("Skimpy portraits", "Mud.SkimpyPortraits", [
                    "Mangupix.DialogueDisplayFrameworkContinued", "Arborsm.ScaleUpUnofficial"]),
                ("Skimpy portraits II", "Mud.SkimpyPortraits2", [
                    "Mangupix.DialogueDisplayFrameworkContinued", "Arborsm.ScaleUpUnofficial"]),
            )
            sources = {}
            for folder, identifier, required in packs:
                source = root / folder
                source.mkdir()
                manifest = {"UniqueID": identifier, "Name": identifier, "Version": "1.0.0",
                            "ContentPackFor": {"UniqueID": "Pathoschild.ContentPatcher"},
                            "Dependencies": [{"UniqueID": value} for value in required]}
                (source / "manifest.json").write_text(json.dumps(manifest), encoding="utf-8")
                (source / "assets/Portraits").mkdir(parents=True)
                (source / "content.json").write_text("{}", encoding="utf-8")
                sources[identifier] = source
            pixels = [bytearray([255, 0, 0, 255] * 1024) for _ in range(1024)]
            source_hashes = {}
            for _, identifier, _ in packs:
                source = sources[identifier]
                path = source / "assets/Portraits/large.png"
                compat.png_write(path, 1024, 1024, pixels)
                source_hashes[(identifier, "large.png")] = compat.digest(path)
            crop_mod = sources["Mud.SkimpyPortraits"]
            crop_json = {"Changes": [{"Action": "EditImage", "Target": "Portraits/Test",
                                      "FromFile": "assets/Portraits/cropped.png",
                                      "FromArea": {"X": 0, "Y": 8, "Width": 16, "Height": 16}}]}
            crop_json_path = crop_mod / "content.json"
            crop_json_path.write_text(json.dumps(crop_json), encoding="utf-8")
            crop_json_hash = compat.digest(crop_json_path)
            crop_path = crop_mod / "assets/Portraits/cropped.png"
            compat.png_write(crop_path, 1024, 1024, pixels)
            source_hashes[("Mud.SkimpyPortraits", "cropped.png")] = compat.digest(crop_path)
            runtime = Path(temp) / ".rgds-runtime" / "Mods"
            resizer = ROOT / "tools/ModCompatResizer/bin/Release/net6.0/RGDSplus.ModCompatResizer.dll"
            env = os.environ.copy()
            env["DOTNET_ROOT"] = str(ROOT / "tools/dotnet6")
            env["PATH"] = str(ROOT / "port/stardewvalleymainline/gamedata") + os.pathsep + env.get("PATH", "")
            packaged_resizer = resizer if resizer.exists() else None
            with patch.dict(os.environ, env):
                result = compat.build_runtime(root, runtime, resizer=packaged_resizer)
            self.assertTrue(result["generated"])
            self.assertEqual(result["resizedPngs"], 5)
            self.assertEqual(result["scaledFromAreaActions"], 1)
            runtime_entries, _ = compat.scan_mods(runtime)
            self.assertTrue(set(compat.PORTRAIT_MOD_IDS).issubset({e["id"] for e in runtime_entries}))
            for _, identifier, _ in packs:
                source = sources[identifier]
                self.assertEqual(compat.png_dimensions(runtime / source.name / "assets/Portraits/large.png"), (128, 128))
                self.assertEqual(compat.digest(source / "assets/Portraits/large.png"), source_hashes[(identifier, "large.png")])
            self.assertEqual(compat.png_dimensions(runtime / crop_mod.name / "assets/Portraits/cropped.png"), (128, 128))
            self.assertEqual(compat.digest(crop_path), source_hashes[("Mud.SkimpyPortraits", "cropped.png")])
            self.assertEqual(compat.digest(crop_json_path), crop_json_hash)
            runtime_crop = compat.read_json(runtime / crop_mod.name / "content.json")
            runtime_area = next(compat._iter_actions(runtime_crop))["FromArea"]
            self.assertEqual(runtime_area, {"X": 0, "Y": 1, "Width": 2, "Height": 2})
            reused = compat.build_runtime(root, runtime, resizer=packaged_resizer)
            self.assertTrue(reused["reused"])
            self.assertEqual(reused["scaledFromAreaActions"], 1)

    def test_state_choice_excludes_source_from_private_runtime(self):
        with tempfile.TemporaryDirectory(prefix="sdv-compat-choice-") as temp:
            root = Path(temp) / "Mods"
            root.mkdir()
            source = root / "A"
            source.mkdir()
            (source / "manifest.json").write_text(json.dumps({"UniqueID": "A", "Name": "A", "Version": "1.0.0"}), encoding="utf-8")
            state_file = Path(temp) / ".rgds-runtime/mod-state.json"
            state_file.parent.mkdir()
            state_file.write_text(json.dumps({"A": False}), encoding="utf-8")
            runtime = Path(temp) / ".rgds-runtime" / "Mods"
            compat.build_runtime(root, runtime, state_file=state_file)
            self.assertTrue(source.exists())
            self.assertEqual(list(root.iterdir()), [source])
            self.assertFalse((runtime / "A").exists())

    def test_disabled_dependency_excludes_dependent_from_private_runtime(self):
        with tempfile.TemporaryDirectory(prefix="sdv-compat-dependency-") as temp:
            root = Path(temp) / "Mods"
            root.mkdir()
            for folder, manifest in (
                ("A", {"UniqueID": "A", "Version": "1.0.0"}),
                ("B", {"UniqueID": "B", "Version": "1.0.0", "Dependencies": [{"UniqueID": "A"}]}),
            ):
                path = root / folder
                path.mkdir()
                (path / "manifest.json").write_text(json.dumps(manifest), encoding="utf-8")
            state_file = Path(temp) / ".rgds-runtime/mod-state.json"
            state_file.parent.mkdir()
            state_file.write_text(json.dumps({"A": False}), encoding="utf-8")
            runtime = Path(temp) / ".rgds-runtime" / "Mods"
            result = compat.build_runtime(root, runtime, state_file=state_file)
            self.assertFalse((runtime / "A").exists())
            self.assertFalse((runtime / "B").exists())
            self.assertTrue(any("required MOD" in warning for warning in result["warnings"]))

    def test_packaged_runtime_does_not_duplicate_default_mods(self):
        with tempfile.TemporaryDirectory(prefix="sdv-compat-defaults-") as temp:
            base = Path(temp)
            root = base / "Mods"
            defaults = base / "default-mods"
            root.mkdir()
            default_mod = defaults / "ConsoleCommands"
            default_mod.mkdir(parents=True)
            (default_mod / "manifest.json").write_text(json.dumps({"UniqueID": "SMAPI.ConsoleCommands", "Version": "1.0.0"}), encoding="utf-8")
            source_copy = root / "ConsoleCommands"
            source_copy.mkdir()
            (source_copy / "manifest.json").write_text(json.dumps({"UniqueID": "SMAPI.ConsoleCommands", "Version": "1.0.0"}), encoding="utf-8")
            runtime = base / ".rgds-runtime/Mods"
            compat.build_runtime(root, runtime, defaults=defaults)
            mods, _ = compat.scan_mods(runtime)
            self.assertEqual([mod["id"] for mod in mods].count("smapi.consolecommands"), 1)

    def test_packaged_resizer_streams_png_and_preserves_alpha(self):
        resizer = ROOT / "tools/ModCompatResizer/bin/Release/net6.0/RGDSplus.ModCompatResizer.dll"
        if not resizer.exists():
            self.skipTest("build the packaged image resizer first")
        with tempfile.TemporaryDirectory(prefix="sdv-resizer-test-") as temp:
            base = Path(temp)
            source = base / "source.png"
            rows = [bytearray([255, 0, 0, 255] * 1024) for _ in range(1024)]
            for y in range(4):
                rows[y][0:16] = bytes([0, 0, 255, 255] * 4) if y < 2 else bytes([0, 255, 0, 0] * 4)
            compat.png_write(source, 1024, 1024, rows)
            target = base / "target.png"
            env = os.environ.copy()
            env["DOTNET_ROOT"] = str(ROOT / "tools/dotnet6")
            env["PATH"] = str(ROOT / "port/stardewvalleymainline/gamedata") + os.pathsep + env.get("PATH", "")
            result = subprocess.run([str(ROOT / "tools/dotnet6/dotnet.exe"), str(resizer), str(source), str(target)],
                                    env=env, capture_output=True, text=True, timeout=60)
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            self.assertEqual(compat.png_dimensions(target), (256, 256))
            _, _, output = compat.png_read(target)
            self.assertEqual(output[0][:4], bytes([0, 0, 255, 128]))
            self.assertEqual(output[0][4:8], bytes([255, 0, 0, 255]))


if __name__ == "__main__":
    unittest.main()
