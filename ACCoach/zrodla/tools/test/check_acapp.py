# Test of the in-game app (src/acapp.py) outside Assetto Corsa: a fake "ac" module, a recorded lap played back
# at 60 frames per second, checks of what the app shows and (with Pillow) a picture of a few frames.
#   python3 check_acapp.py <app folder with data\> <lap.csv> [preview.png]
# The tips come from the .live.txt the trainer wrote; a result is written here after turn 1 of the second lap,
# as the trainer does after every turn.
import csv
import importlib.util
import os
import sys
import types

app_dir, lap_csv = sys.argv[1], sys.argv[2]
png = sys.argv[3] if len(sys.argv) > 3 else None
data_dir = os.path.join(app_dir, "data")
data = [f for f in os.listdir(data_dir) if f.endswith(".txt") and f != "settings.txt" and not f.endswith(".live.txt")][0]
combo = data[:-4]
car, track = combo.split("__")[:2]


class FakeAc(object):
    def __init__(self):
        self.labels, self.state, self.logs = [], {}, []

    def __getattr__(self, name):           # every other ac call: accepted and ignored
        return lambda *a: None

    def newApp(self, name): return 1
    def log(self, s): self.logs.append(s)
    def getCarName(self, i): return car
    def getTrackName(self, i): return track
    def getTrackConfiguration(self, i): return ""

    def addLabel(self, app, text):
        self.labels.append({"text": text, "x": 0, "y": 0, "size": 12, "align": "left", "col": (1, 1, 1, 1), "vis": 1})
        return len(self.labels) - 1

    def setText(self, i, t): self.labels[i]["text"] = t
    def setPosition(self, i, x, y): self.labels[i]["x"], self.labels[i]["y"] = x, y
    def setFontSize(self, i, s): self.labels[i]["size"] = s
    def setFontAlignment(self, i, a): self.labels[i]["align"] = a
    def setFontColor(self, i, r, g, b, a): self.labels[i]["col"] = (r, g, b, a)
    def setVisible(self, i, v): self.labels[i]["vis"] = v
    def getCarState(self, car_id, what): return self.state[what]


ac = FakeAc()
cs = types.SimpleNamespace(NormalizedSplinePosition="pos", SpeedKMH="speed", Gas="gas", Brake="brake", LapTime="t", LapCount="laps")
sys.modules["ac"] = ac
sys.modules["acsys"] = types.SimpleNamespace(CS=cs)
spec = importlib.util.spec_from_file_location("ACCoach", os.path.join(app_dir, "ACCoach.py"))
m = importlib.util.module_from_spec(spec)
spec.loader.exec_module(m)
assert m.acMain(1) == "AC Coach"
assert m.coach is not None, "no data loaded: " + str(ac.logs)

rows = []
with open(lap_csv) as f:
    next(f)
    for r in csv.DictReader(f):
        rows.append((int(r["t_ms"]), float(r["pos"]), float(r["speed"]), float(r["gas"]), float(r["brake"])))

live_file = os.path.join(data_dir, combo + ".live.txt")
live_lines = open(live_file, encoding="utf-8").read().splitlines() if os.path.exists(live_file) else ["seq 0"]
tips = [l for l in live_lines if l.startswith("tip ")]
trainer_tips = len(tips)
if not tips:      # the trainer had nothing to fix: one tip per turn, to see them shown
    tips = ["tip %d test tip %d" % (t[0], t[0]) for t in m.coach.d.turns]
seq = max([int(l.split()[1]) for l in live_lines if l.startswith("seq ")] + [0])


def write_live(result):
    global seq
    seq += 1
    lines = ["accoach_live 1", "seq %d" % seq] + (["result %d %s" % (seq, result)] if result else []) + tips
    with open(live_file, "w", encoding="utf-8") as f:
        f.write("\n".join(lines) + "\n")
    m.mtimes.pop(live_file, None)          # the app looks every 0.25 s; the test does not wait for the clock
    m.load_live()


write_live(None)
frames, fails = [], []


def frame(t_ms, laps):
    lo, hi = 0, len(rows) - 1
    while lo < hi:
        mid = (lo + hi) // 2
        if rows[mid][0] < t_ms: lo = mid + 1
        else: hi = mid
    t, pos, spd, gas, brk = rows[lo]
    ac.state.update({"pos": pos, "speed": spd, "gas": gas, "brake": brk, "t": t_ms, "laps": laps})
    m.acUpdate(1 / 60.0)
    lb = {k: dict(ac.labels[v]) for k, v in m.ui.items()}
    return {"t": t_ms, "d": pos * m.coach.d.length, "brake": brk, "speed": spd, "labels": lb}


