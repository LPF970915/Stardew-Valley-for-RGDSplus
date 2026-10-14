"""Run the private-runtime compatibility checks against an existing MOD folder."""
import argparse
import importlib.util
import json
import os
from pathlib import Path
import shutil
import tempfile
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location(
    "mod_compat", ROOT / "port/stardewvalleymainline/tools/rgds-mod-compat.py")
compat = importlib.util.module_from_spec(spec)
spec.loader.exec_module(compat)


def tree_hashes(root):
    return {
        path.relative_to(root).as_posix(): compat.digest(path)
        for path in root.rglob("*")
        if path.is_file() and not path.is_symlink()
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("mods", type=Path, help="Existing user Mods directory; read-only input")
    parser.add_argument("--resizer", type=Path, default=ROOT / "tools/ModCompatResizer/bin/Release/net6.0/RGDSplus.ModCompatResizer.dll")
    parser.add_argument("--dotnet-root", type=Path, default=ROOT / "tools/dotnet6")
    args = parser.parse_args()
    source_root = args.mods.resolve(strict=True)
    if not source_root.is_dir():
        raise NotADirectoryError(source_root)
    if not args.resizer.is_file():
        raise FileNotFoundError("Build the packaged PNG resizer first: " + str(args.resizer))

    entries, warnings = compat.scan_mods(source_root)
    by_id = {entry["id"]: entry for entry in entries}
    required_targets = set(compat.PORTRAIT_MOD_IDS)
    missing_targets = sorted(required_targets - set(by_id))
    if missing_targets:
        raise RuntimeError("Missing portrait MODs: " + ", ".join(missing_targets))

    closure = set(required_targets)
    pending = list(required_targets)
    while pending:
        identifier = pending.pop()
        for dependency in by_id[identifier]["required"]:
            if dependency not in by_id:
                raise RuntimeError(f"{identifier} requires missing MOD {dependency}")
            if dependency not in closure:
                closure.add(dependency)
                pending.append(dependency)

    before = {identifier: tree_hashes(by_id[identifier]["path"]) for identifier in closure}
    with tempfile.TemporaryDirectory(prefix="rgds-mod-pack-test-") as temp:
        game = Path(temp) / "Game"
        staged = game / "Mods"
        staged.mkdir(parents=True)
        for identifier in sorted(closure):
            source = by_id[identifier]["path"]
            shutil.copytree(source, staged / source.name, symlinks=False)

        runtime = game / ".rgds-runtime/Mods"
        env = os.environ.copy()
        env["DOTNET_ROOT"] = str(args.dotnet_root.resolve())
        env["PATH"] = str(ROOT / "port/stardewvalleymainline/gamedata") + os.pathsep + env.get("PATH", "")
        with patch.dict(os.environ, env):
            result = compat.build_runtime(staged, runtime, resizer=args.resizer)
            reused = compat.build_runtime(staged, runtime, resizer=args.resizer)
        if not result.get("applied") or result.get("reused"):
            raise AssertionError("The isolated runtime was not freshly generated: " + json.dumps(result, ensure_ascii=False))
        if not reused.get("reused"):
            raise AssertionError("An unchanged runtime did not reuse its compatibility cache")

        runtime_entries, runtime_warnings = compat.scan_mods(runtime)
        runtime_ids = {entry["id"] for entry in runtime_entries}
        if runtime_ids != closure:
            raise AssertionError("Runtime MOD/dependency set differs: " + json.dumps({
                "missing": sorted(closure - runtime_ids), "extra": sorted(runtime_ids - closure)}, ensure_ascii=False))

        resized_expected = 0
        scaled_from_area_expected = 0
        memory_before = memory_after = 0
        preserved_from_area_bytes = 0
        large_preserved_sources = []
        crop_source_counts = {}
        pack_report = []
        for identifier in sorted(required_targets):
            entry = by_id[identifier]
            original = entry["path"]
            copied = runtime / original.name
            area_info = compat._from_area_info(original)
            crop_sources = compat._from_area_sources(original)
            preserved_sources = area_info["preserved"]
            resized_sources = set()
            for image in original.rglob("*.png"):
                relative = image.relative_to(original)
                width, height = compat.png_dimensions(image)
                target = copied / relative
                actual = compat.png_dimensions(target)
                large = compat.should_resize_image(image, original)
                preserve = relative.as_posix().casefold() in preserved_sources
                if large and not preserve:
                    expected = (max(1, (width + 7) // 8), max(1, (height + 7) // 8))
                    resized_expected += 1
                    resized_sources.add(relative.as_posix().casefold())
                else:
                    expected = (width, height)
                if preserve:
                    preserved_from_area_bytes += width * height * 4
                    if large:
                        large_preserved_sources.append({
                            "id": identifier,
                            "file": relative.as_posix(),
                            "width": width,
                            "height": height,
                            "decodedMiB": round(width * height * 4 / (1024 * 1024), 1),
                        })
                if actual != expected:
                    raise AssertionError(f"Unexpected PNG dimensions for {identifier}/{relative}: {actual}, expected {expected}")
                memory_before += width * height * 4
                memory_after += actual[0] * actual[1] * 4
            crop_source_counts[identifier] = len(crop_sources)
            for relative, expected_hash in before[identifier].items():
                if compat.digest(original / relative) != expected_hash:
                    raise AssertionError("Source MOD changed during test: " + identifier + "/" + relative)
                if relative.casefold().endswith(".json"):
                    source_json = original / relative
                    runtime_json = copied / relative
                    try:
                        source_data = compat.read_json(source_json)
                        runtime_data = compat.read_json(runtime_json)
                    except (OSError, ValueError, TypeError):
                        continue
                    source_actions = list(compat._iter_actions(source_data))
                    runtime_actions = list(compat._iter_actions(runtime_data))
                    if len(source_actions) != len(runtime_actions):
                        raise AssertionError("Runtime action count changed: " + identifier + "/" + relative)
                    for source_action, runtime_action in zip(source_actions, runtime_actions):
                        source_area = source_action.get("FromArea")
                        runtime_area = runtime_action.get("FromArea")
                        paths, unresolved, _ = compat._area_paths(original, source_action)
                        references = {path.relative_to(original).as_posix().casefold() for path in paths}
                        should_scale = ("FromArea" in source_action and not unresolved and references
                                        and references.issubset(resized_sources)
                                        and compat._area_is_aligned(source_action))
                        if should_scale:
                            expected_area = {key: source_area[key] // 8
                                             for key in ("X", "Y", "Width", "Height")}
                            if runtime_area != expected_area:
                                raise AssertionError("FromArea coordinates weren't scaled with their source: "
                                                     + identifier + "/" + relative)
                            scaled_from_area_expected += 1
                        elif runtime_area != source_area:
                            raise AssertionError("An unrelated FromArea action changed: "
                                                 + identifier + "/" + relative)
            manifest = compat.read_json(original / "manifest.json")
            pack_report.append({"id": identifier, "name": manifest.get("Name"), "version": manifest.get("Version")})

        if result.get("resizedPngs") != resized_expected:
            raise AssertionError(f"Resizer count {result.get('resizedPngs')} != expected {resized_expected}")
        if result.get("scaledFromAreaActions") != scaled_from_area_expected:
            raise AssertionError("Scaled FromArea action count differs: " + json.dumps({
                "reported": result.get("scaledFromAreaActions"),
                "expected": scaled_from_area_expected}, ensure_ascii=False))
        if warnings or result.get("warnings") or runtime_warnings:
            raise AssertionError("MOD scan produced warnings: " + json.dumps({
                "source": warnings, "runtime": result.get("warnings"), "copied": runtime_warnings}, ensure_ascii=False))

        after = {identifier: tree_hashes(by_id[identifier]["path"]) for identifier in closure}
        if before != after:
            raise AssertionError("An original MOD or dependency changed during the test")

        report = {
            "passed": True,
            "portraitPacks": pack_report,
            "requiredDependencyCount": len(closure - required_targets),
            "resizedPngs": result.get("resizedPngs"),
            "scaledFromAreaActions": result.get("scaledFromAreaActions"),
            "preservedFromAreaSourceCounts": crop_source_counts,
            "preservedFromAreaDecodedMiBBefore": round(preserved_from_area_bytes / (1024 * 1024), 1),
            "largePreservedFromAreaSources": large_preserved_sources,
            "decodedPngMiBBefore": round(memory_before / (1024 * 1024), 1),
            "decodedPngMiBAfter": round(memory_after / (1024 * 1024), 1),
            "sourceModsUnchanged": before == after,
            "sourceContentJsonUnchanged": True,
            "runtimeCacheReused": reused.get("reused"),
        }
        print(json.dumps(report, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
