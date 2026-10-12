"""Exercise the actual launcher loop with child exits and PID-scoped requests."""
from pathlib import Path
import shlex
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]


class TitleRelaunchTests(unittest.TestCase):
    def test_normal_requested_crashed_stale_and_emergency_exits(self):
        launcher = (ROOT / 'port/StardewValleyMainline.sh').read_text()
        loop = launcher[launcher.index('while :; do\n'):launcher.index('\nif command -v pm_finish')]
        for scenario, launches in [('title', 2), ('desktop', 1), ('crash', 1), ('stale', 1), ('emergency', 1)]:
            with self.subTest(scenario=scenario), tempfile.TemporaryDirectory(prefix='sdv-title-') as tmp:
                base = Path(tmp)
                child = base / 'dotnet'
                child.write_text('''#!/bin/bash
echo boot >> "$TEST_ROOT/boots"
if [ "$(wc -l < "$TEST_ROOT/boots")" -eq 1 ]; then
  case "$SCENARIO" in
    title) printf '%s' "$$" > "$TEST_ROOT/run.return-title";;
    crash) printf '%s' "$$" > "$TEST_ROOT/run.return-title"; exit 137;;
    stale) printf '999999' > "$TEST_ROOT/run.return-title";;
    emergency) printf '%s' "$$" > "$TEST_ROOT/run.return-title"; touch "$TEST_ROOT/run.exit-requested";;
  esac
fi
exit 0
''', encoding='utf-8', newline='\n')
                linux = '/mnt/' + base.drive[0].lower() + base.as_posix()[2:]
                quoted = shlex.quote(linux)
                script = ('export TEST_ROOT=' + quoted + '\nexport SCENARIO=' + shlex.quote(scenario)
                          + '\nDOTNET_ROOT=$TEST_ROOT\nsession_path=$TEST_ROOT/run\nentry_assembly=fixture\n'
                          + 'chmod +x "$DOTNET_ROOT/dotnet"\n' + loop + '\n')
                result = subprocess.run(['wsl', '-d', 'Ubuntu', '--exec', 'bash', '-s'], input=script.encode(),
                                        capture_output=True, timeout=20)
                self.assertEqual(result.returncode, 0, result.stderr)
                self.assertEqual(len((base / 'boots').read_text().splitlines()), launches, result.stdout)
                if scenario == 'title':
                    self.assertFalse((base / 'run.return-title').exists())


if __name__ == '__main__':
    unittest.main()