# lap 0 (no delta: did not start at the line), then the same lap again as lap 1 (delta shown)
end = rows[-1][0]
t1 = m.coach.d.turns[0]
result_at = None
for laps in (0, 1):
    t = 0
    while t < end:
        f = frame(t, laps)
        if laps == 1 and result_at is None and t1[2] + 60 < f["d"] < t1[2] + 400:
            result_at = len(frames)
            write_live("%d bad T%d  +0.25 s|vs best run: braking 12 m earlier, apex -4 km/h" % (t1[0], t1[0]))
            f = frame(t, laps)
        frames.append(f)
        t += 1000 // 60

txt = lambda f, k: f["labels"][k]["text"]
counts = [int(txt(f, "count").split()[0]) for f in frames if txt(f, "count").endswith(" m")]
shown_tips = [txt(f, "text") for f in frames if txt(f, "text")[:1] in ("T", "Z") and ":" in txt(f, "text")]
heads = [i for i, f in enumerate(frames) if txt(f, "big").startswith("T%d" % t1[0])]
deltas = [txt(f, "delta") for f in frames if txt(f, "delta")]
lap0 = [f for f in frames[:len(frames) // 2] if txt(f, "delta")]


def check(ok, what):
    print(("   OK: " if ok else "   FAIL: ") + what)
    if not ok: fails.append(what)


check(len(counts) > 50 and max(counts) >= 100, "countdown to the braking point (max %s m)" % (max(counts) if counts else "-"))
braking = [f for f in frames if f["brake"] > 0.3 and txt(f, "count")]
check(len(braking) < 20, "the countdown goes away once the brake is on (%d frames braking with it)" % len(braking))
check(len(shown_tips) > 30 and all(len(l) <= m.LINE for s in shown_tips for l in s.split("\n")),
      "tip before the turn (%d from the trainer): %s" % (trainer_tips, shown_tips[0].replace("\n", " / ") if shown_tips else "-"))
secs = (heads[-1] - heads[0] + 1) / 60.0 if heads else 0
check(bool(heads) and heads[0] == result_at and 1.0 <= secs <= m.RESULT_S + 0.1,
      "result after the turn, %.1f s: %s / %s" % (secs, txt(frames[heads[0]], "big") if heads else "-", txt(frames[heads[0]], "text").replace("\n", " / ") if heads else "-"))
check(not lap0 and bool(deltas), "delta only after crossing the line (%d frames, last %s)" % (len(deltas), deltas[-1] if deltas else "-"))
check(not ac.logs or all("loaded" in s for s in ac.logs), "no errors in the log: " + "; ".join(ac.logs))

if png:
    from PIL import Image, ImageDraw, ImageFont
    font = lambda s: ImageFont.truetype("/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf", int(s * 0.8))
    lap1 = frames[len(frames) // 2:]

    def pick(cond):
        for f in lap1:
            if cond(f): return f

    num = lambda f: int(txt(f, "count").split()[0]) if txt(f, "count").endswith(" m") else None
    shots = [pick(lambda f: txt(f, "text")[:1] in ("T", "Z") and not txt(f, "count")),
             pick(lambda f: num(f) is not None and 60 <= num(f) <= 120),
             pick(lambda f: num(f) is not None and num(f) < 0),
             frames[heads[0] + 30] if heads else None]
    shots = [s for s in shots if s]
    hgt = 150
    img = Image.new("RGB", (len(shots) * (m.W + 20), hgt + 30), (60, 70, 60))
    dr = ImageDraw.Draw(img)
    for n, f in enumerate(shots):
        ox, oy = n * (m.W + 20) + 10, 10
        dr.rectangle([ox, oy, ox + m.W, oy + hgt - 20], fill=(30, 30, 30))
        for k, lb in f["labels"].items():
            if not lb["text"] or not lb["vis"]: continue
            fn = font(lb["size"])
            for i, line in enumerate(lb["text"].split("\n")):
                w = dr.textlength(line, font=fn)
                x = lb["x"] - (w / 2 if lb["align"] == "center" else w if lb["align"] == "right" else 0)
                dr.text((ox + x, oy + lb["y"] + i * lb["size"] * 1.1), line, font=fn, fill=tuple(int(255 * v) for v in lb["col"][:3]))
        dr.text((ox, hgt - 2), "%.0f m, %.0f km/h, brake %.0f%%" % (f["d"], f["speed"], f["brake"] * 100), fill=(220, 220, 220))
    img.save(png)
    print("   preview: " + png)

sys.exit(1 if fails else 0)
