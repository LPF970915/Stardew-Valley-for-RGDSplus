"""Compare cold and repeated action windows; never add nested method timers."""
import argparse
import csv
import json
from pathlib import Path


def action_window(rows, number):
    window = [r for r in rows if int(r.get("action_number", 0)) == number]
    if not window:
        return None
    peak = max(window, key=lambda r: float(r["frame_ms"]))
    total_jit = sum(float(r["jit_ms"]) for r in window) if "jit_ms" in peak else None
    return {"actionNumber": number, "frames": len(window),
            "peakFrame": {k: float(v) for k, v in peak.items()},
            "jitMs": total_jit,
            "allocatedBytes": sum(int(r["allocated_bytes"]) for r in window),
            "gc": [sum(int(r["gc" + str(g)]) for r in window) for g in range(3)],
            "over100": sum(float(r["frame_ms"]) > 100 for r in window),
            "over250": sum(float(r["frame_ms"]) > 250 for r in window)}


def summarize(path):
    with path.open(newline="", encoding="utf-8") as f:
        rows = list(csv.DictReader(f))
    numbers = sorted({int(r.get("action_number", 0)) for r in rows} - {0})
    windows = [action_window(rows, number) for number in numbers]
    return {"label": path.name.removesuffix("-frames.csv"), "windows": windows,
            "firstPeakMs": windows[0]["peakFrame"]["frame_ms"] if windows else None,
            "laterPeakMs": max(w["peakFrame"]["frame_ms"] for w in windows[1:]) if len(windows) > 1 else None}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("directory", type=Path)
    args = parser.parse_args()
    results = [summarize(p) for p in sorted(args.directory.glob("*action-frames.csv"))]
    (args.directory / "first-use-analysis.json").write_text(json.dumps(results, indent=2), encoding="utf-8")
    for r in results:
        first = r["firstPeakMs"]
        if first is None:
            continue
        w = r["windows"][0]
        print(f"{r['label']}: first_peak={first:.1f}ms later_peak={r['laterPeakMs']}ms "
              f"first_jit={w['jitMs']}ms first_gc={w['gc']}")


if __name__ == "__main__":
    main()
