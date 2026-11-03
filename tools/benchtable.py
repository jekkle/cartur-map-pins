"""Turns the pilot's results.txt `v22b run` lines into a markdown table.  python tools/benchtable.py results.txt"""
import re, sys

rows = {}
for line in open(sys.argv[1], encoding='utf-8', errors='ignore'):
    m = re.match(r'live: v22b run (\S+) (rest|zoom|pan): (\d+) frames, fps avg ([\d.]+), worst 5% ([\d.]+), GC gen0 \+(\d+), managed heap (-?\d+) KB, drawn (\d+) of (\d+);(.*)', line)
    if not m:
        continue
    stages = {s.group(1): float(s.group(3)) for s in re.finditer(r'\[([^\]]*?) x(\d+) mean ([\d.]+) worst ([\d.]+) ms\]', m.group(10))}
    rows[(m.group(1), m.group(2))] = (float(m.group(4)), float(m.group(5)), int(m.group(6)), int(m.group(9)), stages)

print('| scene | phase | pins | fps avg | fps worst 5% | GC gen0 | pass ms | of it labels | crowding | clusters | fade ms/frame |')
print('|---|---|---|---|---|---|---|---|---|---|---|')
for (scene, phase), (avg, worst, gc, pins, st) in rows.items():
    g = lambda n: st.get(n, 0.0)
    print(f"| {scene} | {phase} | {pins} | {avg:.1f} | {worst:.1f} | {gc} | {g('UpdatePins postfix'):.1f} | {g('LabelCrowding.Apply'):.1f} | {g('Crowding.Measure'):.1f} | {g('Clusters.Measure'):.2f} | {g('PinFade.Step'):.1f} |")
