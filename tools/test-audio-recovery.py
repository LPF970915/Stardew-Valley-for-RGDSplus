"""Exercise audio startup failures without touching the host's audio services."""
import importlib.util
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("audio", ROOT / "port/stardewvalleymainline/tools/rgds-audio.py")
audio = importlib.util.module_from_spec(spec)
spec.loader.exec_module(audio)


class AudioRecoveryTests(unittest.TestCase):
    def exercise(self, *, available=False, running=False, native=False,
                 fallback=True, card=True, extra=None):
        calls = []
        state = {"available": available}

        def command(arguments, environment, timeout=5):
            calls.append((arguments, environment))
            if arguments[:2] == ["pactl", "info"]:
                return state["available"], ""
            if arguments[0] == "pgrep":
                return running, ""
            if arguments[0] == "/etc/init.d/S50pulseaudio":
                state["available"] = native
                return True, "Starting pulseaudio: OK"  # Firmware's false success.
            if arguments[0] == "pulseaudio":
                state["available"] = fallback
                return fallback, "daemon startup failed" if not fallback else ""
            if arguments[:2] == ["pactl", "set-sink-mute"]:
                return True, ""
            self.fail("Unexpected system mutation: " + repr(arguments))

        environment = {"PULSE_SERVER": audio.SERVER, "LD_LIBRARY_PATH": "/game/libs",
                       "LD_PRELOAD": "/game/shim.so", "XDG_CONFIG_HOME": "/qa/config",
                       **(extra or {})}
        with tempfile.TemporaryDirectory() as directory, patch.object(audio, "run", command), \
                patch.object(audio.time, "sleep"), \
                patch.object(audio.Path, "read_text", return_value="rockchipaw883xx" if card else "other"):
            audio.prepare(directory, environment)
        return calls

    def test_healthy_server_preserves_mute_and_volume(self):
        calls = self.exercise(available=True)
        self.assertEqual([args for args, _ in calls], [["pactl", "info"]])

    def test_native_service_recovery(self):
        calls = self.exercise(native=True)
        self.assertFalse(any(args[0] == "pulseaudio" for args, _ in calls))
        self.assertIn(["pactl", "set-sink-mute", "@DEFAULT_SINK@", "0"], [a for a, _ in calls])

    def test_false_init_success_uses_real_speaker_and_clean_libraries(self):
        calls = self.exercise()
        args, environment = next((a, e) for a, e in calls if a[0] == "pulseaudio")
        self.assertIn("-n", args)
        self.assertTrue(any("device=hw:rockchipaw883xx" in arg for arg in args))
        self.assertFalse(any("null-sink" in arg or "udev-detect" in arg for arg in args))
        self.assertNotIn("LD_LIBRARY_PATH", environment)
        self.assertNotIn("LD_PRELOAD", environment)
        self.assertNotIn("XDG_CONFIG_HOME", environment)

    def test_new_qa_server_is_muted(self):
        for variable in ("SDV_DUAL_UI_TEST_DIR", "SDV_DUAL_PERF_TEST_DIR"):
            with self.subTest(variable=variable):
                calls = self.exercise(extra={variable: "/tmp/qa"})
                self.assertEqual(calls[-1][0], ["pactl", "set-sink-mute", "@DEFAULT_SINK@", "1"])

    def test_unreachable_live_server_is_not_restarted(self):
        with self.assertRaisesRegex(RuntimeError, "running but"):
            self.exercise(running=True)

    def test_custom_server_is_not_replaced(self):
        with self.assertRaisesRegex(RuntimeError, "Configured PULSE_SERVER"):
            self.exercise(extra={"PULSE_SERVER": "tcp:custom-host"})

    def test_unknown_card_fails_without_silent_audio(self):
        with self.assertRaisesRegex(RuntimeError, "requires the RGDSplus"):
            self.exercise(card=False)

    def test_failed_fallback_reports_error(self):
        with self.assertRaisesRegex(RuntimeError, "Direct speaker recovery failed"):
            self.exercise(fallback=False)

    def test_command_timeout_is_bounded_failure(self):
        with patch.object(audio.subprocess, "run", side_effect=audio.subprocess.TimeoutExpired("pactl", 5)):
            self.assertFalse(audio.run(["pactl", "info"], {})[0])


if __name__ == "__main__":
    unittest.main()
