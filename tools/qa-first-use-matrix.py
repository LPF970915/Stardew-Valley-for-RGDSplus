"""Sequential device diagnostics; refuse to continue after incomplete cleanup."""
import argparse
import json
from pathlib import Path
import subprocess
import sys
import time

ROOT = Path(__file__).resolve().parents[1]


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--mobility-only", action="store_true", help="Rerun validated movement and engine transitions only.")
    args = parser.parse_args()
    manifest = ROOT / "build" / ("first-use-matrix-" + time.strftime("%Y%m%d-%H%M%S") + ".json")
    stages = [
        ("movement", ["--first-use-scenes", "move", "--seconds", "12", "--repeats", "2"]),
        ("engine-transition", ["--first-use-scenes", "transition", "--seconds", "12", "--repeats", "2"]),
        ("targeted-precompilation", ["--prepare-methods", "--seconds", "8", "--repeats", "2"]),
    ]
    if args.mobility_only:
        stages = stages[:2]
    results = []
    for name, options in stages:
        before = set((ROOT / "build").glob("performance-*"))
        subprocess.run([sys.executable, str(ROOT / "tools/qa-device-performance.py"),
                        "--first-use", "--tick-only", *options], cwd=ROOT, check=True)
        created = set((ROOT / "build").glob("performance-*")) - before
        if len(created) != 1:
            raise RuntimeError("Cannot identify unique stage output: " + name)
        output = created.pop()
        cleanup = json.loads((output / "cleanup.json").read_text(encoding="utf-8"))
        required = ("qaStopped", "protectedFilesUnchanged", "productionDllUnchanged", "probeRemoved")
        if cleanup.get("error") or not all(cleanup.get(key) is True for key in required):
            raise RuntimeError("Incomplete cleanup; stopping matrix: " + name)
        subprocess.run([sys.executable, str(ROOT / "tools/validate-first-use.py"), str(output)], check=True)
        subprocess.run([sys.executable, str(ROOT / "tools/analyze-first-use.py"), str(output)], check=True)
        subprocess.run([sys.executable, str(ROOT / "tools/analyze-device-performance.py"), str(output)], check=True)
        results.append({"stage": name, "directory": str(output), "cleanupVerified": True})
        manifest.write_text(json.dumps(results, indent=2), encoding="utf-8")
        print("STAGE COMPLETE " + name, flush=True)
    print("MATRIX MANIFEST " + str(manifest), flush=True)


if __name__ == "__main__":
    main()
