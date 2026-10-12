import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location("mod_state", Path(__file__).resolve().parents[1] / "port/stardewvalleymainline/tools/rgds-mod-state.py")
state = importlib.util.module_from_spec(spec)
spec.loader.exec_module(state)


class ModStateTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)

    def mod(self, folder, identifier, **extra):
        p = self.root / folder
        p.mkdir(parents=True)
        (p / "manifest.json").write_text(json.dumps(dict(UniqueID=identifier, Name=identifier, Version="1.0.0", **extra)))
        (p / "config.json").write_text('{"keep":42}')
        return p

    def choices(self, **values):
        (self.root / ".rgds-mod-state.json").write_text(json.dumps(values))

    def test_roundtrip_nested_mod_keeps_contents_and_is_idempotent(self):
        p = self.mod("group/A", "A")
        self.choices(A=False)
        state.apply(self.root)
        disabled = p.with_name(state.PREFIX + p.name)
        self.assertTrue(disabled.exists())
        self.assertEqual(state.apply(self.root)["changed"], [])
        self.choices(A=True)
        state.apply(self.root)
        self.assertEqual((p / "config.json").read_text(), '{"keep":42}')

    def test_required_dependency_and_content_pack_are_not_broken(self):
        self.mod("A", "A")
        self.mod("B", "B", ContentPackFor={"UniqueID": "A"})
        self.choices(A=False)
        with self.assertRaisesRegex(ValueError, "requires"):
            state.apply(self.root)
        self.assertTrue((self.root / "A").exists())
        self.choices(A=False, B=False)
        self.assertEqual(len(state.apply(self.root)["changed"]), 2)

    def test_locked_components_cannot_be_disabled(self):
        self.mod("core", "RGDSplus.VerticalWorld")
        self.choices(**{"RGDSplus.VerticalWorld": False})
        self.assertEqual(state.apply(self.root)["changed"], [])

    def test_target_collision_changes_nothing(self):
        self.mod("A", "A")
        (self.root / (state.PREFIX + "A")).mkdir()
        self.choices(A=False)
        with self.assertRaisesRegex(ValueError, "already exists"):
            state.apply(self.root)
        self.assertTrue((self.root / "A" / "manifest.json").exists())

    def test_failed_second_move_rolls_back_first(self):
        self.mod("A", "A")
        self.mod("B", "B")
        self.choices(A=False, B=False)
        rename = Path.rename
        count = 0

        def fail_once(old, new):
            nonlocal count
            count += 1
            if count == 2:
                raise OSError("injected failure")
            return rename(old, new)

        with patch.object(Path, "rename", fail_once), self.assertRaisesRegex(OSError, "injected"):
            state.apply(self.root)
        self.assertTrue((self.root / "A").exists())
        self.assertTrue((self.root / "B").exists())

    def test_commented_manifests_preserve_urls_and_string_commas(self):
        p = self.mod("A", "A")
        (p / "manifest.json").write_text('{/* note */ "UniqueID":"A", // note\n "URL":"https://a/,}",}')
        self.assertEqual(state.read_json(p / "manifest.json")["URL"], "https://a/,}")
        self.choices(a=False)
        self.assertEqual(len(state.apply(self.root)["changed"]), 1)

    def test_optional_dependency_does_not_block(self):
        self.mod("A", "A", Dependencies=[{"UniqueID":"Missing", "IsRequired":False}])
        self.choices(A=True)
        self.assertTrue(state.apply(self.root)["applied"])

    def test_invalid_choice_and_duplicate_ids_fail_before_moves(self):
        self.mod("A", "A")
        self.choices(A="false")
        with self.assertRaises(ValueError):
            state.apply(self.root)
        self.mod("B", "a")
        self.choices(A=False)
        with self.assertRaisesRegex(ValueError, "duplicate"):
            state.apply(self.root)
        self.assertTrue((self.root / "A").exists())


if __name__ == "__main__":
    unittest.main()
