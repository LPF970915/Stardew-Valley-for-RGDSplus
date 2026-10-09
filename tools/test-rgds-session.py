import importlib.util
from pathlib import Path
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('guard', ROOT / 'port/stardewvalleymainline/tools/rgds-session.py')
guard = importlib.util.module_from_spec(spec)
spec.loader.exec_module(guard)


class GuardTests(unittest.TestCase):
    def test_short_presses_and_repeats(self):
        hold = guard.Hold()
        for now in (0, 2, 4):
            hold.event(1, 310, 1, now)
            self.assertFalse(hold.expired(now + .3))
            hold.event(1, 310, 0, now + .4)
        self.assertFalse(hold.expired(99))
        hold.event(1, 310, 1, 100)
        hold.event(1, 310, 2, 101)
        hold.event(1, 310, 1, 101.1)
        self.assertFalse(hold.expired(101.49))
        self.assertTrue(hold.expired(101.5))

    def test_disconnect_cancels_only_its_device(self):
        hold = guard.Hold()
        hold.event(1, 310, 1, 0)
        hold.event(2, 158, 1, 1)
        hold.cancel(1)
        self.assertFalse(hold.expired(2))
        self.assertTrue(hold.expired(2.5))

    def test_pid_reuse_never_signalled(self):
        with patch.object(guard, 'process', return_value=('new', 'S')), patch.object(guard.os, 'kill') as kill:
            guard.terminate((123, 'old'), grace=0)
            guard.resume_menus({123: 'old'})
            kill.assert_not_called()

    def test_hung_game_terminates_independent_of_main_thread(self):
        with patch.object(guard, 'process', return_value=('birth', 'T')), patch.object(guard.signal, 'SIGKILL', 9, create=True), patch.object(guard.os, 'kill') as kill:
            guard.terminate((123, 'birth'), grace=0)
            self.assertEqual([call.args[1] for call in kill.call_args_list], [guard.signal.SIGTERM, 9])

    def test_foreign_game_token_rejected(self):
        def read_text(path, *args, **kwargs):
            return '123'
        def read_bytes(path, *args, **kwargs):
            if path.name == 'cmdline':
                return b'/game/dotnet/dotnet\0StardewModdingAPI.dll\0'
            return b'SDV_SESSION_TOKEN=/tmp/other/run\0'
        with patch.object(Path, 'read_text', read_text), patch.object(Path, 'read_bytes', read_bytes):
            self.assertIsNone(guard.game_identity(Path('/tmp/session/run'), Path('/game')))


if __name__ == '__main__':
    unittest.main()
