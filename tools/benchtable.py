"""Turns the pilot's results.txt `v22b run` and `v22b stages` lines into a markdown table.  python tools/benchtable.py results.txt [scene-prefix]"""
import re, sys

prefix = sys.argv[2] if len(sys.argv) > 2 else ''
runs, stages = {}, {}
for line in open(sys.argv[1], encoding='utf-8', errors='ignore'):
    m = re.match(r'live: v22b run (\S+) (rest|zoom|pan): (\d+) frames, fps avg ([\d.]+), worst 5% ([\d.]+), GC gen0 \+(\d+), managed heap (-?\d+) KB, drawn (\d+) of (\d+)', line)
    if m:
        runs[(m.group(1), m.group(2))] = (float(m.group(4)), float(m.group(5)), int(m.group(6)), int(m.group(9)))
        continue
    m = re.match(r'live: v22b stages (\S+) (rest|zoom|pan): \[(.*?) x(\d+) mean ([\d.]+) worst', line)
    if m:
        stages.setdefault((m.group(1), m.group(2)), {})[m.group(3)] = float(m.group(5))

print('| scene | phase | pins | fps avg | fps worst 5% | GC gen0 | pass ms | labels | crowding | clusters | fade ms/frame |')
print('|---|---|---|---|---|---|---|---|---|---|---|')
for (scene, phase), (avg, worst, gc, pins) in runs.items():
    if not scene.startswith(prefix):
        continue
    st = stages.get((scene, phase), {})
    g = lambda n: st.get(n, 0.0)
    print(f"| {scene} | {phase} | {pins} | {avg:.1f} | {worst:.1f} | {gc} | {g('UpdatePins postfix'):.1f} | {g('LabelCrowding.Apply'):.1f} | {g('Crowding.Measure'):.1f} | {g('Clusters.Measure'):.2f} | {g('PinFade.Step'):.1f} |")
