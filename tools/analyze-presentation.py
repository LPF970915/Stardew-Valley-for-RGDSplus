"""Summarize compositor feedback separately from renderer frame timing."""
import argparse
import collections
import csv
import json
from pathlib import Path

p = argparse.ArgumentParser(description=__doc__)
p.add_argument("directory", type=Path)
a = p.parse_args()
for file in sorted(a.directory.glob("*-presentation.csv")):
    rows = [{k: int(v) for k, v in r.items()} for r in csv.DictReader(file.open())]
    completed = [r for r in rows if not r["discarded"]]
    # Old captures requested feedback at Game.EndDraw, not at buffer commit.
    # Deduplication alone does not establish one feedback per rendered buffer.
    shown = sorted({r["presented_ns"]: r for r in completed}.values(), key=lambda r: r["presented_ns"])
    intervals = [(b["presented_ns"] - a["presented_ns"]) / 1e6 for a, b in zip(shown, shown[1:])]
    ordered = sorted(intervals)
    seconds = collections.Counter((r["presented_ns"]-shown[0]["presented_ns"])//1_000_000_000 for r in shown) if shown else {}
    complete_seconds = [seconds.get(i,0) for i in range(1, max(seconds, default=0))]
    def q(f):
        return ordered[min(len(ordered) - 1, int(len(ordered) * f))] if ordered else None
    print(json.dumps(dict(label=file.stem, commitAligned=bool(rows and "buffer_commit" in rows[0]),
        bufferCommits=rows[0].get("total_buffers") if rows else None,
        feedback=len(rows), shown=len(shown), sameRefresh=len(completed)-len(shown),
        discarded=sum(r["discarded"] for r in rows),
        shownFps=(len(shown)-1)*1e9/(shown[-1]["presented_ns"]-shown[0]["presented_ns"]) if len(shown)>1 else None,
        intervalMs=dict(p50=q(.5),p95=q(.95),p99=q(.99),max=q(1)),
        fullSecondFps=dict(min=min(complete_seconds),max=max(complete_seconds)) if complete_seconds else None,
        holds=dict(collections.Counter(round((b["presented_ns"]-a["presented_ns"])/b["refresh_ns"])
            for a,b in zip(shown,shown[1:]) if b["refresh_ns"])),
        flags=dict(collections.Counter(r["flags"] for r in shown)),
        outputs=dict(collections.Counter(r["outputs"] for r in shown)))))
