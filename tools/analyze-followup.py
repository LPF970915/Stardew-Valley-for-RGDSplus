"""Compare first-use windows and commit-aligned display pacing separately."""
import argparse
import collections
import csv
import json
from pathlib import Path
import statistics

def analyze(folder):
    samples=[]
    for file in sorted(folder.glob("*-frames.csv")):
        label=file.name.removesuffix("-frames.csv")
        if not (folder/(label+"-state.json")).exists(): continue
        result=json.loads((folder/(label+".json")).read_text(encoding="utf-8"))
        rows=list(csv.DictReader(file.open()))
        first_row=next((r for r in rows if int(r["action_number"])>0),None)
        # Include the interval containing the first action, not an extra two
        # seconds starting after its potentially long JIT stall has ended.
        first=float(first_row["elapsed_ms"])-float(first_row["frame_ms"]) if first_row else None
        cold=[r for r in rows if first is not None and int(r["action_number"])>0
              and (float(r["elapsed_ms"])-float(r["frame_ms"]) < first+2000)]
        item=dict(label=label, fps=result["fps"], first2sMaxMs=max((float(r["frame_ms"]) for r in cold),default=None),
                  first2sJitMs=sum(float(r["jit_ms"]) for r in cold), maxMs=result["frameMs"]["max"],
                  over50=result["frameMs"]["over50"], managedMiB=result["managedMiB"])
        updates=next((b for b in result["boundaries"] if b["name"]=="Game.DoUpdate"),None)
        if updates: item["updateHz"]=updates["calls"]/result["seconds"]
        path=folder/(label+"-presentation.csv")
        if path.exists():
            feedback=[{k:int(v) for k,v in r.items()} for r in csv.DictReader(path.open())]
            shown=sorted((r for r in feedback if not r["discarded"]),key=lambda r:r["presented_ns"])
            if len(shown)>1:
                intervals=[(b["presented_ns"]-a["presented_ns"])/1e6 for a,b in zip(shown,shown[1:])]
                holds=collections.Counter(round((b["presented_ns"]-a["presented_ns"])/b["refresh_ns"]) for a,b in zip(shown,shown[1:]))
                item.update(shownFps=(len(shown)-1)*1000/sum(intervals), holds=dict(holds),
                    longHoldPercent=sum(n for k,n in holds.items() if k>=3)*100/len(intervals),
                    shownMaxMs=max(intervals), feedbackCount=len(feedback), bufferCount=feedback[0]["total_buffers"],
                    discarded=sum(r["discarded"] for r in feedback), duplicateTimes=len(shown)-len({r["presented_ns"] for r in shown}))
        system=folder/(label+"-system.txt")
        if system.exists():
            for line in system.read_text().splitlines():
                for name in ("VmRSS", "VmHWM", "MemAvailable"):
                    if line.startswith(name+":"): item[name+"MiB"]=int(line.split()[1])/1024
        samples.append(item)
    return samples

def main():
    p=argparse.ArgumentParser();p.add_argument("folders",nargs="+",type=Path);a=p.parse_args()
    for folder in a.folders:
        samples=analyze(folder)
        (folder/"analysis.json").write_text(json.dumps(samples,indent=2),encoding="utf-8")
        for s in samples:
            print(folder.name,s["label"],"first2s=%.1f max=%.1f"%(s["first2sMaxMs"] or 0,s["maxMs"]),
                  "fps=%.2f shown=%.2f long=%.2f%% RSS=%.1f"%(s["fps"],s.get("shownFps",0),s.get("longHoldPercent",0),s.get("VmRSSMiB",0)))

if __name__=="__main__":main()
