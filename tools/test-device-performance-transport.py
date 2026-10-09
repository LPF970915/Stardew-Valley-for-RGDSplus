import importlib.util
import io
from pathlib import Path
import unittest
from unittest.mock import Mock

spec = importlib.util.spec_from_file_location("perf_runner", Path(__file__).with_name("qa-device-performance.py"))
runner = importlib.util.module_from_spec(spec)
spec.loader.exec_module(runner)


class TransportTests(unittest.TestCase):
    def test_partial_json_is_retryable(self):
        ftp = Mock()
        ftp.open.return_value = io.StringIO('{"id":"one",')
        self.assertIsNone(runner.read_reply(ftp, "/result.json"))

    def test_overlapping_in_place_write_is_retryable(self):
        ftp = Mock()
        ftp.open.return_value = io.StringIO('{"id":"one"}old trailing data')
        self.assertIsNone(runner.read_reply(ftp, "/result.json"))

    def test_missing_reply_is_retryable(self):
        ftp = Mock()
        ftp.open.side_effect = FileNotFoundError()
        self.assertIsNone(runner.read_reply(ftp, "/result.json"))

    def test_complete_reply_preserves_command_identity(self):
        ftp = Mock()
        ftp.open.return_value = io.StringIO('{"id":"one","error":null,"result":{"frames":42}}')
        self.assertEqual(runner.read_reply(ftp, "/result.json"),
                         {"id": "one", "error": None, "result": {"frames": 42}})


if __name__ == "__main__":
    unittest.main()
