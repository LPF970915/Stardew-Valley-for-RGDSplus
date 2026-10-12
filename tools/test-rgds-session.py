import importlib.util
import sys
from types import SimpleNamespace
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

    def test_title_relaunch_keeps_guard_until_launcher_exits(self):
        owner = (10, 'owner')
        old, replacement = (20, 'old'), (30, 'replacement')
        owner_checks = iter([True, True, True, False])
        def alive(target):
            return next(owner_checks) if target == owner else target == replacement
        with patch.dict(sys.modules, {'fcntl': SimpleNamespace()}), \
             patch.object(guard.signal, 'SIGHUP', 1, create=True), \
             patch.object(guard.signal, 'signal'), \
             patch.object(guard, 'process', return_value=('owner', 'S')), \
             patch.object(guard, 'alive', side_effect=alive), \
             patch.object(guard, 'game_identity', side_effect=[old, None, replacement]), \
             patch.object(guard.glob, 'glob', return_value=[]), \
             patch.object(guard.select, 'select', return_value=([], [], [])), \
             patch.object(guard, 'pause_menus'), \
             patch.object(guard, 'resume_menus') as resume, \
             patch.object(guard, 'terminate') as terminate:
            guard.run(Path('/tmp/session/run'), owner[0], Path('/game'))
            terminate.assert_called_once_with(replacement)
            resume.assert_called_once()


if __name__ == '__main__':
    unittest.main()
