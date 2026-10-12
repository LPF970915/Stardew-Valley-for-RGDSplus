"""Apply queued MOD choices before SMAPI loads any mod or resources."""
import argparse
import json
from pathlib import Path
import re

PREFIX = ".rgds-disabled--"
LOCKED = {s.casefold() for s in ("RGDSplus.VerticalWorld", "SMAPI.ConsoleCommands", "SMAPI.SaveBackup",
                               "RGDSplus.PerformanceProbe", "RGDSplus.RenderScaleProbe")}


def read_json(path):
    # Match strings first so comments and trailing commas inside strings survive.
    text = path.read_text(encoding="utf-8-sig")
    text = re.sub(r'("(?:\\.|[^"\\])*")|(/\*[\s\S]*?\*/|//[^\r\n]*)', lambda m: m[1] or "", text)
    text = re.sub(r'("(?:\\.|[^"\\])*")|(,\s*(?=[}\]]))', lambda m: m[1] or "", text)
    return json.loads(text)


def fields(value):
    return {k.casefold(): v for k, v in value.items()}


def scan(root):
    mods = {}

    def visit(folder, depth):
        if depth > 8 or folder.is_symlink():
            return
        if depth and folder.name.startswith(".") and not folder.name.startswith(PREFIX):
            return
        paths = list(folder.iterdir())
        manifest = next((p for p in paths if p.name.casefold() == "manifest.json" and p.is_file()), None)
        if manifest:
            if manifest.is_symlink():
                raise ValueError("Symlink manifest: " + str(manifest))
            data = fields(read_json(manifest))
            identifier = data.get("uniqueid", "").casefold()
            if not identifier or identifier in mods:
                raise ValueError("Missing or duplicate mod ID: " + str(folder))
            dependencies = [fields(d)["uniqueid"].casefold() for d in data.get("dependencies", [])
                            if fields(d).get("isrequired", True)]
            if data.get("contentpackfor"):
                dependencies.append(fields(data["contentpackfor"])["uniqueid"].casefold())
            mods[identifier] = dict(path=folder, enabled=not folder.name.startswith(PREFIX), required=dependencies)
            return
        for child in paths:
            if child.is_dir():
                visit(child, depth + 1)

    visit(root, 0)
    return mods


def apply(root):
    root = Path(root).resolve(strict=True)
    state = root / ".rgds-mod-state.json"
    if not state.exists():
        return dict(applied=True, changed=[])
    if state.is_symlink():
        raise ValueError("Symlink state file")
    choices = read_json(state)
    if not isinstance(choices, dict) or any(type(v) is not bool for v in choices.values()):
        raise ValueError("Invalid mod choices")
    choices = {k.casefold(): v for k, v in choices.items()}
    mods = scan(root)
    desired = {key: True if key in LOCKED else choices.get(key, mod["enabled"]) for key, mod in mods.items()}
    plan = []
    for key, mod in mods.items():
        if desired[key]:
            missing = [dep for dep in mod["required"] if not desired.get(dep, False)]
            if missing:
                raise ValueError(key + " requires " + ", ".join(missing))
        if desired[key] == mod["enabled"]:
            continue
        old = mod["path"]
        name = old.name[len(PREFIX):] if desired[key] else PREFIX + old.name
        if not name or name in (".", "..") or name.startswith(".") and desired[key]:
            raise ValueError("Invalid mod folder name")
        new = old.with_name(name)
        if new.exists() or new.is_symlink():
            raise ValueError("Target already exists: " + str(new))
        if not old.resolve().is_relative_to(root) or new.parent.resolve() != old.parent.resolve():
            raise ValueError("Mod path outside root")
        plan.append((old, new))
    moved = []
    try:
        for old, new in plan:
            old.rename(new)
            moved.append((old, new))
    except Exception:
        for old, new in reversed(moved):
            new.rename(old)
        raise
    return dict(applied=True, changed=[dict(before=str(a.relative_to(root)), after=str(b.relative_to(root))) for a, b in moved])


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--mods", type=Path, required=True)
    args = parser.parse_args()
    try:
        result = apply(args.mods)
    except Exception as error:
        result = dict(applied=False, error=str(error), changed=[])
    report = args.mods / ".rgds-mod-result.json"
    temporary = report.with_suffix(".next")
    temporary.write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
    temporary.replace(report)
    print("[mods] " + json.dumps(result, ensure_ascii=False))
    return 0 if result["applied"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
