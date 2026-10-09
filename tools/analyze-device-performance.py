"""Summarize raw QA frames and 1 Hz procfs samples without merging nested timers."""
import argparse
import csv
import json
from pathlib import Path
import re
import statistics


def samples(path):
    current = None
    section = None
    for line in path.read_text(encoding="utf-8").splitlines():
        if line.startswith("@sample "):
            if current:
                yield current
            parts = line.split(maxsplit=2)
            current = {"utc": int(parts[1]), "label": parts[2] if len(parts) == 3 else "", "files": {}}
            section = None
        elif line.startswith("@file ") and current:
            section = line[6:]
            current["files"][section] = []
        elif current and section:
            current["files"][section].append(line)
    if current:
        yield current


def fields(lines):
    result = {}
    for line in lines:
        parts = line.replace(":", " ").split()
        if len(parts) > 1 and parts[1].isdigit():
            result[parts[0]] = int(parts[1])
    return result


def summary(values):
    values = [v for v in values if v is not None]
    return {"min": min(values), "mean": statistics.mean(values), "max": max(values)} if values else None


def mib(values, key):
    return values[key] / 1024 if key in values else None


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("directory", type=Path)
    args = parser.parse_args()
    out = args.directory
    results = json.loads((out / "results.json").read_text(encoding="utf-8"))
    raw = list(samples(out / "system-samples.txt"))
    records = []
    last = None
    for sample in raw:
        files = sample["files"]
        status_path = next((p for p in files if re.fullmatch(r"/proc/\d+/status", p)), None)
        if status_path is None:
            continue
        pid_root = status_path.rsplit("/", 1)[0]
        status = fields(files[status_path])
        if "VmRSS" not in status:
            continue
        memory = fields(files.get("/proc/meminfo", []))
        rollup = fields(files.get(pid_root + "/smaps_rollup", []))
        io = fields(files.get(pid_root + "/io", []))
        vm = fields(files.get("/proc/vmstat", []))
        stat = " ".join(files.get(pid_root + "/stat", [])).split(") ", 1)[-1].split()
        if len(stat) < 13:
            continue
        system_lines = files.get("/proc/stat", [])
        if not system_lines:
            continue
        system_total = sum(map(int, system_lines[0].split()[1:9]))
        cores = sum(bool(re.match(r"cpu\d+ ", line)) for line in system_lines)
        process_ticks = int(stat[11]) + int(stat[12])
        cpu = None
        if last and system_total > last["systemTotal"]:
            cpu = (process_ticks - last["processTicks"]) / (system_total - last["systemTotal"]) * cores * 100
        def scalar(path):
            value = "".join(files.get(path, []))
            match = re.match(r"\d+", value)
            return int(match.group()) if match else None
        temp = scalar("/sys/class/thermal/thermal_zone0/temp")
        records.append({"utc": sample["utc"], "label": sample["label"], "rssMiB": mib(status, "VmRSS"),
                        "pssMiB": mib(rollup, "Pss"), "anonymousMiB": mib(status, "RssAnon"),
                        "availableMiB": mib(memory, "MemAvailable"), "cpuOneCorePercent": cpu,
                        "cpuKHz": scalar("/sys/devices/system/cpu/cpu0/cpufreq/scaling_cur_freq"),
                        "gpuHz": scalar("/sys/class/devfreq/fde60000.gpu/cur_freq"),
                        "gpuLoad": scalar("/sys/class/devfreq/fde60000.gpu/load"),
                        "dmcHz": scalar("/sys/class/devfreq/dmc/cur_freq"),
                        "socTempC": temp / 1000 if temp is not None else None,
                        "directScan": vm.get("pgscan_direct"), "directSteal": vm.get("pgsteal_direct"),
                        "allocstall": sum(v for k, v in vm.items() if k.startswith("allocstall_")),
                        "systemMajorFaults": vm.get("pgmajfault"), "processMajorFaults": int(stat[9]),
                        "processReadBytes": io.get("read_bytes"), "processReadChars": io.get("rchar"),
                        "oomKills": vm.get("oom_kill"), "swapTotalKiB": memory.get("SwapTotal")})
        last = {"systemTotal": system_total, "processTicks": process_ticks}
    with (out / "system-samples.csv").open("w", newline="", encoding="utf-8") as f:
        writer = csv.DictWriter(f, fieldnames=records[0].keys())
        writer.writeheader()
        writer.writerows(records)
    per_run = []
    for result in results:
        matching = [s for s in records if s["label"] == result["label"]]
        # Labels remain during transitions. Only the interior of the known timed
        # duration is included; procfs boundaries are approximate to one second.
        if matching:
            first = matching[0]["utc"]
            matching = [s for s in matching if first + 1 <= s["utc"] <= first + result["seconds"] - 1]
        system = {"samples": len(matching)}
        for key in ("rssMiB", "pssMiB", "anonymousMiB", "availableMiB", "cpuOneCorePercent", "cpuKHz", "gpuHz", "gpuLoad", "dmcHz", "socTempC"):
            system[key] = summary([s[key] for s in matching])
        for key in ("directScan", "directSteal", "allocstall", "systemMajorFaults", "processMajorFaults", "processReadBytes", "processReadChars", "oomKills"):
            system[key + "Delta"] = matching[-1][key] - matching[0][key] if len(matching) > 1 and matching[0][key] is not None else None
        per_run.append({**result, "system": system})
    groups = {}
    for r in per_run:
        name = re.sub(r"-r\d+-", "-", r["label"])
        groups.setdefault(name, []).append(r)
    grouped = []
    for name, runs in groups.items():
        row = {"condition": name, "repeats": len(runs), "totalSeconds": sum(r["seconds"] for r in runs),
               "fps": summary([r["fps"] for r in runs]), "p95Ms": summary([r["frameMs"]["p95"] for r in runs]),
               "p99Ms": summary([r["frameMs"]["p99"] for r in runs]), "maxFrameMs": max(r["frameMs"]["max"] for r in runs),
               "over50Percent": sum(r["frameMs"]["over50"] for r in runs) / sum(r["frames"] for r in runs) * 100,
               "allocationMiBPerSecond": summary([r["allocatedMiBPerSecond"] for r in runs]),
               "gcPerMinute": [sum(r["gc"][i] for r in runs) * 60 / sum(r["seconds"] for r in runs) for i in range(3)],
               "managedMiB": summary([r["managedMiB"] for r in runs]), "actions": sum(r["actions"] for r in runs),
               "removed": sum(r["removed"] for r in runs), "maxDebris": max(r["maxDebris"] for r in runs), "maxSprites": max(r["maxSprites"] for r in runs), "boundaries": {}}
        for boundary in runs[0]["boundaries"]:
            matches = [b for r in runs for b in r["boundaries"] if b["name"] == boundary["name"]]
            row["boundaries"][boundary["name"]] = {"msPerFrame": statistics.mean(b["msPerFrame"] for b in matches),
                                                       "calls": sum(b["calls"] for b in matches),
                                                       "allocatedMiBPerSecond": sum(b["allocatedBytes"] for b in matches) / row["totalSeconds"] / 1048576}
        for key in ("rssMiB", "pssMiB", "anonymousMiB", "availableMiB", "cpuOneCorePercent", "cpuKHz", "gpuHz", "gpuLoad", "dmcHz", "socTempC"):
            values = [r["system"][key] for r in runs if r["system"][key]]
            row[key] = {"min": min(v["min"] for v in values), "mean": statistics.mean(v["mean"] for v in values), "max": max(v["max"] for v in values)} if values else None
        for key in ("directScanDelta", "directStealDelta", "allocstallDelta", "systemMajorFaultsDelta", "processMajorFaultsDelta", "oomKillsDelta"):
            values = [r["system"][key] for r in runs if r["system"][key] is not None]
            row[key] = sum(values) if values else None
        grouped.append(row)
    (out / "analysis.json").write_text(json.dumps({"runs": per_run, "conditions": grouped, "resourceSampling": "1 Hz; window edges approximate; no PSI available", "nestedBoundaries": True}, indent=2, ensure_ascii=False), encoding="utf-8")
    with (out / "conditions.csv").open("w", newline="", encoding="utf-8-sig") as f:
        columns = ["condition", "repeats", "fps", "p95Ms", "p99Ms", "over50Percent", "allocationMiBPerSecond", "rssMiB", "availableMiB", "socTempC", "directScanDelta", "allocstallDelta", "actions", "removed"]
        writer = csv.DictWriter(f, fieldnames=columns)
        writer.writeheader()
        for row in grouped:
            writer.writerow({k: row[k]["mean"] if isinstance(row[k], dict) else row[k] for k in columns})
    print(f"Runs={len(results)} Conditions={len(grouped)} SystemSamples={len(records)} MeasuredMinutes={sum(r['seconds'] for r in results)/60:.1f}")
    for row in grouped:
        print(f"{row['condition']:38} fps={row['fps']['mean']:6.2f} p95={row['p95Ms']['mean']:7.2f} alloc={row['allocationMiBPerSecond']['mean']:6.2f} >50ms={row['over50Percent']:5.1f}%")


if __name__ == "__main__":
    main()
