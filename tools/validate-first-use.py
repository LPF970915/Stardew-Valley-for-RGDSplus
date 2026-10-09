"""Reject first-use windows which did not perform the intended action."""
import argparse
import json
from pathlib import Path


def validate(result):
    scene = result["scene"]
    if scene not in ("move", "transition"):
        return
    events = result.get("actionEvents", [])
    start_phase, end_phase = ("move-start", "move-stop") if scene == "move" else ("warp-start", "warp-end")
    starts = {event["action"]: event for event in events if event["phase"] == start_phase}
    ends = {event["action"]: event for event in events if event["phase"] == end_phase}
    if not starts or len(starts) != result["actions"] or starts.keys() != ends.keys():
        raise ValueError("Missing action completion: " + result["label"])
    for number, start in starts.items():
        end = ends[number]
        if scene == "move":
            if (start["x"], start["y"]) == (end["x"], end["y"]):
                raise ValueError("No player displacement: " + result["label"])
        elif start["location"] == end["location"] or end.get("menu") or end.get("eventUp") or end.get("freezeControls"):
            raise ValueError("Transition unchanged or blocked: " + result["label"])


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("directory", type=Path)
    args = parser.parse_args()
    results = json.loads((args.directory / "results.json").read_text(encoding="utf-8"))
    for result in results:
        validate(result)
    print("Action validity confirmed: " + str(args.directory))


if __name__ == "__main__":
    main()
