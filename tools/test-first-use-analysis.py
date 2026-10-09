import importlib.util
from pathlib import Path
import unittest

spec = importlib.util.spec_from_file_location("first_use", Path(__file__).with_name("analyze-first-use.py"))
analysis = importlib.util.module_from_spec(spec)
spec.loader.exec_module(analysis)


def row(number, frame, jit=None):
    value = {"action_number": str(number), "frame_ms": str(frame),
             "allocated_bytes": "100", "gc0": "0", "gc1": "0", "gc2": "0"}
    if jit is not None:
        value["jit_ms"] = str(jit)
    return value


class FirstUseTests(unittest.TestCase):
    def test_window_includes_delayed_effect_not_only_trigger_frame(self):
        result = analysis.action_window([row(0, 500), row(1, 20, 0), row(1, 700, 620), row(2, 30, 0)], 1)
        self.assertEqual(result["peakFrame"]["frame_ms"], 700)
        self.assertEqual(result["jitMs"], 620)
        self.assertEqual(result["allocatedBytes"], 200)
        self.assertEqual(result["over250"], 1)

    def test_missing_runtime_metrics_are_not_reported_as_zero(self):
        self.assertIsNone(analysis.action_window([row(1, 20)], 1)["jitMs"])

    def test_absent_action_has_no_window(self):
        self.assertIsNone(analysis.action_window([row(0, 20)], 1))


if __name__ == "__main__":
    unittest.main()
