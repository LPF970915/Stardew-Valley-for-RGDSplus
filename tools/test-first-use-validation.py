import importlib.util
from pathlib import Path
import unittest

spec = importlib.util.spec_from_file_location("validator", Path(__file__).with_name("validate-first-use.py"))
validator = importlib.util.module_from_spec(spec)
spec.loader.exec_module(validator)


class ValidityTests(unittest.TestCase):
    def movement(self, final_y):
        return {"scene": "move", "label": "move", "actions": 1, "actionEvents": [
            {"action": 1, "phase": "move-start", "x": 0, "y": 0},
            {"action": 1, "phase": "move-stop", "x": 0, "y": final_y}]}

    def test_no_displacement_is_invalid(self):
        with self.assertRaises(ValueError):
            validator.validate(self.movement(0))

    def test_movement_is_valid(self):
        validator.validate(self.movement(20))

    def test_missing_completion_is_invalid(self):
        result = self.movement(20)
        result["actionEvents"].pop()
        with self.assertRaises(ValueError):
            validator.validate(result)

    def test_transition_must_change_location_and_not_be_blocked(self):
        result = {"scene": "transition", "label": "warp", "actions": 1, "actionEvents": [
            {"action": 1, "phase": "warp-start", "location": "FarmHouse"},
            {"action": 1, "phase": "warp-end", "location": "Farm", "eventUp": False}]}
        validator.validate(result)
        result["actionEvents"][1]["eventUp"] = True
        with self.assertRaises(ValueError):
            validator.validate(result)


if __name__ == "__main__":
    unittest.main()
