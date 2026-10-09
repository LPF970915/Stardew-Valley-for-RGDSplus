import importlib.util
from pathlib import Path
import tempfile
import unittest

spec = importlib.util.spec_from_file_location("perf_analysis", Path(__file__).with_name("analyze-device-performance.py"))
analysis = importlib.util.module_from_spec(spec)
spec.loader.exec_module(analysis)


class AnalysisTests(unittest.TestCase):
    def test_procfs_fields_preserve_units_and_ignore_nonnumeric(self):
        self.assertEqual(analysis.fields(["VmRSS:\t524288 kB", "State:\tR (running)", "pgscan_direct 42"]),
                         {"VmRSS": 524288, "pgscan_direct": 42})

    def test_samples_preserve_process_sections_and_empty_labels(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "samples.txt"
            path.write_text("@sample 100\n@file /proc/123/status\nVmRSS: 100 kB\n@sample 101 page-r1-data-07\n@file /proc/meminfo\nMemAvailable: 200 kB\n", encoding="utf-8")
            result = list(analysis.samples(path))
        self.assertEqual(len(result), 2)
        self.assertEqual(result[0]["label"], "")
        self.assertEqual(result[1]["label"], "page-r1-data-07")
        self.assertEqual(result[1]["files"]["/proc/meminfo"], ["MemAvailable: 200 kB"])

    def test_missing_values_are_not_reported_as_zero(self):
        self.assertIsNone(analysis.summary([None, None]))
        self.assertEqual(analysis.summary([1, None, 3]), {"min": 1, "mean": 2, "max": 3})

    def test_memory_units_preserve_missing_and_real_zero(self):
        self.assertIsNone(analysis.mib({}, "Pss"))
        self.assertEqual(analysis.mib({"VmRSS": 524288}, "VmRSS"), 512)
        self.assertEqual(analysis.mib({"SwapTotal": 0}, "SwapTotal"), 0)

    def test_io_counters_distinguish_reads_from_storage_bytes(self):
        self.assertEqual(analysis.fields(["rchar: 5000", "read_bytes: 0"]), {"rchar": 5000, "read_bytes": 0})


if __name__ == "__main__":
    unittest.main()
