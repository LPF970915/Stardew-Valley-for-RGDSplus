import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

spec = importlib.util.spec_from_file_location("mod_state", Path(__file__).resolve().parents[1] / "port/stardewvalleymainline/tools/rgds-mod-state.py")
state = importlib.util.module_from_spec(spec)
spec.loader.exec_module(state)


class ModStateTests(unittest.TestCase):
    def test_validation_does_not_rename_or_edit_mods(self):
        with tempfile.TemporaryDirectory() as temp:
            game = Path(temp) / "Game"
            root = game / "Mods"
            root.mkdir(parents=True)
            mod = root / "A"
            mod.mkdir()
            (mod / "manifest.json").write_text(json.dumps({"UniqueID": "A"}), encoding="utf-8")
            (mod / "config.json").write_text('{"keep":42}', encoding="utf-8")
            state_path = game / ".rgds-runtime/mod-state.json"
            state_path.parent.mkdir(parents=True)
            state_path.write_text(json.dumps({"A": False}), encoding="utf-8")
            result = state.validate(root, state_path)
            self.assertTrue(result["applied"])
            self.assertEqual(result["changed"], [])
            self.assertTrue(mod.exists())
            self.assertEqual((mod / "config.json").read_text(), '{"keep":42}')

    def test_invalid_choice_fails_without_changes(self):
        with tempfile.TemporaryDirectory() as temp:
            game = Path(temp) / "Game"
            root = game / "Mods"
            root.mkdir(parents=True)
            mod = root / "A"
            mod.mkdir()
            (mod / "manifest.json").write_text(json.dumps({"UniqueID": "A"}), encoding="utf-8")
            state_path = game / ".rgds-runtime/mod-state.json"
            state_path.parent.mkdir(parents=True)
            state_path.write_text(json.dumps({"A": "false"}), encoding="utf-8")
            with self.assertRaises(ValueError):
                state.validate(root, state_path)
            self.assertTrue(mod.exists())

    def test_bad_manifest_is_reported_without_blocking_valid_mods(self):
        with tempfile.TemporaryDirectory() as temp:
            game = Path(temp) / "Game"
            root = game / "Mods"
            root.mkdir(parents=True)
            valid = root / "A"
            valid.mkdir()
            (valid / "manifest.json").write_text(json.dumps({"UniqueID": "A"}), encoding="utf-8")
            broken = root / "broken"
            broken.mkdir()
            (broken / "manifest.json").write_text("{bad", encoding="utf-8")
            state_path = game / ".rgds-runtime/mod-state.json"
            state_path.parent.mkdir(parents=True)
            state_path.write_text("{}", encoding="utf-8")
            result = state.validate(root, state_path)
            self.assertTrue(result["applied"])
            self.assertEqual(result["mods"], 1)
            self.assertTrue(result["warnings"])


if __name__ == "__main__":
    unittest.main()
