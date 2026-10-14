"""Build the private MOD runtime used by the RGDSplus compatibility layer.

The user's Mods directory is read-only input. The launcher gives SMAPI a
separate runtime directory under the release package, where enabled MODs are
copied and oversized portrait PNGs are reduced without changing cropped source
images used by Content Patcher's FromArea actions.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import re
import shutil
import subprocess
import struct
import tempfile
import zlib

PORTRAIT_MOD_IDS = frozenset(value.casefold() for value in (
    "OO.Portraits", "OO.Portraits2", "Mud.SkimpyPortraits", "Mud.SkimpyPortraits2"))
PNG_THRESHOLD = 512
PNG_SCALE = 0.125
COMPAT_RULES_VERSION = 9
LOCKED_IDS = {value.casefold() for value in ("RGDSplus.VerticalWorld", "SMAPI.ConsoleCommands", "SMAPI.SaveBackup",
                                             "RGDSplus.PerformanceProbe", "RGDSplus.RenderScaleProbe")}
MARKER = ".rgds-runtime.json"
PNG_SIGNATURE = b"\x89PNG\r\n\x1a\n"
SKIP_NAMES = {".rgds-mod-state.json", ".rgds-mod-result.json", "mod-state.json"}
SKIP_DIR_PREFIXES = ("RGDSplus.ModCompatibility--",)
LEGACY_DISABLED_PREFIX = ".rgds-disabled--"


def digest(path):
    hasher = hashlib.sha256()
    with Path(path).open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            hasher.update(chunk)
    return hasher.hexdigest()


def read_json(path):
    text = path.read_text(encoding="utf-8-sig")
    text = re.sub(r'("(?:\\.|[^"\\])*")|(/\*[\s\S]*?\*/|//[^\r\n]*)',
                  lambda m: m[1] or "", text)
    text = re.sub(r'("(?:\\.|[^"\\])*")|(,\s*(?=[}\]]))',
                  lambda m: m[1] or "", text)
    return json.loads(text)


def scan_mods(root):
    result, warnings = [], []

    def visit(folder, depth):
        if depth > 8 or folder.is_symlink():
            return
        if depth and ((folder.name.startswith(".") and not folder.name.startswith(LEGACY_DISABLED_PREFIX))
                      or any(folder.name.startswith(prefix) for prefix in SKIP_DIR_PREFIXES)):
            return
        try:
            paths = sorted(folder.iterdir(), key=lambda item: item.name.casefold())
        except OSError as error:
            warnings.append(f"{folder.name}: {error}")
            return
        manifest = next((p for p in paths if p.name.casefold() == "manifest.json" and p.is_file()), None)
        if manifest:
            try:
                data = read_json(manifest)
                if not isinstance(data, dict):
                    raise ValueError("manifest root must be an object")
                identifier = str(data.get("UniqueID", data.get("UniqueId", ""))).strip()
            except (OSError, ValueError, TypeError, AttributeError):
                identifier = ""
            if not identifier:
                warnings.append(f"{folder.name}: missing or invalid manifest UniqueID")
            else:
                dependencies = []
                for dependency in data.get("Dependencies", []) or []:
                    if isinstance(dependency, dict) and dependency.get("IsRequired", True):
                        dep_id = dependency.get("UniqueID", dependency.get("UniqueId"))
                        if dep_id:
                            dependencies.append(str(dep_id).casefold())
                content_pack = data.get("ContentPackFor")
                if isinstance(content_pack, dict):
                    dep_id = content_pack.get("UniqueID", content_pack.get("UniqueId"))
                    if dep_id:
                        dependencies.append(str(dep_id).casefold())
                result.append({"path": folder, "id": identifier.casefold(), "required": dependencies})
            return
        for child in paths:
            if child.is_dir():
                visit(child, depth + 1)

    visit(root, 0)
    seen, unique = set(), []
    for entry in sorted(result, key=lambda e: e["path"].name.startswith(LEGACY_DISABLED_PREFIX)):
        if entry["id"] in seen:
            warnings.append(f"duplicate UniqueID skipped: {entry['id']}")
            continue
        seen.add(entry["id"])
        unique.append(entry)
    return unique, warnings


def load_choices(state):
    state = Path(state)
    if not state.exists():
        return {}
    try:
        data = read_json(state)
    except (OSError, ValueError, TypeError, AttributeError):
        return {}
    if not isinstance(data, dict):
        return {}
    return {str(key).casefold(): value for key, value in data.items() if type(value) is bool}


def png_dimensions(path):
    with path.open("rb") as stream:
        if stream.read(8) != PNG_SIGNATURE or stream.read(4) != b"\x00\x00\x00\r" or stream.read(4) != b"IHDR":
            raise ValueError("invalid PNG header: " + str(path))
        return struct.unpack(">II", stream.read(8))


def should_resize_image(path, root):
    # Preserve sprite sheets: their frame geometry is unrelated to DDF portraits.
    portrait = any(part.casefold() in {"portrait", "portraits"} for part in path.relative_to(root).parts[:-1])
    width, height = png_dimensions(path)
    return portrait and (width >= PNG_THRESHOLD or height >= PNG_THRESHOLD)


def png_read(path):
    data = path.read_bytes()
    if not data.startswith(PNG_SIGNATURE):
        raise ValueError("not a PNG: " + str(path))
    pos, width, height, depth, color, interlace = len(PNG_SIGNATURE), None, None, None, None, None
    chunks = []
    while pos + 12 <= len(data):
        length = struct.unpack(">I", data[pos:pos + 4])[0]
        kind, body = data[pos + 4:pos + 8], data[pos + 8:pos + 8 + length]
        pos += length + 12
        if kind == b"IHDR":
            width, height, depth, color, _, _, interlace = struct.unpack(">IIBBBBB", body)
        elif kind == b"IDAT":
            chunks.append(body)
        elif kind == b"IEND":
            break
    if (depth, interlace) != (8, 0) or color not in (2, 6):
        raise ValueError("unsupported PNG format: " + str(path))
    channels, raw = (3 if color == 2 else 4), zlib.decompress(b"".join(chunks))
    stride, rows, offset, previous = width * channels, [], 0, bytearray(width * channels)
    for _ in range(height):
        filter_type, offset = raw[offset], offset + 1
        current, offset = bytearray(raw[offset:offset + stride]), offset + stride
        for index in range(stride):
            left = current[index - channels] if index >= channels else 0
            up, upper_left = previous[index], previous[index - channels] if index >= channels else 0
            if filter_type == 1:
                current[index] = (current[index] + left) & 255
            elif filter_type == 2:
                current[index] = (current[index] + up) & 255
            elif filter_type == 3:
                current[index] = (current[index] + ((left + up) // 2)) & 255
            elif filter_type == 4:
                estimate = left + up - upper_left
                pa, pb, pc = abs(estimate - left), abs(estimate - up), abs(estimate - upper_left)
                predictor = left if pa <= pb and pa <= pc else up if pb <= pc else upper_left
                current[index] = (current[index] + predictor) & 255
            elif filter_type != 0:
                raise ValueError("unsupported PNG filter: " + str(filter_type))
        if channels == 3:
            expanded = bytearray(width * 4)
            for x in range(width):
                expanded[x * 4:x * 4 + 3] = current[x * 3:x * 3 + 3]
                expanded[x * 4 + 3] = 255
            rows.append(expanded)
        else:
            rows.append(current)
        previous = current
    return width, height, rows


def png_write(path, width, height, rows):
    def chunk(kind, body):
        return struct.pack(">I", len(body)) + kind + body + struct.pack(">I", zlib.crc32(kind + body) & 0xFFFFFFFF)
    raw = bytearray()
    for row in rows:
        raw.append(0)
        raw.extend(row)
    ihdr = struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0)
    path.write_bytes(PNG_SIGNATURE + chunk(b"IHDR", ihdr) + chunk(b"IDAT", zlib.compress(bytes(raw), 6)) + chunk(b"IEND", b""))


def downsample_png(source, target):
    width, height = png_dimensions(source)
    if width < PNG_THRESHOLD and height < PNG_THRESHOLD:
        shutil.copy2(source, target)
        return False
    width, height, rows = png_read(source)
    factor = round(1 / PNG_SCALE)
    target_width, target_height = max(1, (width + factor - 1) // factor), max(1, (height + factor - 1) // factor)
    reduced = []
    for y in range(target_height):
        row = bytearray(target_width * 4)
        for x in range(target_width):
            alpha_sum = red_sum = green_sum = blue_sum = 0
            for dy in range(factor):
                source_row = rows[min(height - 1, y * factor + dy)]
                for dx in range(factor):
                    source_offset = min(width - 1, x * factor + dx) * 4
                    red, green, blue, alpha = source_row[source_offset:source_offset + 4]
                    alpha_sum += alpha
                    red_sum += red * alpha
                    green_sum += green * alpha
                    blue_sum += blue * alpha
            output_offset = x * 4
            if alpha_sum:
                row[output_offset:output_offset + 4] = bytes((
                    (red_sum + alpha_sum // 2) // alpha_sum,
                    (green_sum + alpha_sum // 2) // alpha_sum,
                    (blue_sum + alpha_sum // 2) // alpha_sum,
                    (alpha_sum + factor * factor // 2) // (factor * factor)))
            else:
                row[output_offset:output_offset + 4] = b"\x00\x00\x00\x00"
        reduced.append(row)
    png_write(target, target_width, target_height, reduced)
    return True


def _iter_actions(value):
    if isinstance(value, dict):
        if "Action" in value:
            yield value
        for child in value.values():
            yield from _iter_actions(child)
    elif isinstance(value, list):
        for child in value:
            yield from _iter_actions(child)


def _area_paths(mod_path, action):
    template = action.get("FromFile")
    if not isinstance(template, str) or not template.casefold().endswith(".png"):
        return [], False, None
    targets = action.get("Target")
    if isinstance(targets, str):
        target_values = [part.strip() for part in targets.split(",") if part.strip()]
    elif isinstance(targets, list):
        target_values = [str(part).strip() for part in targets if str(part).strip()]
    else:
        target_values = [""]
    paths, unresolved = [], False
    for target in target_values:
        candidate = template.replace("{{Target}}", target)
        candidate = candidate.replace("{{TargetWithoutPath}}", PurePosixPath(target).name)
        if "{{" in candidate:
            unresolved = True
            continue
        relative = PurePosixPath(candidate.replace("\\", "/"))
        if relative.is_absolute() or ".." in relative.parts:
            unresolved = True
            continue
        path = Path(mod_path).joinpath(*relative.parts)
        if path.is_file() and not path.is_symlink():
            paths.append(path)
        else:
            unresolved = True
    return paths, unresolved, template


def _area_is_aligned(action):
    area = action.get("FromArea")
    divisor = round(1 / PNG_SCALE)
    return isinstance(area, dict) and all(
        type(area.get(key)) is int and area[key] % divisor == 0
        for key in ("X", "Y", "Width", "Height"))


def _area_fallback_pngs(mod_path, template):
    prefix = template.split("{{", 1)[0].replace("\\", "/")
    directory = prefix.rsplit("/", 1)[0] if "/" in prefix.rstrip("/") else ""
    base = Path(mod_path) / directory if directory else Path(mod_path)
    if not base.is_dir():
        return set()
    return {path.relative_to(mod_path).as_posix().casefold()
            for path in base.rglob("*.png")
            if path.is_file() and not path.is_symlink()}


def _from_area_info(mod_path):
    """Find crop inputs and keep only ambiguous or non-aligned ones full-size."""
    referenced, preserved = set(), set()
    for json_path in Path(mod_path).rglob("*.json"):
        if json_path.is_symlink():
            continue
        try:
            data = read_json(json_path)
        except (OSError, ValueError, TypeError):
            continue
        for action in _iter_actions(data):
            if "FromArea" not in action:
                continue
            paths, unresolved, template = _area_paths(mod_path, action)
            references = {path.relative_to(mod_path).as_posix().casefold() for path in paths}
            referenced.update(references)
            if unresolved:
                # An unknown Content Patcher token makes the exact file set
                # ambiguous. Preserve PNGs in the nearest known asset folder.
                preserved.update(references)
                if template:
                    preserved.update(_area_fallback_pngs(mod_path, template))
            elif not _area_is_aligned(action):
                # Integer coordinates that aren't divisible by the image scale
                # can't be represented exactly in the resized source.
                preserved.update(references)
            else:
                size_classes = set()
                for path in paths:
                    try:
                        width, height = png_dimensions(path)
                    except (OSError, ValueError, struct.error):
                        continue
                    size_classes.add(should_resize_image(path, Path(mod_path)))
                if len(size_classes) > 1:
                    # One FromArea rectangle can't be scaled for only some
                    # targets in a comma-separated Content Patcher action.
                    for path in paths:
                        try:
                            width, height = png_dimensions(path)
                        except (OSError, ValueError, struct.error):
                            continue
                        if should_resize_image(path, Path(mod_path)):
                            preserved.add(path.relative_to(mod_path).as_posix().casefold())
    return {"referenced": referenced, "preserved": preserved}


def _from_area_sources(mod_path):
    """Return every PNG referenced by a Content Patcher FromArea action."""
    details = _from_area_info(mod_path)
    return details["referenced"] | details["preserved"]


def _rewrite_scaled_from_area_json(source, target, mod_path, resized_sources):
    try:
        data = read_json(source)
    except (OSError, ValueError, TypeError):
        shutil.copy2(source, target)
        return 0
    resized_sources = {value.casefold() for value in resized_sources}
    scaled = 0
    divisor = round(1 / PNG_SCALE)
    for action in _iter_actions(data):
        if "FromArea" not in action or not _area_is_aligned(action):
            continue
        paths, unresolved, _ = _area_paths(mod_path, action)
        references = {path.relative_to(mod_path).as_posix().casefold() for path in paths}
        if unresolved or not references or not references.issubset(resized_sources):
            continue
        area = action["FromArea"]
        for key in ("X", "Y", "Width", "Height"):
            area[key] //= divisor
        scaled += 1
    # DDF reads the PNG in expression-sized tiles, not as one portrait. Keep
    # those source rectangles in the same coordinate space as the reduced PNG.
    # Only the four explicitly supported packs reach this transformation.
    portraits = 0
    if resized_sources:
        for action in _iter_actions(data):
            if str(action.get("Target", "")).casefold() != "aedenthorn.dialoguedisplayframework/dictionary":
                continue
            for entry in (action.get("Entries") or {}).values():
                if not isinstance(entry, dict):
                    continue
                # DDF inherits portrait geometry but doesn't inherit packName.
                # Tag aliases in our private copy so inherited layouts fit too.
                if "packName" not in entry and any(key.casefold() == "copyfrom" for key in entry):
                    entry["packName"] = "RGDSplus.CompatiblePortraits"
                    portraits += 1
                portrait = entry.get("portrait")
                if not isinstance(portrait, dict):
                    continue
                if all(type(portrait.get(k)) is int and portrait[k] >= 128 and portrait[k] % divisor == 0 for k in ("w", "h")):
                    for key in ("w", "h", "x", "y"):
                        if type(portrait.get(key)) is int and portrait[key] >= 0:
                            portrait[key] //= divisor
                    # Preserve intended physical size before the viewport
                    # adapter fits it; this also keeps compatibility-off clear.
                    portrait["scale"] = portrait.get("scale", 4) * divisor
                    portraits += 1
    if scaled or portraits:
        target.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    else:
        shutil.copy2(source, target)
    return scaled


def fingerprint(entries, choices, mode, resizer_fingerprint):
    hasher = hashlib.sha256()
    hasher.update(str(COMPAT_RULES_VERSION).encode())
    hasher.update(str(PNG_THRESHOLD).encode())
    hasher.update(str(PNG_SCALE).encode())
    hasher.update("\0".join(sorted(PORTRAIT_MOD_IDS)).encode())
    hasher.update(resizer_fingerprint.encode())
    hasher.update(mode.encode())
    hasher.update(json.dumps(choices, sort_keys=True).encode())
    for entry in entries:
        hasher.update(entry["id"].encode())
        for path in sorted(entry["path"].rglob("*"), key=lambda p: p.as_posix().casefold()):
            if path.is_file() and not path.is_symlink():
                stat = path.stat()
                hasher.update(str(path).encode())
                hasher.update(f"{stat.st_size}:{stat.st_mtime_ns}:{stat.st_ctime_ns}".encode())
    return hasher.hexdigest()


def mutable_runtime_file(path, root):
    parts = path.relative_to(root).parts
    return (path.name.casefold() == "config.json" or path.suffix.casefold() == ".log"
            or any(part.casefold() in {"data", "companion-history", "save-backups"} for part in parts))


def runtime_static_files(root):
    return [path for path in root.rglob("*") if path.is_file() and path.name != MARKER
            and not mutable_runtime_file(path, root)]


def save_runtime_data(runtime):
    """Keep user settings across toggles/rebuilds, entirely inside private state."""
    state = runtime.parent / "mod-data"
    if not runtime.is_dir():
        return
    entries, _ = scan_mods(runtime)
    for entry in entries:
        # Hash IDs to prevent a third-party manifest from creating a path.
        destination = state / hashlib.sha256(entry["id"].encode()).hexdigest()
        for path in entry["path"].rglob("*"):
            if path.is_file() and not path.is_symlink() and path.suffix.casefold() != ".log" and mutable_runtime_file(path, runtime):
                target = destination / path.relative_to(entry["path"])
                target.parent.mkdir(parents=True, exist_ok=True)
                shutil.copy2(path, target)


def restore_runtime_data(runtime, state):
    entries, _ = scan_mods(runtime)
    for entry in entries:
        saved = state / hashlib.sha256(entry["id"].encode()).hexdigest()
        if saved.is_dir():
            for path in saved.rglob("*"):
                if path.is_file() and not path.is_symlink():
                    target = entry["path"] / path.relative_to(saved)
                    if mutable_runtime_file(target, runtime):
                        target.parent.mkdir(parents=True, exist_ok=True)
                        shutil.copy2(path, target)


def build_runtime(root, runtime, defaults=None, mode="auto", resizer=None, state_file=None):
    root = Path(root).resolve(strict=True)
    runtime = Path(os.path.abspath(runtime))
    if root == runtime or root in runtime.parents:
        raise ValueError("private runtime must not be inside the source MOD directory")
    if root.parent not in runtime.parents:
        raise ValueError("private runtime must stay inside the release package")
    for parent in [runtime, *runtime.parents]:
        if parent == root.parent:
            break
        if parent.is_symlink():
            raise ValueError("symlink in private runtime path: " + str(parent))
    runtime.parent.mkdir(parents=True, exist_ok=True)
    entries, warnings = scan_mods(root)
    default_entries, default_warnings = scan_mods(defaults) if defaults and Path(defaults).is_dir() else ([], [])
    warnings.extend(default_warnings)
    default_ids = {entry["id"] for entry in default_entries}
    choices = load_choices(state_file) if state_file else {}
    resizer_path = Path(resizer) if resizer else None
    resizer_fingerprint = digest(resizer_path) if resizer_path and resizer_path.is_file() else "python-fallback"
    current = fingerprint(entries, choices, mode, resizer_fingerprint)
    marker = runtime / MARKER
    if marker.exists():
        try:
            previous = read_json(marker)
            runtime_files = runtime_static_files(runtime)
            valid_runtime = (previous.get("fingerprint") == current
                             and previous.get("fileCount") == len(runtime_files)
                             and previous.get("runtimeDigest") == runtime_digest(runtime_files, runtime))
            if valid_runtime:
                return {"applied": True, "runtime": str(runtime), "generated": True,
                        "resizedPngs": previous.get("resizedPngs", 0),
                        "scaledFromAreaActions": previous.get("scaledFromAreaActions", 0),
                        "warnings": warnings, "reused": True}
        except (OSError, ValueError, TypeError):
            pass
    if runtime.exists():
        if runtime.is_symlink() or not runtime.is_dir():
            raise ValueError("private runtime path is not a regular directory: " + str(runtime))
        save_runtime_data(runtime)
        shutil.rmtree(runtime)
    temporary = Path(tempfile.mkdtemp(prefix=".rgds-runtime-build-", dir=str(runtime.parent)))
    try:
        if defaults and Path(defaults).is_dir():
            for item in sorted(Path(defaults).iterdir(), key=lambda p: p.name.casefold()):
                if item.is_dir():
                    shutil.copytree(item, temporary / item.name, symlinks=False)
        def default_enabled(entry):
            if not entry["path"].name.startswith(LEGACY_DISABLED_PREFIX):
                return True
            # Recover only originals hidden by the old generated compatibility
            # MOD. An ordinary legacy user-disabled MOD stays disabled.
            return (entry["id"] in PORTRAIT_MOD_IDS and any(
                path.is_dir() and path.name.casefold() == ("RGDSplus.ModCompatibility--" + entry["id"]).casefold()
                for path in entry["path"].parent.iterdir()))
        enabled = {entry["id"]: (True if entry["id"] in LOCKED_IDS else choices.get(entry["id"], default_enabled(entry))) for entry in entries}
        changed = True
        while changed:
            changed = False
            for entry in entries:
                if not enabled[entry["id"]]:
                    continue
                missing = [dependency for dependency in entry["required"] if not enabled.get(dependency, False)]
                if missing:
                    enabled[entry["id"]] = False
                    warnings.append(f"{entry['id']} skipped because required MOD is disabled or missing: {', '.join(missing)}")
                    changed = True
        entry_paths = {Path(entry["path"]).resolve() for entry in entries}
        target_ids = PORTRAIT_MOD_IDS
        area_info = {entry["id"]: _from_area_info(entry["path"])
                     for entry in entries if entry["id"] in target_ids}
        resize_sources = {identifier: set() for identifier in area_info}
        if mode != "off":
            for entry in entries:
                if entry["id"] not in area_info:
                    continue
                for image in entry["path"].rglob("*.png"):
                    if image.is_symlink() or not image.is_file():
                        continue
                    try:
                        width, height = png_dimensions(image)
                    except (OSError, ValueError, struct.error):
                        continue
                    relative_image = image.relative_to(entry["path"]).as_posix().casefold()
                    if (should_resize_image(image, entry["path"])
                            and relative_image not in area_info[entry["id"]]["preserved"]):
                        resize_sources[entry["id"]].add(relative_image)
        resized, copied, scaled_areas, resize_jobs = 0, 0, 0, []
        resize_cache_jobs = []
        portrait_records = []
        image_cache = runtime.parent / "portrait-cache"
        image_cache.mkdir(exist_ok=True)
        for item in sorted(root.iterdir(), key=lambda p: p.name.casefold()):
            legacy = item.name.startswith(LEGACY_DISABLED_PREFIX)
            if item.name in SKIP_NAMES or (item.name.startswith(".") and not legacy) or any(item.name.startswith(prefix) for prefix in SKIP_DIR_PREFIXES):
                continue
            if item.is_symlink():
                warnings.append(f"symlink skipped: {item.name}")
                continue
            # Older releases renamed originals and installed a generated MOD
            # beside them. Read that original in place; never rename it again.
            destination = temporary / ("RGDSplus.LegacySource--" + item.name[len(LEGACY_DISABLED_PREFIX):] if legacy else item.name)
            if item.is_file():
                shutil.copy2(item, destination)
                copied += 1
                continue
            if not item.is_dir():
                continue
            item_manifest = item / "manifest.json"
            top = next((e for e in entries if e["path"] == item), None)
            if item_manifest.exists() and top is None:
                warnings.append(f"invalid or duplicate MOD skipped: {item.name}")
                continue
            if top and top["id"] in default_ids:
                continue
            if top and not enabled.get(top["id"], True):
                continue
            for source in item.rglob("*"):
                if source.is_dir() or source.is_symlink():
                    if source.is_symlink():
                        warnings.append(f"symlink skipped: {source}")
                    continue
                relative = source.relative_to(item)
                manifest_parent = next((parent for parent in [source.parent, *source.parents]
                                        if (parent / "manifest.json").exists()), None)
                if manifest_parent is not None and manifest_parent.resolve() not in entry_paths:
                    continue
                ancestors = [e for e in entries if e["path"] == source.parent or e["path"] in source.parents]
                if any(e["id"] in default_ids for e in ancestors):
                    continue
                if any(not enabled.get(e["id"], True) for e in ancestors):
                    continue
                target_entries = [e for e in entries if e["id"] in target_ids and e["path"] in source.parents]
                target_mod = bool(target_entries)
                preserve_crop_source = any(
                    source.relative_to(e["path"]).as_posix().casefold()
                    in area_info.get(e["id"], {}).get("preserved", set())
                    for e in target_entries)
                out = destination / relative
                out.parent.mkdir(parents=True, exist_ok=True)
                if target_mod and not preserve_crop_source and source.suffix.casefold() == ".png" and mode != "off":
                    try:
                        width, height = png_dimensions(source)
                        if any(should_resize_image(source, entry["path"]) for entry in target_entries):
                            key = hashlib.sha256((digest(source) + resizer_fingerprint + str(PNG_SCALE)).encode()).hexdigest()
                            cached = image_cache / (key + ".png")
                            factor = round(1 / PNG_SCALE)
                            expected_size = ((width + factor - 1) // factor, (height + factor - 1) // factor)
                            cache_valid = False
                            if cached.is_file():
                                try:
                                    cache_valid = png_dimensions(cached) == expected_size and Path(str(cached) + ".pixels.json").is_file()
                                except (OSError, ValueError, struct.error):
                                    pass
                            if cache_valid:
                                shutil.copy2(cached, out)
                                shutil.copy2(Path(str(cached) + ".pixels.json"), Path(str(out) + ".pixels.json"))
                                resized += 1
                            elif not resizer:
                                resized += int(downsample_png(source, out))
                                shutil.copy2(out, cached)
                            elif not Path(resizer).is_file():
                                raise FileNotFoundError("packaged image resizer is missing")
                            else:
                                shutil.copy2(source, out)
                                resize_jobs.append((source, out))
                                resize_cache_jobs.append((out, cached))
                            portrait_records.append((source, out, cached))
                        else:
                            shutil.copy2(source, out)
                    except (OSError, ValueError, struct.error) as error:
                        shutil.copy2(source, out)
                        warnings.append(f"could not resize {source.name}: {error}")
                elif target_mod and mode != "off" and source.suffix.casefold() == ".json":
                    area_entry = None
                    for candidate in target_entries:
                        try:
                            source.relative_to(candidate["path"])
                            area_entry = candidate
                            break
                        except ValueError:
                            continue
                    if area_entry:
                        scaled_areas += _rewrite_scaled_from_area_json(
                            source, out, area_entry["path"], resize_sources.get(area_entry["id"], set()))
                    else:
                        shutil.copy2(source, out)
                else:
                    shutil.copy2(source, out)
                copied += 1
        if resize_jobs:
            list_path = None
            try:
                with tempfile.NamedTemporaryFile("w", encoding="utf-8", newline="", prefix=".rgds-resize-", suffix=".jsonl",
                                                 dir=str(runtime.parent), delete=False) as stream:
                    list_path = Path(stream.name)
                    for source, target in resize_jobs:
                        stream.write(json.dumps([str(source), str(target)], ensure_ascii=False) + "\n")
                dotnet_root = os.environ.get("DOTNET_ROOT", "")
                dotnet = str(Path(dotnet_root) / "dotnet") if dotnet_root else "dotnet"
                completed = subprocess.run([dotnet, str(resizer), "--batch", str(list_path), "--factor=" + str(round(1 / PNG_SCALE))], check=True,
                                           text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
                match = re.search(r"resized=(\d+)", completed.stdout)
                resized += int(match.group(1)) if match else 0
                for output, cached in resize_cache_jobs:
                    shutil.copy2(output, cached)
                    metadata = Path(str(output) + ".pixels.json")
                    if metadata.is_file():
                        shutil.copy2(metadata, Path(str(cached) + ".pixels.json"))
            finally:
                if list_path:
                    list_path.unlink(missing_ok=True)
        portrait_sources = {}
        for source, output, cached in portrait_records:
            metadata = Path(str(output) + ".pixels.json")
            if metadata.is_file():
                pixels = read_json(metadata)
                portrait_sources[pixels["hash"]] = dict(pixels, source=str(source), factor=round(1 / PNG_SCALE), cacheKey=cached.stem)
                metadata.unlink()
        restore_runtime_data(temporary, runtime.parent / "mod-data")
        runtime_files = runtime_static_files(temporary)
        runtime_hash = runtime_digest(runtime_files, temporary)
        (temporary / MARKER).write_text(json.dumps({"kind": "RGDSplus private MOD runtime", "fingerprint": current,
            "source": str(root), "copiedFiles": copied, "resizedPngs": resized,
            "portraitSources": portrait_sources,
            "scaledFromAreaActions": scaled_areas, "fileCount": len(runtime_files),
            "runtimeDigest": runtime_hash}, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
        temporary.rename(runtime)
    except Exception:
        shutil.rmtree(temporary, ignore_errors=True)
        raise
    return {"applied": True, "runtime": str(runtime), "generated": True, "resizedPngs": resized,
            "scaledFromAreaActions": scaled_areas, "warnings": warnings, "reused": False}


def runtime_digest(files, root):
    hasher = hashlib.sha256()
    for path in sorted(files, key=lambda p: p.relative_to(root).as_posix().casefold()):
        hasher.update(path.relative_to(root).as_posix().encode())
        hasher.update(digest(path).encode())
    return hasher.hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--mods", type=Path, required=True)
    parser.add_argument("--runtime", type=Path, required=True)
    parser.add_argument("--state-file", type=Path, required=True)
    parser.add_argument("--defaults", type=Path)
    parser.add_argument("--mode", choices=("auto", "off"), default="auto")
    parser.add_argument("--resizer", type=Path)
    args = parser.parse_args()
    try:
        result = build_runtime(args.mods, args.runtime, args.defaults, args.mode, args.resizer, args.state_file)
    except Exception as error:
        result = {"applied": False, "error": str(error), "runtime": str(args.runtime)}
    print("[mod-compat] " + json.dumps(result, ensure_ascii=False))
    return 0 if result["applied"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
