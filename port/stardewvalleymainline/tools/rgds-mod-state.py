"""Validate queued MOD choices without changing the user's MOD directory.

The launcher applies these choices while building its private runtime copy.
This helper intentionally performs no rename, copy, or deletion in ``Mods``.
"""
import argparse
import json
from pathlib import Path
import re

LOCKED = {s.casefold() for s in ("RGDSplus.VerticalWorld", "SMAPI.ConsoleCommands", "SMAPI.SaveBackup",
                                  "RGDSplus.PerformanceProbe", "RGDSplus.RenderScaleProbe")}


def read_json(path):
    text = path.read_text(encoding="utf-8-sig")
    text = re.sub(r'("(?:\\.|[^"\\])*")|(/\*[\s\S]*?\*/|//[^\r\n]*)', lambda m: m[1] or "", text)
    text = re.sub(r'("(?:\\.|[^"\\])*")|(,\s*(?=[}\]]))', lambda m: m[1] or "", text)
    return json.loads(text)


def scan(root):
    mods = {}
    warnings = []

    def visit(folder, depth):
        if depth > 8 or folder.is_symlink() or (depth and (
                (folder.name.startswith(".") and not folder.name.startswith(".rgds-disabled--"))
                or folder.name.startswith("RGDSplus.ModCompatibility--"))):
            return
        try:
            paths = sorted(folder.iterdir(), key=lambda path: path.name.casefold())
        except OSError as error:
            warnings.append(f"{folder.name}: {error}")
            return
        manifest = next((p for p in paths if p.name.casefold() == "manifest.json" and p.is_file()), None)
        if manifest:
            try:
                data = read_json(manifest)
                identifier = str(next((v for k, v in data.items() if k.casefold() == "uniqueid"), "")).strip()
            except (OSError, ValueError, TypeError, AttributeError):
                identifier = ""
            if not identifier:
                warnings.append(f"{folder.name}: missing or invalid manifest UniqueID")
            elif identifier.casefold() in mods:
                warnings.append(f"duplicate UniqueID skipped: {identifier}")
            else:
                mods[identifier.casefold()] = {"path": str(folder), "enabled": not folder.name.startswith(".")}
            return
        for child in paths:
            if child.is_dir():
                visit(child, depth + 1)

    visit(Path(root), 0)
    return mods, warnings


def validate(root, state_file):
    root = Path(root).resolve(strict=True)
    state = Path(state_file)
    if state.is_symlink():
        raise ValueError("Symlink state file")
    state = state.resolve()
    if not state.exists():
        return {"applied": True, "changed": [], "warnings": [], "mods": 0}
    choices = read_json(state)
    if not isinstance(choices, dict) or any(type(value) is not bool for value in choices.values()):
        raise ValueError("Invalid mod choices")
    mods, warnings = scan(root)
    unknown = [key for key in choices if str(key).casefold() not in mods and str(key).casefold() not in LOCKED]
    warnings.extend("choice has no installed MOD: " + str(key) for key in unknown)
    for key in LOCKED:
        if key in choices and choices[key] is False:
            warnings.append("locked component remains enabled: " + key)
    return {"applied": True, "changed": [], "warnings": warnings, "mods": len(mods)}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--mods", type=Path, required=True)
    parser.add_argument("--state-file", type=Path, required=True)
    args = parser.parse_args()
    try:
        result = validate(args.mods, args.state_file)
    except Exception as error:
        result = {"applied": False, "error": str(error), "changed": []}
    print("[mods] " + json.dumps(result, ensure_ascii=False))
    return 0 if result["applied"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
